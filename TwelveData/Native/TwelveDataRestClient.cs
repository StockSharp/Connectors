namespace StockSharp.TwelveData.Native;

sealed class TwelveDataApiException : InvalidOperationException
{
	public TwelveDataApiException(HttpStatusCode statusCode, int? code, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}

sealed class TwelveDataRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly int _maxAttempts;

	public TwelveDataRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",
			"apikey " + token.ThrowIfEmpty(nameof(token)));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-TwelveData/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await Get<TwelveDataApiUsage>("api_usage", cancellationToken);
	}

	public Task<TwelveDataReferenceResponse> Search(string query, int outputSize,
		CancellationToken cancellationToken)
		=> Get<TwelveDataReferenceResponse>("symbol_search?symbol=" +
			Escape(query.ThrowIfEmpty(nameof(query))) + "&outputsize=" +
			Math.Clamp(outputSize, 1, 120).ToString(CultureInfo.InvariantCulture), cancellationToken);

	public Task<TwelveDataReferenceResponse> GetStocks(string symbol, string country,
		string exchange, string micCode, CancellationToken cancellationToken)
		=> GetReference("stocks", symbol, country, exchange, micCode, cancellationToken);

	public Task<TwelveDataReferenceResponse> GetEtfs(string symbol, string country,
		string exchange, string micCode, CancellationToken cancellationToken)
		=> GetReference("etf", symbol, country, exchange, micCode, cancellationToken);

	public Task<TwelveDataReferenceResponse> GetForexPairs(string symbol,
		CancellationToken cancellationToken)
		=> GetReference("forex_pairs", symbol, null, null, null, cancellationToken);

	public Task<TwelveDataReferenceResponse> GetCryptocurrencies(string symbol, string exchange,
		CancellationToken cancellationToken)
		=> GetReference("cryptocurrencies", symbol, null, exchange, null, cancellationToken);

	public Task<TwelveDataReferenceResponse> GetCommodities(string symbol,
		CancellationToken cancellationToken)
		=> GetReference("commodities", symbol, null, null, null, cancellationToken);

	public Task<TwelveDataQuote> GetQuote(TwelveDataSecurityKey key, bool isPrePost,
		CancellationToken cancellationToken)
	{
		var path = new StringBuilder("quote?symbol=").Append(Escape(key.Symbol));
		AppendIdentity(path, key);
		if (isPrePost)
			path.Append("&prepost=true");
		return Get<TwelveDataQuote>(path.ToString(), cancellationToken);
	}

	public Task<TwelveDataTimeSeries> GetTimeSeries(TwelveDataSecurityKey key, string interval,
		DateTime from, DateTime to, int outputSize, TwelveDataAdjustments adjustment,
		bool isPrePost, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("time_series?symbol=").Append(Escape(key.Symbol))
			.Append("&interval=").Append(Escape(interval.ThrowIfEmpty(nameof(interval))))
			.Append("&start_date=").Append(Escape(ToDateTime(from)))
			.Append("&end_date=").Append(Escape(ToDateTime(to)))
			.Append("&outputsize=").Append(Math.Clamp(outputSize, 1, 5000)
				.ToString(CultureInfo.InvariantCulture))
			.Append("&order=asc&timezone=UTC&adjust=").Append(adjustment.ToNative());
		AppendIdentity(path, key);
		if (isPrePost)
			path.Append("&prepost=true");
		return Get<TwelveDataTimeSeries>(path.ToString(), cancellationToken);
	}

	private Task<TwelveDataReferenceResponse> GetReference(string endpoint, string symbol,
		string country, string exchange, string micCode, CancellationToken cancellationToken)
	{
		var path = new StringBuilder(endpoint);
		var separator = '?';
		AppendParameter(path, ref separator, "symbol", symbol);
		AppendParameter(path, ref separator, "country", country);
		AppendParameter(path, ref separator, "exchange", exchange);
		AppendParameter(path, ref separator, "mic_code", micCode);
		return Get<TwelveDataReferenceResponse>(path.ToString(), cancellationToken);
	}

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
		where T : TwelveDataResponse
	{
		var uri = new Uri(_address, path);
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);

			if (IsTransient((int)response.StatusCode) && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateHttpError(response.StatusCode, content, uri.AbsolutePath);
			if (content.IsEmpty())
				return null;

			T result;
			try
			{
				result = JsonConvert.DeserializeObject<T>(content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Invalid Twelve Data response from '{uri.AbsolutePath}'.", error);
			}

			if (result?.IsError != true)
				return result;
			if (IsTransient(result.Code ?? 0) && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			throw CreateError(response.StatusCode, result.Code, result.Message, uri.AbsolutePath);
		}
	}

	private static TwelveDataApiException CreateError(HttpStatusCode statusCode, int? code,
		string details, string path)
	{
		if (details?.Length > 1000)
			details = details[..1000];
		var apiCode = code == null ? string.Empty : $", API code {code.Value}";
		return new(statusCode, code,
			$"Twelve Data request '{path}' failed ({(int)statusCode} {statusCode}{apiCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static TwelveDataApiException CreateHttpError(HttpStatusCode statusCode,
		string content, string path)
	{
		TwelveDataResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<TwelveDataResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		return CreateError(statusCode, response?.Code,
			response?.Message.IsEmpty(content), path);
	}

	private static bool IsTransient(int code)
		=> code == 429 || code is >= 500 and <= 511;

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

	private static void AppendIdentity(StringBuilder path, TwelveDataSecurityKey key)
	{
		if (!key.Exchange.IsEmpty())
			path.Append("&exchange=").Append(Escape(key.Exchange));
		if (!key.MicCode.IsEmpty())
			path.Append("&mic_code=").Append(Escape(key.MicCode));
	}

	private static void AppendParameter(StringBuilder path, ref char separator, string name,
		string value)
	{
		if (value.IsEmpty())
			return;
		path.Append(separator).Append(name).Append('=').Append(Escape(value));
		separator = '&';
	}

	private static string ToDateTime(DateTime value)
		=> value.ToUtc().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
