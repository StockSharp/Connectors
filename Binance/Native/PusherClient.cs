namespace StockSharp.Binance.Native;

class PusherClient : BaseLogReceiver
{
	private abstract class BasePusherClient : BaseLogReceiver
	{
		private readonly WebSocketClient _client;

		protected BinanceSections Section { get; }
		protected PusherClient Client { get; }

		protected BasePusherClient(PusherClient client, WorkingTime workingTime, BinanceSections section, string path)
		{
			Section = section;
			Parent = Client = client ?? throw new ArgumentNullException(nameof(client));

			_client = new(
				GetUrl() + "/" + path.ThrowIfEmpty(nameof(path)),
				(state, token) =>
				{
					// this states controlled by BinanceMessageAdapter
					if (state == ConnectionStates.Connected || state == ConnectionStates.Disconnected)
						return default;

					if (Client.StateChanged is { } handler)
						return handler(state, token);

					return default;
				},
				(error, token) =>
				{
					this.AddErrorLog(error);

					if (Client.Error is { } handler)
						return handler(error, token);

					return default;
				},
				OnParse,
				(s, a) => this.AddInfoLog(s, a),
				(s, a) => this.AddErrorLog(s, a),
				(s, a) => this.AddVerboseLog(s, a))
			{
				ReconnectAttempts = ((BinanceMessageAdapter)Client.Parent).ReConnectionSettings.ReAttemptCount,
				WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			};
		}

		// to get readable name after obfuscation
		public override string Name => Client.Name + Section;

		private string GetUrl()
		{
			var adapter = (BinanceMessageAdapter)Client.Parent;
			var isDemo = adapter.IsDemo;

			var hostSpot = adapter.HostWebSocketSpot;
			var hostFuture = adapter.HostWebSocketFuture;
			var hostFutureCoin = adapter.HostWebSocketFutureCoin;

			switch (Section)
			{
				case BinanceSections.Spot:
					if (isDemo)
						return "wss://testnet.binance.vision";

					hostSpot.CheckHostName(nameof(adapter.HostWebSocketSpot));
					return $"wss://{hostSpot}";

				case BinanceSections.Margin:
					if (isDemo)
						throw new NotSupportedException();

					hostSpot.CheckHostName(nameof(adapter.HostWebSocketSpot));
					return $"wss://{hostSpot}";

				case BinanceSections.Futures:
					if(isDemo)
						return "wss://stream.binancefuture.com";

					hostFuture.CheckHostName(nameof(adapter.HostWebSocketFuture));
					return $"wss://{hostFuture}";

				case BinanceSections.FuturesCoin:
					if(isDemo)
						return "wss://dstream.binancefuture.com";

					hostFutureCoin.CheckHostName(nameof(adapter.HostWebSocketFutureCoin));
					return $"wss://{hostFutureCoin}";

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public ValueTask Connect(CancellationToken cancellationToken)
		{
			this.AddInfoLog(LocalizedStrings.Connecting);
			return _client.ConnectAsync(cancellationToken);
		}

		public void Disconnect()
		{
			this.AddInfoLog(LocalizedStrings.Disconnecting);
			_client.Disconnect();
		}

		protected abstract ValueTask OnParse(WebSocketMessage msg, CancellationToken cancellationToken);
	}

	private class MarketDataPusherClient(PusherClient parent, WorkingTime workingTime, BinanceSections section, string path) : BasePusherClient(parent, workingTime, section, path)
	{
		// to get readable name after obfuscation
		public override string Name => base.Name + "_MarketData";

		protected override async ValueTask OnParse(WebSocketMessage msg, CancellationToken cancellationToken)
		{
			using var reader = msg.AsReader();

			string stream = null;

			while (reader.Read())
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();

					if (propertyName == "stream")
					{
						reader.Read();
						stream = reader.Value.ToString();
					}
					else if (propertyName == "data")
					{
						reader.Read(); // Move to start of data object
						if (stream == null)
							throw new InvalidOperationException("Stream property not found before data");

						await ProcessData(reader, stream, cancellationToken);
						return; // Exit after processing data
					}
					else
						reader.Skip();
				}
			}

			throw new InvalidOperationException("Invalid JSON structure: data not found");
		}

		private static DateTime ConvertFromUnixTimeMls(long milliseconds)
		{
			return milliseconds.FromUnix(false);
		}

