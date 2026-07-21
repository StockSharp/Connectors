namespace StockSharp.Kalshi.Native;

sealed class KalshiSocketClient : BaseLogReceiver
{
	private sealed class Subscription
	{
		public string Key { get; init; }
		public KalshiSocketChannels Channel { get; init; }
		public string Ticker { get; init; }
		public int ReferenceCount { get; set; }
		public long CommandId { get; set; }
		public long SubscriptionId { get; set; }
		public bool IsCancelPending { get; set; }
	}

	private readonly string _endpoint;
	private readonly KalshiAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Subscription> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Subscription> _commands = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _nextCommandId;
	private bool _isPrivateInitialized;

	public KalshiSocketClient(string endpoint,
		KalshiAuthenticator authenticator, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.NormalizeSocketEndpoint(nameof(endpoint));
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Kalshi_WS";

	public event Func<KalshiSocketEvent, CancellationToken, ValueTask>
		EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_client is not null)
			throw new InvalidOperationException(
				"Kalshi WebSocket is already initialized.");
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += socket =>
			_authenticator.AddSocketHeaders(socket.Options);
		_client = client;
		return default;
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		using (_sync.EnterScope())
		{
			_subscriptions.Clear();
			_commands.Clear();
			_isPrivateInitialized = false;
		}
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	public async ValueTask SubscribeAsync(KalshiSocketChannels channel,
		string ticker, CancellationToken cancellationToken)
	{
		ticker = ticker.ThrowIfEmpty(nameof(ticker)).Trim();
		var key = CreateKey(channel, ticker);
		Subscription subscription;
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(key, out subscription))
			{
				subscription.ReferenceCount++;
				return;
			}
			subscription = new()
			{
				Key = key,
				Channel = channel,
				Ticker = ticker,
				ReferenceCount = 1,
			};
			_subscriptions.Add(key, subscription);
		}
		try
		{
			await EnsureConnectedAsync(cancellationToken);
			await SendSubscribeAsync(subscription, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(key);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(KalshiSocketChannels channel,
		string ticker, CancellationToken cancellationToken)
	{
		ticker = ticker.ThrowIfEmpty(nameof(ticker)).Trim();
		Subscription subscription;
		long subscriptionId;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(CreateKey(channel, ticker),
				out subscription))
				return;
			if (--subscription.ReferenceCount > 0)
				return;
			_subscriptions.Remove(subscription.Key);
			subscription.IsCancelPending = true;
			subscriptionId = subscription.SubscriptionId;
		}
		if (subscriptionId > 0 && _client?.IsConnected == true)
			await SendUnsubscribeAsync(subscriptionId, cancellationToken);
	}

	public async ValueTask EnsureAccountSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_isPrivateInitialized)
				return;
			_isPrivateInitialized = true;
		}
		try
		{
			await EnsureConnectedAsync(cancellationToken);
			foreach (var channel in new[]
			{
				KalshiSocketChannels.Fill,
				KalshiSocketChannels.MarketPositions,
				KalshiSocketChannels.UserOrders,
			})
			{
				var subscription = AddPrivateSubscription(channel);
				if (subscription is not null)
					await SendSubscribeAsync(subscription, cancellationToken);
			}
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_isPrivateInitialized = false;
				foreach (var channel in new[]
				{
					KalshiSocketChannels.Fill,
					KalshiSocketChannels.MarketPositions,
					KalshiSocketChannels.UserOrders,
				})
				{
					if (!_subscriptions.Remove(CreateKey(channel,
						string.Empty), out var subscription))
						continue;
					if (subscription.CommandId > 0)
						_commands.Remove(subscription.CommandId);
				}
			}
			throw;
		}
	}

	private Subscription AddPrivateSubscription(KalshiSocketChannels channel)
	{
		var key = CreateKey(channel, string.Empty);
		using (_sync.EnterScope())
		{
			if (_subscriptions.ContainsKey(key))
				return null;
			var subscription = new Subscription
			{
				Key = key,
				Channel = channel,
				ReferenceCount = 1,
			};
			_subscriptions.Add(key, subscription);
			return subscription;
		}
	}

	private async ValueTask EnsureConnectedAsync(
		CancellationToken cancellationToken)
	{
		if (!_authenticator.IsAvailable)
			throw new InvalidOperationException(
				"Kalshi API credentials are required for WebSocket streaming.");
		var client = _client ?? throw new InvalidOperationException(
			"Kalshi WebSocket is not initialized.");
		if (!client.IsConnected)
			await client.ConnectAsync(cancellationToken);
	}

	private async ValueTask SendSubscribeAsync(Subscription subscription,
		CancellationToken cancellationToken)
	{
		long commandId;
		using (_sync.EnterScope())
		{
			commandId = ++_nextCommandId;
			subscription.CommandId = commandId;
			subscription.SubscriptionId = 0;
			_commands[commandId] = subscription;
		}
		try
		{
			await SendAsync(new KalshiSocketRequest
			{
				Id = commandId,
				Command = KalshiSocketCommands.Subscribe,
				Parameters = new()
				{
					Channels = [subscription.Channel],
					MarketTickers = subscription.Ticker.IsEmpty()
						? null
						: [subscription.Ticker],
					IsUseYesPrice = subscription.Channel ==
						KalshiSocketChannels.OrderBookDelta ? true : null,
				},
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (_commands.TryGetValue(commandId, out var pending) &&
					ReferenceEquals(pending, subscription))
					_commands.Remove(commandId);
				if (subscription.CommandId == commandId)
					subscription.CommandId = 0;
			}
			throw;
		}
	}

	private ValueTask SendUnsubscribeAsync(long subscriptionId,
		CancellationToken cancellationToken)
		=> SendAsync(new KalshiSocketRequest
		{
			Id = Interlocked.Increment(ref _nextCommandId),
			Command = KalshiSocketCommands.Unsubscribe,
			Parameters = new()
			{
				SubscriptionIds = [subscriptionId],
			},
		}, cancellationToken);

	private async ValueTask SendAsync(KalshiSocketRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Kalshi WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Disconnected)
		{
			using (_sync.EnterScope())
			{
				_commands.Clear();
				foreach (var subscription in _subscriptions.Values)
				{
					subscription.CommandId = 0;
					subscription.SubscriptionId = 0;
				}
			}
		}
		else if (state == ConnectionStates.Restored)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions.Values];
			foreach (var subscription in subscriptions)
				await SendSubscribeAsync(subscription, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var item = JsonConvert.DeserializeObject<KalshiSocketEvent>(payload,
				_settings) ?? throw new InvalidDataException(
					"Kalshi WebSocket returned an empty JSON message.");
			if (item.Type == KalshiSocketEventTypes.Subscribed)
			{
				await OnSubscribedAsync(item, cancellationToken);
				return;
			}
			if (item.Type == KalshiSocketEventTypes.Error)
			{
				OnSubscriptionError(item);
				throw new InvalidOperationException("Kalshi WebSocket error " +
					(item.Message?.ErrorCode?.ToString(
						CultureInfo.InvariantCulture) ?? "unknown") + ": " +
					(item.Message?.ErrorMessage ?? "unknown error"));
			}
			if (item.Type is KalshiSocketEventTypes.Unsubscribed or
				KalshiSocketEventTypes.Ok)
				return;
			if (EventReceived is { } handler)
				await handler(item, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask OnSubscribedAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		if (item.Id is not long commandId ||
			item.Message?.SubscriptionId is not long subscriptionId)
			return;
		var shouldCancel = false;
		using (_sync.EnterScope())
		{
			if (!_commands.Remove(commandId, out var subscription))
				return;
			subscription.SubscriptionId = subscriptionId;
			shouldCancel = subscription.IsCancelPending;
		}
		if (shouldCancel)
			await SendUnsubscribeAsync(subscriptionId, cancellationToken);
	}

	private void OnSubscriptionError(KalshiSocketEvent item)
	{
		if (item.Id is not long commandId)
			return;
		using (_sync.EnterScope())
		{
			if (!_commands.Remove(commandId, out var subscription))
				return;
			if (_subscriptions.TryGetValue(subscription.Key, out var current) &&
				ReferenceEquals(current, subscription))
				_subscriptions.Remove(subscription.Key);
			if (subscription.Ticker.IsEmpty())
				_isPrivateInitialized = false;
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string CreateKey(KalshiSocketChannels channel,
		string ticker)
		=> ((int)channel).ToString(CultureInfo.InvariantCulture) + "|" +
			(ticker ?? string.Empty);

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
