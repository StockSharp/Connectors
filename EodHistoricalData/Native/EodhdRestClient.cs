namespace StockSharp.EodHistoricalData.Native;

sealed class EodhdApiException : InvalidOperationException
{
	public EodhdApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
	public bool IsLookupMiss => StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound;
}

sealed class EodhdRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly string _token;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

	public EodhdRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-EODHD/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await Get<EodhdExchange[]>("exchanges-list", cancellationToken);
	}

	public Task<EodhdExchange[]> GetExchanges(CancellationToken cancellationToken)
		=> Get<EodhdExchange[]>("exchanges-list", cancellationToken);

	public Task<EodhdSymbol[]> GetExchangeSymbols(string exchange, bool isDelisted,
		string type, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("exchange-symbol-list/")
			.Append(Escape(exchange.ThrowIfEmpty(nameof(exchange))))
			.Append("?delisted=").Append(isDelisted ? '1' : '0');
		Append(path, "type", type);
		return Get<EodhdSymbol[]>(path.ToString(), cancellationToken);
	}

	public Task<EodhdSearchItem[]> Search(string query, int limit, string type,
		string exchange, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("search/")
			.Append(Escape(query.ThrowIfEmpty(nameof(query))))
			.Append("?limit=").Append(Math.Clamp(limit, 1, 500)
				.ToString(CultureInfo.InvariantCulture));
		Append(path, "type", type);
		Append(path, "exchange", exchange);
		return Get<EodhdSearchItem[]>(path.ToString(), cancellationToken);
	}

	public Task<EodhdRealTimeQuote> GetRealTime(string ticker,
		CancellationToken cancellationToken)
		=> Get<EodhdRealTimeQuote>("real-time/" +
			Escape(ticker.ThrowIfEmpty(nameof(ticker))), cancellationToken);

	public Task<EodhdEodBar[]> GetEod(string ticker, DateTime from, DateTime to,
		TimeSpan timeFrame, CancellationToken cancellationToken)
		=> Get<EodhdEodBar[]>("eod/" + Escape(ticker.ThrowIfEmpty(nameof(ticker))) +
			$"?from={ToDate(from)}&to={ToDate(to)}&period={timeFrame.ToEodPeriod()}",
			cancellationToken);

	public Task<EodhdIntradayBar[]> GetIntraday(string ticker, DateTime from, DateTime to,
		TimeSpan timeFrame, CancellationToken cancellationToken)
		=> Get<EodhdIntradayBar[]>("intraday/" +
			Escape(ticker.ThrowIfEmpty(nameof(ticker))) +
			$"?interval={timeFrame.ToIntradayInterval()}&from={ToUnixSeconds(from)}" +
			$"&to={ToUnixSeconds(to)}", cancellationToken);

	public Task<EodhdTicks> GetTicks(string ticker, DateTime? from, DateTime? to,
		int limit, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("ticks?s=")
			.Append(Escape(ticker.ThrowIfEmpty(nameof(ticker))));
		if (from != null)
			path.Append("&from=").Append(ToUnixSeconds(from.Value));
		if (to != null)
			path.Append("&to=").Append(ToUnixSeconds(to.Value));
		path.Append("&limit=").Append(Math.Max(1, limit).ToString(CultureInfo.InvariantCulture));
		return Get<EodhdTicks>(path.ToString(), cancellationToken);
	}

	public Task<EodhdNewsItem[]> GetNews(string ticker, DateTime? from, DateTime? to,
		int limit, int offset, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("news?limit=")
			.Append(Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture))
			.Append("&offset=").Append(Math.Max(0, offset).ToString(CultureInfo.InvariantCulture));
		Append(path, "s", ticker);
		if (from != null)
			path.Append("&from=").Append(ToDate(from.Value));
		if (to != null)
			path.Append("&to=").Append(ToDate(to.Value));
		return Get<EodhdNewsItem[]>(path.ToString(), cancellationToken);
	}

	public Task<EodhdOptionPage> GetOptionContracts(EodhdOptionQuery query,
		CancellationToken cancellationToken)
		=> Get<EodhdOptionPage>(BuildOptionPath("mp/unicornbay/options/contracts", query,
			false), cancellationToken);

	public Task<EodhdOptionPage> GetOptionEod(EodhdOptionQuery query,
		CancellationToken cancellationToken)
		=> Get<EodhdOptionPage>(BuildOptionPath("mp/unicornbay/options/eod", query,
			true), cancellationToken);

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, AddAuthentication(path));
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
			try
			{
				return JsonConvert.DeserializeObject<T>(content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Invalid EODHD response from '{uri.AbsolutePath}'.", error);
			}
		}
	}

	private string AddAuthentication(string path)
		=> path + (path.Contains('?') ? '&' : '?') + "api_token=" + Escape(_token) +
			"&fmt=json";

	private static string BuildOptionPath(string endpoint, EodhdOptionQuery query,
		bool isEod)
	{
		if (query == null)
			throw new ArgumentNullException(nameof(query));
		var path = new StringBuilder(endpoint).Append('?');
		if (isEod)
			path.Append("compact=0&");
		path.Append("page[offset]=")
			.Append(Math.Clamp(query.Offset, 0, 10000).ToString(CultureInfo.InvariantCulture))
			.Append("&page[limit]=")
			.Append(Math.Clamp(query.Limit, 1, 1000).ToString(CultureInfo.InvariantCulture));
		Append(path, "filter[contract]", query.Contract);
		Append(path, "filter[underlying_symbol]", query.UnderlyingSymbol);
		Append(path, "filter[exp_date_eq]", ToNullableDate(query.Expiry));
		Append(path, "filter[exp_date_from]", ToNullableDate(query.ExpiryFrom));
		Append(path, "filter[exp_date_to]", ToNullableDate(query.ExpiryTo));
		Append(path, "filter[tradetime_eq]", ToNullableDate(query.TradeTime));
		Append(path, "filter[tradetime_from]", ToNullableDate(query.TradeTimeFrom));
		Append(path, "filter[tradetime_to]", ToNullableDate(query.TradeTimeTo));
		Append(path, "filter[type]", query.OptionType?.ToNative());
		Append(path, "filter[strike_eq]", ToNullableDecimal(query.Strike));
		Append(path, "filter[strike_from]", ToNullableDecimal(query.StrikeFrom));
		Append(path, "filter[strike_to]", ToNullableDecimal(query.StrikeTo));
		Append(path, "sort", query.Sort);
		return path.ToString();
	}

	private static void Append(StringBuilder path, string name, string value)
	{
		if (!value.IsEmpty())
			path.Append('&').Append(name).Append('=').Append(Escape(value));
	}

	private static string ToNullableDate(DateTime? value)
		=> value == null ? null : ToDate(value.Value);

	private static string ToNullableDecimal(decimal? value)
		=> value?.ToString(CultureInfo.InvariantCulture);

	private static string ToDate(DateTime value)
		=> value.ToUtc().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static long ToUnixSeconds(DateTime value)
		=> checked((long)(value.ToUtc() - DateTime.UnixEpoch).TotalSeconds);

	private static EodhdApiException CreateError(HttpStatusCode statusCode, string content,
		string path)
	{
		EodhdErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<EodhdErrorResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = response?.Message.IsEmpty(response?.Error).IsEmpty(content);
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode, $"EODHD request '{path}' failed " +
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

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
