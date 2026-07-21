namespace StockSharp.Polymarket.Native;

sealed class PolymarketSocketClient : BaseLogReceiver
{
	private readonly string _marketEndpoint;
	private readonly string _userEndpoint;
	private readonly PolymarketAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _marketSubscriptions =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _marketClient;
	private WebSocketClient _userClient;
	private bool _isMarketInitialized;
	private bool _isUserSubscribed;

	public PolymarketSocketClient(string marketEndpoint, string userEndpoint,
		PolymarketAuthenticator authenticator, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_marketEndpoint = marketEndpoint.NormalizeSocketEndpoint(
			nameof(marketEndpoint));
		_userEndpoint = userEndpoint.NormalizeSocketEndpoint(nameof(userEndpoint));
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Polymarket_WS";

	public event Func<PolymarketSocketEvent, CancellationToken, ValueTask>
		EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_marketClient is not null || _userClient is not null)
			throw new InvalidOperationException(
				"Polymarket WebSocket is already initialized.");
		_marketClient = CreateClient(_marketEndpoint, false);
		if (_authenticator.IsAvailable)
			_userClient = CreateClient(_userEndpoint, true);
		return default;
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var market = _marketClient;
		var user = _userClient;
		_marketClient = null;
		_userClient = null;
		using (_sync.EnterScope())
		{
			_isMarketInitialized = false;
			_isUserSubscribed = false;
			_marketSubscriptions.Clear();
		}
		await DisposeClientAsync(market, cancellationToken);
		await DisposeClientAsync(user, cancellationToken);
	}

	public async ValueTask SubscribeMarketAsync(string tokenId,
		CancellationToken cancellationToken)
	{
		tokenId = tokenId.ThrowIfEmpty(nameof(tokenId)).Trim();
		bool isInitial;
		string[] assets;
		using (_sync.EnterScope())
		{
			if (!_marketSubscriptions.Add(tokenId))
				return;
			isInitial = !_isMarketInitialized;
			if (isInitial)
				_isMarketInitialized = true;
			assets = isInitial ? [.. _marketSubscriptions] : [tokenId];
		}
		try
		{
			var client = _marketClient ?? throw new InvalidOperationException(
				"Polymarket market WebSocket is not initialized.");
			if (!client.IsConnected)
				await client.ConnectAsync(cancellationToken);
			await SendMarketSubscriptionAsync(assets, isInitial, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_marketSubscriptions.Remove(tokenId);
				if (isInitial)
					_isMarketInitialized = false;
			}
			throw;
		}
	}

	public async ValueTask UnsubscribeMarketAsync(string tokenId,
		CancellationToken cancellationToken)
	{
		tokenId = tokenId.ThrowIfEmpty(nameof(tokenId)).Trim();
		var shouldSend = false;
		using (_sync.EnterScope())
			shouldSend = _marketSubscriptions.Remove(tokenId) &&
				_isMarketInitialized;
		if (shouldSend)
			await SendMarketSubscriptionAsync([tokenId], false, false,
				cancellationToken);
	}

	public async ValueTask EnsureUserSubscriptionAsync(
		CancellationToken cancellationToken)
	{
		if (!_authenticator.IsAvailable || _userClient is null)
			throw new InvalidOperationException(
				"Polymarket API credentials are required for the user stream.");
		using (_sync.EnterScope())
		{
			if (_isUserSubscribed)
				return;
			_isUserSubscribed = true;
		}
		try
		{
			if (!_userClient.IsConnected)
				await _userClient.ConnectAsync(cancellationToken);
			await SendUserSubscriptionAsync(_userClient, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_isUserSubscribed = false;
			throw;
		}
	}

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var market = _marketClient;
		var user = _userClient;
		if (market?.IsConnected == true)
			await SendRawAsync(market, "PING", cancellationToken);
		if (user?.IsConnected == true)
			await SendRawAsync(user, "PING", cancellationToken);
	}

	private WebSocketClient CreateClient(string endpoint, bool isUser)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			endpoint,
			(state, token) => OnStateChangedAsync(client, isUser, state, token),
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
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Polymarket/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		bool isUser, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (isUser)
			{
				bool isSubscribed;
				using (_sync.EnterScope())
					isSubscribed = _isUserSubscribed;
				if (isSubscribed)
					await SendUserSubscriptionAsync(client, cancellationToken);
			}
			else
			{
				string[] assets;
				using (_sync.EnterScope())
				{
					assets = [.. _marketSubscriptions];
					_isMarketInitialized = assets.Length > 0;
				}
				if (assets.Length > 0)
					await SendMarketSubscriptionAsync(client, assets, true, true,
						cancellationToken);
			}
		}
		else if (state == ConnectionStates.Disconnected && !isUser)
		{
			using (_sync.EnterScope())
				_isMarketInitialized = false;
		}
		if (!isUser && StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask SendMarketSubscriptionAsync(string[] assets,
		bool isInitial, bool isSubscribe, CancellationToken cancellationToken)
	{
		var client = _marketClient;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Polymarket market WebSocket is not connected.");
		return SendMarketSubscriptionAsync(client, assets, isInitial,
			isSubscribe, cancellationToken);
	}

	private ValueTask SendMarketSubscriptionAsync(WebSocketClient client,
		string[] assets, bool isInitial, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new PolymarketMarketSocketRequest
		{
			AssetIds = assets,
			Type = isInitial ? PolymarketSocketChannels.Market : null,
			Operation = isInitial ? null : isSubscribe
				? PolymarketSocketOperations.Subscribe
				: PolymarketSocketOperations.Unsubscribe,
			IsCustomFeatureEnabled = isSubscribe ? true : null,
		}, cancellationToken);

	private ValueTask SendUserSubscriptionAsync(WebSocketClient client,
		CancellationToken cancellationToken)
		=> SendAsync(client, new PolymarketUserSocketRequest
		{
			Authentication = _authenticator.CreateSocketAuthentication(),
			Type = PolymarketSocketChannels.User,
		}, cancellationToken);

	private async ValueTask SendAsync<TPayload>(WebSocketClient client,
		TPayload payload, CancellationToken cancellationToken)
	{
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Polymarket WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask SendRawAsync(WebSocketClient client,
		string payload, CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(Encoding.UTF8.GetBytes(payload),
				WebSocketMessageType.Text, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.Equals("PONG",
			StringComparison.OrdinalIgnoreCase))
			return;
		try
		{
			var trimmed = payload.TrimStart();
			PolymarketSocketEvent[] events;
			if (trimmed.StartsWith('['))
				events = JsonConvert.DeserializeObject<PolymarketSocketEvent[]>(
					payload, _jsonSettings) ?? [];
			else if (trimmed.StartsWith('{'))
			{
				var item = JsonConvert.DeserializeObject<PolymarketSocketEvent>(
					payload, _jsonSettings);
				events = item is null ? [] : [item];
			}
			else
				throw new InvalidDataException(
					"Polymarket WebSocket returned a non-JSON message: " + payload);
			foreach (var item in events)
			{
				if (item?.EventType is null)
				{
					var error = item?.Error.IsEmpty() == false
						? item.Error
						: item?.Message;
					if (!error.IsEmpty())
						throw new InvalidOperationException(
							"Polymarket WebSocket error: " + error);
					continue;
				}
				if (EventReceived is { } handler)
					await handler(item, cancellationToken);
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static async ValueTask DisposeClientAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
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

	protected override void DisposeManaged()
	{
		_marketClient?.Dispose();
		_userClient?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
