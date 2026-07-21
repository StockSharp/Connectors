namespace StockSharp.Kaiko.Native;

sealed class KaikoApiException(HttpStatusCode statusCode, string message) : InvalidOperationException(message)
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class KaikoRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 64 * 1024 * 1024;
	private readonly HttpClient _referenceClient;
	private readonly HttpClient _marketClient;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _requestInterval;
	private readonly bool _isAuthenticated;
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _lastRequestTime;
	private bool _isDisposed;

	public KaikoRestClient(string referenceEndpoint, string marketEndpoint,
		SecureString apiKey, TimeSpan requestInterval)
	{
		if (requestInterval < TimeSpan.Zero ||
			requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));
		_requestInterval = requestInterval;
		_referenceClient = CreateClient(referenceEndpoint, "Reference");
		_marketClient = CreateClient(marketEndpoint, "Market");
		var key = apiKey?.UnSecure()?.Trim();
		_isAuthenticated = !key.IsEmpty();
		if (_isAuthenticated)
			_marketClient.DefaultRequestHeaders.TryAddWithoutValidation(
				"X-Api-Key", key);
	}

	public override string Name => "Kaiko_REST";

	public async ValueTask ValidateAsync(CancellationToken cancellationToken)
	{
		var response = await GetInstrumentsAsync(null,
			KaikoInstrumentClasses.Unknown, null, null, null, 1,
			cancellationToken);
		if (response.Length == 0)
			throw new InvalidDataException(
				"Kaiko reference API returned no instruments.");
	}

	public async ValueTask<KaikoInstrument[]> GetInstrumentsAsync(
		string exchange, KaikoInstrumentClasses instrumentClass, string code,
		string baseAsset, string quoteAsset, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var path = new StringBuilder("v1/instruments?limit=")
			.Append(Format(limit));
		if (limit > 1)
			path.Append("&orderBy=trade_count&order=-1");
		Append(path, "exchange_code", exchange);
		if (instrumentClass != KaikoInstrumentClasses.Unknown)
			Append(path, "class", instrumentClass.ToWire());
		Append(path, "code", code);
		Append(path, "base_asset", baseAsset);
		Append(path, "quote_asset", quoteAsset);
		var response = await GetAsync<KaikoReferenceResponse>(_referenceClient,
			path.ToString(), false, cancellationToken) ?? throw new
			InvalidDataException("Kaiko reference API returned an empty response.");
		EnsureSuccess(response.Result, "reference");
		return response.Data ?? [];
	}

	public async ValueTask<KaikoMarketResponse<KaikoTrade>> GetTradesAsync(
		KaikoSecurityKey key, DateTime from, DateTime to, int pageSize,
		string continuationToken, CancellationToken cancellationToken)
	{
		EnsureMarketAccess();
		var path = GetMarketPath("v3/data/trades.v1", key, "trades");
		path += BuildPageQuery(from, to, pageSize, continuationToken, '?');
		var response = await GetAsync<KaikoMarketResponse<KaikoTrade>>(
			_marketClient, path, true, cancellationToken) ?? throw new
			InvalidDataException("Kaiko trades API returned an empty response.");
		EnsureSuccess(response.Result, "trades");
		return response;
	}

	public async ValueTask<KaikoMarketResponse<KaikoOhlcv>> GetOhlcvAsync(
		KaikoSecurityKey key, TimeSpan timeFrame, DateTime from, DateTime to,
		int pageSize, string continuationToken,
		CancellationToken cancellationToken)
	{
		EnsureMarketAccess();
		var path = GetMarketPath("v2/data/trades.v1", key,
			"aggregations/ohlcv");
		if (continuationToken.IsEmpty())
			path += "?interval=" + Escape(timeFrame.ToAggregate()) +
				BuildPageQuery(from, to, pageSize, null, '&');
		else
			path += BuildPageQuery(from, to, pageSize, continuationToken, '?');
		var response = await GetAsync<KaikoMarketResponse<KaikoOhlcv>>(
			_marketClient, path, true, cancellationToken) ?? throw new
			InvalidDataException("Kaiko OHLCV API returned an empty response.");
		EnsureSuccess(response.Result, "OHLCV");
		return response;
	}

	private async ValueTask<T> GetAsync<T>(HttpClient client, string path,
		bool isAuthenticated, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (isAuthenticated)
			EnsureMarketAccess();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				await EnforceRequestIntervalAsync(cancellationToken);
				using var request = new HttpRequestMessage(HttpMethod.Get, path);
				HttpResponseMessage response;
				try
				{
					response = await client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
				}
				catch (Exception error) when (attempt < 3 &&
					!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					await DelayRetryAsync(attempt, null, cancellationToken);
					continue;
				}
				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var body = await ReadResponseAsync(response,
						cancellationToken);
					if (response.IsSuccessStatusCode)
						return body.Length == 0 ? default :
							JsonConvert.DeserializeObject<T>(
								Encoding.UTF8.GetString(body), _settings);
					if (attempt < 3 && IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta,
							cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, body);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private void EnsureMarketAccess()
	{
		if (!_isAuthenticated)
			throw new InvalidOperationException(
				"Kaiko market data requires an API key.");
	}

	private async ValueTask EnforceRequestIntervalAsync(
		CancellationToken cancellationToken)
	{
		var remaining = _requestInterval -
			(DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ??
			TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(30))
			delay = TimeSpan.FromSeconds(30);
		await Task.Delay(delay, cancellationToken);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private KaikoApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		KaikoErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<KaikoErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var detail = error?.Message.IsEmpty(error?.Error)
			.IsEmpty(error?.Detail);
		var message = detail.IsEmpty()
			? $"Kaiko API returned HTTP {(int)statusCode} ({statusCode})."
			: $"Kaiko API returned HTTP {(int)statusCode}: {detail}";
		return new(statusCode, message);
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Kaiko response is too large ({length} bytes).");
		var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (body.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Kaiko response is too large ({body.Length} bytes).");
		return body;
	}

	private static HttpClient CreateClient(string endpoint, string component)
	{
		var client = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			BaseAddress = ValidateEndpoint(endpoint, component),
			Timeout = TimeSpan.FromSeconds(60),
		};
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Kaiko/1.0");
		return client;
	}

	private static Uri ValidateEndpoint(string endpoint, string component)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty() || !uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				$"A valid HTTPS Kaiko {component} endpoint is required.",
				nameof(endpoint));
		return uri;
	}

	private static string GetMarketPath(string version,
		KaikoSecurityKey key, string resource)
		=> version + "/exchanges/" + Escape(key.Exchange) + "/" +
			Escape(key.InstrumentClass.ToWire()) + "/" + Escape(key.Code) +
			"/" + resource;

	private static string BuildPageQuery(DateTime from, DateTime to,
		int pageSize, string continuationToken, char prefix)
	{
		if (!continuationToken.IsEmpty())
			return prefix + "continuation_token=" + Escape(continuationToken);
		if (pageSize is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to,
			nameof(from));
		return prefix + "start_time=" + Escape(Format(from)) + "&end_time=" +
			Escape(Format(to)) + "&sort=asc&page_size=" + Format(pageSize);
	}

	private static void Append(StringBuilder path, string name, string value)
	{
		if (!value.IsEmpty())
			path.Append('&').Append(name).Append('=').Append(Escape(value.Trim()));
	}

	private static void EnsureSuccess(KaikoResults result, string resource)
	{
		if (result != KaikoResults.Success)
			throw new InvalidDataException(
				$"Kaiko {resource} API returned result '{result}'.");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string Format(DateTime value)
		=> value.EnsureUtc().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
			CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_referenceClient.Dispose();
		_marketClient.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
