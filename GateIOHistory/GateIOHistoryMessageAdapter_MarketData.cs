namespace StockSharp.GateIOHistory;

using System.IO;
using System.IO.Compression;
using System.Text;

using Ecng.IO;
using Ecng.Logging;

using StockSharp.Connectors.Common;

partial class GateIOHistoryMessageAdapter
{
	private const string _bucketName = "gateio-public-data";
	private const string _bucketRegion = "ap-northeast-1";
	private const string _baseUrl = "https://download.gatedata.org/";

	private readonly SynchronizedDictionary<string, (DateTime till, (DateTime, DateTime)? range)> _rangeCache = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HttpClient _client = new();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);

		var secCodeLike = lookupMsg.SecurityId.SecurityCode;
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		var endpoints = new Dictionary<string, (SecurityTypes, string)>
		{
			{ "spot/currency_pairs", (SecurityTypes.CryptoCurrency, BoardCodes.GateIO) },
			{ "futures/usdt/contracts", (SecurityTypes.Future, BoardCodes.GateIOPerpetual) },
			{ "futures/btc/contracts", (SecurityTypes.Future, BoardCodes.GateIOPerpetual) },
			{ "delivery/usdt/contracts", (SecurityTypes.Future, BoardCodes.GateIODelivery) },
		};

		foreach (var (endpoint, (secType, boardCode)) in endpoints)
		{
			if (!secTypes.IsEmpty() && !secTypes.Contains(secType))
				continue;

			try
			{
				var json = await _client.GetStringAsync($"https://api.gateio.ws/api/v4/{endpoint}", token);

				dynamic items = json.DeserializeObject<object>();

				foreach (var item in items)
				{
					token.ThrowIfCancellationRequested();

					string symbol;
					decimal? tickSize = null;
					decimal? lotSize = null;

					if (endpoint.StartsWith("spot"))
					{
						symbol = (string)item.id;
						tickSize = ((string)item.precision)?.To<int?>() is { } prec ? (decimal)Math.Pow(10, -prec) : null;
						lotSize = ((string)item.amount_precision)?.To<int?>() is { } aprec ? (decimal)Math.Pow(10, -aprec) : null;
					}
					else
					{
						symbol = (string)item.name;
						tickSize = ((string)item.order_price_round)?.To<decimal?>();
						lotSize = ((string)item.order_size_min)?.To<decimal?>();
					}

					if (!secCodeLike.IsEmpty() && !symbol.ContainsIgnoreCase(secCodeLike))
						continue;

					var secMsg = new SecurityMessage
					{
						SecurityId = new() { SecurityCode = symbol, BoardCode = boardCode },
						SecurityType = secType,
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
			catch (Exception ex)
			{
				if (token.IsCancellationRequested)
					break;
				this.AddErrorLog(ex);
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, token);
	}

	private static string GetBizType(string boardCode, string secCode)
	{
		if (boardCode.EqualsIgnoreCase(BoardCodes.GateIO))
			return "spot";

		if (boardCode.EqualsIgnoreCase(BoardCodes.GateIODelivery))
			return "futures_usdt";

		// For perpetual futures, determine BTC or USDT based on symbol
		// BTC_USD -> futures_btc, BTC_USDT -> futures_usdt
		if (secCode.EndsWithIgnoreCase("_USD") && !secCode.EndsWithIgnoreCase("_USDT"))
			return "futures_btc";

		return "futures_usdt";
	}

	private async ValueTask<(DateTime min, DateTime max)?> GetRange(string prefix, CancellationToken cancellationToken)
	{
		if (_rangeCache.TryGetValue(prefix, out var t) && t.till > DateTime.UtcNow)
			return t.range;

		try
		{
			var range = await S3BucketHelper.GetDateRangeFromFoldersAsync(
				_client, _bucketName, _bucketRegion, prefix, "yyyyMM", cancellationToken);

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
		Stream networkStream;

		try
		{
			networkStream = await _client.GetStreamAsync($"{_baseUrl}{urlPart}", cancellationToken);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return true;
		}

		return await Do.InvariantAsync(async () =>
		{
			await using (networkStream)
			await using (var gzStream = new GZipStream(networkStream, CompressionMode.Decompress))
			{
				var reader = new FastCsvReader(gzStream, Encoding.UTF8, StringHelper.N)
				{
					ColumnSeparator = ','
				};

				while (await reader.NextLineAsync(cancellationToken))
				{
					try
					{
						if (!await converter(reader))
							return false;
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException(LocalizedStrings.FileNotParsedLineError.Put(urlPart, reader.CurrentLine), ex);
					}
				}

				return true;
			}
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
		var bizType = GetBizType(boardCode, secCode);
		var dataType = bizType == "spot" ? "deals" : "trades";

		var prefix = $"{bizType}/{dataType}/";

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

		// Gate.IO provides monthly archives
		var startMonth = new DateTime(from2.Year, from2.Month, 1).UtcKind();
		var endMonth = new DateTime(to2.Year, to2.Month, 1).UtcKind();

		for (var month = startMonth; month <= endMonth; month = month.AddMonths(1))
		{
			var yearMonth = month.ToString("yyyyMM");

			if (!await Process($"{bizType}/{dataType}/{yearMonth}/{secCode}-{yearMonth}.csv.gz", async reader =>
			{
				if (reader.ColumnCount < 4)
					return true;

				// Spot format: timestamp, dealid, price, amount, side (1:sell, 2:buy)
				// Futures format: timestamp, tradeid, price, signed_amount (positive=buy, negative=sell)
				var ts = reader.ReadDouble();
				var tradeId = reader.ReadLong();
				var price = reader.ReadDecimal();
				var volume = reader.ReadDecimal();

				Sides? side;
				if (reader.ColumnCount >= 5)
				{
					// Spot: side is in separate column
					var sideCode = reader.ReadInt();
					side = sideCode == 2 ? Sides.Buy : (sideCode == 1 ? Sides.Sell : null);
				}
				else
				{
					// Futures: side is determined by volume sign
					side = volume >= 0 ? Sides.Buy : Sides.Sell;
					volume = volume.Abs();
				}

				var time = ts.FromUnix();
				if (time < from)
					return true;
				if (time > to)
					return false;

				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = transId,
					DataTypeEx = DataType.Ticks,
					TradeId = tradeId,
					TradePrice = price,
					TradeVolume = volume,
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
		var bizType = GetBizType(boardCode, secCode);

		var prefix = $"{bizType}/orderbooks/";

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

		// Truncate to hour boundaries
		var startHour = new DateTime(from2.Year, from2.Month, from2.Day, from2.Hour, 0, 0, DateTimeKind.Utc);
		var endHour = new DateTime(to2.Year, to2.Month, to2.Day, to2.Hour, 0, 0, DateTimeKind.Utc);

		// Order book state - accumulated across all batches
		var bidsState = new Dictionary<decimal, decimal>();  // price -> volume
		var asksState = new Dictionary<decimal, decimal>();  // price -> volume

		// Current batch changes (to send as message)
		var bidsChanges = new Dictionary<decimal, decimal>();
		var asksChanges = new Dictionary<decimal, decimal>();

		long? currentBeginId = null;
		DateTime? currentTime = null;
		var isFirstBatch = true;

		async ValueTask flushBookAsync(DateTime fallbackTime)
		{
			if (bidsChanges.Count == 0 && asksChanges.Count == 0)
				return;

			// Convert to QuoteChange arrays, sorted appropriately
			// Bids: descending by price, Asks: ascending by price
			var bidsList = bidsChanges
				.OrderByDescending(kv => kv.Key)
				.Select(kv => new QuoteChange(kv.Key, kv.Value))
				.ToArray();

			var asksList = asksChanges
				.OrderBy(kv => kv.Key)
				.Select(kv => new QuoteChange(kv.Key, kv.Value))
				.ToArray();

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = mdMsg.SecurityId,
				OriginalTransactionId = transId,
				ServerTime = currentTime ?? fallbackTime,
				Bids = bidsList,
				Asks = asksList,
				State = isFirstBatch ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
			}, cancellationToken);

			bidsChanges.Clear();
			asksChanges.Clear();
			isFirstBatch = false;
		}

		for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
		{
			var yearMonth = hour.ToString("yyyyMM");
			var hourStr = hour.ToString("yyyyMMddHH");

			if (!await Process($"{bizType}/orderbooks/{yearMonth}/{secCode}-{hourStr}.csv.gz", async reader =>
			{
				if (reader.ColumnCount < 6)
					return true;

				// Spot format (7 columns): timestamp, side (1=Sell/Ask, 2=Buy/Bid), action, price, amount, begin_id, merged
				// Futures format (6 columns): timestamp, action, price, size, begin_id, merged (side determined by size sign)
				var isSpotFormat = reader.ColumnCount >= 7;

				var ts = reader.ReadDouble();
				int? sideCode = isSpotFormat ? reader.ReadInt() : null;
				var action = reader.ReadString();
				var price = reader.ReadDecimal();
				var size = reader.ReadDecimal();
				var beginId = reader.ReadLong();

				var time = ts.FromUnix();
				if (time < from)
					return true;
				if (time > to)
				{
					await flushBookAsync(time);
					return false;
				}

				// Check if begin_id changed - means end of current batch
				if (currentBeginId != beginId)
				{
					// Flush previous batch (if any)
					var hadData = bidsChanges.Count > 0 || asksChanges.Count > 0;
					await flushBookAsync(time);

					// Only decrement left after flushing actual data
					if (hadData && --left <= 0)
						return false;

					currentBeginId = beginId;
					currentTime = time;
				}

				// Determine side:
				// Spot: side column (1=Sell/Ask, 2=Buy/Bid)
				// Futures: side determined by size sign (positive=Bid/long, negative=Ask/short)
				bool isBid;
				decimal amount;

				if (isSpotFormat)
				{
					isBid = sideCode == 2;
					amount = size;
				}
				else
				{
					// For futures: positive size = Bid (long), negative size = Ask (short)
					if (size == 0)
						return true;

					isBid = size > 0;
					amount = size.Abs();
				}

				// Select state and changes dictionaries based on side
				var state = isBid ? bidsState : asksState;
				var changes = isBid ? bidsChanges : asksChanges;

				// Apply action according to gate_io.txt format:
				// set = base quantity (set absolute value)
				// take = reduce quantity (subtract from current)
				// make = increase quantity (add to current)
				decimal newVolume;

				if (action.EqualsIgnoreCase("set"))
				{
					newVolume = amount;
				}
				else if (action.EqualsIgnoreCase("take"))
				{
					var currentVolume = state.TryGetValue(price, out var v) ? v : 0;
					newVolume = (currentVolume - amount).Max(0);
				}
				else if (action.EqualsIgnoreCase("make"))
				{
					var currentVolume = state.TryGetValue(price, out var v) ? v : 0;
					newVolume = currentVolume + amount;
				}
				else
				{
					// Unknown action - skip
					return true;
				}

				// Update state
				if (newVolume > 0)
					state[price] = newVolume;
				else
					state.Remove(price);

				// Record the change (new volume, or 0 if removed)
				changes[price] = newVolume;

				return true;
			}, cancellationToken))
				break;

			if (left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await flushBookAsync(DateTime.UtcNow);

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
		var bizType = GetBizType(boardCode, secCode);
		var tf = mdMsg.GetTimeFrame();
		var tfNative = tf.ToNative();

		var prefix = $"{bizType}/candlesticks_{tfNative}/";

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

		// Gate.IO: Spot uses daily files for small TFs (< 1h), monthly for large TFs
		// Futures always use monthly files regardless of TF
		var useDaily = bizType == "spot" && tf < TimeSpan.FromHours(1);

        async ValueTask<bool> processCandle(FastCsvReader reader)
        {
            if (reader.ColumnCount < 6)
                return true;

            // Format: timestamp, size, close, high, low, open
            var ts = reader.ReadLong();
            var volume = reader.ReadDecimal();
            var close = reader.ReadDecimal();
            var high = reader.ReadDecimal();
            var low = reader.ReadDecimal();
            var open = reader.ReadDecimal();

            var time = ts.FromUnix(true);
            if (time < from)
                return true;
            if (time > to)
                return false;

            await SendOutMessageAsync(new TimeFrameCandleMessage
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
            }, cancellationToken);

            return --left > 0;
        }

        if (useDaily)
		{
			for (var date = from2.Date; date <= to2.Date; date = date.AddDays(1))
			{
				var yearMonth = date.ToString("yyyyMM");
				var dateStr = date.ToString("yyyyMMdd");

				if (!await Process($"{bizType}/candlesticks_{tfNative}/{yearMonth}/{secCode}-{dateStr}.csv.gz", processCandle, cancellationToken))
					break;

				if (left <= 0)
					break;

				await IterationInterval.Delay(cancellationToken);
			}
		}
		else
		{
			var startMonth = new DateTime(from2.Year, from2.Month, 1).UtcKind();
			var endMonth = new DateTime(to2.Year, to2.Month, 1).UtcKind();

			for (var month = startMonth; month <= endMonth; month = month.AddMonths(1))
			{
				var yearMonth = month.ToString("yyyyMM");

				if (!await Process($"{bizType}/candlesticks_{tfNative}/{yearMonth}/{secCode}-{yearMonth}.csv.gz", processCandle, cancellationToken))
					break;

				if (left <= 0)
					break;

				await IterationInterval.Delay(cancellationToken);
			}
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}
