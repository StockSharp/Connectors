namespace StockSharp.MaxExchange.Native;

sealed class MaxExchangeRestClient : BaseLogReceiver
{
	private const string _apiNamespace = "/api/v3";
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly MaxExchangeAuthenticator _authenticator;
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

	public MaxExchangeRestClient(string endpoint,
		SecureString key, SecureString secret)
	{
		_endpoint = new Uri(
			endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_authenticator = new(key, secret);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-MAX-Exchange-Connector/1.0");
	}

	public override string Name => "MAX_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<MaxExchangeSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<MaxExchangeSymbol[]>(
			HttpMethod.Get, "/markets", [], false, cancellationToken);

	public ValueTask<MaxExchangeTicker> GetTickerAsync(
		string symbol, CancellationToken cancellationToken)
		=> SendAsync<MaxExchangeTicker>(
			HttpMethod.Get, "/ticker",
			Values(("market", NormalizeSymbol(symbol))),
			false, cancellationToken);

	public async ValueTask<MaxExchangeOrderBook> GetOrderBookAsync(
		string symbol, int depth, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var result = await SendAsync<MaxExchangeOrderBook>(
			HttpMethod.Get, "/depth",
			Values(
				("market", symbol),
				("limit", depth.Max(1).Min(300))),
			false, cancellationToken);
		result.Pair = symbol;
		result.Limit = depth;
		if (result.Timestamp is > 0 and < 100_000_000_000)
			result.Timestamp = checked(result.Timestamp * 1000);
		return result;
	}

	public ValueTask<MaxExchangeTrade[]> GetPublicTradesAsync(
		string symbol, CancellationToken cancellationToken)
		=> SendAsync<MaxExchangeTrade[]>(
			HttpMethod.Get, "/trades",
			Values(
				("market", NormalizeSymbol(symbol)),
				("limit", 1000)),
			false, cancellationToken);

	public async ValueTask<MaxExchangeCandle[]> GetCandlesAsync(
		string symbol, string resolution, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var seconds = resolution.ToMaxExchangeTimeFrame().TotalSeconds
			.To<long>();
		var requested = Math.Ceiling(
			(to.ToUtc() - from.ToUtc()).TotalSeconds / seconds)
			.To<long>().Max(1).Min(10000);
		var values = await SendRawAsync(
			HttpMethod.Get, "/k",
			Values(
				("market", NormalizeSymbol(symbol)),
				("limit", requested),
				("period", seconds),
				("timestamp", from.ToUtc().ToMaxExchangeSeconds())),
			false, cancellationToken);
		return DeserializeKlines(values);
	}

	public ValueTask<MaxExchangeBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<MaxExchangeBalance[]>(
			HttpMethod.Get, "/wallet/spot/accounts", [],
			true, cancellationToken);

	public ValueTask<MaxExchangeOrder[]> GetOpenOrdersAsync(
		string symbol, CancellationToken cancellationToken)
		=> SendAsync<MaxExchangeOrder[]>(
			HttpMethod.Get, "/wallet/spot/orders/open",
			Values(
				("market", NormalizeSymbol(symbol)),
				("limit", 1000)),
			true, cancellationToken);

