namespace StockSharp.MtNewswires.Native;

sealed class MtNewswiresApiException : InvalidOperationException
{
	public MtNewswiresApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class MtNewswiresClient : BaseLogReceiver
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

	public MtNewswiresClient(Uri address, string token, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ??
			throw new ArgumentNullException(nameof(address)));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-MtNewswires/1.0");
	}

	public Task<MtNewswiresArticle[]> GetLatest(string dataSource,
		string datasetId, string symbol, int count,
		CancellationToken cancellationToken)
	{
		if (count <= 0)
			throw new ArgumentOutOfRangeException(nameof(count), count, null);
		return Get(dataSource, datasetId, symbol,
			$"last={count.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
	}

	public Task<MtNewswiresArticle[]> GetRange(string dataSource,
		string datasetId, string symbol, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from, null);
		var query = $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
		return Get(dataSource, datasetId, symbol, query, cancellationToken);
	}

	private Task<MtNewswiresArticle[]> Get(string dataSource, string datasetId,
		string symbol, string query, CancellationToken cancellationToken)
	{
		ValidatePathSegment(dataSource, nameof(dataSource));
		ValidatePathSegment(datasetId, nameof(datasetId));
		var path = new StringBuilder("data/")
			.Append(Uri.EscapeDataString(dataSource))
			.Append('/')
			.Append(Uri.EscapeDataString(datasetId));
		if (!symbol.IsEmpty())
		{
			ValidateSymbol(symbol);
			path.Append('/').Append(Uri.EscapeDataString(symbol));
		}
		var requestPath = path.ToString();
		var address = new Uri(_address,
			$"{requestPath}?{query}&format=json&token={Uri.EscapeDataString(_token)}");
		return Send(address, requestPath, cancellationToken);
	}

	private async Task<MtNewswiresArticle[]> Send(Uri address, string requestPath,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException) when (attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(
					$"viaNexus request '{requestPath}' failed: {Redact(error.Message)}");
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
				attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				throw new TimeoutException(
					$"viaNexus request '{requestPath}' timed out.");
			}

			using (response)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);

				if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode is >= 500 and <= 511) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateError(response.StatusCode, content, requestPath);
				if (content.IsEmpty())
					return [];

				try
				{
					return JsonConvert.DeserializeObject<MtNewswiresArticle[]>(
						content, _jsonSettings) ?? [];
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						$"viaNexus returned invalid JSON for '{requestPath}'.", error);
				}
			}
		}
	}

	private MtNewswiresApiException CreateError(HttpStatusCode statusCode,
		string content, string requestPath)
	{
		ViaNexusErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<ViaNexusErrorResponse>(
				content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = Redact(response?.GetMessage().IsEmpty(content))?.Trim();
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode,
			$"viaNexus request '{requestPath}' failed " +
			$"({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static void ValidatePathSegment(string value, string name)
	{
		value.ThrowIfEmpty(name);
		if (value.Any(character => !char.IsLetterOrDigit(character) &&
			character is not '_' and not '-'))
		{
			throw new ArgumentException(
				$"viaNexus {name} contains unsupported characters.", name);
		}
	}

	private static void ValidateSymbol(string symbol)
	{
		if (symbol.Any(character => char.IsControl(character) ||
			character is '/' or '\\' or '?' or '#'))
		{
			throw new ArgumentException("viaNexus symbol is invalid.", nameof(symbol));
		}
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is { } delta)
			return ClampDelay(delta);
		if (response?.Headers.RetryAfter?.Date is { } date)
			return ClampDelay(date.UtcDateTime - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) :
			delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	private string Redact(string value)
		=> value?.Replace(_token, "***", StringComparison.Ordinal);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
