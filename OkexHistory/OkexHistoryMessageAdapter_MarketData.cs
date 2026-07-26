namespace StockSharp.OkexHistory;

using System.Text;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Globalization;
using System.Linq;

using Ecng.IO;
using Ecng.IO.Compression;
using Ecng.Logging;

using StockSharp.OkexHistory.Native;

partial class OkexHistoryMessageAdapter
{
	private readonly SynchronizedDictionary<string, (DateTime till, SynchronizedSet<DateTime> dates)> _datesCache = new(StringComparer.InvariantCultureIgnoreCase);

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
			{ "SPOT", (SecurityTypes.CryptoCurrency, BoardCodes.Okex) },
			{ "SWAP", (SecurityTypes.Swap, BoardCodes.Okex) },
			{ "FUTURES", (SecurityTypes.Future, BoardCodes.Okex) },
		};

		foreach (var (nativeType, (secType, boardCode)) in endpoints)
		{
			if (!secTypes.IsEmpty() && !secTypes.Contains(secType))
				continue;

			try
			{
				var json = await _client.GetStringAsync($"https://{Address}/api/v5/public/instruments?instType={nativeType}", token);
				var response = DeserializeResponse<OkxInstrument>(json);

				foreach (var item in response)
				{
					token.ThrowIfCancellationRequested();

					var instId = item.Id; // e.g. "BTC-USDT"

					if (!secCodeLike.IsEmpty() && !instId.ContainsIgnoreCase(secCodeLike))
						continue;

					var tickSize = item.TickSize.To<decimal?>();
					var lotSize = item.LotSize.To<decimal?>();
					var listTime = item.ListingTime.To<long?>();
					var expTime = item.ExpiryTime.To<long?>();

					var secMsg = new SecurityMessage
					{
						SecurityId = new() { SecurityCode = instId, BoardCode = boardCode },
						SecurityType = secType,
						OriginalTransactionId = lookupMsg.TransactionId,
						PriceStep = tickSize,
						MinVolume = lotSize,
						VolumeStep = lotSize,
						IssueDate = listTime is { } lt ? lt.FromUnixAuto() : null,
						ExpiryDate = expTime is { } et ? et.FromUnixAuto() : null,
					}.TryFillUnderlyingId(item.Underlying);

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

	private static T[] DeserializeResponse<T>(string json)
	{
		var response = json.DeserializeObject<OkxResponse<T>>()
			?? throw new InvalidOperationException("Empty OKX response.");

		if (response.Code != "0")
			throw new InvalidOperationException(response.Message.IsEmpty()
				? $"OKX request failed with code {response.Code}."
				: response.Message);

		return response.Data ?? [];
	}

	private static OkxInstrumentTypes GetInstrumentType(MarketDataMessage mdMsg, string secId)
	{
		if (mdMsg.SecurityType == SecurityTypes.Swap || secId.EndsWithIgnoreCase("-SWAP"))
			return OkxInstrumentTypes.Swap;

		if (mdMsg.SecurityType == SecurityTypes.Option)
			return OkxInstrumentTypes.Option;

		if (mdMsg.SecurityType == SecurityTypes.Future)
			return OkxInstrumentTypes.Futures;

		return OkxInstrumentTypes.Spot;
	}

	private static string GetInstrumentFamily(string secId, OkxInstrumentTypes instrumentType)
	{
		if (instrumentType == OkxInstrumentTypes.Swap && secId.EndsWithIgnoreCase("-SWAP"))
			return secId[..^5];

		var parts = secId.Split('-');

		if (instrumentType == OkxInstrumentTypes.Option && parts.Length > 4)
			return parts.Take(parts.Length - 3).Join("-");

		if (instrumentType == OkxInstrumentTypes.Futures &&
			parts.Length > 2 && parts[^1].Length == 6 && parts[^1].All(char.IsDigit))
			return parts.Take(parts.Length - 1).Join("-");

		return secId;
	}

	private static string GetNativeSecurityId(MarketDataMessage mdMsg)
	{
		var secId = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.SecurityType == SecurityTypes.Swap)
		{
			if (!secId.EndsWithIgnoreCase("-SWAP"))
				secId += "-SWAP";
		}
		else if (mdMsg.SecurityType == SecurityTypes.Option)
		{
			if (!secId.EndsWithIgnoreCase("-C") && !secId.EndsWithIgnoreCase("-P"))
				secId += $"-{mdMsg.ExpiryDate:yyMMdd}-{mdMsg.Strike}-{(mdMsg.OptionType == OptionTypes.Call ? "C" : "P")}";
		}
		else if (mdMsg.SecurityType == SecurityTypes.Future && mdMsg.ExpiryDate is not null)
		{
			var expirySuffix = $"-{mdMsg.ExpiryDate:yyMMdd}";

			if (!secId.EndsWithIgnoreCase(expirySuffix))
				secId += expirySuffix;
		}

		return secId;
	}

	private static QuoteChange[] ParseQuotes(string[][] quotes, int maxDepth)
	{
		if (quotes is null)
			return [];

		var result = new List<QuoteChange>();

		foreach (var quote in quotes.Take(maxDepth))
		{
			if (quote is null || quote.Length < 2)
				continue;

			var price = quote[0].To<decimal?>();
			var size = quote[1].To<decimal?>();

			if (price is null || size is null)
				continue;

			var ordersCount = quote.Length > 2 ? quote[2].To<int?>() : null;
			result.Add(new(price.Value, size.Value, ordersCount));
		}

		return [.. result];
	}

	private async Task<ISet<DateTime>> GetAvailableDatesAsync(
		OkxHistoryModules module,
		MarketDataMessage mdMsg,
		string secId,
		DateTime from,
		DateTime to,
		CancellationToken token)
	{
		if (!CheckDates)
			return null;

		var now = DateTime.UtcNow;
		var firstAvailable = module switch
		{
			OkxHistoryModules.Trades => new DateTime(2021, 9, 1, 0, 0, 0, DateTimeKind.Utc),
			OkxHistoryModules.Candles => new DateTime(2023, 7, 1, 0, 0, 0, DateTimeKind.Utc),
			_ => DateTime.MinValue.UtcKind(),
		};

		var rangeFrom = (from.Date < firstAvailable ? firstAvailable : from.Date).UtcKind();
		var rangeTo = (to.Date > now.Date ? now.Date : to.Date).UtcKind();

		if (rangeFrom > rangeTo)
			return new SynchronizedSet<DateTime>();

		var instrumentType = GetInstrumentType(mdMsg, secId);
		var filterName = instrumentType == OkxInstrumentTypes.Spot ? "instIdList" : "instFamilyList";
		var filterValue = instrumentType == OkxInstrumentTypes.Spot
			? secId
			: GetInstrumentFamily(secId, instrumentType);
		var cacheKey = $"{module}:{instrumentType}:{secId}:{rangeFrom:yyyyMMdd}:{rangeTo:yyyyMMdd}";

		if (_datesCache.TryGetValue(cacheKey, out var cached) && cached.till > now)
			return cached.dates;

		var result = new SynchronizedSet<DateTime>();

		for (var chunkFrom = rangeFrom; chunkFrom <= rangeTo; chunkFrom = chunkFrom.AddDays(20))
		{
			token.ThrowIfCancellationRequested();

			var chunkTo = chunkFrom.AddDays(19);
			if (chunkTo > rangeTo)
				chunkTo = rangeTo;

			var url = $"https://{Address}/api/v5/public/market-data-history" +
				$"?module={(int)module}" +
				$"&instType={instrumentType.ToString().ToUpperInvariant()}" +
				$"&{filterName}={filterValue.DataEscape()}" +
				$"&dateAggrType=daily" +
				$"&begin={(long)chunkFrom.ToUnix(false)}" +
				$"&end={(long)chunkTo.ToUnix(false)}";

			try
			{
				var json = await _client.GetStringAsync(url, token);

				foreach (var batch in DeserializeResponse<OkxHistoryBatch>(json))
				{
					foreach (var group in batch.Details ?? [])
					{
						foreach (var file in group.Files ?? [])
						{
							if (!file.FileName.StartsWithIgnoreCase(secId + "-"))
								continue;

							var name = Path.GetFileNameWithoutExtension(file.FileName);
							if (name.Length < 10)
								continue;

							if (DateTime.TryParseExact(
								name[^10..],
								"yyyy-MM-dd",
								CultureInfo.InvariantCulture,
								DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
								out var date))
								result.Add(date.Date);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this.AddWarningLog("OKX range request failed: {0}", ex);
				return null;
			}

			if (chunkTo < rangeTo)
				await Task.Delay(TimeSpan.FromMilliseconds(400), token);
		}

		_datesCache[cacheKey] = (now.AddMinutes(30), result);
		return result;
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

		const string dataType = "trades";
		var secId = GetNativeSecurityId(mdMsg);
		var availableDates = await GetAvailableDatesAsync(
			OkxHistoryModules.Trades, mdMsg, secId, from, to, cancellationToken);

		foreach (var date in from.Date.Range(to.Date, TimeSpan.FromDays(1)))
		{
			if (availableDates?.Contains(date.Date) == false)
				continue;

			if (!await ProcessOkxZip(secId, dataType, date, async reader =>
			{
				// CSV format: instrument_name,trade_id,side,price,size,created_time
				// Example: BTC-USD,5477185,buy,91461.1,0.00043734,1764518406939
				// Note: size may be in scientific notation (e.g., 1.772e-05)
				reader.Skip(); // skip instrument_name
				var tradeId = reader.ReadLong();
				var side = reader.ReadString();
				var price = reader.ReadDecimal();
				var size = reader.ReadDecimal();
				var time = reader.ReadLong().FromUnixAuto();

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
					TradeVolume = size.Abs(),
					ServerTime = time,
					OriginSide = side.EqualsIgnoreCase("buy") ? Sides.Buy : Sides.Sell,
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

	private static readonly CachedSynchronizedPairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1),  "1m" },
		{ TimeSpan.FromMinutes(3),  "3m" },
		{ TimeSpan.FromMinutes(5),  "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1),    "1H" },
		{ TimeSpan.FromHours(2),    "2H" },
		{ TimeSpan.FromHours(4),    "4H" },
		{ TimeSpan.FromHours(6),    "6H" },
		{ TimeSpan.FromHours(12),   "12H" },
		{ TimeSpan.FromDays(1),     "1D" },
		{ TimeSpan.FromDays(7),     "1W" },
	};

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var tf = mdMsg.GetTimeFrame();

		// Only 1-minute candles available from OKX historical data
		if (tf != TimeSpan.FromMinutes(1))
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
			return;
		}

		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? DateTime.MinValue;
		var left = mdMsg.Count ?? long.MaxValue;
		var secId = GetNativeSecurityId(mdMsg);
		var availableDates = await GetAvailableDatesAsync(
			OkxHistoryModules.Candles, mdMsg, secId, from, to, cancellationToken);

		foreach (var date in from.Date.Range(to.Date, TimeSpan.FromDays(1)))
		{
			if (availableDates?.Contains(date.Date) == false)
				continue;

			// Candles use different URL: static.okx.com with "candlesticks" path
			var url = $"https://{ArchiveAddress}/cdn/okex/traderecords/candlesticks/daily/{date:yyyyMMdd}/{secId}-candlesticks-{date:yyyy-MM-dd}.zip";

			Stream zipStream;
			try
			{
				zipStream = await _client.GetStreamAsync(url, cancellationToken);
			}
			catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				continue;
			}

			var needBreak = false;

			await Do.InvariantAsync(async () =>
			{
				foreach (var (name, item) in zipStream.Unzip())
				{
					var reader = new FastCsvReader(item, Encoding.UTF8, StringHelper.N)
					{
						ColumnSeparator = ','
					};

					if (!await reader.NextLineAsync(cancellationToken))
						continue;

					// Skip header
					if (reader.CurrentLine?.StartsWithIgnoreCase("instrument") == true)
					{
						if (!await reader.NextLineAsync(cancellationToken))
							continue;
					}

					do
					{
						// CSV format: instrument_name,open,high,low,close,vol,vol_ccy,vol_quote,open_time,confirm
						reader.Skip(); // instrument_name
						var open = reader.ReadDecimal();
						var high = reader.ReadDecimal();
						var low = reader.ReadDecimal();
						var close = reader.ReadDecimal();
						var volume = reader.ReadDecimal();
						reader.Skip(); // vol_ccy
						reader.Skip(); // vol_quote
						var time = reader.ReadLong().FromUnixAuto();
						var confirm = reader.ReadInt();

						if (time < from)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						await SendOutMessageAsync(new TimeFrameCandleMessage
						{
							OriginalTransactionId = transId,
							SecurityId = mdMsg.SecurityId,
							DataType = mdMsg.DataType2,
							OpenPrice = open,
							ClosePrice = close,
							HighPrice = high,
							LowPrice = low,
							TotalVolume = volume,
							OpenTime = time,
							State = confirm == 1 ? CandleStates.Finished : CandleStates.Active,
						}, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}
					}
					while (await reader.NextLineAsync(cancellationToken));
				}

				return true;
			});

			if (needBreak || left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(transId, cancellationToken);
	}

	private async Task<bool> ProcessOkxZip(string secId, string dataType, DateTime date, Func<FastCsvReader, ValueTask<bool>> converter, CancellationToken cancellationToken)
	{
		Stream zipStream;

		var url = $"https://{ArchiveAddress}/cdn/okex/traderecords/{dataType}/daily/{date:yyyyMMdd}/{secId}-{dataType}-{date:yyyy-MM-dd}.zip";

		try
		{
			zipStream = await _client.GetStreamAsync(url, cancellationToken);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return true; // no file for that date — continue
		}

		return await Do.InvariantAsync(async () =>
		{
			foreach (var (name, item) in zipStream.Unzip())
			{
				var reader = new FastCsvReader(item, Encoding.UTF8, StringHelper.N)
				{
					ColumnSeparator = ','
				};

				// Skip header if present
				if (!await reader.NextLineAsync(cancellationToken))
					continue;

				// Skip header line if present
				if (reader.CurrentLine?.StartsWithIgnoreCase("instrument") == true ||
					reader.CurrentLine?.StartsWithIgnoreCase("ts,") == true)
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
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? DateTime.MinValue;
		var left = mdMsg.Count ?? long.MaxValue;
		var maxDepth = mdMsg.MaxDepth ?? 400;

		var secId = GetNativeSecurityId(mdMsg);

		// Iterate from start date to end date inclusive
		for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
		{
			// URL format: https://static.okx.com/cdn/okx/match/orderbook/L2/400lv/daily/{yyyyMMdd}/{secId}-L2orderbook-400lv-{yyyy-MM-dd}.tar.gz
			var url = $"https://{ArchiveAddress}/cdn/okx/match/orderbook/L2/400lv/daily/{date:yyyyMMdd}/{secId}-L2orderbook-400lv-{date:yyyy-MM-dd}.tar.gz";

			Stream networkStream;
			try
			{
				networkStream = await _client.GetStreamAsync(url, cancellationToken);
			}
			catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				continue; // no file for that date — continue to next date
			}

			var needBreak = false;

			await using var _ = networkStream;
			await using var gzipStream = new GZipStream(networkStream, CompressionMode.Decompress);

			using var tarReader = new TarReader(gzipStream);

			while (await tarReader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
			{
				if (entry.DataStream is null)
					continue;

				using var entryReader = new StreamReader(entry.DataStream, Encoding.UTF8);

				while (await entryReader.ReadLineAsync(cancellationToken) is { } line)
				{
					if (line.IsEmpty())
						continue;

					cancellationToken.ThrowIfCancellationRequested();

					try
					{
						var book = line.DeserializeObject<OkxOrderBook>()
							?? throw new InvalidOperationException("Empty OKX order book record.");
						var timestamp = book.Timestamp.To<long?>()
							?? throw new InvalidOperationException("OKX order book timestamp is missing.");
						var time = timestamp.FromUnixAuto();

						if (time < from)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						var bids = ParseQuotes(book.Bids, maxDepth);
						var asks = ParseQuotes(book.Asks, maxDepth);

						await SendOutMessageAsync(new QuoteChangeMessage
						{
							OriginalTransactionId = transId,
							SecurityId = mdMsg.SecurityId,
							ServerTime = time,
							Bids = bids,
							Asks = asks,
							State = book.Action == OkxBookActions.Snapshot
								? QuoteChangeStates.SnapshotComplete
								: QuoteChangeStates.Increment,
						}, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}
					}
					catch (Exception ex)
					{
						this.AddWarningLog("Failed to parse order book line: {0}", ex.Message);
					}
				}

				if (needBreak)
					break;
			}

			if (needBreak || left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(transId, cancellationToken);
	}
}
