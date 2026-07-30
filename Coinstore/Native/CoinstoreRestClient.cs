namespace StockSharp.Coinstore.Native;

sealed class CoinstoreRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly CoinstoreAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public CoinstoreRestClient(string endpoint,
		SecureString key, SecureString secret)
	{
		_endpoint = new Uri(
			endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_authenticator = new(key, secret);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Coinstore-Connector/1.0");
	}

	public override string Name => "COINSTORE_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<CoinstoreSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<CoinstoreSymbol[]>(
			HttpMethod.Post, "v2/public/config/spot/symbols",
			[], new { }, false, cancellationToken);

	public async ValueTask<CoinstoreTicker> GetTickerAsync(
		string symbol, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var tickers = await SendAsync<CoinstoreTicker[]>(
			HttpMethod.Get, "v1/market/tickers",
			[], null, false, cancellationToken);
		return (tickers ?? []).FirstOrDefault(
			ticker => ticker?.Pair.EqualsIgnoreCase(symbol) == true);
	}

	public async ValueTask<CoinstoreOrderBook> GetOrderBookAsync(
		string symbol, int depth, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		depth = NormalizeDepth(depth);
		var result = await SendAsync<CoinstoreOrderBook>(
			HttpMethod.Get,
			$"v1/market/depth/{Uri.EscapeDataString(symbol)}",
			Values(("depth", depth)), null, false,
			cancellationToken);
		if (result is not null)
		{
			result.Pair = result.Pair.IsEmpty(symbol);
			result.Limit = depth;
		}
		return result;
	}

	public async ValueTask<CoinstoreTrade[]> GetPublicTradesAsync(
		string symbol, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var result = await SendAsync<CoinstoreTrade[]>(
			HttpMethod.Get,
			$"v1/market/trade/{Uri.EscapeDataString(symbol)}",
			Values(("size", 100)), null, false,
			cancellationToken);

		foreach (var trade in result ?? [])
			trade.Pair = trade.Pair.IsEmpty(symbol);

		return result;
	}

	public async ValueTask<CoinstoreCandle[]> GetCandlesAsync(
		string symbol, string resolution, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var timeFrame = resolution.ToCoinstoreTimeFrame();
		var requested = Math.Ceiling(
			(to.ToUtc() - from.ToUtc()).TotalSeconds /
			timeFrame.TotalSeconds).To<int>().Max(1).Min(2000);
		var result = await SendAsync<CoinstoreKlineResult>(
			HttpMethod.Get,
			$"v1/market/kline/{Uri.EscapeDataString(symbol)}",
			Values(
				("period", timeFrame.ToCoinstoreRestPeriod()),
				("size", requested)),
			null, false, cancellationToken);
		return [.. (result?.Items ?? [])
			.Where(candle =>
			{
				var time = candle.StartTime.FromCoinstoreSeconds();
				return time >= from.ToUtc() && time <= to.ToUtc();
			})];
	}

	public async ValueTask<CoinstoreBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		var entries = await SendAsync<CoinstoreBalanceEntry[]>(
			HttpMethod.Post, "spot/accountList",
			[], new { }, true, cancellationToken);
		return [.. (entries ?? [])
			.Where(static entry => !entry.Currency.IsEmpty())
			.GroupBy(static entry => entry.Currency,
				StringComparer.OrdinalIgnoreCase)
			.Select(group =>
			{
				var amount = group.Sum(static entry => entry.Balance);
				var available = group
					.Where(static entry =>
						entry.Type == 1 ||
						entry.TypeName.EqualsIgnoreCase("AVAILABLE"))
					.Sum(static entry => entry.Balance);
				return new CoinstoreBalance
				{
					Currency = group.Key.ToUpperInvariant(),
					Amount = amount,
					Available = available,
				};
			})];
	}

	public ValueTask<CoinstoreOrder[]> GetOpenOrdersAsync(
		string symbol, CancellationToken cancellationToken)
		=> SendAsync<CoinstoreOrder[]>(
			HttpMethod.Get, "v2/trade/order/active",
			Values(("symbol", NormalizeOptionalSymbol(symbol))),
			null, true, cancellationToken);

	public async ValueTask<CoinstoreOrder> GetOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		var orders = await SendAsync<CoinstoreOrder[]>(
			HttpMethod.Get, "v2/trade/order/orderInfo",
			Values(("ordId", ParseOrderId(orderId))),
			null, true, cancellationToken);
		var result = (orders ?? []).FirstOrDefault();
		if (result is not null && result.Pair.IsEmpty())
			result.Pair = NormalizeOptionalSymbol(symbol);
		return result;
	}

	public ValueTask<CoinstoreOrder[]> GetOrdersAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		_ = from;
		_ = to;
		_ = limit;
		return GetOpenOrdersAsync(symbol, cancellationToken);
	}

	public async ValueTask<CoinstorePrivateTrade[]>
		GetPrivateTradesAsync(
			string symbol, DateTime? from, DateTime? to, int limit,
			CancellationToken cancellationToken)
	{
		_ = from;
		_ = to;
		symbol = NormalizeSymbol(symbol);
		var result = await SendAsync<CoinstorePrivateTrade[]>(
			HttpMethod.Get, "trade/match/accountMatches",
			Values(
				("symbol", symbol),
				("pageNum", 1),
				("pageSize", limit.Max(1).Min(100))),
			null, true, cancellationToken);

		foreach (var trade in result ?? [])
			trade.Pair = symbol;

		return result;
	}

	public async ValueTask<CoinstorePlaceOrderResult> PlaceOrderAsync(
		string symbol, CoinstorePlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		var body = JObject.FromObject(order, JsonSerializer.Create(
			_jsonSettings));
		body["symbol"] = NormalizeSymbol(symbol);
		body["timestamp"] ??= DateTime.UtcNow
			.ToCoinstoreMilliseconds();
		var data = await SendTokenAsync(
			HttpMethod.Post, "trade/order/place",
			[], body, true, cancellationToken);
		var orderId = data?["ordId"]?.Value<string>() ??
			data?["order_id"]?.Value<string>();
		return new() { OrderId = orderId };
	}

	public async ValueTask CancelOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		_ = await SendTokenAsync(
			HttpMethod.Post, "trade/order/cancel", [],
			new
			{
				symbol = NormalizeSymbol(symbol),
				ordId = ParseOrderId(orderId),
			},
			true, cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol, CancellationToken cancellationToken)
	{
		var orders = await GetOpenOrdersAsync(
			NormalizeOptionalSymbol(symbol), cancellationToken);

		foreach (var group in (orders ?? [])
			.Where(static order =>
				order?.Id.IsEmpty() == false &&
				!order.Pair.IsEmpty())
			.GroupBy(static order => order.Pair,
				StringComparer.OrdinalIgnoreCase))
		{
			var ids = group
				.Select(static order => ParseOrderId(order.Id))
				.ToArray();
			if (ids.Length == 0)
				continue;
			_ = await SendTokenAsync(
				HttpMethod.Post, "trade/order/cancelBatch", [],
				new
				{
					symbol = NormalizeSymbol(group.Key),
					orderIds = ids,
				},
				true, cancellationToken);
		}
	}

	internal static int NormalizeDepth(int depth)
	{
		foreach (var supported in new[] { 5, 10, 20, 50, 100 })
			if (depth <= supported)
				return supported;
		return 100;
	}

	internal static TData Deserialize<TData>(string body)
	{
		try
		{
			var response = JsonConvert.DeserializeObject<
				CoinstoreResponse<TData>>(
				body.ThrowIfEmpty(nameof(body)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"Coinstore returned an empty response.");
			EnsureSuccess(response.Code, response.Message);
			return response.Data;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coinstore returned malformed JSON.", error);
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
		HttpMethod method, string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			method, path, query, body, isPrivate,
			cancellationToken);
		return Deserialize<TData>(raw);
	}

	private async ValueTask<JToken> SendTokenAsync(
		HttpMethod method, string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			method, path, query, body, isPrivate,
			cancellationToken);
		try
		{
			var root = JObject.Parse(raw);
			EnsureSuccess(root["code"],
				root.Value<string>("message") ??
				root.Value<string>("msg"));
			return root["data"];
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coinstore returned malformed JSON.", error);
		}
	}

	private async ValueTask<string> SendRawAsync(
		HttpMethod method, string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body, bool isPrivate,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Coinstore API key and secret are required " +
					"for private operations.");
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var queryString = BuildQuery(query);
		var target = path +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
		var bodyText = body is null ? null : SerializeBody(body);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(
				method, new Uri(_endpoint, target));
			if (bodyText is not null)
				request.Content = new StringContent(
					bodyText, Encoding.UTF8, "application/json");
			if (isPrivate)
				AddAuthentication(
					request, queryString + (bodyText ?? string.Empty));

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

	private void AddAuthentication(
		HttpRequestMessage request, string payload)
	{
		var expires = DateTime.UtcNow.ToCoinstoreMilliseconds();
		request.Headers.TryAddWithoutValidation(
			"X-CS-APIKEY", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"X-CS-EXPIRES",
			expires.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation(
			"X-CS-SIGN",
			_authenticator.Sign(expires, payload));
		request.Headers.TryAddWithoutValidation(
			"exch-language", "en_US");
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(20);
		}
		finally
		{
			_rateSync.Release();
		}
	}

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
					value.Value, CultureInfo.InvariantCulture)))
			.Join("&");

	private static long ParseOrderId(string orderId)
		=> long.TryParse(
			orderId.ThrowIfEmpty(nameof(orderId)),
			NumberStyles.None, CultureInfo.InvariantCulture,
			out var value)
				? value
				: throw new FormatException(
					$"Invalid Coinstore order ID '{orderId}'.");

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().Contains(
			'/', StringComparison.Ordinal)
				? symbol.ToCoinstoreSymbol()
				: symbol.Trim().ToUpperInvariant();

	private static string NormalizeOptionalSymbol(string symbol)
		=> symbol.IsEmpty() ? null : NormalizeSymbol(symbol);

	private static void EnsureSuccess(JToken code, string message)
	{
		var value = code?.ToString();
		if (value.IsEmpty() || value == "0")
			return;
		throw new InvalidOperationException(
			$"Coinstore API error {value}: {message}");
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

	private static Exception CreateHttpError(
		HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<
				CoinstoreError>(body);
			if (error is not null)
				details = new[]
				{
					error.Code?.ToString(),
					error.Message,
					error.LegacyMessage,
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
			$"Coinstore HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null, statusCode);
	}
}
