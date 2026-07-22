namespace StockSharp.Tardis.Native;

sealed class TardisApiException : InvalidOperationException
{
	public TardisApiException(HttpStatusCode statusCode, string code,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public string Code { get; }
}

sealed class TardisRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 128 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _requestInterval;
	private readonly string _apiKey;
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

	public TardisRestClient(string endpoint, SecureString apiKey,
		TimeSpan requestInterval)
	{
		var root = ValidateEndpoint(endpoint);
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		if (requestInterval < TimeSpan.Zero ||
			requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));

		_apiKey = apiKey.UnSecure().Trim();
		if (_apiKey.IsEmpty() || _apiKey.Length > 4096 ||
			_apiKey.Any(character => char.IsControl(character) ||
				char.IsWhiteSpace(character)))
			throw new ArgumentException(
				"Tardis API key is empty or invalid.", nameof(apiKey));

		_requestInterval = requestInterval;
		_client = new()
		{
			BaseAddress = root,
			Timeout = TimeSpan.FromSeconds(60),
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Tardis/1.0");
		_client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", _apiKey);
	}

	public override string Name => "Tardis_REST";

	public async ValueTask<TardisInstrument[]> GetInstrumentsAsync(
		string exchange, CancellationToken cancellationToken)
	{
		exchange = TardisExtensions.NormalizeExchange(exchange);
		var result = await GetAsync<TardisInstrument[]>(
			"instruments/" + Escape(exchange), cancellationToken);
		return result ?? [];
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
					throw new IOException("Tardis REST transport failed: " +
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
								"Tardis returned invalid JSON.", error);
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

	private TardisApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		TardisErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<TardisErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var code = error?.Code.IsEmpty(statusCode.ToString());
		var detail = error?.Message.IsEmpty(code) ?? code;
		return new(statusCode, code,
			$"Tardis REST request failed ({(int)statusCode}): {Redact(detail)}");
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long contentLength &&
			contentLength > _maximumResponseLength)
			throw new InvalidDataException("Tardis response is too large.");
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
				throw new InvalidDataException("Tardis response is too large.");
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
				"Tardis endpoint must be an HTTPS API v1 root without credentials, query, or fragment.",
				nameof(endpoint));
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private string Redact(string value)
		=> value.IsEmpty()
			? value
			: value.Replace(_apiKey, "***", StringComparison.Ordinal);

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
