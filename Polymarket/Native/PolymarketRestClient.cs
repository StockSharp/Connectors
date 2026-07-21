namespace StockSharp.Polymarket.Native;

sealed class PolymarketApiException : InvalidOperationException
{
	public PolymarketApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class PolymarketRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private const string _initialCursor = "MA==";
	private const string _endCursor = "LTE=";
	private readonly HttpClient _clobClient;
	private readonly HttpClient _dataClient;
	private readonly PolymarketAuthenticator _authenticator;
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

	public PolymarketRestClient(string clobEndpoint, string dataEndpoint,
		PolymarketAuthenticator authenticator)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		_clobClient = CreateClient(clobEndpoint, nameof(clobEndpoint));
		_dataClient = CreateClient(dataEndpoint, nameof(dataEndpoint));
	}

	public override string Name => "Polymarket_REST";

	public ValueTask<PolymarketVersionResponse> GetVersionAsync(
		CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketVersionResponse>(HttpMethod.Get, "version",
			false, true, cancellationToken);

	public ValueTask<long> GetTimeAsync(CancellationToken cancellationToken)
		=> SendClobAsync<long>(HttpMethod.Get, "time", false, true,
			cancellationToken);

	public async ValueTask<PolymarketMarketDefinition[]> GetMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = new List<PolymarketMarketDefinition>();
		var cursor = _initialCursor;
		for (var page = 0; page < 1000 && cursor != _endCursor; page++)
		{
			var response = await SendClobAsync<PolymarketMarketsPage>(
				HttpMethod.Get, "sampling-markets?next_cursor=" + Escape(cursor),
				false, true, cancellationToken);
			markets.AddRange(response?.Data ?? []);
			var next = response?.NextCursor;
			if (next.IsEmpty() || next == cursor || (response.Data?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. markets];
	}

	public ValueTask<PolymarketOrderBook> GetBookAsync(string tokenId,
		CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketOrderBook>(HttpMethod.Get,
			"book?token_id=" + Escape(tokenId.ThrowIfEmpty(nameof(tokenId))),
			false, true, cancellationToken);

	public ValueTask<PolymarketLastTradePrice> GetLastTradePriceAsync(
		string tokenId, CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketLastTradePrice>(HttpMethod.Get,
			"last-trade-price?token_id=" + Escape(tokenId.ThrowIfEmpty(
				nameof(tokenId))), false, true, cancellationToken);

	public ValueTask<PolymarketPriceHistoryResponse> GetPriceHistoryAsync(
		string tokenId, DateTime from, DateTime to, int fidelity,
		CancellationToken cancellationToken)
	{
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));
		if (fidelity is < 1 or > 1440)
			throw new ArgumentOutOfRangeException(nameof(fidelity));
		var path = "prices-history?market=" + Escape(tokenId.ThrowIfEmpty(
			nameof(tokenId))) + "&startTs=" + from.ToPolymarketSeconds().ToString(
			CultureInfo.InvariantCulture) + "&endTs=" +
			to.ToPolymarketSeconds().ToString(CultureInfo.InvariantCulture) +
			"&fidelity=" + fidelity.ToString(CultureInfo.InvariantCulture);
		return SendClobAsync<PolymarketPriceHistoryResponse>(HttpMethod.Get,
			path, false, true, cancellationToken);
	}

	public async ValueTask<PolymarketOpenOrder[]> GetOpenOrdersAsync(int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var orders = new List<PolymarketOpenOrder>();
		var cursor = _initialCursor;
		while (cursor != _endCursor && orders.Count < limit)
		{
			var page = await SendClobAsync<PolymarketOpenOrdersPage>(
				HttpMethod.Get, "data/orders?next_cursor=" + Escape(cursor), true,
				true, cancellationToken);
			orders.AddRange(page?.Data ?? []);
			var next = page?.NextCursor;
			if (next.IsEmpty() || next == cursor || (page.Data?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. orders.Take(limit)];
	}

	public async ValueTask<PolymarketTrade[]> GetTradesAsync(int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var trades = new List<PolymarketTrade>();
		var cursor = _initialCursor;
		while (cursor != _endCursor && trades.Count < limit)
		{
			var page = await SendClobAsync<PolymarketTradesPage>(HttpMethod.Get,
				"data/trades?next_cursor=" + Escape(cursor), true, true,
				cancellationToken);
			trades.AddRange(page?.Data ?? []);
			var next = page?.NextCursor;
			if (next.IsEmpty() || next == cursor || (page.Data?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. trades.Take(limit)];
	}

	public ValueTask<PolymarketBalance> GetBalanceAsync(
		PolymarketSignatureTypes signatureType,
		CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketBalance>(HttpMethod.Get,
			"balance-allowance?asset_type=COLLATERAL&signature_type=" +
			((int)signatureType).ToString(CultureInfo.InvariantCulture), true,
			true, cancellationToken);

	public async ValueTask<PolymarketPosition[]> GetPositionsAsync(string user,
		int limit, CancellationToken cancellationToken)
	{
		user = user.NormalizeAddress(nameof(user));
		ValidateLimit(limit);
		const int pageSize = 500;
		var positions = new List<PolymarketPosition>();
		for (var offset = 0; positions.Count < limit; offset += pageSize)
		{
			var take = Math.Min(pageSize, limit - positions.Count);
			var page = await SendDataAsync<PolymarketPosition[]>(HttpMethod.Get,
				"positions?user=" + Escape(user) + "&limit=" +
				take.ToString(CultureInfo.InvariantCulture) + "&offset=" +
				offset.ToString(CultureInfo.InvariantCulture), true,
				cancellationToken) ?? [];
			positions.AddRange(page);
			if (page.Length < take)
				break;
		}
		return [.. positions];
	}

	public ValueTask<PolymarketOrderResponse> CreateOrderAsync(
		PolymarketOrderRequest request, CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketOrderResponse, PolymarketOrderRequest>(
			HttpMethod.Post, "order", request ?? throw new ArgumentNullException(
				nameof(request)), true, false, cancellationToken);

	public ValueTask<PolymarketCancelResponse> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketCancelResponse, PolymarketCancelOrderRequest>(
			HttpMethod.Delete, "order", new()
			{
				OrderId = orderId.NormalizeOrderId(),
			}, true, false, cancellationToken);

	public ValueTask<PolymarketCancelResponse> CancelAllAsync(
		CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketCancelResponse>(HttpMethod.Delete,
			"cancel-all", true, false, cancellationToken);

	public ValueTask<PolymarketCancelResponse> CancelMarketAsync(
		string conditionId, string tokenId, CancellationToken cancellationToken)
		=> SendClobAsync<PolymarketCancelResponse, PolymarketCancelMarketRequest>(
			HttpMethod.Delete, "cancel-market-orders", new()
			{
				Market = conditionId,
				AssetId = tokenId,
			}, true, false, cancellationToken);

	private ValueTask<TResponse> SendClobAsync<TResponse>(HttpMethod method,
		string path, bool isPrivate, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(_clobClient, method, path, string.Empty,
			isPrivate, isRetryAllowed, cancellationToken);

	private ValueTask<TResponse> SendClobAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest body, bool isPrivate,
		bool isRetryAllowed, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(_clobClient, method, path,
			JsonConvert.SerializeObject(body, _settings), isPrivate,
			isRetryAllowed, cancellationToken);

	private ValueTask<TResponse> SendDataAsync<TResponse>(HttpMethod method,
		string path, bool isRetryAllowed, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(_dataClient, method, path, string.Empty, false,
			isRetryAllowed, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpClient client,
		HttpMethod method, string path, string body, bool isPrivate,
		bool isRetryAllowed, CancellationToken cancellationToken)
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
			{
				var separator = path.IndexOf('?');
				_authenticator.AddRestHeaders(request, "/" +
					(separator < 0 ? path : path[..separator]), body);
			}
			using var response = await client.SendAsync(request,
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
			if (typeof(TResponse) == typeof(string))
				return (TResponse)(object)payload;
			if (payload.IsEmpty())
				return default;
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(payload, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Polymarket returned malformed JSON for " + method + " /" +
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
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
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
				"Polymarket response exceeds the maximum supported size.");
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (Encoding.UTF8.GetByteCount(payload) > _maximumResponseLength)
			throw new InvalidDataException(
				"Polymarket response exceeds the maximum supported size.");
		return payload;
	}

	private static Exception CreateException(HttpStatusCode statusCode,
		string payload)
	{
		PolymarketApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<PolymarketApiError>(payload);
		}
		catch (JsonException)
		{
		}
		var message = error?.ErrorMessage ?? error?.Error ?? error?.Message;
		if (message.IsEmpty())
			message = payload.IsEmpty()
				? $"Polymarket returned HTTP {(int)statusCode} ({statusCode})."
				: payload;
		return new PolymarketApiException(statusCode, message);
	}

	private static HttpClient CreateClient(string endpoint, string name)
	{
		var client = new HttpClient
		{
			BaseAddress = new(endpoint.NormalizeHttpEndpoint(name),
				UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Polymarket/1.0");
		client.DefaultRequestHeaders.Accept.Add(new(
			"application/json"));
		return client;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 10000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Polymarket history limit must be between one and 10000.");
	}

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		_clobClient.Dispose();
		_dataClient.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
