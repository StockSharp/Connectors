namespace StockSharp.WooX.Native;

sealed class WooXRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly Uri _historicalEndpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _secret;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public WooXRestClient(string endpoint, string historicalEndpoint, SecureString key,
		SecureString secret)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_historicalEndpoint = new Uri(historicalEndpoint.ThrowIfEmpty(nameof(historicalEndpoint))
			.TrimEnd('/') + "/", UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secret = secret.IsEmpty() ? null : secret.UnSecure();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-WooX-Connector/1.0");
	}

	public override string Name => nameof(WooX) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && !_secret.IsEmpty();

	public string ApiKey => _apiKey;

	public string CreateWebSocketSignature(string timestamp)
		=> Sign("|" + timestamp);

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<WooXSymbolsResponse> GetSymbolsAsync(CancellationToken cancellationToken)
		=> SendGetAsync<WooXSymbolsResponse>(_endpoint, "v1/public/info",
			WooXEmptyParameters.Instance, WooXApiVersions.V1, false, cancellationToken);

	public ValueTask<WooXMarketTradesResponse> GetMarketTradesAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXMarketTradesResponse>(_endpoint, "v1/public/market_trades",
			new WooXMarketTradesQuery { Symbol = symbol, Limit = limit }, WooXApiVersions.V1,
			false, cancellationToken);

	public ValueTask<WooXDataResponse<WooXHistoricalData<WooXMarketTrade>>>
		GetHistoricalTradesAsync(WooXHistoricalTradesQuery query,
			CancellationToken cancellationToken)
		=> SendGetAsync<WooXDataResponse<WooXHistoricalData<WooXMarketTrade>>>(
			_historicalEndpoint, "v1/hist/trade", query, WooXApiVersions.V1, false,
			cancellationToken);

	public ValueTask<WooXOrderBookResponse> GetOrderBookAsync(string symbol, int maximumLevel,
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXOrderBookResponse>(_endpoint,
			$"v1/public/orderbook/{Uri.EscapeDataString(symbol)}",
			new WooXOrderBookQuery { MaximumLevel = maximumLevel }, WooXApiVersions.V1,
			false, cancellationToken);

	public ValueTask<WooXKlinesResponse> GetKlinesAsync(WooXKlinesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXKlinesResponse>(_endpoint, "v1/public/kline", query,
			WooXApiVersions.V1, false, cancellationToken);

	public ValueTask<WooXDataResponse<WooXHistoricalData<WooXCandle>>>
		GetHistoricalKlinesAsync(WooXHistoricalKlinesQuery query,
			CancellationToken cancellationToken)
		=> SendGetAsync<WooXDataResponse<WooXHistoricalData<WooXCandle>>>(
			_historicalEndpoint, "v1/hist/kline", query, WooXApiVersions.V1, false,
			cancellationToken);

	public ValueTask<WooXFuturesResponse> GetFuturesAsync(CancellationToken cancellationToken)
		=> SendGetAsync<WooXFuturesResponse>(_endpoint, "v1/public/futures",
			WooXEmptyParameters.Instance, WooXApiVersions.V1, false, cancellationToken);

	public ValueTask<WooXPlaceOrderResponse> PlaceOrderAsync(WooXPlaceOrderRequest request,
		CancellationToken cancellationToken)
		=> SendFormAsync<WooXPlaceOrderResponse>(HttpMethod.Post, "v1/order", request,
			cancellationToken);

	public ValueTask<WooXOperationResponse> CancelOrderAsync(WooXCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> SendFormAsync<WooXOperationResponse>(HttpMethod.Delete, "v1/order", request,
			cancellationToken);

	public ValueTask<WooXOperationResponse> CancelSymbolOrdersAsync(
		WooXCancelSymbolRequest request, CancellationToken cancellationToken)
		=> SendFormAsync<WooXOperationResponse>(HttpMethod.Delete, "v1/orders", request,
			cancellationToken);

	public ValueTask<WooXOperationResponse> CancelAllOrdersAsync(
		CancellationToken cancellationToken)
		=> SendV3WithoutBodyAsync<WooXOperationResponse>(HttpMethod.Delete,
			"v3/orders/pending", cancellationToken);

	public ValueTask<WooXDataResponse<WooXEditOrderData>> EditOrderAsync(long orderId,
		WooXEditOrderRequest request, CancellationToken cancellationToken)
		=> SendJsonAsync<WooXDataResponse<WooXEditOrderData>, WooXEditOrderRequest>(
			HttpMethod.Put, $"v3/order/{orderId.ToString(CultureInfo.InvariantCulture)}",
			request, cancellationToken);

	public ValueTask<WooXRowsResponse<WooXOrder>> GetOrdersAsync(WooXOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXRowsResponse<WooXOrder>>(_endpoint, "v1/orders", query,
			WooXApiVersions.V1, true, cancellationToken);

	public ValueTask<WooXRowsResponse<WooXTrade>> GetTradesAsync(WooXTradeHistoryQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXRowsResponse<WooXTrade>>(_endpoint, "v1/client/trades", query,
			WooXApiVersions.V1, true, cancellationToken);

	public ValueTask<WooXDataResponse<WooXBalanceData>> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXDataResponse<WooXBalanceData>>(_endpoint, "v3/balances",
			WooXEmptyParameters.Instance, WooXApiVersions.V3, true, cancellationToken);

	public ValueTask<WooXDataResponse<WooXAccountInfo>> GetAccountInfoAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXDataResponse<WooXAccountInfo>>(_endpoint, "v3/accountinfo",
			WooXEmptyParameters.Instance, WooXApiVersions.V3, true, cancellationToken);

	public ValueTask<WooXDataResponse<WooXPositionsData>> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<WooXDataResponse<WooXPositionsData>>(_endpoint, "v3/positions",
			WooXEmptyParameters.Instance, WooXApiVersions.V3, true, cancellationToken);

	public ValueTask<WooXOperationResponse> SetFuturesLeverageAsync(
		WooXSetLeverageRequest request, CancellationToken cancellationToken)
		=> SendFormAsync<WooXOperationResponse>(HttpMethod.Post, "v1/client/futures_leverage",
			request, cancellationToken);

	private async ValueTask<TResponse> SendGetAsync<TResponse>(Uri endpoint, string path,
		IWooXParameters parameters, WooXApiVersions version, bool isSigned,
		CancellationToken cancellationToken)
		where TResponse : WooXResponse
	{
		ArgumentNullException.ThrowIfNull(parameters);
		if (isSigned)
			EnsureCredentials();
		var query = BuildParameters(parameters.GetParameters());
		var requestPath = "/" + path.TrimStart('/');
		var requestTarget = requestPath + (query.IsEmpty() ? string.Empty : "?" + query);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(endpoint, requestTarget.TrimStart('/')));
			if (isSigned)
				AddAuthentication(request, version, requestPath, query, string.Empty);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return DeserializeResponse<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendFormAsync<TResponse>(HttpMethod method,
		string path, IWooXParameters parameters, CancellationToken cancellationToken)
		where TResponse : WooXResponse
	{
		ArgumentNullException.ThrowIfNull(parameters);
		EnsureCredentials();
		var requestPath = "/" + path.TrimStart('/');
		var form = BuildParameters(parameters.GetParameters());
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_endpoint, requestPath.TrimStart('/')))
		{
			Content = new StringContent(form, Encoding.UTF8,
				"application/x-www-form-urlencoded"),
		};
		AddAuthentication(request, WooXApiVersions.V1, requestPath, form, string.Empty);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return DeserializeResponse<TResponse>(body);
	}

	private async ValueTask<TResponse> SendJsonAsync<TResponse, TRequest>(HttpMethod method,
		string path, TRequest body, CancellationToken cancellationToken)
		where TResponse : WooXResponse
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var requestPath = "/" + path.TrimStart('/');
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_endpoint, requestPath.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, WooXApiVersions.V3, requestPath, string.Empty, json);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return DeserializeResponse<TResponse>(responseBody);
	}

	private async ValueTask<TResponse> SendV3WithoutBodyAsync<TResponse>(HttpMethod method,
		string path, CancellationToken cancellationToken)
		where TResponse : WooXResponse
	{
		EnsureCredentials();
		var requestPath = "/" + path.TrimStart('/');
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_endpoint, requestPath.TrimStart('/')));
		AddAuthentication(request, WooXApiVersions.V3, requestPath, string.Empty, string.Empty);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return DeserializeResponse<TResponse>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, WooXApiVersions version,
		string requestPath, string parameters, string body)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var payload = version == WooXApiVersions.V1
			? parameters + "|" + timestamp
			: timestamp + request.Method.Method.ToUpperInvariant() + requestPath +
				(body.IsEmpty()
					? parameters.IsEmpty() ? string.Empty : "?" + parameters
					: body);
		request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
		request.Headers.TryAddWithoutValidation("x-api-timestamp", timestamp);
		request.Headers.TryAddWithoutValidation("x-api-signature", Sign(payload));
	}

	private string Sign(string payload)
		=> Convert.ToHexString(HMACSHA256.HashData(_secret.UTF8(), payload.UTF8()))
			.ToLowerInvariant();

	private static string BuildParameters(IEnumerable<WooXParameter> parameters)
		=> parameters
			.Where(static parameter => !parameter.Name.IsEmpty() && parameter.Value is not null)
			.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)
			.Select(static parameter => Uri.EscapeDataString(parameter.Name) + "=" +
				Uri.EscapeDataString(parameter.Value))
			.Join("&");

	private TResponse DeserializeResponse<TResponse>(string body)
		where TResponse : WooXResponse
	{
		try
		{
			var response = JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException("WOO X returned an empty response.");
			if (!response.IsSuccess)
				throw new InvalidOperationException(
					$"WOO X API error {response.Code}: {response.Message}".Trim());
			return response;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("WOO X returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"WOO X API key and secret are required for private operations.");
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(100);
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

	private static Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		var text = body?.Trim();
		if (text?.Length > 512)
			text = text[..512];
		return new HttpRequestException(
			$"WOO X HTTP {(int)statusCode} ({statusCode}): {text}".Trim(), null, statusCode);
	}

	private enum WooXApiVersions
	{
		V1,
		V3,
	}
}
