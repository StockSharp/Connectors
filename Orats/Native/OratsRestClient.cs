namespace StockSharp.Orats.Native;

sealed class OratsApiException : InvalidOperationException
{
	public OratsApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class OratsRestClient : BaseLogReceiver
{
	private const int MaxResponseBytes = 128 * 1024 * 1024;

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly string _token;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };

	public OratsRestClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-ORATS/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await GetTickers("AAPL", cancellationToken);
	}

	public Task<OratsResponse<OratsTicker>> GetTickers(string ticker,
		CancellationToken cancellationToken)
	{
		var path = CreatePath("tickers");
		Append(path, "ticker", ticker);
		return Get<OratsResponse<OratsTicker>>(path.ToString(), cancellationToken);
	}

	public Task<OratsResponse<OratsSnapshot>> GetCurrentOptions(string tickers,
		OratsDataModes mode, CancellationToken cancellationToken)
	{
		var path = CreatePath(Current(mode, "strikes/options"));
		Append(path, "tickers", tickers.ThrowIfEmpty(nameof(tickers)));
		return Get<OratsResponse<OratsSnapshot>>(path.ToString(), cancellationToken);
	}

	public Task<OratsResponse<OratsStrike>> GetCurrentChain(string ticker,
		OratsDataModes mode, CancellationToken cancellationToken)
	{
		var path = CreatePath(Current(mode, "strikes"));
		Append(path, "ticker", ticker.ThrowIfEmpty(nameof(ticker)));
		return Get<OratsResponse<OratsStrike>>(path.ToString(), cancellationToken);
	}

	public Task<OratsResponse<OratsStrike>> GetHistoricalOption(string ticker,
		DateTime expiration, decimal strike, DateTime? tradeDate,
		CancellationToken cancellationToken)
	{
		var path = CreatePath("hist/strikes/options");
		Append(path, "ticker", ticker.ThrowIfEmpty(nameof(ticker)));
		Append(path, "expirDate", ToDate(expiration));
		Append(path, "strike", strike);
		Append(path, "tradeDate", tradeDate == null ? null : ToDate(tradeDate.Value));
		return Get<OratsResponse<OratsStrike>>(path.ToString(), cancellationToken);
	}

	public Task<OratsResponse<OratsDaily>> GetDailies(string ticker,
		DateTime? tradeDate, CancellationToken cancellationToken)
	{
		var path = CreatePath("hist/dailies");
		Append(path, "ticker", ticker.ThrowIfEmpty(nameof(ticker)));
		Append(path, "tradeDate", tradeDate == null ? null : ToDate(tradeDate.Value));
		return Get<OratsResponse<OratsDaily>>(path.ToString(), cancellationToken);
	}

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue("application/json"));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (IsTransient(response.StatusCode) && attempt < _maxAttempts)
			{
				var delay = GetRetryDelay(response, attempt);
				response.Dispose();
				await Task.Delay(delay, cancellationToken);
				continue;
			}

			var content = await ReadContent(response.Content, cancellationToken);
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
					$"Invalid ORATS response from '{uri.AbsolutePath}'.", error);
			}
		}
	}

	private static async Task<string> ReadContent(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content?.Headers.ContentLength is > MaxResponseBytes)
			throw new InvalidDataException("ORATS response exceeds the 128 MiB limit.");
		if (content == null)
			return null;

		using var input = await content.ReadAsStreamAsync(cancellationToken);
		using var output = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length),
				cancellationToken);
			if (read <= 0)
				break;
			if (output.Length + read > MaxResponseBytes)
				throw new InvalidDataException("ORATS response exceeds the 128 MiB limit.");
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
		return Encoding.UTF8.GetString(output.ToArray());
	}

	private static OratsApiException CreateError(HttpStatusCode statusCode,
		string content, string path)
	{
		OratsErrorResponse error = null;
		if (content?.TrimStart().StartsWith('{') == true)
		{
			try
			{
				error = JsonConvert.DeserializeObject<OratsErrorResponse>(content,
					_jsonSettings);
			}
			catch (JsonException)
			{
			}
		}
		var details = error?.Message;
		if (details.IsEmpty())
			details = content;
		if (details?.Length > 1000)
			details = details[..1000];
		var code = error?.Code.IsEmpty() == false ? $" [{error.Code}]" : string.Empty;
		var type = error?.Type.IsEmpty() == false ? $" {error.Type}" : string.Empty;
		return new(statusCode, $"ORATS request '{path}' failed " +
			$"({(int)statusCode} {statusCode}){type}{code}" +
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
		=> new StringBuilder(endpoint).Append("?token=").Append(Escape(_token));

	private static void Append(StringBuilder path, string name, string value)
	{
		if (!value.IsEmpty())
			path.Append('&').Append(name).Append('=').Append(Escape(value));
	}

	private static void Append(StringBuilder path, string name, decimal value)
		=> path.Append('&').Append(name).Append('=')
			.Append(value.ToString(CultureInfo.InvariantCulture));

	private static string Current(OratsDataModes mode, string endpoint)
		=> mode == OratsDataModes.Live ? $"live/{endpoint}" : endpoint;

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
