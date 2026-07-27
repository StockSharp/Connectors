namespace StockSharp.Bit2Me.Native;

sealed class Bit2MeRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
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
	private long _lastNonce;

	public Bit2MeRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_basePath = _endpoint.AbsolutePath.TrimEnd('/');
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"Bit2Me API key and secret must be configured together.");
		_secret = secretValue.IsEmpty()
			? null
			: Encoding.UTF8.GetBytes(secretValue);
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Bit2Me-Connector/1.0");
	}

	public override string Name => "Bit2Me_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && _secret is { Length: > 0 };

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<Bit2MeMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeMarket[]>("v1/trading/market-config",
			Bit2MeEmptyQuery.Instance, false, cancellationToken);

	public ValueTask<Bit2MeTicker[]> GetTickersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeTicker[]>("v2/trading/tickers",
			new Bit2MeMarketQuery { Symbol = symbol }, false, cancellationToken);

	public ValueTask<Bit2MeOrderBook> GetOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeOrderBook>("v2/trading/order-book",
			new Bit2MeMarketQuery { Symbol = symbol }, false, cancellationToken);

	public ValueTask<Bit2MePublicTrade[]> GetPublicTradesAsync(string symbol,
		int limit, CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MePublicTrade[]>("v1/trading/trade/last",
			new Bit2MePublicTradesQuery { Symbol = symbol, Limit = limit }, false,
			cancellationToken);

	public ValueTask<Bit2MeCandle[]> GetCandlesAsync(Bit2MeCandleQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeCandle[]>("v1/trading/candle", query, false,
			cancellationToken);

	public ValueTask<Bit2MeWallet[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeWallet[]>("v1/trading/wallet/balance",
			Bit2MeEmptyQuery.Instance, true, cancellationToken);

	public ValueTask<Bit2MeOrder[]> GetOrdersAsync(Bit2MeOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeOrder[]>("v1/trading/order", query, true,
			cancellationToken);

	public ValueTask<Bit2MeOrder> GetOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeOrder>(
			$"v1/trading/order/{Uri.EscapeDataString(
				orderId.ThrowIfEmpty(nameof(orderId)))}",
			Bit2MeEmptyQuery.Instance, true, cancellationToken);

	public ValueTask<Bit2MeTrade[]> GetOrderTradesAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendGetAsync<Bit2MeTrade[]>(
			$"v1/trading/order/{Uri.EscapeDataString(
				orderId.ThrowIfEmpty(nameof(orderId)))}/trades",
			Bit2MeEmptyQuery.Instance, true, cancellationToken);

	public async ValueTask<Bit2MeTrade[]> GetTradesAsync(Bit2MeTradesQuery query,
		CancellationToken cancellationToken)
	{
		var token = await SendGetAsync<JToken>("v1/trading/trade", query, true,
			cancellationToken);
		if (token is JArray array)
			return array.ToObject<Bit2MeTrade[]>(JsonSerializer.Create(
				_jsonSettings)) ?? [];
		return token?["data"]?.ToObject<Bit2MeTrade[]>(
			JsonSerializer.Create(_jsonSettings)) ?? [];
	}

	public ValueTask<Bit2MeOrder> PlaceOrderAsync(Bit2MeOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<Bit2MeOrder, Bit2MeOrderRequest>(HttpMethod.Post,
			"v1/trading/order", request, cancellationToken);

	public ValueTask<Bit2MeOrder> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendDeleteAsync<Bit2MeOrder>(
			$"v1/trading/order/{Uri.EscapeDataString(
				orderId.ThrowIfEmpty(nameof(orderId)))}",
			cancellationToken);

	internal static string SerializeBody(object value)
		=> JsonConvert.SerializeObject(value, new JsonSerializerSettings
		{
			DateParseHandling = DateParseHandling.None,
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None,
			Culture = CultureInfo.InvariantCulture,
		});

	internal static string CreateSignature(string secret, long nonce, string url,
		string body = null)
	{
		secret.ThrowIfEmpty(nameof(secret));
		url.ThrowIfEmpty(nameof(url));
		var message = body.IsEmpty()
			? $"{nonce.ToString(CultureInfo.InvariantCulture)}:{url}"
			: $"{nonce.ToString(CultureInfo.InvariantCulture)}:{url}:{body}";
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(message));
		using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
		return Convert.ToBase64String(hmac.ComputeHash(hash));
	}

	private async ValueTask<TResponse> SendGetAsync<TResponse>(string path,
		IBit2MeQuery query, bool isAuthenticated,
		CancellationToken cancellationToken)
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
				AddAuthentication(request, GetSignaturePath(target));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return response.StatusCode == HttpStatusCode.NoContent ||
					responseBody.IsEmpty()
						? default
						: Deserialize<TResponse>(responseBody);
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendBodyAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest body,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		var target = path.TrimStart('/');
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_endpoint, target))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, GetSignaturePath(target), json);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return response.StatusCode == HttpStatusCode.NoContent ||
			responseBody.IsEmpty()
				? default
				: Deserialize<TResponse>(responseBody);
	}

	private async ValueTask<TResponse> SendDeleteAsync<TResponse>(string path,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var target = path.TrimStart('/');
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete,
			new Uri(_endpoint, target));
		AddAuthentication(request, GetSignaturePath(target));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return response.StatusCode == HttpStatusCode.NoContent || body.IsEmpty()
			? default
			: Deserialize<TResponse>(body);
	}

	private void AddAuthentication(HttpRequestMessage request, string url,
		string body = null)
	{
		var nonce = NextNonce();
		request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
		request.Headers.TryAddWithoutValidation("x-nonce",
			nonce.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("api-signature",
			CreateSignature(Encoding.UTF8.GetString(_secret), nonce, url, body));
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(ref _lastNonce, next, current) ==
				current)
				return next;
		}
	}

	private static string BuildTarget(string path, IBit2MeQuery query)
	{
		var queryString = query.GetParameters()
			.Where(static parameter =>
				!parameter.Name.IsEmpty() && parameter.Value is not null)
			.Select(static parameter =>
				Uri.EscapeDataString(parameter.Name) + "=" +
				Uri.EscapeDataString(parameter.Value))
			.Join("&");
		return path.TrimStart('/') +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private string GetSignaturePath(string target)
		=> (_basePath.IsEmpty() ? string.Empty : _basePath) +
			"/" + target.TrimStart('/');

	private TResponse Deserialize<TResponse>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException(
					"Bit2Me returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bit2Me returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bit2Me API key and secret are required for private operations.");
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(225);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(300 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(HttpStatusCode statusCode,
		string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<Bit2MeError>(body);
			if (error is not null)
				details = new[]
				{
					error.Code,
					error.Error,
					error.Message,
					error.Description,
				}.Where(static value => !value.IsEmpty()).Join(": ");
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Bit2Me HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}
}
