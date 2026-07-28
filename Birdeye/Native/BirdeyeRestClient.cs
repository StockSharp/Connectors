namespace StockSharp.Birdeye.Native;

sealed class BirdeyeRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly string _chain;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTime _nextRequestTime;

	public BirdeyeRestClient(
		string endpoint,
		SecureString apiKey,
		string chain,
		TimeSpan requestInterval)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Birdeye REST endpoint must be an absolute HTTP URL.",
				nameof(endpoint));
		if (requestInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(requestInterval));
		var key = apiKey.UnSecure();
		if (key.IsEmpty())
			throw new ArgumentException(
				"Birdeye API key is required.",
				nameof(apiKey));
		_chain = BirdeyeExtensions.NormalizeChain(chain);
		_requestInterval = requestInterval;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Birdeye-Connector/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"X-API-KEY", key);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"x-chain", _chain);
	}

	public override string Name => "Birdeye_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<BirdeyeToken[]> GetTokensAsync(
		decimal minimumLiquidity,
		int maximumItems,
		CancellationToken cancellationToken)
	{
		maximumItems = maximumItems.Max(1).Min(10000);
		var result = new List<BirdeyeToken>(maximumItems);
		for (var offset = 0;
			result.Count < maximumItems;
			offset += 50)
		{
			var limit = Math.Min(50, maximumItems - result.Count);
			var data = await SendAsync(
				"/defi/tokenlist",
				Query(
					("sort_by", "liquidity"),
					("sort_type", "desc"),
					("offset", offset.ToString(
						CultureInfo.InvariantCulture)),
					("limit", limit.ToString(
						CultureInfo.InvariantCulture)),
					("min_liquidity", minimumLiquidity.ToString(
						CultureInfo.InvariantCulture)),
					("ui_amount_mode", "scaled")),
				cancellationToken);
			var page = ParseTokens(data, _chain);
			result.AddRange(page);
			if (page.Length < limit)
				break;
		}
		return [.. result.Take(maximumItems)];
	}

	public async ValueTask<BirdeyeToken> GetOverviewAsync(
		string address,
		CancellationToken cancellationToken)
	{
		address.ThrowIfEmpty(nameof(address));
		var data = await SendAsync(
			"/defi/token_overview",
			Query(
				("address", address),
				("frames", "24h"),
				("ui_amount_mode", "scaled")),
			cancellationToken);
		return ParseOverview(data, _chain, address);
	}

	public async ValueTask<BirdeyeCandle[]> GetCandlesAsync(
		string address,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		bool priceInUsd,
		CancellationToken cancellationToken)
	{
		address.ThrowIfEmpty(nameof(address));
		var data = await SendAsync(
			"/defi/v3/ohlcv",
			Query(
				("address", address),
				("type", timeFrame.ToInterval()),
				("currency", priceInUsd ? "usd" : "native"),
				("time_from", ToUnixSeconds(from)),
				("time_to", ToUnixSeconds(to)),
				("ui_amount_mode", "scaled"),
				("mode", "range"),
				("padding", "false"),
				("outlier", "true")),
			cancellationToken);
		return ParseCandles(data, address, timeFrame);
	}

	internal static BirdeyeToken[] DeserializeTokens(
		string json,
		string chain)
		=> ParseTokens(ParseEnvelope(json), chain);

	internal static BirdeyeToken DeserializeOverview(
		string json,
		string chain,
		string address)
		=> ParseOverview(
			ParseEnvelope(json), chain, address);

	internal static BirdeyeCandle[] DeserializeCandles(
		string json,
		string address,
		TimeSpan timeFrame)
		=> ParseCandles(
			ParseEnvelope(json), address, timeFrame);

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
					$"Birdeye HTTP {(int)response.StatusCode} " +
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
				"Birdeye returned invalid JSON.", error);
		}
		if (root is not JObject envelope)
			throw new InvalidDataException(
				"Birdeye response envelope is missing.");
		if (envelope.Value<bool?>("success") != true)
			throw new InvalidDataException(
				$"Birdeye request failed: " +
					(envelope.Value<string>("message") ??
						envelope.Value<string>("error") ??
						"unknown error"));
		return envelope["data"] ??
			throw new InvalidDataException(
				"Birdeye response has no data field.");
	}

	private static BirdeyeToken[] ParseTokens(
		JToken data,
		string chain)
	{
		chain = BirdeyeExtensions.NormalizeChain(chain);
		var items = data switch
		{
			JArray array => array,
			JObject value when value["tokens"] is JArray tokens =>
				tokens,
			JObject value when value["items"] is JArray values =>
				values,
			_ => [],
		};
		return [.. items
			.OfType<JObject>()
			.Select(value => ParseToken(value, chain))
			.Where(static token => token is not null)];
	}

	private static BirdeyeToken ParseOverview(
		JToken data,
		string chain,
		string address)
	{
		if (data is not JObject value)
			return null;
		var token = ParseToken(value, chain);
		if (token is null)
		{
			var symbol = value.Value<string>("symbol");
			if (symbol.IsEmpty())
				return null;
			token = new()
			{
				Address = address,
				Symbol = symbol.ToUpperInvariant(),
				Name = value.Value<string>("name"),
				Chain = BirdeyeExtensions.NormalizeChain(chain),
			};
		}
		token.Address = token.Address.IsEmpty()
			? address
			: token.Address;
		token.Price =
			Decimal(value["price"]) ?? token.Price;
		token.Liquidity =
			Decimal(value["liquidity"]) ?? token.Liquidity;
		token.Volume24Hours = Decimal(
			value["v24hUSD"] ??
				value["volume24hUSD"] ??
				value["volume_24h_usd"]) ??
			token.Volume24Hours;
		token.PriceChange24Hours = Decimal(
			value["priceChange24hPercent"] ??
				value["price_change_24h_percent"] ??
				value["priceChange24h"]) ??
			token.PriceChange24Hours;
		token.MarketCap =
			Decimal(value["marketCap"] ?? value["mc"]) ??
				token.MarketCap;
		token.FullyDilutedValue =
			Decimal(value["fdv"]) ??
				token.FullyDilutedValue;
		token.LastTradeTime = Time(
			value["lastTradeUnixTime"] ??
				value["last_trade_unix_time"]) ??
			token.LastTradeTime;
		return token;
	}

	private static BirdeyeToken ParseToken(
		JObject value,
		string chain)
	{
		var address = value.Value<string>("address");
		var symbol = value.Value<string>("symbol")?
			.ToUpperInvariant();
		if (address.IsEmpty() || symbol.IsEmpty())
			return null;
		return new()
		{
			Address = address,
			Symbol = symbol,
			Name = value.Value<string>("name") ?? symbol,
			Decimals = Integer(value["decimals"]),
			Chain = chain,
			Price = Decimal(
				value["price"] ??
					value["priceUsd"] ??
					value["price_usd"]),
			Liquidity = Decimal(value["liquidity"]),
			Volume24Hours = Decimal(
				value["v24hUSD"] ??
					value["volume24hUSD"] ??
					value["volume_24h_usd"]),
			PriceChange24Hours = Decimal(
				value["v24hChangePercent"] ??
					value["priceChange24hPercent"] ??
					value["price_change_24h_percent"]),
			MarketCap =
				Decimal(value["mc"] ?? value["marketCap"]),
			FullyDilutedValue = Decimal(value["fdv"]),
			LastTradeTime = Time(
				value["lastTradeUnixTime"] ??
					value["last_trade_unix_time"]),
		};
	}

	private static BirdeyeCandle[] ParseCandles(
		JToken data,
		string address,
		TimeSpan timeFrame)
	{
		var items = data switch
		{
			JArray array => array,
			JObject value when value["items"] is JArray values =>
				values,
			_ => [],
		};
		return [.. items
			.OfType<JObject>()
			.Select(value => new BirdeyeCandle
			{
				Address =
					value.Value<string>("address") ?? address,
				TimeFrame = timeFrame,
				OpenTime = Time(
					value["unixTime"] ??
						value["unix_time"]) ?? default,
				Open = Decimal(
					value["o"] ?? value["open"]) ?? 0,
				High = Decimal(
					value["h"] ?? value["high"]) ?? 0,
				Low = Decimal(
					value["l"] ?? value["low"]) ?? 0,
				Close = Decimal(
					value["c"] ?? value["close"]) ?? 0,
				Volume = Decimal(
					value["v"] ?? value["volume"]) ?? 0,
				VolumeUsd = Decimal(
					value["vUsd"] ?? value["volume_usd"]),
			})
			.Where(static candle =>
				candle.OpenTime != default)];
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
			return DateTimeOffset.FromUnixTimeSeconds(
				timestamp).UtcDateTime;
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
