namespace StockSharp.DexScreener.Native;

sealed class DexScreenerRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTime _nextRequestTime;

	public DexScreenerRestClient(
		string endpoint,
		TimeSpan requestInterval)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"DEX Screener REST endpoint must be an absolute HTTP URL.",
				nameof(endpoint));
		if (requestInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(requestInterval));
		_requestInterval = requestInterval;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-DexScreener-Connector/1.0");
	}

	public override string Name => "DexScreener_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<DexScreenerPair[]> LookupAsync(
		string chainId,
		string tokenAddress,
		string query,
		CancellationToken cancellationToken)
	{
		if (!tokenAddress.IsEmpty())
		{
			chainId.ThrowIfEmpty(nameof(chainId));
			var tokenPairs = await SendAsync(
				$"/token-pairs/v1/{EscapePath(chainId)}/" +
					EscapePath(tokenAddress),
				[],
				cancellationToken);
			return ParsePairs(tokenPairs);
		}
		query.ThrowIfEmpty(nameof(query));
		var result = await SendAsync(
			"/latest/dex/search",
			Query(("q", query)),
			cancellationToken);
		return ParsePairs(result);
	}

	public async ValueTask<DexScreenerPair> GetPairAsync(
		string chainId,
		string pairAddress,
		CancellationToken cancellationToken)
	{
		chainId.ThrowIfEmpty(nameof(chainId));
		pairAddress.ThrowIfEmpty(nameof(pairAddress));
		var result = await SendAsync(
			$"/latest/dex/pairs/{EscapePath(chainId)}/" +
				EscapePath(pairAddress),
			[],
			cancellationToken);
		return ParsePairs(result).FirstOrDefault();
	}

	internal static DexScreenerPair[] DeserializePairs(string json)
		=> ParsePairs(ParseJson(json));

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
					$"DEX Screener HTTP {(int)response.StatusCode} " +
						$"({response.ReasonPhrase}): {body}");
			return ParseJson(body);
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
				"DEX Screener returned invalid JSON.", error);
		}
	}

	private static DexScreenerPair[] ParsePairs(JToken data)
	{
		var pairs = data switch
		{
			JArray array => array,
			JObject value when value["pairs"] is JArray values =>
				values,
			JObject value => new JArray(value),
			_ => [],
		};
		return [.. pairs
			.OfType<JObject>()
			.Select(ParsePair)
			.Where(static pair => pair is not null)];
	}

	private static DexScreenerPair ParsePair(JObject value)
	{
		var chainId = value.Value<string>("chainId");
		var dexId = value.Value<string>("dexId");
		var pairAddress = value.Value<string>("pairAddress");
		var baseToken = value["baseToken"] as JObject;
		var quoteToken = value["quoteToken"] as JObject;
		var baseSymbol =
			baseToken?.Value<string>("symbol")?
				.ToUpperInvariant();
		var quoteSymbol =
			quoteToken?.Value<string>("symbol")?
				.ToUpperInvariant();
		if (chainId.IsEmpty() ||
			dexId.IsEmpty() ||
			pairAddress.IsEmpty() ||
			baseSymbol.IsEmpty() ||
			quoteSymbol.IsEmpty())
			return null;
		var liquidity = value["liquidity"];
		var h24 = value["txns"]?["h24"];
		return new()
		{
			ChainId = chainId,
			DexId = dexId,
			PairAddress = pairAddress,
			BaseAddress =
				baseToken.Value<string>("address"),
			BaseName = baseToken.Value<string>("name"),
			BaseSymbol = baseSymbol,
			QuoteAddress =
				quoteToken.Value<string>("address"),
			QuoteName = quoteToken.Value<string>("name"),
			QuoteSymbol = quoteSymbol,
			PriceNative = Decimal(value["priceNative"]),
			PriceUsd = Decimal(value["priceUsd"]),
			Volume24Hours = Decimal(value["volume"]?["h24"]),
			PriceChange24Hours =
				Decimal(value["priceChange"]?["h24"]),
			LiquidityUsd = Decimal(liquidity?["usd"]),
			LiquidityBase = Decimal(liquidity?["base"]),
			LiquidityQuote = Decimal(liquidity?["quote"]),
			FullyDilutedValue = Decimal(value["fdv"]),
			MarketCap = Decimal(value["marketCap"]),
			Buys24Hours = Integer(h24?["buys"]),
			Sells24Hours = Integer(h24?["sells"]),
			CreatedAt = Time(value["pairCreatedAt"]),
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
		if (!long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var timestamp))
			return null;
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

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(
			value.ThrowIfEmpty(nameof(value)));

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
