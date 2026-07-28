namespace StockSharp.Coinalyze.Native;

sealed class CoinalyzeRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTime _nextRequestTime;

	public CoinalyzeRestClient(
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
				"Coinalyze REST endpoint must be an absolute HTTP URL.",
				nameof(endpoint));
		if (requestInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(requestInterval));
		var key = apiKey.UnSecure();
		if (key.IsEmpty())
			throw new ArgumentException(
				"Coinalyze API key is required.",
				nameof(apiKey));
		_requestInterval = requestInterval;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Coinalyze-Connector/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"api_key", key);
	}

	public override string Name => "Coinalyze_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<CoinalyzeInstrument[]> GetMarketsAsync(
		CoinalyzeMarketTypes marketType,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			marketType == CoinalyzeMarketTypes.Futures
				? "/future-markets"
				: "/spot-markets",
			[],
			cancellationToken);
		return ParseMarkets(data, marketType);
	}

	public async ValueTask<CoinalyzeCandle[]> GetHistoryAsync(
		CoinalyzeInstrument instrument,
		CoinalyzeCandleMetrics metric,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		bool convertToUsd,
		CancellationToken cancellationToken)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));
		if (instrument.MarketType == CoinalyzeMarketTypes.Spot &&
			metric != CoinalyzeCandleMetrics.Price)
			throw new NotSupportedException(
				"Coinalyze spot markets support only OHLCV history.");
		if (metric == CoinalyzeCandleMetrics.LongShortRatio &&
			!instrument.HasLongShortRatio)
			throw new NotSupportedException(
				$"Coinalyze does not provide long/short data for " +
					$"'{instrument.Symbol}'.");
		if (metric == CoinalyzeCandleMetrics.Price &&
			!instrument.HasOhlcv)
			throw new NotSupportedException(
				$"Coinalyze does not provide OHLCV for " +
					$"'{instrument.Symbol}'.");
		var path = metric switch
		{
			CoinalyzeCandleMetrics.Price =>
				"/ohlcv-history",
			CoinalyzeCandleMetrics.OpenInterest =>
				"/open-interest-history",
			CoinalyzeCandleMetrics.FundingRate =>
				"/funding-rate-history",
			CoinalyzeCandleMetrics.Liquidation =>
				"/liquidation-history",
			CoinalyzeCandleMetrics.LongShortRatio =>
				"/long-short-ratio-history",
			_ => throw new ArgumentOutOfRangeException(
				nameof(metric), metric, null),
		};
		var data = await SendAsync(
			path,
			Query(
				("symbols", instrument.Symbol),
				("interval", timeFrame.ToInterval()),
				("from", ToUnixSeconds(from)),
				("to", ToUnixSeconds(to)),
				metric is
					CoinalyzeCandleMetrics.OpenInterest or
					CoinalyzeCandleMetrics.Liquidation
						? (
							"convert_to_usd",
							convertToUsd
								? "true"
								: "false")
						: (null, null)),
			cancellationToken);
		return ParseHistory(data, metric);
	}

	internal static CoinalyzeInstrument[] DeserializeMarkets(
		string json,
		CoinalyzeMarketTypes marketType)
		=> ParseMarkets(ParseJson(json), marketType);

	internal static CoinalyzeCandle[] DeserializeHistory(
		string json,
		CoinalyzeCandleMetrics metric)
		=> ParseHistory(ParseJson(json), metric);

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
					$"Coinalyze HTTP {(int)response.StatusCode} " +
						$"({response.ReasonPhrase}): {body}");
			var data = ParseJson(body);
			if (data is JObject error &&
				error["error"] is not null)
				throw new InvalidDataException(
					$"Coinalyze request failed: " +
						error.Value<string>("error"));
			return data;
		}
		finally
		{
			_nextRequestTime =
				DateTime.UtcNow + _requestInterval;
			_requestSync.Release();
		}
	}

	private static JToken ParseJson(string json)
	{
		try
		{
			return JToken.Parse(
				json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coinalyze returned invalid JSON.", error);
		}
	}

	private static CoinalyzeInstrument[] ParseMarkets(
		JToken data,
		CoinalyzeMarketTypes marketType)
		=> [.. (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value =>
			{
				var symbol = value.Value<string>("symbol");
				var exchange = value.Value<string>("exchange");
				var baseAsset = value.Value<string>("base_asset")?
					.ToUpperInvariant();
				var quoteAsset =
					value.Value<string>("quote_asset")?
						.ToUpperInvariant();
				if (symbol.IsEmpty() ||
					exchange.IsEmpty() ||
					baseAsset.IsEmpty() ||
					quoteAsset.IsEmpty())
					return null;
				var expiry = value.Value<long?>("expire_at");
				return new CoinalyzeInstrument
				{
					Symbol = symbol,
					Exchange = exchange,
					ExchangeSymbol =
						value.Value<string>(
							"symbol_on_exchange") ??
						symbol,
					BaseAsset = baseAsset,
					QuoteAsset = quoteAsset,
					MarketType = marketType,
					IsPerpetual =
						value.Value<bool?>("is_perpetual") ==
							true,
					ExpiryDate = expiry > 0
						? DateTimeOffset.FromUnixTimeSeconds(
							expiry.Value).UtcDateTime
						: null,
					MarginType =
						value.Value<string>("margined"),
					Denomination = value.Value<string>(
						"oi_lq_vol_denominated_in"),
					HasLongShortRatio = value.Value<bool?>(
						"has_long_short_ratio_data") == true,
					HasOhlcv =
						value.Value<bool?>("has_ohlcv_data") !=
							false,
					HasBuySell = value.Value<bool?>(
						"has_buy_sell_data") == true,
				};
			})
			.Where(static value => value is not null)];

	private static CoinalyzeCandle[] ParseHistory(
		JToken data,
		CoinalyzeCandleMetrics metric)
	{
		var history = (data as JArray ?? [])
			.OfType<JObject>()
			.Select(value => value["history"] as JArray)
			.FirstOrDefault(value => value is not null) ?? [];
		return [.. history
			.OfType<JObject>()
			.Select(value => ParseCandle(value, metric))
			.Where(static value => value is not null)];
	}

	private static CoinalyzeCandle ParseCandle(
		JObject value,
		CoinalyzeCandleMetrics metric)
	{
		var time = Time(value["t"]);
		if (time is null)
			return null;
		if (metric == CoinalyzeCandleMetrics.Liquidation)
		{
			var longValue = Decimal(value["l"]) ?? 0;
			var shortValue = Decimal(value["s"]) ?? 0;
			return new()
			{
				OpenTime = time.Value,
				Open = longValue,
				High = longValue + shortValue,
				Low = 0,
				Close = shortValue,
				Volume = longValue + shortValue,
			};
		}
		if (metric == CoinalyzeCandleMetrics.LongShortRatio)
		{
			var ratio = Decimal(value["r"]) ?? 0;
			var longValue = Decimal(value["l"]) ?? 0;
			var shortValue = Decimal(value["s"]) ?? 0;
			return new()
			{
				OpenTime = time.Value,
				Open = ratio,
				High = longValue,
				Low = shortValue,
				Close = ratio,
			};
		}
		return new()
		{
			OpenTime = time.Value,
			Open = Decimal(value["o"]) ?? 0,
			High = Decimal(value["h"]) ?? 0,
			Low = Decimal(value["l"]) ?? 0,
			Close = Decimal(value["c"]) ?? 0,
			Volume = Decimal(value["v"]) ?? 0,
			BuyVolume = Decimal(value["bv"]),
			Trades = Integer(value["tx"]),
		};
	}

	private static decimal? Decimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static long? Long(JToken value)
		=> long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static int? Integer(JToken value)
		=> int.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static DateTime? Time(JToken value)
	{
		var seconds = Long(value);
		if (seconds is null)
			return null;
		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(
				seconds.Value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	private static string ToUnixSeconds(DateTime value)
		=> new DateTimeOffset(
			value.ToUniversalTime())
			.ToUnixTimeSeconds()
			.ToString(CultureInfo.InvariantCulture);

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
