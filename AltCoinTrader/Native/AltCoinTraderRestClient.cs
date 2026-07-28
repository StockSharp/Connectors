namespace StockSharp.AltCoinTrader.Native;

sealed class AltCoinTraderRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly AltCoinTraderAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public AltCoinTraderRestClient(
		string endpoint,
		SecureString key,
		SecureString secret)
	{
		_endpoint = new Uri(
			endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		if (!_endpoint.Scheme.EqualsIgnoreCase("https"))
			throw new ArgumentException(
				"AltCoinTrader REST endpoint must use HTTPS.",
				nameof(endpoint));
		_authenticator = new(key, secret);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-AltCoinTrader-Connector/1.0");
	}

	public override string Name => "ALTCOINTRADER_REST";

	public bool IsCredentialsAvailable
		=> _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<AltCoinTraderMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderMarket[]>(
			HttpMethod.Get,
			"/markets",
			[],
			null,
			false,
			cancellationToken);

	public ValueTask<AltCoinTraderTicker> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderTicker>(
			HttpMethod.Get,
			$"/ticker/{NormalizeSymbol(symbol)}",
			[],
			null,
			false,
			cancellationToken);

	public ValueTask<AltCoinTraderOrderBook> GetOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrderBook>(
			HttpMethod.Get,
			$"/orderbook/{NormalizeSymbol(symbol)}",
			Values(("limit", NormalizeDepth(depth))),
			null,
			false,
			cancellationToken);

	public ValueTask<AltCoinTraderTrade[]> GetPublicTradesAsync(
		string symbol,
		int limit,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderTrade[]>(
			HttpMethod.Get,
			$"/trades/{NormalizeSymbol(symbol)}",
			Values(("limit", NormalizeTradeLimit(limit))),
			null,
			false,
			cancellationToken);

	public ValueTask<AltCoinTraderBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderBalance[]>(
			HttpMethod.Get,
			"/balances",
			[],
			null,
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderOrder> PlaceLimitOrderAsync(
		AltCoinTraderLimitOrderRequest order,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrder>(
			HttpMethod.Post,
			"/orders",
			[],
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderOrder> PlaceMarketOrderAsync(
		AltCoinTraderMarketOrderRequest order,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrder>(
			HttpMethod.Post,
			"/orders/market",
			[],
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderOrder[]> GetOpenOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrder[]>(
			HttpMethod.Get,
			"/orders/open",
			Values(("market", NormalizeOptionalSymbol(symbol))),
			null,
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderOrder[]> GetOrdersAsync(
		string symbol,
		string status,
		DateTime? from,
		DateTime? to,
		int limit,
		int page,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrder[]>(
			HttpMethod.Get,
			"/orders/history",
			Values(
				("market", NormalizeOptionalSymbol(symbol)),
				("status", status),
				("start_time",
					from?.ToUtc().ToAltCoinTraderSeconds()),
				("end_time",
					to?.ToUtc().ToAltCoinTraderSeconds()),
				("limit", NormalizePrivateLimit(limit)),
				("page", page.Max(1))),
			null,
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderOrder> GetOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrder>(
			HttpMethod.Get,
			$"/orders/{EscapePath(orderId)}",
			[],
			null,
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderOrder> CancelOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderOrder>(
			HttpMethod.Delete,
			$"/orders/{EscapePath(orderId)}",
			[],
			null,
			true,
			cancellationToken);

	public ValueTask<AltCoinTraderUserTrade[]> GetPrivateTradesAsync(
		string symbol,
		DateTime? from,
		DateTime? to,
		int limit,
		int page,
		CancellationToken cancellationToken)
		=> SendAsync<AltCoinTraderUserTrade[]>(
			HttpMethod.Get,
			"/trades",
			Values(
				("market", NormalizeOptionalSymbol(symbol)),
				("start_time",
					from?.ToUtc().ToAltCoinTraderSeconds()),
				("end_time",
					to?.ToUtc().ToAltCoinTraderSeconds()),
				("limit", NormalizePrivateLimit(limit)),
				("page", page.Max(1))),
			null,
			true,
			cancellationToken);

	internal static int NormalizeDepth(int depth)
		=> depth.Max(1).Min(200);

	internal static int NormalizeTradeLimit(int limit)
		=> limit.Max(1).Min(500);

	internal static TData Deserialize<TData>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TData>(
				body.ThrowIfEmpty(nameof(body)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				});
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"AltCoinTrader returned malformed JSON.", error);
		}
	}

	internal static string SerializeBody(object value)
		=> JsonConvert.SerializeObject(
			value ?? throw new ArgumentNullException(nameof(value)),
			new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			});

	private async ValueTask<TData> SendAsync<TData>(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			method,
			path,
			query,
			body,
			isPrivate,
			cancellationToken);
		return Deserialize<TData>(raw);
	}

	private async ValueTask<string> SendRawAsync(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"AltCoinTrader API key and secret are required " +
					"for private operations.");

		path = "/" + path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var queryString = BuildQuery(query);
		var target = path +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
		var bodyText = body is null ? null : SerializeBody(body);
		var maximumAttempts = method == HttpMethod.Get
			? _maximumReadAttempts
			: 1;

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(
				method,
				new Uri(_endpoint, target.TrimStart('/')));
			if (bodyText is not null)
				request.Content = new StringContent(
					bodyText,
					Encoding.UTF8,
					"application/json");
			if (isPrivate)
				AddAuthentication(
					request,
					method.Method,
					path,
					bodyText);

			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var responseBody =
				await response.Content.ReadAsStringAsync(
					cancellationToken);
			if (response.IsSuccessStatusCode)
				return responseBody;
			if (attempt + 1 >= maximumAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(
					response.StatusCode, responseBody);
			await DelayRetryAsync(
				response, attempt, cancellationToken);
		}
	}

	private void AddAuthentication(
		HttpRequestMessage request,
		string method,
		string path,
		string body)
	{
		var timestamp = DateTime.UtcNow
			.ToAltCoinTraderSeconds();
		request.Headers.TryAddWithoutValidation(
			"X-API-KEY",
			_authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"X-TIMESTAMP",
			timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation(
			"X-SIGNATURE",
			_authenticator.Sign(
				timestamp,
				method,
				path,
				body));
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
			_nextRequestTime =
				DateTime.UtcNow.AddMilliseconds(250);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static int NormalizePrivateLimit(int limit)
		=> limit.Max(1).Min(200);

	private static (string Name, object Value)[] Values(
		params (string Name, object Value)[] values)
		=> [.. (values ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)];

	private static string BuildQuery(
		IEnumerable<(string Name, object Value)> values)
		=> (values ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.Select(static value =>
				Uri.EscapeDataString(value.Name) + "=" +
				Uri.EscapeDataString(Convert.ToString(
					value.Value,
					CultureInfo.InvariantCulture)))
			.Join("&");

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToAltCoinTraderSymbol();

	private static string NormalizeOptionalSymbol(string symbol)
		=> symbol.IsEmpty() ? null : NormalizeSymbol(symbol);

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(
			value.ThrowIfEmpty(nameof(value)).Trim());

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response,
		int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromSeconds(1 << attempt);
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(
		HttpStatusCode statusCode,
		string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<
				AltCoinTraderError>(body);
			if (error is not null)
				details = $"{error.Code}: {error.Message}";
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"AltCoinTrader HTTP {(int)statusCode} " +
				$"({statusCode}): {details}",
			null,
			statusCode);
	}
}
