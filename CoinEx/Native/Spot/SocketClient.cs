namespace StockSharp.CoinEx.Native.Spot;

using Ecng.IO.Compression;

using StockSharp.CoinEx.Native.Spot.Model;

class SocketClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(CoinEx) + "_" + nameof(Spot) + nameof(SocketClient);

	public event Func<IEnumerable<Ticker>, CancellationToken, ValueTask> TickersReceived;
	public event Func<string, bool, OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<string, IEnumerable<Tick>, CancellationToken, ValueTask> TicksReceived;
	public event Func<Best, CancellationToken, ValueTask> BestReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalanceReceived;
	public event Func<Order, string, CancellationToken, ValueTask> OrderReceived;
	public event Func<Deal, CancellationToken, ValueTask> DealReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly Authenticator _authenticator;
	private readonly IdGenerator _transIdGen;
	private DateTime? _nextPing;

	public SocketClient(Authenticator authenticator, int attempts, IdGenerator transIdGen, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_transIdGen = transIdGen ?? throw new ArgumentNullException(nameof(transIdGen));

		_client = new(
			"wss://socket.coinex.com/v2/spot",
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
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = attempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		_client.PreProcess2 += OnPreProcess;

		if (_authenticator.CanSign)
		{
			_client.ResendTimeout = TimeSpan.FromSeconds(5);
			_client.PostConnect += OnPostConnect;
		}
	}

	protected override void DisposeManaged()
	{
		if (_authenticator.CanSign)
			_client.PostConnect -= OnPostConnect;

		_client.PreProcess2 -= OnPreProcess;
		_client.Dispose();

		base.DisposeManaged();
	}

	private int OnPreProcess(ReadOnlyMemory<byte> source, Memory<byte> dest)
		=> source.Span.UnGZip(dest.Span);

	public ValueTask Connect(CancellationToken cancellationToken)
	{
		_nextPing = null;

		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken token)
	{
		var timestamp = (long)DateTime.UtcNow.ToUnix(false);

		return Send(Channel.Server, Commands.Sign, _transIdGen.GetNextId(), 0, new
		{
			access_id = _authenticator.Key.UnSecure(),
			signed_str = _authenticator.Sign(timestamp.ToString()),
			timestamp,
		}, token);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var method = (string)obj.method;
		var data = obj.data;

		if (method.ContainsIgnoreCase(Channel.Ticker))
		{
			if (TickersReceived is { } handler)
				await handler(((JToken)data.state_list).DeserializeObject<IEnumerable<Ticker>>(), cancellationToken);
		}
		else if (method.ContainsIgnoreCase(Channel.Ticks))
		{
			if (TicksReceived is { } handler)
				await handler((string)data.market, ((JToken)data.deal_list).DeserializeObject<IEnumerable<Tick>>(), cancellationToken);
		}
		else if (method.ContainsIgnoreCase(Channel.Depth))
		{
			if (OrderBookReceived is { } handler)
				await handler((string)data.market, (bool)data.is_full, ((JToken)data.depth).DeserializeObject<OrderBook>(), cancellationToken);
		}
		else if (method.ContainsIgnoreCase(Channel.Best))
		{
			if (BestReceived is { } handler)
				await handler(((JToken)data).DeserializeObject<Best>(), cancellationToken);
		}
		else if (method.ContainsIgnoreCase(Channel.Order))
		{
			if (OrderReceived is { } handler)
				await handler(((JToken)data.order).DeserializeObject<Order>(), (string)data.@event, cancellationToken);
		}
		else if (method.ContainsIgnoreCase(Channel.Deals))
		{
			if (DealReceived is { } handler)
				await handler(((JToken)data).DeserializeObject<Deal>(), cancellationToken);
		}
		else if (method.ContainsIgnoreCase(Channel.Balance))
		{
			if (BalanceReceived is { } handler)
				await handler(((JToken)data.balance_list).DeserializeObject<IEnumerable<Balance>>(), cancellationToken);
		}
		else
		{
			if (obj.message == "OK")
				return;

			if (data is null || data.result != "pong")
				this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)obj.ToString());
		}
	}

	private static class Commands
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
		public const string Query = "query";
		public const string Sign = "sign";
		public const string Time = "time";
		public const string Ping = "ping";
	}

	private static class Channel
	{
		public const string Ticker = "state";
		public const string Depth = "depth";
		public const string Ticks = "deals";
		public const string Best = "bbo";

		public const string Balance = "balance";
		public const string Order = "order";
		public const string Server = "server";
		public const string Deals = "user_deals";
	}

	public ValueTask SubscribeTicker(long id, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Ticker, Commands.Subscribe, id, id, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask UnSubscribeTicker(long id, long originTransId, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Ticker, Commands.Unsubscribe, id, -originTransId, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask SubscribeBest(long id, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Best, Commands.Subscribe, id, id, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask UnSubscribeBest(long id, long originTransId, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Best, Commands.Unsubscribe, id, -originTransId, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask SubscribeTicks(long id, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Ticks, Commands.Subscribe, id, id, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask UnSubscribeTicks(long id, long originTransId, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Ticks, Commands.Unsubscribe, id, -originTransId, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask SubscribeOrderBook(long id, string symbol, int depth, CancellationToken cancellationToken)
	{
		return Send(Channel.Depth, Commands.Subscribe, id, id, new { market_list = new[] { new object[] { symbol, depth, "0", false } } }, cancellationToken);
	}

	public ValueTask UnSubscribeOrderBook(long id, long originTransId, string symbol, CancellationToken cancellationToken)
	{
		return Send(Channel.Depth, Commands.Unsubscribe, id, -originTransId, new { market_list = new[] { symbol } }, cancellationToken);
	}

	public ValueTask SubscribeBalance(long id, CancellationToken cancellationToken)
	{
		return Send(Channel.Balance, Commands.Subscribe, id, id, new { ccy_list = Array.Empty<string>() }, cancellationToken);
	}

	public ValueTask UnSubscribeBalance(long id, long originTransId, CancellationToken cancellationToken)
	{
		return Send(Channel.Balance, Commands.Unsubscribe, id, -originTransId, new { ccy_list = Array.Empty<string>() }, cancellationToken);
	}

	public ValueTask SubscribeOrders(long id, CancellationToken cancellationToken)
	{
		return Send(Channel.Order, Commands.Subscribe, id, id, new { market_list = Array.Empty<string>() }, cancellationToken);
	}

	public ValueTask UnSubscribeOrders(long id, long originTransId, CancellationToken cancellationToken)
	{
		return Send(Channel.Order, Commands.Unsubscribe, id, -originTransId, new { market_list = Array.Empty<string>() }, cancellationToken);
	}

	public ValueTask SubscribeDeals(long id, CancellationToken cancellationToken)
	{
		return Send(Channel.Deals, Commands.Subscribe, id, id, new { market_list = Array.Empty<string>() }, cancellationToken);
	}

	public ValueTask UnSubscribeDeals(long id, long originTransId, CancellationToken cancellationToken)
	{
		return Send(Channel.Deals, Commands.Unsubscribe, id, -originTransId, new { market_list = Array.Empty<string>() }, cancellationToken);
	}

	public ValueTask SendPing(long id, CancellationToken cancellationToken)
	{
		if (_nextPing != null && DateTime.UtcNow < _nextPing.Value)
			return default;

		_nextPing = DateTime.UtcNow.AddSeconds(5);
		return Send(Channel.Server, Commands.Ping, id, 0, new { }, cancellationToken);
	}

	private ValueTask Send(string channel, string command, long id, long subId, object @params, CancellationToken cancellationToken)
	{
		if (channel.IsEmpty())
			throw new ArgumentNullException(nameof(channel));

		if (command.IsEmpty())
			throw new ArgumentNullException(nameof(command));

		if (@params == null)
			throw new ArgumentNullException(nameof(@params));

		return _client.SendAsync(new
		{
			method = $"{channel}.{command}",
			@params,
			id
		}, cancellationToken, subId);
	}
}