namespace StockSharp.NasdaqDataLink.Native;

sealed class NasdaqDataLinkApiException : InvalidOperationException
{
	public NasdaqDataLinkApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class NasdaqDataLinkClient : BaseLogReceiver, IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
	};

	private readonly Uri _address;
	private readonly string _apiToken;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

	public NasdaqDataLinkClient(Uri address, string apiToken)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_apiToken = apiToken.ThrowIfEmpty(nameof(apiToken));
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Token", _apiToken);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-NasdaqDataLink/1.0");
	}

	public Task<NasdaqDataLinkSearchResponse> Search(NasdaqDataLinkSearchQuery query,
		CancellationToken cancellationToken)
		=> Get<NasdaqDataLinkSearchResponse>(CreateAddress(
			$"datasets.json?{query.ToQueryString()}"), cancellationToken);

	public async Task<NasdaqDataLinkDataset> GetMetadata(string databaseCode,
		string datasetCode, CancellationToken cancellationToken)
	{
		var response = await Get<NasdaqDataLinkDatasetResponse>(CreateAddress(
			$"datasets/{Escape(databaseCode)}/{Escape(datasetCode)}/metadata.json"),
			cancellationToken);
		return response?.Dataset ?? throw new InvalidOperationException(
			$"Nasdaq Data Link returned no metadata for '{databaseCode}/{datasetCode}'.");
	}

	public async Task<NasdaqDataLinkDatasetData> GetData(string databaseCode,
		string datasetCode, NasdaqDataLinkDataQuery query,
		CancellationToken cancellationToken)
	{
		var response = await Get<NasdaqDataLinkDataResponse>(CreateAddress(
			$"datasets/{Escape(databaseCode)}/{Escape(datasetCode)}/data.json?{query.ToQueryString()}"),
			cancellationToken);
		return response?.DatasetData ?? throw new InvalidOperationException(
			$"Nasdaq Data Link returned no observations for '{databaseCode}/{datasetCode}'.");
	}

	private Uri CreateAddress(string relative)
		=> new(_address, relative);

	private async Task<T> Get<T>(Uri address, CancellationToken cancellationToken)
		where T : class
	{
		for (var attempt = 0; attempt < 4; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);

			if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode is >= 500 and <= 511) && attempt < 3)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (response.StatusCode == HttpStatusCode.NoContent)
				return null;
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address);
			if (body.IsEmpty())
				return null;

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"Nasdaq Data Link returned an empty response for '{address}'.");
		}

		throw new InvalidOperationException(
			$"Nasdaq Data Link request '{address}' exhausted its retry limit.");
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		var delay = response.Headers.RetryAfter?.Delta;
		if (delay == null && response.Headers.RetryAfter?.Date != null)
			delay = response.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
		if (delay != null && delay.Value > TimeSpan.Zero)
			return delay.Value > TimeSpan.FromSeconds(30)
				? TimeSpan.FromSeconds(30)
				: delay.Value;
		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static NasdaqDataLinkApiException CreateApiError(HttpStatusCode statusCode,
		string body, Uri address)
	{
		NasdaqDataLinkErrorEnvelope envelope = null;
		try
		{
			envelope = JsonConvert.DeserializeObject<NasdaqDataLinkErrorEnvelope>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var details = envelope?.Error?.Message;
		if (envelope?.Error?.Code.IsEmpty() == false)
			details = $"{envelope.Error.Code}: {details}";
		if (details.IsEmpty())
			details = body?.Length > 1000 ? body[..1000] : body;
		return new NasdaqDataLinkApiException(statusCode,
			$"Nasdaq Data Link request '{address}' failed ({(int)statusCode} {statusCode}): {details}");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static Uri EnsureTrailingSlash(Uri address)
	{
		var value = address.AbsoluteUri;
		return value.EndsWith('/') ? address : new Uri(value + "/");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
