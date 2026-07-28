namespace StockSharp.SimFin.Native;

sealed class SimFinRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly Uri _endpoint;
	private readonly string _key;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTimeOffset _lastRequest;

	public SimFinRestClient(Uri endpoint, SecureString key,
		TimeSpan requestInterval, HttpMessageHandler handler = null)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"SimFin endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));
		_endpoint = endpoint.AbsoluteUri.EndsWith("/",
			StringComparison.Ordinal)
				? endpoint
				: new(endpoint.AbsoluteUri + "/");
		_key = key.IsEmpty()
			? throw new ArgumentNullException(nameof(key))
			: key.UnSecure().Trim();
		_requestInterval = requestInterval >= TimeSpan.Zero &&
			requestInterval <= TimeSpan.FromMinutes(1)
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

	public async ValueTask<SimFinCompany[]> GetCompaniesAsync(
		CancellationToken cancellationToken)
		=> (await GetAsync("companies/list", null,
			cancellationToken)).ToCompanies();

	public async ValueTask<SimFinPrice[]> GetPricesAsync(
		string ticker, DateTime from, DateTime to, bool ratios,
		bool asReported, CancellationToken cancellationToken)
		=> (await GetAsync("companies/prices/compact",
			new Dictionary<string, string>
			{
				["ticker"] = ticker.ThrowIfEmpty(nameof(ticker)),
				["ratios"] = ratios.ToString().ToLowerInvariant(),
				["asreported"] =
					asReported.ToString().ToLowerInvariant(),
				["start"] = from.ToString("yyyy-MM-dd",
					CultureInfo.InvariantCulture),
				["end"] = to.ToString("yyyy-MM-dd",
					CultureInfo.InvariantCulture),
			}, cancellationToken)).ToPrices();

	public async ValueTask<SimFinFundamental[]>
		GetFundamentalsAsync(string ticker, string statements,
			string period, DateTime? from, DateTime? to,
			bool asReported, CancellationToken cancellationToken)
	{
		var query = new Dictionary<string, string>
		{
			["ticker"] = ticker.ThrowIfEmpty(nameof(ticker)),
			["statements"] =
				statements.ThrowIfEmpty(nameof(statements)),
			["period"] = period,
			["ttm"] = "false",
			["asreported"] =
				asReported.ToString().ToLowerInvariant(),
		};
		if (from is not null)
			query["start"] = from.Value.ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture);
		if (to is not null)
			query["end"] = to.Value.ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture);
		return (await GetAsync("companies/statements/compact",
			query, cancellationToken)).ToFundamentals();
	}

	private async ValueTask<JToken> GetAsync(string path,
		IReadOnlyDictionary<string, string> query,
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var wait = _requestInterval -
				(DateTimeOffset.UtcNow - _lastRequest);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);
			var suffix = query is null
				? string.Empty
				: "?" + query
					.Where(static pair => !pair.Value.IsEmpty())
					.Select(static pair =>
						$"{Uri.EscapeDataString(pair.Key)}=" +
						Uri.EscapeDataString(pair.Value))
					.Join("&");
			using var request = new HttpRequestMessage(
				HttpMethod.Get, new Uri(_endpoint, path + suffix));
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue(
					"application/json"));
			request.Headers.TryAddWithoutValidation(
				"Authorization", _key);
			request.Headers.TryAddWithoutValidation(
				"User-Agent", "StockSharp SimFin connector");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			_lastRequest = DateTimeOffset.UtcNow;
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					$"SimFin returned HTTP " +
						$"{(int)response.StatusCode}: {text}",
					null, response.StatusCode);
			try
			{
				return JToken.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"SimFin returned invalid JSON.", error);
			}
		}
		finally
		{
			_requestSync.Release();
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}
}
