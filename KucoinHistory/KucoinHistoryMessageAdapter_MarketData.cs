namespace StockSharp.KucoinHistory;

using System.IO;
using System.Text;

using Ecng.IO;
using Ecng.IO.Compression;
using Ecng.Logging;

using StockSharp.Connectors.Common;

partial class KucoinHistoryMessageAdapter
{
	private const string _bucketName = "k-line-history-data";
	private const string _bucketRegion = "ap-northeast-1";
	private const string _baseUrl = "https://historical-data.kucoin.com/";

	private readonly SynchronizedDictionary<string, (DateTime till, (DateTime, DateTime)? range)> _rangeCache = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HttpClient _client = new();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);

		var secCodeLike = lookupMsg.SecurityId.SecurityCode;
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		// Use API for securities lookup
		var url = $"https://{ApiAddress}/api/v1/symbols";

		try
		{
			var json = await _client.GetStringAsync(url, token);

			dynamic response = json.DeserializeObject<object>();

			if ((string)response.code != "200000")
			{
				this.AddErrorLog("Kucoin API error: {0}", (string)response.msg);
			}
			else
			{
				foreach (var item in response.data)
				{
					token.ThrowIfCancellationRequested();

					string symbol = (string)item.symbol; // e.g. "BTC-USDT"
					var tickSize = ((string)item.priceIncrement)?.To<decimal?>();
					var lotSize = ((string)item.baseIncrement)?.To<decimal?>();

					if (!secCodeLike.IsEmpty() && !symbol.ContainsIgnoreCase(secCodeLike))
						continue;

					var secMsg = new SecurityMessage
					{
						SecurityId = new() { SecurityCode = symbol, BoardCode = BoardCodes.Kucoin },
						SecurityType = SecurityTypes.CryptoCurrency,
						OriginalTransactionId = lookupMsg.TransactionId,
						PriceStep = tickSize,
						MinVolume = lotSize,
						VolumeStep = lotSize,
					};

					if (!secMsg.IsMatch(lookupMsg, secTypes))
						continue;

					await SendOutMessageAsync(secMsg, token);

					if (--left <= 0)
						break;
				}
			}
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
				this.AddErrorLog(ex);
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, token);
	}

	private static string GetMarket(string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.KucoinFT) ? "futures" : "spot";

	private async ValueTask<(DateTime min, DateTime max)?> GetRange(string prefix, CancellationToken cancellationToken)
	{
		if (_rangeCache.TryGetValue(prefix, out var t) && t.till > DateTime.UtcNow)
			return t.range;

		try
		{
			var range = await S3BucketHelper.GetDateRangeFromFilesAsync(
				_client, _bucketName, _bucketRegion, prefix, "yyyy-MM-dd", cancellationToken, _baseUrl);

			_rangeCache[prefix] = (DateTime.UtcNow.AddHours(1), range);
			return range;
		}
		catch (Exception ex)
		{
			if (cancellationToken.IsCancellationRequested)
				throw;

			this.AddWarningLog("Cannot detect date range: {0}", ex);
			return (default, default);
		}
	}

	private async Task<bool> Process(string urlPart, Func<FastCsvReader, ValueTask<bool>> converter, CancellationToken cancellationToken)
	{
		Stream zipStream;

		try
		{
			zipStream = await _client.GetStreamAsync($"{_baseUrl}{urlPart}", cancellationToken);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return true;
		}

		return await Do.InvariantAsync(async () =>
		{
			foreach (var (name, item) in zipStream.Unzip())
			{
				var reader = new FastCsvReader(item, Encoding.UTF8, StringHelper.N)
				{
					ColumnSeparator = ','
				};

				// Skip header row if present
				if (!await reader.NextLineAsync(cancellationToken))
					continue;

				if (reader.CurrentLine is null || !char.IsDigit(reader.CurrentLine[0]))
				{
					if (!await reader.NextLineAsync(cancellationToken))
						continue;
				}

				do
				{
					try
					{
						if (!await converter(reader))
							return false;
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException(LocalizedStrings.FileNotParsedLineError.Put(name, reader.CurrentLine), ex);
					}
				}
				while (await reader.NextLineAsync(cancellationToken));
			}

			return true;
		});
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? DateTime.MinValue;
		var left = mdMsg.Count ?? long.MaxValue;

		var secCode = mdMsg.SecurityId.SecurityCode;
		var boardCode = mdMsg.SecurityId.BoardCode;
		var market = GetMarket(boardCode);
		var archiveSymbol = secCode.ToArchiveSymbol();

		var prefix = $"data/{market}/daily/trades/{archiveSymbol}/{archiveSymbol}-trades-";

		var from2 = from;
		var to2 = to;

		if (CheckDates)
		{
			var range = await GetRange(prefix, cancellationToken);

			if (range is null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			if (range.Value != default)
			{
				from2 = range.Value.min.Max(from2);
				to2 = range.Value.max.Min(to2);
			}
		}

		foreach (var date in from2.Date.Range(to2.Date, TimeSpan.FromDays(1)))
		{
			if (!await Process($"data/{market}/daily/trades/{archiveSymbol}/{archiveSymbol}-trades-{date:yyyy-MM-dd}.zip", async reader =>
			{
				if (reader.ColumnCount < 5)
					return true;

				// Format: trade_id,trade_time,price,size,side
				var tradeId = reader.ReadString();
				var tradeTime = reader.ReadLong(); // milliseconds
				var price = reader.ReadDecimal();
				var size = reader.ReadDecimal();
				var sideStr = reader.ReadString();

				var time = tradeTime.FromUnixAuto();
				if (time < from)
					return true;
				if (time > to)
					return false;

				var side = sideStr.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;

				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = transId,
					DataTypeEx = DataType.Ticks,
					TradeStringId = tradeId,
					TradePrice = price,
					TradeVolume = size,
					ServerTime = time,
					OriginSide = side,
				}, cancellationToken);

				return --left > 0;
			}, cancellationToken))
				break;

			if (left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? DateTime.MinValue;
		var left = mdMsg.Count ?? long.MaxValue;

		var secCode = mdMsg.SecurityId.SecurityCode;
		var boardCode = mdMsg.SecurityId.BoardCode;
		var market = GetMarket(boardCode);
		var depthSymbol = secCode.ToDepthSymbol();

		var prefix = $"data/{market}/daily/depth/orderbooklv50/{depthSymbol}/{depthSymbol}-orderbooklv50-";

		var from2 = from;
		var to2 = to;

		if (CheckDates)
		{
			var range = await GetRange(prefix, cancellationToken);

			if (range is null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			if (range.Value != default)
			{
				from2 = range.Value.min.Max(from2);
				to2 = range.Value.max.Min(to2);
			}
		}

		foreach (var date in from2.Date.Range(to2.Date, TimeSpan.FromDays(1)))
		{
			Stream zipStream;

			try
			{
				zipStream = await _client.GetStreamAsync($"{_baseUrl}data/{market}/daily/depth/orderbooklv50/{depthSymbol}/{depthSymbol}-orderbooklv50-{date:yyyy-MM-dd}.zip", cancellationToken);
			}
			catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				continue;
			}

			// Collect all order books for this day then sort
			var dayOrderBooks = new List<QuoteChangeMessage>();

			foreach (var (name, item) in zipStream.Unzip())
			{
				using var streamReader = new StreamReader(item, Encoding.UTF8);

				// First line is "data" header
				await streamReader.ReadLineAsync(cancellationToken);

				string line;
				while ((line = await streamReader.ReadLineAsync(cancellationToken)) != null)
				{
					if (line.IsEmptyOrWhiteSpace())
						continue;

					try
					{
						dynamic snapshot = line.DeserializeObject<object>();

						var time = ((long)snapshot.timestamp).FromUnixAuto();

						if (time < from)
							continue;

						if (time > to)
							continue;

						var bids = new List<QuoteChange>();
						var asks = new List<QuoteChange>();

						foreach (var bid in snapshot.bids)
						{
							var price = ((string)bid[0]).To<decimal>();
							var volume = ((string)bid[1]).To<decimal>();
							bids.Add(new QuoteChange(price, volume));
						}

						foreach (var ask in snapshot.asks)
						{
							var price = ((string)ask[0]).To<decimal>();
							var volume = ((string)ask[1]).To<decimal>();
							asks.Add(new QuoteChange(price, volume));
						}

						dayOrderBooks.Add(new QuoteChangeMessage
						{
							SecurityId = mdMsg.SecurityId,
							OriginalTransactionId = transId,
							ServerTime = time,
							Bids = [.. bids],
							Asks = [.. asks],
							State = QuoteChangeStates.SnapshotComplete,
						});

						// Early exit if we have enough
						if (dayOrderBooks.Count >= left)
							break;
					}
					catch (Exception ex)
					{
						this.AddWarningLog("Failed to parse orderbook line: {0}", ex.Message);
					}
				}

				// Early exit if we have enough
				if (dayOrderBooks.Count >= left)
					break;
			}

			// Sort by time and send
			foreach (var quote in dayOrderBooks.OrderBy(q => q.ServerTime))
			{
				await SendOutMessageAsync(quote, cancellationToken);
				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? DateTime.MinValue;
		var left = mdMsg.Count ?? long.MaxValue;

		var secCode = mdMsg.SecurityId.SecurityCode;
		var boardCode = mdMsg.SecurityId.BoardCode;
		var market = GetMarket(boardCode);
		var archiveSymbol = secCode.ToArchiveSymbol();
		var tf = mdMsg.GetTimeFrame();
		var tfNative = tf.ToNative();

		var prefix = $"data/{market}/daily/klines/{archiveSymbol}/{tfNative}/{archiveSymbol}-{tfNative}-";

		var from2 = from;
		var to2 = to;

		if (CheckDates)
		{
			var range = await GetRange(prefix, cancellationToken);

			if (range is null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			if (range.Value != default)
			{
				from2 = range.Value.min.Max(from2);
				to2 = range.Value.max.Min(to2);
			}
		}

		foreach (var date in from2.Date.Range(to2.Date, TimeSpan.FromDays(1)))
		{
			// Collect all candles for this day then sort
			var dayCandles = new List<TimeFrameCandleMessage>();

			var isFutures = market == "futures";

			if (!await Process($"data/{market}/daily/klines/{archiveSymbol}/{tfNative}/{archiveSymbol}-{tfNative}-{date:yyyy-MM-dd}.zip", async reader =>
			{
				// Futures 1d files may have only 5 columns (no volume), Spot files have 6-7 columns
				if (reader.ColumnCount < 5)
					return true;

				var ts = reader.ReadLong();
				var open = reader.ReadDecimal();

				// Spot format: time,open,close,high,low,volume,turnover (OCHL)
				// Futures format: time,open,high,low,close,volume (OHLC)
				decimal high, low, close;
				if (isFutures)
				{
					high = reader.ReadDecimal();
					low = reader.ReadDecimal();
					close = reader.ReadDecimal();
				}
				else
				{
					close = reader.ReadDecimal();
					high = reader.ReadDecimal();
					low = reader.ReadDecimal();
				}

				// Volume may be missing in some files (e.g., Futures 1d)
				var volume = reader.ColumnCount > 5 ? reader.ReadDecimal() : 0m;

				var time = ts.FromUnixAuto();
				if (time < from)
					return true;
				if (time > to)
					return false;

				dayCandles.Add(new TimeFrameCandleMessage
				{
					OriginalTransactionId = transId,
					DataType = mdMsg.DataType2,
					OpenTime = time,
					CloseTime = time + tf,
					OpenPrice = open,
					HighPrice = high,
					LowPrice = low,
					ClosePrice = close,
					TotalVolume = volume,
					State = CandleStates.Finished,
				});

				return true;
			}, cancellationToken))
			{
				// converter returned false (time > to) - stop processing more days
				// Sort and send candles collected so far
				foreach (var candle in dayCandles.OrderBy(c => c.OpenTime))
				{
					await SendOutMessageAsync(candle, cancellationToken);
					if (--left <= 0)
						break;
				}
				break;
			}

			// File processed successfully or not found (404) - send candles and continue to next day
			foreach (var candle in dayCandles.OrderBy(c => c.OpenTime))
			{
				await SendOutMessageAsync(candle, cancellationToken);
				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}
