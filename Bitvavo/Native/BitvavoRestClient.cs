namespace StockSharp.Bitvavo.Native;

sealed class BitvavoRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private const long _accessWindow = 10000;
	private readonly Uri _endpoint;
	private readonly string _basePath;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private long _lastTimestamp;

	public BitvavoRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_basePath = _endpoint.AbsolutePath.TrimEnd('/');
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"Bitvavo API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bitvavo-Connector/1.0");
	}

	public override string Name => nameof(Bitvavo) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _secret is { Length: > 0 };

	public string ApiKey => _apiKey;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public long CreateWebSocketTimestamp() => NextTimestamp();

	public string CreateWebSocketSignature(long timestamp)
		=> Sign("GET", "/v2/websocket", string.Empty, timestamp);

	public ValueTask<BitvavoMarket[]> GetMarketsAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoMarket[]>("markets", BitvavoEmptyQuery.Instance, false,
			cancellationToken);

	public ValueTask<BitvavoTicker> GetTickerAsync(string market,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoTicker>("ticker/24h",
			new BitvavoMarketQuery { Market = market }, false, cancellationToken);

	public ValueTask<BitvavoOrderBook> GetDepthAsync(string market,
		BitvavoDepthQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoOrderBook>(
			$"{Uri.EscapeDataString(market)}/book", query, false, cancellationToken);

	public ValueTask<BitvavoPublicTrade[]> GetPublicTradesAsync(string market,
		BitvavoTradesQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoPublicTrade[]>(
			$"{Uri.EscapeDataString(market)}/trades", query, false, cancellationToken);

	public ValueTask<BitvavoCandle[]> GetCandlesAsync(string market,
		BitvavoCandlesQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoCandle[]>(
			$"{Uri.EscapeDataString(market)}/candles", query, false, cancellationToken);

	public ValueTask<BitvavoBalance[]> GetBalancesAsync(BitvavoBalanceQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoBalance[]>("balance", query, true, cancellationToken);

	public ValueTask<BitvavoOrder[]> GetOpenOrdersAsync(BitvavoOpenOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoOrder[]>("ordersOpen", query, true, cancellationToken);

	public ValueTask<BitvavoOrder[]> GetOrdersAsync(BitvavoOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoOrder[]>("orders", query, true, cancellationToken);

	public ValueTask<BitvavoFill[]> GetPrivateTradesAsync(
		BitvavoPrivateTradesQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoFill[]>("trades", query, true, cancellationToken);

	public ValueTask<BitvavoOrder> GetOrderAsync(BitvavoOrderLookupQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitvavoOrder>("order", query, true, cancellationToken);

	public ValueTask<BitvavoOrder> PlaceOrderAsync(BitvavoOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BitvavoOrder, BitvavoOrderRequest>(HttpMethod.Post, "order",
			request, cancellationToken);

	public ValueTask<BitvavoOrder> UpdateOrderAsync(BitvavoUpdateOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BitvavoOrder, BitvavoUpdateOrderRequest>(HttpMethod.Put, "order",
			request, cancellationToken);

	public ValueTask<BitvavoCancelResult> CancelOrderAsync(BitvavoCancelOrderQuery query,
		CancellationToken cancellationToken)
		=> SendDeleteAsync<BitvavoCancelResult>("order", query, cancellationToken);

	public ValueTask<BitvavoCancelResult[]> CancelOrdersAsync(
		BitvavoCancelOrdersQuery query, CancellationToken cancellationToken)
		=> SendDeleteAsync<BitvavoCancelResult[]>("orders", query, cancellationToken);

	private async ValueTask<TResponse> SendGetAsync<TResponse>(string path,
		IBitvavoQuery query, bool isAuthenticated, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isAuthenticated)
			EnsureCredentials();
		var target = BuildTarget(path, query);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, target));
			if (isAuthenticated)
				AddAuthentication(request, "GET", GetSignaturePath(target), string.Empty);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return response.StatusCode == HttpStatusCode.NoContent || body.IsEmpty()
					? default
					: Deserialize<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendDeleteAsync<TResponse>(string path,
		IBitvavoQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		EnsureCredentials();
		var target = BuildTarget(path, query);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete,
			new Uri(_endpoint, target));
		AddAuthentication(request, "DELETE", GetSignaturePath(target), string.Empty);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return response.StatusCode == HttpStatusCode.NoContent || body.IsEmpty()
			? default
			: Deserialize<TResponse>(body);
	}

	private async ValueTask<TResponse> SendBodyAsync<TResponse, TRequest>(HttpMethod method,
		string path, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		var target = path.TrimStart('/');
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method, new Uri(_endpoint, target))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, method.Method.ToUpperInvariant(),
			GetSignaturePath(target), json);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return response.StatusCode == HttpStatusCode.NoContent || responseBody.IsEmpty()
			? default
			: Deserialize<TResponse>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, string method, string path,
		string body)
	{
		var timestamp = NextTimestamp();
		request.Headers.TryAddWithoutValidation("Bitvavo-Access-Key", _apiKey);
		request.Headers.TryAddWithoutValidation("Bitvavo-Access-Timestamp",
			timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("Bitvavo-Access-Window",
			_accessWindow.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("Bitvavo-Access-Signature",
			Sign(method, path, body, timestamp));
	}

	private string Sign(string method, string path, string body, long timestamp)
	{
		EnsureCredentials();
		var payload = timestamp.ToString(CultureInfo.InvariantCulture) + method + path + body;
		using var hmac = new HMACSHA256(_secret);
		return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
			.ToLowerInvariant();
	}

	private long NextTimestamp()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastTimestamp);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(ref _lastTimestamp, next, current) == current)
				return next;
		}
	}

	private string GetSignaturePath(string target)
		=> (_basePath.IsEmpty() ? string.Empty : _basePath) + "/" + target.TrimStart('/');

	private static string BuildTarget(string path, IBitvavoQuery query)
	{
		var queryString = query.GetParameters()
			.Where(static parameter => !parameter.Name.IsEmpty() && parameter.Value is not null)
			.Select(static parameter => Uri.EscapeDataString(parameter.Name) + "=" +
				Uri.EscapeDataString(parameter.Value))
			.Join("&");
		return path.TrimStart('/') + (queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		try
		{
			if (body.AsSpan().TrimStart().StartsWith("{"))
			{
				var error = JsonConvert.DeserializeObject<BitvavoError>(body,
					_jsonSettings);
				if (error is not null &&
					(error.ErrorCode is not null || !error.Error.IsEmpty()))
					throw new InvalidOperationException(FormatError(error));
			}
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException("Bitvavo returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitvavo returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitvavo API key and secret are required for private operations.");
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(75);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		string details;
		try
		{
			var error = JsonConvert.DeserializeObject<BitvavoError>(body, _jsonSettings);
			details = error is null ? body?.Trim() : FormatError(error);
		}
		catch (JsonException)
		{
			details = body?.Trim();
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Bitvavo HTTP {(int)statusCode} ({statusCode}): {details}".Trim(), null,
			statusCode);
	}

	private static string FormatError(BitvavoError error)
		=> error.ErrorCode is null
			? error.Error
			: $"{error.ErrorCode}: {error.Error}".Trim();
}
