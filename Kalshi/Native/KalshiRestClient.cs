namespace StockSharp.Kalshi.Native;

sealed class KalshiApiException : InvalidOperationException
{
	public KalshiApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class KalshiRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly string _apiRootPath;
	private readonly KalshiAuthenticator _authenticator;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public KalshiRestClient(string endpoint, KalshiAuthenticator authenticator)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		var normalized = endpoint.NormalizeHttpEndpoint(nameof(endpoint));
		var address = new Uri(normalized, UriKind.Absolute);
		_apiRootPath = address.AbsolutePath.TrimEnd('/');
		_client = new()
		{
			BaseAddress = address,
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Kalshi/1.0");
		_client.DefaultRequestHeaders.Accept.Add(new("application/json"));
	}

	public override string Name => "Kalshi_REST";

	public async ValueTask<KalshiMarket[]> GetMarketsAsync(int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit, 50000, "security lookup");
		var markets = new List<KalshiMarket>();
		string cursor = null;
		while (markets.Count < limit)
		{
			var take = Math.Min(1000, limit - markets.Count);
			var path = "markets?status=open&limit=" + take.ToString(
				CultureInfo.InvariantCulture);
			if (!cursor.IsEmpty())
				path += "&cursor=" + Escape(cursor);
			var page = await SendAsync<KalshiMarketsPage>(HttpMethod.Get, path,
				false, true, cancellationToken);
			markets.AddRange(page?.Markets ?? []);
			var next = page?.Cursor;
			if (next.IsEmpty() || next == cursor || (page.Markets?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. markets.Take(limit)];
	}

	public async ValueTask<KalshiMarket> GetMarketAsync(string ticker,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync<KalshiMarketResponse>(HttpMethod.Get,
			"markets/" + Escape(ticker.ThrowIfEmpty(nameof(ticker))), false, true,
			cancellationToken);
		return response?.Market ?? throw new InvalidDataException(
			"Kalshi returned an empty market response.");
	}

	public async ValueTask<KalshiOrderBook> GetOrderBookAsync(string ticker,
		int depth, CancellationToken cancellationToken)
	{
		if (depth is < 0 or > 100)
			throw new ArgumentOutOfRangeException(nameof(depth));
		var response = await SendAsync<KalshiOrderBookResponse>(HttpMethod.Get,
			"markets/" + Escape(ticker.ThrowIfEmpty(nameof(ticker))) +
			"/orderbook?depth=" + depth.ToString(CultureInfo.InvariantCulture),
			false, true, cancellationToken);
		return response?.OrderBook ?? new KalshiOrderBook
		{
			Yes = [],
			No = [],
		};
	}

	public async ValueTask<KalshiTrade[]> GetTradesAsync(string ticker,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit, 10000, "trade history");
		var trades = new List<KalshiTrade>();
		string cursor = null;
		while (trades.Count < limit)
		{
			var take = Math.Min(1000, limit - trades.Count);
			var path = "markets/trades?limit=" + take.ToString(
				CultureInfo.InvariantCulture);
			if (!ticker.IsEmpty())
				path += "&ticker=" + Escape(ticker);
			if (from is DateTime start)
				path += "&min_ts=" + start.ToKalshiSeconds().ToString(
					CultureInfo.InvariantCulture);
			if (to is DateTime end)
				path += "&max_ts=" + end.ToKalshiSeconds().ToString(
					CultureInfo.InvariantCulture);
			if (!cursor.IsEmpty())
				path += "&cursor=" + Escape(cursor);
			var page = await SendAsync<KalshiTradesPage>(HttpMethod.Get, path,
				false, true, cancellationToken);
			trades.AddRange(page?.Trades ?? []);
			var next = page?.Cursor;
			if (next.IsEmpty() || next == cursor || (page.Trades?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. trades.Take(limit)];
	}

	public async ValueTask<KalshiCandlestick[]> GetCandlesticksAsync(
		string ticker, DateTime from, DateTime to, int periodMinutes,
		CancellationToken cancellationToken)
	{
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));
		if (periodMinutes is < 1 or > 1440)
			throw new ArgumentOutOfRangeException(nameof(periodMinutes));
		var path = "markets/candlesticks?market_tickers=" +
			Escape(ticker.ThrowIfEmpty(nameof(ticker))) + "&start_ts=" +
			from.ToKalshiSeconds().ToString(CultureInfo.InvariantCulture) +
			"&end_ts=" + to.ToKalshiSeconds().ToString(
				CultureInfo.InvariantCulture) + "&period_interval=" +
			periodMinutes.ToString(CultureInfo.InvariantCulture);
		var response = await SendAsync<KalshiCandlesticksResponse>(HttpMethod.Get,
			path, false, true, cancellationToken);
		return response?.Markets?.FirstOrDefault(market => market.Ticker.Equals(
			ticker, StringComparison.OrdinalIgnoreCase))?.Candlesticks ?? [];
	}