		private async ValueTask ProcessData(JsonTextReader reader, string stream, CancellationToken cancellationToken)
		{
			string streamName;

			if (stream == AllSymbolsStreams.BookTicker)
				streamName = stream;
			else
			{
				var parts = stream.SplitByAt();
				streamName = parts[1].EqualsIgnoreCase("arr") ? stream : parts[1];
			}

			switch (streamName)
			{
				case OneSymbolStreams.Ticker:
				case OneSymbolStreams.BookTicker:
				case AllSymbolsStreams.Ticker:
				case AllSymbolsStreams.BookTicker:
					await ProcessTicker(reader, cancellationToken);
					break;

				case OneSymbolStreams.Trades:
					await ProcessTrade(reader, cancellationToken);
					break;

				case OneSymbolStreams.OrderBook:
					await ProcessOrderBook(reader, cancellationToken);
					break;

				case OneSymbolStreams.OrderLog:
				case AllSymbolsStreams.OrderLog:
					await ProcessOrderLog(reader, cancellationToken);
					break;

				case var _ when streamName.StartsWithIgnoreCase(OneSymbolStreams.Candles):
					await ProcessCandle(reader, cancellationToken);
					break;

				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, streamName);
					break;
			}
		}

		private static void DeserializeBase<T>(T evt, string propertyName, JsonTextReader reader)
			where T : BaseEvent
		{
			switch (propertyName)
			{
				case "E": evt.EventTime = ConvertFromUnixTimeMls(reader.Value.To<long>()); break;
				default: reader.Skip(); break;
			}
		}

		private async ValueTask ProcessTicker(JsonTextReader reader, CancellationToken cancellationToken)
		{
			if (reader.TokenType == JsonToken.StartArray)
			{
				if (Client.TickerChanged is { } handler)
				{
					while (reader.Read() && reader.TokenType != JsonToken.EndArray)
					{
						var ticker = DeserializeTicker(reader);
						await handler(Section, ticker, cancellationToken);
					}
				}
			}
			else
			{
				var ticker = DeserializeTicker(reader);

				if (Client.TickerChanged is { } handler)
					await handler(Section, ticker, cancellationToken);
			}
		}

