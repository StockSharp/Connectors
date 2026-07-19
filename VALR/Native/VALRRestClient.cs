namespace StockSharp.VALR.Native;

sealed class VALRRestClient : BaseLogReceiver
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
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly string _subAccountId;
	private readonly RateGate _publicGate = new(TimeSpan.FromMilliseconds(2050));
	private readonly RateGate _bucketGate = new(TimeSpan.FromMilliseconds(55));
	private readonly RateGate _privateGate = new(TimeSpan.FromMilliseconds(35));
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};

	public VALRRestClient(string endpoint, SecureString key,
		SecureString secret, string subAccountId)
	{
		_endpoint = CreateEndpoint(endpoint);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		_apiSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		_subAccountId = subAccountId?.Trim();
		if (_apiKey.IsEmpty() != _apiSecret.IsEmpty())
			throw new ArgumentException(
				"VALR API key and secret must be configured together.");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-VALR-Connector/1.0");
	}

	public override string Name => "VALR_REST";

	public bool IsCredentialsAvailable =>
		!_apiKey.IsEmpty() && !_apiSecret.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_publicGate.Dispose();
		_bucketGate.Dispose();
		_privateGate.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<VALRPair[]> GetPairsAsync(
		CancellationToken cancellationToken)
		=> GetPublicAsync<VALRPair[]>("/v1/public/pairs", null, _publicGate,
			cancellationToken);

	public ValueTask<VALRMarketSummary> GetMarketSummaryAsync(string pair,
		CancellationToken cancellationToken)
		=> GetPublicAsync<VALRMarketSummary>(
			$"/v1/public/{EscapePath(pair.NormalizeSymbol())}/marketsummary", null,
			_publicGate, cancellationToken);

	public ValueTask<VALROrderBook> GetOrderBookAsync(string pair,
		CancellationToken cancellationToken)
		=> GetPublicAsync<VALROrderBook>(
			$"/v1/public/{EscapePath(pair.NormalizeSymbol())}/orderbook", null,
			_publicGate, cancellationToken);

	public ValueTask<VALRPublicTrade[]> GetPublicTradesAsync(string pair,
		VALRTradesRequest request, CancellationToken cancellationToken)
		=> GetPublicAsync<VALRPublicTrade[]>(
			$"/v1/public/{EscapePath(pair.NormalizeSymbol())}/trades",
			VALRQueryWriter.Create(request), _publicGate, cancellationToken);

	public ValueTask<VALRCandle[]> GetCandlesAsync(string pair,
		VALRCandlesRequest request, CancellationToken cancellationToken)
		=> GetPublicAsync<VALRCandle[]>(
			$"/v1/public/{EscapePath(pair.NormalizeSymbol())}/buckets",
			VALRQueryWriter.Create(request), _bucketGate, cancellationToken);

	public ValueTask<VALRBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> GetPrivateAsync<VALRBalance[]>("/v1/account/balances",
			"excludeZeroBalances=true", cancellationToken);

	public ValueTask<VALROpenOrder[]> GetOpenOrdersAsync(
		CancellationToken cancellationToken)
		=> GetPrivateAsync<VALROpenOrder[]>("/v1/orders/open", null,
			cancellationToken);

	public ValueTask<VALROrderStatus> GetActiveOrderAsync(string pair,
		string orderId, CancellationToken cancellationToken)
		=> GetPrivateAsync<VALROrderStatus>(
			$"/v1/orders/{EscapePath(pair.NormalizeSymbol())}/orderid/{EscapePath(orderId)}",
			null, cancellationToken);

	public ValueTask<VALROrderStatus> GetHistoricalOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> GetPrivateAsync<VALROrderStatus>(
			$"/v1/orders/history/summary/orderid/{EscapePath(orderId)}", null,
			cancellationToken);

	public ValueTask<VALROrderStatus[]> GetOrderHistoryAsync(
		VALROrderHistoryRequest request, CancellationToken cancellationToken)
		=> GetPrivateAsync<VALROrderStatus[]>("/v1/orders/history",
			VALRQueryWriter.Create(request), cancellationToken);

	public ValueTask<VALRAccountTrade[]> GetAccountTradesAsync(
		VALRTradesRequest request, CancellationToken cancellationToken)
		=> GetPrivateAsync<VALRAccountTrade[]>("/v1/account/tradehistory",
			VALRQueryWriter.Create(request), cancellationToken);

	public ValueTask<VALRPosition[]> GetOpenPositionsAsync(
		VALRPositionRequest request, CancellationToken cancellationToken)
		=> GetPrivateAsync<VALRPosition[]>("/v1/positions/open",
			VALRQueryWriter.Create(request), cancellationToken);

	public ValueTask<VALRIdResponse> PlaceLimitOrderAsync(
		VALRLimitOrderRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<VALRIdResponse, VALRLimitOrderRequest>(
			HttpMethod.Post, "/v2/orders/limit", request, cancellationToken);

	public ValueTask<VALRIdResponse> PlaceMarketOrderAsync(
		VALRMarketOrderRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<VALRIdResponse, VALRMarketOrderRequest>(
			HttpMethod.Post, "/v2/orders/market", request, cancellationToken);

	public ValueTask<VALRIdResponse> PlaceStopLimitOrderAsync(
		VALRStopLimitOrderRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<VALRIdResponse, VALRStopLimitOrderRequest>(
			HttpMethod.Post, "/v2/orders/stop/limit", request,
			cancellationToken);

	public ValueTask<VALRIdResponse> ModifyOrderAsync(
		VALRModifyOrderRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<VALRIdResponse, VALRModifyOrderRequest>(
			HttpMethod.Put, "/v2/orders/modify", request, cancellationToken);

	public ValueTask<VALRIdResponse> CancelOrderAsync(
		VALRCancelOrderRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<VALRIdResponse, VALRCancelOrderRequest>(
			HttpMethod.Delete, "/v2/orders/order", request, cancellationToken);

	public ValueTask<VALRIdResponse[]> CancelAllOrdersAsync(string pair,
		CancellationToken cancellationToken)
		=> SendPrivateWithoutBodyAsync<VALRIdResponse[]>(HttpMethod.Delete,
			pair.IsEmpty() ? "/v1/orders" :
				$"/v1/orders/{EscapePath(pair.NormalizeSymbol())}",
			cancellationToken);

	private ValueTask<TResponse> GetPublicAsync<TResponse>(string path,
		string query, RateGate gate, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(false, HttpMethod.Get, path, query, null, gate,
			true, cancellationToken);

	private ValueTask<TResponse> GetPrivateAsync<TResponse>(string path,
		string query, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendAsync<TResponse>(true, HttpMethod.Get, path, query, null,
			_privateGate, true, cancellationToken);
	}

	private ValueTask<TResponse> SendPrivateAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest request,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendAsync<TResponse>(true, method, path, null,
			Serialize(request), _privateGate, false, cancellationToken);
	}

	private ValueTask<TResponse> SendPrivateWithoutBodyAsync<TResponse>(
		HttpMethod method, string path, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendAsync<TResponse>(true, method, path, null, null,
			_privateGate, false, cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(bool isPrivate,
		HttpMethod method, string path, string query, string body, RateGate gate,
		bool canRetry, CancellationToken cancellationToken)
	{
		var signedPath = query.IsEmpty() ? path : $"{path}?{query}";
		for (var attempt = 0; ; attempt++)
		{
			await gate.WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, signedPath.TrimStart('/')));
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			if (isPrivate)
				ApplyAuthentication(request, method.Method, signedPath, body);

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (canRetry && attempt < _maximumReadAttempts - 1 &&
				IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, responseBody);
			return Deserialize<TResponse>(responseBody);
		}
	}

	private void ApplyAuthentication(HttpRequestMessage request, string method,
		string path, string body)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var signature = CreateSignature(_apiSecret, timestamp, method, path,
			body, _subAccountId);
		request.Headers.TryAddWithoutValidation("X-VALR-API-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("X-VALR-SIGNATURE", signature);
		request.Headers.TryAddWithoutValidation("X-VALR-TIMESTAMP", timestamp);
		if (!_subAccountId.IsEmpty())
			request.Headers.TryAddWithoutValidation("X-VALR-SUB-ACCOUNT-ID",
				_subAccountId);
	}

	internal static string CreateSignature(string secret, string timestamp,
		string method, string path, string body, string subAccountId)
	{
		var payload = timestamp + method.ToUpperInvariant() + path +
			(body ?? string.Empty) + (subAccountId ?? string.Empty);
		using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(
			secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(
			payload))).ToLowerInvariant();
	}

	private string Serialize<TValue>(TValue value)
		=> JsonConvert.SerializeObject(value, _jsonSettings);

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("VALR returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
				throw new InvalidDataException(
					"VALR returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"VALR returned an unexpected response shape.", error);
		}
	}

	private Exception CreateApiError(HttpStatusCode statusCode, string body)
	{
		VALRErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<VALRErrorResponse>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message.IsEmpty(error?.Error);
		if (message.IsEmpty())
		{
			message = body?.Trim();
			if (message?.Length > 512)
				message = message[..512];
		}
		if (message.IsEmpty())
			message = "The API rejected the request.";
		return new VALRApiException(statusCode, error?.Code, error?.OrderId,
			$"VALR HTTP {(int)statusCode}" +
			(error?.Code is int code ? $" ({code})" : string.Empty) +
			$": {message}");
	}

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static Uri CreateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"VALR REST endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"VALR API key and secret are required for private operations.");
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
