namespace StockSharp.Bitunix.Native;

sealed class BitunixRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _secretKey;
	private readonly string _name;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public BitunixRestClient(string name, string endpoint, SecureString key, SecureString secret)
	{
		_name = name.ThrowIfEmpty(nameof(name));
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/", UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secretKey = secret.IsEmpty() ? null : secret.UnSecure();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bitunix-Connector/1.0");
	}

	public override string Name => nameof(Bitunix) + "_" + _name + "Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && !_secretKey.IsEmpty();

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BitunixSpotPair[]> GetSpotPairsAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BitunixSpotPair[]>("api/spot/v1/common/coin_pair/list",
			new BitunixEmptyQuery(), false, cancellationToken);

	public ValueTask<string> GetSpotLastPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<string>("api/spot/v1/market/last_price",
			new BitunixSymbolQuery { Symbol = symbol }, false, cancellationToken);

	public ValueTask<BitunixSpotDepth> GetSpotDepthAsync(string symbol, string precision,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixSpotDepth>("api/spot/v1/market/depth",
			new BitunixSpotDepthQuery { Symbol = symbol, Precision = precision }, false,
			cancellationToken);

	public ValueTask<BitunixSpotCandle> GetSpotCurrentCandleAsync(string symbol,
		string interval, CancellationToken cancellationToken)
		=> SendGetAsync<BitunixSpotCandle>("api/spot/v1/market/kline",
			new BitunixSpotKlineQuery { Symbol = symbol, Interval = interval }, false,
			cancellationToken);

	public ValueTask<BitunixSpotCandle[]> GetSpotCandlesAsync(string symbol, string interval,
		long? endTime, int limit, CancellationToken cancellationToken)
		=> SendGetAsync<BitunixSpotCandle[]>("api/spot/v1/market/kline/history",
			new BitunixSpotKlineQuery
			{
				Symbol = symbol,
				Interval = interval,
				EndTime = endTime,
				Limit = limit,
			}, false, cancellationToken);

	public ValueTask<BitunixSpotBalance[]> GetSpotBalancesAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixSpotBalance[]>("api/spot/v1/user/account",
			new BitunixEmptyQuery(), true, cancellationToken);

	public ValueTask<BitunixSpotOrderResult> PlaceSpotOrderAsync(
		BitunixSpotPlaceOrderRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixSpotOrderResult, BitunixSpotPlaceOrderRequest>(
			"api/spot/v1/order/place_order", request, cancellationToken);

	public ValueTask<BitunixEmptyData> CancelSpotOrdersAsync(
		BitunixSpotCancelOrdersRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixEmptyData, BitunixSpotCancelOrdersRequest>(
			"api/spot/v1/order/cancel", request, cancellationToken);

	public ValueTask<BitunixSpotOrder[]> GetSpotPendingOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendPostAsync<BitunixSpotOrder[], BitunixSpotPendingOrdersRequest>(
			"api/spot/v1/order/pending/list",
			new BitunixSpotPendingOrdersRequest { Symbol = symbol }, cancellationToken);

	public ValueTask<BitunixSpotOrderPage> GetSpotOrderHistoryAsync(
		BitunixSpotOrderHistoryRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixSpotOrderPage, BitunixSpotOrderHistoryRequest>(
			"api/spot/v1/order/history/page", request, cancellationToken);

	public ValueTask<BitunixSpotFill> GetSpotFillAsync(string orderId, string symbol,
		CancellationToken cancellationToken)
		=> SendPostAsync<BitunixSpotFill, BitunixSpotFillsRequest>(
			"api/spot/v1/order/deal/list",
			new BitunixSpotFillsRequest { OrderId = orderId, Symbol = symbol }, cancellationToken);

	public ValueTask<BitunixFuturesProduct[]> GetFuturesProductsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesProduct[]>("api/v1/futures/market/trading_pairs",
			new BitunixEmptyQuery(), false, cancellationToken);

	public ValueTask<BitunixFuturesTicker[]> GetFuturesTickersAsync(string symbols,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesTicker[]>("api/v1/futures/market/tickers",
			new BitunixSymbolsQuery { Symbols = symbols }, false, cancellationToken);

	public ValueTask<BitunixFuturesDepth> GetFuturesDepthAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesDepth>("api/v1/futures/market/depth",
			new BitunixFuturesDepthQuery
			{
				Symbol = symbol,
				Limit = limit.ToString(CultureInfo.InvariantCulture),
			}, false, cancellationToken);

	public ValueTask<BitunixFuturesCandle[]> GetFuturesCandlesAsync(string symbol,
		string interval, long? startTime, long? endTime, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesCandle[]>("api/v1/futures/market/kline",
			new BitunixFuturesKlineQuery
			{
				Symbol = symbol,
				Interval = interval,
				StartTime = startTime,
				EndTime = endTime,
				Limit = limit,
			}, false, cancellationToken);

	public ValueTask<BitunixFuturesAccount> GetFuturesAccountAsync(string marginCoin,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesAccount>("api/v1/futures/account",
			new BitunixFuturesAccountQuery { MarginCoin = marginCoin }, true,
			cancellationToken);

	public async ValueTask ChangeFuturesLeverageAsync(BitunixFuturesChangeLeverageRequest request,
		CancellationToken cancellationToken)
		=> _ = await SendPostAsync<BitunixFuturesChangeLeverageRequest,
			BitunixFuturesChangeLeverageRequest>("api/v1/futures/account/change_leverage",
			request, cancellationToken);

	public async ValueTask ChangeFuturesMarginModeAsync(
		BitunixFuturesChangeMarginModeRequest request, CancellationToken cancellationToken)
		=> _ = await SendPostAsync<BitunixFuturesChangeMarginModeRequest,
			BitunixFuturesChangeMarginModeRequest>("api/v1/futures/account/change_margin_mode",
			request, cancellationToken);

	public ValueTask<BitunixFuturesPosition[]> GetFuturesPositionsAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesPosition[]>("api/v1/futures/position/get_pending_positions",
			new BitunixFuturesPositionsQuery { Symbol = symbol }, true, cancellationToken);

	public ValueTask<BitunixFuturesOrderIdResult> PlaceFuturesOrderAsync(
		BitunixFuturesPlaceOrderRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixFuturesOrderIdResult, BitunixFuturesPlaceOrderRequest>(
			"api/v1/futures/trade/place_order", request, cancellationToken);

	public ValueTask<BitunixFuturesOrderIdResult> ModifyFuturesOrderAsync(
		BitunixFuturesModifyOrderRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixFuturesOrderIdResult, BitunixFuturesModifyOrderRequest>(
			"api/v1/futures/trade/modify_order", request, cancellationToken);

	public ValueTask<BitunixFuturesOrderResult> CancelFuturesOrdersAsync(
		BitunixFuturesCancelOrdersRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixFuturesOrderResult, BitunixFuturesCancelOrdersRequest>(
			"api/v1/futures/trade/cancel_orders", request, cancellationToken);

	public ValueTask<BitunixFuturesOrderResult> CancelAllFuturesOrdersAsync(
		BitunixFuturesCancelAllOrdersRequest request, CancellationToken cancellationToken)
		=> SendPostAsync<BitunixFuturesOrderResult, BitunixFuturesCancelAllOrdersRequest>(
			"api/v1/futures/trade/cancel_all_orders", request, cancellationToken);

	public ValueTask<BitunixFuturesOrderPage> GetFuturesPendingOrdersAsync(
		BitunixFuturesOrdersQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesOrderPage>("api/v1/futures/trade/get_pending_orders",
			query, true, cancellationToken);

	public ValueTask<BitunixFuturesOrderPage> GetFuturesOrderHistoryAsync(
		BitunixFuturesOrdersQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesOrderPage>("api/v1/futures/trade/get_history_orders",
			query, true, cancellationToken);

	public ValueTask<BitunixFuturesTradePage> GetFuturesTradeHistoryAsync(
		BitunixFuturesTradesQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesTradePage>("api/v1/futures/trade/get_history_trades",
			query, true, cancellationToken);

	public ValueTask<BitunixFuturesOrder> GetFuturesOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendGetAsync<BitunixFuturesOrder>("api/v1/futures/trade/get_order_detail",
			new BitunixFuturesOrderDetailQuery { OrderId = orderId }, true, cancellationToken);

	private async ValueTask<TData> SendGetAsync<TData>(string path, IBitunixQuery query,
		bool isSigned, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isSigned)
			EnsureCredentials();

		var parameters = GetParameters(query);
		var queryString = BuildQuery(parameters);
		var signaturePayload = BuildSignaturePayload(parameters);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path.TrimStart('/') +
					(queryString.IsEmpty() ? string.Empty : "?" + queryString)));
			if (isSigned)
				ApplySignature(request, signaturePayload, string.Empty);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TData>(body);
			if (attempt + 1 < _maximumReadAttempts && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateHttpException(response.StatusCode, body);
		}
	}

	private async ValueTask<TData> SendPostAsync<TData, TRequest>(string path,
		TRequest requestData, CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(requestData);
		EnsureCredentials();
		var body = JsonConvert.SerializeObject(requestData, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		ApplySignature(request, string.Empty, body);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpException(response.StatusCode, responseBody);
		return Deserialize<TData>(responseBody);
	}

	private void ApplySignature(HttpRequestMessage request, string query, string body)
	{
		var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var digest = Sha256(nonce + timestamp + _apiKey + query + body);
		var signature = Sha256(digest + _secretKey);
		request.Headers.Add("api-key", _apiKey);
		request.Headers.Add("nonce", nonce);
		request.Headers.Add("timestamp", timestamp);
		request.Headers.Add("sign", signature);
	}

	private static string Sha256(string value)
		=> Convert.ToHexString(SHA256.HashData(value.UTF8())).ToLowerInvariant();

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitunix API key and secret are required for private operations.");
	}

	private TData Deserialize<TData>(string body)
	{
		BitunixResponse<TData> response;
		try
		{
			response = JsonConvert.DeserializeObject<BitunixResponse<TData>>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Bitunix API returned malformed JSON.", error);
		}
		if (response is null)
			throw new InvalidDataException("Bitunix API returned an empty response.");
		if (response.Code != "0")
			throw new InvalidOperationException(
				$"Bitunix API error {response.Code}: {response.Message}".Trim());
		return response.Data;
	}

	private static BitunixParameter[] GetParameters(IBitunixQuery query)
		=> [.. query.GetParameters()
			.Where(static parameter => !parameter.Name.IsEmpty() && !parameter.Value.IsEmpty())
			.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)];

	private static string BuildQuery(IEnumerable<BitunixParameter> parameters)
		=> parameters.Select(static parameter =>
			$"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}")
			.Join("&");

	private static string BuildSignaturePayload(IEnumerable<BitunixParameter> parameters)
		=> parameters.Select(static parameter => parameter.Name + parameter.Value).Join(string.Empty);

	private static Exception CreateHttpException(HttpStatusCode statusCode, string body)
		=> new InvalidOperationException(
			$"Bitunix HTTP {(int)statusCode} ({statusCode}): {body}".Trim());

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(110);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
