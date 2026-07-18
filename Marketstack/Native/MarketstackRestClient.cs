namespace StockSharp.Marketstack.Native;

sealed class MarketstackApiException : InvalidOperationException
{
	public MarketstackApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class MarketstackRestClient : BaseLogReceiver
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

	public MarketstackRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-Marketstack/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await GetTickers("AAPL", null, 0, 1, cancellationToken);
	}

	public Task<MarketstackPage<MarketstackTicker>> GetTickers(string search, string exchange,
		long offset, int limit, CancellationToken cancellationToken)
	{
		var path = CreatePath("tickerslist");
		Append(path, "search", search);
		Append(path, "exchange", exchange);
		Append(path, "limit", Math.Clamp(limit, 1, 1000));
		Append(path, "offset", Math.Max(0, offset));
		return Get<MarketstackPage<MarketstackTicker>>(path.ToString(), cancellationToken);
	}

	public Task<MarketstackPage<MarketstackBar>> GetEod(string symbol, string exchange,
		DateTime from, DateTime to, long offset, int limit,
		CancellationToken cancellationToken)
	{
		var path = CreatePath("eod");
		Append(path, "symbols", symbol.ThrowIfEmpty(nameof(symbol)));
		Append(path, "exchange", exchange);
		Append(path, "date_from", ToDate(from));
		Append(path, "date_to", ToDate(to));
		Append(path, "sort", "ASC");
		Append(path, "limit", Math.Clamp(limit, 1, 1000));
		Append(path, "offset", Math.Max(0, offset));
		return Get<MarketstackPage<MarketstackBar>>(path.ToString(), cancellationToken);
	}

	public Task<MarketstackPage<MarketstackBar>> GetIntraday(string symbol, string exchange,
		DateTime from, DateTime to, TimeSpan timeFrame, bool isAfterHours, long offset,
		int limit, CancellationToken cancellationToken)
	{
		var path = CreatePath("intraday");
		Append(path, "symbols", symbol.ThrowIfEmpty(nameof(symbol)));
		Append(path, "interval", timeFrame.ToMarketstackInterval());
		Append(path, "exchange", exchange);
		Append(path, "date_from", ToDate(from));
		Append(path, "date_to", ToDate(to));
		Append(path, "sort", "ASC");
		Append(path, "limit", Math.Clamp(limit, 1, 1000));
		Append(path, "offset", Math.Max(0, offset));
		Append(path, "after_hours", isAfterHours ? "true" : "false");
		return Get<MarketstackPage<MarketstackBar>>(path.ToString(), cancellationToken);
	}

	public Task<MarketstackStockPriceResponse> GetStockPrice(string symbol, string exchange,
		CancellationToken cancellationToken)
	{
		var path = CreatePath("stockprice");
		Append(path, "ticker", symbol.ThrowIfEmpty(nameof(symbol)));
		Append(path, "exchange", exchange);
		return Get<MarketstackStockPriceResponse>(path.ToString(), cancellationToken);
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

			var apiError = TryGetError(content);
			if (!response.IsSuccessStatusCode || apiError != null)
				throw CreateError(response.StatusCode, apiError, content, uri.AbsolutePath);
			if (content.IsEmpty())
				return default;

			try
			{
				return JsonConvert.DeserializeObject<T>(content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Invalid Marketstack response from '{uri.AbsolutePath}'.", error);
			}
		}
	}

	private static MarketstackError TryGetError(string content)
	{
		if (content.IsEmpty() || !content.TrimStart().StartsWith('{'))
			return null;
		try
		{
			return JsonConvert.DeserializeObject<MarketstackErrorEnvelope>(content,
				_jsonSettings)?.Error;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static MarketstackApiException CreateError(HttpStatusCode statusCode,
		MarketstackError error, string content, string path)
	{
		var details = error?.Message.IsEmpty(content);
		if (details?.Length > 1000)
			details = details[..1000];
		var code = error?.Code.IsEmpty() == false ? $" [{error.Code}]" : string.Empty;
		return new(statusCode, $"Marketstack request '{path}' failed " +
			$"({(int)statusCode} {statusCode}){code}" +
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

	private StringBuilder CreatePath(string endpoint)
		=> new StringBuilder(endpoint).Append("?access_key=").Append(Escape(_token));

	private static void Append(StringBuilder path, string name, string value)
	{
		if (!value.IsEmpty())
			path.Append('&').Append(name).Append('=').Append(Escape(value));
	}

	private static void Append(StringBuilder path, string name, long value)
		=> path.Append('&').Append(name).Append('=')
			.Append(value.ToString(CultureInfo.InvariantCulture));

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
