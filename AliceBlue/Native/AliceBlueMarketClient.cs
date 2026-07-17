namespace StockSharp.AliceBlue.Native;

sealed class AliceBlueMarketClient : BaseLogReceiver
{
	private const string _url = "wss://ws1.aliceblueonline.com/NorenWS";
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _clientId;
	private readonly string _sessionToken;
	private readonly SynchronizedDictionary<string, bool> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private TaskCompletionSource<bool> _loginCompletion;

	public AliceBlueMarketClient(string clientId, SecureString sessionToken, int reconnectAttempts,
		WorkingTime workingTime)
	{
		var resolvedClientId = clientId.ThrowIfEmpty(nameof(clientId));
		_clientId = resolvedClientId.EndsWith("_API", StringComparison.OrdinalIgnoreCase)
			? resolvedClientId
			: resolvedClientId + "_API";
		var token = sessionToken.ThrowIfEmpty(nameof(sessionToken)).UnSecure();
		if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			token = token[7..].Trim();
		_sessionToken = token.DoubleSha256();

		_client = new(
			_url,
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

	public override string Name => nameof(AliceBlue) + "_" + nameof(AliceBlueMarketClient);

	public event Func<AliceBlueMarketUpdate, CancellationToken, ValueTask> MarketDataReceived;
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
		=> Send(new AliceBlueMarketHeartbeat(), cancellationToken);

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

		await Send(new AliceBlueMarketLoginRequest
		{
			UserId = _clientId,
			AccountId = _clientId,
			SessionToken = _sessionToken,
		}, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var content = message.AsString()?.Trim();
		if (content.IsEmpty())
			return;

		var envelope = JsonConvert.DeserializeObject<AliceBlueSocketEnvelope>(content, _jsonSettings)
			?? throw new InvalidDataException("Alice Blue returned an empty market WebSocket message.");

		switch (envelope.Type?.ToLowerInvariant())
		{
			case "cf":
			{
				var acknowledgement = JsonConvert.DeserializeObject<AliceBlueMarketAcknowledgement>(content, _jsonSettings)
					?? throw new InvalidDataException("Alice Blue returned an invalid market WebSocket acknowledgement.");
				if (!acknowledgement.Key.EqualsIgnoreCase("OK"))
				{
					var error = new InvalidOperationException($"Alice Blue market WebSocket login failed: {acknowledgement.Key.IsEmpty(acknowledgement.Status)}");
					_loginCompletion?.TrySetException(error);
					throw error;
				}

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
				var update = JsonConvert.DeserializeObject<AliceBlueMarketUpdate>(content, _jsonSettings)
					?? throw new InvalidDataException("Alice Blue returned an invalid market-data update.");
				if (!update.Exchange.IsEmpty() && !update.Token.IsEmpty() && MarketDataReceived is { } handler)
					await handler(update, cancellationToken);
				break;
			}

			case "h":
			case "ok":
				break;

			default:
				this.AddVerboseLog("Ignored Alice Blue market WebSocket message type {0}.", envelope.Type);
				break;
		}
	}

	private ValueTask SendSubscription(string instrumentKey, bool isDepth, bool subscribe,
		CancellationToken cancellationToken)
		=> Send(new AliceBlueMarketSubscriptionRequest
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
