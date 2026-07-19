namespace StockSharp.CoinsPh.Native;

sealed class CoinsPhRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private const long _receiveWindow = 5000;
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
	private long _timeOffset;

	public CoinsPhRestClient(string endpoint, SecureString key,
		SecureString secret)
	{
		_endpoint = CreateEndpoint(endpoint);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"Coins.ph API key and secret must be configured together.");
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
			"StockSharp-CoinsPh-Connector/1.0");
	}

	public override string Name => "CoinsPh_Rest";

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

	public async ValueTask SyncTimeAsync(CancellationToken cancellationToken)
	{
		var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var response = await SendPublicAsync<CoinsPhServerTime>(HttpMethod.Get,
			"openapi/v1/time", new(), 1, cancellationToken);
		var finished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		Interlocked.Exchange(ref _timeOffset,
			response.ServerTime - ((started + finished) / 2));
	}

	public ValueTask<CoinsPhExchangeInfo> GetExchangeInfoAsync(
		CancellationToken cancellationToken)
		=> SendPublicAsync<CoinsPhExchangeInfo>(HttpMethod.Get,
			"openapi/v1/exchangeInfo", new(), 1, cancellationToken);

	public ValueTask<CoinsPhTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendPublicAsync<CoinsPhTicker>(HttpMethod.Get,
			"openapi/quote/v1/ticker/24hr",
			new CoinsPhQueryBuilder().Add("symbol",
				symbol.ThrowIfEmpty(nameof(symbol))), 1, cancellationToken);

	public ValueTask<CoinsPhOrderBook> GetOrderBookAsync(
		CoinsPhDepthRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var limit = request.Limit.Min(200).Max(1);
		return SendPublicAsync<CoinsPhOrderBook>(HttpMethod.Get,
			"openapi/quote/v1/depth", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
				.Add("limit", limit), limit > 100 ? 5 : 1, cancellationToken);
	}

	public ValueTask<CoinsPhPublicTrade[]> GetTradesAsync(
		CoinsPhTradesRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPublicAsync<CoinsPhPublicTrade[]>(HttpMethod.Get,
			"openapi/quote/v1/trades", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
				.Add("limit", request.Limit.Min(1000).Max(1)), 1,
			cancellationToken);
	}

	public ValueTask<CoinsPhKline[]> GetKlinesAsync(CoinsPhKlinesRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPublicAsync<CoinsPhKline[]>(HttpMethod.Get,
			"openapi/quote/v1/klines", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
				.Add("interval", request.Interval.ThrowIfEmpty(nameof(request.Interval)))
				.Add("startTime", request.StartTime)
				.Add("endTime", request.EndTime)
				.Add("limit", request.Limit.Min(1000).Max(1)), 1,
			cancellationToken);
	}

	public ValueTask<CoinsPhAccount> GetAccountAsync(
		CancellationToken cancellationToken)
		=> SendSignedAsync<CoinsPhAccount>(HttpMethod.Get,
			"openapi/v1/account", new(), true, 10, cancellationToken);

	public ValueTask<CoinsPhOrder> PlaceOrderAsync(CoinsPhOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhOrder>(HttpMethod.Post,
			"openapi/v1/order", CreateOrderQuery(request), false, 1,
			cancellationToken);
	}

	public ValueTask<CoinsPhOrderLookupResult> GetOrderAsync(
		CoinsPhOrderLookupRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhOrderLookupResult>(HttpMethod.Get,
			"openapi/v1/order", new CoinsPhQueryBuilder()
				.Add("orderId", request.OrderId)
				.Add("origClientOrderId", request.ClientOrderId), true, 1,
			cancellationToken);
	}

	public ValueTask<CoinsPhOrder> CancelOrderAsync(
		CoinsPhCancelOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhOrder>(HttpMethod.Delete,
			"openapi/v1/order", new CoinsPhQueryBuilder()
				.Add("orderId", request.OrderId)
				.Add("origClientOrderId", request.ClientOrderId), false, 1,
			cancellationToken);
	}

	public ValueTask<CoinsPhOrder[]> CancelAllAsync(CoinsPhCancelAllRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhOrder[]>(HttpMethod.Delete,
			"openapi/v1/openOrders", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol))),
			false, 1, cancellationToken);
	}

	public ValueTask<CoinsPhOrder[]> GetOpenOrdersAsync(
		CoinsPhOpenOrdersRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhOrder[]>(HttpMethod.Get,
			"openapi/v1/openOrders", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol), true,
			request.Symbol.IsEmpty() ? 3 : 1, cancellationToken);
	}

	public ValueTask<CoinsPhOrder[]> GetOrderHistoryAsync(
		CoinsPhOrderHistoryRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhOrder[]>(HttpMethod.Get,
			"openapi/v1/historyOrders", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
				.Add("orderId", request.OrderId)
				.Add("startTime", request.StartTime)
				.Add("endTime", request.EndTime)
				.Add("limit", request.Limit.Min(1000).Max(1)), true, 10,
			cancellationToken);
	}

	public ValueTask<CoinsPhAccountTrade[]> GetMyTradesAsync(
		CoinsPhMyTradesRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendSignedAsync<CoinsPhAccountTrade[]>(HttpMethod.Get,
			"openapi/v1/myTrades", new CoinsPhQueryBuilder()
				.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
				.Add("orderId", request.OrderId)
				.Add("startTime", request.StartTime)
				.Add("endTime", request.EndTime)
				.Add("fromId", request.FromId)
				.Add("limit", request.Limit.Min(1000).Max(1)), true, 10,
			cancellationToken);
	}

	public ValueTask<CoinsPhCancelReplaceResponse> CancelReplaceAsync(
		CoinsPhCancelReplaceRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new CoinsPhQueryBuilder()
			.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
			.AddEnum("side", request.Side)
			.AddEnum("type", request.Type)
			.Add("cancelReplaceMode", "STOP_ON_FAILURE")
			.AddEnum("timeInForce", request.TimeInForce)
			.Add("quantity", request.Quantity)
			.Add("quoteOrderQty", request.QuoteOrderQuantity)
			.Add("price", request.Price)
			.Add("newClientOrderId", request.ClientOrderId)
			.Add("cancelOrderId", request.CancelOrderId)
			.Add("cancelOrigClientOrderId", request.CancelClientOrderId)
			.Add("stopPrice", request.StopPrice)
			.Add("newOrderRespType", "FULL");
		return SendSignedAsync<CoinsPhCancelReplaceResponse>(HttpMethod.Post,
			"openapi/v1/order/cancelReplace", query, false, 1,
			cancellationToken);
	}

	public ValueTask<CoinsPhListenKey> CreateListenKeyAsync(
		CancellationToken cancellationToken)
		=> SendApiKeyAsync<CoinsPhListenKey>(HttpMethod.Post,
			"openapi/v1/userDataStream", new(), false, cancellationToken);

	public ValueTask<CoinsPhEmptyResponse> KeepAliveListenKeyAsync(
		CoinsPhListenKeyRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendApiKeyAsync<CoinsPhEmptyResponse>(HttpMethod.Put,
			"openapi/v1/userDataStream", new CoinsPhQueryBuilder()
				.Add("listenKey", request.ListenKey.ThrowIfEmpty(
					nameof(request.ListenKey))), true, cancellationToken);
	}

	public ValueTask<CoinsPhEmptyResponse> CloseListenKeyAsync(
		CoinsPhListenKeyRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendApiKeyAsync<CoinsPhEmptyResponse>(HttpMethod.Delete,
			"openapi/v1/userDataStream", new CoinsPhQueryBuilder()
				.Add("listenKey", request.ListenKey.ThrowIfEmpty(
					nameof(request.ListenKey))), false, cancellationToken);
	}

	private static CoinsPhQueryBuilder CreateOrderQuery(CoinsPhOrderRequest request)
		=> new CoinsPhQueryBuilder()
			.Add("symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)))
			.AddEnum("side", request.Side)
			.AddEnum("type", request.Type)
			.AddEnum("timeInForce", request.TimeInForce)
			.Add("quantity", request.Quantity)
			.Add("quoteOrderQty", request.QuoteOrderQuantity)
			.Add("price", request.Price)
			.Add("newClientOrderId", request.ClientOrderId)
			.Add("stopPrice", request.StopPrice)
			.Add("newOrderRespType", "FULL");

	private async ValueTask<TResponse> SendPublicAsync<TResponse>(
		HttpMethod method, string path, CoinsPhQueryBuilder query, int weight,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(weight, cancellationToken);
			using var request = new HttpRequestMessage(method,
				CreateUri(path, query.ToString()));
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

	private async ValueTask<TResponse> SendSignedAsync<TResponse>(
		HttpMethod method, string path, CoinsPhQueryBuilder baseQuery,
		bool isRetrySafe, int weight, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		for (var attempt = 0; ; attempt++)
		{
			var query = baseQuery.Clone()
				.Add("recvWindow", _receiveWindow)
				.Add("timestamp", CreateTimestamp());
			var payload = query.ToString();
			query.Add("signature", Sign(payload));
			await WaitRateLimitAsync(weight, cancellationToken);
			using var request = new HttpRequestMessage(method,
				CreateUri(path, query.ToString()));
			request.Headers.TryAddWithoutValidation("X-COINS-APIKEY", _apiKey);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);

			var error = CreateHttpError(response.StatusCode, body);
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts)
				throw error;
			if (error is CoinsPhApiException { ErrorCode: -1021 })
			{
				await SyncTimeAsync(cancellationToken);
				continue;
			}
			if (!IsTransient(response.StatusCode))
				throw error;
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendApiKeyAsync<TResponse>(
		HttpMethod method, string path, CoinsPhQueryBuilder query,
		bool isRetrySafe, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(1, cancellationToken);
			using var request = new HttpRequestMessage(method,
				CreateUri(path, query.ToString()));
			request.Headers.TryAddWithoutValidation("X-COINS-APIKEY", _apiKey);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("Coins.ph returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
				throw new InvalidDataException(
					"Coins.ph returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coins.ph returned an unexpected response shape.", error);
		}
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		try
		{
			var error = JsonConvert.DeserializeObject<CoinsPhApiError>(
				body ?? string.Empty, _jsonSettings);
			if (error is not null && (error.Code != 0 || !error.Message.IsEmpty()))
				return new CoinsPhApiException(error.Code,
					$"Coins.ph HTTP {(int)statusCode}: {error.Message}");
		}
		catch (JsonException)
		{
		}

		var details = body?.Trim();
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Coins.ph HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
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
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
				Interlocked.Read(ref _timeOffset);
			if (timestamp <= _lastTimestamp)
				timestamp = _lastTimestamp + 1;
			_lastTimestamp = timestamp;
			return timestamp;
		}
	}

	private async ValueTask WaitRateLimitAsync(int weight,
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow +
				TimeSpan.FromMilliseconds(510 * weight.Max(1));
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
				"Coins.ph API key and secret are required for private operations.");
	}

	private static Uri CreateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"Coins.ph REST endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}

	private Uri CreateUri(string path, string query)
	{
		var uri = new Uri(_endpoint, path.ThrowIfEmpty(nameof(path)));
		return query.IsEmpty() ? uri : new UriBuilder(uri) { Query = query }.Uri;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is (HttpStatusCode)418 or (HttpStatusCode)429 ||
			(int)statusCode >= 500;

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
