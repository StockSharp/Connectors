namespace StockSharp.BloFin.Native;

sealed class BloFinRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _secret;
	private readonly string _passphrase;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private DateTime _nextTradeRequestTime;

	public BloFinRestClient(string endpoint, SecureString key, SecureString secret,
		SecureString passphrase)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/", UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secret = secret.IsEmpty() ? null : secret.UnSecure();
		_passphrase = passphrase.IsEmpty() ? null : passphrase.UnSecure();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-BloFin-Connector/1.0");
	}

	public override string Name => nameof(BloFin) + "_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && !_secret.IsEmpty() && !_passphrase.IsEmpty();

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BloFinServerTime> GetServerTimeAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BloFinServerTime>("api/v1/public/time", new BloFinEmptyQuery(), false,
			cancellationToken);

	public ValueTask<BloFinInstrument[]> GetInstrumentsAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinInstrument[]>("api/v1/market/instruments",
			new BloFinInstrumentQuery { InstrumentId = instrumentId }, false, cancellationToken);

	public ValueTask<BloFinTicker[]> GetTickersAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinTicker[]>("api/v1/market/tickers",
			new BloFinInstrumentQuery { InstrumentId = instrumentId }, false, cancellationToken);

	public ValueTask<BloFinBook[]> GetBookAsync(string instrumentId, int size,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinBook[]>("api/v1/market/books", new BloFinBookQuery
		{
			InstrumentId = instrumentId,
			Size = size,
		}, false, cancellationToken);

	public ValueTask<BloFinTrade[]> GetTradesAsync(string instrumentId, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinTrade[]>("api/v1/market/trades", new BloFinTradesQuery
		{
			InstrumentId = instrumentId,
			Limit = limit,
		}, false, cancellationToken);

	public ValueTask<BloFinFundingRate[]> GetFundingRatesAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinFundingRate[]>("api/v1/market/funding-rate",
			new BloFinInstrumentQuery { InstrumentId = instrumentId }, false, cancellationToken);

	public ValueTask<BloFinCandle[]> GetCandlesAsync(BloFinCandlesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinCandle[]>("api/v1/market/candles", query, false, cancellationToken);

	public ValueTask<BloFinAccount> GetAccountAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BloFinAccount>("api/v1/account/balance", new BloFinEmptyQuery(), true,
			cancellationToken);

	public ValueTask<BloFinPosition[]> GetPositionsAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinPosition[]>("api/v1/account/positions",
			new BloFinPositionsQuery { InstrumentId = instrumentId }, true, cancellationToken);

	public ValueTask<BloFinOrder[]> GetPendingOrdersAsync(BloFinOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinOrder[]>("api/v1/trade/orders-pending", query, true,
			cancellationToken);

	public ValueTask<BloFinOrder[]> GetOrderHistoryAsync(BloFinOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinOrder[]>("api/v1/trade/orders-history", query, true,
			cancellationToken);

	public ValueTask<BloFinFill[]> GetFillsAsync(BloFinFillsQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BloFinFill[]>("api/v1/trade/fills-history", query, true,
			cancellationToken);

	public ValueTask<BloFinOperationResult[]> PlaceOrderAsync(BloFinPlaceOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<BloFinOperationResult[], BloFinPlaceOrderRequest>("api/v1/trade/order",
			request, cancellationToken);

	public ValueTask<BloFinOperationResult> CancelOrderAsync(BloFinCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<BloFinOperationResult, BloFinCancelOrderRequest>(
			"api/v1/trade/cancel-order", request, cancellationToken);

	public ValueTask<BloFinOperationResult[]> CancelOrdersAsync(BloFinCancelOrderRequest[] request,
		CancellationToken cancellationToken)
		=> SendPostAsync<BloFinOperationResult[], BloFinCancelOrderRequest[]>(
			"api/v1/trade/cancel-batch-orders", request, cancellationToken);

	public ValueTask<BloFinLeverageResult> SetLeverageAsync(BloFinSetLeverageRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<BloFinLeverageResult, BloFinSetLeverageRequest>(
			"api/v1/account/set-leverage", request, cancellationToken);

	private async ValueTask<TData> SendGetAsync<TData>(string path, IBloFinQuery query,
		bool isSigned, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isSigned)
			EnsureCredentials();

		var queryString = query.ToQueryString();
		var requestPath = "/" + path.TrimStart('/') +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(false, cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, requestPath.TrimStart('/')));
			if (isSigned)
				AddAuthentication(request, requestPath, string.Empty);

			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return DeserializeResponse<TData>(body);

			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TData> SendPostAsync<TData, TRequest>(string path, TRequest body,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var requestPath = "/" + path.TrimStart('/');
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		await WaitRateLimitAsync(true, cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, requestPath.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, requestPath, json);
		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return DeserializeResponse<TData>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, string requestPath, string body)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
		var prehash = requestPath + request.Method.Method.ToUpperInvariant() + timestamp + nonce + body;
		var hash = HMACSHA256.HashData(_secret.UTF8(), prehash.UTF8());
		var hex = Convert.ToHexString(hash).ToLowerInvariant();
		var signature = Convert.ToBase64String(hex.UTF8());

		request.Headers.TryAddWithoutValidation("ACCESS-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("ACCESS-SIGN", signature);
		request.Headers.TryAddWithoutValidation("ACCESS-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("ACCESS-NONCE", nonce);
		request.Headers.TryAddWithoutValidation("ACCESS-PASSPHRASE", _passphrase);
	}

	private TData DeserializeResponse<TData>(string body)
	{
		BloFinResponseHeader header;
		try
		{
			header = JsonConvert.DeserializeObject<BloFinResponseHeader>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("BloFin returned malformed JSON.", error);
		}
		if (header is null)
			throw new InvalidDataException("BloFin returned an empty response.");
		if (header.Code != "0")
			throw new InvalidOperationException(
				$"BloFin API error {header.Code}: {header.Message}".Trim());

		try
		{
			var response = JsonConvert.DeserializeObject<BloFinResponse<TData>>(body, _jsonSettings);
			if (response is null)
				throw new InvalidDataException("BloFin returned an empty response.");
			return response.Data;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("BloFin returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BloFin API key, secret, and passphrase are required for private operations.");
	}

	private async ValueTask WaitRateLimitAsync(bool isTrade,
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var now = DateTime.UtcNow;
			var next = _nextRequestTime;
			if (isTrade && _nextTradeRequestTime > next)
				next = _nextTradeRequestTime;
			var delay = next - now;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			now = DateTime.UtcNow;
			_nextRequestTime = now.AddMilliseconds(125);
			if (isTrade)
				_nextTradeRequestTime = now.AddMilliseconds(335);
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
			$"BloFin HTTP {(int)statusCode} ({statusCode}): {text}".Trim(), null, statusCode);
	}
}
