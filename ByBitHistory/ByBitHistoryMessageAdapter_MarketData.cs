namespace StockSharp.ByBitHistory;

using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;

using Ecng.Logging;
using Ecng.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

partial class ByBitHistoryMessageAdapter
{
	private static HttpClient CreateHttpClient()
	{
		var c = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		}, disposeHandler: true);

		var h = c.DefaultRequestHeaders;
		if (!h.UserAgent.Any())
			// Recent Chrome UA for Windows 10 64-bit
			h.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
		if (!h.Accept.Any())
			h.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
		if (!h.AcceptEncoding.Any())
		{
			h.AcceptEncoding.ParseAdd("gzip");
			h.AcceptEncoding.ParseAdd("deflate");
		}
		if (!h.AcceptLanguage.Any())
			h.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

		// Extra fetch hints (some CDNs ignore, harmless) + keep-alive
		h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
		h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
		h.TryAddWithoutValidation("Sec-Fetch-Site", "none");
		h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

		return c;
	}

	private readonly HttpClient _client = CreateHttpClient();

	private const string _bybitApi = "https://api.bybit.com";
	private const string _bybitObCdn = "https://quote-saver.bycsi.com";
	private const string _bybitPublic = "https://public.bybit.com";

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);

		var secCodeLike = lookupMsg.SecurityId.SecurityCode;
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		var endpoints = new Dictionary<string, (SecurityTypes, string)>
		{
			{ "spot",    (SecurityTypes.CryptoCurrency, BoardCodes.ByBit) },
			{ "linear",  (SecurityTypes.Future,         BoardCodes.ByBitLin) },
			{ "inverse", (SecurityTypes.Future,         BoardCodes.ByBitInv) },
			{ "option",  (SecurityTypes.Option,         BoardCodes.ByBitOpt) },
		};

		foreach (var (category, (secTypeDefault, boardCode)) in endpoints)
		{
			if (!secTypes.IsEmpty() && !secTypes.Contains(secTypeDefault))
				continue;

			try
			{
				var url = $"{_bybitApi}/v5/market/instruments-info?category={category}";
				var json = await _client.GetStringAsync(url, token);
				dynamic doc = json.DeserializeObject<object>();

				if ((string)doc.retCode != "0" && (int)doc.retCode != 0)
					continue;

				foreach (var item in doc.result.list)
				{
					token.ThrowIfCancellationRequested();

					var symbol = (string)item.symbol;

					if (!secCodeLike.IsEmpty() && !symbol.ContainsIgnoreCase(secCodeLike))
						continue;

					var tickSize = ((string)item.priceFilter?.tickSize)?.To<decimal?>();
					var qtyStep  = ((string)item.lotSizeFilter?.qtyStep)?.To<decimal?>();
					var minQty   = ((string)item.lotSizeFilter?.minOrderQty)?.To<decimal?>();

					SecurityTypes secType = secTypeDefault;
					DateTime? issue = null;
					DateTime? expiry = null;

					if (!category.EqualsIgnoreCase("spot"))
					{
						string contractType = item.contractType;
						if (!contractType.IsEmpty() && contractType.ContainsIgnoreCase("Perpetual"))
							secType = SecurityTypes.Swap;

						var launchTime = ((string)item.launchTime)?.To<long?>();
						var deliveryTime = ((string)item.deliveryTime)?.To<long?>() ?? ((string)item.settleTime)?.To<long?>();

						if (launchTime is { } lt)
							issue = lt.FromNative();
						if (deliveryTime is { } et && et > 0)
							expiry = et.FromNative();
					}
					else
					{
						var launchTime = ((string)item.launchTime)?.To<long?>();
						if (launchTime is { } lt)
							issue = lt.FromNative();
					}

					var secMsg = new SecurityMessage
					{
						SecurityId = new() { SecurityCode = symbol, BoardCode = boardCode },
						SecurityType = secType,
						OriginalTransactionId = lookupMsg.TransactionId,
						PriceStep = tickSize,
						MinVolume = minQty,
						VolumeStep = qtyStep,
						IssueDate = issue,
						ExpiryDate = expiry,
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

		var symbol = mdMsg.SecurityId.SecurityCode;
		var isSpot = mdMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.ByBit);

		Func<FastCsvReader, ExecutionMessage> parser;

		if (isSpot)
		{
			parser = r =>
			{
				//id,timestamp,price,volume,side,rpi
				//1,1756684800360,108253.8,0.05733,sell,0

				return new()
				{
					TradeId = r.ReadLong(),
					ServerTime = r.ReadLong().FromUnix(false),
					TradePrice = r.ReadDecimal(),
					TradeVolume = r.ReadDecimal(),
					OriginSide = r.ReadNullableEnum<Sides>(),
				};
			};
		}
		else
		{
			parser = r =>
			{
				//timestamp,symbol,side,size,price,tickDirection,trdMatchID,grossValue,homeNotional,foreignNotional
				//1730419200.4438,BTCUSDT,Buy,0.004,70332.40,PlusTick,0549abed-c863-5538-8330-e0ff20c9c490,2.8132959999999996e+10,0.004,281.32959999999997

				var time = r.ReadDouble().FromUnix();
				r.Skip(); // symbol

				return new()
				{
					ServerTime = time,
					OriginSide = r.ReadNullableEnum<Sides>(),
					TradeVolume = r.ReadDecimal(),
					TradePrice = r.ReadDecimal(),
					IsUpTick = r.ReadString().ToUpTick(),
					TradeStringId = r.ReadString(),
				};
			};
		}

		foreach (var date in from.Date.Range(to.Date, TimeSpan.FromDays(1)))
		{
			var url = isSpot
				? $"{_bybitPublic}/spot/{symbol}/{symbol}_{date:yyyy-MM-dd}.csv.gz"
				: $"{_bybitPublic}/trading/{symbol}/{symbol}{date:yyyy-MM-dd}.csv.gz"
			;

			Stream gzStream;

			try
			{
				var httpStream = await _client.GetStreamAsync(url, cancellationToken);
				gzStream = new GZipStream(httpStream, CompressionMode.Decompress, true);
			}
			catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				continue; // no data for this date
			}
			catch (Exception ex)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				this.AddErrorLog("Bybit download failed:\n{0}", ex);
				continue;
			}

			using (gzStream)
			{
				var reader = new FastCsvReader(gzStream, Encoding.UTF8, StringHelper.N)
				{
					ColumnSeparator = ',',
				};

				// initial header line
				if (!await reader.NextLineAsync(cancellationToken))
					continue;

				// skip header
				if (!await reader.NextLineAsync(cancellationToken))
					continue;

				do
				{
					try
					{
						var tick = parser(reader);

						if (tick.ServerTime < from)
							continue;
						else if (tick.ServerTime > to)
							break;

						tick.OriginalTransactionId = transId;
						tick.SecurityId = mdMsg.SecurityId;
						tick.DataTypeEx = DataType.Ticks;

						await SendOutMessageAsync(tick, cancellationToken);

						if (--left <= 0)
							break;
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException(LocalizedStrings.FileNotParsedLineError.Put("bybit", reader.CurrentLine), ex);
					}
				}
				while (await reader.NextLineAsync(cancellationToken));
			}

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

		var to = mdMsg.To ?? DateTime.UtcNow.Date;
		var from = mdMsg.From ?? to.AddDays(-1);
		var symbol = mdMsg.SecurityId.SecurityCode;
		var category = mdMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.ByBit)
			? "spot"
			: mdMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.ByBitLin)
				? "linear"
				: "inverse";
		var depth = 200;
		var left = mdMsg.Count ?? long.MaxValue;

		foreach (var day in from.Date.Range(to.Date, TimeSpan.FromDays(1)))
		{
			var url = $"{_bybitObCdn}/orderbook/{category}/{symbol}/{day:yyyy-MM-dd}_{symbol}_ob{depth}.data.zip";

			try
			{
				Stream httpStream;

				try
				{
					httpStream = await _client.GetStreamAsync(url, cancellationToken);
				}
				catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
				{
					continue; // no data
				}
				catch (Exception ex)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					this.AddErrorLog("Bybit download failed:\n{0}", ex);
					continue;
				}

				using var zip = new ZipArchive(httpStream, ZipArchiveMode.Read, leaveOpen: true);

				foreach (var entry in zip.Entries)
				{
					if (entry.Length == 0)
						continue;

					using var es = entry.Open();
					using var sr = new StreamReader(es, Encoding.UTF8, true, 1 << 16, leaveOpen: true);

					string line;

					while ((line = await sr.ReadLineAsync(cancellationToken)) != null)
					{
						line = line.Trim();

						if (line.Length == 0)
							continue;

						try
						{
							dynamic json = line.FromJson(typeof(object));

							var ob = ((JObject)json.data).DeserializeObject<OrderBook>();

							var msg = new QuoteChangeMessage
							{
								OriginalTransactionId = transId,
								SecurityId = mdMsg.SecurityId,
								ServerTime = ((long)json.ts).FromNative(),
								Bids = [.. ob.Bids.Select(l => new QuoteChange((decimal)l.Price, (decimal)l.Size))],
								Asks = [.. ob.Asks.Select(l => new QuoteChange((decimal)l.Price, (decimal)l.Size))],
								State = ((string)json.type).EqualsIgnoreCase("delta")
									? QuoteChangeStates.Increment
									: QuoteChangeStates.SnapshotComplete,
							};

							await SendOutMessageAsync(msg, cancellationToken);

							if (--left <= 0)
								break;
						}
						catch (Exception ex)
						{
							if (cancellationToken.IsCancellationRequested)
								break;

							this.AddErrorLog("OB line parse error {0}:\n{1}", url, ex);
						}

						if (left <= 0)
							break;
					}

					if (left <= 0)
						break;
				}
			}
			catch (Exception ex)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				this.AddWarningLog("OB parse failed {0}:\n{1}", url, ex);
			}

			if (left <= 0)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private class OrderBook
	{
		[JsonProperty("s")]
		public string Symbol { get; set; }

		[JsonProperty("b")]
		public OrderBookLevel[] Bids { get; set; }

		[JsonProperty("a")]
		public OrderBookLevel[] Asks { get; set; }

		[JsonProperty("u")]
		public long UpdateId { get; set; }

		[JsonProperty("seq")]
		public long Sequence { get; set; }
	}

	[JsonConverter(typeof(JArrayToObjectConverter))]
	private class OrderBookLevel
	{
		public double Price { get; set; }
		public double Size { get; set; }
	}
}
