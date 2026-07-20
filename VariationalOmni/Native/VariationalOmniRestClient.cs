namespace StockSharp.VariationalOmni.Native;

sealed class VariationalOmniRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 4 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public VariationalOmniRestClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			(!_endpoint.Scheme.Equals(Uri.UriSchemeHttp,
				StringComparison.OrdinalIgnoreCase) &&
			 !_endpoint.Scheme.Equals(Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase)))
			throw new ArgumentException(
				"Variational Omni endpoint must be an HTTP or HTTPS URI.",
				nameof(endpoint));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Variational-Omni-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "VARIATIONAL_OMNI_REST";

	public async ValueTask<VariationalOmniStatistics> GetStatisticsAsync(
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, "/metadata/stats"));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize(body);
			if (attempt < 3 && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body);
		}
	}

	private VariationalOmniStatistics Deserialize(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException(
				"Variational Omni returned an empty statistics response.");
		try
		{
			return JsonConvert.DeserializeObject<VariationalOmniStatistics>(
				body, _jsonSettings) ?? throw new InvalidDataException(
					"Variational Omni returned no statistics payload.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Variational Omni returned malformed statistics JSON.", error);
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddSeconds(1);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromSeconds(Math.Pow(2, attempt));
		await Task.Delay(delay.Min(TimeSpan.FromSeconds(30)),
			cancellationToken);
	}

	private static Exception CreateException(HttpStatusCode statusCode,
		string body)
	{
		VariationalOmniError error = null;
		try
		{
			if (!body.IsEmpty())
				error = JsonConvert.DeserializeObject<VariationalOmniError>(body);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message.IsEmpty() == false
			? error.Message
			: error?.Detail.IsEmpty() == false
				? error.Detail
				: body?.Trim().Truncate(512, string.Empty);
		return new InvalidOperationException(
			$"Variational Omni HTTP {(int)statusCode} ({statusCode}): " +
			(detail.IsEmpty() ? "request rejected" : detail));
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Variational Omni response exceeds the 4 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Variational Omni response exceeds the 4 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
