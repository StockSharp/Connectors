namespace StockSharp.Finnhub.Native;

sealed class FinnhubApiException : InvalidOperationException
{
	public FinnhubApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class FinnhubRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly int _maxAttempts;

	public FinnhubRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("X-Finnhub-Token",
			token.ThrowIfEmpty(nameof(token)));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-Finnhub/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await Get<FinnhubExchange[]>("stock/exchange", cancellationToken);
	}

	public Task<FinnhubStockSymbol[]> GetStockSymbols(string exchange, string mic,
		CancellationToken cancellationToken)
	{
		var path = $"stock/symbol?exchange={Escape(exchange.ThrowIfEmpty(nameof(exchange)))}";
		if (!mic.IsEmpty())
			path += $"&mic={Escape(mic)}";
		return Get<FinnhubStockSymbol[]>(path, cancellationToken);
	}

	public Task<FinnhubAssetSymbol[]> GetForexSymbols(string exchange,
		CancellationToken cancellationToken)
		=> Get<FinnhubAssetSymbol[]>($"forex/symbol?exchange={Escape(exchange.ThrowIfEmpty(nameof(exchange)))}",
			cancellationToken);

	public Task<FinnhubAssetSymbol[]> GetCryptoSymbols(string exchange,
		CancellationToken cancellationToken)
		=> Get<FinnhubAssetSymbol[]>($"crypto/symbol?exchange={Escape(exchange.ThrowIfEmpty(nameof(exchange)))}",
			cancellationToken);

	public Task<FinnhubSymbolLookupResponse> Search(string query, string exchange,
		CancellationToken cancellationToken)
	{
		var path = $"search?q={Escape(query.ThrowIfEmpty(nameof(query)))}";
		if (!exchange.IsEmpty())
			path += $"&exchange={Escape(exchange)}";
		return Get<FinnhubSymbolLookupResponse>(path, cancellationToken);
	}

	public Task<FinnhubQuote> GetQuote(string symbol, CancellationToken cancellationToken)
		=> Get<FinnhubQuote>($"quote?symbol={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}", cancellationToken);

	public Task<FinnhubBidAsk> GetBidAsk(string symbol, CancellationToken cancellationToken)
		=> Get<FinnhubBidAsk>($"stock/bidask?symbol={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}",
			cancellationToken);

	public Task<FinnhubCandles> GetCandles(FinnhubMarkets market, string symbol, string resolution,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var section = market switch
		{
			FinnhubMarkets.Stocks => "stock",
			FinnhubMarkets.Forex => "forex",
			FinnhubMarkets.Crypto => "crypto",
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};
		return Get<FinnhubCandles>($"{section}/candle?symbol={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}" +
			$"&resolution={Escape(resolution.ThrowIfEmpty(nameof(resolution)))}" +
			$"&from={ToUnixSeconds(from).ToString(CultureInfo.InvariantCulture)}" +
			$"&to={ToUnixSeconds(to).ToString(CultureInfo.InvariantCulture)}", cancellationToken);
	}

	public Task<FinnhubTicks> GetTicks(string symbol, DateTime date, int limit, long skip,
		CancellationToken cancellationToken)
		=> Get<FinnhubTicks>($"stock/tick?symbol={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}" +
			$"&date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
			$"&limit={Math.Clamp(limit, 1, 25000).ToString(CultureInfo.InvariantCulture)}" +
			$"&skip={Math.Max(0, skip).ToString(CultureInfo.InvariantCulture)}", cancellationToken);

	public Task<FinnhubNewsItem[]> GetCompanyNews(string symbol, DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> Get<FinnhubNewsItem[]>($"company-news?symbol={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}" +
			$"&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
			$"&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", cancellationToken);

	public Task<FinnhubNewsItem[]> GetMarketNews(FinnhubNewsCategories category, long? minId,
		CancellationToken cancellationToken)
	{
		var path = $"news?category={category.ToNative()}";
		if (minId != null)
			path += $"&minId={minId.Value.ToString(CultureInfo.InvariantCulture)}";
		return Get<FinnhubNewsItem[]>(path, cancellationToken);
	}

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);

			if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode is >= 500 and <= 511) && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, uri.AbsolutePath);
			if (content.IsEmpty())
				return default;

			return JsonConvert.DeserializeObject<T>(content, _jsonSettings);
		}
	}

	private static FinnhubApiException CreateError(HttpStatusCode statusCode, string content,
		string path)
	{
		FinnhubErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<FinnhubErrorResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var details = response?.Error.IsEmpty(content);
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode, $"Finnhub request '{path}' failed ({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is { } delta)
			return ClampDelay(delta);
		if (response.Headers.RetryAfter?.Date is { } date)
			return ClampDelay(date.UtcDateTime - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay < TimeSpan.Zero ? TimeSpan.Zero :
			delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;

	private static long ToUnixSeconds(DateTime value)
		=> new DateTimeOffset(value.ToUtc()).ToUnixTimeSeconds();

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
