namespace StockSharp.BitoPro.Native;

sealed class BitoProRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly BitoProAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private long _lastNonce;

	public BitoProRestClient(string endpoint, string email,
		SecureString key, SecureString secret)
	{
		_endpoint = CreateEndpoint(endpoint, nameof(endpoint));
		_authenticator = new(email, key, secret);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-BitoPro-Connector/1.0");
	}

	public override string Name => "BitoPro_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<BitoProSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProSymbol[]>>(
			"/provisioning/trading-pairs", [], false,
			cancellationToken)).Data;

	public async ValueTask<BitoProTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<BitoProDataResponse<BitoProTicker>>(
			$"/tickers/{NormalizeSymbol(symbol)}", [], false,
			cancellationToken)).Data;

	public ValueTask<BitoProOrderBook> GetOrderBookAsync(string symbol,
		int depth, CancellationToken cancellationToken)
		=> SendGetAsync<BitoProOrderBook>(
			$"/order-book/{NormalizeSymbol(symbol)}",
			Query(("limit", NormalizeDepth(depth).ToString(
				CultureInfo.InvariantCulture))),
			false, cancellationToken);

	public async ValueTask<BitoProTrade[]> GetPublicTradesAsync(
		string symbol, CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProTrade[]>>(
			$"/trades/{NormalizeSymbol(symbol)}", [], false,
			cancellationToken)).Data;

	public async ValueTask<BitoProCandle[]> GetCandlesAsync(string symbol,
		string resolution, DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProCandle[]>>(
			$"/trading-history/{NormalizeSymbol(symbol)}",
			Query(
				("resolution", resolution.ThrowIfEmpty(
					nameof(resolution))),
				("from", from.ToUtc().ToBitoProSeconds().ToString(
					CultureInfo.InvariantCulture)),
				("to", to.ToUtc().ToBitoProSeconds().ToString(
					CultureInfo.InvariantCulture))),
			false, cancellationToken)).Data;

	public async ValueTask<BitoProBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProBalance[]>>(
			"/accounts/balance", [], true,
			cancellationToken)).Data;

	public async ValueTask<BitoProOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProOrder[]>>(
			"/orders/open",
			Query(("pair", NormalizeOptionalSymbol(symbol))),
			true, cancellationToken)).Data;

	public ValueTask<BitoProOrder> GetOrderAsync(string symbol,
		string orderId, CancellationToken cancellationToken)
		=> SendGetAsync<BitoProOrder>(
			$"/orders/{NormalizeSymbol(symbol)}/" +
				Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId))),
			[], true, cancellationToken);

	public async ValueTask<BitoProOrder[]> GetOrdersAsync(string symbol,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProOrder[]>>(
			$"/orders/all/{NormalizeSymbol(symbol)}",
			Query(
				("startTimestamp", ToMilliseconds(from)),
				("endTimestamp", ToMilliseconds(to)),
				("statusKind", "ALL"),
				("limit", limit.Max(1).Min(1000).ToString(
					CultureInfo.InvariantCulture))),
			true, cancellationToken)).Data;

	public async ValueTask<BitoProPrivateTrade[]> GetPrivateTradesAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<
			BitoProDataResponse<BitoProPrivateTrade[]>>(
			$"/orders/trades/{NormalizeSymbol(symbol)}",
			Query(
				("startTimestamp", ToMilliseconds(from)),
				("endTimestamp", ToMilliseconds(to)),
				("limit", limit.Max(1).Min(1000).ToString(
					CultureInfo.InvariantCulture))),
			true, cancellationToken)).Data;

	public ValueTask<BitoProPlaceOrderResult> PlaceOrderAsync(string symbol,
		BitoProPlaceOrderRequest order,
		CancellationToken cancellationToken)
		=> SendPostAsync<BitoProPlaceOrderResult>(
			$"/orders/{NormalizeSymbol(symbol)}",
			order ?? throw new ArgumentNullException(nameof(order)),
			cancellationToken);

	public async ValueTask CancelOrderAsync(string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		_ = await SendDeleteAsync<JToken>(
			$"/orders/{NormalizeSymbol(symbol)}/" +
				Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId))),
			[], cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var path = symbol.IsEmpty()
			? "/orders/all"
			: $"/orders/{NormalizeSymbol(symbol)}";
		_ = await SendDeleteAsync<JToken>(
			path, [], cancellationToken);
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
			using var request = new HttpRequestMessage(
				HttpMethod.Get, new Uri(_endpoint, target));
			if (isPrivate)
				AddAuthentication(request,
					_authenticator.CreateGetPayload(NextNonce()));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TData>(responseBody);
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
		var payload = _authenticator.CreatePostPayload(json);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8,
				"application/json"),
		};
		AddAuthentication(request, payload);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return Deserialize<TData>(responseBody);
	}

	private async ValueTask<TData> SendDeleteAsync<TData>(string path,
		KeyValuePair<string, string>[] query,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete,
			new Uri(_endpoint, BuildTarget(path, query)));
		AddAuthentication(request,
			_authenticator.CreateGetPayload(NextNonce()));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return responseBody.IsEmpty()
			? default
			: Deserialize<TData>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request,
		string payload)
	{
		request.Headers.TryAddWithoutValidation(
			"X-BITOPRO-APIKEY", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"X-BITOPRO-PAYLOAD", payload);
		request.Headers.TryAddWithoutValidation(
			"X-BITOPRO-SIGNATURE", _authenticator.Sign(payload));
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = DateTime.UtcNow.ToBitoProMilliseconds();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(
				ref _lastNonce, next, current) == current)
				return next;
		}
	}

	private TData Deserialize<TData>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TData>(
				body.ThrowIfEmpty(nameof(body)), _jsonSettings) ??
				throw new InvalidDataException(
					"BitoPro returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BitoPro returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BitoPro email, API key and secret are required " +
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(100);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static KeyValuePair<string, string>[] Query(
		params (string Name, string Value)[] values)
		=> values
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.Select(static value =>
				new KeyValuePair<string, string>(
					value.Name, value.Value))
			.ToArray();

	private static string BuildTarget(string path,
		IEnumerable<KeyValuePair<string, string>> query)
	{
		var queryString = (query ?? [])
			.Where(static pair =>
				!pair.Key.IsEmpty() && pair.Value is not null)
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" +
				Uri.EscapeDataString(pair.Value))
			.Join("&");
		return path.TrimStart('/') +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private static string ToMilliseconds(DateTime? value)
		=> value is DateTime time
			? time.ToUtc().ToBitoProMilliseconds().ToString(
				CultureInfo.InvariantCulture)
			: null;

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToBitoProSymbol().ToLowerInvariant();

	private static string NormalizeOptionalSymbol(string symbol)
		=> symbol.IsEmpty() ? null : NormalizeSymbol(symbol);

	internal static int NormalizeDepth(int depth)
	{
		foreach (var supported in new[] { 1, 5, 10, 20, 30, 50 })
			if (depth <= supported)
				return supported;
		return 50;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			(int)statusCode >= 500;

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
			var error = JsonConvert.DeserializeObject<BitoProError>(body);
			if (error is not null)
				details = new[] { error.Code, error.Error, error.Message }
					.Where(static value => !value.IsEmpty())
					.Join(": ");
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"BitoPro HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null, statusCode);
	}
}
