namespace StockSharp.Mexc.Native.Futures;

using Newtonsoft.Json.Linq;

using StockSharp.Mexc.Native.Futures.Model;

class SocketClient : BaseLogReceiver
{
	public override string Name => nameof(Mexc) + "_" + nameof(Futures) + nameof(SocketClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<OrderBookUpdate, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<Trade, CancellationToken, ValueTask> TradeReceived;
	public event Func<CandleStream, CancellationToken, ValueTask> CandleReceived;
	public event Func<Position[], CancellationToken, ValueTask> PositionsReceived;
	public event Func<Order, CancellationToken, ValueTask> OrderReceived;
	public event Func<UserTrade, CancellationToken, ValueTask> UserTradeReceived;
	public event Func<Balance[], CancellationToken, ValueTask> BalancesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly string _url;
	private readonly Authenticator _authenticator;
	private readonly SynchronizedDictionary<int, long> _subscriptions = new();
	private int _subscriptionId;
	private long _tradeIdSeed;
	private bool _isLoggedIn;
	private bool _loginInProgress;
	private string _activeFilterKey;
	private object _pendingFilterRequest;
	private CancellationTokenSource _pingCts;
	private Task _pingTask;

	public SocketClient(MexcMessageAdapter adapter, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_url = adapter.IsDemo ? "wss://contract.mexc.com/edge" : $"wss://{adapter.FuturesWsDomain}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			_url,
			(state, token) =>
			{
				if (state == ConnectionStates.Disconnected)
				{
					_isLoggedIn = false;
					_loginInProgress = false;
				}

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
			ReconnectAttempts = adapter.ReConnectionSettings.ReAttemptCount,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			SendSettings = Native.Extensions.CreateJsonSettings(),
		};
	}

	protected override void DisposeManaged()
	{
		StopPingLoop();
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		_isLoggedIn = false;
		_loginInProgress = false;
		await _client.ConnectAsync(cancellationToken);
		StartPingLoop();
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		StopPingLoop();
		_isLoggedIn = false;
		_loginInProgress = false;
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var raw = msg.AsString();
		if (raw.EqualsIgnoreCase("pong"))
			return;

		var obj = msg.AsObject() as JObject;
		if (obj is null)
			return;

		var channel = (string)obj["channel"];
		if (channel.IsEmpty())
			return;

		if (channel.EqualsIgnoreCase("rs.error"))
		{
			var error = new InvalidOperationException((string)obj["data"] ?? "Unknown WS error");
			_loginInProgress = false;

			if (Error is { } errorHandler)
				await errorHandler(error, cancellationToken);

			return;
		}

		if (channel.EqualsIgnoreCase("rs.login"))
		{
			_loginInProgress = false;
			_isLoggedIn = ((string)obj["data"]).EqualsIgnoreCase("success");

			if (_isLoggedIn && _pendingFilterRequest is { } request)
			{
				await Send(request, cancellationToken);
				_pendingFilterRequest = null;
			}

			return;
		}

		if (channel.StartsWith("rs.", StringComparison.OrdinalIgnoreCase))
		{
			this.AddDebugLog("WS command reply: {0}", obj.ToString());
			return;
		}

		if (channel.EqualsIgnoreCase("push.ticker"))
		{
			var data = obj["data"] as JObject;
			if (data is null || TickerReceived is not { } handler)
				return;

			await handler(new Ticker
			{
				Symbol = WsHelpers.ResolveSymbol(obj, data),
				LastPrice = WsHelpers.ToDouble(data["lastPrice"]),
				HighPrice = WsHelpers.ToDouble(data["high24Price"]),
				LowPrice = WsHelpers.ToDouble(data["lower24Price"]),
				PriceChangePercent = WsHelpers.ToDouble(data["riseFallRate"]),
				Volume = WsHelpers.ToDouble(data["volume24"]),
			}, cancellationToken);
			return;
		}

		if (channel.EqualsIgnoreCase("push.deal"))
		{
			var data = obj["data"] as JObject;
			if (data is null || TradeReceived is not { } handler)
				return;

			await handler(new Trade
			{
				Symbol = WsHelpers.ResolveSymbol(obj, data),
				Id = Interlocked.Increment(ref _tradeIdSeed),
				Price = WsHelpers.ToDouble(data["p"]),
				Qty = WsHelpers.ToDouble(data["v"]),
				Time = WsHelpers.ToDateTime(data["t"]),
				IsBuyerMaker = data["M"]?.To<int?>() == 2,
			}, cancellationToken);
			return;
		}

		if (channel.EqualsIgnoreCase("push.depth"))
		{
			var data = obj["data"] as JObject;
			if (data is null || OrderBookReceived is not { } handler)
				return;

			var version = data["version"]?.To<long?>() ?? 0;

			await handler(new OrderBookUpdate
			{
				Symbol = WsHelpers.ResolveSymbol(obj, data),
				FirstUpdateId = version,
				FinalUpdateId = version,
				Bids = WsHelpers.ToBookEntries(data["bids"]),
				Asks = WsHelpers.ToBookEntries(data["asks"]),
			}, cancellationToken);
			return;
		}

		if (channel.EqualsIgnoreCase("push.kline"))
		{
			var data = obj["data"] as JObject;
			if (data is null || CandleReceived is not { } handler)
				return;

			var symbol = WsHelpers.ResolveSymbol(obj, data);
			var nativeInterval = WsHelpers.FromWsKlineInterval((string)data["interval"]);
			var openTime = WsHelpers.ToDateTime(data["t"]);

			await handler(new CandleStream
			{
				Symbol = symbol,
				Kline = new CandleData
				{
					Symbol = symbol,
					Interval = nativeInterval,
					OpenTime = openTime,
					CloseTime = openTime + WsHelpers.ToTimeFrame(nativeInterval),
					Open = WsHelpers.ToDouble(data["o"]) ?? 0,
					Close = WsHelpers.ToDouble(data["c"]) ?? 0,
					High = WsHelpers.ToDouble(data["h"]) ?? 0,
					Low = WsHelpers.ToDouble(data["l"]) ?? 0,
					Volume = WsHelpers.ToDouble(data["q"]) ?? 0,
					QuoteVolume = WsHelpers.ToDouble(data["a"]) ?? 0,
					IsClosed = false,
				}
			}, cancellationToken);
			return;
		}

		if (channel.EqualsIgnoreCase("push.personal.order"))
		{
			var data = obj["data"] as JObject;
			if (data is null)
				return;

			var order = WsHelpers.ToOrder(data);

			if (OrderReceived is { } orderHandler)
				await orderHandler(order, cancellationToken);

			var trade = WsHelpers.ToUserTrade(data, ref _tradeIdSeed);
			if (trade is not null && UserTradeReceived is { } tradeHandler)
				await tradeHandler(trade, cancellationToken);

			return;
		}

		if (channel.EqualsIgnoreCase("push.personal.asset"))
		{
			var data = obj["data"] as JObject;
			if (data is null || BalancesReceived is not { } handler)
				return;

			var available = WsHelpers.ToDouble(data["availableBalance"]);
			var frozen = WsHelpers.ToDouble(data["frozenBalance"]);

			await handler(
			[
				new Balance
				{
					Asset = (string)data["currency"],
					AvailableBalance = available,
					BalanceValue = available + frozen,
					CrossWalletBalance = available,
					UpdateTime = WsHelpers.ToDateTime(obj["ts"]),
				}
			], cancellationToken);

			return;
		}

		if (channel.EqualsIgnoreCase("push.personal.position"))
		{
			var data = obj["data"] as JObject;
			if (data is null || PositionsReceived is not { } handler)
				return;

			await handler(
			[
				new Position
				{
					Symbol = WsHelpers.ResolveSymbol(obj, data),
					PositionAmt = WsHelpers.ToDouble(data["holdVol"]),
					EntryPrice = WsHelpers.ToDouble(data["holdAvgPrice"]) ?? WsHelpers.ToDouble(data["openAvgPrice"]),
					UnRealizedProfit = WsHelpers.ToDouble(data["realised"]),
					LiquidationPrice = WsHelpers.ToDouble(data["liquidatePrice"]),
					Leverage = WsHelpers.ToDouble(data["leverage"]),
					PositionSide = data["positionType"]?.To<int?>() == 1 ? "LONG" : "SHORT",
					UpdateTime = WsHelpers.ToDateTime(obj["ts"]),
				}
			], cancellationToken);
		}
	}

	public ValueTask SubscribeTicker(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, "sub.ticker", symbol, cancellationToken);

	public ValueTask UnsubscribeTicker(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "unsub.ticker", symbol, cancellationToken);

	public ValueTask SubscribeOrderBook(long transId, string symbol, int levels, CancellationToken cancellationToken)
		=> Subscribe(transId, "sub.depth", symbol, cancellationToken, new { compress = false });

	public ValueTask UnsubscribeOrderBook(long originTransId, string symbol, int levels, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "unsub.depth", symbol, cancellationToken);

	public ValueTask SubscribeTrades(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, "sub.deal", symbol, cancellationToken);

	public ValueTask UnsubscribeTrades(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "unsub.deal", symbol, cancellationToken);

	public ValueTask SubscribeCandles(long transId, string symbol, string interval, CancellationToken cancellationToken)
		=> Subscribe(transId, "sub.kline", symbol, cancellationToken, new { interval = WsHelpers.ToWsKlineInterval(interval) });

	public ValueTask UnsubscribeCandles(long originTransId, string symbol, string interval, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "unsub.kline", symbol, cancellationToken);

	public async ValueTask EnsurePrivateSubscriptions(long transId, bool orders, bool deals, bool positions, bool assets, CancellationToken cancellationToken)
	{
		var filters = new List<string>();

		if (orders)
			filters.Add("order");

		if (deals)
			filters.Add("order.deal");

		if (positions)
			filters.Add("position");

		if (assets)
			filters.Add("asset");

		if (filters.Count == 0)
		{
			_activeFilterKey = string.Empty;
			_pendingFilterRequest = null;
			await SendLogin(false, cancellationToken);
			return;
		}

		var key = filters.OrderBy(f => f).JoinComma();
		if (_isLoggedIn && _activeFilterKey == key)
			return;

		_activeFilterKey = key;

		var filterRequest = new
		{
			method = "personal.filter",
			param = new
			{
				filters = filters.Select(f => new { filter = f }).ToArray()
			}
		};

		_pendingFilterRequest = filterRequest;

		if (_isLoggedIn)
		{
			await Send(filterRequest, cancellationToken);
			_pendingFilterRequest = null;
			return;
		}

		await SendLogin(false, cancellationToken);
	}

	private async ValueTask SendLogin(bool subscribe, CancellationToken cancellationToken)
	{
		if (_loginInProgress)
			return;

		_loginInProgress = true;
		_isLoggedIn = false;

		var reqTime = ((long)DateTime.UtcNow.ToUnix(false)).ToString();
		var apiKey = _authenticator.Key.UnSecure();
		var signature = _authenticator.Sign(apiKey + reqTime);

		await Send(new
		{
			subscribe,
			method = "login",
			param = new
			{
				apiKey,
				reqTime,
				signature,
			}
		}, cancellationToken);
	}

	private ValueTask Subscribe(long transId, string method, string symbol, CancellationToken cancellationToken, object extraParams = null)
	{
		var id = ++_subscriptionId;
		_subscriptions[id] = transId;

		var wsSymbol = symbol.ToFuturesWsSymbol();

		return Send(new
		{
			method,
			param = MergeParams(new { symbol = wsSymbol }, extraParams),
		}, cancellationToken);
	}

	private ValueTask Unsubscribe(long originTransId, string method, string symbol, CancellationToken cancellationToken)
	{
		var id = ++_subscriptionId;

		return Send(new
		{
			method,
			param = new { symbol = symbol.ToFuturesWsSymbol() },
		}, cancellationToken);
	}

	private static object MergeParams(object baseParams, object extraParams)
	{
		if (extraParams is null)
			return baseParams;

		var merged = JObject.FromObject(baseParams);
		merged.Merge(JObject.FromObject(extraParams), new JsonMergeSettings
		{
			MergeArrayHandling = MergeArrayHandling.Union
		});

		return merged;
	}

	private ValueTask Send(object message, CancellationToken cancellationToken)
		=> _client.SendAsync(message, cancellationToken);

	private void StartPingLoop()
	{
		if (_pingCts != null)
			return;

		_pingCts = new CancellationTokenSource();
		_pingTask = PingLoopAsync(_pingCts.Token);
	}

	private void StopPingLoop()
	{
		var cts = _pingCts;
		_pingCts = null;
		_pingTask = null;

		if (cts is null)
			return;

		try
		{
			cts.Cancel();
		}
		finally
		{
			cts.Dispose();
		}
	}

	private async Task PingLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
				await Send(new { method = "ping" }, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			this.AddErrorLog(ex);
		}
	}
}