	public ValueTask<KalshiBalance> GetBalanceAsync(int subaccount,
		CancellationToken cancellationToken)
		=> SendAsync<KalshiBalance>(HttpMethod.Get,
			"portfolio/balance?subaccount=" + ValidateSubaccount(subaccount), true,
			true, cancellationToken);

	public async ValueTask<KalshiPosition[]> GetPositionsAsync(int subaccount,
		int limit, CancellationToken cancellationToken)
	{
		ValidateLimit(limit, 10000, "position history");
		var positions = new List<KalshiPosition>();
		string cursor = null;
		while (positions.Count < limit)
		{
			var take = Math.Min(1000, limit - positions.Count);
			var path = "portfolio/positions?subaccount=" +
				ValidateSubaccount(subaccount) + "&limit=" + take.ToString(
					CultureInfo.InvariantCulture) + "&count_filter=position";
			if (!cursor.IsEmpty())
				path += "&cursor=" + Escape(cursor);
			var page = await SendAsync<KalshiPositionsPage>(HttpMethod.Get, path,
				true, true, cancellationToken);
			positions.AddRange(page?.Positions ?? []);
			var next = page?.Cursor;
			if (next.IsEmpty() || next == cursor ||
				(page.Positions?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. positions.Take(limit)];
	}

	public async ValueTask<KalshiOrder[]> GetOrdersAsync(int subaccount,
		int limit, CancellationToken cancellationToken)
	{
		ValidateLimit(limit, 10000, "order history");
		var orders = new List<KalshiOrder>();
		string cursor = null;
		while (orders.Count < limit)
		{
			var take = Math.Min(1000, limit - orders.Count);
			var path = "portfolio/orders?subaccount=" +
				ValidateSubaccount(subaccount) + "&limit=" + take.ToString(
					CultureInfo.InvariantCulture);
			if (!cursor.IsEmpty())
				path += "&cursor=" + Escape(cursor);
			var page = await SendAsync<KalshiOrdersPage>(HttpMethod.Get, path,
				true, true, cancellationToken);
			orders.AddRange(page?.Orders ?? []);
			var next = page?.Cursor;
			if (next.IsEmpty() || next == cursor || (page.Orders?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. orders.Take(limit)];
	}

	public async ValueTask<KalshiFill[]> GetFillsAsync(int subaccount,
		int limit, CancellationToken cancellationToken)
	{
		ValidateLimit(limit, 10000, "fill history");
		var fills = new List<KalshiFill>();
		string cursor = null;
		while (fills.Count < limit)
		{
			var take = Math.Min(1000, limit - fills.Count);
			var path = "portfolio/fills?subaccount=" +
				ValidateSubaccount(subaccount) + "&limit=" + take.ToString(
					CultureInfo.InvariantCulture);
			if (!cursor.IsEmpty())
				path += "&cursor=" + Escape(cursor);
			var page = await SendAsync<KalshiFillsPage>(HttpMethod.Get, path,
				true, true, cancellationToken);
			fills.AddRange(page?.Fills ?? []);
			var next = page?.Cursor;
			if (next.IsEmpty() || next == cursor || (page.Fills?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. fills.Take(limit)];
	}

	public async ValueTask<KalshiOrder> GetOrderAsync(string orderId,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync<KalshiOrderResponse>(HttpMethod.Get,
			"portfolio/orders/" + Escape(orderId.ThrowIfEmpty(nameof(orderId))),
			true, true, cancellationToken);
		return response?.Order ?? throw new InvalidDataException(
			"Kalshi returned an empty order response.");
	}

	public ValueTask<KalshiCreateOrderResponse> CreateOrderAsync(
		KalshiCreateOrderRequest request, CancellationToken cancellationToken)
		=> SendAsync<KalshiCreateOrderResponse, KalshiCreateOrderRequest>(
			HttpMethod.Post, "portfolio/events/orders", request ?? throw new
			ArgumentNullException(nameof(request)), true, false, cancellationToken);

	public ValueTask<KalshiAmendOrderResponse> AmendOrderAsync(string orderId,
		int subaccount, KalshiAmendOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<KalshiAmendOrderResponse, KalshiAmendOrderRequest>(
			HttpMethod.Post, "portfolio/events/orders/" +
			Escape(orderId.ThrowIfEmpty(nameof(orderId))) +
			"/amend?subaccount=" + ValidateSubaccount(subaccount),
			request ?? throw new ArgumentNullException(nameof(request)), true, false,
			cancellationToken);

	public ValueTask<KalshiCancelOrderResponse> CancelOrderAsync(string orderId,
		int subaccount, CancellationToken cancellationToken)
		=> SendAsync<KalshiCancelOrderResponse>(HttpMethod.Delete,
			"portfolio/events/orders/" +
			Escape(orderId.ThrowIfEmpty(nameof(orderId))) +
			"?subaccount=" + ValidateSubaccount(subaccount), true, false,
			cancellationToken);

	private ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, bool isPrivate, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(method, path, string.Empty, isPrivate,
			isRetryAllowed, cancellationToken);

	private ValueTask<TResponse> SendAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest body, bool isPrivate,
		bool isRetryAllowed, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(method, path,
			JsonConvert.SerializeObject(body, _settings), isPrivate,
			isRetryAllowed, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, string body, bool isPrivate, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (!body.IsEmpty())
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			if (isPrivate)
				_authenticator.AddRestHeaders(request, _apiRootPath + "/" + path);
			using var response = await _client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (isRetryAllowed && attempt < 2 &&
				(response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			var payload = await ReadPayloadAsync(response, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateException(response.StatusCode, payload);
			if (payload.IsEmpty())
				return default;
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(payload, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Kalshi returned malformed JSON for " + method + " /" +
					path + ".", error);
			}
		}
	}

	private async ValueTask WaitAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async ValueTask<string> ReadPayloadAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"Kalshi response exceeds the maximum supported size.");
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (Encoding.UTF8.GetByteCount(payload) > _maximumResponseLength)
			throw new InvalidDataException(
				"Kalshi response exceeds the maximum supported size.");
		return payload;
	}

	private static Exception CreateException(HttpStatusCode statusCode,
		string payload)
	{
		KalshiApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<KalshiApiError>(payload);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message;
		if (error?.Details.IsEmpty() == false)
			message = message.IsEmpty() ? error.Details : message + ": " +
				error.Details;
		if (message.IsEmpty())
			message = payload.IsEmpty()
				? $"Kalshi returned HTTP {(int)statusCode} ({statusCode})."
				: payload;
		return new KalshiApiException(statusCode, message);
	}

	private static string ValidateSubaccount(int value)
	{
		if (value is < 0 or > 63)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi subaccount must be between zero and 63.");
		return value.ToString(CultureInfo.InvariantCulture);
	}

	private static void ValidateLimit(int value, int maximum, string name)
	{
		if (value is < 1 || value > maximum)
			throw new ArgumentOutOfRangeException(name, value,
				$"Kalshi {name} limit must be between one and {maximum}.");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
