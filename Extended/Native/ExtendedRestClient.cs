namespace StockSharp.Extended.Native;

sealed class ExtendedRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public ExtendedRestClient(string endpoint, string apiKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Extended REST endpoint must use HTTP or HTTPS.", nameof(endpoint));
		_apiKey = apiKey?.Trim();
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Extended-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "Extended_REST";

	public async ValueTask<ExtendedMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedMarket[], ExtendedEmpty>(HttpMethod.Get,
			"info/markets", null, false, cancellationToken)).Data ?? [];

	public async ValueTask<ExtendedOrderBook> GetOrderBookAsync(string market,
		CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedOrderBook, ExtendedEmpty>(HttpMethod.Get,
			"info/markets/" + Escape(market) + "/orderbook", null, false,
			cancellationToken)).Data;

	public async ValueTask<ExtendedPublicTrade[]> GetPublicTradesAsync(
		string market, CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedPublicTrade[], ExtendedEmpty>(HttpMethod.Get,
			"info/markets/" + Escape(market) + "/trades", null, false,
			cancellationToken)).Data ?? [];

	public async ValueTask<ExtendedCandle[]> GetCandlesAsync(string market,
		string interval, int limit, DateTime? endTime,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder("info/candles/")
			.Append(Escape(market)).Append("/trades?interval=")
			.Append(Escape(interval)).Append("&limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture));
		if (endTime is DateTime end)
			path.Append("&endTime=").Append(end.EnsureExtendedUtc()
				.ToExtendedUnixMilliseconds().ToString(CultureInfo.InvariantCulture));
		return (await SendAsync<ExtendedCandle[], ExtendedEmpty>(HttpMethod.Get,
			path.ToString(), null, false, cancellationToken)).Data ?? [];
	}

	public async ValueTask<ExtendedAccount> GetAccountAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedAccount, ExtendedEmpty>(HttpMethod.Get,
			"user/account/info", null, true, cancellationToken)).Data;

	public async ValueTask<ExtendedBalance> GetBalanceAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedBalance, ExtendedEmpty>(HttpMethod.Get,
			"user/balance", null, true, cancellationToken)).Data;

	public async ValueTask<ExtendedSpotBalance[]> GetSpotBalancesAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedSpotBalance[], ExtendedEmpty>(HttpMethod.Get,
			"user/spot/balances", null, true, cancellationToken)).Data ?? [];

	public async ValueTask<ExtendedPosition[]> GetPositionsAsync(
		string market, CancellationToken cancellationToken)
	{
		var path = "user/positions" + (market.IsEmpty()
			? string.Empty
			: "?market=" + Escape(market));
		return (await SendAsync<ExtendedPosition[], ExtendedEmpty>(HttpMethod.Get,
			path, null, true, cancellationToken)).Data ?? [];
	}

	public async ValueTask<ExtendedOrder[]> GetOpenOrdersAsync(string market,
		CancellationToken cancellationToken)
	{
		var path = "user/orders" + (market.IsEmpty()
			? string.Empty
			: "?market=" + Escape(market));
		return (await SendAsync<ExtendedOrder[], ExtendedEmpty>(HttpMethod.Get,
			path, null, true, cancellationToken)).Data ?? [];
	}

	public async ValueTask<ExtendedOrder> GetOrderAsync(long orderId,
		CancellationToken cancellationToken)
	{
		if (orderId <= 0)
			throw new ArgumentOutOfRangeException(nameof(orderId), orderId,
				"Extended order ID must be positive.");
		return (await SendAsync<ExtendedOrder, ExtendedEmpty>(HttpMethod.Get,
			"user/orders/" + orderId.ToString(CultureInfo.InvariantCulture),
			null, true, cancellationToken)).Data;
	}

	public ValueTask<ExtendedResponse<ExtendedOrder[]>> GetOrderHistoryAsync(
		string market, int limit, long? cursor,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder("user/orders/history?limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture));
		Append(path, "market", market);
		Append(path, "cursor", cursor);
		return SendAsync<ExtendedOrder[], ExtendedEmpty>(HttpMethod.Get,
			path.ToString(), null, true, cancellationToken);
	}

	public ValueTask<ExtendedResponse<ExtendedAccountTrade[]>> GetTradesAsync(
		string market, int limit, long? cursor,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder("user/trades?limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture));
		Append(path, "market", market);
		Append(path, "cursor", cursor);
		return SendAsync<ExtendedAccountTrade[], ExtendedEmpty>(HttpMethod.Get,
			path.ToString(), null, true, cancellationToken);
	}

	public async ValueTask<ExtendedTradingFee[]> GetFeesAsync(string market,
		CancellationToken cancellationToken)
	{
		var path = "user/fees" + (market.IsEmpty()
			? string.Empty
			: "?market=" + Escape(market));
		return (await SendAsync<ExtendedTradingFee[], ExtendedEmpty>(HttpMethod.Get,
			path, null, true, cancellationToken)).Data ?? [];
	}

	public async ValueTask<ExtendedPlacedOrder> PlaceOrderAsync(
		ExtendedCreateOrderRequest request, CancellationToken cancellationToken)
		=> (await SendAsync<ExtendedPlacedOrder, ExtendedCreateOrderRequest>(
			HttpMethod.Post, "user/order", request, true, cancellationToken)).Data;

	public ValueTask<ExtendedResponse<ExtendedEmpty>> CancelOrderAsync(long orderId,
		CancellationToken cancellationToken)
	{
		if (orderId <= 0)
			throw new ArgumentOutOfRangeException(nameof(orderId), orderId,
				"Extended order ID must be positive.");
		return SendAsync<ExtendedEmpty, ExtendedEmpty>(HttpMethod.Delete,
			"user/order/" + orderId.ToString(CultureInfo.InvariantCulture), null,
			true, cancellationToken);
	}

	public ValueTask<ExtendedResponse<ExtendedEmpty>> CancelOrderAsync(
		string externalId, CancellationToken cancellationToken)
		=> SendAsync<ExtendedEmpty, ExtendedEmpty>(HttpMethod.Delete,
			"user/order?externalId=" + Escape(externalId), null, true,
			cancellationToken);

	public ValueTask<ExtendedResponse<ExtendedEmpty>> MassCancelAsync(
		ExtendedMassCancelRequest request, CancellationToken cancellationToken)
		=> SendAsync<ExtendedEmpty, ExtendedMassCancelRequest>(HttpMethod.Post,
			"user/order/massCancel", request ?? throw new ArgumentNullException(
				nameof(request)), true, cancellationToken);

	private async ValueTask<ExtendedResponse<TResponse>> SendAsync<TResponse,
		TRequest>(HttpMethod method, string path, TRequest requestBody,
		bool isPrivate, CancellationToken cancellationToken)
		where TRequest : class
	{
		if (isPrivate && _apiKey.IsEmpty())
			throw new InvalidOperationException(
				"An Extended API key is required for this operation.");
		var canRetry = method == HttpMethod.Get;
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, path.TrimStart('/')));
			if (isPrivate)
				request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
			if (requestBody is not null)
			{
				var json = JsonConvert.SerializeObject(requestBody, _jsonSettings);
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			}
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (canRetry && attempt + 1 < _maximumReadAttempts &&
				IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * (1 << attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					"Extended HTTP " + (int)response.StatusCode + " (" +
					response.StatusCode + "): " + Limit(body, 2048), null,
					response.StatusCode);
			if (body.IsEmpty())
				body = "{\"status\":\"OK\"}";
			ExtendedResponse<TResponse> result;
			try
			{
				result = JsonConvert.DeserializeObject<ExtendedResponse<TResponse>>(
					body, _jsonSettings) ?? throw new InvalidDataException(
						"Extended returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Extended returned malformed REST JSON.", error);
			}
			if (!result.IsSuccess || result.Error is not null)
				throw new InvalidOperationException(
					"Extended API error" + (result.Error is null
						? string.Empty
						: " " + result.Error.Code.ToString(CultureInfo.InvariantCulture)) +
					": " + (result.Error?.Message.IsEmpty() == false
						? result.Error.Message
						: "request failed"));
			return result;
		}
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(40);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static void Append(StringBuilder query, string name, string value)
	{
		if (!value.IsEmpty())
			query.Append('&').Append(name).Append('=').Append(Escape(value));
	}

	private static void Append(StringBuilder query, string name, long? value)
	{
		if (value is long actual)
			query.Append('&').Append(name).Append('=')
				.Append(actual.ToString(CultureInfo.InvariantCulture));
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Extended result limit must be between 1 and 1000.");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Extended response exceeds the 32 MiB safety limit.");
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
					"Extended response exceeds the 32 MiB safety limit.");
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
