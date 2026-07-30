namespace StockSharp.OpenFigi.Native;

sealed class OpenFigiRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly Uri _endpoint;
	private readonly string _key;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTimeOffset _lastRequest;

	public OpenFigiRestClient(Uri endpoint, SecureString key,
		TimeSpan requestInterval, HttpMessageHandler handler = null)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"OpenFIGI endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));
		_endpoint = endpoint.AbsoluteUri.EndsWith("/",
			StringComparison.Ordinal)
				? endpoint
				: new(endpoint.AbsoluteUri + "/");
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
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

	public async ValueTask<OpenFigiInstrument[]> MapAsync(
		JObject job, CancellationToken cancellationToken)
	{
		if (job is null)
			throw new ArgumentNullException(nameof(job));
		var response = await PostAsync("mapping",
			new JArray(job), cancellationToken);
		if (response is not JArray array || array.Count != 1 ||
			array[0] is not JObject result)
			throw new InvalidDataException(
				"OpenFIGI mapping returned an invalid response.");
		var error = result.Value<string>("error");
		if (!error.IsEmpty())
			throw new InvalidOperationException(error);
		if (!result.Value<string>("warning").IsEmpty())
			return [];
		return result["data"]?.ToObject<OpenFigiInstrument[]>() ?? [];
	}

	public async ValueTask<OpenFigiInstrument[]> SearchAsync(
		JObject criteria, bool useSearch, int maximumPages,
		int maximumResults, CancellationToken cancellationToken)
	{
		if (criteria is null)
			throw new ArgumentNullException(nameof(criteria));
		if (maximumPages is < 1 or > 150)
			throw new ArgumentOutOfRangeException(
				nameof(maximumPages));
		if (maximumResults is < 1 or > 15000)
			throw new ArgumentOutOfRangeException(
				nameof(maximumResults));
		var request = (JObject)criteria.DeepClone();
		var result = new List<OpenFigiInstrument>(
			Math.Min(maximumResults, 100));
		var cursors = new HashSet<string>(
			StringComparer.Ordinal);

		for (var page = 0;
			page < maximumPages && result.Count < maximumResults;
			page++)
		{
			var response = await PostAsync(
				useSearch ? "search" : "filter", request,
				cancellationToken);
			if (response is not JObject root)
				throw new InvalidDataException(
					"OpenFIGI search returned an invalid response.");
			var data = root["data"]?
				.ToObject<OpenFigiInstrument[]>() ?? [];
			result.AddRange(data.Take(maximumResults - result.Count));
			var next = root.Value<string>("next");
			if (next.IsEmpty())
				break;
			if (!cursors.Add(next))
				throw new InvalidDataException(
					"OpenFIGI returned a repeated page cursor.");
			request["start"] = next;
		}

		return [.. result];
	}

	private async ValueTask<JToken> PostAsync(string path,
		JToken body, CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var wait = _requestInterval -
				(DateTimeOffset.UtcNow - _lastRequest);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);
			using var request = new HttpRequestMessage(
				HttpMethod.Post, new Uri(_endpoint, path))
			{
				Content = new StringContent(
					body.ToString(Formatting.None), Encoding.UTF8,
					"application/json"),
			};
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue(
					"application/json"));
			request.Headers.TryAddWithoutValidation(
				"User-Agent", "StockSharp OpenFIGI connector");
			if (!_key.IsEmpty())
				request.Headers.TryAddWithoutValidation(
					"X-OPENFIGI-APIKEY", _key);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			_lastRequest = DateTimeOffset.UtcNow;
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					$"OpenFIGI returned HTTP " +
						$"{(int)response.StatusCode}: {text}",
					null, response.StatusCode);
			try
			{
				return JToken.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"OpenFIGI returned invalid JSON.", error);
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
