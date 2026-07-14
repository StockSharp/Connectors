namespace StockSharp.BingX.Native.Futures;

using System.Globalization;

using Newtonsoft.Json.Linq;

using StockSharp.BingX.Native.Futures.Model;

class SocketClient : BaseLogReceiver
{
	public override string Name => nameof(BingX) + "_" + nameof(Futures) + nameof(SocketClient);

	public event Func<IEnumerable<Ticker>, CancellationToken, ValueTask> TickersReceived;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<IEnumerable<Trade>, CancellationToken, ValueTask> TradesReceived;
	public event Func<IEnumerable<Candle>, CancellationToken, ValueTask> CandlesReceived;
	public event Func<IEnumerable<Position>, CancellationToken, ValueTask> PositionsReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> UserTradesReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private WebSocketClient _client;
	private readonly string _baseWsUrl;
	private readonly string _restBaseUrl;
	private readonly Authenticator _authenticator;
	private readonly int _reconnectAttempts;
	private string _listenKey;
	private long _requestId;
	private long _tradeIdSeed;
	private CancellationTokenSource _listenKeyCts;
	private Task _listenKeyTask;

	public SocketClient(BingXMessageAdapter adapter, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseWsUrl = adapter.IsDemo
			? "wss://vst-open-api-ws.bingx.com/swap-market"
			: $"wss://{adapter.FuturesWsDomain}";

		_restBaseUrl = adapter.IsDemo
			? "https://open-api-vst.bingx.com"
			: $"https://{adapter.RestDomain}";

		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_reconnectAttempts = adapter.ReConnectionSettings.ReAttemptCount;

		_client = CreateClient(_baseWsUrl, workingTime);
	}

	protected override void DisposeManaged()
	{
		StopListenKeyLoop(closeListenKey: true);
		DisposeClient();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);

		StopListenKeyLoop(closeListenKey: false);

		var wsUrl = _baseWsUrl;

		if (_authenticator.CanSign)
		{
			await GetListenKey(cancellationToken);

			if (!_listenKey.IsEmpty())
			{
				wsUrl = $"{_baseWsUrl}?listenKey={_listenKey.DataEscape()}";
				StartListenKeyLoop();
			}
		}