	public ValueTask<MaxExchangeOrder> GetOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		_ = symbol;
		return SendAsync<MaxExchangeOrder>(
			HttpMethod.Get, "/order",
			Values(("id", ParseOrderId(orderId))),
			true, cancellationToken);
	}

	public ValueTask<MaxExchangeOrder[]> GetOrdersAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		_ = to;
		return SendAsync<MaxExchangeOrder[]>(
			HttpMethod.Get, "/wallet/spot/orders/closed",
			Values(
				("market", NormalizeSymbol(symbol)),
				("timestamp", from?.ToUtc()
					.ToMaxExchangeMilliseconds()),
				("order_by", "asc"),
				("limit", limit.Max(1).Min(1000))),
			true, cancellationToken);
	}

	public ValueTask<MaxExchangePrivateTrade[]> GetPrivateTradesAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		_ = to;
		return SendAsync<MaxExchangePrivateTrade[]>(
			HttpMethod.Get, "/wallet/spot/trades",
			Values(
				("market", NormalizeOptionalSymbol(symbol)),
				("timestamp", from?.ToUtc()
					.ToMaxExchangeMilliseconds()),
				("order", "asc"),
				("limit", limit.Max(1).Min(1000))),
			true, cancellationToken);
	}

	public ValueTask<MaxExchangePlaceOrderResult> PlaceOrderAsync(
		string symbol, MaxExchangePlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		var values = ToValues(order);
		values["market"] = NormalizeSymbol(symbol);
		return SendAsync<MaxExchangePlaceOrderResult>(
			HttpMethod.Post, "/wallet/spot/order",
			values, true, cancellationToken);
	}

	public async ValueTask CancelOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		_ = symbol;
		_ = await SendAsync<JToken>(
			HttpMethod.Delete, "/order",
			Values(("id", ParseOrderId(orderId))),
			true, cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol, CancellationToken cancellationToken)
	{
		_ = await SendAsync<JToken[]>(
			HttpMethod.Delete, "/wallet/spot/orders",
			Values(("market", NormalizeOptionalSymbol(symbol))),
			true, cancellationToken);
	}

	internal static MaxExchangeCandle[] DeserializeKlines(string body)
	{
		try
		{
			var root = JArray.Parse(
				body.ThrowIfEmpty(nameof(body)));
			return [.. root
				.OfType<JArray>()
				.Where(static candle => candle.Count >= 6)
				.Select(static candle => new MaxExchangeCandle
				{
					Timestamp = candle[0].Value<long>(),
					Open = candle[1].Value<decimal>(),
					High = candle[2].Value<decimal>(),
					Low = candle[3].Value<decimal>(),
					Close = candle[4].Value<decimal>(),
					Volume = candle[5].Value<decimal>(),
				})];
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"MAX Exchange returned malformed kline data.", error);
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

	internal static int NormalizeDepth(int depth)
	{
		foreach (var supported in new[] { 1, 5, 10, 20, 50 })
			if (depth <= supported)
				return supported;
		return 50;
	}

	private async ValueTask<TData> SendAsync<TData>(
		HttpMethod method, string path,
		Dictionary<string, object> values, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var body = await SendRawAsync(
			method, path, values, isPrivate, cancellationToken);
		if (body.IsEmpty())
			return default;
		try
		{
			return JsonConvert.DeserializeObject<TData>(
				body, _jsonSettings) ??
				throw new InvalidDataException(
					"MAX Exchange returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"MAX Exchange returned an unexpected response shape.",
				error);
		}
	}

	private async ValueTask<string> SendRawAsync(
		HttpMethod method, string path,
		Dictionary<string, object> values, bool isPrivate,
		CancellationToken cancellationToken)
	{
		if (isPrivate)
			EnsureCredentials();
		path = "/" + path.ThrowIfEmpty(nameof(path)).Trim('/');
		values ??= [];

		for (var attempt = 0; ; attempt++)
		{
			var nonce = NextNonce();
			await WaitRateLimitAsync(cancellationToken);
			var fullPath = _apiNamespace + path;
			var requestValues = new Dictionary<string, object>(
				values, StringComparer.Ordinal)
			{
				["nonce"] = nonce,
			};
			var target = method == HttpMethod.Get
				? BuildTarget(fullPath, requestValues)
				: fullPath.TrimStart('/');
			using var request = new HttpRequestMessage(
				method, new Uri(_endpoint, target));
			if (method != HttpMethod.Get)
			{
				request.Content = new StringContent(
					SerializeBody(requestValues),
					Encoding.UTF8, "application/json");
			}
			if (isPrivate)
				AddAuthentication(
					request, fullPath, nonce, values);
			using var response = await _http.SendAsync(
				request, HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return responseBody;
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(
					response.StatusCode, responseBody);
			await DelayRetryAsync(
				response, attempt, cancellationToken);
		}
	}

	private void AddAuthentication(HttpRequestMessage request,
		string path, long nonce,
		IReadOnlyDictionary<string, object> values)
	{
		var payload = _authenticator.BuildPayload(
			path, nonce, values);
		request.Headers.TryAddWithoutValidation(
			"X-MAX-ACCESSKEY", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"X-MAX-PAYLOAD", payload);
		request.Headers.TryAddWithoutValidation(
			"X-MAX-SIGNATURE", _authenticator.Sign(payload));
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = DateTime.UtcNow.ToMaxExchangeMilliseconds();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(
				ref _lastNonce, next, current) == current)
				return next;
		}
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

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"MAX Exchange API key and secret are required " +
					"for private operations.");
	}

	private static Dictionary<string, object> Values(
		params (string Name, object Value)[] values)
		=> values
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.ToDictionary(
				static value => value.Name,
				static value => value.Value,
				StringComparer.Ordinal);

	private static Dictionary<string, object> ToValues(object value)
	{
		var json = JObject.FromObject(value);
		return json.Properties()
			.Where(static property =>
				property.Value.Type is not JTokenType.Null and
					not JTokenType.Undefined)
			.ToDictionary(
				static property => property.Name,
				static property => property.Value is JValue scalar
					? scalar.Value
					: property.Value.ToObject<object>(),
				StringComparer.Ordinal);
	}

	private static string BuildTarget(string path,
		IEnumerable<KeyValuePair<string, object>> values)
	{
		var query = (values ?? [])
			.Where(static pair =>
				!pair.Key.IsEmpty() && pair.Value is not null)
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" +
				Uri.EscapeDataString(Convert.ToString(
					pair.Value, CultureInfo.InvariantCulture)))
			.Join("&");
		return path.TrimStart('/') +
			(query.IsEmpty() ? string.Empty : "?" + query);
	}

	private static long ParseOrderId(string orderId)
		=> long.TryParse(
			orderId.ThrowIfEmpty(nameof(orderId)),
			NumberStyles.None, CultureInfo.InvariantCulture,
			out var value)
				? value
				: throw new FormatException(
					$"Invalid MAX Exchange order ID '{orderId}'.");

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().Contains(
			'/', StringComparison.Ordinal)
				? symbol.ToMaxExchangeSymbol()
				: symbol.Trim().ToLowerInvariant();

	private static string NormalizeOptionalSymbol(string symbol)
		=> symbol.IsEmpty() ? null : NormalizeSymbol(symbol);

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

	private static Exception CreateHttpError(
		HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<
				MaxExchangeError>(body);
			if (error is not null)
				details = new[]
				{
					error.Code,
					error.Error,
					error.Message,
					error.Errors?.Join("; "),
				}
				.Where(static value => !value.IsEmpty())
				.Join(": ");
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"MAX Exchange HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null, statusCode);
	}
}
