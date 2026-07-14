namespace StockSharp.Coincheck.Native;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Coincheck) + "_" + nameof(PusherClient);

	public event Func<Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	//public event Action<string> TradesSubscribed;
	//public event Action<string> OrderBooksSubscribed;

	private readonly WebSocketClient _client;

	public PusherClient(WorkingTime workingTime)
	{
		_client = new(
			"wss://ws-api.coincheck.com/",
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
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

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

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var arr = msg.AsObject<JArray>();

		if (arr.Count == 2)
		{
			if (OrderBookChanged is { } handler)
				await handler((string)arr[0], arr[1].DeserializeObject<OrderBook>(), cancellationToken);
		}
		else
		{
			if (NewTrade is { } handler)
				await handler(arr.DeserializeObject<Trade>(), cancellationToken);
		}
	}

	private static class Channels
	{
		public const string Trades = "trades";
		public const string OrderBook = "orderbook";
	}

	public ValueTask SubscribeTradesAsync(string currency, CancellationToken cancellationToken)
		=> ProcessAsync("subscribe", currency + "-" + Channels.Trades, cancellationToken);

	public ValueTask UnSubscribeTradesAsync(string currency, CancellationToken cancellationToken)
		=> ProcessAsync("unsubscribe", currency + "-" + Channels.Trades, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string currency, CancellationToken cancellationToken)
		=> ProcessAsync("subscribe", currency + "-" + Channels.OrderBook, cancellationToken);

	public ValueTask UnSubscribeOrderBookAsync(string currency, CancellationToken cancellationToken)
		=> ProcessAsync("unsubscribe", currency + "-" + Channels.OrderBook, cancellationToken);

	private ValueTask ProcessAsync(string type, string channel, CancellationToken cancellationToken)
	{
		if (type.IsEmpty())
			throw new ArgumentNullException(nameof(type));

		if (channel.IsEmpty())
			throw new ArgumentNullException(nameof(channel));

		return _client.SendAsync(new
		{
			type,
			channel,
		}, cancellationToken);
	}
}