namespace StockSharp.HashKey.Native;

sealed class HashKeyRestClient : BaseLogReceiver
{
	private enum RateBuckets
	{
		Query,
		Order,
	}

	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _queryRateSync = new(1, 1);
	private readonly SemaphoreSlim _orderRateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextQueryTime;
	private DateTime _nextOrderTime;
	private long _clockOffsetMilliseconds;

	public HashKeyRestClient(string endpoint, SecureString key, SecureString secret)
	{
		var value = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out _endpoint) ||
			!_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException("HashKey endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));

		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"HashKey API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);

		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-HashKey-Connector/1.0");
	}

	public override string Name => "HashKey_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _secret is not null;

	protected override void DisposeManaged()
	{
		_queryRateSync.Dispose();
		_orderRateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<HashKeyServerTime> GetTimeAsync(
		CancellationToken cancellationToken)
	{
		var result = await SendAsync<HashKeyServerTime>(HttpMethod.Get,
			"api/v1/time", null, false, RateBuckets.Query, true, cancellationToken);
		if (result.ServerTime > 0)
			Interlocked.Exchange(ref _clockOffsetMilliseconds,
				result.ServerTime - DateTime.UtcNow.ToMilliseconds());
		return result;
	}

	public ValueTask<HashKeyExchangeInfo> GetExchangeInfoAsync(
		HashKeySymbolQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		return SendAsync<HashKeyExchangeInfo>(HttpMethod.Get, "api/v1/exchangeInfo",
			query, false, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyOrderBook> GetDepthAsync(HashKeyDepthQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)));
		Append(query, "limit", request.Limit);
		return SendAsync<HashKeyOrderBook>(HttpMethod.Get, "quote/v1/depth", query,
			false, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyPublicTrade[]> GetPublicTradesAsync(
		HashKeyPublicTradesQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)));
		Append(query, "limit", request.Limit);
		return SendAsync<HashKeyPublicTrade[]>(HttpMethod.Get, "quote/v1/trades", query,
			false, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyCandle[]> GetCandlesAsync(HashKeyCandlesQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)));
		Append(query, "interval", request.Interval.ThrowIfEmpty(nameof(request.Interval)));
		Append(query, "limit", request.Limit);
		Append(query, "startTime", request.StartTime);
		Append(query, "endTime", request.EndTime);
		return SendAsync<HashKeyCandle[]>(HttpMethod.Get, "quote/v1/klines", query,
			false, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyTicker[]> GetTickersAsync(HashKeyTickerQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		Append(query, "instType", request.InstrumentType.ToWire());
		return SendAsync<HashKeyTicker[]>(HttpMethod.Get, "quote/v1/ticker/24hr",
			query, false, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyBookTicker[]> GetBookTickersAsync(
		HashKeySymbolQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		return SendAsync<HashKeyBookTicker[]>(HttpMethod.Get,
			"quote/v1/ticker/bookTicker", query, false, RateBuckets.Query, true,
			cancellationToken);
	}

	public ValueTask<HashKeyMarkPrice> GetMarkPriceAsync(HashKeySymbolQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)));
		return SendAsync<HashKeyMarkPrice>(HttpMethod.Get, "quote/v1/markPrice", query,
			false, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyListenKeyResponse> CreateListenKeyAsync(
		CancellationToken cancellationToken)
		=> SendAsync<HashKeyListenKeyResponse>(HttpMethod.Post,
			"api/v1/userDataStream", null, true, RateBuckets.Order, false,
			cancellationToken);

	public ValueTask<HashKeyEmptyResponse> KeepListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "listenKey", listenKey.ThrowIfEmpty(nameof(listenKey)));
		return SendAsync<HashKeyEmptyResponse>(HttpMethod.Put, "api/v1/userDataStream",
			query, true, RateBuckets.Order, false, cancellationToken);
	}

	public ValueTask<HashKeyEmptyResponse> DeleteListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "listenKey", listenKey.ThrowIfEmpty(nameof(listenKey)));
		return SendAsync<HashKeyEmptyResponse>(HttpMethod.Delete,
			"api/v1/userDataStream", query, true, RateBuckets.Order, false,
			cancellationToken);
	}

	public ValueTask<HashKeyAccount> GetSpotAccountAsync(
		CancellationToken cancellationToken)
		=> SendAsync<HashKeyAccount>(HttpMethod.Get, "api/v1/account", null, true,
			RateBuckets.Query, true, cancellationToken);

	public ValueTask<HashKeyFuturesBalance[]> GetFuturesBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<HashKeyFuturesBalance[]>(HttpMethod.Get, "api/v1/futures/balance",
			null, true, RateBuckets.Query, true, cancellationToken);

	public ValueTask<HashKeyFuturesPosition[]> GetFuturesPositionsAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "symbol", symbol);
		return SendAsync<HashKeyFuturesPosition[]>(HttpMethod.Get,
			"api/v1/futures/positions", query, true, RateBuckets.Query, true,
			cancellationToken);
	}

	public ValueTask<HashKeySpotOrder> CreateSpotOrderAsync(
		HashKeySpotCreateOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)));
		Append(query, "side", request.Side.ToWire());
		Append(query, "type", request.Type.ToWire());
		Append(query, "quantity", request.Quantity);
		Append(query, "amount", request.Amount);
		Append(query, "price", request.Price);
		Append(query, "newClientOrderId", request.ClientOrderId);
		Append(query, "timeInForce", request.TimeInForce?.ToWire());
		Append(query, "stpMode", request.SelfTradePreventionMode.ToWire());
		return SendAsync<HashKeySpotOrder>(HttpMethod.Post, "api/v1.1/spot/order",
			query, true, RateBuckets.Order, false, cancellationToken);
	}

	public ValueTask<HashKeyFuturesOrder> CreateFuturesOrderAsync(
		HashKeyFuturesCreateOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol.ThrowIfEmpty(nameof(request.Symbol)));
		Append(query, "side", request.Side.ToWire());
		Append(query, "type", request.Type.ToWire());
		Append(query, "quantity", request.Quantity);
		Append(query, "price", request.Price);
		Append(query, "priceType", request.PriceType.ToWire());
		Append(query, "stopPrice", request.StopPrice);
		Append(query, "timeInForce", request.TimeInForce?.ToWire());
		Append(query, "clientOrderId", request.ClientOrderId);
		Append(query, "stpMode", request.SelfTradePreventionMode.ToWire());
		return SendAsync<HashKeyFuturesOrder>(HttpMethod.Post,
			"api/v1/futures/order", query, true, RateBuckets.Order, false,
			cancellationToken);
	}

	public ValueTask<HashKeySpotOrder> GetSpotOrderAsync(HashKeyOrderQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = CreateOrderQuery(request, false, false, false);
		return SendAsync<HashKeySpotOrder>(HttpMethod.Get, "api/v1/spot/order", query,
			true, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeySpotOrder[]> GetSpotOpenOrdersAsync(HashKeyOrderQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = CreateOrderQuery(request, true, false, false);
		return SendAsync<HashKeySpotOrder[]>(HttpMethod.Get, "api/v1/spot/openOrders",
			query, true, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeySpotOrder[]> GetSpotHistoryOrdersAsync(
		HashKeyOrderQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = CreateOrderQuery(request, true, true, false);
		return SendAsync<HashKeySpotOrder[]>(HttpMethod.Get, "api/v1/spot/tradeOrders",
			query, true, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyFuturesOrder> GetFuturesOrderAsync(
		HashKeyOrderQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = CreateOrderQuery(request, false, false, true);
		return SendAsync<HashKeyFuturesOrder>(HttpMethod.Get, "api/v1/futures/order",
			query, true, RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyFuturesOrder[]> GetFuturesOpenOrdersAsync(
		HashKeyOrderQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = CreateOrderQuery(request, true, false, true);
		return SendAsync<HashKeyFuturesOrder[]>(HttpMethod.Get,
			"api/v1/futures/openOrders", query, true, RateBuckets.Query, true,
			cancellationToken);
	}

	public ValueTask<HashKeyFuturesOrder[]> GetFuturesHistoryOrdersAsync(
		HashKeyOrderQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = CreateOrderQuery(request, true, true, true);
		return SendAsync<HashKeyFuturesOrder[]>(HttpMethod.Get,
			"api/v1/futures/historyOrders", query, true, RateBuckets.Query, true,
			cancellationToken);
	}

	public ValueTask<HashKeySpotAccountTrade[]> GetSpotAccountTradesAsync(
		HashKeyTradeQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<HashKeySpotAccountTrade[]>(HttpMethod.Get,
			"api/v1/account/trades", CreateTradeQuery(request), true,
			RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeyFuturesTrade[]> GetFuturesTradesAsync(
		HashKeyTradeQuery request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<HashKeyFuturesTrade[]>(HttpMethod.Get,
			"api/v1/futures/userTrades", CreateTradeQuery(request), true,
			RateBuckets.Query, true, cancellationToken);
	}

	public ValueTask<HashKeySpotOrder> CancelSpotOrderAsync(
		HashKeyCancelOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<HashKeySpotOrder>(HttpMethod.Delete, "api/v1/spot/order",
			CreateCancelQuery(request), true, RateBuckets.Order, false,
			cancellationToken);
	}

	public ValueTask<HashKeyFuturesOrder> CancelFuturesOrderAsync(
		HashKeyCancelOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<HashKeyFuturesOrder>(HttpMethod.Delete,
			"api/v1/futures/order", CreateCancelQuery(request), true,
			RateBuckets.Order, false, cancellationToken);
	}

	public ValueTask<HashKeyOperationResponse> CancelAllAsync(HashKeySections section,
		HashKeyCancelAllRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		Append(query, "side", request.Side?.ToWire());
		Append(query, "fromOrderId", request.FromOrderId);
		Append(query, "limit", request.Limit);
		var path = section == HashKeySections.Spot
			? "api/v1/spot/cancelAllOpenOrders"
			: "api/v1/futures/cancelAllOpenOrders";
		return SendAsync<HashKeyOperationResponse>(HttpMethod.Delete, path, query, true,
			RateBuckets.Order, false, cancellationToken);
	}

	private static StringBuilder CreateOrderQuery(HashKeyOrderQuery request,
		bool includeListFields, bool includeHistoryFields, bool isFutures)
	{
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		Append(query, "orderId", request.OrderId);
		Append(query, isFutures ? "clientOrderId" : "origClientOrderId",
			request.ClientOrderId);
		Append(query, "type", request.Type?.ToWire());
		if (includeListFields)
		{
			Append(query, "fromOrderId", request.FromOrderId);
			Append(query, "side", request.Side?.ToWire());
			Append(query, "limit", request.Limit);
		}
		if (includeHistoryFields)
		{
			Append(query, "startTime", request.StartTime);
			Append(query, "endTime", request.EndTime);
		}
		return query;
	}

	private static StringBuilder CreateTradeQuery(HashKeyTradeQuery request)
	{
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		Append(query, "clientOrderId", request.ClientOrderId);
		Append(query, "fromId", request.FromId);
		Append(query, "toId", request.ToId);
		Append(query, "startTime", request.StartTime);
		Append(query, "endTime", request.EndTime);
		Append(query, "limit", request.Limit);
		return query;
	}

	private static StringBuilder CreateCancelQuery(HashKeyCancelOrderRequest request)
	{
		var query = new StringBuilder();
		Append(query, "symbol", request.Symbol);
		Append(query, "orderId", request.OrderId);
		Append(query, "clientOrderId", request.ClientOrderId);
		Append(query, "type", request.Type?.ToWire());
		return query;
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, StringBuilder query, bool isSigned, RateBuckets bucket,
		bool isRetrySafe, CancellationToken cancellationToken)
	{
		if (isSigned)
			EnsureCredentials();
		var baseQuery = query?.ToString();
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(bucket, cancellationToken);
			var finalQuery = BuildQuery(baseQuery, isSigned);
			using var request = new HttpRequestMessage(method,
				CreateUri(path, finalQuery));
			if (isSigned)
				request.Headers.TryAddWithoutValidation("X-HK-APIKEY", _apiKey);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private string BuildQuery(string baseQuery, bool isSigned)
	{
		var query = new StringBuilder(baseQuery ?? string.Empty);
		if (!isSigned)
			return query.ToString();
		Append(query, "recvWindow", 5000);
		var timestamp = DateTime.UtcNow.ToMilliseconds() +
			Interlocked.Read(ref _clockOffsetMilliseconds);
		Append(query, "timestamp", timestamp);
		var payload = query.ToString();
		using var hmac = new HMACSHA256(_secret);
		var signature = Convert.ToHexString(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
		Append(query, "signature", signature);
		return query.ToString();
	}

	private async ValueTask WaitRateLimitAsync(RateBuckets bucket,
		CancellationToken cancellationToken)
	{
		var sync = bucket == RateBuckets.Query ? _queryRateSync : _orderRateSync;
		await sync.WaitAsync(cancellationToken);
		try
		{
			var next = bucket == RateBuckets.Query ? _nextQueryTime : _nextOrderTime;
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			var nextTime = DateTime.UtcNow + (bucket == RateBuckets.Query
				? TimeSpan.FromMilliseconds(205)
				: TimeSpan.FromMilliseconds(55));
			if (bucket == RateBuckets.Query)
				_nextQueryTime = nextTime;
			else
				_nextOrderTime = nextTime;
		}
		finally
		{
			sync.Release();
		}
	}

	private Uri CreateUri(string path, string query)
	{
		var value = path.TrimStart('/');
		if (!query.IsEmpty())
			value += "?" + query;
		return new(_endpoint, value);
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("HashKey returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException("HashKey returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"HashKey returned an unexpected response shape.", error);
		}
	}

	private static void Append(StringBuilder query, string name, string value)
	{
		if (value.IsEmpty())
			return;
		if (query.Length > 0)
			query.Append('&');
		query.Append(name).Append('=').Append(Uri.EscapeDataString(value));
	}

	private static void Append(StringBuilder query, string name, decimal? value)
	{
		if (value is decimal actual)
			Append(query, name, actual.ToString(CultureInfo.InvariantCulture));
	}

	private static void Append(StringBuilder query, string name, long? value)
	{
		if (value is long actual)
			Append(query, name, actual.ToString(CultureInfo.InvariantCulture));
	}

	private static void Append(StringBuilder query, string name, int value)
	{
		if (value > 0)
			Append(query, name, value.ToString(CultureInfo.InvariantCulture));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			(response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
			(response.StatusCode == (HttpStatusCode)429
				? TimeSpan.FromMinutes(1)
				: TimeSpan.FromMilliseconds(500 * (1 << attempt)));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<HashKeyErrorResponse>(
				body ?? string.Empty, _jsonSettings);
			details = new[] { error?.Code, error?.Message }
				.Where(static value => !value.IsEmpty()).Join(": ").IsEmpty(details);
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"HashKey HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"HashKey API key and secret are required for private operations.");
	}
}
