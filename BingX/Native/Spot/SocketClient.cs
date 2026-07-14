namespace StockSharp.BingX.Native.Spot;

using System.Globalization;

using Newtonsoft.Json.Linq;

using StockSharp.BingX.Native.Spot.Model;

class SocketClient : BaseLogReceiver
{
	public override string Name => nameof(BingX) + "_" + nameof(Spot) + nameof(SocketClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<Trade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Candle, CancellationToken, ValueTask> CandleReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> UserTradesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private WebSocketClient _client;
	private readonly string _baseWsUrl;
	private readonly string _restBaseUrl;
	private readonly Authenticator _authenticator;
	private readonly int _reconnectAttempts;
	private string _listenKey;
	private long _requestId;
	private CancellationTokenSource _listenKeyCts;
	private Task _listenKeyTask;

	public SocketClient(BingXMessageAdapter adapter, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		if (adapter.IsDemo)
			throw new NotSupportedException(LocalizedStrings.DemoMode);

		_baseWsUrl = $"wss://{adapter.SpotWsDomain}";
		_restBaseUrl = $"https://{adapter.RestDomain}";
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

		await ProcessLegacyEvent(eventType, obj, cancellationToken);
	}

	private async ValueTask ProcessDataType(string dataType, JToken data, JObject root, CancellationToken cancellationToken)
	{
		try
		{
			if (dataType.EqualsIgnoreCase("spot.executionReport"))
			{
				await ProcessExecutionReport(data as JObject, cancellationToken);
				return;
			}

			if (dataType.Contains("@ticker", StringComparison.OrdinalIgnoreCase))
			{
				if (data is JObject dataObj && TickerReceived is { } tickerHandler)
				{
					var ticker = dataObj.DeserializeObject<Ticker>();
					ticker.Symbol ??= GetSymbolFromDataType(dataType);

					if (ticker.EventTime == default)
						ticker.EventTime = ToDateTime(dataObj["E"] ?? dataObj["C"] ?? root["timestamp"]);

					await tickerHandler(ticker, cancellationToken);
				}

				return;
			}

			if (dataType.Contains("@depth", StringComparison.OrdinalIgnoreCase))
			{
				if (data is JObject dataObj && OrderBookReceived is { } obHandler)
				{
					var symbol = (string)dataObj["symbol"] ?? (string)dataObj["s"] ?? GetSymbolFromDataType(dataType);
					var eventTime = ToDateTime(dataObj["timestamp"] ?? dataObj["ts"] ?? dataObj["E"] ?? root["timestamp"]);

					var orderBook = new OrderBook
					{
						Symbol = symbol,
						EventTime = eventTime,
						Bids = ParseDepthLevels(dataObj["bids"] ?? dataObj["b"]),
						Asks = ParseDepthLevels(dataObj["asks"] ?? dataObj["a"]),
					};

					await obHandler(orderBook, cancellationToken);
				}

				return;
			}

			if (dataType.Contains("@trade", StringComparison.OrdinalIgnoreCase))
			{
				if (TradeReceived is { } tradeHandler)
				{
					var symbol = GetSymbolFromDataType(dataType);

					if (data is JArray arr)
					{
						foreach (var item in arr.OfType<JObject>())
							await tradeHandler(ParseTrade(item, symbol), cancellationToken);
					}
					else if (data is JObject item)
					{
						await tradeHandler(ParseTrade(item, symbol), cancellationToken);
					}
				}

				return;
			}

			if (dataType.Contains("@kline_", StringComparison.OrdinalIgnoreCase))
			{
				if (CandleReceived is not { } candleHandler)
					return;

				var symbol = GetSymbolFromDataType(dataType);
				var interval = GetIntervalFromDataType(dataType);

				if (data is JArray arr)
				{
					foreach (var item in arr.OfType<JObject>())
						await candleHandler(ParseCandle(item, symbol, interval), cancellationToken);
				}
				else if (data is JObject item)
				{
					if (item["data"] is JArray nested)
					{
						foreach (var nestedItem in nested.OfType<JObject>())
							await candleHandler(ParseCandle(nestedItem, symbol, interval), cancellationToken);
					}
					else
					{
						await candleHandler(ParseCandle(item, symbol, interval), cancellationToken);
					}
				}

				return;
			}

			this.AddVerboseLog("Unknown dataType: {0}", dataType);
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Error processing dataType {0}: {1}", dataType, ex);
		}
	}

	private async ValueTask ProcessExecutionReport(JObject dataObj, CancellationToken cancellationToken)
	{
		if (dataObj is null)
			return;

		var order = new Order
		{
			Symbol = (string)dataObj["s"],
			OrderId = dataObj["i"]?.To<long?>() ?? 0,
			ClientOrderId = (string)dataObj["C"] ?? (string)dataObj["c"],
			Price = ToDouble(dataObj["p"]),
			OriginalQuantity = ToDouble(dataObj["q"]),
			ExecutedQuantity = ToDouble(dataObj["z"]),
			Status = (string)dataObj["X"],
			Type = (string)dataObj["o"],
			Side = (string)dataObj["S"],
			Time = ToDateTime(dataObj["O"]),
			UpdateTime = ToDateTime(dataObj["E"] ?? dataObj["ws"]),
		};

		if (OrdersReceived is { } orderHandler)
			await orderHandler([order], cancellationToken);

		var executionType = (string)dataObj["x"];
		var isTradeExecution = executionType.EqualsIgnoreCase("TRADE");

		if (!isTradeExecution || UserTradesReceived is not { } tradeHandler)
			return;

		var trade = new UserTrade
		{
			EventType = (string)dataObj["e"] ?? "executionReport",
			EventTime = ToDateTime(dataObj["E"]),
			Symbol = order.Symbol,
			TradeId = dataObj["t"]?.To<long?>() ?? 0,
			OrderId = order.OrderId,
			Price = ToDouble(dataObj["L"] ?? dataObj["p"]),
			Quantity = ToDouble(dataObj["l"] ?? dataObj["z"]),
			ClientOrderId = order.ClientOrderId,
			Commission = ToDouble(dataObj["n"]),
			CommissionAsset = (string)dataObj["N"],
			TradeTime = ToDateTime(dataObj["T"] ?? dataObj["E"]),
			IsMaker = dataObj["m"]?.To<bool?>() ?? false,
		};

		await tradeHandler([trade], cancellationToken);
	}

	private async ValueTask ProcessLegacyEvent(string eventType, JObject obj, CancellationToken cancellationToken)
	{
		switch (eventType)
		{
			case "24hrTicker":
				if (TickerReceived is { } tickerHandler)
					await tickerHandler(obj.DeserializeObject<Ticker>(), cancellationToken);
				break;
			case "depthUpdate":
				if (OrderBookReceived is { } obHandler)
					await obHandler(obj.DeserializeObject<OrderBook>(), cancellationToken);
				break;
			case "trade":
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(obj.DeserializeObject<Trade>(), cancellationToken);
				break;
			case "kline":
				var klineData = obj["k"] as JObject;
				if (klineData is not null && CandleReceived is { } candleHandler)
					await candleHandler(klineData.DeserializeObject<Candle>(), cancellationToken);
				break;
			case "outboundAccountPosition":
				if (obj["B"] is JArray balancesArr && BalancesReceived is { } balanceHandler)
				{
					var balances = balancesArr.Select(b => b.DeserializeObject<Balance>());
					await balanceHandler(balances, cancellationToken);
				}
				break;
			case "executionReport":
				await ProcessExecutionReport(obj, cancellationToken);
				break;
			default:
				this.AddVerboseLog("Unknown event type: {0}", eventType);
				break;
		}
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
		var closeTime = ToDateTime(data["T"] ?? data["E"]);
		var openTime = ToDateTime(data["t"]);

		if (openTime == default && closeTime != default)
			openTime = closeTime - GetIntervalTimeSpan(interval);

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
			Volume = ToDouble(data["v"]) ?? 0,
			IsClosed = data["x"]?.To<bool?>() ?? false,
		};
	}

	private static double[][] ParseDepthLevels(JToken token)
	{
		if (token is not JArray levels || levels.Count == 0)
			return [];

		var result = new List<double[]>();

		foreach (var level in levels)
		{
			double? price = null;
			double? volume = null;

			if (level is JArray arr)
			{
				price = ToDouble(arr.ElementAtOrDefault(0));
				volume = ToDouble(arr.ElementAtOrDefault(1));
			}
			else if (level is JObject obj)
			{
				price = ToDouble(obj["p"] ?? obj["price"]);
				volume = ToDouble(obj["a"] ?? obj["q"] ?? obj["quantity"] ?? obj["v"]);
			}

			if (price is null || volume is null)
				continue;

			result.Add([price.Value, volume.Value]);
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

	public ValueTask Ping(CancellationToken cancellationToken)
		=> _client?.SendAsync("ping", cancellationToken) ?? default;
}
