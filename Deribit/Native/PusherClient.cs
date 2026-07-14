namespace StockSharp.Deribit.Native;

using System.Dynamic;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Deribit) + "_" + nameof(PusherClient);

	public event Func<long, IEnumerable<Symbol>, CancellationToken, ValueTask> NewSymbols;
	public event Func<IEnumerable<Trade>, CancellationToken, ValueTask> NewTrades;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<Announcement, CancellationToken, ValueTask> NewAnnouncement;
	public event Func<long, IEnumerable<Ohlc>, CancellationToken, ValueTask> NewCandles;
	public event Func<long, IEnumerable<Position>, CancellationToken, ValueTask> PositionChanged;
	public event Func<long, Account, CancellationToken, ValueTask> AccountChanged;
	public event Func<long, Order, CancellationToken, ValueTask> OrderChanged;
	public event Func<long, DeribitWithdraw, CancellationToken, ValueTask> WithdrawUpdated;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> NewUserTrades;
	public event Func<long?, MessageTypes?, Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public event Action<long, Exception> TradesSubscribeResponse;
	public event Action<long, Exception> OrderBookSubscribeResponse;
	public event Action<long, Exception> TickerSubscribeResponse;
	public event Action<long, Exception> NewsSubscribeResponse;
	public event Action<long, Exception> TradesUnSubscribeResponse;
	public event Action<long, Exception> OrderBookUnSubscribeResponse;
	public event Action<long, Exception> TickerUnSubscribeResponse;
	public event Action<long, Exception> NewsUnSubscribeResponse;

	private static class DeribitMessageTypes
	{
		public const MessageTypes Ticks = (MessageTypes)(-1);
		public const MessageTypes Withdraw = (MessageTypes)(-2);
		public const MessageTypes OrderState = (MessageTypes)(-3);
	}

	private readonly SynchronizedDictionary<long, (MessageTypes type, bool isSubsribe)> _requests = [];

	private readonly Authenticator _authenticator;
	private readonly WebSocketClient _client;
	private bool _isLogout;

	private const long _authTransId = 1;

	public PusherClient(string address, Authenticator authenticator, int attemptsCount, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			$"wss://{address}/ws/api/v2/",
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
					return handler(null, null, error, token);
				return default;
			},
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = attemptsCount,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		if (_authenticator.CanSign)
			_client.PostConnect += OnPostConnect;
	}

	protected override void DisposeManaged()
	{
		if (_authenticator.CanSign)
			_client.PostConnect -= OnPostConnect;

		_client.Dispose();
		base.DisposeManaged();
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var data = string.Empty;
		var signature = _authenticator.Sign(data, out var nonce, out var timestamp);

		return Send(_authTransId, Actions.Auth, new
		{
			grant_type = "client_signature",
			client_id = _authenticator.Key.To<string>(),
			timestamp,
			signature,
			nonce,
			data,
		}, cancellationToken);
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		_isLogout = false;
		this.AddInfoLog(LocalizedStrings.Connecting);

		_requests.Add(_authTransId, (MessageTypes.Connect, true));
		await _client.ConnectAsync(cancellationToken);
	}

	public async ValueTask Disconnect(long id, CancellationToken cancellationToken)
	{
		if (_isLogout || !_client.IsConnected)
			return;

		_isLogout = true;
		this.AddInfoLog("Logout");
		await Send(id, Actions.Logout, null, cancellationToken);
		
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	public ValueTask CancelOnDisconnect(long id, bool enabled, string scope, CancellationToken cancellationToken)
	{
		_requests.Add(id, (MessageTypes.Command, enabled));
		return Send(id, enabled ? Actions.EnableCod : Actions.DisableCod, new
		{
			scope,
		}, cancellationToken);
	}

	public ValueTask DisableHeartbeat(long id, CancellationToken cancellationToken)
	{
		_requests.Add(id, (MessageTypes.Command, false));
		return Send(id, Actions.DisableHeartbeat, null, cancellationToken);
	}

	public ValueTask Ping(long id, CancellationToken cancellationToken)
	{
		_requests.Add(id, (MessageTypes.Time, false));
		return Send(id, Actions.Test, null, cancellationToken);
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var id = (long?)obj.id;

		if (obj.error != null)
		{
			var tuple = id == null ? default : _requests.TryGetValue2(id.Value);
			var error = new InvalidOperationException((string)obj.error.ToString());

			if (Error is { } errorHandler)
				await errorHandler(id, tuple?.type, error, cancellationToken);
		}
		else
		{
			if (id == null)
			{
				if (obj.method == "subscription")
				{
					var p = obj.@params;
					var channel = (string)p.channel;
					var data = (JToken)p.data;

					if (channel.StartsWithIgnoreCase("book."))
					{
						if (OrderBookChanged is { } handler)
							await handler(data.DeserializeObject<OrderBook>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("ticker."))
					{
						if (TickerChanged is { } handler)
							await handler(data.DeserializeObject<Ticker>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("trades."))
					{
						if (NewTrades is { } handler)
							await handler(data.DeserializeObject<IEnumerable<Trade>>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("announcements"))
					{
						if (NewAnnouncement is { } handler)
							await handler(data.DeserializeObject<Announcement>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("user.orders."))
					{
						if (OrderChanged is { } handler)
							await handler(0, data.DeserializeObject<Order>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("user.trades."))
					{
						if (NewUserTrades is { } handler)
							await handler(data.DeserializeObject<IEnumerable<UserTrade>>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("user.portfolio."))
					{
						if (AccountChanged is { } handler)
							await handler(0, data.DeserializeObject<Account>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("chart.trades."))
					{
						if (NewCandles is { } handler)
							await handler(0, [data.DeserializeObject<Ohlc>()], cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("user.changes."))
					{
						if (OrderChanged is { } orderHandler)
						{
							foreach (var order in ((JToken)p.data.orders).DeserializeObject<IEnumerable<Order>>())
								await orderHandler(0, order, cancellationToken);
						}

						if (NewUserTrades is { } tradesHandler)
							await tradesHandler(((JToken)p.data.trades).DeserializeObject<IEnumerable<UserTrade>>(), cancellationToken);

						if (PositionChanged is { } posHandler)
							await posHandler(0, ((JToken)p.data.positions).DeserializeObject<IEnumerable<Position>>(), cancellationToken);
					}
					else
						this.AddErrorLog(LocalizedStrings.UnknownEvent, channel);
				}
			}
			else
			{
				if (_requests.TryGetValue(id.Value, out var tuple))
				{
					var msgType = tuple.type;

					var res = (JToken)obj.result;

					switch (msgType)
					{
						case MessageTypes.SecurityLookup:
							if (NewSymbols is { } newSymbolsHandler)
								await newSymbolsHandler(id.Value, res.DeserializeObject<IEnumerable<Symbol>>(), cancellationToken);
							break;

						case MessageTypes.Level1Change:
						{
							(tuple.isSubsribe ? TickerSubscribeResponse : TickerUnSubscribeResponse)?.Invoke(id.Value, null);
							break;
						}

						case MessageTypes.QuoteChange:
						{
							(tuple.isSubsribe ? OrderBookSubscribeResponse : OrderBookUnSubscribeResponse)?.Invoke(id.Value, null);
							break;
						}

						case DeribitMessageTypes.Ticks:
						{
							(tuple.isSubsribe ? TradesSubscribeResponse : TradesUnSubscribeResponse)?.Invoke(id.Value, null);
							break;
						}

						case MessageTypes.News:
						{
							(tuple.isSubsribe ? NewsSubscribeResponse : NewsUnSubscribeResponse)?.Invoke(id.Value, null);
							break;
						}

						case MessageTypes.CandleTimeFrame:
						{
							// subscribe reply

							//var ticks = ((JToken)obj.result.ticks).DeserializeObject<long[]>();
							//var open = ((JToken)obj.result.open).DeserializeObject<double[]>();
							//var high = ((JToken)obj.result.high).DeserializeObject<double[]>();
							//var low = ((JToken)obj.result.low).DeserializeObject<double[]>();
							//var close = ((JToken)obj.result.close).DeserializeObject<double[]>();
							//var volume = ((JToken)obj.result.volume).DeserializeObject<double[]>();

							//NewCandles?.Invoke(id.Value, ticks.Select((t, i) => new Ohlc
							//{
							//	Tick = t,
							//	Open = open[i],
							//	High = high[i],
							//	Low = low[i],
							//	Close = close[i],
							//	Volume = volume[i],
							//}));

							break;
						}

						case MessageTypes.Portfolio:
							// subscribe reply
							break;

						case MessageTypes.PortfolioLookup:
							if (AccountChanged is { } accountHandler)
							{
								if (res.Type == JTokenType.Array)
								{
									foreach (var account in res.DeserializeObject<IEnumerable<Account>>())
										await accountHandler(id.Value, account, cancellationToken);
								}
								else
									await accountHandler(id.Value, res.DeserializeObject<Account>(), cancellationToken);
							}

							break;

						case MessageTypes.PositionChange:
							if (PositionChanged is { } posChangedHandler)
								await posChangedHandler(id.Value, res.DeserializeObject<IEnumerable<Position>>(), cancellationToken);
							break;

						case MessageTypes.OrderStatus:
						{
							if (OrderChanged is { } orderStatusHandler)
							{
								foreach (var order in res.DeserializeObject<IEnumerable<Order>>())
									await orderStatusHandler(id.Value, order, cancellationToken);
							}

							break;
						}

						case DeribitMessageTypes.OrderState:
							if (OrderChanged is { } orderStateHandler)
								await orderStateHandler(id.Value, res.DeserializeObject<Order>(), cancellationToken);
							break;

						case MessageTypes.Execution:
							// subscribe reply
							//NewUserTrades?.Invoke(res.DeserializeObject<IEnumerable<UserTrade>>());
							break;

						case MessageTypes.OrderRegister:
						case MessageTypes.OrderReplace:
						{
							var order = ((JToken)obj.result.order).DeserializeObject<Order>();

							if (OrderChanged is { } orderRegHandler)
								await orderRegHandler(0, order, cancellationToken);

							if (obj.result.trades != null)
							{
								if (NewUserTrades is { } userTradesHandler)
									await userTradesHandler(((JToken)obj.result.trades).DeserializeObject<IEnumerable<UserTrade>>(), cancellationToken);
							}

							break;
						}

						case MessageTypes.OrderCancel:
							if (res.Type != JTokenType.Integer)
							{
								if (OrderChanged is { } orderCancelHandler)
									await orderCancelHandler(0, res.DeserializeObject<Order>(), cancellationToken);
							}

							break;

						case MessageTypes.OrderGroupCancel:
							break;

						case DeribitMessageTypes.Withdraw:
							if (WithdrawUpdated is { } withdrawHandler)
								await withdrawHandler(id.Value, res.DeserializeObject<DeribitWithdraw>(), cancellationToken);
							break;

						case MessageTypes.Connect:
							this.AddInfoLog(LocalizedStrings.Connected);
							break;

						case MessageTypes.Command:
							// COD response
							break;

						case MessageTypes.Time:
							break;

						default:
							this.AddErrorLog(LocalizedStrings.UnknownEvent, msgType);
							break;
					}
				}
				else
					this.AddErrorLog(LocalizedStrings.UnknownEvent, id.Value);
			}
		}
	}

	private static class Actions
	{
		//public const string Ping = "public/ping";
		public const string Auth = "public/auth";
		public const string PublicSubscribe = "public/subscribe";
		public const string PublicUnSubscribe = "public/unsubscribe";
		public const string GetAccountSummary = "private/get_account_summary";
		public const string Buy = "private/buy";
		public const string Sell = "private/sell";
		public const string Edit = "private/edit";
		public const string Cancel = "private/cancel";
		public const string CancelByLabel = "private/cancel_by_label";
		public const string CancelAll = "private/cancel_all";
		public const string GetOrderState = "private/get_order_state";
		public const string GetOpenOrders = "private/get_open_orders_by_currency";
		public const string GetPositions = "private/get_positions";
		public const string PrivateSubscribe = "private/subscribe";
		public const string PrivateUnSubscribe = "private/unsubscribe";
		public const string Withdraw = "private/withdraw";
		public const string Logout = "private/logout";
		public const string EnableCod = "private/enable_cancel_on_disconnect";
		public const string DisableCod = "private/disable_cancel_on_disconnect";
		public const string DisableHeartbeat = "public/disable_heartbeat";
		public const string Test = "public/test";
	}

	private static class Channels
	{
		public const string OrderBook = "book.{0}.raw";
		public const string Trade = "trades.{0}.raw";
		public const string Ticker = "ticker.{0}.raw";
		public const string Announcements = "announcements";
		//public const string UserOrder = "user.orders.any.any.raw";
		//public const string UserTrade = "user.trades.any.any.raw";
		public const string Portfolio = "user.portfolio.{0}";
		public const string UserChanges = "user.changes.any.any.raw";
		public const string Chart = "chart.trades.{0}.{1}";
	}

	public ValueTask SubscribeTicker(long transactionId, string instrument, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.Level1Change, true));
		return PublicSubscribe(transactionId, Channels.Ticker.Put(instrument), cancellationToken);
	}

	public ValueTask UnSubscribeTicker(long originTransId, long transactionId, string instrument, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.Level1Change, false));
		return PublicUnSubscribe(originTransId, transactionId, Channels.Ticker.Put(instrument), cancellationToken);
	}

	public ValueTask SubscribeTrades(long transactionId, string instrument, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (DeribitMessageTypes.Ticks, true));
		return PublicSubscribe(transactionId, Channels.Trade.Put(instrument), cancellationToken);
	}

	public ValueTask UnSubscribeTrades(long originTransId, long transactionId, string instrument, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (DeribitMessageTypes.Ticks, false));
		return PublicUnSubscribe(originTransId, transactionId, Channels.Trade.Put(instrument), cancellationToken);
	}

	public ValueTask SubscribeOrderBook(long transactionId, string instrument, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.QuoteChange, true));
		return PublicSubscribe(transactionId, Channels.OrderBook.Put(instrument), cancellationToken);
	}

	public ValueTask UnSubscribeOrderBook(long originTransId, long transactionId, string instrument, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.QuoteChange, false));
		return PublicUnSubscribe(originTransId, transactionId, Channels.OrderBook.Put(instrument), cancellationToken);
	}

	public ValueTask RequestCandles(long transactionId, string instrument, long from, long to, string resolution, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.CandleTimeFrame, true));

		return Send(transactionId, "public/get_tradingview_chart_data", new
		{
			instrument_name = instrument,
			start_timestamp = from,
			end_timestamp = to,
			resolution
		}, cancellationToken);
	}

	public ValueTask SubscribeAnnouncements(long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.News, true));
		return PublicSubscribe(transactionId, Channels.Announcements, cancellationToken);
	}

	public ValueTask UnSubscribeAnnouncements(long originTransId, long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.News, false));
		return PublicUnSubscribe(originTransId, transactionId, Channels.Announcements, cancellationToken);
	}

	public ValueTask SubscribeCandles(long transactionId, string instrument, string resolution, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.CandleTimeFrame, true));
		return PublicSubscribe(transactionId, Channels.Chart.Put(instrument, resolution), cancellationToken);
	}

	public ValueTask UnSubscribeCandles(long originTransId, long transactionId, string instrument, string resolution, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.CandleTimeFrame, false));
		return PublicUnSubscribe(originTransId, transactionId, Channels.Chart.Put(instrument, resolution), cancellationToken);
	}

	private ValueTask PublicSubscribe(long transactionId, string channel, CancellationToken cancellationToken)
	{
		return Send(transactionId, Actions.PublicSubscribe, new { channels = new[] { channel } }, cancellationToken, transactionId);
	}

	private ValueTask PublicUnSubscribe(long originTransId, long transactionId, string channel, CancellationToken cancellationToken)
	{
		return Send(transactionId, Actions.PublicUnSubscribe, new { channels = new[] { channel } }, cancellationToken, -originTransId);
	}

	//public void ProcessPing()
	//{
	//	Process(Actions.Ping, new { });
	//}

	public ValueTask RequestAccount(long transactionId, string currency, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.PortfolioLookup, true));

		return Send(transactionId, Actions.GetAccountSummary, new
		{
			currency,
			extended = true,
		}, cancellationToken);
	}

	public ValueTask RequestPositions(long transactionId, string currency, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.PositionChange, true));
		return Send(transactionId, Actions.GetPositions, new { currency }, cancellationToken);
	}

	public ValueTask RequestOrderState(long transactionId, string orderId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (DeribitMessageTypes.OrderState, true));
		return Send(transactionId, Actions.GetOrderState, new { order_id = orderId }, cancellationToken);
	}

	public ValueTask RequestOpenOrders(long transactionId, string currency, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.OrderStatus, true));
		return Send(transactionId, Actions.GetOpenOrders, new { currency }, cancellationToken);
	}

	public ValueTask SubscribeOrders(long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.Execution, true));
		return Send(transactionId, Actions.PrivateSubscribe, new
		{
			channels = new[] { Channels.UserChanges },
		}, cancellationToken, transactionId);
	}

	public ValueTask UnSubscribeOrders(long originTransId, long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.Execution, false));
		return Send(transactionId, Actions.PrivateUnSubscribe, new
		{
			channels = new[] { Channels.UserChanges },
		}, cancellationToken, -originTransId);
	}

	public ValueTask SubscribeAccount(long transactionId, string coin, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.Portfolio, true));
		return Send(transactionId, Actions.PrivateSubscribe, new
		{
			channels = new[] { Channels.Portfolio.Put(coin) },
		}, cancellationToken, transactionId);
	}

	public ValueTask UnSubscribeAccount(long originTransId, long transactionId, string coin, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.Portfolio, false));
		return Send(transactionId, Actions.PrivateUnSubscribe, new
		{
			channels = new[] { Channels.Portfolio.Put(coin) },
		}, cancellationToken, -originTransId);
	}

	public ValueTask RegisterOrder(long transactionId, Sides side, string instrument, decimal quantity,
		string type, decimal? price, decimal? stopPrice, decimal? visibleVolume, string tif,
		string trigger, string advanced, bool? postOnly, bool? reduceOnly, CancellationToken cancellationToken)
	{
		dynamic order = new ExpandoObject();

		order.instrument_name = instrument;
		order.amount = quantity.To<string>();
		order.type = type;

		if (price != null)
			order.price = price.Value.To<string>();

		if (stopPrice != null)
			order.stop_price = stopPrice.Value.To<string>();

		order.label = transactionId.To<string>();

		if (visibleVolume != null)
			order.max_show = visibleVolume.Value.To<string>();

		if (postOnly != null)
			order.post_only = postOnly.Value ? "true" : "false";

		if (reduceOnly != null)
			order.reduce_only = reduceOnly.Value ? "true" : "false";

		if (!trigger.IsEmpty())
			order.trigger = trigger;

		if (!advanced.IsEmpty())
			order.advanced = advanced;

		order.time_in_force = tif;

		_requests.Add(transactionId, (MessageTypes.OrderRegister, true));
		return Send(transactionId, side == Sides.Buy ? Actions.Buy : Actions.Sell, order, cancellationToken);
	}

	public ValueTask EditOrder(long transactionId, string orderId, decimal quantity, decimal price,
		decimal? stopPrice, string advanced, bool? postOnly, CancellationToken cancellationToken)
	{
		dynamic order = new ExpandoObject();

		order.order_id = orderId;
		order.amount = quantity.To<string>();
		order.price = price.To<string>();

		if (postOnly != null)
			order.post_only = postOnly.Value ? "true" : "false";

		if (!advanced.IsEmpty())
			order.advanced = advanced;

		if (stopPrice != null)
			order.stopPx = stopPrice.Value.To<string>();

		_requests.Add(transactionId, (MessageTypes.OrderReplace, true));
		return Send(transactionId, Actions.Edit, order, cancellationToken);
	}

	public ValueTask CancelOrder(long transactionId, string orderId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.OrderCancel, true));
		return Send(transactionId, Actions.Cancel, new { order_id = orderId }, cancellationToken);
	}

	public ValueTask CancelOrderByLabel(long transactionId, long originalTransactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.OrderCancel, true));
		return Send(transactionId, Actions.CancelByLabel, new { label = originalTransactionId.To<string>() }, cancellationToken);
	}

	public ValueTask CancelGroupOrders(long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (MessageTypes.OrderGroupCancel, true));
		return Send(transactionId, Actions.CancelAll, new { }, cancellationToken);
	}

	public ValueTask Withdraw(long transactionId, string currency, decimal volume, WithdrawInfo info, string comment, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		_requests.Add(transactionId, (DeribitMessageTypes.Withdraw, true));
		return Send(transactionId, Actions.Withdraw, new
		{
			currency,
			address = info.CryptoAddress,
			amount = volume,
			priority = info.Express ? "insane" : "high",
			tfa = comment,
		}, cancellationToken);
	}

	private ValueTask Send(long id, string method, object @params, CancellationToken cancellationToken, long subId = default)
	{
		return _client.SendAsync(new
		{
			jsonrpc = "2.0",
			id,
			method,
			@params
		}, cancellationToken, subId);
	}
}