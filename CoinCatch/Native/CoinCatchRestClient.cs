namespace StockSharp.CoinCatch.Native;

sealed class CoinCatchRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly CoinCatchProductTypes _productType;
	private readonly HttpClient _http = new();
	private readonly CoinCatchAuthenticator _authenticator;
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

	public CoinCatchRestClient(string endpoint,
		CoinCatchProductTypes productType, SecureString key,
		SecureString secret, SecureString passphrase)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"CoinCatch REST endpoint must be an absolute HTTP URL.",
				nameof(endpoint));
		_productType = productType;
		_authenticator = new(key, secret, passphrase);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinCatch-Connector/1.0");
	}

	public override string Name => "CoinCatch_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authenticator.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<CoinCatchSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
	{
		var json = _productType.IsFutures()
			? await SendGetRawAsync(
				"/api/mix/v1/market/contracts",
				Query(("productType", _productType.ToProductCode())),
				false, cancellationToken)
			: await SendGetRawAsync(
				"/api/spot/v1/public/products",
				[], false, cancellationToken);
		return DeserializeSymbols(json, _productType);
	}

	public async ValueTask<CoinCatchTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var prefix = _productType.IsFutures() ? "mix" : "spot";
		var json = await SendGetRawAsync(
			$"/api/{prefix}/v1/market/ticker",
			Query(("symbol", symbol.ThrowIfEmpty(nameof(symbol)))),
			false, cancellationToken);
		return Deserialize<CoinCatchTicker>(json);
	}

	public async ValueTask<CoinCatchOrderBook> GetOrderBookAsync(
		string symbol, int depth, CancellationToken cancellationToken)
	{
		var path = _productType.IsFutures()
			? "/api/mix/v1/market/depth"
			: "/api/spot/v1/market/depth";
		var query = _productType.IsFutures()
			? Query(
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("limit", depth.Max(1).Min(150).ToString(
					CultureInfo.InvariantCulture)))
			: Query(
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("type", "step0"),
				("limit", depth.Max(1).Min(150).ToString(
					CultureInfo.InvariantCulture)));
		var book = Deserialize<CoinCatchOrderBook>(
			await SendGetRawAsync(path, query, false, cancellationToken));
		book.Symbol = symbol;
		return book;
	}

	public async ValueTask<CoinCatchTrade[]> GetTradesAsync(string symbol,
		int limit, DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
	{
		var historical = from is not null || to is not null;
		var prefix = _productType.IsFutures() ? "mix" : "spot";
		var path = $"/api/{prefix}/v1/market/" +
			(historical ? "fills-history" : "fills");
		var maximum = _productType.IsFutures()
			? 100
			: historical ? 1000 : 500;
		var json = await SendGetRawAsync(path,
			Query(
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("limit", limit.Max(1).Min(maximum).ToString(
					CultureInfo.InvariantCulture)),
				("startTime", ToTimestamp(from)),
				("endTime", ToTimestamp(to))),
			false, cancellationToken);
		return Deserialize<CoinCatchTrade[]>(json) ?? [];
	}

	public async ValueTask<CoinCatchCandle[]> GetCandlesAsync(
		string symbol, string granularity, DateTime? from, DateTime? to,
		int limit, CancellationToken cancellationToken)
	{
		var isHistory = from is not null || to is not null;
		string path;
		KeyValuePair<string, string>[] query;
		if (_productType.IsFutures())
		{
			path = isHistory
				? "/api/mix/v1/market/history-candles"
				: "/api/mix/v1/market/candles";
			query = Query(
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("granularity",
					granularity.ThrowIfEmpty(nameof(granularity))),
				("startTime", ToTimestamp(from)),
				("endTime", ToTimestamp(to)),
				("limit", limit.Max(1).Min(1000).ToString(
					CultureInfo.InvariantCulture)));
		}
		else
		{
			path = isHistory
				? "/api/spot/v1/market/history-candles"
				: "/api/spot/v1/market/candles";
			query = Query(
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("period",
					granularity.ThrowIfEmpty(nameof(granularity))),
				("after", ToTimestamp(from)),
				("before", ToTimestamp(to)),
				("endTime", isHistory ? ToTimestamp(to) : null),
				("limit", limit.Max(1).Min(isHistory ? 200 : 1000)
					.ToString(CultureInfo.InvariantCulture)));
		}
		var json = await SendGetRawAsync(
			path, query, false, cancellationToken);
		return DeserializeCandles(json, _productType);
	}

	public async ValueTask<CoinCatchBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		var json = _productType.IsFutures()
			? await SendGetRawAsync(
				"/api/mix/v1/account/accounts",
				Query(("productType", _productType.ToProductCode())),
				true, cancellationToken)
			: await SendGetRawAsync(
				"/api/spot/v1/account/assets",
				[], true, cancellationToken);
		return Deserialize<CoinCatchBalance[]>(json) ?? [];
	}

	public async ValueTask<CoinCatchPosition[]> GetPositionsAsync(
		CancellationToken cancellationToken)
	{
		if (!_productType.IsFutures())
			return [];
		var json = await SendGetRawAsync(
			"/api/mix/v1/position/allPosition-v2",
			Query(("productType", _productType.ToProductCode())),
			true, cancellationToken);
		return Deserialize<CoinCatchPosition[]>(json) ?? [];
	}

	public async ValueTask<CoinCatchOrder[]> GetOpenOrdersAsync(
		string symbol, CancellationToken cancellationToken)
	{
		string json;
		if (_productType.IsFutures())
		{
			json = symbol.IsEmpty()
				? await SendGetRawAsync(
					"/api/mix/v1/order/marginCoinCurrent",
					Query(("productType",
						_productType.ToProductCode())),
					true, cancellationToken)
				: await SendGetRawAsync(
					"/api/mix/v1/order/current",
					Query(("symbol", symbol)),
					true, cancellationToken);
		}
		else
		{
			json = await SendPostRawAsync(
				"/api/spot/v1/trade/open-orders",
				new { symbol = symbol ?? string.Empty },
				cancellationToken);
		}
		return DeserializeOrders(json);
	}

	public async ValueTask<CoinCatchOrder[]> GetOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		orderId.ThrowIfEmpty(nameof(orderId));
		var json = _productType.IsFutures()
			? await SendGetRawAsync(
				"/api/mix/v1/order/detail",
				Query(
					("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
					("orderId", orderId)),
				true, cancellationToken)
			: await SendPostRawAsync(
				"/api/spot/v1/trade/orderInfo",
				new
				{
					symbol = symbol.ThrowIfEmpty(nameof(symbol)),
					orderId,
				},
				cancellationToken);
		return DeserializeOrders(json);
	}

	public async ValueTask<CoinCatchOrder[]> GetHistoryOrdersAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		string json;
		if (_productType.IsFutures())
		{
			var end = (to ?? DateTime.UtcNow).ToUtc();
			var start = (from ?? end.AddDays(-7)).ToUtc();
			json = await SendGetRawAsync(
				"/api/mix/v1/order/history",
				Query(
					("symbol", symbol),
					("startTime", ToTimestamp(start)),
					("endTime", ToTimestamp(end)),
					("pageSize", limit.Max(1).Min(100).ToString(
						CultureInfo.InvariantCulture))),
				true, cancellationToken);
		}
		else
		{
			json = await SendPostRawAsync(
				"/api/spot/v1/trade/history",
				new
				{
					symbol,
					limit = limit.Max(1).Min(500),
				},
				cancellationToken);
		}
		return DeserializeOrders(json);
	}

	public async ValueTask<CoinCatchPlaceOrderResult> PlaceOrderAsync(
		string symbol, string marginCoin, string side,
		string orderType, string timeInForce, decimal? price,
		decimal quantity, string clientOrderId, bool reduceOnly,
		CancellationToken cancellationToken)
	{
		object body;
		string path;
		if (_productType.IsFutures())
		{
			path = "/api/mix/v1/order/placeOrder";
			body = new
			{
				symbol,
				marginCoin,
				size = quantity.ToWire(),
				price = orderType.EqualsIgnoreCase("limit")
					? price?.ToWire()
					: null,
				side,
				orderType,
				timeInForceValue = timeInForce,
				clientOid = clientOrderId,
				reduceOnly,
			};
		}
		else
		{
			path = "/api/spot/v1/trade/orders";
			body = new
			{
				symbol,
				side,
				orderType,
				force = timeInForce,
				price = orderType.EqualsIgnoreCase("limit")
					? price?.ToWire()
					: null,
				quantity = quantity.ToWire(),
				clientOrderId,
			};
		}
		return Deserialize<CoinCatchPlaceOrderResult>(
			await SendPostRawAsync(path, body, cancellationToken));
	}

	public async ValueTask CancelOrderAsync(string symbol,
		string marginCoin, string orderId,
		CancellationToken cancellationToken)
	{
		var path = _productType.IsFutures()
			? "/api/mix/v1/order/cancel-order"
			: "/api/spot/v1/trade/cancel-order-v2";
		var body = _productType.IsFutures()
			? (object)new
			{
				symbol,
				marginCoin,
				orderId,
			}
			: new
			{
				symbol,
				orderId,
			};
		_ = await SendPostRawAsync(path, body, cancellationToken);
	}

	public async ValueTask CancelSymbolOrdersAsync(string symbol,
		string marginCoin, CancellationToken cancellationToken)
	{
		var path = _productType.IsFutures()
			? "/api/mix/v1/order/cancel-symbol-orders"
			: "/api/spot/v1/trade/cancel-symbol-order";
		var body = _productType.IsFutures()
			? (object)new { symbol, marginCoin }
			: new { symbol };
		_ = await SendPostRawAsync(path, body, cancellationToken);
	}

	internal static TData Deserialize<TData>(string body)
	{
		try
		{
			var response = JsonConvert.DeserializeObject<
				CoinCatchResponse<TData>>(
					body.ThrowIfEmpty(nameof(body)),
					CreateJsonSettings()) ??
				throw new InvalidDataException(
					"CoinCatch returned an empty response.");
			if (!response.IsSuccess)
				throw new InvalidDataException(
					$"CoinCatch request failed ({response.Code}): " +
						response.Message);
			return response.Data;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinCatch returned an unexpected response shape.",
				error);
		}
	}

	internal static CoinCatchSymbol[] DeserializeSymbols(string body,
		CoinCatchProductTypes productType)
	{
		var symbols = Deserialize<CoinCatchSymbol[]>(body) ?? [];
		foreach (var symbol in symbols)
		{
			if (symbol is null)
				continue;
			if (symbol.SymbolName.IsEmpty())
				symbol.SymbolName =
					symbol.Symbol.ToCoinCatchWebSocketSymbol();
			if (symbol.BaseCoin.IsEmpty() ||
				symbol.QuoteCoin.IsEmpty())
				FillCurrencies(symbol, productType);
		}
		return symbols;
	}

	internal static CoinCatchCandle[] DeserializeCandles(string body,
		CoinCatchProductTypes productType)
	{
		_ = productType;
		return Deserialize<CoinCatchCandle[]>(body) ?? [];
	}

	internal static CoinCatchOrderPage DeserializeOrderPage(string body)
	{
		var page = Deserialize<CoinCatchOrderPage>(body);
		if (page is null)
			throw new InvalidDataException(
				"CoinCatch returned no order page.");
		page.Orders ??= [];
		return page;
	}

	private static CoinCatchOrder[] DeserializeOrders(string body)
	{
		var root = JObject.Parse(body);
		var responseCode = (string)root["code"];
		if (!responseCode.EqualsIgnoreCase(
			CoinCatchResponse<JToken>.SuccessCode))
			throw new InvalidDataException(
				$"CoinCatch request failed ({responseCode}): " +
					(string)(root["msg"] ?? root["message"]));
		var data = root["data"];
		if (data is null || data.Type == JTokenType.Null)
			return [];
		var serializer = JsonSerializer.Create(CreateJsonSettings());
		if (data is JArray)
			return data.ToObject<CoinCatchOrder[]>(serializer) ?? [];
		if (data["orderList"] is JArray orders)
			return orders.ToObject<CoinCatchOrder[]>(serializer) ?? [];
		var single = data.ToObject<CoinCatchOrder>(serializer);
		return single is null ? [] : [single];
	}

	private static void FillCurrencies(CoinCatchSymbol symbol,
		CoinCatchProductTypes productType)
	{
		var compact = symbol.SymbolName.IsEmpty()
			? symbol.Symbol.ToCoinCatchWebSocketSymbol()
			: symbol.SymbolName.Trim().ToUpperInvariant();
		var quotes = productType == CoinCatchProductTypes.CoinFutures
			? new[] { "USD", "USDT", "USDC", "BTC", "ETH" }
			: new[] { "USDT", "USDC", "USD", "BTC", "ETH", "EUR" };
		foreach (var quote in quotes)
		{
			if (compact.Length <= quote.Length ||
				!compact.EndsWith(quote, StringComparison.Ordinal))
				continue;
			symbol.BaseCoin = compact[..^quote.Length];
			symbol.QuoteCoin = quote;
			return;
		}
	}

	private async ValueTask<string> SendGetRawAsync(string path,
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
				AddAuthentication(request, path, query, string.Empty);
			using var response = await _http.SendAsync(
				request, HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return body;
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<string> SendPostRawAsync(string path,
		object body, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var json = JsonConvert.SerializeObject(
			body ?? new { }, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(
			HttpMethod.Post, new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(
				json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, path, [], json);
		using var response = await _http.SendAsync(
			request, HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return responseBody;
	}

	private void AddAuthentication(HttpRequestMessage request,
		string path, IEnumerable<KeyValuePair<string, string>> query,
		string body)
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
			var now = DateTime.UtcNow.ToCoinCatchTime();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(
				ref _lastTimestamp, next, current) == current)
				return next;
		}
	}

	private static KeyValuePair<string, string>[] Query(
		params (string Name, string Value)[] values)
		=> values
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.Select(static value => new KeyValuePair<string, string>(
				value.Name, value.Value))
			.ToArray();

	private static string ToTimestamp(DateTime? value)
		=> value is DateTime timestamp
			? timestamp.ToUtc().ToCoinCatchTime().ToString(
				CultureInfo.InvariantCulture)
			: null;

	private static string BuildTarget(string path,
		IEnumerable<KeyValuePair<string, string>> query)
	{
		var queryString = CoinCatchAuthenticator.BuildQuery(query);
		return path.TrimStart('/') +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"CoinCatch API key, secret and passphrase are required " +
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
		=> new HttpRequestException(
			$"CoinCatch HTTP {(int)statusCode} ({statusCode}): " +
				(body.IsEmpty() ? "<empty response>" : body));

	private static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			DateParseHandling = DateParseHandling.None,
			NullValueHandling = NullValueHandling.Ignore,
			Culture = CultureInfo.InvariantCulture,
		};
}
