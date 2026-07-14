namespace StockSharp.ByBit.Native;

abstract class BaseSocketClient : BaseLogReceiver, IConnection
{
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;

	protected static class Ops
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
		public const string Ping = "ping";
		public const string Auth = "auth";
	}

	protected BaseSocketClient(string url, int reconnectAttempts, WorkingTime workingTime)
	{
		_client = new(
			url.ThrowIfEmpty(nameof(url)),
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);
				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);
				if (Error is { } handler)
					return handler(error, token);
				return default;
			},
			async (c, msg, t) =>
			{
				await OnProcess(msg.AsObject<WebSocketResponse>(), t);
			},
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		_client.PostConnect += OnPostConnect;
	}

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();

		base.DisposeManaged();
	}

	protected virtual ValueTask OnPostConnect(bool reconnect, CancellationToken token)
		=> default;

	protected virtual ValueTask OnProcess(WebSocketResponse response, CancellationToken cancellationToken)
	{
		if (!response.Topic.IsEmpty())
			return OnProcess(response.Topic, response, cancellationToken);
		else
		{
			if (response.Op == Ops.Auth && response.RetCode > 0)
				this.AddErrorLog("auth failed: (code={0}) {1}", response.RetCode, response.RetMsg);

			// pongs and other responses
			return default;
		}
	}

	protected abstract ValueTask OnProcess(string topic, WebSocketResponse response, CancellationToken cancellationToken);

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	protected ValueTask Subscribe(long transId, string channel, CancellationToken cancellationToken)
	{
		var request = new
		{
			op = Ops.Subscribe,
			args = new[]
			{
				channel
			}
		};

		return Send(request, transId, cancellationToken);
	}

	protected ValueTask Unsubscribe(long originTransId, string channel, CancellationToken cancellationToken)
	{
		var request = new
		{
			op = Ops.Unsubscribe,
			args = new[]
			{
				channel
			}
		};

		return Send(request, -originTransId, cancellationToken);
	}

	protected ValueTask Send(object request, long subId, CancellationToken cancellationToken)
		=> _client.SendAsync(request, cancellationToken, subId);

	public ValueTask SendPing(long transactionId, CancellationToken cancellationToken)
		=> Send(new { req_id = transactionId, op = Ops.Ping }, 0, cancellationToken);
}

class PublicSocketClient(string url, int reconnectAttempts, WorkingTime workingTime, ByBitSections section) : BaseSocketClient(url, reconnectAttempts, workingTime)
{
	// to get readable name after obfuscation
	public override string Name => $"{nameof(ByBit)}_{nameof(PublicSocketClient)}_{_section}";

	public event Func<ByBitSections, string, int, WebSocketOrderBookDelta, CancellationToken, ValueTask> OrderBookDelta;
	public event Func<ByBitSections, string, int, WebSocketOrderBookSnapshot, CancellationToken, ValueTask> OrderBookSnapshot;
	public event Func<ByBitSections, string, IEnumerable<WebSocketTrade>, CancellationToken, ValueTask> TradesReceived;
	public event Func<ByBitSections, string, string, IEnumerable<WebSocketKline>, CancellationToken, ValueTask> KlinesReceived;
	public event Func<ByBitSections, WebSocketTicker, CancellationToken, ValueTask> TickerReceived;

	private static class Channels
	{
		public const string Trade = "publicTrade";
		public const string OrderBook = "orderbook";
		public const string Kline = "kline";
		public const string Tickers = "tickers";
	}

	private readonly ByBitSections _section = section;

    protected override async ValueTask OnProcess(string topic, WebSocketResponse response, CancellationToken cancellationToken)
	{
		var parts = topic.SplitByDot();

		T getData<T>()
			=> response.Data.DeserializeObject<T>();

		switch (parts[0])
		{
			case Channels.OrderBook:
			{
				if (response.Type == "snapshot")
				{
					if (OrderBookSnapshot is { } handler)
						await handler(_section, parts[2], parts[1].To<int>(), getData<WebSocketOrderBookSnapshot>(), cancellationToken);
				}
				else if (response.Type == "delta")
				{
					if (OrderBookDelta is { } handler)
						await handler(_section, parts[2], parts[1].To<int>(), getData<WebSocketOrderBookDelta>(), cancellationToken);
				}
				else
					this.AddWarningLog(LocalizedStrings.UnknownEvent, response.Type);

				break;
			}
			case Channels.Trade:
				if (TradesReceived is { } tradesHandler)
					await tradesHandler(_section, parts[1], getData<IEnumerable<WebSocketTrade>>(), cancellationToken);
				break;
			case Channels.Tickers:
				if (TickerReceived is { } tickerHandler)
					await tickerHandler(_section, getData<WebSocketTicker>(), cancellationToken);
				break;
			case Channels.Kline:
				if (KlinesReceived is { } klinesHandler)
					await klinesHandler(_section, parts[2], parts[1], getData<IEnumerable<WebSocketKline>>(), cancellationToken);
				break;
			default:
				this.AddWarningLog(LocalizedStrings.UnknownEvent, topic);
				break;
		}
	}

	private static string CreateChannel(string channel, string symbol)
		=> $"{channel}.{symbol}";

