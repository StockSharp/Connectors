namespace StockSharp.CoinGlass.Native;

sealed class CoinGlassRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTime _nextRequestTime;

	public CoinGlassRestClient(
		string endpoint,
		SecureString apiKey,
		TimeSpan requestInterval)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"CoinGlass REST endpoint must be an absolute HTTP URL.",
				nameof(endpoint));
		if (requestInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(requestInterval));
		var key = apiKey.UnSecure();
		if (key.IsEmpty())
			throw new ArgumentException(
				"CoinGlass API key is required.",
				nameof(apiKey));
		_requestInterval = requestInterval;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinGlass-Connector/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"CG-API-KEY", key);
	}

	public override string Name => "CoinGlass_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ValidateAsync(
		CoinGlassMarketTypes marketType,
		string exchange,
		string symbol,
		CancellationToken cancellationToken)
		=> _ = await GetInstrumentsAsync(
			marketType,
			exchange,
			symbol,
			cancellationToken);

	public async ValueTask<CoinGlassInstrument[]>
		GetInstrumentsAsync(
			CoinGlassMarketTypes marketType,
			string exchange,
			string symbol,
			CancellationToken cancellationToken)
	{
		if (marketType is
			CoinGlassMarketTypes.Futures or
			CoinGlassMarketTypes.Spot)
		{
			var data = await SendAsync(
				$"/api/{marketType.ToApiName()}/" +
					"supported-exchange-pairs",
				Query(("exchange", exchange)),
				cancellationToken);
			return ParsePairs(data, marketType, exchange);
		}
		if (marketType == CoinGlassMarketTypes.Options)
		{
			var data = await SendAsync(
				"/api/option/info",
				Query(("symbol", symbol)),
				cancellationToken);
			return ParseOptions(data, symbol);
		}
		var etfs = await SendAsync(
			$"/api/etf/{marketType.ToApiName()}/list",
			[],
			cancellationToken);
		return ParseEtfs(etfs, marketType);
	}

	public async ValueTask<CoinGlassInstrument> GetSnapshotAsync(
		CoinGlassInstrument instrument,
		CancellationToken cancellationToken)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));
		CoinGlassInstrument[] values;
		if (instrument.MarketType is
			CoinGlassMarketTypes.Futures or
			CoinGlassMarketTypes.Spot)
		{
			var data = await SendAsync(
				$"/api/{instrument.MarketType.ToApiName()}/" +
					"pairs-markets",
				Query(("symbol", instrument.BaseAsset)),
				cancellationToken);
			values = ParsePairMarkets(
				data, instrument.MarketType);
		}
		else
		{
			values = await GetInstrumentsAsync(
				instrument.MarketType,
				instrument.Exchange,
				instrument.BaseAsset,
				cancellationToken);
		}
		return values.FirstOrDefault(value =>
			value.NativeId.EqualsIgnoreCase(instrument.NativeId) ||
			(value.Exchange.EqualsIgnoreCase(instrument.Exchange) &&
				(value.InstrumentId.EqualsIgnoreCase(
					instrument.InstrumentId) ||
				value.Symbol.EqualsIgnoreCase(instrument.Symbol))));
	}

	public async ValueTask<CoinGlassCandle[]> GetCandlesAsync(
		CoinGlassInstrument instrument,
		CoinGlassCandleMetrics metric,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		int limit,
		CancellationToken cancellationToken)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));
		if (instrument.MarketType is
			CoinGlassMarketTypes.BitcoinEtf or
			CoinGlassMarketTypes.EthereumEtf)
		{
			if (metric != CoinGlassCandleMetrics.Price)
				throw new NotSupportedException(
					"CoinGlass ETF candle history supports only price.");
			if (instrument.MarketType !=
				CoinGlassMarketTypes.BitcoinEtf)
				throw new NotSupportedException(
					"CoinGlass v4 does not publish an Ethereum ETF " +
						"price-history endpoint.");
			var data = await SendAsync(
				"/api/etf/bitcoin/price/history",
				Query(
					("ticker", instrument.InstrumentId),
					("range", ToEtfRange(from, to))),
				cancellationToken);
			return ParseOhlc(data);
		}
		if (instrument.MarketType ==
			CoinGlassMarketTypes.Options)
		{
			if (metric is
				CoinGlassCandleMetrics.FundingRate or
				CoinGlassCandleMetrics.Liquidation)
				throw new NotSupportedException(
					"CoinGlass options history supports price, " +
						"open interest and volume only.");
			var usePrice = metric == CoinGlassCandleMetrics.Price;
			var useOpenInterest =
				usePrice ||
				metric == CoinGlassCandleMetrics.OpenInterest;
			var data = await SendAsync(
				useOpenInterest
					? "/api/option/exchange-oi-history"
					: "/api/option/exchange-vol-history",
				Query(
					("symbol", instrument.BaseAsset),
					("unit", "USD")),
				cancellationToken);
			return ParseSeries(
				data, instrument.Exchange, usePrice);
		}
		if (instrument.MarketType ==
			CoinGlassMarketTypes.Spot &&
			metric != CoinGlassCandleMetrics.Price)
			throw new NotSupportedException(
				"CoinGlass spot history supports only price OHLC.");

		var path = metric switch
		{
			CoinGlassCandleMetrics.Price =>
				$"/api/{instrument.MarketType.ToApiName()}/" +
					"price/history",
			CoinGlassCandleMetrics.OpenInterest =>
				"/api/futures/open-interest/history",
			CoinGlassCandleMetrics.FundingRate =>
				"/api/futures/funding-rate/history",
			CoinGlassCandleMetrics.Liquidation =>
				"/api/futures/liquidation/history",
			_ => throw new ArgumentOutOfRangeException(
				nameof(metric), metric, null),
		};
		var response = await SendAsync(
			path,
			Query(
				("exchange", instrument.Exchange),
				("symbol", instrument.InstrumentId),
				("interval", timeFrame.ToInterval()),
				("limit", limit.Max(1).Min(1000).ToString(
					CultureInfo.InvariantCulture)),
				("start_time", ToUnixMilliseconds(from)),
				("end_time", ToUnixMilliseconds(to)),
				metric == CoinGlassCandleMetrics.OpenInterest
					? ("unit", "usd")
					: (null, null)),
			cancellationToken);
		return metric == CoinGlassCandleMetrics.Liquidation
			? ParseLiquidations(response)
			: ParseOhlc(response);
	}

	internal static CoinGlassInstrument[] DeserializePairs(
		string json,
		CoinGlassMarketTypes marketType,
		string exchange)
		=> ParsePairs(ParseEnvelope(json), marketType, exchange);

	internal static CoinGlassInstrument[] DeserializePairMarkets(
		string json,
		CoinGlassMarketTypes marketType)
		=> ParsePairMarkets(ParseEnvelope(json), marketType);

	internal static CoinGlassInstrument[] DeserializeOptions(
		string json,
		string symbol)
		=> ParseOptions(ParseEnvelope(json), symbol);

	internal static CoinGlassInstrument[] DeserializeEtfs(
		string json,
		CoinGlassMarketTypes marketType)
		=> ParseEtfs(ParseEnvelope(json), marketType);

	internal static CoinGlassCandle[] DeserializeOhlc(string json)
		=> ParseOhlc(ParseEnvelope(json));

	internal static CoinGlassCandle[] DeserializeLiquidations(
		string json)
		=> ParseLiquidations(ParseEnvelope(json));

	internal static CoinGlassCandle[] DeserializeSeries(
		string json,
		string exchange,
		bool usePrice)
		=> ParseSeries(
			ParseEnvelope(json), exchange, usePrice);

	private async ValueTask<JToken> SendAsync(
		string path,
		KeyValuePair<string, string>[] query,
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			using var request = new HttpRequestMessage(
				HttpMethod.Get,
				new Uri(
					_endpoint,
					path.TrimStart('/') +
						CreateQueryString(query)));
			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new InvalidDataException(
					$"CoinGlass HTTP {(int)response.StatusCode} " +
						$"({response.ReasonPhrase}): {body}");
			return ParseEnvelope(body);
		}
		finally
		{
			_nextRequestTime =
				DateTime.UtcNow + _requestInterval;
			_requestSync.Release();
		}
	}

	private static JToken ParseEnvelope(string json)
	{
		JToken root;
		try
		{
			root = JToken.Parse(
				json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinGlass returned invalid JSON.", error);
		}
		if (root is not JObject envelope)
			throw new InvalidDataException(
				"CoinGlass response envelope is missing.");
		var code = envelope["code"]?.ToString();
		if (code != "0")
			throw new InvalidDataException(
				$"CoinGlass request failed ({code}): " +
					(envelope.Value<string>("msg") ??
						envelope.Value<string>("message") ??
						"unknown error"));
		return envelope["data"] ??
			throw new InvalidDataException(
				"CoinGlass response has no data field.");
	}

	private static CoinGlassInstrument[] ParsePairs(
		JToken data,
		CoinGlassMarketTypes marketType,
		string exchange)
	{
		if (data is not JObject exchanges)
			return [];
		var result = new List<CoinGlassInstrument>();

		foreach (var property in exchanges.Properties())
		{
			if (!exchange.IsEmpty() &&
				!property.Name.EqualsIgnoreCase(exchange))
				continue;

			foreach (var value in
				(property.Value as JArray ?? []).OfType<JObject>())
			{
				var instrumentId =
					value.Value<string>("instrument_id");
				var baseAsset = value.Value<string>("base_asset")?
					.ToUpperInvariant();
				var quoteAsset =
					value.Value<string>("quote_asset")?
						.ToUpperInvariant();
				if (instrumentId.IsEmpty() ||
					baseAsset.IsEmpty() ||
					quoteAsset.IsEmpty())
					continue;
				result.Add(new()
				{
					NativeId = NativeId(
						marketType, property.Name, instrumentId),
					InstrumentId = instrumentId,
					Symbol =
						$"{baseAsset}/{quoteAsset}@{property.Name}",
					BaseAsset = baseAsset,
					QuoteAsset = quoteAsset,
					Exchange = property.Name,
					Name = instrumentId,
					MarketType = marketType,
					PriceStep =
						Decimal(value["price_tick_size"]),
					MaxLeverage =
						Decimal(value["max_leverage"]),
				});
			}
		}

		return [.. result];
	}

	private static CoinGlassInstrument[] ParsePairMarkets(
		JToken data,
		CoinGlassMarketTypes marketType)
		=> [.. (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value =>
			{
				var exchange =
					value.Value<string>("exchange_name");
				var pair = value.Value<string>("symbol");
				var parts = pair?.Split('/');
				var baseAsset = parts?.FirstOrDefault()?
					.ToUpperInvariant();
				var quoteAsset = parts?.ElementAtOrDefault(1)?
					.ToUpperInvariant();
				var instrumentId =
					value.Value<string>("instrument_id") ??
						pair?.Replace("/", string.Empty);
				if (exchange.IsEmpty() ||
					instrumentId.IsEmpty() ||
					baseAsset.IsEmpty() ||
					quoteAsset.IsEmpty())
					return null;
				return new CoinGlassInstrument
				{
					NativeId = NativeId(
						marketType, exchange, instrumentId),
					InstrumentId = instrumentId,
					Symbol =
						$"{baseAsset}/{quoteAsset}@{exchange}",
					BaseAsset = baseAsset,
					QuoteAsset = quoteAsset,
					Exchange = exchange,
					Name = instrumentId,
					MarketType = marketType,
					LastPrice =
						Decimal(value["current_price"]),
					IndexPrice = Decimal(value["index_price"]),
					Volume = Decimal(
						value["volume_usd"] ??
							value["volume_usd_24h"] ??
							value["volume_usd_1h"]),
					Change = Decimal(
						value["price_change_percent_24h"] ??
							value["price_change_percent_1h"]),
					OpenInterest =
						Decimal(value["open_interest_usd"]),
					FundingRate =
						Decimal(value["funding_rate"]),
					LongLiquidation = Decimal(
						value["long_liquidation_usd_24h"]),
					ShortLiquidation = Decimal(
						value["short_liquidation_usd_24h"]),
					ServerTime = Time(
						value["next_funding_time"]),
				};
			})
			.Where(static value => value is not null)];

	private static CoinGlassInstrument[] ParseOptions(
		JToken data,
		string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol))
			.Trim()
			.ToUpperInvariant();
		return [.. (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value =>
			{
				var exchange =
					value.Value<string>("exchange_name");
				if (exchange.IsEmpty())
					return null;
				var instrumentId = $"{symbol}-OPTIONS";
				return new CoinGlassInstrument
				{
					NativeId = NativeId(
						CoinGlassMarketTypes.Options,
						exchange,
						instrumentId),
					InstrumentId = instrumentId,
					Symbol = $"{instrumentId}@{exchange}",
					BaseAsset = symbol,
					QuoteAsset = "USD",
					Exchange = exchange,
					Name = $"{symbol} options analytics",
					MarketType = CoinGlassMarketTypes.Options,
					OpenInterest =
						Decimal(value["open_interest_usd"]),
					Volume =
						Decimal(value["volume_usd_24h"]),
					Change = Decimal(
						value["open_interest_change_24h"]),
				};
			})
			.Where(static value => value is not null)];
	}

	private static CoinGlassInstrument[] ParseEtfs(
		JToken data,
		CoinGlassMarketTypes marketType)
		=> [.. (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value =>
			{
				var ticker = value.Value<string>("ticker")?
					.ToUpperInvariant();
				if (ticker.IsEmpty())
					return null;
				var exchange =
					value.Value<string>("primary_exchange") ??
						"US";
				var asset =
					marketType == CoinGlassMarketTypes.BitcoinEtf
						? "BTC"
						: "ETH";
				return new CoinGlassInstrument
				{
					NativeId = NativeId(
						marketType, exchange, ticker),
					InstrumentId = ticker,
					Symbol = ticker,
					BaseAsset = asset,
					QuoteAsset = "USD",
					Exchange = exchange,
					Name =
						value.Value<string>("fund_name") ??
							value.Value<string>("name") ??
							ticker,
					MarketType = marketType,
					LastPrice = Decimal(value["price"]),
					Volume = Decimal(
						value["volume_quantity"] ??
							value["volume_usd"]),
					Change = Decimal(
						value["price_change_percent"]),
					ServerTime = Time(
						value["update_timestamp"] ??
							value["update_time"] ??
							value["last_trade_time"]),
				};
			})
			.Where(static value => value is not null)];

	private static CoinGlassCandle[] ParseOhlc(JToken data)
		=> [.. (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value => new CoinGlassCandle
			{
				OpenTime = Time(value["time"]) ?? default,
				Open = Decimal(value["open"]) ?? 0,
				High = Decimal(value["high"]) ?? 0,
				Low = Decimal(value["low"]) ?? 0,
				Close = Decimal(value["close"]) ?? 0,
				Volume = Decimal(
					value["volume"] ??
						value["volume_usd"]) ?? 0,
			})
			.Where(static value =>
				value.OpenTime != default)];

	private static CoinGlassCandle[] ParseLiquidations(
		JToken data)
		=> [.. (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value =>
			{
				var longValue = Decimal(
					value["long_liquidation_usd"] ??
						value[
							"aggregated_long_liquidation_usd"])
					?? 0;
				var shortValue = Decimal(
					value["short_liquidation_usd"] ??
						value[
							"aggregated_short_liquidation_usd"])
					?? 0;
				return new CoinGlassCandle
				{
					OpenTime =
						Time(value["time"]) ?? default,
					Open = longValue,
					High = longValue + shortValue,
					Low = 0,
					Close = shortValue,
					Volume = longValue + shortValue,
				};
			})
			.Where(static value =>
				value.OpenTime != default)];

	private static CoinGlassCandle[] ParseSeries(
		JToken data,
		string exchange,
		bool usePrice)
	{
		var root = data switch
		{
			JArray array => array.OfType<JObject>().FirstOrDefault(),
			JObject value => value,
			_ => null,
		};
		if (root is null)
			return [];
		var times = root["time_list"] as JArray ?? [];
		var values = usePrice
			? root["price_list"] as JArray
			: FindSeries(root["data_map"], exchange);
		values ??= [];
		var count = Math.Min(times.Count, values.Count);
		var result = new List<CoinGlassCandle>(count);
		for (var index = 0; index < count; index++)
		{
			var time = Time(times[index]);
			var value = Decimal(values[index]);
			if (time is null || value is null)
				continue;
			result.Add(new()
			{
				OpenTime = time.Value,
				Open = value.Value,
				High = value.Value,
				Low = value.Value,
				Close = value.Value,
			});
		}
		return [.. result];
	}

	private static JArray FindSeries(
		JToken dataMap,
		string exchange)
	{
		if (dataMap is not JObject map)
			return null;
		return map.Properties()
			.FirstOrDefault(property =>
				property.Name.Equals(
					exchange,
					StringComparison.OrdinalIgnoreCase))
			?.Value as JArray;
	}

	private static string NativeId(
		CoinGlassMarketTypes marketType,
		string exchange,
		string instrumentId)
		=> $"{marketType.ToString().ToLowerInvariant()}:" +
			$"{exchange}:{instrumentId}";

	private static decimal? Decimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static DateTime? Time(JToken value)
	{
		if (long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var timestamp))
		{
			try
			{
				return DateTimeOffset.FromUnixTimeMilliseconds(
					timestamp).UtcDateTime;
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}
		return DateTime.TryParse(
			value?.ToString(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? result
				: null;
	}

	private static string ToUnixMilliseconds(DateTime value)
		=> new DateTimeOffset(
			value.ToUniversalTime())
			.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);

	private static string ToEtfRange(
		DateTime from,
		DateTime to)
		=> to - from <= TimeSpan.FromDays(1)
			? "1d"
			: to - from <= TimeSpan.FromDays(7)
				? "7d"
				: "all";

	private static KeyValuePair<string, string>[] Query(
		params (string Key, string Value)[] values)
		=> [.. values
			.Where(static value =>
				!value.Key.IsEmpty() && value.Value is not null)
			.Select(static value =>
				new KeyValuePair<string, string>(
					value.Key, value.Value))];

	private static string CreateQueryString(
		IEnumerable<KeyValuePair<string, string>> values)
	{
		var query = (values ?? [])
			.Where(static value =>
				!value.Key.IsEmpty() && value.Value is not null)
			.Select(static value =>
				Uri.EscapeDataString(value.Key) + "=" +
					Uri.EscapeDataString(value.Value))
			.ToArray();
		return query.Length == 0
			? string.Empty
			: "?" + string.Join("&", query);
	}
}
