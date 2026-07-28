namespace StockSharp.CoinPaprika.Native;

sealed class CoinPaprikaRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly string _token;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTime _nextRequestTime;

	public CoinPaprikaRestClient(
		string endpoint,
		SecureString token,
		TimeSpan requestInterval)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"CoinPaprika REST endpoint must be an absolute " +
					"HTTP URL.",
				nameof(endpoint));
		if (requestInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(requestInterval));
		_token = token.UnSecure();
		_requestInterval = requestInterval;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinPaprika-Connector/1.0");
		if (!_token.IsEmpty())
			_http.DefaultRequestHeaders.TryAddWithoutValidation(
				"Authorization", _token);
	}

	public override string Name => "CoinPaprika_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ValidateAsync(
		CancellationToken cancellationToken)
		=> _ = await SendAsync(
			"/global", [], cancellationToken);

	public async ValueTask<CoinPaprikaInstrument[]> GetCoinsAsync(
		string quoteCurrency,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			"/coins", [], cancellationToken);
		return ParseCoins(data, quoteCurrency);
	}

	public async ValueTask<CoinPaprikaInstrument[]>
		GetExchangeMarketsAsync(
			string exchangeId,
			string quoteCurrency,
			CancellationToken cancellationToken)
	{
		exchangeId.ThrowIfEmpty(nameof(exchangeId));
		var data = await SendAsync(
			$"/exchanges/{EscapePath(exchangeId)}/markets",
			Query(("quotes", quoteCurrency)),
			cancellationToken);
		return ParseMarkets(
			data, exchangeId, quoteCurrency);
	}

	public async ValueTask<CoinPaprikaInstrument> GetTickerAsync(
		CoinPaprikaInstrument instrument,
		string quoteCurrency,
		CancellationToken cancellationToken)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));
		if (!instrument.ExchangeId.IsEmpty())
			return (await GetExchangeMarketsAsync(
				instrument.ExchangeId,
				quoteCurrency,
				cancellationToken))
				.FirstOrDefault(item =>
					item.NativeId.EqualsIgnoreCase(
						instrument.NativeId));
		var data = await SendAsync(
			$"/tickers/{EscapePath(instrument.CoinId)}",
			Query(("quotes", quoteCurrency)),
			cancellationToken);
		return ParseTicker(data, quoteCurrency);
	}

	public async ValueTask<CoinPaprikaCandle[]> GetCandlesAsync(
		string coinId,
		string quoteCurrency,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		int limit,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			$"/coins/{EscapePath(coinId)}/ohlcv/historical",
			Query(
				("start", from.ToUniversalTime()
					.ToString("O", CultureInfo.InvariantCulture)),
				("end", to.ToUniversalTime()
					.ToString("O", CultureInfo.InvariantCulture)),
				("limit", limit.Max(1).Min(366).ToString(
					CultureInfo.InvariantCulture)),
				("interval", timeFrame.ToInterval()),
				("quote", quoteCurrency.ToLowerInvariant())),
			cancellationToken);
		return ParseCandles(data);
	}

	internal static CoinPaprikaInstrument[] DeserializeCoins(
		string json,
		string quoteCurrency)
		=> ParseCoins(ParseJson(json), quoteCurrency);

	internal static CoinPaprikaInstrument[] DeserializeMarkets(
		string json,
		string exchangeId,
		string quoteCurrency)
		=> ParseMarkets(
			ParseJson(json), exchangeId, quoteCurrency);

	internal static CoinPaprikaInstrument DeserializeTicker(
		string json,
		string quoteCurrency)
		=> ParseTicker(ParseJson(json), quoteCurrency);

	internal static CoinPaprikaCandle[] DeserializeCandles(string json)
		=> ParseCandles(ParseJson(json));

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
					path.TrimStart('/') + CreateQueryString(query)));
			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new InvalidDataException(
					$"CoinPaprika HTTP {(int)response.StatusCode} " +
						$"({response.ReasonPhrase}): {body}");
			var data = ParseJson(body);
			if (data is JObject error &&
				error["error"] is not null)
				throw new InvalidDataException(
					$"CoinPaprika request failed: " +
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
				"CoinPaprika returned invalid JSON.", error);
		}
	}

	private static CoinPaprikaInstrument[] ParseCoins(
		JToken data,
		string quoteCurrency)
	{
		quoteCurrency =
			CoinPaprikaExtensions.NormalizeQuote(quoteCurrency);
		return [.. AsArray(data)
			.OfType<JObject>()
			.Select(value =>
			{
				var id = value.Value<string>("id");
				var symbol = value.Value<string>("symbol")?
					.ToUpperInvariant();
				if (id.IsEmpty() || symbol.IsEmpty())
					return null;
				return new CoinPaprikaInstrument
				{
					NativeId = $"coin:{id}:{quoteCurrency}",
					CoinId = id,
					Symbol = $"{symbol}/{quoteCurrency}",
					BaseSymbol = symbol,
					QuoteSymbol = quoteCurrency,
					Name = value.Value<string>("name"),
					Category = value.Value<string>("type"),
					IsActive =
						value.Value<bool?>("is_active") != false,
					Rank = value.Value<int?>("rank"),
				};
			})
			.Where(static instrument => instrument is not null)];
	}

	private static CoinPaprikaInstrument[] ParseMarkets(
		JToken data,
		string exchangeId,
		string quoteCurrency)
	{
		quoteCurrency =
			CoinPaprikaExtensions.NormalizeQuote(quoteCurrency);
		return [.. AsArray(data)
			.OfType<JObject>()
			.Select(value =>
			{
				var pair = value.Value<string>("pair");
				var baseId =
					value.Value<string>("base_currency_id");
				var quoteId =
					value.Value<string>("quote_currency_id");
				if (pair.IsEmpty() || baseId.IsEmpty())
					return null;
				var parts = pair.Split('/');
				var baseSymbol = parts.FirstOrDefault()?
					.ToUpperInvariant();
				var quoteSymbol = parts.ElementAtOrDefault(1)?
					.ToUpperInvariant() ?? quoteCurrency;
				var quote = FindQuote(
					value["quotes"], quoteCurrency);
				return new CoinPaprikaInstrument
				{
					NativeId =
						$"market:{exchangeId}:{baseId}:{quoteId}",
					CoinId = baseId,
					QuoteCoinId = quoteId,
					Symbol =
						$"{pair.ToUpperInvariant()}@{exchangeId}",
					BaseSymbol = baseSymbol,
					QuoteSymbol = quoteSymbol,
					Name =
						value.Value<string>(
							"base_currency_name") ??
						baseSymbol,
					ExchangeId = exchangeId,
					Category =
						value.Value<string>("category"),
					IsActive =
						value.Value<bool?>("outlier") != true,
					Price = Decimal(quote?["price"]),
					Volume24Hours =
						Decimal(quote?["volume_24h"]),
					LastUpdated =
						Time(value["last_updated"]),
				};
			})
			.Where(static instrument => instrument is not null)];
	}

	private static CoinPaprikaInstrument ParseTicker(
		JToken data,
		string quoteCurrency)
	{
		if (data is not JObject value)
			return null;
		quoteCurrency =
			CoinPaprikaExtensions.NormalizeQuote(quoteCurrency);
		var id = value.Value<string>("id");
		var symbol = value.Value<string>("symbol")?
			.ToUpperInvariant();
		if (id.IsEmpty() || symbol.IsEmpty())
			return null;
		var quote = FindQuote(value["quotes"], quoteCurrency);
		return new()
		{
			NativeId = $"coin:{id}:{quoteCurrency}",
			CoinId = id,
			Symbol = $"{symbol}/{quoteCurrency}",
			BaseSymbol = symbol,
			QuoteSymbol = quoteCurrency,
			Name = value.Value<string>("name"),
			IsActive = true,
			Rank = value.Value<int?>("rank"),
			Price = Decimal(quote?["price"]),
			Volume24Hours =
				Decimal(quote?["volume_24h"]),
			MarketCap = Decimal(quote?["market_cap"]),
			Change24Hours =
				Decimal(quote?["percent_change_24h"]),
			LastUpdated = Time(value["last_updated"]),
		};
	}

	private static CoinPaprikaCandle[] ParseCandles(JToken data)
		=> [.. AsArray(data)
			.OfType<JObject>()
			.Select(value => new CoinPaprikaCandle
			{
				OpenTime =
					Time(value["time_open"]) ?? default,
				CloseTime =
					Time(value["time_close"]) ?? default,
				Open = Decimal(value["open"]) ?? 0,
				High = Decimal(value["high"]) ?? 0,
				Low = Decimal(value["low"]) ?? 0,
				Close = Decimal(value["close"]) ?? 0,
				Volume = Decimal(value["volume"]) ?? 0,
				MarketCap = Decimal(value["market_cap"]),
			})
			.Where(static candle =>
				candle.OpenTime != default &&
				candle.Open > 0 &&
				candle.Close > 0)];

	private static JToken FindQuote(
		JToken quotes,
		string quoteCurrency)
	{
		if (quotes is not JObject values)
			return null;
		return values.Properties()
			.FirstOrDefault(property =>
				property.Name.Equals(
					quoteCurrency,
					StringComparison.OrdinalIgnoreCase))
			?.Value;
	}

	private static JArray AsArray(JToken token)
		=> token switch
		{
			JArray array => array,
			JObject value => new(value),
			_ => [],
		};

	private static decimal? Decimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static DateTime? Time(JToken value)
		=> DateTime.TryParse(
			value?.ToString(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? result
				: null;

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
