namespace StockSharp.NovaDax.Native;

sealed class NovaDaxRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly NovaDaxAuthenticator _authenticator;
	private readonly string _accountId;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public NovaDaxRestClient(
		string endpoint,
		SecureString key,
		SecureString secret,
		string accountId)
	{
		_endpoint = new Uri(
			endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_authenticator = new(key, secret);
		_accountId = accountId?.Trim();
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-NovaDAX-Connector/1.0");
	}

	public override string Name => "NOVADAX_REST";

	public bool IsCredentialsAvailable
		=> _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<NovaDaxSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<NovaDaxSymbol[]>(
			HttpMethod.Get,
			"/v1/common/symbols",
			[],
			null,
			false,
			cancellationToken);

	public ValueTask<NovaDaxTicker> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<NovaDaxTicker>(
			HttpMethod.Get,
			"/v1/market/ticker",
			Values(("symbol", NormalizeSymbol(symbol))),
			null,
			false,
			cancellationToken);

	public async ValueTask<NovaDaxOrderBook> GetOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		depth = NormalizeDepth(depth);
		var book = await SendAsync<NovaDaxOrderBook>(
			HttpMethod.Get,
			"/v1/market/depth",
			Values(("symbol", symbol), ("limit", depth)),
			null,
			false,
			cancellationToken);
		if (book is not null)
		{
			book.Pair = symbol;
			book.Limit = depth;
		}
		return book;
	}

	public async ValueTask<NovaDaxTrade[]> GetPublicTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var trades = await SendAsync<NovaDaxTrade[]>(
			HttpMethod.Get,
			"/v1/market/trades",
			Values(("symbol", symbol), ("limit", 100)),
			null,
			false,
			cancellationToken);

		foreach (var trade in trades ?? [])
			trade.Pair = symbol;
		return trades;
	}

	public async ValueTask<NovaDaxCandle[]> GetCandlesAsync(
		string symbol,
		string interval,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		_ = interval.ToNovaDaxTimeFrame();
		var candles = await SendAsync<NovaDaxCandle[]>(
			HttpMethod.Get,
			"/v1/market/kline/history",
			Values(
				("symbol", symbol),
				("unit", interval),
				("from", from.ToUtc().ToNovaDaxSeconds()),
				("to", to.ToUtc().ToNovaDaxSeconds())),
			null,
			false,
			cancellationToken);

		foreach (var candle in candles ?? [])
			candle.Pair = candle.Pair.IsEmpty(symbol);
		return candles;
	}

	public ValueTask<NovaDaxBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<NovaDaxBalance[]>(
			HttpMethod.Get,
			"/v1/account/getBalance",
			[],
			null,
			true,
			cancellationToken);

	public ValueTask<NovaDaxOrder[]> GetOpenOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<NovaDaxOrder[]>(
			HttpMethod.Get,
			"/v1/orders/list",
			Values(
				("symbol", NormalizeOptionalSymbol(symbol)),
				("status", "UNFINISHED"),
				("limit", 100)),
			null,
			true,
			cancellationToken);

	public ValueTask<NovaDaxOrder> GetOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
	{
		_ = symbol;
		return SendAsync<NovaDaxOrder>(
			HttpMethod.Get,
			"/v1/orders/get",
			Values(("id", orderId.ThrowIfEmpty(nameof(orderId)))),
			null,
			true,
			cancellationToken);
	}

	public ValueTask<NovaDaxOrder[]> GetOrdersAsync(
		string symbol,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
		=> SendAsync<NovaDaxOrder[]>(
			HttpMethod.Get,
			"/v1/orders/list",
			Values(
				("symbol", NormalizeOptionalSymbol(symbol)),
				("fromTimestamp",
					from?.ToUtc().ToNovaDaxMilliseconds()),
				("toTimestamp",
					to?.ToUtc().ToNovaDaxMilliseconds()),
				("limit", limit.Max(1).Min(100))),
			null,
			true,
			cancellationToken);

	public async ValueTask<NovaDaxPrivateTrade[]>
		GetPrivateTradesAsync(
			string symbol,
			DateTime? from,
			DateTime? to,
			int limit,
			CancellationToken cancellationToken)
	{
		var orders = await GetOrdersAsync(
			symbol, from, to, limit, cancellationToken);
		var trades = new List<NovaDaxPrivateTrade>();

		foreach (var order in (orders ?? [])
			.Where(static order =>
				order?.Id.IsEmpty() == false &&
				order.ExecutedAmount > 0))
		{
			var fills = await SendAsync<NovaDaxPrivateTrade[]>(
				HttpMethod.Get,
				"/v1/orders/fills",
				Values(
					("orderId", order.Id),
					("symbol", NormalizeOptionalSymbol(symbol)),
					("fromTimestamp",
						from?.ToUtc().ToNovaDaxMilliseconds()),
					("toTimestamp",
						to?.ToUtc().ToNovaDaxMilliseconds()),
					("limit", limit.Max(1).Min(100))),
				null,
				true,
				cancellationToken);
			trades.AddRange(fills ?? []);
			if (trades.Count >= limit)
				break;
		}

		return
		[
			.. trades
				.OrderBy(static trade => trade.CreatedTimestamp)
				.TakeLast(limit.Max(1).Min(100)),
		];
	}

	public async ValueTask<NovaDaxPlaceOrderResult> PlaceOrderAsync(
		string symbol,
		NovaDaxPlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		symbol = NormalizeSymbol(symbol);
		var body = JObject.FromObject(
			order,
			JsonSerializer.Create(_jsonSettings));
		body["symbol"] = symbol;
		var result = await SendAsync<NovaDaxOrder>(
			HttpMethod.Post,
			"/v1/orders/create",
			[],
			body,
			true,
			cancellationToken);
		return new()
		{
			OrderId = result?.Id,
			Order = result,
		};
	}

	public async ValueTask CancelOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
	{
		_ = symbol;
		var result = await SendAsync<NovaDaxCancelResult>(
			HttpMethod.Post,
			"/v1/orders/cancel",
			[],
			new
			{
				id = orderId.ThrowIfEmpty(nameof(orderId)),
			},
			true,
			cancellationToken);
		if (result?.Result != true)
			throw new InvalidOperationException(
				"NovaDAX did not accept the cancellation request.");
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty())
		{
			var openOrders = await GetOpenOrdersAsync(
				null, cancellationToken);

			foreach (var market in (openOrders ?? [])
				.Where(static order => !order.Pair.IsEmpty())
				.Select(static order => order.Pair)
				.Distinct(StringComparer.OrdinalIgnoreCase))
				await CancelAllOrdersAsync(
					market, cancellationToken);

			return;
		}

		_ = await SendAsync<JToken>(
			HttpMethod.Post,
			"/v1/orders/cancel-by-symbol",
			[],
			new
			{
				symbol = NormalizeSymbol(symbol),
			},
			true,
			cancellationToken);
	}

	internal static int NormalizeDepth(int depth)
		=> depth.Max(1).Min(50);

	internal static TData Deserialize<TData>(string body)
	{
		try
		{
			var response = JsonConvert.DeserializeObject<
				NovaDaxResponse<TData>>(
				body.ThrowIfEmpty(nameof(body)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"NovaDAX returned an empty response.");
			EnsureSuccess(response.Code, response.Message);
			return response.Data;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"NovaDAX returned malformed JSON.", error);
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
				"NovaDAX API key and secret are required " +
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
					queryString,
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
		string query,
		string body)
	{
		var timestamp = DateTime.UtcNow.ToNovaDaxMilliseconds();
		request.Headers.TryAddWithoutValidation(
			"X-Nova-Access-Key",
			_authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"X-Nova-Signature",
			_authenticator.Sign(
				method,
				path,
				query,
				body,
				timestamp));
		request.Headers.TryAddWithoutValidation(
			"X-Nova-Timestamp",
			timestamp.ToString(CultureInfo.InvariantCulture));
		if (!_accountId.IsEmpty())
			request.Headers.TryAddWithoutValidation(
				"X-Nova-Account-Id", _accountId);
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
			.OrderBy(static value =>
				value.Name, StringComparer.Ordinal)
			.Select(static value =>
				FormEncode(value.Name) + "=" +
				FormEncode(Convert.ToString(
					value.Value,
					CultureInfo.InvariantCulture)))
			.Join("&");

	private static string FormEncode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty)
			.Replace("%20", "+", StringComparison.Ordinal);

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).ToNovaDaxSymbol();

	private static string NormalizeOptionalSymbol(string symbol)
		=> symbol.IsEmpty() ? null : NormalizeSymbol(symbol);

	private static void EnsureSuccess(
		string code,
		string message)
	{
		if (code.EqualsIgnoreCase("A10000"))
			return;
		throw new InvalidOperationException(
			$"NovaDAX API error {code}: {message}");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response,
		int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(
		HttpStatusCode statusCode,
		string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<NovaDaxError>(
				body);
			if (error is not null)
				details = $"{error.Code}: {error.Message}";
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"NovaDAX HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null,
			statusCode);
	}
}
