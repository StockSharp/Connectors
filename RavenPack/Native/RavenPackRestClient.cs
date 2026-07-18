namespace StockSharp.RavenPack.Native;

sealed class RavenPackApiException : InvalidOperationException
{
	public RavenPackApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class RavenPackRestClient : BaseLogReceiver
{
	private const int _maxResponseSize = 64 * 1024 * 1024;

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly int _maxAttempts;

	public RavenPackRestClient(Uri address, string apiKey, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_maxAttempts = Math.Max(1, maxAttempts);
		apiKey.ThrowIfEmpty(nameof(apiKey));
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("API_KEY", apiKey);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-RavenPack/1.0");
	}

	public Task<RavenPackDataset> GetDataset(string datasetId,
		CancellationToken cancellationToken)
		=> Get<RavenPackDataset>(
			$"datasets/{Escape(datasetId.ThrowIfEmpty(nameof(datasetId)))}",
			false, cancellationToken);

	public Task<RavenPackMappingResponse> MapEntity(RavenPackIdentifier identifier,
		CancellationToken cancellationToken)
		=> Post<RavenPackMappingRequest, RavenPackMappingResponse>("entity-mapping",
			new() { Identifiers = [identifier] }, false, cancellationToken);

	public Task<RavenPackEntityReference> GetEntityReference(string entityId,
		CancellationToken cancellationToken)
		=> Get<RavenPackEntityReference>(
			$"entity-reference/{Escape(entityId.ThrowIfEmpty(nameof(entityId)))}",
			true, cancellationToken);

	public Task<RavenPackRecordsResponse> GetRecords(string datasetId,
		RavenPackJsonQueryRequest query, CancellationToken cancellationToken)
		=> Post<RavenPackJsonQueryRequest, RavenPackRecordsResponse>(
			$"json/{Escape(datasetId.ThrowIfEmpty(nameof(datasetId)))}",
			query ?? throw new ArgumentNullException(nameof(query)), true, cancellationToken);

	public async Task<string> GetDocumentUrl(string documentId,
		CancellationToken cancellationToken)
	{
		var response = await Get<RavenPackDocumentUrlResponse>(
			$"document/{Escape(documentId.ThrowIfEmpty(nameof(documentId)))}/url",
			true, cancellationToken);
		return response?.Url;
	}

	private Task<T> Get<T>(string path, bool isNotFoundEmpty,
		CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, isNotFoundEmpty, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body,
		bool isNotFoundEmpty, CancellationToken cancellationToken)
		where TRequest : class
		=> Send<TResponse>(HttpMethod.Post, path,
			JsonConvert.SerializeObject(body ?? throw new ArgumentNullException(nameof(body)),
				_jsonSettings), isNotFoundEmpty, cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, string body,
		bool isNotFoundEmpty, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, uri);
			if (body != null)
			{
				request.Content = new StringContent(body, Encoding.UTF8, "application/json");
			}

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (IsTransient(response.StatusCode) && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}

			var content = await ReadContent(response.Content, cancellationToken);
			if (isNotFoundEmpty && response.StatusCode == HttpStatusCode.NotFound)
				return default;
			if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Accepted)
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
					$"RavenPack returned invalid JSON for '{uri.AbsolutePath}'.", error);
			}
		}
	}

	internal static RavenPackApiException CreateError(HttpStatusCode statusCode,
		string content, string path)
	{
		RavenPackErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<RavenPackErrorResponse>(content,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}

		var details = response?.Message;
		if (details.IsEmpty() && response?.Errors?.Length > 0)
		{
			details = string.Join("; ", response.Errors.Where(error => error != null)
				.Select(error => error.Message.IsEmpty(error.Type).IsEmpty(error.Field)));
		}
		details = details.IsEmpty(content)?.Trim();
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode,
			$"RavenPack request '{path}' failed ({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static async Task<string> ReadContent(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content == null)
			return null;
		if (content.Headers.ContentLength is > _maxResponseSize)
			throw new InvalidDataException("RavenPack response exceeds 64 MiB.");

		await using var input = await content.ReadAsStreamAsync(cancellationToken);
		using var output = new MemoryStream();
		var buffer = new byte[64 * 1024];
		while (true)
		{
			var read = await input.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (output.Length + read > _maxResponseSize)
				throw new InvalidDataException("RavenPack response exceeds 64 MiB.");
			output.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
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
