namespace StockSharp.OrderlyNetwork.Native;

sealed class OrderlyNetworkRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly OrderlyNetworkSigner _signer;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly Lock _sync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private DateTime _serverTime;

	public OrderlyNetworkRestClient(string endpoint,
		OrderlyNetworkSigner signer)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') +
			"/", UriKind.Absolute);
		_signer = signer ?? throw new ArgumentNullException(nameof(signer));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-OrderlyNetwork-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
	}

	public override string Name => "OrderlyNetwork_REST";

	public bool IsCredentialsAvailable => _signer.IsSigningAvailable;

	public DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	public async ValueTask<OrderlyNetworkSymbolInfo[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<OrderlyNetworkRows<OrderlyNetworkSymbolInfo>,
			OrderlyNetworkEmptyRequest>(HttpMethod.Get, "/v1/public/info", null,
			false, true, cancellationToken))?.Rows ?? [];

	public async ValueTask<OrderlyNetworkFuture[]> GetFuturesAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<OrderlyNetworkRows<OrderlyNetworkFuture>,
			OrderlyNetworkEmptyRequest>(HttpMethod.Get, "/v1/public/futures", null,
			false, true, cancellationToken))?.Rows ?? [];

	public async ValueTask<OrderlyNetworkMarketTrade[]> GetPublicTradesAsync(
		string symbol, int limit, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Orderly public trade limit must be between 1 and 1000.");
		var path = "/v1/public/market_trades?symbol=" + Escape(symbol) +
			"&limit=" + limit.ToString(CultureInfo.InvariantCulture);
		return (await SendAsync<OrderlyNetworkRows<OrderlyNetworkMarketTrade>,
			OrderlyNetworkEmptyRequest>(HttpMethod.Get, path, null, false, true,
			cancellationToken))?.Rows ?? [];
	}

	public ValueTask<OrderlyNetworkOrderbook> GetOrderbookAsync(string symbol,
		int depth, CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(depth), depth,
				"Orderly order-book depth must be between 1 and 500.");
		return SendAsync<OrderlyNetworkOrderbook, OrderlyNetworkOrderbookQuery>(
			HttpMethod.Post, "/v1/public/query", new()
			{
				Symbol = symbol.ThrowIfEmpty(nameof(symbol)),
				MaximumLevel = depth,
			}, false, true, cancellationToken);
	}

	public ValueTask<OrderlyNetworkCandles> GetCandlesAsync(string symbol,
		string interval, DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 5000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Orderly candle limit must be between 1 and 5000.");
		from = from.EnsureOrderlyUtc();
		to = to.EnsureOrderlyUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"Orderly candle start time cannot be later than end time.");
		return SendAsync<OrderlyNetworkCandles, OrderlyNetworkCandleQuery>(
			HttpMethod.Post, "/v1/public/query", new()
			{
				Symbol = symbol.ThrowIfEmpty(nameof(symbol)),
				Interval = interval.ThrowIfEmpty(nameof(interval)),
				StartTime = from.ToOrderlyMilliseconds(),
				EndTime = to.ToOrderlyMilliseconds(),
				Limit = limit,
			}, false, true, cancellationToken);
	}

	public ValueTask<OrderlyNetworkHoldingData> GetHoldingsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<OrderlyNetworkHoldingData, OrderlyNetworkEmptyRequest>(
			HttpMethod.Get, "/v1/client/holding?all=true", null, true, true,
			cancellationToken);

	public ValueTask<OrderlyNetworkPositions> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<OrderlyNetworkPositions, OrderlyNetworkEmptyRequest>(
			HttpMethod.Get, "/v1/positions", null, true, true,
			cancellationToken);

	public async ValueTask<OrderlyNetworkOrder[]> GetOrdersAsync(string symbol,
		DateTime? from, DateTime? to, int page, int size,
		CancellationToken cancellationToken)
	{
		ValidatePrivatePage(page, size);
		var path = new StringBuilder("/v1/orders?page=")
			.Append(page.ToString(CultureInfo.InvariantCulture))
			.Append("&size=").Append(size.ToString(CultureInfo.InvariantCulture));
		if (!symbol.IsEmpty())
			path.Append("&symbol=").Append(Escape(symbol));
		if (from is DateTime start)
			path.Append("&start_t=").Append(start.EnsureOrderlyUtc()
				.ToOrderlyMilliseconds().ToString(CultureInfo.InvariantCulture));
		if (to is DateTime end)
			path.Append("&end_t=").Append(end.EnsureOrderlyUtc()
				.ToOrderlyMilliseconds().ToString(CultureInfo.InvariantCulture));
		return (await SendAsync<OrderlyNetworkRows<OrderlyNetworkOrder>,
			OrderlyNetworkEmptyRequest>(HttpMethod.Get, path.ToString(), null, true,
			true, cancellationToken))?.Rows ?? [];
	}

	public async ValueTask<OrderlyNetworkPrivateTrade[]> GetPrivateTradesAsync(
		string symbol, DateTime? from, DateTime? to, int page, int size,
		CancellationToken cancellationToken)
	{
		ValidatePrivatePage(page, size);
		var path = new StringBuilder("/v1/trades?page=")
			.Append(page.ToString(CultureInfo.InvariantCulture))
			.Append("&size=").Append(size.ToString(CultureInfo.InvariantCulture));
		if (!symbol.IsEmpty())
			path.Append("&symbol=").Append(Escape(symbol));
		if (from is DateTime start)
			path.Append("&start_t=").Append(start.EnsureOrderlyUtc()
				.ToOrderlyMilliseconds().ToString(CultureInfo.InvariantCulture));
		if (to is DateTime end)
			path.Append("&end_t=").Append(end.EnsureOrderlyUtc()
				.ToOrderlyMilliseconds().ToString(CultureInfo.InvariantCulture));
		return (await SendAsync<OrderlyNetworkRows<OrderlyNetworkPrivateTrade>,
			OrderlyNetworkEmptyRequest>(HttpMethod.Get, path.ToString(), null, true,
			true, cancellationToken))?.Rows ?? [];
	}

	public ValueTask<OrderlyNetworkOrderAcceptance> PlaceOrderAsync(
		OrderlyNetworkOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<OrderlyNetworkOrderAcceptance, OrderlyNetworkOrderRequest>(
			HttpMethod.Post, "/v1/order",
			request ?? throw new ArgumentNullException(nameof(request)), true, false,
			cancellationToken);

	public ValueTask<OrderlyNetworkOrderAcceptance> EditOrderAsync(
		OrderlyNetworkEditOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<OrderlyNetworkOrderAcceptance,
			OrderlyNetworkEditOrderRequest>(HttpMethod.Put, "/v1/order",
			request ?? throw new ArgumentNullException(nameof(request)), true, false,
			cancellationToken);

	public ValueTask<OrderlyNetworkStatusResponse> CancelOrderAsync(string symbol,
		long orderId, CancellationToken cancellationToken)
	{
		if (orderId <= 0)
			throw new ArgumentOutOfRangeException(nameof(orderId), orderId,
				"Orderly order ID must be positive.");
		var path = "/v1/order?order_id=" +
			orderId.ToString(CultureInfo.InvariantCulture) + "&symbol=" +
			Escape(symbol);
		return SendAsync<OrderlyNetworkStatusResponse, OrderlyNetworkEmptyRequest>(
			HttpMethod.Delete, path, null, true, false, cancellationToken);
	}

	public ValueTask<OrderlyNetworkStatusResponse> CancelClientOrderAsync(
		string symbol, string clientOrderId,
		CancellationToken cancellationToken)
	{
		var path = "/v1/client/order?client_order_id=" +
			Escape(clientOrderId) + "&symbol=" + Escape(symbol);
		return SendAsync<OrderlyNetworkStatusResponse, OrderlyNetworkEmptyRequest>(
			HttpMethod.Delete, path, null, true, false, cancellationToken);
	}

	public ValueTask<OrderlyNetworkStatusResponse> CancelOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var path = "/v1/orders" + (symbol.IsEmpty()
			? string.Empty
			: "?symbol=" + Escape(symbol));
		return SendAsync<OrderlyNetworkStatusResponse, OrderlyNetworkEmptyRequest>(
			HttpMethod.Delete, path, null, true, false, cancellationToken);
	}

	private async ValueTask<TData> SendAsync<TData, TRequest>(HttpMethod method,
		string pathWithQuery, TRequest body, bool isPrivate, bool canRetry,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		pathWithQuery = pathWithQuery.ThrowIfEmpty(nameof(pathWithQuery));
		if (!pathWithQuery.StartsWith('/'))
			pathWithQuery = "/" + pathWithQuery;
		var json = body is null ? null : JsonConvert.SerializeObject(body,
			_jsonSettings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, pathWithQuery.TrimStart('/')));
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			else
				request.Headers.TryAddWithoutValidation("Content-Type",
					"application/x-www-form-urlencoded");
			if (isPrivate)
				AddAuthentication(request, pathWithQuery, json);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (canRetry && attempt + 1 < _maximumReadAttempts &&
				IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * (1 << attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException("Orderly Network HTTP " +
					(int)response.StatusCode + " (" + response.StatusCode + "): " +
					Limit(responseBody, 2048), null, response.StatusCode);
			return Deserialize<TData>(responseBody);
		}
	}

	private void AddAuthentication(HttpRequestMessage request,
		string pathWithQuery, string body)
	{
		if (!_signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"An Orderly account ID and ED25519 secret are required.");
		var signature = _signer.SignRequest(request.Method, pathWithQuery, body,
			ServerTime);
		request.Headers.TryAddWithoutValidation("orderly-account-id",
			_signer.AccountId);
		request.Headers.TryAddWithoutValidation("orderly-key",
			signature.PublicKey);
		request.Headers.TryAddWithoutValidation("orderly-timestamp",
			signature.Timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("orderly-signature",
			signature.Value);
	}

	private TData Deserialize<TData>(string responseBody)
	{
		if (responseBody.IsEmpty())
			throw new InvalidDataException(
				"Orderly Network returned an empty REST response.");
		OrderlyNetworkResponse<TData> response;
		try
		{
			response = JsonConvert.DeserializeObject<
				OrderlyNetworkResponse<TData>>(responseBody, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Orderly Network returned malformed REST JSON.", error);
		}
		if (response is null)
			throw new InvalidDataException(
				"Orderly Network returned an empty REST JSON value.");
		var timestamp = response.Timestamp > 0
			? response.Timestamp
			: response.QueryTimestamp;
		if (timestamp > 0)
			using (_sync.EnterScope())
				_serverTime = timestamp.FromOrderlyMilliseconds();
		if (!response.IsSuccess)
			throw new InvalidOperationException("Orderly Network request failed" +
				(response.Code.IsEmpty() ? string.Empty : " (" + response.Code + ")") +
				": " + (response.Message.IsEmpty()
					? "unknown error"
					: response.Message));
		return response.Data;
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(110);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static void ValidatePrivatePage(int page, int size)
	{
		if (page < 1)
			throw new ArgumentOutOfRangeException(nameof(page), page,
				"Orderly history page must be positive.");
		if (size is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(size), size,
				"Orderly history page size must be between 1 and 500.");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Orderly Network response exceeds the 32 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Orderly Network response exceeds the 32 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}
}

sealed class OrderlyNetworkEmptyRequest
{
}
