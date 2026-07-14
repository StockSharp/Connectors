namespace StockSharp.OkexHistory;

using System.Text;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;

using Ecng.IO;
using Ecng.IO.Compression;
using Ecng.Logging;

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
			{ "SWAP", (SecurityTypes.Future, BoardCodes.Okex) },
			{ "FUTURES", (SecurityTypes.Future, BoardCodes.Okex) },
		};

		foreach (var (nativeType, (secType, boardCode)) in endpoints)
		{
			if (!secTypes.IsEmpty() && !secTypes.Contains(secType))
				continue;

			try
			{
				var json = await _client.GetStringAsync($"https://{Address}/api/v5/public/instruments?instType={nativeType}", token);

				dynamic doc = json.DeserializeObject<object>();

				foreach (var item in doc.data)
				{
					token.ThrowIfCancellationRequested();

					var instId = (string)item.instId; // e.g. "BTC-USDT"

					if (!secCodeLike.IsEmpty() && !instId.ContainsIgnoreCase(secCodeLike))
						continue;

					// optional fields from OKX instruments:
					// tickSz, lotSz, listTime, expTime, uly
					decimal? tickSize = ((string)item.tickSz)?.To<decimal?>();
					decimal? lotSize = ((string)item.lotSz)?.To<decimal?>();
					var listTime = ((string)item.listTime)?.To<long?>();
					var expTime = ((string)item.expTime)?.To<long?>();
					var underlying = (string)item.uly;

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
					}.TryFillUnderlyingId(underlying);

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

	private async Task<ISet<DateTime>> GetAvailableDatesAsync(string dataType, CancellationToken token)
	{
		if (!CheckDates)
			return null;

		var now = DateTime.UtcNow;

		if (_datesCache.TryGetValue(dataType, out var cached) && cached.till > now)
			return cached.dates;

		var result = new SynchronizedSet<DateTime>();

		var marker = string.Empty; // first page
		const int pageSize = 300;

		for (var i = 0; i < 100; i++)
		{
			token.ThrowIfCancellationRequested();

			var path = $"cdn/okex/traderecords/{dataType}/daily";
			var url = $"https://{Address}/priapi/v5/broker/public/v2/orderRecord?path={path.DataEscape()}&nextMarker={marker.DataEscape()}&size={pageSize}&t={(long)DateTime.UtcNow.ToUnix(false)}";

			string json;

			try
			{
				json = await _client.GetStringAsync(url, token);
			}
			catch (Exception ex)
			{
				this.AddWarningLog("OKX range request failed: {0}", ex);
				break;
			}

			dynamic doc = json.DeserializeObject<object>();

			if ((string)doc.code != "0")
				break;

			dynamic data = doc.data;

			foreach (var rec in data.recordFileList)
			{
				var fileName = (string)rec.fileName;
				if (fileName.Length != 8)
					continue;

				if (!DateTime.TryParseExact(fileName, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
					continue;

				result.Add(d.Date);
			}

			var isTruncate = (bool)data.isTruncate;
			if (!isTruncate)
				break;

			marker = (string)data.nextMarker ?? string.Empty;
			if (marker.IsEmpty())
				break;
		}

		_datesCache[dataType] = (now.AddMinutes(30), result);
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

		var availableDates = await GetAvailableDatesAsync(dataType, cancellationToken);

		var secCode = mdMsg.SecurityId.SecurityCode;

		foreach (var date in from.Date.Range(to.Date, TimeSpan.FromDays(1)))
		{
			if (availableDates?.Contains(date.Date) == false)
				continue;

			var secId = secCode;

			if (mdMsg.SecurityType == SecurityTypes.Swap)
				secId += "-SWAP";
			else if (mdMsg.SecurityType == SecurityTypes.Option)
			{
				// BTC-USD-250806-102000-P
				secId += $"-{mdMsg.ExpiryDate:yyMMdd}-{mdMsg.Strike}-{(mdMsg.OptionType == OptionTypes.Call ? "C" : "P")}";
			}
			else if (mdMsg.SecurityType == SecurityTypes.Future)
			{
				if (mdMsg.ExpiryDate is not null)
				{
					// BTC-USD-250806
					secId += $"-{mdMsg.ExpiryDate:yyMMdd}";
				}
			}

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

		// Use "trades" dates - if trades exist, candles likely exist too
		var availableDates = await GetAvailableDatesAsync("trades", cancellationToken);

		var secCode = mdMsg.SecurityId.SecurityCode;

		foreach (var date in from.Date.Range(to.Date, TimeSpan.FromDays(1)))
		{
			if (availableDates?.Contains(date.Date) == false)
				continue;

			var secId = secCode;

			if (mdMsg.SecurityType == SecurityTypes.Swap)
				secId += "-SWAP";
			else if (mdMsg.SecurityType == SecurityTypes.Option)
				secId += $"-{mdMsg.ExpiryDate:yyMMdd}-{mdMsg.Strike}-{(mdMsg.OptionType == OptionTypes.Call ? "C" : "P")}";
			else if (mdMsg.SecurityType == SecurityTypes.Future)
			{
				if (mdMsg.ExpiryDate is not null)
				{
					// BTC-USD-250806
					secId += $"-{mdMsg.ExpiryDate:yyMMdd}";
				}
			}

			// Candles use different URL: static.okx.com with "candlesticks" path
			var url = $"https://static.okx.com/cdn/okex/traderecords/candlesticks/daily/{date:yyyyMMdd}/{secId}-candlesticks-{date:yyyy-MM-dd}.zip";

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

		var url = $"https://{Address}/cdn/okex/traderecords/{dataType}/daily/{date:yyyyMMdd}/{secId}-{dataType}-{date:yyyy-MM-dd}.zip";

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

		var secCode = mdMsg.SecurityId.SecurityCode;
		var secId = secCode;

		if (mdMsg.SecurityType == SecurityTypes.Swap)
			secId += "-SWAP";
		else if (mdMsg.SecurityType == SecurityTypes.Option)
			secId += $"-{mdMsg.ExpiryDate:yyMMdd}-{mdMsg.Strike}-{(mdMsg.OptionType == OptionTypes.Call ? "C" : "P")}";
		else if (mdMsg.SecurityType == SecurityTypes.Future)
		{
			if (mdMsg.ExpiryDate is not null)
			{
				// BTC-USD-250806
				secId += $"-{mdMsg.ExpiryDate:yyMMdd}";
			}
		}

		// Iterate from start date to end date inclusive
		for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
		{
			// URL format: https://static.okx.com/cdn/okx/match/orderbook/L2/400lv/daily/{yyyyMMdd}/{secId}-L2orderbook-400lv-{yyyy-MM-dd}.tar.gz
			var url = $"https://static.okx.com/cdn/okx/match/orderbook/L2/400lv/daily/{date:yyyyMMdd}/{secId}-L2orderbook-400lv-{date:yyyy-MM-dd}.tar.gz";

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
						dynamic doc = line.DeserializeObject<object>();

						var ts = ((string)doc.ts)?.To<long>() ?? 0;
						var time = ts.FromUnixAuto();

						if (time < from)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						var action = (string)doc.action;
						var isSnapshot = action.EqualsIgnoreCase("snapshot");

						static QuoteChange[] ParseQuotes(dynamic quotes, int maxDepth)
						{
							if (quotes is null)
								return [];

							var result = new List<QuoteChange>();
							var count = 0;

							foreach (var q in quotes)
							{
								if (++count > maxDepth)
									break;

								var price = ((string)q[0])?.To<decimal?>() ?? 0;
								var size = ((string)q[1])?.To<decimal?>() ?? 0;
								var ordersCount = ((string)q[2])?.To<int?>();

								result.Add(new(price, size, ordersCount));
							}

							return [.. result];
						}

						var bids = ParseQuotes(doc.bids, maxDepth);
						var asks = ParseQuotes(doc.asks, maxDepth);

						await SendOutMessageAsync(new QuoteChangeMessage
						{
							OriginalTransactionId = transId,
							SecurityId = mdMsg.SecurityId,
							ServerTime = time,
							Bids = bids,
							Asks = asks,
							State = isSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
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
