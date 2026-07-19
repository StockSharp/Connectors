namespace StockSharp.Luno.Native;

sealed class LunoRestClient : BaseLogReceiver
{
	private sealed class RateGate : IDisposable
	{
		private readonly SemaphoreSlim _sync = new(1, 1);
		private readonly TimeSpan _interval;
		private DateTime _nextRequest;

		public RateGate(TimeSpan interval)
		{
			_interval = interval;
		}

		public async ValueTask WaitAsync(CancellationToken cancellationToken)
		{
			await _sync.WaitAsync(cancellationToken);
			try
			{
				var delay = _nextRequest - DateTime.UtcNow;
				if (delay > TimeSpan.Zero)
					await Task.Delay(delay, cancellationToken);
				_nextRequest = DateTime.UtcNow + _interval;
			}
			finally
			{
				_sync.Release();
			}
		}

		public void Dispose() => _sync.Dispose();
	}

	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly string _authorization;
	private readonly RateGate _publicGate = new(TimeSpan.FromMilliseconds(1050));
	private readonly RateGate _privateGate = new(TimeSpan.FromMilliseconds(205));
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};

	public LunoRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = CreateEndpoint(endpoint);
		var apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var apiSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (apiKey.IsEmpty() != apiSecret.IsEmpty())
			throw new ArgumentException(
				"Luno API key and secret must be configured together.");
		if (!apiKey.IsEmpty())
			_authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(
				$"{apiKey}:{apiSecret}"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Luno-Connector/1.0");
	}

	public override string Name => "Luno_REST";

	public bool IsCredentialsAvailable => !_authorization.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_publicGate.Dispose();
		_privateGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<LunoMarketInfo[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await GetPublicAsync<LunoMarketsResponse>(
			"/api/exchange/1/markets", null, cancellationToken)).Markets ?? [];

	public ValueTask<LunoTicker> GetTickerAsync(string pair,
		CancellationToken cancellationToken)
		=> GetPublicAsync<LunoTicker>("/api/1/ticker",
			LunoQueryWriter.Create(new LunoTickerRequest
			{
				Pair = pair.NormalizeSymbol(),
			}),
			cancellationToken);

	public ValueTask<LunoOrderBook> GetOrderBookAsync(string pair,
		CancellationToken cancellationToken)
		=> GetPublicAsync<LunoOrderBook>("/api/1/orderbook_top",
			LunoQueryWriter.Create(new LunoTickerRequest
			{
				Pair = pair.NormalizeSymbol(),
			}),
			cancellationToken);

	public async ValueTask<LunoPublicTrade[]> GetPublicTradesAsync(
		LunoTradesRequest request, CancellationToken cancellationToken)
		=> (await GetPublicAsync<LunoPublicTradesResponse>("/api/1/trades",
			LunoQueryWriter.Create(request), cancellationToken)).Trades ?? [];

	public async ValueTask<LunoCandle[]> GetCandlesAsync(
		LunoCandlesRequest request, CancellationToken cancellationToken)
		=> (await GetPrivateAsync<LunoCandlesResponse>(
			"/api/exchange/1/candles", LunoQueryWriter.Create(request),
			cancellationToken)).Candles ?? [];

	public async ValueTask<LunoBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> (await GetPrivateAsync<LunoBalancesResponse>("/api/1/balance", null,
			cancellationToken)).Balances ?? [];

	public async ValueTask<LunoOrder[]> GetOrdersAsync(
		LunoOrderListRequest request, CancellationToken cancellationToken)
		=> (await GetPrivateAsync<LunoOrdersResponse>(
			"/api/exchange/2/listorders", LunoQueryWriter.Create(request),
			cancellationToken)).Orders ?? [];

	public ValueTask<LunoOrder> GetOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> GetPrivateAsync<LunoOrder>("/api/exchange/3/order",
			LunoQueryWriter.Create(new LunoOrderLookupRequest
			{
				OrderId = orderId,
			}),
			cancellationToken);

	public async ValueTask<LunoUserTrade[]> GetUserTradesAsync(
		LunoUserTradesRequest request, CancellationToken cancellationToken)
		=> (await GetPrivateAsync<LunoUserTradesResponse>("/api/1/listtrades",
			LunoQueryWriter.Create(request), cancellationToken)).Trades ?? [];

	public ValueTask<LunoIdResponse> PlaceLimitOrderAsync(
		LunoLimitOrderRequest request, CancellationToken cancellationToken)
		=> PostPrivateAsync<LunoIdResponse>("/api/1/postorder",
			LunoQueryWriter.Create(request), cancellationToken);

	public ValueTask<LunoIdResponse> PlaceMarketOrderAsync(
		LunoMarketOrderRequest request, CancellationToken cancellationToken)
		=> PostPrivateAsync<LunoIdResponse>("/api/1/marketorder",
			LunoQueryWriter.Create(request), cancellationToken);

	public ValueTask<LunoSuccessResponse> CancelOrderAsync(
		LunoCancelOrderRequest request, CancellationToken cancellationToken)
		=> PostPrivateAsync<LunoSuccessResponse>("/api/1/stoporder",
			LunoQueryWriter.Create(request), cancellationToken);

	private ValueTask<TResponse> GetPublicAsync<TResponse>(string path,
		string query, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(false, HttpMethod.Get, path, query, null,
			_publicGate, true, cancellationToken);

	private ValueTask<TResponse> GetPrivateAsync<TResponse>(string path,
		string query, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendAsync<TResponse>(true, HttpMethod.Get, path, query, null,
			_privateGate, true, cancellationToken);
	}

	private ValueTask<TResponse> PostPrivateAsync<TResponse>(string path,
		string form, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendAsync<TResponse>(true, HttpMethod.Post, path, null, form,
			_privateGate, false, cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(bool isPrivate,
		HttpMethod method, string path, string query, string form, RateGate gate,
		bool canRetry, CancellationToken cancellationToken)
	{
		var requestPath = query.IsEmpty() ? path : $"{path}?{query}";
		for (var attempt = 0; ; attempt++)
		{
			await gate.WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, requestPath.TrimStart('/')));
			if (form is not null)
				request.Content = new StringContent(form, Encoding.UTF8,
					"application/x-www-form-urlencoded");
			if (isPrivate)
				request.Headers.Authorization = new AuthenticationHeaderValue(
					"Basic", _authorization);

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (canRetry && attempt < _maximumReadAttempts - 1 &&
				IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body);
			return Deserialize<TResponse>(body);
		}
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("Luno returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
				throw new InvalidDataException(
					"Luno returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Luno returned an unexpected response shape.", error);
		}
	}

	private Exception CreateApiError(HttpStatusCode statusCode, string body)
	{
		LunoErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<LunoErrorResponse>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.Error;
		if (message.IsEmpty())
		{
			message = body?.Trim();
			if (message?.Length > 512)
				message = message[..512];
		}
		if (message.IsEmpty())
			message = "The API rejected the request.";
		return new LunoApiException(statusCode, error?.ErrorCode,
			$"Luno HTTP {(int)statusCode}" +
			(error?.ErrorCode.IsEmpty() == false
				? $" ({error.ErrorCode})"
				: string.Empty) + $": {message}");
	}

	private static Uri CreateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"Luno REST endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Luno API key and secret are required for this operation.");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			(response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
			TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}
}