	public ValueTask SubscribeTrades(long transId, string symbol, CancellationToken cancellationToken) =>
		Subscribe(transId, CreateChannel(Channels.Trade, symbol), cancellationToken);

	public ValueTask UnsubscribeTrades(long originTransId, string symbol, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, CreateChannel(Channels.Trade, symbol), cancellationToken);

	public ValueTask SubscribeOrderBook(long transId, string symbol, int depth, CancellationToken cancellationToken) =>
		Subscribe(transId, CreateChannel($"{Channels.OrderBook}.{depth}", symbol), cancellationToken);

	public ValueTask UnsubscribeOrderBook(long originTransId, string symbol, int depth, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, CreateChannel($"{Channels.OrderBook}.{depth}", symbol), cancellationToken);

	public ValueTask SubscribeTicker(long transId, string symbol, CancellationToken cancellationToken) =>
		Subscribe(transId, CreateChannel(Channels.Tickers, symbol), cancellationToken);

	public ValueTask UnsubscribeTicker(long originTransId, string symbol, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, CreateChannel(Channels.Tickers, symbol), cancellationToken);

	public ValueTask SubscribeKlines(long transId, string symbol, string interval, CancellationToken cancellationToken) =>
		Subscribe(transId, CreateChannel($"{Channels.Kline}.{interval}", symbol), cancellationToken);

	public ValueTask UnsubscribeKlines(long originTransId, string symbol, string interval, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, CreateChannel($"{Channels.Kline}.{interval}", symbol), cancellationToken);
}

abstract class AuthSocketClient(string url, int reconnectAttempts, WorkingTime workingTime, Authenticator authenticator, int recvWindow) : BaseSocketClient(url, reconnectAttempts, workingTime)
{
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	private readonly int _recvWindow = recvWindow;

    protected override ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var expires = (long)DateTime.UtcNow.AddMilliseconds(_recvWindow).ToUnix(false);
		var signature = _authenticator.Sign($"GET/realtime{expires}");

		var request = new
		{
			op = Ops.Auth,
			args = new object[]
			{
				_authenticator.Key.UnSecure(),
				expires,
				signature
			}
		};

		return Send(request, 0, cancellationToken);
	}
}

class PrivateSocketClient(string url, int reconnectAttempts, WorkingTime workingTime, Authenticator authenticator, int recvWindow) : AuthSocketClient(url, reconnectAttempts, workingTime, authenticator, recvWindow)
{
	// to get readable name after obfuscation
	public override string Name => nameof(ByBit) + "_" + nameof(PrivateSocketClient);

	private static class Channels
	{
		public const string Position = "position";
		public const string Execution = "execution";
		public const string Order = "order";
		public const string Wallet = "wallet";
	}

	public event Func<IEnumerable<Wallet>, CancellationToken, ValueTask> WalletsReceived;
	public event Func<IEnumerable<Position>, CancellationToken, ValueTask> PositionsReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<WebSocketExecution>, CancellationToken, ValueTask> ExecutionsReceived;

    protected override async ValueTask OnProcess(string topic, WebSocketResponse response, CancellationToken cancellationToken)
	{
		T getData<T>()
			=> response.Data.DeserializeObject<T>();

		switch (topic)
		{
			case Channels.Wallet:
				if (WalletsReceived is { } walletsHandler)
					await walletsHandler(getData<IEnumerable<Wallet>>(), cancellationToken);
				break;
			case Channels.Position:
				if (PositionsReceived is { } positionsHandler)
					await positionsHandler(getData<IEnumerable<Position>>(), cancellationToken);
				break;
			case Channels.Order:
				if (OrdersReceived is { } ordersHandler)
					await ordersHandler(getData<IEnumerable<Order>>(), cancellationToken);
				break;
			case Channels.Execution:
				if (ExecutionsReceived is { } executionsHandler)
					await executionsHandler(getData<IEnumerable<WebSocketExecution>>(), cancellationToken);
				break;
			default:
				this.AddWarningLog(LocalizedStrings.UnknownEvent, topic);
				break;
		}
	}

	public ValueTask SubscribePositions(long transId, CancellationToken cancellationToken) =>
		Subscribe(transId, Channels.Position, cancellationToken);

	public ValueTask UnsubscribePositions(long originTransId, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, Channels.Position, cancellationToken);

	public ValueTask SubscribeWallets(long transId, CancellationToken cancellationToken) =>
		Subscribe(transId, Channels.Wallet, cancellationToken);

	public ValueTask UnsubscribeWallets(long originTransId, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, Channels.Wallet, cancellationToken);

	public ValueTask SubscribeExecutions(long transId, CancellationToken cancellationToken) =>
		Subscribe(transId, Channels.Execution, cancellationToken);

	public ValueTask UnsubscribeExecutions(long originTransId, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, Channels.Execution, cancellationToken);

	public ValueTask SubscribeOrders(long transId, CancellationToken cancellationToken) =>
		Subscribe(transId, Channels.Order, cancellationToken);

	public ValueTask UnsubscribeOrders(long originTransId, CancellationToken cancellationToken) =>
		Unsubscribe(originTransId, Channels.Order, cancellationToken);
}