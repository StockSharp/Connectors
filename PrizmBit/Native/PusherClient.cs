namespace StockSharp.PrizmBit.Native;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(PrizmBit) + "_" + nameof(PusherClient);

	public event Func<DateTime, MarketPrice, CancellationToken, ValueTask> MarketPriceChanged;
	public event Func<DateTime, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<DateTime, SocketTrade, CancellationToken, ValueTask> NewTrade;
	public event Func<DateTime, SocketUserTrade, CancellationToken, ValueTask> NewUserTrade;
	public event Func<DateTime, CanceledOrder, CancellationToken, ValueTask> OrderCanceled;
	public event Func<DateTime, UserCanceledOrder, CancellationToken, ValueTask> UserOrderCanceled;
	public event Func<DateTime, Balance, CancellationToken, ValueTask> BalanceChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly Authenticator _authenticator;

	public PusherClient(string endpoint, Authenticator authenticator, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			endpoint.ThrowIfEmpty(nameof(endpoint)),
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler.InvokeAsync(state, token);
				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);
				if (Error is { } handler)
					return handler.InvokeAsync(error, token);
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

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		return _client.DisconnectAsync(cancellationToken);
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();

		var type = (string)obj.type;
		var timestamp = ((long)obj.timestamp).FromUnix(false);
		var data = (JToken)obj.data;

		switch (type)
		{
			case Channels.MarketPrice:
				await (MarketPriceChanged.InvokeAsync(timestamp, data.DeserializeObject<MarketPrice>(), cancellationToken));
				break;
			case Channels.Trade:
				await (NewTrade.InvokeAsync(timestamp, data.DeserializeObject<SocketTrade>(), cancellationToken));
				break;
			case Channels.UserTrade:
				await (NewUserTrade.InvokeAsync(timestamp, data.DeserializeObject<SocketUserTrade>(), cancellationToken));
				break;
			case Channels.OrderBook:
				await (OrderBookChanged.InvokeAsync(timestamp, data.DeserializeObject<OrderBook>(), cancellationToken));
				break;
			case Channels.CanceledOrder:
				await (OrderCanceled.InvokeAsync(timestamp, data.DeserializeObject<CanceledOrder>(), cancellationToken));
				break;
			case Channels.UserCanceledOrder:
				await (UserOrderCanceled.InvokeAsync(timestamp, data.DeserializeObject<UserCanceledOrder>(), cancellationToken));
				break;
			case Channels.ParaminingUpdate:
				//MarketPriceChanged?.Invoke(timestamp, data.DeserializeObject<MarketPrice>());
				break;
			case Channels.TradingBalance:
				await (BalanceChanged.InvokeAsync(timestamp, data.DeserializeObject<Balance>(), cancellationToken));
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
				break;
		}

		return;
	}

	private static class Channels
	{
		//public const string Ping = "ping";
		//public const string Pong = "pong";
		public const string MarketPrice = "MarketPrice";
		public const string OrderBook = "OrderBook";
		public const string Trade = "Trade";
		public const string UserTrade = "UserTrade";
		public const string CanceledOrder = "CanceledOrder";
		public const string UserCanceledOrder = "UserCanceledOrder";
		public const string ParaminingUpdate = "ParaminingUpdate";
		public const string TradingBalance = "TradingBalance";
	}

	public ValueTask SubscribeTicker(long marketId, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new { marketId }, cancellationToken);
	}

	public ValueTask SubscribeAccount(CancellationToken cancellationToken)
	{
		return _client.SendAsync(new { clientId = _authenticator.Key.UnSecure() }, cancellationToken);
	}
}
