namespace StockSharp.BinanceHistory;

using System.Text;
using System.IO;

using Ecng.IO;
using Ecng.IO.Compression;
using Ecng.Serialization;
using Ecng.Logging;

using Newtonsoft.Json.Linq;

using StockSharp.Connectors.Common;

public partial class BinanceHistoryMessageAdapter
{
	private const string _bucketName = "data.binance.vision";
	private const string _bucketRegion = "ap-northeast-1";
	private const string _s3BaseUrl = "https://s3.ap-northeast-1.amazonaws.com/data.binance.vision/";
	private const string _coinExchangeInfoUrl = "https://dapi.binance.com/dapi/v1/exchangeInfo";

	private readonly SynchronizedDictionary<string, (DateTime till, (DateTime, DateTime)? range)> _rangeCache = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _contractSizes = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HttpClient _client = new();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);

		var secCodeLike = lookupMsg.SecurityId.SecurityCode;
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		var endpoints = new Dictionary<string, (SecurityTypes type, string board)>
		{
			{ "https://data-api.binance.vision/api/v3/exchangeInfo", (SecurityTypes.CryptoCurrency, BoardCodes.Binance) },
			{ "https://fapi.binance.com/fapi/v1/exchangeInfo", (SecurityTypes.Future, BoardCodes.BinanceFut) },
			{ _coinExchangeInfoUrl, (SecurityTypes.Future, BoardCodes.BinanceCoin) }
		};

		foreach (var endpoint in endpoints)
		{
			if (!secTypes.IsEmpty() && !secTypes.Contains(endpoint.Value.type))
				continue;

			var response = await _client.GetStringAsync(endpoint.Key, token);
			dynamic exchangeInfo = response.DeserializeObject<object>();

			foreach (var item in exchangeInfo.symbols)
			{
				token.ThrowIfCancellationRequested();

				var symbol = (string)item.symbol;

				if (!secCodeLike.IsEmpty() && !symbol.ContainsIgnoreCase(secCodeLike))
					continue;

				var secMsg = new SecurityMessage
				{
					SecurityId = new()
					{
						SecurityCode = symbol,
						BoardCode = endpoint.Value.board,
					},
					SecurityType = endpoint.Value.type,
					OriginalTransactionId = lookupMsg.TransactionId,
					Decimals = (int?)item.quoteAssetPrecision,
				}.TryFillUnderlyingId((string)item.baseAsset);

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, token);

				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To.Value;
		var from = mdMsg.From.Value;
		var left = mdMsg.Count ?? long.MaxValue;

		var secCode = mdMsg.SecurityId.SecurityCode;
		var section = mdMsg.GetSection();
		var tf = mdMsg.GetTimeFrame().ToNative();

		var prefix = $"data/{section}/daily/klines/{secCode}/{tf}/{secCode}-{tf}-";

		var to2 = to;
		var from2 = from;

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
			if (!await Process($"{prefix}{date:yyyy-MM-dd}.zip", async reader =>
			{
				var msg = new TimeFrameCandleMessage
				{
					OriginalTransactionId = transId,
					State = CandleStates.Finished,
					DataType = mdMsg.DataType2,
					OpenTime = reader.ReadLong().ToTime(),
					OpenPrice = reader.ReadDecimal(),
					HighPrice = reader.ReadDecimal(),
					LowPrice = reader.ReadDecimal(),
					ClosePrice = reader.ReadDecimal(),
					TotalVolume = reader.ReadDecimal(),
					CloseTime = reader.ReadLong().ToTime(),
				};

				if (msg.OpenTime < from)
					return true;

				if (msg.OpenTime > to)
					return false;

				reader.Skip();

				msg.TotalTicks = reader.ReadInt();
				msg.BuyVolume = reader.ReadDecimal();

				await SendOutMessageAsync(msg, cancellationToken);

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
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To.Value;
		var from = mdMsg.From.Value;
		var left = mdMsg.Count ?? long.MaxValue;

		var secId = mdMsg.SecurityId;
		var secCode = secId.SecurityCode;
		var section = mdMsg.GetSection();

		var prefix = $"data/{section}/daily/trades/{secCode}/{secCode}-trades-";

		var to2 = to;
		var from2 = from;

		if (CheckDates)
		{
			var range = await GetRange(prefix, cancellationToken);

			if (range is null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			from2 = range.Value.min.Max(from2);
			to2 = range.Value.max.Min(to2);
		}

		foreach (var date in from2.Date.Range(to2.Date, TimeSpan.FromDays(1)))
		{
			if (!await Process($"{prefix}{date:yyyy-MM-dd}.zip", async reader =>
			{
				var msg = new ExecutionMessage
				{
					OriginalTransactionId = transId,
					DataTypeEx = DataType.Ticks,

					TradeId = reader.ReadLong(),
					TradePrice = reader.ReadDecimal(),
					TradeVolume = reader.ReadDecimal(),
				};

				reader.Skip();

				msg.ServerTime = reader.ReadLong().ToTime();
				msg.OriginSide = reader.ReadBool() ? Sides.Buy : Sides.Sell;

				if (msg.ServerTime < from)
					return true;

				if (msg.ServerTime > to)
					return false;

				await SendOutMessageAsync(msg, cancellationToken);

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
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? DateTime.MinValue;
		var left = mdMsg.Count ?? long.MaxValue;

		var secCode = mdMsg.SecurityId.SecurityCode;
		var section = mdMsg.GetSection();
		var prefix = $"data/{section}/daily/bookTicker/{secCode}/{secCode}-bookTicker-";

		var to2 = to;
		var from2 = from;

		if (CheckDates)
		{
			var range = await GetRange(prefix, cancellationToken);

			if (range is null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			from2 = range.Value.min.Max(from2);
			to2 = range.Value.max.Min(to2);
		}

		foreach (var date in from2.Date.Range(to2.Date, TimeSpan.FromDays(1)))
		{
			if (!await Process($"{prefix}{date:yyyy-MM-dd}.zip", async reader =>
			{
				//update_id,best_bid_price,best_bid_qty,best_ask_price,best_ask_qty,transaction_time,event_time
				//4307082988118,0.02634800,315928.00000000,0.02634900,2385.00000000,1711756800080,1711756800087

				var msg = new Level1ChangeMessage
				{
					 OriginalTransactionId = transId,
					SeqNum = reader.ReadLong(),
				};

				msg.TryAdd(Level1Fields.BestBidPrice, reader.ReadDecimal());
				msg.TryAdd(Level1Fields.BestBidVolume, reader.ReadDecimal());
				msg.TryAdd(Level1Fields.BestAskPrice, reader.ReadDecimal());
				msg.TryAdd(Level1Fields.BestAskVolume, reader.ReadDecimal());

				msg.ServerTime = reader.ReadLong().ToTime();

				if (msg.ServerTime < from)
					return true;

				if (msg.ServerTime > to)
					return false;

				await SendOutMessageAsync(msg, cancellationToken);

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
		var section = mdMsg.GetSection();
		var prefix = $"data/{section}/daily/bookDepth/{secCode}/{secCode}-bookDepth-";
		var inverse = mdMsg.SecurityId.BoardCode == BoardCodes.BinanceCoin;
		var contractSize = inverse ? await GetCoinContractSize(secCode, cancellationToken) : 1m;

		var to2 = to;
		var from2 = from;

		if (CheckDates)
		{
			var range = await GetRange(prefix, cancellationToken);

			if (range is null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			from2 = range.Value.min.Max(from2);
			to2 = range.Value.max.Min(to2);
		}

		foreach (var date in from2.Date.Range(to2.Date, TimeSpan.FromDays(1)))
		{
			var timestamp = default(DateTime?);
			var bids = new List<(decimal percentage, decimal depth, decimal notional)>();
			var asks = new List<(decimal percentage, decimal depth, decimal notional)>();

			QuoteChange[] ConvertBands(List<(decimal percentage, decimal depth, decimal notional)> bands, bool isBid)
			{
				var quotes = new List<QuoteChange>();
				var previousDepth = 0m;
				var previousNotional = 0m;

				foreach (var band in bands.OrderBy(b => b.percentage.Abs()))
				{
					var volume = band.depth - previousDepth;
					var notional = band.notional - previousNotional;
					previousDepth = band.depth;
					previousNotional = band.notional;

					if (volume == 0 && notional == 0)
						continue;

					if (volume <= 0 || notional <= 0)
						throw new InvalidDataException($"Invalid cumulative depth for {secCode} at {timestamp:O}, band {band.percentage}.");

					quotes.Add(new QuoteChange
					{
						Volume = volume,
						Price = inverse ? volume * contractSize / notional : notional / volume,
					});
				}

				return [.. isBid ? quotes.OrderByDescending(q => q.Price) : quotes.OrderBy(q => q.Price)];
			}

			async ValueTask<bool> trySendQuotes()
			{
				if (bids.Count == 0 && asks.Count == 0)
					return true;

				var bidQuotes = ConvertBands(bids, true);
				var askQuotes = ConvertBands(asks, false);

				if (bidQuotes.Length > 0 && askQuotes.Length > 0 && bidQuotes[0].Price >= askQuotes[0].Price)
					throw new InvalidDataException($"Crossed depth bands for {secCode} at {timestamp:O} in {prefix}{date:yyyy-MM-dd}.zip.");

				await SendOutMessageAsync(new QuoteChangeMessage
				{
					OriginalTransactionId = transId,
					ServerTime = timestamp.Value,
					Bids = bidQuotes,
					Asks = askQuotes,
				}, cancellationToken);

				bids.Clear();
				asks.Clear();

				return --left > 0;
			}

			if (!await Process($"{prefix}{date:yyyy-MM-dd}.zip", async reader =>
			{
				var time = reader.ReadDateTime("yyyy-MM-dd HH:mm:ss").UtcKind();

				if (time < from)
					return true;

				if (time > to)
				{
					await trySendQuotes();
					return false;
				}

				if (timestamp is not null && timestamp != time)
				{
					if (!await trySendQuotes())
						return false;
				}

				// Depth and notional are cumulative from the mid-price to the percentage boundary.
				var percentage = reader.ReadDecimal();
				var depth = reader.ReadDecimal();
				var notional = reader.ReadDecimal();

				(percentage > 0 ? asks : bids).Add((percentage, depth, notional));

				timestamp = time;

				return true;
			}, cancellationToken))
				break;

			if (left <= 0)
				break;

			if (!await trySendQuotes())
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<decimal> GetCoinContractSize(string symbol, CancellationToken cancellationToken)
	{
		if (_contractSizes.TryGetValue(symbol, out var size))
			return size;

		var info = (await _client.GetStringAsync(_coinExchangeInfoUrl, cancellationToken)).DeserializeObject<JObject>();

		foreach (var contract in info["symbols"] ?? throw new InvalidDataException("Missing COIN-M contract specifications."))
		{
			if ((decimal?)contract["contractSize"] is > 0 and var contractSize)
				_contractSizes[(string)contract["symbol"]] = contractSize;
		}

		return _contractSizes.TryGetValue(symbol, out size)
			? size
			: throw new InvalidDataException($"Missing COIN-M contract size for {symbol}.");
	}

	private async ValueTask<(DateTime min, DateTime max)?> GetRange(string prefix, CancellationToken cancellationToken)
	{
		if (_rangeCache.TryGetValue(prefix, out var t) && t.till > DateTime.UtcNow)
			return t.range;

		try
		{
			var range = await S3BucketHelper.GetDateRangeFromFilesAsync(
				_client, _bucketName, _bucketRegion, prefix, "yyyy-MM-dd", cancellationToken, _s3BaseUrl);

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
		const string baseUrl = "https://data.binance.vision/";

		Stream zipStream;

		try
		{
			zipStream = await _client.GetStreamAsync($"{baseUrl}{urlPart}", cancellationToken);
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
}
