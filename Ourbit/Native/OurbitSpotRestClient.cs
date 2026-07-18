namespace StockSharp.Ourbit.Native;

sealed class OurbitSpotRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly int _receiveWindow;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public OurbitSpotRestClient(string endpoint, SecureString key, SecureString secret,
		int receiveWindow)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/", UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_receiveWindow = receiveWindow;
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Ourbit-Connector/1.0");
	}

	public override string Name => nameof(Ourbit) + "_SpotRest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _hasher is not null;

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<OurbitSpotExchangeInfo> GetExchangeInfoAsync(
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotExchangeInfo>("api/v3/exchangeInfo", HttpMethod.Get,
			new OurbitEmptyRequest(), false, cancellationToken);

	public ValueTask<OurbitSpotTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotTicker>("api/v3/ticker/24hr", HttpMethod.Get,
			new OurbitSymbolRequest { Symbol = symbol }, false, cancellationToken);

	public ValueTask<OurbitSpotBookTicker> GetBookTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotBookTicker>("api/v3/ticker/bookTicker", HttpMethod.Get,
			new OurbitSymbolRequest { Symbol = symbol }, false, cancellationToken);

	public ValueTask<OurbitSpotDepth> GetDepthAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotDepth>("api/v3/depth", HttpMethod.Get,
			new OurbitDepthRequest { Symbol = symbol, Limit = limit }, false, cancellationToken);

	public ValueTask<OurbitSpotTrade[]> GetTradesAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotTrade[]>("api/v3/trades", HttpMethod.Get,
			new OurbitDepthRequest { Symbol = symbol, Limit = limit }, false, cancellationToken);

	public ValueTask<OurbitSpotKline[]> GetCandlesAsync(string symbol, string interval,
		long? startTime, long? endTime, int limit, CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotKline[]>("api/v3/klines", HttpMethod.Get,
			new OurbitSpotHistoryRequest
			{
				Symbol = symbol,
				Interval = interval,
				StartTime = startTime,
				EndTime = endTime,
				Limit = limit,
			}, false, cancellationToken);

	public ValueTask<OurbitSpotAccount> GetAccountAsync(CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotAccount>("api/v3/account", HttpMethod.Get,
			new OurbitEmptyRequest(), true, cancellationToken);

	public ValueTask<OurbitSpotOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotOrder[]>("api/v3/openOrders", HttpMethod.Get,
			symbol.IsEmpty() ? new OurbitEmptyRequest() : new OurbitSymbolRequest { Symbol = symbol },
			true, cancellationToken);

	public ValueTask<OurbitSpotOrder[]> GetAllOrdersAsync(string symbol, long? startTime,
		long? endTime, int limit, CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotOrder[]>("api/v3/allOrders", HttpMethod.Get,
			new OurbitSpotHistoryRequest
			{
				Symbol = symbol,
				StartTime = startTime,
				EndTime = endTime,
				Limit = limit,
			}, true, cancellationToken);

	public ValueTask<OurbitSpotFill[]> GetFillsAsync(string symbol, long? startTime,
		long? endTime, int limit, CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotFill[]>("api/v3/myTrades", HttpMethod.Get,
			new OurbitSpotHistoryRequest
			{
				Symbol = symbol,
				StartTime = startTime,
				EndTime = endTime,
				Limit = limit,
			}, true, cancellationToken);

	public ValueTask<OurbitSpotOrderResult> PlaceOrderAsync(OurbitSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotOrderResult>("api/v3/order", HttpMethod.Post, request, true,
			cancellationToken);

	public ValueTask<OurbitSpotOrderResult> CancelOrderAsync(OurbitSpotCancelRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotOrderResult>("api/v3/order", HttpMethod.Delete, request, true,
			cancellationToken);

	public ValueTask<OurbitSpotOrder[]> CancelAllOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotOrder[]>("api/v3/openOrders", HttpMethod.Delete,
			new OurbitSymbolRequest { Symbol = symbol }, true, cancellationToken);

	public ValueTask<OurbitSpotListenKey> CreateListenKeyAsync(
		CancellationToken cancellationToken)
		=> SendAsync<OurbitSpotListenKey>("api/v3/userDataStream", HttpMethod.Post,
			new OurbitEmptyRequest(), true, cancellationToken);

	public async ValueTask KeepListenKeyAsync(string listenKey, CancellationToken cancellationToken)
		=> _ = await SendAsync<OurbitSpotListenKey>("api/v3/userDataStream", HttpMethod.Put,
			new OurbitSpotListenKeyRequest { ListenKey = listenKey }, true, cancellationToken);

	public async ValueTask CloseListenKeyAsync(string listenKey, CancellationToken cancellationToken)
		=> _ = await SendAsync<OurbitSpotListenKey>("api/v3/userDataStream", HttpMethod.Delete,
			new OurbitSpotListenKeyRequest { ListenKey = listenKey }, true, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(string path, HttpMethod method,
		IOurbitParameters requestData, bool isSigned, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(requestData);
		if (isSigned)
			EnsureCredentials();

		var parameters = requestData.GetParameters()
			.Where(static parameter => !parameter.Name.IsEmpty() && parameter.Value is not null)
			.ToList();
		if (isSigned)
		{
			parameters.Add(new("recvWindow", _receiveWindow.ToString(CultureInfo.InvariantCulture)));
			parameters.Add(new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				.ToString(CultureInfo.InvariantCulture)));
			var unsigned = BuildQuery(parameters);
			parameters.Add(new("signature", Sign(unsigned)));
		}
		var payload = BuildQuery(parameters);
		var canRetry = method == HttpMethod.Get;
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var hasBody = method == HttpMethod.Post || method == HttpMethod.Put;
			var requestUri = new Uri(_endpoint, path.TrimStart('/') +
				(hasBody || payload.IsEmpty() ? string.Empty : "?" + payload));
			using var request = new HttpRequestMessage(method, requestUri);
			if (hasBody)
				request.Content = new StringContent(payload, Encoding.UTF8,
					"application/x-www-form-urlencoded");
			if (isSigned)
				request.Headers.Add("X-OURBIT-APIKEY", _apiKey);

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);
			if (canRetry && attempt + 1 < _maximumReadAttempts && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException("Ourbit API key and secret are required for private spot operations.");
	}

	private string Sign(string value)
	{
		using (_signSync.EnterScope())
			return Convert.ToHexString(_hasher.ComputeHash(value.UTF8())).ToLowerInvariant();
	}

	private static string BuildQuery(IEnumerable<OurbitParameter> parameters)
		=> parameters.Select(static parameter =>
			$"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}").Join("&");

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			return default;
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Ourbit spot API returned malformed JSON.", error);
		}
	}

	private static Exception CreateException(HttpStatusCode statusCode, string body)
	{
		OurbitSpotError error = null;
		try
		{
			if (!body.IsEmpty())
				error = JsonConvert.DeserializeObject<OurbitSpotError>(body);
		}
		catch (JsonException)
		{
		}
		return new InvalidOperationException(
			$"Ourbit spot HTTP {(int)statusCode} ({statusCode}), code={error?.Code}: {error?.Message ?? body}".Trim());
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(25);
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
