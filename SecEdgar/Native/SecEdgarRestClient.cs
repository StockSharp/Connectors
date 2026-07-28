namespace StockSharp.SecEdgar.Native;

sealed class SecEdgarRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly Uri _dataEndpoint;
	private readonly Uri _websiteEndpoint;
	private readonly string _userAgent;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTimeOffset _lastRequest;

	public SecEdgarRestClient(Uri dataEndpoint, Uri websiteEndpoint,
		string userAgent, TimeSpan requestInterval,
		HttpMessageHandler handler = null)
	{
		ValidateEndpoint(dataEndpoint, nameof(dataEndpoint));
		ValidateEndpoint(websiteEndpoint, nameof(websiteEndpoint));
		_dataEndpoint = EnsureTrailingSlash(dataEndpoint);
		_websiteEndpoint = EnsureTrailingSlash(websiteEndpoint);
		_userAgent = userAgent.ThrowIfEmpty(nameof(userAgent)).Trim();
		if (!_userAgent.Contains('@',
			StringComparison.Ordinal))
			throw new ArgumentException(
				"SEC EDGAR User-Agent must include a contact email.",
				nameof(userAgent));
		_requestInterval = requestInterval >=
			TimeSpan.FromMilliseconds(100) &&
			requestInterval <= TimeSpan.FromSeconds(10)
				? requestInterval
				: throw new ArgumentOutOfRangeException(
					nameof(requestInterval));
		if (handler is null)
			handler = new HttpClientHandler
			{
				AutomaticDecompression =
					DecompressionMethods.GZip |
					DecompressionMethods.Deflate,
			};
		_http = new(handler, true)
		{
			Timeout = TimeSpan.FromMinutes(2),
		};
	}

	public ValueTask<JObject> GetTickersAsync(
		CancellationToken cancellationToken)
		=> GetObjectAsync(new(_websiteEndpoint,
			"files/company_tickers_exchange.json"),
			cancellationToken);

	public ValueTask<JObject> GetSubmissionAsync(string cik,
		CancellationToken cancellationToken)
		=> GetObjectAsync(new(_dataEndpoint,
			$"submissions/{cik.NormalizeCik()}.json"),
			cancellationToken);

	public ValueTask<JObject> GetSubmissionFileAsync(string name,
		CancellationToken cancellationToken)
		=> GetObjectAsync(new(_dataEndpoint,
			$"submissions/{Uri.EscapeDataString(
				name.ThrowIfEmpty(nameof(name)))}"),
			cancellationToken);

	public ValueTask<JObject> GetCompanyFactsAsync(string cik,
		CancellationToken cancellationToken)
		=> GetObjectAsync(new(_dataEndpoint,
			$"api/xbrl/companyfacts/{cik.NormalizeCik()}.json"),
			cancellationToken);

	private async ValueTask<JObject> GetObjectAsync(Uri address,
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var wait = _requestInterval -
				(DateTimeOffset.UtcNow - _lastRequest);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);
			using var request = new HttpRequestMessage(
				HttpMethod.Get, address);
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue(
					"application/json"));
			request.Headers.TryAddWithoutValidation(
				"User-Agent", _userAgent);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			_lastRequest = DateTimeOffset.UtcNow;
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					$"SEC EDGAR returned HTTP " +
						$"{(int)response.StatusCode}.",
					null, response.StatusCode);
			try
			{
				return JObject.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"SEC EDGAR returned invalid JSON.", error);
			}
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private static void ValidateEndpoint(Uri value, string name)
	{
		if (value is null || !value.IsAbsoluteUri ||
			value.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"SEC EDGAR endpoints must be absolute HTTPS URIs.",
				name);
	}

	private static Uri EnsureTrailingSlash(Uri value)
		=> value.AbsoluteUri.EndsWith("/",
			StringComparison.Ordinal)
				? value
				: new(value.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}
}
