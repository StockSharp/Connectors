namespace StockSharp.CoinMarketCap.Native;

sealed class CoinMarketCapApiException : InvalidOperationException
{
	public CoinMarketCapApiException(HttpStatusCode statusCode, int? errorCode,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
	}

	public HttpStatusCode StatusCode { get; }
	public int? ErrorCode { get; }
}

sealed class CoinMarketCapRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 64 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _requestInterval;
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

	public CoinMarketCapRestClient(string endpoint,
		CoinMarketCapAccessModes accessMode, SecureString apiKey,
		TimeSpan requestInterval)
	{
		var root = ValidateEndpoint(endpoint);
		if (!Enum.IsDefined(accessMode))
			throw new ArgumentOutOfRangeException(nameof(accessMode), accessMode,
				null);
		if (accessMode == CoinMarketCapAccessModes.ApiKey && apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		if (requestInterval < TimeSpan.Zero ||
			requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));
		_requestInterval = requestInterval;
		_client = new()
		{
			BaseAddress = root,
			Timeout = TimeSpan.FromSeconds(60),
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinMarketCap/1.0");
		if (accessMode == CoinMarketCapAccessModes.ApiKey)
			_client.DefaultRequestHeaders.TryAddWithoutValidation(
				"X-CMC_PRO_API_KEY", apiKey.UnSecure().Trim());
	}

	public override string Name => "CoinMarketCap_REST";

	public async ValueTask ValidateAsync(CancellationToken cancellationToken)
	{
		var result = await GetMapPageAsync(1, 1, cancellationToken);
		if (result.Length == 0 || result[0]?.Id <= 0)
			throw new InvalidDataException(
				"CoinMarketCap cryptocurrency map returned no instruments.");
	}

	public async ValueTask<CoinMarketCapMapEntry[]> GetMapPageAsync(int start,
		int limit, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
		if (limit is < 1 or > 5000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		return await GetEnvelopeAsync<CoinMarketCapMapEntry[]>(
			"v1/cryptocurrency/map?listing_status=active&sort=id&start=" +
			Format(start) + "&limit=" + Format(limit), cancellationToken) ?? [];
	}

	public async ValueTask<CoinMarketCapQuoteAsset> GetQuoteAsync(int id,
		string quoteCurrency, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
		var result = await GetEnvelopeAsync<CoinMarketCapQuoteAsset[]>(
			"v3/cryptocurrency/quotes/latest?id=" + Format(id) + "&convert=" +
			Escape(quoteCurrency) + "&skip_invalid=false", cancellationToken) ?? [];
		return result.FirstOrDefault(item => item?.Id == id);
	}

	public ValueTask<CoinMarketCapHistoricalData> GetOhlcvAsync(int id,
		string quoteCurrency, CoinMarketCapTimePeriods period, DateTime from,
		DateTime to, int count, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
		if (count is < 1 or > 10000)
			throw new ArgumentOutOfRangeException(nameof(count));
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to,
			nameof(from));
		var wirePeriod =
			CoinMarketCapEnumConverter<CoinMarketCapTimePeriods>.ToWire(period);
		var start = period == CoinMarketCapTimePeriods.Daily
			? from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: from.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
				CultureInfo.InvariantCulture);
		var end = period == CoinMarketCapTimePeriods.Daily
			? to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: to.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
				CultureInfo.InvariantCulture);
		return GetEnvelopeAsync<CoinMarketCapHistoricalData>(
			"v2/cryptocurrency/ohlcv/historical?id=" + Format(id) +
			"&time_period=" + Escape(wirePeriod) + "&time_start=" +
			Escape(start) + "&time_end=" + Escape(end) + "&count=" +
			Format(count) + "&interval=" + Escape(wirePeriod) + "&convert=" +
			Escape(quoteCurrency) + "&skip_invalid=false", cancellationToken);
	}

	private async ValueTask<T> GetEnvelopeAsync<T>(string path,
		CancellationToken cancellationToken)
	{
		var response = await GetAsync<CoinMarketCapResponse<T>>(path,
			cancellationToken) ?? throw new InvalidDataException(
				"CoinMarketCap returned an empty response.");
		if (response.Status is null)
			throw new InvalidDataException(
				"CoinMarketCap response has no status object.");
		if (response.Status.ErrorCode != 0)
			throw new CoinMarketCapApiException(HttpStatusCode.OK,
				response.Status.ErrorCode, FormatError(response.Status));
		return response.Data;
	}

	private async ValueTask<T> GetAsync<T>(string path,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
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
					response = await _client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
					var bytes = await ReadResponseAsync(response,
						cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						return JsonConvert.DeserializeObject<T>(
							Encoding.UTF8.GetString(bytes), _settings);
					}
					if (attempt < 3 && IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, bytes);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
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

	private CoinMarketCapApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		CoinMarketCapErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<CoinMarketCapErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var status = error?.Status;
		var message = status is null
			? $"CoinMarketCap API returned HTTP {(int)statusCode} ({statusCode})."
			: $"CoinMarketCap API returned HTTP {(int)statusCode}: " +
				FormatError(status);
		return new(statusCode, status?.ErrorCode, message);
	}

	private static string FormatError(CoinMarketCapStatus status)
	{
		var detail = status.ErrorMessage.IsEmpty("request failed");
		if (!status.ErrorDetail.IsEmpty())
			detail += " " + status.ErrorDetail;
		return $"CoinMarketCap error {status.ErrorCode}: {detail}";
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"CoinMarketCap response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"CoinMarketCap response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private static Uri ValidateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty())
			throw new ArgumentException(
				"A valid HTTPS CoinMarketCap endpoint is required.",
				nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"CoinMarketCap endpoint cannot contain a query or fragment.",
				nameof(endpoint));
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
