namespace StockSharp.Usmart.Native;

sealed class UsmartRestClient : Disposable
{
	private const string _quoteLive = "https://open-hz.usmartsg.com:8443/";
	private const string _quoteDemo = "https://open-hz-uat.yxzq.com/";
	private const string _tradeLive = "https://open-jy.yxzq.com/";
	private const string _tradeDemo = "http://open-jy-uat.yxzq.com/";

	private readonly string _accessToken;
	private readonly string _channelId;
	private readonly bool _isDemo;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
	private readonly RSA _rsa = RSA.Create();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private long _requestSequence;

	public UsmartRestClient(string accessToken, string channelId, string privateKey,
		bool isDemo)
	{
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken));
		_channelId = channelId.ThrowIfEmpty(nameof(channelId));
		_isDemo = isDemo;
		_rsa.ImportFromPem(privateKey.ThrowIfEmpty(nameof(privateKey)));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-uSMART");
	}

	public Task<UsmartResponse<UsmartMarketState>> GetMarketState(string market,
		CancellationToken cancellationToken)
		=> PostQuote<UsmartMarketRequest, UsmartResponse<UsmartMarketState>>(
			"quotes-openservice/api/v1/marketstate", new() { Market = market }, cancellationToken);

	public Task<UsmartResponse<UsmartListData<UsmartSecurity>>> GetSecurities(string market,
		CancellationToken cancellationToken)
		=> PostQuote<UsmartMarketRequest, UsmartResponse<UsmartListData<UsmartSecurity>>>(
			"quotes-openservice/api/v1/basicinfo", new() { Market = market }, cancellationToken);

	public Task<UsmartResponse<UsmartListData<UsmartQuote>>> GetQuotes(string[] securityIds,
		CancellationToken cancellationToken)
		=> PostQuote<UsmartSecuritiesRequest, UsmartResponse<UsmartListData<UsmartQuote>>>(
			"quotes-openservice/api/v1/realtime",
			new() { SecurityIds = securityIds }, cancellationToken);

	public Task<UsmartResponse<UsmartListData<UsmartCandle>>> GetKlines(string securityId,
		int type, long start, int count, CancellationToken cancellationToken)
		=> PostQuote<UsmartKlineRequest, UsmartResponse<UsmartListData<UsmartCandle>>>(
			"quotes-openservice/api/v1/kline", new()
			{
				SecurityId = securityId,
				Type = type,
				Start = start,
				Count = count,
			}, cancellationToken);

	public Task<UsmartResponse<UsmartListData<UsmartTick>>> GetTicks(string securityId,
		long tradeTime, long sequence, int count, CancellationToken cancellationToken)
		=> PostQuote<UsmartTickRequest, UsmartResponse<UsmartListData<UsmartTick>>>(
			"quotes-openservice/api/v1/tick", new()
			{
				SecurityId = securityId,
				TradeTime = tradeTime,
				Sequence = sequence,
				Count = count,
			}, cancellationToken);

	public Task<UsmartResponse<UsmartListData<UsmartDepthLevel>>> GetDepth(string securityId,
		CancellationToken cancellationToken)
		=> PostQuote<UsmartSecurityRequest, UsmartResponse<UsmartListData<UsmartDepthLevel>>>(
			"quotes-openservice/api/v1/orderbook", new() { SecurityId = securityId },
			cancellationToken);

	public Task<UsmartResponse<UsmartOrderAction>> PlaceOrder(UsmartPlaceOrderRequest request,
		CancellationToken cancellationToken)
		=> PostTrade<UsmartPlaceOrderRequest, UsmartResponse<UsmartOrderAction>>(
			"order-center-sg/open-api/entrust-order", request, false, cancellationToken);

	public Task<UsmartResponse<UsmartOrderAction>> PlaceFractionalOrder(
		UsmartFractionalOrderRequest request, CancellationToken cancellationToken)
		=> PostTrade<UsmartFractionalOrderRequest, UsmartResponse<UsmartOrderAction>>(
			"order-center-sg/open-api/odd-entrust", request, false, cancellationToken);

	public Task<UsmartResponse<UsmartOrderAction>> ModifyOrder(UsmartModifyOrderRequest request,
		CancellationToken cancellationToken)
		=> PostTrade<UsmartModifyOrderRequest, UsmartResponse<UsmartOrderAction>>(
			"order-center-sg/open-api/modify-order", request, false, cancellationToken);

	public Task<UsmartResponse<UsmartOrderAction>> CancelFractionalOrder(
		UsmartFractionalCancelRequest request, CancellationToken cancellationToken)
		=> PostTrade<UsmartFractionalCancelRequest, UsmartResponse<UsmartOrderAction>>(
			"order-center-sg/open-api/odd-modify", request, false, cancellationToken);

	public Task<UsmartResponse<UsmartPage<UsmartOrder>>> GetTodayOrders(
		UsmartPagedRequest request, CancellationToken cancellationToken)
		=> PostTrade<UsmartPagedRequest, UsmartResponse<UsmartPage<UsmartOrder>>>(
			"order-center-sg/open-api/today-entrust", request, true, cancellationToken);

	public Task<UsmartResponse<UsmartPage<UsmartTrade>>> GetTrades(
		UsmartRecordsRequest request, CancellationToken cancellationToken)
		=> PostTrade<UsmartRecordsRequest, UsmartResponse<UsmartPage<UsmartTrade>>>(
			"order-center-sg/open-api/stock-record", request, true, cancellationToken);

	public Task<UsmartResponse<UsmartHolding[]>> GetHoldings(UsmartExchangeTypes exchange,
		CancellationToken cancellationToken)
		=> PostTrade<UsmartExchangeRequest, UsmartResponse<UsmartHolding[]>>(
			"asset-center-sg/open-api/stock-holding", new() { Exchange = exchange }, true,
			cancellationToken);

	public Task<UsmartResponse<UsmartAsset>> GetAsset(UsmartExchangeTypes exchange,
		CancellationToken cancellationToken)
		=> PostTrade<UsmartExchangeRequest, UsmartResponse<UsmartAsset>>(
			"asstet-center-sg/open-api/stock-asset", new() { Exchange = exchange }, true,
			cancellationToken);

	private Task<TResponse> PostQuote<TRequest, TResponse>(string path, TRequest request,
		CancellationToken cancellationToken)
		where TResponse : UsmartResponse
		=> Post<TRequest, TResponse>(new Uri(new Uri(_isDemo ? _quoteDemo : _quoteLive), path),
			request, true, true, cancellationToken);

	private Task<TResponse> PostTrade<TRequest, TResponse>(string path, TRequest request,
		bool isRetryable, CancellationToken cancellationToken)
		where TResponse : UsmartResponse
		=> Post<TRequest, TResponse>(new Uri(new Uri(_isDemo ? _tradeDemo : _tradeLive), path),
			request, false, isRetryable, cancellationToken);

	private async Task<TResponse> Post<TRequest, TResponse>(Uri uri, TRequest request,
		bool isQuote, bool isRetryable, CancellationToken cancellationToken)
		where TResponse : UsmartResponse
	{
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		for (var attempt = 1; ; attempt++)
		{
			var requestId = CreateRequestId();
			var timestamp = GetUnixSeconds().ToString(CultureInfo.InvariantCulture);
			using var message = new HttpRequestMessage(HttpMethod.Post, uri)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			};
			message.Headers.TryAddWithoutValidation("Authorization", _accessToken);
			message.Headers.TryAddWithoutValidation("X-Channel", _channelId);
			message.Headers.TryAddWithoutValidation("X-Lang", "3");
			message.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
			message.Headers.TryAddWithoutValidation("X-Time", timestamp);
			message.Headers.TryAddWithoutValidation("X-Dt", "t4");
			message.Headers.TryAddWithoutValidation("X-Type", "2");
			var raw = isQuote
				? _accessToken + _channelId + "3" + requestId + timestamp + body
				: body;
			message.Headers.TryAddWithoutValidation("X-Sign", Sign(raw));

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(message,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (Exception error) when (isRetryable && attempt < 3 &&
				error is HttpRequestException or TaskCanceledException &&
				!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
				continue;
			}

			using (response)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					if (isRetryable && attempt < 3 &&
						(response.StatusCode == HttpStatusCode.RequestTimeout ||
						 (int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
					{
						await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
						continue;
					}
					throw new HttpRequestException(
						$"uSMART HTTP {(int)response.StatusCode}: {content.IsEmpty(response.ReasonPhrase)}.",
						null, response.StatusCode);
				}
				var result = JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
					?? throw new InvalidOperationException("uSMART returned an empty JSON response.");
				if (result.Code != 0)
					throw new InvalidOperationException(
						$"uSMART API error {result.Code}: {result.Message.IsEmpty("Unknown error")}.");
				return result;
			}
		}
	}

	private string CreateRequestId()
	{
		var milliseconds = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
		var sequence = Interlocked.Increment(ref _requestSequence) % 1_000_000;
		return checked(milliseconds * 1_000_000 + sequence)
			.ToString(CultureInfo.InvariantCulture);
	}

	private string Sign(string content)
	{
		var signature = _rsa.SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.MD5,
			RSASignaturePadding.Pkcs1);
		return Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	private static long GetUnixSeconds()
		=> (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rsa.Dispose();
		base.DisposeManaged();
	}
}
