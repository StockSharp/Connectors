namespace StockSharp.StandX.Native;

sealed class StandXMarketWebSocketClient : BaseLogReceiver
{
	private readonly record struct StreamKey(
		StandXChannels Channel, string Symbol);

	private readonly string _endpoint;
	private readonly string _token;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<StreamKey> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authenticationPending;

	public StandXMarketWebSocketClient(string endpoint, string token,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_token = token;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "STANDX_MARKET_WS";

	public bool IsAuthenticated => !_token.IsEmpty();

	public event Func<StandXSymbolPrice, CancellationToken, ValueTask>
		PriceReceived;
	public event Func<StandXOrderBook, long, CancellationToken, ValueTask>
		BookReceived;
	public event Func<StandXPublicTrade, CancellationToken, ValueTask>
		PublicTradeReceived;
	public event Func<StandXOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<StandXPosition, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<StandXWalletBalance, CancellationToken, ValueTask>
		BalanceReceived;
	public event Func<StandXUserTrade, CancellationToken, ValueTask>
		UserTradeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"StandX market WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			if (IsAuthenticated)
				await AuthenticateAsync(client, cancellationToken);
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		FailAuthentication(new InvalidOperationException(
			"StandX market WebSocket disconnected."));
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

	public ValueTask SubscribeAsync(StandXChannels channel, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, symbol, true, cancellationToken);

	public ValueTask UnsubscribeAsync(StandXChannels channel, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, symbol, false, cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
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
		client.Init += static socket => socket.Options.SetRequestHeader(
			"User-Agent", "StockSharp-StandX-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (IsAuthenticated)
				await AuthenticateAsync(client, cancellationToken);
			StreamKey[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var pending = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
		{
			if (_authenticationPending is not null)
				throw new InvalidOperationException(
					"StandX market authentication is already pending.");
			_authenticationPending = pending;
		}
		try
		{
			await SendAsync(client, new()
			{
				Authentication = new()
				{
					Token = _token,
					Streams = [],
				},
			}, cancellationToken);
			await pending.Task.WaitAsync(TimeSpan.FromSeconds(10),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				if (ReferenceEquals(_authenticationPending, pending))
					_authenticationPending = null;
		}
	}

	private async ValueTask ChangeSubscriptionAsync(StandXChannels channel,
		string symbol, bool isSubscribe, CancellationToken cancellationToken)
	{
		ValidateStream(channel, symbol);
		var key = new StreamKey(channel, symbol?.Trim() ?? string.Empty);
		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe
				? _subscriptions.Add(key)
				: _subscriptions.Remove(key);
		if (!changed)
			return;
		var client = _client;
		if (client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(client, key, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(key);
				else
					_subscriptions.Add(key);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		StreamKey key, bool isSubscribe, CancellationToken cancellationToken)
	{
		var stream = new StandXStream
		{
			Channel = key.Channel,
			Symbol = key.Symbol.IsEmpty() ? null : key.Symbol,
		};
		return SendAsync(client, new()
		{
			Subscribe = isSubscribe ? stream : null,
			Unsubscribe = isSubscribe ? null : stream,
		}, cancellationToken);
	}

	private async ValueTask SendAsync(WebSocketClient client,
		StandXMarketSocketRequest request,
		CancellationToken cancellationToken)
	{
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

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<StandXSocketHeader>(payload);
			if (header.Code is int code && code != 0)
				throw new InvalidOperationException(
					$"StandX WebSocket error {code}: {header.Message}");
			if (header.Channel is not StandXChannels channel)
				throw new InvalidDataException(
					"StandX WebSocket message has no supported channel.");
			switch (channel)
			{
				case StandXChannels.Auth:
					ProcessAuthentication(Deserialize<StandXSocketMessage<
						StandXSocketAuthenticationResult>>(payload).Data);
					break;
				case StandXChannels.Price:
					if (PriceReceived is { } priceHandler)
						await priceHandler(Deserialize<StandXSocketMessage<
							StandXSymbolPrice>>(payload).Data, cancellationToken);
					break;
				case StandXChannels.DepthBook:
					if (BookReceived is { } bookHandler)
					{
						var feed = Deserialize<StandXSocketMessage<
							StandXOrderBook>>(payload);
						await bookHandler(feed.Data, feed.Sequence,
							cancellationToken);
					}
					break;
				case StandXChannels.PublicTrade:
					if (PublicTradeReceived is { } publicTradeHandler)
						await publicTradeHandler(Deserialize<StandXSocketMessage<
							StandXPublicTrade>>(payload).Data, cancellationToken);
					break;
				case StandXChannels.Order:
					if (OrderReceived is { } orderHandler)
						await orderHandler(Deserialize<StandXSocketMessage<
							StandXOrder>>(payload).Data, cancellationToken);
					break;
				case StandXChannels.Position:
					if (PositionReceived is { } positionHandler)
						await positionHandler(Deserialize<StandXSocketMessage<
							StandXPosition>>(payload).Data, cancellationToken);
					break;
				case StandXChannels.Balance:
					if (BalanceReceived is { } balanceHandler)
						await balanceHandler(Deserialize<StandXSocketMessage<
							StandXWalletBalance>>(payload).Data, cancellationToken);
					break;
				case StandXChannels.Trade:
					if (UserTradeReceived is { } userTradeHandler)
						await userTradeHandler(Deserialize<StandXSocketMessage<
							StandXUserTrade>>(payload).Data, cancellationToken);
					break;
				default:
					throw new InvalidDataException(
						$"Unsupported StandX WebSocket channel '{channel}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void ProcessAuthentication(StandXSocketAuthenticationResult result)
	{
		if (result is null)
			throw new InvalidDataException(
				"StandX WebSocket returned no authentication result.");
		TaskCompletionSource<bool> pending;
		using (_sync.EnterScope())
			pending = _authenticationPending;
		if (pending is null)
			return;
		if (result.Code == 200)
			pending.TrySetResult(true);
		else
			pending.TrySetException(new InvalidOperationException(
				$"StandX WebSocket authentication failed ({result.Code}): " +
				result.Message));
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"StandX WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"StandX WebSocket returned malformed JSON.", error);
		}
	}

	private static void ValidateStream(StandXChannels channel, string symbol)
	{
		var isPublic = channel is StandXChannels.Price or
			StandXChannels.DepthBook or StandXChannels.PublicTrade;
		var isPrivate = channel is StandXChannels.Order or
			StandXChannels.Position or StandXChannels.Balance or
			StandXChannels.Trade;
		if (!isPublic && !isPrivate)
			throw new ArgumentOutOfRangeException(nameof(channel), channel,
				"Unsupported StandX subscription channel.");
		if (isPublic && symbol.IsEmpty())
			throw new ArgumentNullException(nameof(symbol));
		if (isPrivate && !symbol.IsEmpty())
			throw new ArgumentException(
				"StandX private streams do not accept a symbol.", nameof(symbol));
	}

	private void FailAuthentication(Exception error)
	{
		TaskCompletionSource<bool> pending;
		using (_sync.EnterScope())
		{
			pending = _authenticationPending;
			_authenticationPending = null;
		}
		pending?.TrySetException(error);
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	protected override void DisposeManaged()
	{
		FailAuthentication(new ObjectDisposedException(
			nameof(StandXMarketWebSocketClient)));
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