		RecreateClient(wsUrl);
		await _client.ConnectAsync(cancellationToken);
	}

	private void RecreateClient(string url)
	{
		var oldClient = _client;
		_client = CreateClient(url, oldClient?.WorkingTime ?? ((BingXMessageAdapter)Parent).ReConnectionSettings.WorkingTime);

		if (oldClient is null)
			return;

		try
		{
			oldClient.Dispose();
		}
		catch
		{
		}
	}

	private WebSocketClient CreateClient(string url, WorkingTime workingTime)
	{
		return new WebSocketClient(
			url,
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
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			SendSettings = Extensions.CreateJsonSettings(),
		};
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		StopListenKeyLoop(closeListenKey: true);
		_client?.Disconnect();
	}

	private void DisposeClient()
	{
		var client = _client;
		_client = null;

		if (client is null)
			return;

		try
		{
			client.Dispose();
		}
		catch
		{
		}
	}

	private async ValueTask GetListenKey(CancellationToken cancellationToken)
	{
		var url = $"{_restBaseUrl}/openApi/user/auth/userDataStream".To<Uri>();
		var request = new RestRequest((string)null, Method.Post);
		request = request.ApplySecret(url, _authenticator);

		try
		{
			var response = await request.InvokeAsync<object>(url, this, this.AddVerboseLog, cancellationToken);
			_listenKey = ExtractListenKey(response);

			if (_listenKey.IsEmpty())
				this.AddWarningLog("Listen key response does not contain listenKey.");
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Failed to get listen key: {0}", ex.Message);
			_listenKey = null;
		}
	}

	private async ValueTask ExtendListenKey(CancellationToken cancellationToken)
	{
		if (_listenKey.IsEmpty())
			return;

		var url = $"{_restBaseUrl}/openApi/user/auth/userDataStream".To<Uri>();
		var request = new RestRequest((string)null, Method.Put)
			.AddQueryParameter("listenKey", _listenKey);
		request = request.ApplySecret(url, _authenticator);

		await request.InvokeAsync<object>(url, this, this.AddVerboseLog, cancellationToken);
	}

	private async ValueTask CloseListenKey(CancellationToken cancellationToken)
	{
		if (_listenKey.IsEmpty())
			return;

		var listenKey = _listenKey;
		_listenKey = null;

		var url = $"{_restBaseUrl}/openApi/user/auth/userDataStream".To<Uri>();
		var request = new RestRequest((string)null, Method.Delete)
			.AddQueryParameter("listenKey", listenKey);
		request = request.ApplySecret(url, _authenticator);

		await request.InvokeAsync<object>(url, this, this.AddVerboseLog, cancellationToken);
	}

	private static string ExtractListenKey(object response)
	{
		if (response is null)
			return null;

		var token = response as JToken ?? JToken.FromObject(response);

		var listenKey = (string)token["listenKey"];
		if (!listenKey.IsEmpty())
			return listenKey;

		listenKey = (string)token["data"]?["listenKey"];
		if (!listenKey.IsEmpty())
			return listenKey;

		return null;
	}

	private void StartListenKeyLoop()
	{
		StopListenKeyLoop(closeListenKey: false);

		_listenKeyCts = new CancellationTokenSource();
		_listenKeyTask = ListenKeyLoopAsync(_listenKeyCts.Token);
	}

	private void StopListenKeyLoop(bool closeListenKey)
	{
		var cts = _listenKeyCts;
		_listenKeyCts = null;
		_listenKeyTask = null;

		if (cts is not null)
		{
			try
			{
				cts.Cancel();
			}
			finally
			{
				cts.Dispose();
			}
		}

		if (!closeListenKey || _listenKey.IsEmpty())
			return;

		_ = CloseListenKeySafe();
	}

	private async Task CloseListenKeySafe()
	{
		try
		{
			await CloseListenKey(CancellationToken.None);
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Failed to close listen key: {0}", ex.Message);
		}
	}

	private async Task ListenKeyLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
				await ExtendListenKey(cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Listen key keepalive failed: {0}", ex);
		}
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var raw = msg.AsString();

		if (!raw.IsEmpty())
		{
			if (raw.EqualsIgnoreCase("ping"))
			{
				await SendPong(cancellationToken);
				return;
			}

			if (raw.EqualsIgnoreCase("pong"))
				return;
		}

		var obj = msg.AsObject() as JObject;
		if (obj is null)
			return;

		if (obj.TryGetValue("ping", out _))
		{
			await SendPong(cancellationToken);
			return;
		}

		var code = obj["code"]?.To<int?>();
		if (code is int c && c != 0)
		{
			var ex = new InvalidOperationException($"BingX WS error: code={c}, msg={(string)obj["msg"] ?? (string)obj["message"]}");
			if (Error is { } errHandler)
				await errHandler(ex, cancellationToken);
			return;
		}

		var dataType = (string)obj["dataType"];

		if (!dataType.IsEmpty())
		{
			var data = obj["data"];
			await ProcessDataType(dataType, data, obj, cancellationToken);
			return;
		}

		var eventType = (string)obj["e"];
		if (eventType.IsEmpty())
			return;

		switch (eventType.ToUpperInvariant())
		{
			case "ACCOUNT_UPDATE":
				await ProcessAccountUpdate(obj, cancellationToken);
				break;
			case "ORDER_TRADE_UPDATE":
				await ProcessOrderTradeUpdate(obj, cancellationToken);
				break;
			case "ACCOUNT_CONFIG_UPDATE":
				break;
			default:
				this.AddVerboseLog("Unknown futures event type: {0}", eventType);
				break;
		}
	}

	private async ValueTask ProcessDataType(string dataType, JToken data, JObject root, CancellationToken cancellationToken)
	{
		try
		{
			if (dataType.Contains("@ticker", StringComparison.OrdinalIgnoreCase))
			{
				if (TickersReceived is not { } tickerHandler)
					return;

				var symbol = GetSymbolFromDataType(dataType);
				var tickers = new List<Ticker>();

				if (data is JObject dataObj)
				{
					tickers.Add(ParseTicker(dataObj, symbol, root));
				}
				else if (data is JArray arr)
				{
					foreach (var item in arr.OfType<JObject>())
						tickers.Add(ParseTicker(item, symbol, root));
				}

				if (tickers.Count > 0)
					await tickerHandler(tickers, cancellationToken);

				return;
			}

			if (dataType.Contains("@depth", StringComparison.OrdinalIgnoreCase))
			{
				if (OrderBookReceived is { } obHandler && data is JObject dataObj)
				{
					var symbol = (string)dataObj["symbol"] ?? (string)dataObj["s"] ?? GetSymbolFromDataType(dataType);
					var eventTime = ToDateTime(dataObj["timestamp"] ?? dataObj["ts"] ?? dataObj["E"] ?? root["timestamp"]);

					var orderBook = new OrderBook
					{
						Symbol = symbol,
						EventTime = eventTime,
						Bids = ParseDepthEntries(dataObj["bids"] ?? dataObj["b"]),
						Asks = ParseDepthEntries(dataObj["asks"] ?? dataObj["a"]),
					};

					await obHandler(orderBook, cancellationToken);
				}

				return;
			}

			if (dataType.Contains("@trade", StringComparison.OrdinalIgnoreCase))
			{
				if (TradesReceived is not { } tradesHandler)
					return;

				var symbol = GetSymbolFromDataType(dataType);
				var trades = new List<Trade>();

				if (data is JObject dataObj)
				{
					trades.Add(ParseTrade(dataObj, symbol));
				}
				else if (data is JArray arr)
				{
					foreach (var item in arr.OfType<JObject>())
						trades.Add(ParseTrade(item, symbol));
				}

				if (trades.Count > 0)
					await tradesHandler(trades, cancellationToken);

				return;
			}

			if (dataType.Contains("@kline_", StringComparison.OrdinalIgnoreCase))
			{
				if (CandlesReceived is not { } candleHandler)
					return;

				var symbol = GetSymbolFromDataType(dataType);
				var interval = GetIntervalFromDataType(dataType);
				var candles = new List<Candle>();

				if (data is JObject dataObj)
				{
					candles.Add(ParseCandle(dataObj, symbol, interval));
				}
				else if (data is JArray arr)
				{
					foreach (var item in arr.OfType<JObject>())
						candles.Add(ParseCandle(item, symbol, interval));
				}

				if (candles.Count > 0)
					await candleHandler(candles, cancellationToken);

				return;
			}

			this.AddVerboseLog("Unknown futures dataType: {0}", dataType);
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Error processing futures dataType {0}: {1}", dataType, ex);
		}
	}

	private async ValueTask ProcessAccountUpdate(JObject obj, CancellationToken cancellationToken)
	{
		var eventTime = ToDateTime(obj["E"]);
		var account = obj["a"] as JObject;

		if (account is null)
			return;

		if (account["B"] is JArray balancesArr && BalancesReceived is { } balHandler)
		{
			var balances = balancesArr
				.OfType<JObject>()
				.Select(b => new Balance
				{
					Asset = (string)b["a"],
					BalanceAmount = ToDouble(b["wb"]),
					CrossWalletBalance = ToDouble(b["cw"]),
					CrossUnrealizedPnl = ToDouble(b["bc"]),
					AvailableBalance = ToDouble(b["cw"]),
					UpdateTime = eventTime,
				})
				.Where(b => !b.Asset.IsEmpty())
				.ToArray();

			if (balances.Length > 0)
				await balHandler(balances, cancellationToken);
		}

		if (account["P"] is JArray positionsArr && PositionsReceived is { } posHandler)
		{
			var positions = positionsArr
				.OfType<JObject>()
				.Select(p => new Position
				{
					Symbol = (string)p["s"],
					PositionAmount = ToDouble(p["pa"]) ?? 0,
					EntryPrice = ToDouble(p["ep"]),
					UnrealizedProfit = ToDouble(p["up"]),
					MarginType = (string)p["mt"],
					IsolatedWallet = ToDouble(p["iw"]),
					PositionSide = (string)p["ps"],
					UpdateTime = eventTime,
				})
				.Where(p => !p.Symbol.IsEmpty())
				.ToArray();

			if (positions.Length > 0)
				await posHandler(positions, cancellationToken);
		}
	}

	private async ValueTask ProcessOrderTradeUpdate(JObject obj, CancellationToken cancellationToken)
	{
		var eventTime = ToDateTime(obj["E"]);
		var data = obj["o"] as JObject;

		if (data is null)
			return;

		var order = new Order
		{
			Symbol = (string)data["s"],
			ClientOrderId = (string)data["c"] ?? (string)data["C"],
			OrderId = data["i"]?.To<long?>() ?? 0,
			Side = (string)data["S"],
			PositionSide = (string)data["ps"],
			Type = (string)data["o"],
			Status = (string)data["X"] ?? (string)data["x"],
			Price = ToDouble(data["p"]),
			OriginalQuantity = ToDouble(data["q"]),
			ExecutedQuantity = ToDouble(data["z"]),
			StopPrice = ToDouble(data["sp"]),
			WorkingType = (string)data["wt"],
			Time = ToDateTime(data["T"] ?? data["O"]),
			UpdateTime = eventTime == default ? ToDateTime(data["T"]) : eventTime,
		};

		if (OrdersReceived is { } orderHandler)
			await orderHandler([order], cancellationToken);

		var executionType = (string)data["x"];
		var isTradeExecution = executionType.EqualsIgnoreCase("TRADE");

		if (!isTradeExecution || UserTradesReceived is not { } tradeHandler)
			return;

		var userTrade = new UserTrade
		{
			Symbol = order.Symbol,
			Id = data["t"]?.To<long?>() ?? Interlocked.Increment(ref _tradeIdSeed),
			OrderId = order.OrderId,
			Side = order.Side,
			Quantity = ToDouble(data["l"] ?? data["z"]),
			Price = ToDouble(data["L"] ?? data["ap"] ?? data["p"]),
			Commission = ToDouble(data["n"]),
			CommissionAsset = (string)data["N"],
			Time = ToDateTime(data["T"] ?? obj["E"]),
			PositionSide = order.PositionSide,
			IsBuyer = order.Side.EqualsIgnoreCase("BUY"),
			IsMaker = data["m"]?.To<bool?>() ?? false,
		};

		await tradeHandler([userTrade], cancellationToken);
	}

	private static Ticker ParseTicker(JObject data, string fallbackSymbol, JObject root)
	{
		return new Ticker
		{
			Symbol = (string)data["s"] ?? fallbackSymbol,
			PriceChange = ToDouble(data["p"]),
			PriceChangePercent = ToDouble(data["P"]),
			LastPrice = ToDouble(data["c"]),
			LastQuantity = ToDouble(data["L"] ?? data["l"]),
			OpenPrice = ToDouble(data["o"]),
			HighPrice = ToDouble(data["h"]),
			LowPrice = ToDouble(data["l"]),
			Volume = ToDouble(data["v"]),
			QuoteVolume = ToDouble(data["q"] ?? data["m"]),
			OpenTime = ToDateTime(data["O"]),
			CloseTime = ToDateTime(data["C"] ?? data["E"] ?? root["timestamp"]),
		};
	}

	private static Trade ParseTrade(JObject data, string fallbackSymbol)
	{
		return new Trade
		{
			Symbol = (string)data["s"] ?? fallbackSymbol,
			TradeId = data["t"]?.To<long?>() ?? 0,
			Price = ToDouble(data["p"]),
			Quantity = ToDouble(data["q"]),
			TradeTime = ToDateTime(data["T"] ?? data["E"]),
			EventTime = ToDateTime(data["E"]),
			IsBuyerMaker = data["m"]?.To<bool?>() ?? false,
		};
	}

	private static Candle ParseCandle(JObject data, string fallbackSymbol, string interval)
	{
		var openTime = ToDateTime(data["t"]);
		var closeTime = ToDateTime(data["T"]);

		if (closeTime == default && openTime != default)
			closeTime = openTime + GetIntervalTimeSpan(interval);

		return new Candle
		{
			Symbol = (string)data["s"] ?? fallbackSymbol,
			Interval = (string)data["i"] ?? interval,
			OpenTime = openTime,
			CloseTime = closeTime,
			Open = ToDouble(data["o"]) ?? 0,
			Close = ToDouble(data["c"]) ?? 0,
			High = ToDouble(data["h"]) ?? 0,
			Low = ToDouble(data["l"]) ?? 0,
			Volume = ToDouble(data["a"] ?? data["v"]) ?? 0,
			QuoteVolume = ToDouble(data["v"]),
			TradeCount = data["u"]?.To<int?>() ?? 0,
			IsClosed = data["x"]?.To<bool?>() ?? false,
		};
	}

	private static OrderBookEntry[] ParseDepthEntries(JToken token)
	{
		if (token is not JArray levels || levels.Count == 0)
			return [];

		var result = new List<OrderBookEntry>();

		foreach (var level in levels)
		{
			double? price = null;
			double? size = null;

			if (level is JArray arr)
			{
				price = ToDouble(arr.ElementAtOrDefault(0));
				size = ToDouble(arr.ElementAtOrDefault(1));
			}
			else if (level is JObject obj)
			{
				price = ToDouble(obj["p"] ?? obj["price"]);
				size = ToDouble(obj["a"] ?? obj["q"] ?? obj["quantity"] ?? obj["v"]);
			}

			if (price is null || size is null)
				continue;

			result.Add(new OrderBookEntry
			{
				Price = price,
				Size = size
			});
		}

		return [.. result];
	}

	private static string GetSymbolFromDataType(string dataType)
	{
		if (dataType.IsEmpty())
			return null;

		var at = dataType.IndexOf('@');
		return at <= 0 ? dataType : dataType[..at];
	}

	private static string GetIntervalFromDataType(string dataType)
	{
		if (dataType.IsEmpty())
			return null;

		const string marker = "@kline_";
		var idx = dataType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		return idx < 0 ? null : dataType[(idx + marker.Length)..];
	}

	private static TimeSpan GetIntervalTimeSpan(string interval)
	{
		return Native.Extensions.TimeFrames.TryGetKey2(interval) ?? TimeSpan.FromMinutes(1);
	}

	private static DateTime ToDateTime(JToken token)
	{
		var value = token?.To<long?>() ?? 0;
		if (value <= 0)
			return default;

		return value > 9_999_999_999 ? value.FromUnix(false) : value.FromUnix(true);
	}

	private static double? ToDouble(JToken token)
	{
		if (token is null)
			return null;

		var str = token.Value<string>();

		if (str.IsEmpty())
			return null;

		str = str.Trim();

		if (str.EndsWith("%", StringComparison.Ordinal))
			str = str[..^1];

		if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
			return value;

		return null;
	}

	private ValueTask SendPong(CancellationToken cancellationToken)
	{
		return _client?.SendAsync("pong", cancellationToken) ?? default;
	}

	private ValueTask SendSub(string reqType, string dataType, CancellationToken cancellationToken)
	{
		if (_client is null)
			return default;

		var message = new
		{
			id = Interlocked.Increment(ref _requestId).ToString(),
			reqType,
			dataType
		};

		return _client.SendAsync(message, cancellationToken);
	}

	public ValueTask SubscribeTicker(string symbol, CancellationToken cancellationToken)
		=> SendSub("sub", $"{symbol}@ticker", cancellationToken);

	public ValueTask UnsubscribeTicker(string symbol, CancellationToken cancellationToken)
		=> SendSub("unsub", $"{symbol}@ticker", cancellationToken);

	public ValueTask SubscribeOrderBook(string symbol, string level, CancellationToken cancellationToken)
		=> SendSub("sub", $"{symbol}@depth{level}", cancellationToken);

	public ValueTask UnsubscribeOrderBook(string symbol, string level, CancellationToken cancellationToken)
		=> SendSub("unsub", $"{symbol}@depth{level}", cancellationToken);

	public ValueTask SubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> SendSub("sub", $"{symbol}@trade", cancellationToken);

	public ValueTask UnsubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> SendSub("unsub", $"{symbol}@trade", cancellationToken);

	public ValueTask SubscribeCandles(string symbol, string interval, CancellationToken cancellationToken)
		=> SendSub("sub", $"{symbol}@kline_{interval}", cancellationToken);

	public ValueTask UnsubscribeCandles(string symbol, string interval, CancellationToken cancellationToken)
		=> SendSub("unsub", $"{symbol}@kline_{interval}", cancellationToken);

	public ValueTask SubscribePositions(CancellationToken cancellationToken) => default;
	public ValueTask UnsubscribePositions(CancellationToken cancellationToken) => default;
	public ValueTask SubscribeOrders(CancellationToken cancellationToken) => default;
	public ValueTask UnsubscribeOrders(CancellationToken cancellationToken) => default;

	public ValueTask Ping(CancellationToken cancellationToken)
		=> _client?.SendAsync("ping", cancellationToken) ?? default;
}
