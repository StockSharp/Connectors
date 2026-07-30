namespace StockSharp.Noren.Native;

sealed class NorenSocketClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _userId;
	private readonly string _accountId;
	private readonly string _sessionToken;
	private readonly bool _subscribeOrders;
	private readonly bool _useBearerAuthentication;
	private readonly SynchronizedDictionary<string, bool> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private TaskCompletionSource<bool> _loginCompletion;

	public NorenSocketClient(string userId, string accountId, SecureString sessionToken, bool subscribeOrders,
		int reconnectAttempts, WorkingTime workingTime, string webSocketEndpoint,
		bool useBearerAuthentication = false)
	{
		_userId = userId.ThrowIfEmpty(nameof(userId));
		_accountId = accountId.ThrowIfEmpty(nameof(accountId));
		_sessionToken = sessionToken.ThrowIfEmpty(nameof(sessionToken)).UnSecure();
		_subscribeOrders = subscribeOrders;
		_useBearerAuthentication = useBearerAuthentication;

		_client = new(
			webSocketEndpoint.ThrowIfEmpty(nameof(webSocketEndpoint)),
			(state, cancellationToken) => StateChanged is { } stateHandler ? stateHandler(state, cancellationToken) : default,
			(error, cancellationToken) => Error is { } errorHandler ? errorHandler(error, cancellationToken) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Noren) + "_" + nameof(NorenSocketClient);

	public event Func<NorenMarketUpdate, CancellationToken, ValueTask> MarketDataReceived;
	public event Func<NorenOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		_loginCompletion = CreateCompletion();
		await _client.ConnectAsync(cancellationToken);
		await _loginCompletion.Task.WaitAsync(cancellationToken);
	}

	public ValueTask Disconnect(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> Send(new NorenSocketHeartbeat(), cancellationToken);

	public async ValueTask Subscribe(string instrumentKey, bool isDepth, CancellationToken cancellationToken)
	{
		instrumentKey.ParseInstrumentKey();
		if (_subscriptions.TryGetValue(instrumentKey, out var previous))
		{
			if (previous == isDepth)
				return;
			await SendSubscription(instrumentKey, previous, false, cancellationToken);
		}

		_subscriptions[instrumentKey] = isDepth;
		await SendSubscription(instrumentKey, isDepth, true, cancellationToken);
	}

	public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryGetAndRemove(instrumentKey, out var isDepth))
			return;
		await SendSubscription(instrumentKey, isDepth, false, cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		if (reconnect || _loginCompletion == null || _loginCompletion.Task.IsCompleted)
			_loginCompletion = CreateCompletion();

		await _client.SendAsync(CreateLoginPayload(), cancellationToken);
	}

	internal string CreateLoginPayload()
		=> JsonConvert.SerializeObject(
			_useBearerAuthentication
				? new NorenSocketOAuthLoginRequest
				{
					UserId = _userId,
					AccountId = _accountId,
					AccessToken = _sessionToken,
				}
				: new NorenSocketLoginRequest
				{
					UserId = _userId,
					AccountId = _accountId,
					SessionToken = _sessionToken,
				},
			Formatting.None,
			_jsonSettings);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty())
			return;

		var envelope = JsonConvert.DeserializeObject<NorenSocketEnvelope>(text, _jsonSettings)
			?? throw new InvalidDataException("Noren returned an empty WebSocket message.");

		switch (envelope.Type?.ToLowerInvariant())
		{
			case "ak":
			case "ck":
			{
				var acknowledgement = JsonConvert.DeserializeObject<NorenSocketAcknowledgement>(text, _jsonSettings)
					?? throw new InvalidDataException("Noren returned an invalid connection acknowledgement.");
				if (!acknowledgement.Status.EqualsIgnoreCase("OK"))
				{
					var error = new InvalidOperationException($"Noren WebSocket login failed: {acknowledgement.ErrorMessage.IsEmpty(acknowledgement.Status)}");
					_loginCompletion?.TrySetException(error);
					throw error;
				}

				if (_subscribeOrders)
					await Send(new NorenSocketOrderRequest { AccountId = _accountId }, cancellationToken);
				foreach (var subscription in _subscriptions.ToArray())
					await SendSubscription(subscription.Key, subscription.Value, true, cancellationToken);
				_loginCompletion?.TrySetResult(true);
				break;
			}

			case "tk":
			case "tf":
			case "dk":
			case "df":
			{
				var update = JsonConvert.DeserializeObject<NorenMarketUpdate>(text, _jsonSettings)
					?? throw new InvalidDataException("Noren returned an invalid market-data update.");
				if (!update.Exchange.IsEmpty() && !update.Token.IsEmpty() && MarketDataReceived is { } handler)
					await handler(update, cancellationToken);
				break;
			}

			case "om":
			{
				var order = JsonConvert.DeserializeObject<NorenOrder>(text, _jsonSettings)
					?? throw new InvalidDataException("Noren returned an invalid order update.");
				if (!order.OrderId.IsEmpty() && OrderReceived is { } handler)
					await handler(order, cancellationToken);
				break;
			}

			case "ok":
			case "h":
				break;

			default:
			{
				var acknowledgement = JsonConvert.DeserializeObject<NorenSocketAcknowledgement>(text, _jsonSettings);
				if (acknowledgement != null && !acknowledgement.ErrorMessage.IsEmpty())
					throw new InvalidOperationException($"Noren WebSocket error: {acknowledgement.ErrorMessage}");
				this.AddVerboseLog("Ignored Noren WebSocket message type {0}.", envelope.Type);
				break;
			}
		}
	}

	private ValueTask SendSubscription(string instrumentKey, bool isDepth, bool subscribe,
		CancellationToken cancellationToken)
		=> Send(new NorenSocketSubscriptionRequest
		{
			Type = subscribe ? isDepth ? "d" : "t" : isDepth ? "ud" : "u",
			Instruments = instrumentKey,
		}, cancellationToken);

	private ValueTask Send<T>(T request, CancellationToken cancellationToken)
		where T : class
		=> _client.SendAsync(JsonConvert.SerializeObject(request, Formatting.None, _jsonSettings), cancellationToken);

	private static TaskCompletionSource<bool> CreateCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
