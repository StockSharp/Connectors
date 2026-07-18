namespace StockSharp.Benzinga.Native;

sealed class BenzingaApiException : InvalidOperationException
{
	public BenzingaApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class BenzingaRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly int _maxAttempts;

	public BenzingaRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_maxAttempts = Math.Max(1, maxAttempts);
		token.ThrowIfEmpty(nameof(token));
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"token {token}");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-BENZINGA/1.0");
	}

	public Task<BenzingaDelayedQuoteResponse> GetDelayedQuotes(string symbol, string isin,
		CancellationToken cancellationToken)
	{
		var path = "api/v2/quoteDelayed?";
		if (!symbol.IsEmpty())
			path += $"symbols={Escape(symbol)}";
		else if (!isin.IsEmpty())
			path += $"isin={Escape(isin)}";
		else
			throw new ArgumentException("A Benzinga symbol or ISIN is required.");
		return Get<BenzingaDelayedQuoteResponse>(path, true, cancellationToken);
	}

	public Task<BenzingaBarsResponse[]> GetBars(string symbol, DateTime from, DateTime to,
		string interval, BenzingaSessions session, CancellationToken cancellationToken)
	{
		var path = $"api/v2/bars?symbols={Escape(symbol.ThrowIfEmpty(nameof(symbol)))}" +
			$"&from={Escape(FormatUtc(from))}&to={Escape(FormatUtc(to))}" +
			$"&interval={Escape(interval.ThrowIfEmpty(nameof(interval)))}" +
			$"&session={session.ToNative()}";
		return Get<BenzingaBarsResponse[]>(path, true, cancellationToken);
	}

	public Task<BenzingaNewsItem[]> GetNews(string symbol, string channels, DateTime? from,
		DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("api/v2/news?displayOutput=full&format=json")
			.Append("&page=").Append(Math.Max(0, page).ToString(CultureInfo.InvariantCulture))
			.Append("&pageSize=").Append(Math.Clamp(pageSize, 1, 100)
				.ToString(CultureInfo.InvariantCulture));
		if (!symbol.IsEmpty())
			path.Append("&tickers=").Append(Escape(symbol));
		if (!channels.IsEmpty())
			path.Append("&channels=").Append(Escape(channels));
		if (from != null)
			path.Append("&dateFrom=").Append(from.Value.ToUtc()
				.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		if (to != null)
			path.Append("&dateTo=").Append(to.Value.ToUtc()
				.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		return Get<BenzingaNewsItem[]>(path.ToString(), true, cancellationToken);
	}

	private async Task<T> Get<T>(string path, bool isNotFoundEmpty,
		CancellationToken cancellationToken)
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
			if (isNotFoundEmpty && response.StatusCode == HttpStatusCode.NotFound)
				return default;
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
					$"Benzinga returned invalid JSON for '{uri.AbsolutePath}'.", error);
			}
		}
	}

	private static BenzingaApiException CreateError(HttpStatusCode statusCode, string content,
		string path)
	{
		BenzingaErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<BenzingaErrorResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var details = response?.Message;
		if (details.IsEmpty() && response?.Errors?.Length > 0)
		{
			details = string.Join("; ", response.Errors.Where(error => error != null)
				.Select(error => error.Value.IsEmpty(error.Code).IsEmpty(error.Id)));
		}
		details = details.IsEmpty(content)?.Trim();
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode,
			$"Benzinga request '{path}' failed ({(int)statusCode} {statusCode})" +
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

	private static string FormatUtc(DateTime value)
		=> value.ToUtc().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

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
