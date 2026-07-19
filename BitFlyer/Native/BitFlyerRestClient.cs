namespace StockSharp.BitFlyer.Native;

sealed class BitFlyerRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private readonly Lock _timestampSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private DateTime _nextRequest;
	private long _lastTimestamp;

	public BitFlyerRestClient(string endpoint, SecureString key,
		SecureString secret)
	{
		_endpoint = CreateEndpoint(endpoint);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"bitFlyer API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);

		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-bitFlyer-Connector/1.0");
	}

	public override string Name => "BitFlyer_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && _secret is not null;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_requestSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BitFlyerMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendPublicAsync<BitFlyerMarket[]>(HttpMethod.Get, "/v1/getmarkets",
			null, cancellationToken);

	public ValueTask<BitFlyerBoard> GetBoardAsync(BitFlyerProductRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPublicAsync<BitFlyerBoard>(HttpMethod.Get, "/v1/getboard",
			request.ToQueryString(), cancellationToken);
	}

	public ValueTask<BitFlyerTicker> GetTickerAsync(BitFlyerProductRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPublicAsync<BitFlyerTicker>(HttpMethod.Get, "/v1/getticker",
			request.ToQueryString(), cancellationToken);
	}

	public ValueTask<BitFlyerPublicExecution[]> GetExecutionsAsync(
		BitFlyerPublicExecutionsRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPublicAsync<BitFlyerPublicExecution[]>(HttpMethod.Get,
			"/v1/getexecutions", request.ToQueryString(), cancellationToken);
	}

	public ValueTask<BitFlyerBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendPrivateGetAsync<BitFlyerBalance[]>("/v1/me/getbalance", null,
			cancellationToken);

	public ValueTask<BitFlyerCollateral> GetCollateralAsync(
		CancellationToken cancellationToken)
		=> SendPrivateGetAsync<BitFlyerCollateral>("/v1/me/getcollateral", null,
			cancellationToken);

	public ValueTask<BitFlyerPosition[]> GetPositionsAsync(
		BitFlyerProductRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateGetAsync<BitFlyerPosition[]>("/v1/me/getpositions",
			request.ToQueryString(), cancellationToken);
	}

	public ValueTask<BitFlyerChildOrderAcceptance> PlaceChildOrderAsync(
		BitFlyerChildOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateAsync<BitFlyerChildOrderAcceptance,
			BitFlyerChildOrderRequest>(HttpMethod.Post, "/v1/me/sendchildorder",
			request, false, cancellationToken);
	}

	public ValueTask<BitFlyerParentOrderAcceptance> PlaceParentOrderAsync(
		BitFlyerParentOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateAsync<BitFlyerParentOrderAcceptance,
			BitFlyerParentOrderRequest>(HttpMethod.Post, "/v1/me/sendparentorder",
			request, false, cancellationToken);
	}

	public ValueTask CancelChildOrderAsync(BitFlyerCancelChildOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateNoContentAsync(HttpMethod.Post,
			"/v1/me/cancelchildorder", request, cancellationToken);
	}

	public ValueTask CancelParentOrderAsync(
		BitFlyerCancelParentOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateNoContentAsync(HttpMethod.Post,
			"/v1/me/cancelparentorder", request, cancellationToken);
	}

	public ValueTask CancelAllAsync(BitFlyerCancelAllRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateNoContentAsync(HttpMethod.Post,
			"/v1/me/cancelallchildorders", request, cancellationToken);
	}

	public ValueTask<BitFlyerChildOrder[]> GetChildOrdersAsync(
		BitFlyerChildOrdersRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateGetAsync<BitFlyerChildOrder[]>(
			"/v1/me/getchildorders", request.ToQueryString(), cancellationToken);
	}

	public ValueTask<BitFlyerParentOrder[]> GetParentOrdersAsync(
		BitFlyerParentOrdersRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateGetAsync<BitFlyerParentOrder[]>(
			"/v1/me/getparentorders", request.ToQueryString(), cancellationToken);
	}

	public ValueTask<BitFlyerAccountExecution[]> GetAccountExecutionsAsync(
		BitFlyerAccountExecutionsRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateGetAsync<BitFlyerAccountExecution[]>(
			"/v1/me/getexecutions", request.ToQueryString(), cancellationToken);
	}

	public BitFlyerRpcAuthParameters CreateWebSocketAuthentication()
	{
		EnsureCredentials();
		var timestamp = CreateTimestamp();
		var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
		return new()
		{
			ApiKey = _apiKey,
			Timestamp = timestamp,
			Nonce = nonce,
			Signature = Sign(timestamp.ToString(CultureInfo.InvariantCulture) +
				nonce),
		};
	}

	private async ValueTask<TResponse> SendPublicAsync<TResponse>(
		HttpMethod method, string path, string query,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				CreateUri(path, query));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendPrivateGetAsync<TResponse>(
		string path, string query, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var uri = CreateUri(path, query);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			AddAuthentication(request, uri.PathAndQuery, string.Empty);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendPrivateAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest payload, bool isRetrySafe,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var body = Serialize(payload);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var uri = CreateUri(path, null);
			using var request = new HttpRequestMessage(method, uri)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			};
			AddAuthentication(request, uri.PathAndQuery, body);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(responseBody);
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask SendPrivateNoContentAsync<TRequest>(HttpMethod method,
		string path, TRequest payload, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var body = Serialize(payload);
		await WaitRateLimitAsync(cancellationToken);
		var uri = CreateUri(path, null);
		using var request = new HttpRequestMessage(method, uri)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, uri.PathAndQuery, body);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, string path,
		string body)
	{
		var timestamp = CreateTimestamp().ToString(CultureInfo.InvariantCulture);
		var signature = Sign(timestamp + request.Method.Method + path + body);
		request.Headers.TryAddWithoutValidation("ACCESS-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("ACCESS-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("ACCESS-SIGN", signature);
	}

	private string Serialize<TValue>(TValue value)
		=> JsonConvert.SerializeObject(value, _jsonSettings);

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("bitFlyer returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
				throw new InvalidDataException(
					"bitFlyer returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"bitFlyer returned an unexpected response shape.", error);
		}
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		try
		{
			var error = JsonConvert.DeserializeObject<BitFlyerApiError>(
				body ?? string.Empty, _jsonSettings);
			if (error is not null && (error.Status != 0 || !error.Message.IsEmpty()))
				return new BitFlyerApiException(error.Status,
					$"bitFlyer HTTP {(int)statusCode}: {error.Message}");
		}
		catch (JsonException)
		{
		}

		var details = body?.Trim();
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"bitFlyer HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}

	private string Sign(string payload)
	{
		using var hmac = new HMACSHA256(_secret);
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
	}

	private long CreateTimestamp()
	{
		using (_timestampSync.EnterScope())
		{
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (timestamp <= _lastTimestamp)
				timestamp = _lastTimestamp + 1;
			_lastTimestamp = timestamp;
			return timestamp;
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(1050);
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"bitFlyer API key and secret are required for private operations.");
	}

	private static Uri CreateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"bitFlyer REST endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}

	private Uri CreateUri(string path, string query)
	{
		var uri = new Uri(_endpoint,
			path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
		return query.IsEmpty() ? uri : new UriBuilder(uri) { Query = query }.Uri;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			(response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
			TimeSpan.FromMilliseconds(750 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}
}