		private static Ticker DeserializeTicker(JsonTextReader reader)
		{
			var ticker = new Ticker();

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();
					reader.Read();

					switch (propertyName)
					{
						case "s": ticker.Symbol = reader.Value.ToString(); break;
						case "p": ticker.PriceChange = reader.Value.To<double>(); break;
						case "P": ticker.PriceChangePercentage = reader.Value.To<double>(); break;
						case "w": ticker.VWAP = reader.Value.To<double>(); break;
						case "x": ticker.PrevClose = reader.Value.To<double>(); break;
						case "c": ticker.CurrClose = reader.Value.To<double>(); break;
						case "Q": ticker.CloseQuantity = reader.Value.To<double>(); break;
						case "b": ticker.BestBidPrice = reader.Value.To<double>(); break;
						case "B": ticker.BestBidQuantity = reader.Value.To<double>(); break;
						case "a": ticker.BestAskPrice = reader.Value.To<double>(); break;
						case "A": ticker.BestAskQuantity = reader.Value.To<double>(); break;
						case "o": ticker.Open = reader.Value.To<double>(); break;
						case "h": ticker.High = reader.Value.To<double>(); break;
						case "l": ticker.Low = reader.Value.To<double>(); break;
						case "v": ticker.AssetVolume = reader.Value.To<double>(); break;
						case "q": ticker.QuoteVolume = reader.Value.To<double>(); break;
						case "O": ticker.StatisticsOpenTime = ConvertFromUnixTimeMls(reader.Value.To<long>()); break;
						case "C": ticker.StatisticsCloseTime = ConvertFromUnixTimeMls(reader.Value.To<long>()); break;
						case "F": ticker.FirstTradeId = reader.Value.To<long>(); break;
						case "L": ticker.LastTradeId = reader.Value.To<long>(); break;
						case "n": ticker.TradesCount = reader.Value.To<int>(); break;
						default: DeserializeBase(ticker, propertyName, reader); break;
					}
				}
			}

			return ticker;
		}

		private async ValueTask ProcessTrade(JsonTextReader reader, CancellationToken cancellationToken)
		{
			var trade = new Trade();

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();
					reader.Read();

					switch (propertyName)
					{
						case "s": trade.Symbol = reader.Value.ToString(); break;
						case "t": trade.Id = reader.Value.To<long>(); break;
						case "p": trade.Price = reader.Value.To<double>(); break;
						case "q": trade.Quantity = reader.Value.To<double>(); break;
						case "b": trade.Buyer = reader.Value.To<long>(); break;
						case "a": trade.Seller = reader.Value.To<long>(); break;
						case "T": trade.Time = ConvertFromUnixTimeMls(reader.Value.To<long>()); break;
						case "m": trade.IsMarketMaker = reader.Value.To<bool>(); break;
						case "M": trade.Ignore = reader.Value.To<bool>(); break;
						case "X": trade.Source = reader.Value.ToString(); break;
						default: DeserializeBase(trade, propertyName, reader); break;
					}
				}
			}

			if (Client.NewTrade is { } handler)
				await handler(Section, trade, cancellationToken);
		}

		private async ValueTask ProcessOrderBook(JsonTextReader reader, CancellationToken cancellationToken)
		{
			var orderBook = new OrderBook();

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();
					reader.Read();

					switch (propertyName)
					{
						case "s": orderBook.Symbol = reader.Value.ToString(); break;
						case "U": orderBook.FirstUpdateId = reader.Value.To<long>(); break;
						case "u": orderBook.LastUpdateId = reader.Value.To<long>(); break;
						case "pu": orderBook.FutLastUpdateId = reader.Value.To<long>(); break;
						case "b": orderBook.Bids = ReadOrderBookEntries(reader); break;
						case "a": orderBook.Asks = ReadOrderBookEntries(reader); break;
						default: DeserializeBase(orderBook, propertyName, reader); break;
					}
				}
			}

			if (Client.OrderBookChanged is { } handler)
				await handler(Section, orderBook, cancellationToken);
		}

		private static OrderBookEntry[] ReadOrderBookEntries(JsonTextReader reader)
		{
			var entries = new List<OrderBookEntry>();

			while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			{
				if (reader.TokenType == JsonToken.StartArray)
				{
					reader.Read();
					var price = reader.Value.To<double>();

					reader.Read();
					var size = reader.Value.To<double>();

					entries.Add(new() { Price = price, Size = size });

					reader.Read(); // EndArray
				}
			}

			return [.. entries];
		}

		private async ValueTask ProcessOrderLog(JsonTextReader reader, CancellationToken cancellationToken)
		{
			var orderLog = new FutOrderLog();
			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();

					if (propertyName == "E")
					{
						reader.Read();
						orderLog.EventTime = ConvertFromUnixTimeMls(reader.Value.To<long>());
					}
					else if (propertyName == "o")
					{
						orderLog.Order = DeserializeFutOrderLogItem(reader);
					}
					else
						reader.Skip();
				}
			}

			if (Client.NewOrderLog is { } handler)
				await handler(Section, orderLog, cancellationToken);
		}

		private static FutOrderLogItem DeserializeFutOrderLogItem(JsonTextReader reader)
		{
			var item = new FutOrderLogItem();

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();
					reader.Read();

					switch (propertyName)
					{
						case "s": item.Symbol = reader.Value.ToString(); break;
						case "S": item.Side = reader.Value.ToString(); break;
						case "o": item.Type = reader.Value.ToString(); break;
						case "f": item.Tif = reader.Value.ToString(); break;
						case "q": item.Quantity = reader.Value.To<double>(); break;
						case "p": item.Price = reader.Value.To<double>(); break;
						case "ap": item.AveragePrice = reader.Value.To<double>(); break;
						case "X": item.Status = reader.Value.ToString(); break;
						case "l": item.LastTradeSize = reader.Value.To<double>(); break;
						case "z": item.AccumFilled = reader.Value.To<double>(); break;
						case "T": item.TradeTime = reader.Value != null ? ConvertFromUnixTimeMls(reader.Value.To<long>()) : null; break;
						default: reader.Skip(); break;
					}
				}
			}

			return item;
		}

		private async ValueTask ProcessCandle(JsonTextReader reader, CancellationToken cancellationToken)
		{
			var ohlc = new Ohlc();

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();

					if (propertyName == "k")
					{
						ohlc.Candle = DeserializeKLine(reader);
					}
					else if (propertyName == "s")
					{
						reader.Read();
						ohlc.Symbol = reader.Value.ToString();
					}
					else if (propertyName == "E")
					{
						reader.Read();
						ohlc.EventTime = ConvertFromUnixTimeMls(reader.Value.To<long>());
					}
					else
						reader.Skip();
				}
			}

			if (Client.NewCandle is { } handler)
				await handler(Section, ohlc, cancellationToken);
		}

		private static KLine DeserializeKLine(JsonTextReader reader)
		{
			var kline = new KLine();

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					var propertyName = reader.Value.ToString();
					reader.Read();

					switch (propertyName)
					{
						case "t": kline.StartTime = ConvertFromUnixTimeMls(reader.Value.To<long>()); break;
						case "T": kline.CloseTime = ConvertFromUnixTimeMls(reader.Value.To<long>()); break;
						case "s": kline.Symbol = reader.Value.ToString(); break;
						case "i": kline.Interval = reader.Value.ToString(); break;
						case "f": kline.FirstTradeId = reader.Value.To<long>(); break;
						case "L": kline.LastTradeId = reader.Value.To<long>(); break;
						case "o": kline.Open = reader.Value.To<decimal>(); break;
						case "c": kline.Close = reader.Value.To<decimal>(); break;
						case "h": kline.High = reader.Value.To<decimal>(); break;
						case "l": kline.Low = reader.Value.To<decimal>(); break;
						case "v": kline.AssetVolume = reader.Value.To<decimal>(); break;
						case "n": kline.TradesCount = reader.Value.To<int>(); break;
						case "x": kline.IsFormed = reader.Value.To<bool>(); break;
						case "q": kline.QuoteVolume = reader.Value.To<double>(); break;
						case "V": kline.TakerBuyAssetVolume = reader.Value != null ? reader.Value.To<decimal>() : (decimal?)null; break;
						case "Q": kline.TakerBuyQuoteVolume = reader.Value != null ? reader.Value.To<decimal>() : (decimal?)null; break;
						case "B": kline.Ignore = reader.Value.ToString(); break;
						default: reader.Skip(); break;
					}
				}
			}

			return kline;
		}
	}

	private class AuthPusherClient : BasePusherClient
	{
		private readonly string _isolatedSymbol;

		public AuthPusherClient(PusherClient parent, BinanceSections section, string isolatedSymbol, string path)
			: base(parent, parent._workingTime, section, path)
		{
			BinanceMessageAdapter.CheckSectionSymbol(section, isolatedSymbol);
			_isolatedSymbol = isolatedSymbol;
		}

		// to get readable name after obfuscation
		public override string Name => base.Name + "_Auth";

		protected override async ValueTask OnParse(WebSocketMessage msg, CancellationToken cancellationToken)
		{
			var obj = msg.AsObject();

			var stream = (string)obj.e;

			switch (stream.ToLowerInvariant())
			{
				case "executionreport":
				{
					if (Client.NewExecutionReport is { } handler)
						await handler(Section, _isolatedSymbol, ((JToken)obj).DeserializeObject<ExecutionReport>(), cancellationToken);
					break;
				}

				case "order_trade_update":
				{
					if (Client.NewExecutionReport is { } handler)
						await handler(Section, _isolatedSymbol, ((JToken)obj.o).DeserializeObject<ExecutionReport>(), cancellationToken);
					break;
				}

				case "outboundaccountposition":
				case "account_update":
				{
					if (Client.AccountUpdated is { } handler)
						await handler(Section, _isolatedSymbol, ((JToken)obj).DeserializeObject<AccountUpdate>(), cancellationToken);
					break;
				}

				case "listenkeyexpired":
				{
					this.AddErrorLog("listen key expired. will reconnect...");

					if (Client.StateChanged is { } handler)
						await handler(ConnectionStates.Failed, cancellationToken);
					break;
				}

				case "trade_lite":
					// TODO Not really required right now
					break;

				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, stream);
					break;
			}
		}
	}

	private readonly WorkingTime _workingTime;

	public PusherClient(WorkingTime workingTime)
	{
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Binance) + "_" + nameof(PusherClient);

	public event Func<BinanceSections, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<BinanceSections, Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<BinanceSections, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<BinanceSections, FutOrderLog, CancellationToken, ValueTask> NewOrderLog;
	public event Func<BinanceSections, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<BinanceSections, string, ExecutionReport, CancellationToken, ValueTask> NewExecutionReport;
	public event Func<BinanceSections, string, AccountUpdate, CancellationToken, ValueTask> AccountUpdated;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private class StreamInfo(BinanceSections section, string oneSymbolStream, string allSymbolsStream, PusherClient parent)
	{
		private readonly BinanceSections _section = section;
		private readonly string _oneSymbolStream = oneSymbolStream.ThrowIfEmpty(nameof(oneSymbolStream));
		private readonly string _allSymbolsStream = allSymbolsStream;
		private readonly PusherClient _parent = parent ?? throw new ArgumentNullException(nameof(parent));

		private readonly CachedSynchronizedSet<string> _subscribedStreams = new(StringComparer.InvariantCultureIgnoreCase);
		private DateTime? _lastSubscription;
		private bool _subscriptionsChanged;
		private readonly CachedSynchronizedSet<MarketDataPusherClient> _clients = [];

		private void DisconnectInternal()
		{
			foreach (var client in _clients.CopyAndClear())
				client.Disconnect();
		}

		public void Disconnect()
		{
			DisconnectInternal();

			using (_subscribedStreams.EnterScope())
			{
				_subscribedStreams.Clear();
				_lastSubscription = null;
				_subscriptionsChanged = false;
			}
		}

		public void Process(bool isSubscribe, string symbol)
		{
			var stream = $"{symbol?.ToLowerInvariant()}@{_oneSymbolStream}";

			using (_subscribedStreams.EnterScope())
			{
				if (isSubscribe)
				{
					if (!_subscribedStreams.TryAdd(stream))
						return;
				}
				else
				{
					if (!_subscribedStreams.Remove(stream))
						return;
				}

				_lastSubscription = DateTime.UtcNow;
				_subscriptionsChanged = true;
			}
		}

		public ValueTask ProcessSubscriptions(CancellationToken cancellationToken)
		{
			bool canConnect;

			using (_subscribedStreams.EnterScope())
			{
				if (!_subscriptionsChanged || _lastSubscription == null || (DateTime.UtcNow - _lastSubscription.Value).TotalSeconds < 5)
					return default;

				_subscriptionsChanged = false;
				canConnect = _subscribedStreams.Count > 0;
			}

			try
			{
				DisconnectInternal();
			}
			catch (Exception ex)
			{
				_parent.AddErrorLog(ex);
			}

			if (!canConnect)
				return default;

			var subscribedStreams = _subscribedStreams.Cache;

			ValueTask createClient(IEnumerable<string> streams)
			{
				var client = new MarketDataPusherClient(_parent, _parent._workingTime, _section, $"stream?streams={streams.Join("/")}");
				_clients.Add(client);
				return client.Connect(cancellationToken);
			}

			if (subscribedStreams.Length <= 20)
			{
				return createClient(subscribedStreams);
			}
			else
			{
				if (_allSymbolsStream.IsEmpty())
				{
					return subscribedStreams
						.Chunk(100)
						.Select(createClient)
						.WhenAll();
				}
				else
				{
					return createClient([_allSymbolsStream]);
				}
			}
		}
	}

	private readonly CachedSynchronizedDictionary<(BinanceSections section, string symbol), StreamInfo> _marketDataStreams = [];
	private readonly CachedSynchronizedDictionary<(BinanceSections section, string symbol), AuthPusherClient> _authClients = [];

	private void DisconnectMarketData()
	{
		foreach (var stream in _marketDataStreams.CachedValues)
			stream.Disconnect();

		_marketDataStreams.Clear();
	}

	private void DisconnectAuth(BinanceSections section, string isolatedSymbol)
	{
		if (_authClients.TryGetAndRemove((section, isolatedSymbol), out var client))
			client.Disconnect();
	}

	public void DisconnectAll()
	{
		DisconnectMarketData();

		foreach (var section in _authClients.CachedKeys)
		{
			DisconnectAuth(section.section, section.symbol);
		}
	}

	private static class OneSymbolStreams
	{
		public const string Ticker = "ticker";
		public const string BookTicker = "bookTicker";
		public const string Trades = "trade";
		public const string OrderBook = "depth";
		public const string Candles = "kline_";
		public const string OrderLog = "forceOrder";
	}

	private static class AllSymbolsStreams
	{
		public const string Ticker = "!ticker@arr";
		public const string BookTicker = "!bookTicker";
		public const string OrderLog = "!forceOrder@arr";
	}

	public void SubscribeTicker(BinanceSections section, string symbol)
		=> Process(true, section, symbol, OneSymbolStreams.Ticker, AllSymbolsStreams.Ticker);

	public void UnSubscribeTicker(BinanceSections section, string symbol)
		=> Process(false, section, symbol, OneSymbolStreams.Ticker, AllSymbolsStreams.Ticker);

	public void SubscribeBookTicker(BinanceSections section, string symbol)
		=> Process(true, section, symbol, OneSymbolStreams.BookTicker, section == BinanceSections.Spot ? string.Empty : AllSymbolsStreams.BookTicker);

	public void UnSubscribeBookTicker(BinanceSections section, string symbol)
		=> Process(false, section, symbol, OneSymbolStreams.BookTicker, section == BinanceSections.Spot ? string.Empty : AllSymbolsStreams.BookTicker);

	public void SubscribeTrades(BinanceSections section, string symbol)
		=> Process(true, section, symbol, OneSymbolStreams.Trades, string.Empty);

	public void UnSubscribeTrades(BinanceSections section, string symbol)
		=> Process(false, section, symbol, OneSymbolStreams.Trades, string.Empty);

	public void SubscribeOrderBook(BinanceSections section, string symbol, int mls)
		=> Process(true, section, symbol, $"{OneSymbolStreams.OrderBook}@{mls}ms", string.Empty);

	public void UnSubscribeOrderBook(BinanceSections section, string symbol, int mls)
		=> Process(false, section, symbol, $"{OneSymbolStreams.OrderBook}@{mls}ms", string.Empty);

	public void SubscribeOrderLog(BinanceSections section, string symbol)
		=> Process(true, section, symbol, OneSymbolStreams.OrderLog, AllSymbolsStreams.OrderLog);

	public void UnSubscribeOrderLog(BinanceSections section, string symbol)
		=> Process(false, section, symbol, OneSymbolStreams.OrderLog, AllSymbolsStreams.OrderLog);

	public void SubscribeCandles(BinanceSections section, string symbol, string timeFrame)
		=> Process(true, section, symbol, OneSymbolStreams.Candles + timeFrame, string.Empty);

	public void UnSubscribeCandles(BinanceSections section, string symbol, string timeFrame)
		=> Process(false, section, symbol, OneSymbolStreams.Candles + timeFrame, string.Empty);

	public bool IsAccountSubscribed(BinanceSections section, string isolatedSymbol)
		=> _authClients.ContainsKey((section, isolatedSymbol));

	public async Task SubscribeAccount(BinanceSections section, string isolatedSymbol, string listenKey, CancellationToken cancellationToken)
	{
		if (_authClients.ContainsKey((section, isolatedSymbol)))
			throw new InvalidOperationException($"connection ({section}, {isolatedSymbol}) was not disconnected");

		var client = new AuthPusherClient(this, section, isolatedSymbol, "ws/" + listenKey);
		await client.Connect(cancellationToken);
		_authClients.Add((section, isolatedSymbol), client);
	}

	public void UnSubscribeAccount(BinanceSections section, string isolatedSymbol)
		=> DisconnectAuth(section, isolatedSymbol);

	private void Process(bool isSubscribe, BinanceSections section, string symbol, string oneSymbolStream, string allSymbolsStream)
	{
		if (oneSymbolStream.IsEmpty())
			throw new ArgumentNullException(nameof(oneSymbolStream));

		_marketDataStreams.SafeAdd((section, oneSymbolStream), key => new(key.section, key.Item2, allSymbolsStream, this)).Process(isSubscribe, symbol);
	}

	public ValueTask ProcessSubscriptions(CancellationToken cancellationToken)
		=> _marketDataStreams
			.CachedValues
			.Select(stream => stream.ProcessSubscriptions(cancellationToken))
			.WhenAll();

	protected override void DisposeManaged()
	{
		DisconnectAll();
		base.DisposeManaged();
	}
}
