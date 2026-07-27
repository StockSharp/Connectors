namespace StockSharp.BtcTurk.Native;

sealed class BtcTurkRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private readonly Uri _restEndpoint;
	private readonly Uri _graphEndpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _apiSecret;
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

	public BtcTurkRestClient(string restEndpoint, string graphEndpoint,
		SecureString key, SecureString secret)
	{
		_restEndpoint = CreateEndpoint(restEndpoint, nameof(restEndpoint));
		_graphEndpoint = CreateEndpoint(graphEndpoint, nameof(graphEndpoint));
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"BtcTurk API key and secret must be configured together.");
		if (!secretValue.IsEmpty())
		{
			try
			{
				_apiSecret = Convert.FromBase64String(secretValue);
			}
			catch (FormatException error)
			{
				throw new ArgumentException(
					"BtcTurk API secret must be Base64 encoded.",
					nameof(secret), error);
			}
		}

		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-BtcTurk-Connector/1.0");
	}

	public override string Name => "BtcTurk_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && _apiSecret is { Length: > 0 };

	protected override void DisposeManaged()
	{
		if (_apiSecret is not null)
			CryptographicOperations.ZeroMemory(_apiSecret);
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BtcTurkExchangeInfo> GetExchangeInfoAsync(
		CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkExchangeInfo>(
			"api/v2/server/exchangeinfo", BtcTurkEmptyQuery.Instance, false,
			cancellationToken);

	public ValueTask<BtcTurkTicker[]> GetTickersAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkTicker[]>("api/v2/ticker",
			new BtcTurkMarketQuery { PairSymbol = pairSymbol }, false,
			cancellationToken);

	public ValueTask<BtcTurkOrderBook> GetOrderBookAsync(string pairSymbol,
		int depth, CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkOrderBook>("api/v2/orderbook",
			new BtcTurkOrderBookQuery
			{
				PairSymbol = pairSymbol,
				Depth = depth,
			}, false, cancellationToken);

	public ValueTask<BtcTurkPublicTrade[]> GetPublicTradesAsync(
		string pairSymbol, int count, CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkPublicTrade[]>("api/v2/trades",
			new BtcTurkPublicTradesQuery
			{
				PairSymbol = pairSymbol,
				Count = count,
			}, false, cancellationToken);

	public ValueTask<BtcTurkKline> GetKlinesAsync(BtcTurkKlineQuery query,
		CancellationToken cancellationToken)
		=> SendDirectGetAsync<BtcTurkKline>(_graphEndpoint,
			"v1/klines/history", query, cancellationToken);

	public ValueTask<BtcTurkBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkBalance[]>("api/v1/users/balances",
			BtcTurkEmptyQuery.Instance, true, cancellationToken);

	public ValueTask<BtcTurkOpenOrders> GetOpenOrdersAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkOpenOrders>("api/v1/openOrders",
			new BtcTurkMarketQuery { PairSymbol = pairSymbol }, true,
			cancellationToken);

	public ValueTask<BtcTurkOrder[]> GetOrdersAsync(BtcTurkOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkOrder[]>("api/v1/allOrders", query, true,
			cancellationToken);

	public ValueTask<BtcTurkOrder> GetOrderAsync(long orderId,
		CancellationToken cancellationToken)
	{
		if (orderId <= 0)
			throw new ArgumentOutOfRangeException(nameof(orderId), orderId,
				"BtcTurk order identifier must be positive.");
		return SendApiGetAsync<BtcTurkOrder>(
			$"api/v1/order/{orderId.ToString(CultureInfo.InvariantCulture)}",
			BtcTurkEmptyQuery.Instance, true, cancellationToken);
	}

	public ValueTask<BtcTurkUserTrade[]> GetTradesAsync(
		BtcTurkTradesQuery query, CancellationToken cancellationToken)
		=> SendApiGetAsync<BtcTurkUserTrade[]>(
			"api/v1/users/transactions/trade", query, true,
			cancellationToken);

	public ValueTask<BtcTurkOrder> PlaceOrderAsync(
		BtcTurkOrderRequest request, CancellationToken cancellationToken)
		=> SendApiBodyAsync<BtcTurkOrder, BtcTurkOrderRequest>(
			HttpMethod.Post, "api/v1/order", request, cancellationToken);

	public async ValueTask CancelOrderAsync(long orderId,
		CancellationToken cancellationToken)
	{
		if (orderId <= 0)
			throw new ArgumentOutOfRangeException(nameof(orderId), orderId,
				"BtcTurk order identifier must be positive.");
		await SendApiDeleteAsync("api/v1/order",
			new BtcTurkCancelQuery { OrderId = orderId },
			cancellationToken);
	}

	internal static string SerializeBody(object value)
		=> JsonConvert.SerializeObject(value, new JsonSerializerSettings
		{
			DateParseHandling = DateParseHandling.None,
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None,
			Culture = CultureInfo.InvariantCulture,
		});

	internal static string CreateSignature(string apiKey,
		string secretBase64, long nonce)
	{
		apiKey.ThrowIfEmpty(nameof(apiKey));
		secretBase64.ThrowIfEmpty(nameof(secretBase64));
		var secret = Convert.FromBase64String(secretBase64);
		try
		{
			return CreateSignature(apiKey, secret, nonce);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(secret);
		}
	}

	private async ValueTask<TData> SendApiGetAsync<TData>(string path,
		IBtcTurkQuery query, bool isAuthenticated,
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
				new Uri(_restEndpoint, target));
			if (isAuthenticated)
				AddAuthentication(request);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return Unwrap<TData>(responseBody);
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TData> SendDirectGetAsync<TData>(Uri endpoint,
		string path, IBtcTurkQuery query,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		var target = BuildTarget(path, query);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(endpoint, target));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TData>(responseBody);
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TData> SendApiBodyAsync<TData, TRequest>(
		HttpMethod method, string path, TRequest body,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_restEndpoint, path.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8,
				"application/json"),
		};
		AddAuthentication(request);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return Unwrap<TData>(responseBody);
	}

	private async ValueTask SendApiDeleteAsync(string path,
		IBtcTurkQuery query, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var target = BuildTarget(path, query);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete,
			new Uri(_restEndpoint, target));
		AddAuthentication(request);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		if (!responseBody.IsEmpty())
			_ = Unwrap<JToken>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request)
	{
		var nonce = NextNonce();
		request.Headers.TryAddWithoutValidation("X-PCK", _apiKey);
		request.Headers.TryAddWithoutValidation("X-Stamp",
			nonce.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("X-Signature",
			CreateSignature(_apiKey, _apiSecret, nonce));
	}

	private static string CreateSignature(string apiKey, byte[] secret,
		long nonce)
	{
		var message = Encoding.UTF8.GetBytes(
			apiKey + nonce.ToString(CultureInfo.InvariantCulture));
		using var hmac = new HMACSHA256(secret);
		return Convert.ToBase64String(hmac.ComputeHash(message));
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = (long)(DateTime.UtcNow - DateTime.UnixEpoch)
				.TotalMilliseconds;
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(ref _lastNonce, next, current) ==
				current)
				return next;
		}
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
		=> new(endpoint.ThrowIfEmpty(parameterName).TrimEnd('/') + "/",
			UriKind.Absolute);

	private static string BuildTarget(string path, IBtcTurkQuery query)
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

	private TData Unwrap<TData>(string body)
	{
		var response = Deserialize<BtcTurkResponse<TData>>(body);
		if (!response.IsSuccess)
			throw new InvalidDataException(CreateErrorDetails(response));
		return response.Data;
	}

	private TData Deserialize<TData>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TData>(body, _jsonSettings)
				?? throw new InvalidDataException(
					"BtcTurk returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BtcTurk returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BtcTurk API key and secret are required for private operations.");
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(125);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
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
			var error = JsonConvert.DeserializeObject<
				BtcTurkResponse<JToken>>(body);
			if (error is not null)
				details = CreateErrorDetails(error);
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"BtcTurk HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}

	private static string CreateErrorDetails<TData>(
		BtcTurkResponse<TData> response)
		=> new[]
		{
			response.Code?.ToString(),
			response.Message,
			response.Details,
		}.Where(static value => !value.IsEmpty()).Join(": ")
			.IsEmpty("BtcTurk request failed.");
}

sealed class BtcTurkOrderBookQuery : IBtcTurkQuery
{
	public string PairSymbol { get; init; }
	public int Depth { get; init; }

	public BtcTurkParameter[] GetParameters()
		=>
		[
			new("pairSymbol",
				PairSymbol.ThrowIfEmpty(nameof(PairSymbol))),
			new("limit", Depth.Max(1).Min(100)
				.ToString(CultureInfo.InvariantCulture)),
		];
}
