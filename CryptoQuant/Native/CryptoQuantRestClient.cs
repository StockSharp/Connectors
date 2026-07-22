namespace StockSharp.CryptoQuant.Native;

sealed class CryptoQuantApiException : InvalidOperationException
{
	public CryptoQuantApiException(HttpStatusCode statusCode,
		CryptoQuantStatuses apiStatus, string message)
		: base(message)
	{
		StatusCode = statusCode;
		ApiStatus = apiStatus;
	}

	public HttpStatusCode StatusCode { get; }
	public CryptoQuantStatuses ApiStatus { get; }
}

sealed class CryptoQuantRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 128 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _requestInterval;
	private readonly string _accessToken;
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

	public CryptoQuantRestClient(string endpoint, SecureString accessToken,
		TimeSpan requestInterval)
	{
		var root = ValidateEndpoint(endpoint);
		if (accessToken.IsEmpty())
			throw new ArgumentNullException(nameof(accessToken));
		if (requestInterval < TimeSpan.Zero ||
			requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));

		_accessToken = accessToken.UnSecure().Trim();
		if (_accessToken.IsEmpty() || _accessToken.Length > 4096 ||
			_accessToken.Any(character => char.IsControl(character) ||
				char.IsWhiteSpace(character)))
			throw new ArgumentException(
				"CryptoQuant access token is empty or invalid.",
				nameof(accessToken));

		_requestInterval = requestInterval;
		_client = new()
		{
			BaseAddress = root,
			Timeout = TimeSpan.FromSeconds(60),
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-CryptoQuant/1.0");
		_client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", _accessToken);
	}

	public override string Name => "CryptoQuant_REST";

	public async ValueTask<CryptoQuantEndpoint[]> GetEndpointsAsync(
		CancellationToken cancellationToken)
	{
		var response = await GetAsync<CryptoQuantResponse<CryptoQuantEndpoint>>(
			"discovery/endpoints", cancellationToken);
		ValidateResponse(response, "endpoint discovery");
		return response.Result.Data ?? [];
	}

	public async ValueTask<CryptoQuantOhlcv[]> GetOhlcvAsync(
		CryptoQuantInstrument instrument, CryptoQuantWindows window,
		DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));
		if (limit is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to,
			nameof(from));

		var routeNamespace = CryptoQuantExtensions.NormalizeIdentifier(
			instrument.Namespace, nameof(instrument.Namespace));
		var path = routeNamespace + "/market-data/price-ohlcv?window=" +
			Escape(window.ToWire()) +
			"&from=" + Escape(from.FormatCryptoQuantTime()) +
			"&to=" + Escape(to.FormatCryptoQuantTime()) +
			"&limit=" + Format(limit);
		if (instrument.Kind == CryptoQuantInstrumentKinds.Token)
			path += "&token=" + Escape(CryptoQuantExtensions.NormalizeIdentifier(
				instrument.Token, nameof(instrument.Token)));

		var response = await GetAsync<CryptoQuantResponse<CryptoQuantOhlcv>>(
			path, cancellationToken);
		ValidateResponse(response, "OHLCV");
		if (response.Result.Window != window)
			throw new InvalidDataException(
				"CryptoQuant returned a different OHLCV window.");
		return response.Result.Data ?? [];
	}

	private static void ValidateResponse<TItem>(
		CryptoQuantResponse<TItem> response, string operation)
	{
		if (response?.Status is null)
			throw new InvalidDataException(
				$"CryptoQuant {operation} response has no status.");
		if (response.Status.Code != 200)
			throw new CryptoQuantApiException(HttpStatusCode.OK,
				response.Status.Message,
				$"CryptoQuant {operation} failed: {response.Status.Message} ({response.Status.Code}).");
		if (response.Result is null)
			throw new InvalidDataException(
				$"CryptoQuant {operation} response has no result.");
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
				catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					if (attempt < 3)
					{
						await DelayRetryAsync(attempt, null, cancellationToken);
						continue;
					}
					throw new IOException("CryptoQuant REST transport failed: " +
						Redact(error.Message));
				}

				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var bytes = await ReadResponseAsync(response, cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						try
						{
							return JsonConvert.DeserializeObject<T>(
								Encoding.UTF8.GetString(bytes), _settings);
						}
						catch (JsonException error)
						{
							throw new InvalidDataException(
								"CryptoQuant returned invalid JSON.", error);
						}
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
		var remaining = _requestInterval - (DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromMinutes(1))
			delay = TimeSpan.FromMinutes(1);
		await Task.Delay(delay, cancellationToken);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private CryptoQuantApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		CryptoQuantErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<CryptoQuantErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var apiStatus = error?.Status?.Message ?? CryptoQuantStatuses.Unknown;
		var detail = apiStatus == CryptoQuantStatuses.Unknown
			? statusCode.ToString()
			: apiStatus.ToString();
		return new(statusCode, apiStatus,
			$"CryptoQuant REST request failed ({(int)statusCode}): {Redact(detail)}");
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long contentLength &&
			contentLength > _maximumResponseLength)
			throw new InvalidDataException("CryptoQuant response is too large.");
		await using var stream = await response.Content.ReadAsStreamAsync(
			cancellationToken);
		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		while (true)
		{
			var read = await stream.ReadAsync(chunk, cancellationToken);
			if (read == 0)
				break;
			if (buffer.Length + read > _maximumResponseLength)
				throw new InvalidDataException("CryptoQuant response is too large.");
			buffer.Write(chunk, 0, read);
		}
		return buffer.ToArray();
	}

	private static Uri ValidateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty() ||
			!uri.AbsolutePath.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"CryptoQuant endpoint must be an HTTPS API v1 root without credentials, query, or fragment.",
				nameof(endpoint));
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private string Redact(string value)
		=> value.IsEmpty()
			? value
			: value.Replace(_accessToken, "***", StringComparison.Ordinal);

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
