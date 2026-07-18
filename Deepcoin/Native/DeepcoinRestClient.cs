namespace StockSharp.Deepcoin.Native;

sealed class DeepcoinRestClient : BaseLogReceiver
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

	public DeepcoinRestClient(string endpoint, SecureString key, SecureString secret,
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
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Deepcoin-Connector/2.0");
	}

	public override string Name => nameof(Deepcoin) + "_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && !_secret.IsEmpty() && !_passphrase.IsEmpty();

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<DeepcoinServerTime> GetServerTimeAsync(CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinServerTime>("deepcoin/v2/market/time", new DeepcoinEmptyQuery(),
			false, cancellationToken);

	public ValueTask<DeepcoinInstrument[]> GetInstrumentsAsync(DeepcoinProductTypes productType,
		string instrumentId, CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinInstrument[]>("deepcoin/v2/market/instruments",
			new DeepcoinInstrumentsQuery
			{
				ProductType = productType,
				InstrumentId = instrumentId,
			}, false, cancellationToken);

	public ValueTask<DeepcoinTicker[]> GetTickersAsync(DeepcoinProductTypes productType,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinTicker[]>("deepcoin/v2/market/tickers",
			new DeepcoinTickersQuery { ProductType = productType }, false, cancellationToken);

	public ValueTask<DeepcoinBook> GetBookAsync(string instrumentId, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinBook>("deepcoin/v2/market/books", new DeepcoinBookQuery
		{
			InstrumentId = instrumentId,
			Limit = limit,
		}, false, cancellationToken);

	public ValueTask<DeepcoinTrade[]> GetTradesAsync(string instrumentId, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinTrade[]>("deepcoin/v2/market/trades", new DeepcoinTradesQuery
		{
			InstrumentId = instrumentId,
			Limit = limit,
		}, false, cancellationToken);

	public ValueTask<DeepcoinCandle[]> GetCandlesAsync(DeepcoinCandlesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinCandle[]>("deepcoin/v2/market/candles", query, false,
			cancellationToken);

	public ValueTask<DeepcoinBalance[]> GetBalancesAsync(DeepcoinProductTypes productType,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinBalance[]>("deepcoin/v2/account/balances",
			new DeepcoinBalancesQuery { ProductType = productType }, true, cancellationToken);

	public ValueTask<DeepcoinPosition[]> GetPositionsAsync(DeepcoinProductTypes productType,
		string instrumentId, CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinPosition[]>("deepcoin/v2/account/positions",
			new DeepcoinPositionsQuery
			{
				ProductType = productType,
				InstrumentId = instrumentId,
			}, true, cancellationToken);

	public ValueTask<DeepcoinOrder[]> GetPendingOrdersAsync(DeepcoinPendingOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinOrder[]>("deepcoin/v2/trade/orders-pending", query, true,
			cancellationToken);

	public ValueTask<DeepcoinOrder[]> GetOrderHistoryAsync(DeepcoinOrdersHistoryQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinOrder[]>("deepcoin/v2/trade/orders-history", query, true,
			cancellationToken);

	public ValueTask<DeepcoinFill[]> GetFillsAsync(DeepcoinFillsQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinFill[]>("deepcoin/v2/trade/fills", query, true,
			cancellationToken);

	public ValueTask<DeepcoinOperationResult> PlaceOrderAsync(DeepcoinPlaceOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<DeepcoinOperationResult, DeepcoinPlaceOrderRequest>(
			"deepcoin/v2/trade/order", request, cancellationToken);

	public ValueTask<DeepcoinOperationResult> CancelOrderAsync(DeepcoinCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<DeepcoinOperationResult, DeepcoinCancelOrderRequest>(
			"deepcoin/v2/trade/cancel-order", request, cancellationToken);

	public ValueTask<DeepcoinOperationResult> AmendOrderAsync(DeepcoinAmendOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<DeepcoinOperationResult, DeepcoinAmendOrderRequest>(
			"deepcoin/v2/trade/amend-order", request, cancellationToken);

	public ValueTask<DeepcoinBatchCancelResult> BatchCancelAsync(DeepcoinBatchCancelRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<DeepcoinBatchCancelResult, DeepcoinBatchCancelRequest>(
			"deepcoin/v2/trade/batch-cancel-order", request, cancellationToken);

	public ValueTask<DeepcoinSetLeverageResult> SetLeverageAsync(DeepcoinSetLeverageRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<DeepcoinSetLeverageResult, DeepcoinSetLeverageRequest>(
			"deepcoin/v2/account/set-leverage", request, cancellationToken);

	public ValueTask<DeepcoinListenKey> AcquireListenKeyAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinListenKey>("deepcoin/v2/listenkey/acquire",
			new DeepcoinEmptyQuery(), true, cancellationToken);

	public ValueTask<DeepcoinListenKey> ExtendListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
		=> SendGetAsync<DeepcoinListenKey>("deepcoin/v2/listenkey/extend",
			new DeepcoinListenKeyQuery { ListenKey = listenKey }, true, cancellationToken);

	private async ValueTask<TData> SendGetAsync<TData>(string path, IDeepcoinQuery query,
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
			await WaitRateLimitAsync(cancellationToken);
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
		await WaitRateLimitAsync(cancellationToken);
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
		var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
			CultureInfo.InvariantCulture);
		var prehash = timestamp + request.Method.Method.ToUpperInvariant() + requestPath + body;
		var signature = Convert.ToBase64String(HMACSHA256.HashData(_secret.UTF8(), prehash.UTF8()));

		request.Headers.TryAddWithoutValidation("DC-ACCESS-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("DC-ACCESS-SIGN", signature);
		request.Headers.TryAddWithoutValidation("DC-ACCESS-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("DC-ACCESS-PASSPHRASE", _passphrase);
	}

	private TData DeserializeResponse<TData>(string body)
	{
		DeepcoinResponseHeader header;
		try
		{
			header = JsonConvert.DeserializeObject<DeepcoinResponseHeader>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Deepcoin returned malformed JSON.", error);
		}
		if (header is null)
			throw new InvalidDataException("Deepcoin returned an empty response.");

		try
		{
			if (header.Code.IsEmpty())
			{
				var direct = JsonConvert.DeserializeObject<TData>(body, _jsonSettings);
				return direct is null
					? throw new InvalidDataException("Deepcoin returned an empty response.")
					: direct;
			}
			if (header.Code != "0")
				throw new InvalidOperationException(
					$"Deepcoin API error {header.Code}: {header.Message}".Trim());

			var response = JsonConvert.DeserializeObject<DeepcoinResponse<TData>>(body, _jsonSettings);
			if (response is null)
				throw new InvalidDataException("Deepcoin returned an empty response.");
			return response.Data;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Deepcoin returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Deepcoin API key, secret, and passphrase are required for private operations.");
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
			$"Deepcoin HTTP {(int)statusCode} ({statusCode}): {text}".Trim(), null, statusCode);
	}
}
