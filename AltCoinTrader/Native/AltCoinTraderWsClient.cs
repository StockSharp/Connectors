namespace StockSharp.AltCoinTrader.Native;

sealed class AltCoinTraderWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		string Channel,
		string Market,
		int Depth);

	private readonly string _endpoint;
	private readonly bool _isPrivate;
	private readonly AltCoinTraderAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public AltCoinTraderWsClient(
		string endpoint,
		bool isPrivate,
		SecureString key,
		SecureString secret,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = AltCoinTraderWsProtocol.CreateEndpoint(
			endpoint, isPrivate);
		_isPrivate = isPrivate;
		_authenticator = new(key, secret);
		if (_isPrivate && !_authenticator.IsAvailable)
			throw new InvalidOperationException(
				"AltCoinTrader private WebSocket requires " +
					"an API key and secret.");
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => _isPrivate
		? "ALTCOINTRADER_PRIVATE_WS"
		: "ALTCOINTRADER_PUBLIC_WS";

	public event Func<AltCoinTraderTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<AltCoinTraderOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<AltCoinTraderTrade[],
		CancellationToken, ValueTask> TradesReceived;
	public event Func<AltCoinTraderOrder,
		CancellationToken, ValueTask> OrderReceived;
	public event Func<AltCoinTraderUserTrade,
		CancellationToken, ValueTask> FillReceived;
	public event Func<AltCoinTraderBalance[],
		CancellationToken, ValueTask> BalancesReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"AltCoinTrader WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
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
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
			using (_sync.EnterScope())
				_serverSubscriptions.Clear();
		}
	}

	public ValueTask SubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("ticker", NormalizeSymbol(symbol), 0),
			true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("ticker", NormalizeSymbol(symbol), 0),
			false,
			cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				"orderbook",
				NormalizeSymbol(symbol),
				AltCoinTraderRestClient.NormalizeDepth(depth)),
			true,
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				"orderbook",
				NormalizeSymbol(symbol),
				AltCoinTraderRestClient.NormalizeDepth(depth)),
			false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("trades", NormalizeSymbol(symbol), 0),
			true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("trades", NormalizeSymbol(symbol), 0),
			false,
			cancellationToken);

	public ValueTask SubscribePrivateAsync(
		string channel,
		CancellationToken cancellationToken)
	{
		if (!_isPrivate)
			throw new InvalidOperationException(
				"Private subscriptions require the private WebSocket.");
		channel = NormalizePrivateChannel(channel);
		return ChangeSubscriptionAsync(
			new(channel, null, 0),
			true,
			cancellationToken);
	}

	public ValueTask UnsubscribePrivateAsync(
		string channel,
		CancellationToken cancellationToken)
	{
		if (!_isPrivate)
			throw new InvalidOperationException(
				"Private subscriptions require the private WebSocket.");
		channel = NormalizePrivateChannel(channel);
		return ChangeSubscriptionAsync(
			new(channel, null, 0),
			false,
			cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) =>
				OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) =>
				OnProcessAsync(message, token),
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
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-AltCoinTrader-Connector/1.0");
			if (_isPrivate)
			{
				var timestamp = DateTime.UtcNow
					.ToAltCoinTraderSeconds();
				socket.Options.SetRequestHeader(
					"X-API-KEY",
					_authenticator.Key);
				socket.Options.SetRequestHeader(
					"X-TIMESTAMP",
					timestamp.ToString(
						CultureInfo.InvariantCulture));
				socket.Options.SetRequestHeader(
					"X-SIGNATURE",
					_authenticator.Sign(
						timestamp,
						"GET",
						"/ws/private",
						null));
			}
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(
		WebSocketClient client,
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				subscriptions = [.. _desiredSubscriptions];
				_serverSubscriptions.AddRange(subscriptions);
			}
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(
					client,
					subscription,
					true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		Subscription subscription,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = _client;
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = client?.IsConnected == true &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = client?.IsConnected == true &&
					_serverSubscriptions.Remove(subscription);
			}
		}
		if (!send)
			return;

		try
		{
			await SendSubscriptionAsync(
				client,
				subscription,
				isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverSubscriptions.Remove(subscription);
				else
					_serverSubscriptions.Add(subscription);
			}
			throw;
		}
	}

	private async ValueTask SendSubscriptionAsync(
		WebSocketClient client,
		Subscription subscription,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"AltCoinTrader WebSocket is disconnected.");
		var payload = AltCoinTraderWsProtocol.CreateSubscription(
			subscription.Channel,
			subscription.Market,
			subscription.Depth > 0
				? subscription.Depth
				: null,
			isSubscribe);

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

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var frame =
				AltCoinTraderWsProtocol.DeserializeFrame(payload);
			switch (frame.Channel.Trim().ToLowerInvariant())
			{
				case "subscribed":
				return;
				case "unsubscribed":
					return;
				case "error":
					throw new InvalidDataException(
						"AltCoinTrader WebSocket error: " +
							frame.Message);
				case "ticker":
				await ProcessTickerAsync(
					frame, cancellationToken);
				return;
				case "orderbook":
					await ProcessOrderBookAsync(
						frame, cancellationToken);
				return;
				case "trades":
					await ProcessTradesAsync(
						frame, cancellationToken);
				return;
				case "orders":
					await ProcessOrderAsync(
						frame, cancellationToken);
				return;
				case "fills":
					await ProcessFillAsync(
						frame, cancellationToken);
				return;
				case "balances":
					await ProcessBalancesAsync(
						frame, cancellationToken);
				return;
				default:
					throw new InvalidDataException(
						"Unknown AltCoinTrader WebSocket channel " +
							$"'{frame.Channel}'.");
			}
		}
		catch (Exception error) when (
			error is JsonException or
			InvalidDataException or
			InvalidOperationException or
			FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessTickerAsync(
		AltCoinTraderWsFrame frame,
		CancellationToken cancellationToken)
	{
		var ticker = frame.Data?.ToObject<
			AltCoinTraderTicker>();
		if (ticker is null)
			throw new InvalidDataException(
				"AltCoinTrader ticker frame has no data.");
		ticker.Symbol = ticker.Symbol.IsEmpty(frame.Market);
		if (TickerReceived is { } handler)
			await handler.InvokeAsync(ticker, cancellationToken);
	}

	private async ValueTask ProcessOrderBookAsync(
		AltCoinTraderWsFrame frame,
		CancellationToken cancellationToken)
	{
		var book = frame.Data?.ToObject<
			AltCoinTraderOrderBook>();
		if (book is null)
			throw new InvalidDataException(
				"AltCoinTrader order-book frame has no data.");
		book.Symbol = book.Symbol.IsEmpty(frame.Market);
		if (OrderBookReceived is { } handler)
			await handler.InvokeAsync(book, cancellationToken);
	}

	private async ValueTask ProcessTradesAsync(
		AltCoinTraderWsFrame frame,
		CancellationToken cancellationToken)
	{
		var trades = frame.Data?.Type == JTokenType.Array
			? frame.Data.ToObject<AltCoinTraderTrade[]>()
			: frame.Data is null
				? null
				: [frame.Data.ToObject<AltCoinTraderTrade>()];
		trades = [.. (trades ?? [])
			.Where(static trade => trade is not null)];
		foreach (var trade in trades)
			trade.Market = trade.Market.IsEmpty(frame.Market);
		if (trades.Length > 0 && TradesReceived is { } handler)
			await handler.InvokeAsync(trades, cancellationToken);
	}

	private async ValueTask ProcessOrderAsync(
		AltCoinTraderWsFrame frame,
		CancellationToken cancellationToken)
	{
		var order = frame.Data?.ToObject<AltCoinTraderOrder>();
		if (order is null)
			throw new InvalidDataException(
				"AltCoinTrader order frame has no data.");
		if (OrderReceived is { } handler)
			await handler.InvokeAsync(order, cancellationToken);
	}

	private async ValueTask ProcessFillAsync(
		AltCoinTraderWsFrame frame,
		CancellationToken cancellationToken)
	{
		var fill = frame.Data?.ToObject<
			AltCoinTraderUserTrade>();
		if (fill is null)
			throw new InvalidDataException(
				"AltCoinTrader fill frame has no data.");
		if (FillReceived is { } handler)
			await handler.InvokeAsync(fill, cancellationToken);
	}

	private async ValueTask ProcessBalancesAsync(
		AltCoinTraderWsFrame frame,
		CancellationToken cancellationToken)
	{
		var balances = frame.Data?.ToObject<
			AltCoinTraderBalance[]>();
		if (balances is null)
			throw new InvalidDataException(
				"AltCoinTrader balances frame has no data.");
		if (BalancesReceived is { } handler)
			await handler.InvokeAsync(balances, cancellationToken);
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToAltCoinTraderSymbol();

	private static string NormalizePrivateChannel(string channel)
		=> channel?.Trim().ToLowerInvariant() switch
		{
			"orders" => "orders",
			"fills" => "fills",
			"balances" => "balances",
			_ => throw new ArgumentOutOfRangeException(
				nameof(channel),
				channel,
				"Unsupported AltCoinTrader private channel."),
		};
}
