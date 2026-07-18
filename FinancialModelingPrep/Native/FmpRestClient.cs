namespace StockSharp.FinancialModelingPrep.Native;

sealed class FmpApiException : InvalidOperationException
{
	public FmpApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class FmpRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

	public FmpRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-FMP/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", token);
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await SearchSymbol("AAPL", 1, null, cancellationToken);
	}

	public Task<FmpSymbolItem[]> SearchSymbol(string query, int limit, string exchange,
		CancellationToken cancellationToken)
	{
		var path = new StringBuilder("search-symbol?query=")
			.Append(Escape(query.ThrowIfEmpty(nameof(query))))
			.Append("&limit=").Append(Math.Clamp(limit, 1, 250)
				.ToString(CultureInfo.InvariantCulture));
		Append(path, "exchange", exchange);
		return Get<FmpSymbolItem[]>(path.ToString(), cancellationToken);
	}

	public Task<FmpSymbolItem[]> SearchName(string query, int limit, string exchange,
		CancellationToken cancellationToken)
	{
		var path = new StringBuilder("search-name?query=")
			.Append(Escape(query.ThrowIfEmpty(nameof(query))))
			.Append("&limit=").Append(Math.Clamp(limit, 1, 250)
				.ToString(CultureInfo.InvariantCulture));
		Append(path, "exchange", exchange);
		return Get<FmpSymbolItem[]>(path.ToString(), cancellationToken);
	}

	public Task<FmpSymbolItem[]> GetStockScreener(string exchange, int page, int limit,
		CancellationToken cancellationToken)
	{
		var path = new StringBuilder("company-screener?page=")
			.Append(Math.Max(0, page).ToString(CultureInfo.InvariantCulture))
			.Append("&limit=").Append(Math.Clamp(limit, 1, 1000)
				.ToString(CultureInfo.InvariantCulture));
		Append(path, "exchange", exchange);
		return Get<FmpSymbolItem[]>(path.ToString(), cancellationToken);
	}

	public Task<FmpSymbolItem[]> GetSymbols(FmpMarkets market,
		CancellationToken cancellationToken)
	{
		var endpoint = market switch
		{
			FmpMarkets.Stocks => "stock-list",
			FmpMarkets.Forex => "forex-list",
			FmpMarkets.Crypto => "cryptocurrency-list",
			FmpMarkets.Indices => "index-list",
			FmpMarkets.Commodities => "commodities-list",
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};
		return Get<FmpSymbolItem[]>(endpoint, cancellationToken);
	}

	public Task<FmpQuote[]> GetQuote(string symbol, CancellationToken cancellationToken)
		=> Get<FmpQuote[]>("quote?symbol=" +
			Escape(symbol.ThrowIfEmpty(nameof(symbol))), cancellationToken);

	public Task<FmpBar[]> GetEod(string symbol, DateTime from, DateTime to,
		FmpEodAdjustments adjustment, CancellationToken cancellationToken)
	{
		var endpoint = adjustment switch
		{
			FmpEodAdjustments.Adjusted => "historical-price-eod/full",
			FmpEodAdjustments.NonSplitAdjusted =>
				"historical-price-eod/non-split-adjusted",
			FmpEodAdjustments.DividendAdjusted =>
				"historical-price-eod/dividend-adjusted",
			_ => throw new ArgumentOutOfRangeException(nameof(adjustment), adjustment, null),
		};
		return Get<FmpBar[]>($"{endpoint}?symbol={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}" +
			$"&from={ToDate(from)}&to={ToDate(to)}", cancellationToken);
	}

	public Task<FmpBar[]> GetIntraday(string symbol, DateTime from, DateTime to,
		TimeSpan timeFrame, bool isNonAdjusted, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("historical-chart/")
			.Append(timeFrame.ToFmpInterval())
			.Append("?symbol=").Append(Escape(symbol.ThrowIfEmpty(nameof(symbol))))
			.Append("&from=").Append(ToDate(from))
			.Append("&to=").Append(ToDate(to));
		if (isNonAdjusted)
			path.Append("&nonadjusted=true");
		return Get<FmpBar[]>(path.ToString(), cancellationToken);
	}

	public Task<FmpNewsItem[]> GetNews(FmpMarkets market, string symbol, DateTime? from,
		DateTime? to, int page, int limit, CancellationToken cancellationToken)
	{
		var family = market switch
		{
			FmpMarkets.Stocks => "stock",
			FmpMarkets.Forex => "forex",
			FmpMarkets.Crypto => "crypto",
			_ => throw new NotSupportedException(
				$"FMP does not expose a dedicated {market} news endpoint."),
		};
		var path = new StringBuilder("news/").Append(family);
		if (symbol.IsEmpty())
			path.Append("-latest");
		path.Append("?page=").Append(Math.Max(0, page).ToString(CultureInfo.InvariantCulture))
			.Append("&limit=").Append(Math.Clamp(limit, 1, 250)
				.ToString(CultureInfo.InvariantCulture));
		Append(path, "symbols", symbol);
		if (from != null)
			path.Append("&from=").Append(ToDate(from.Value));
		if (to != null)
			path.Append("&to=").Append(ToDate(to.Value));
		return Get<FmpNewsItem[]>(path.ToString(), cancellationToken);
	}

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (IsTransient(response.StatusCode) && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, uri.AbsolutePath);
			if (content.IsEmpty())
				return default;
			if (content.TrimStart().StartsWith('{'))
			{
				FmpErrorResponse apiError = null;
				try
				{
					apiError = JsonConvert.DeserializeObject<FmpErrorResponse>(content,
						_jsonSettings);
				}
				catch (JsonException)
				{
				}
				if (apiError?.ErrorMessage.IsEmpty(apiError?.Message)
					.IsEmpty(apiError?.Error).IsEmpty() == false)
				{
					throw CreateError(response.StatusCode, content, uri.AbsolutePath);
				}
			}
			try
			{
				return JsonConvert.DeserializeObject<T>(content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Invalid FMP response from '{uri.AbsolutePath}'.", error);
			}
		}
	}

	private static FmpApiException CreateError(HttpStatusCode statusCode, string content,
		string path)
	{
		FmpErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<FmpErrorResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = response?.ErrorMessage.IsEmpty(response?.Message)
			.IsEmpty(response?.Error).IsEmpty(content);
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode, $"FMP request '{path}' failed " +
			$"({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode is >= 500 and <= 511;

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

	private static void Append(StringBuilder path, string name, string value)
	{
		if (!value.IsEmpty())
			path.Append('&').Append(name).Append('=').Append(Escape(value));
	}

	private static string ToDate(DateTime value)
		=> value.ToUtc().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
