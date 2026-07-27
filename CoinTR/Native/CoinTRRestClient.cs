namespace StockSharp.CoinTR.Native;

sealed class CoinTRRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly CoinTRAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private long _lastTimestamp;

	public CoinTRRestClient(string endpoint, SecureString key,
		SecureString secret, SecureString passphrase)
	{
		_endpoint = CreateEndpoint(endpoint, nameof(endpoint));
		_authenticator = new(key, secret, passphrase);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinTR-Connector/1.0");
	}

	public override string Name => "CoinTR_REST";

	public bool IsCredentialsAvailable
		=> _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<CoinTRSymbol[]> GetSymbolsAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTRSymbol[]>(
			"/api/v2/spot/public/symbols",
			Query(("symbol", symbol)), false, cancellationToken);

	public ValueTask<CoinTRTicker[]> GetTickersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTRTicker[]>(
			"/api/v2/spot/market/tickers",
			Query(("symbol", symbol)), false, cancellationToken);

	public ValueTask<CoinTROrderBook> GetOrderBookAsync(string symbol,
		int depth, CancellationToken cancellationToken)
		=> SendGetAsync<CoinTROrderBook>(
			"/api/v2/spot/market/orderbook",
			Query(
				("symbol", symbol),
				("type", "step0"),
				("limit", depth.Max(1).Min(150).ToString(
					CultureInfo.InvariantCulture))),
			false, cancellationToken);

	public ValueTask<CoinTRTrade[]> GetTradesAsync(string symbol, int limit,
		DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
	{
		var isHistory = from is not null || to is not null || limit > 100;
		return SendGetAsync<CoinTRTrade[]>(
			isHistory
				? "/api/v2/spot/market/fills-history"
				: "/api/v2/spot/market/fills",
			Query(
				("symbol", symbol),
				("limit", limit.Max(1).Min(isHistory ? 1000 : 100)
					.ToString(CultureInfo.InvariantCulture)),
				("startTime", ToTimestamp(from)),
				("endTime", ToTimestamp(to))),
			false, cancellationToken);
	}

	public ValueTask<CoinTRCandle[]> GetCandlesAsync(string symbol,
		string granularity, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTRCandle[]>(
			"/api/v2/spot/market/candles",
			Query(
				("symbol", symbol),
				("granularity", granularity),
				("startTime", ToTimestamp(from)),
				("endTime", ToTimestamp(to)),
				("limit", limit.Max(1).Min(1000)
					.ToString(CultureInfo.InvariantCulture))),
			false, cancellationToken);

	public ValueTask<CoinTRBalance[]> GetAssetsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTRBalance[]>(
			"/api/v2/spot/account/assets",
			Query(("assetType", "hold_only")), true, cancellationToken);

	public ValueTask<CoinTROrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTROrder[]>(
			"/api/v2/spot/trade/unfilled-orders",
			Query(("symbol", symbol), ("limit", "100")),
			true, cancellationToken);

	public ValueTask<CoinTROrder[]> GetOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTROrder[]>(
			"/api/v2/spot/trade/orderInfo",
			Query(("orderId", orderId.ThrowIfEmpty(nameof(orderId)))),
			true, cancellationToken);

	public ValueTask<CoinTROrder[]> GetHistoryOrdersAsync(string symbol,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTROrder[]>(
			"/api/v2/spot/trade/history-orders",
			Query(
				("symbol", symbol),
				("startTime", ToTimestamp(from)),
				("endTime", ToTimestamp(to)),
				("limit", limit.Max(1).Min(100)
					.ToString(CultureInfo.InvariantCulture))),
			true, cancellationToken);

	public ValueTask<CoinTRFill[]> GetFillsAsync(string symbol,
		DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
		=> SendGetAsync<CoinTRFill[]>(
			"/api/v2/spot/trade/fills",
			Query(
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("startTime", ToTimestamp(from)),
				("endTime", ToTimestamp(to)),
				("limit", "100")),
			true, cancellationToken);

	public ValueTask<CoinTRPlaceOrderResult> PlaceOrderAsync(
		CoinTRPlaceOrderRequest order,
		CancellationToken cancellationToken)
		=> SendPostAsync<CoinTRPlaceOrderResult>(
			"/api/v2/spot/trade/place-order",
			order ?? throw new ArgumentNullException(nameof(order)),
			cancellationToken);

	public async ValueTask CancelOrderAsync(string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		_ = await SendPostAsync<JToken>(
			"/api/v2/spot/trade/cancel-order",
			new
			{
				symbol = symbol.ThrowIfEmpty(nameof(symbol)),
				orderId = orderId.ThrowIfEmpty(nameof(orderId)),
			},
			cancellationToken);
	}

	public async ValueTask BatchCancelOrdersAsync(string symbol,
		IEnumerable<string> orderIds,
		CancellationToken cancellationToken)
	{
		var items = (orderIds ??
			throw new ArgumentNullException(nameof(orderIds)))
			.Where(static id => !id.IsEmpty())
			.Distinct(StringComparer.Ordinal)
			.Take(50)
			.Select(static id => new { orderId = id })
			.ToArray();
		if (items.Length == 0)
			return;
		_ = await SendPostAsync<JToken>(
			"/api/v2/spot/trade/batch-cancel-order",
			new
			{
				symbol = symbol.ThrowIfEmpty(nameof(symbol)),
				orderList = items,
			},
			cancellationToken);
	}

	internal static string SerializeBody(object value)
		=> JsonConvert.SerializeObject(value,
			new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			});

	private async ValueTask<TData> SendGetAsync<TData>(string path,
		KeyValuePair<string, string>[] query, bool isPrivate,
		CancellationToken cancellationToken)
	{
		if (isPrivate)
			EnsureCredentials();
		var target = BuildTarget(path, query);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, target));
			if (isPrivate)
				AddAuthentication(request, path, query, string.Empty);
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

	private async ValueTask<TData> SendPostAsync<TData>(string path,
		object body, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var json = SerializeBody(body);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8,
				"application/json"),
		};
		AddAuthentication(request, path, [], json);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return Unwrap<TData>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, string path,
		IEnumerable<KeyValuePair<string, string>> query, string body)
	{
		var timestamp = NextTimestamp();
		request.Headers.TryAddWithoutValidation(
			"ACCESS-KEY", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"ACCESS-SIGN", _authenticator.Sign(
				timestamp, request.Method.Method, path, query, body));
		request.Headers.TryAddWithoutValidation(
			"ACCESS-TIMESTAMP",
			timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation(
			"ACCESS-PASSPHRASE", _authenticator.Passphrase);
		request.Headers.TryAddWithoutValidation("locale", "en-US");
	}

	private long NextTimestamp()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastTimestamp);
			var now = DateTime.UtcNow.ToCoinTRTime();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(ref _lastTimestamp, next,
				current) == current)
				return next;
		}
	}

	private static KeyValuePair<string, string>[] Query(
		params (string Name, string Value)[] values)
		=> values
			.Where(static value => !value.Name.IsEmpty() &&
				value.Value is not null)
			.Select(static value =>
				new KeyValuePair<string, string>(
					value.Name, value.Value))
			.ToArray();

	private static string ToTimestamp(DateTime? value)
		=> value is DateTime timestamp
			? timestamp.ToUtc().ToCoinTRTime().ToString(
				CultureInfo.InvariantCulture)
			: null;

	private static string BuildTarget(string path,
		IEnumerable<KeyValuePair<string, string>> query)
	{
		var queryString = CoinTRAuthenticator.BuildQuery(query);
		return path.TrimStart('/') +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private TData Unwrap<TData>(string body)
	{
		var response = Deserialize<CoinTRResponse<TData>>(body);
		if (!response.IsSuccess)
			throw new InvalidDataException(
				$"CoinTR request failed ({response.Code}): " +
				response.Message);
		return response.Data;
	}

	private TData Deserialize<TData>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TData>(
				body, _jsonSettings) ??
				throw new InvalidDataException(
					"CoinTR returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinTR returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"CoinTR API key, secret and passphrase are required " +
				"for private operations.");
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
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
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private static Uri CreateEndpoint(string endpoint, string name)
		=> new(endpoint.ThrowIfEmpty(name).TrimEnd('/') + "/",
			UriKind.Absolute);

	private static Exception CreateHttpError(HttpStatusCode statusCode,
		string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<
				CoinTRResponse<JToken>>(body);
			if (error is not null)
				details = $"({error.Code}) {error.Message}";
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"CoinTR HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}
}
