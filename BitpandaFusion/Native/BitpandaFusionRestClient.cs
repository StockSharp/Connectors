namespace StockSharp.BitpandaFusion.Native;

sealed class BitpandaFusionRestClient : BaseLogReceiver
{
	private enum RateLimits
	{
		Global,
		MarketData,
		CreateOrder,
	}

	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _globalRateSync = new(1, 1);
	private readonly SemaphoreSlim _marketRateSync = new(1, 1);
	private readonly SemaphoreSlim _createOrderRateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTimeOffset,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextGlobalRequestTime;
	private DateTime _nextMarketRequestTime;
	private DateTime _nextCreateOrderTime;

	public BitpandaFusionRestClient(string endpoint, SecureString key)
	{
		var value = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out _endpoint) ||
			!_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
		{
			throw new ArgumentException(
				"Bitpanda Fusion endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));
		}

		var apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		if (apiKey.IsEmpty())
			throw new ArgumentException("Bitpanda Fusion API key is required.", nameof(key));

		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-BitpandaFusion-Connector/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"X-Client-Origin", "stocksharp");
	}

	public override string Name => "BitpandaFusion_Rest";

	protected override void DisposeManaged()
	{
		_globalRateSync.Dispose();
		_marketRateSync.Dispose();
		_createOrderRateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BitpandaFusionServerTime> GetTimeAsync(
		CancellationToken cancellationToken)
		=> SendReadAsync<BitpandaFusionServerTime>("v1/time", true,
			cancellationToken);

	public ValueTask<BitpandaFusionPair[]> GetPairsAsync(string pair,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		AppendQuery(query, "pair", pair);
		return SendReadAsync<BitpandaFusionPair[]>("v1/pairs" + query, true,
			cancellationToken);
	}

	public ValueTask<BitpandaFusionTicker[]> GetTickersAsync(string pair,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		AppendQuery(query, "pair", pair);
		return SendReadAsync<BitpandaFusionTicker[]>("v1/tickers" + query, true,
			cancellationToken);
	}

	public ValueTask<BitpandaFusionOrderBook> GetOrderBookAsync(string pair, int depth,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		AppendQuery(query, "depth", depth.ToString(CultureInfo.InvariantCulture));
		return SendReadAsync<BitpandaFusionOrderBook>(
			$"v1/orderbook/{EscapeSegment(pair)}{query}", true, cancellationToken);
	}

	public ValueTask<BitpandaFusionCandle[]> GetCandlesAsync(string pair,
		BitpandaFusionCandlesFilter filter, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var query = new StringBuilder();
		AppendQuery(query, "interval", filter.Interval);
		AppendQuery(query, "from", filter.From?.ToUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "to", filter.To?.ToUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		if (filter.Limit > 0)
			AppendQuery(query, "limit", filter.Limit.ToString(CultureInfo.InvariantCulture));
		return SendReadAsync<BitpandaFusionCandle[]>(
			$"v1/candles/{EscapeSegment(pair)}{query}", true, cancellationToken);
	}

	public ValueTask<BitpandaFusionAsset[]> GetAssetsAsync(string assets,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		AppendQuery(query, "asset", assets);
		return SendReadAsync<BitpandaFusionAsset[]>("v1/assets" + query, true,
			cancellationToken);
	}

	public ValueTask<BitpandaFusionAccount> GetAccountAsync(
		CancellationToken cancellationToken)
		=> SendReadAsync<BitpandaFusionAccount>("v1/account", false,
			cancellationToken);

	public ValueTask<BitpandaFusionBalance[]> GetBalancesAsync(string currency,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		AppendQuery(query, "currency", currency);
		return SendReadAsync<BitpandaFusionBalance[]>(
			"v1/account/balances" + query, false, cancellationToken);
	}

	public ValueTask<BitpandaFusionOrdersPage> GetOrdersAsync(
		BitpandaFusionOrdersFilter filter, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var query = new StringBuilder();
		AppendQuery(query, "pair", filter.Pair);
		AppendQuery(query, "status", filter.Status?.ToWire());
		AppendQuery(query, "startTime", filter.From?.ToUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "endTime", filter.To?.ToUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		if (filter.Limit > 0)
			AppendQuery(query, "limit", filter.Limit.ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "cursor", filter.Cursor);
		return SendReadAsync<BitpandaFusionOrdersPage>(
			"v1/account/orders" + query, false, cancellationToken);
	}

	public ValueTask<BitpandaFusionOrder> GetOrderAsync(string id,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitpandaFusionOrder>(
			$"v1/account/orders/{EscapeSegment(id)}", false, cancellationToken);

	public ValueTask<BitpandaFusionTradesPage> GetTradesAsync(
		BitpandaFusionTradesFilter filter, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var query = new StringBuilder();
		AppendQuery(query, "pair", filter.Pair);
		AppendQuery(query, "orderId", filter.OrderId);
		AppendQuery(query, "startTime", filter.From?.ToUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "endTime", filter.To?.ToUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		if (filter.Limit > 0)
			AppendQuery(query, "limit", filter.Limit.ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "cursor", filter.Cursor);
		return SendReadAsync<BitpandaFusionTradesPage>(
			"v1/account/trades" + query, false, cancellationToken);
	}

	public ValueTask<BitpandaFusionTrade> GetTradeAsync(string id,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitpandaFusionTrade>(
			$"v1/account/trades/{EscapeSegment(id)}", false, cancellationToken);

	public ValueTask<BitpandaFusionOrder> CreateOrderAsync(
		BitpandaFusionCreateOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendWriteAsync<BitpandaFusionCreateOrderRequest, BitpandaFusionOrder>(
			HttpMethod.Post, "v1/account/orders", request, true, cancellationToken);
	}

	public ValueTask<BitpandaFusionOrder> CancelOrderAsync(string id,
		CancellationToken cancellationToken)
		=> SendDeleteAsync<BitpandaFusionOrder>(
			$"v1/account/orders/{EscapeSegment(id)}", cancellationToken);

	private async ValueTask<TResponse> SendReadAsync<TResponse>(string path,
		bool isMarketData, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			if (isMarketData)
				await WaitRateLimitAsync(RateLimits.MarketData, cancellationToken);
			await WaitRateLimitAsync(RateLimits.Global, cancellationToken);

			using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri(path));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body, true);
			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendWriteAsync<TRequest, TResponse>(
		HttpMethod method, string path, TRequest payload, bool isCreateOrder,
		CancellationToken cancellationToken)
	{
		if (isCreateOrder)
			await WaitRateLimitAsync(RateLimits.CreateOrder, cancellationToken);
		await WaitRateLimitAsync(RateLimits.Global, cancellationToken);

		var json = JsonConvert.SerializeObject(payload, _jsonSettings);
		using var request = new HttpRequestMessage(method, CreateUri(path))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return Deserialize<TResponse>(body, true);
	}

	private async ValueTask<TResponse> SendDeleteAsync<TResponse>(string path,
		CancellationToken cancellationToken)
	{
		await WaitRateLimitAsync(RateLimits.Global, cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete, CreateUri(path));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return body.IsEmpty() ? default : Deserialize<TResponse>(body, true);
	}

	private TResponse Deserialize<TResponse>(string body, bool isRequired)
	{
		if (body.IsEmpty())
		{
			if (!isRequired)
				return default;
			throw new InvalidDataException("Bitpanda Fusion returned an empty response.");
		}

		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException(
					"Bitpanda Fusion returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitpanda Fusion returned an unexpected response shape.", error);
		}
	}

	private async ValueTask WaitRateLimitAsync(RateLimits limit,
		CancellationToken cancellationToken)
	{
		var sync = limit switch
		{
			RateLimits.Global => _globalRateSync,
			RateLimits.MarketData => _marketRateSync,
			RateLimits.CreateOrder => _createOrderRateSync,
			_ => throw new ArgumentOutOfRangeException(nameof(limit), limit, null),
		};
		await sync.WaitAsync(cancellationToken);
		try
		{
			var next = limit switch
			{
				RateLimits.Global => _nextGlobalRequestTime,
				RateLimits.MarketData => _nextMarketRequestTime,
				RateLimits.CreateOrder => _nextCreateOrderTime,
				_ => throw new ArgumentOutOfRangeException(nameof(limit), limit, null),
			};
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);

			var nextTime = DateTime.UtcNow + (limit switch
			{
				RateLimits.Global => TimeSpan.FromMilliseconds(62),
				RateLimits.MarketData => TimeSpan.FromMilliseconds(252),
				RateLimits.CreateOrder => TimeSpan.FromMilliseconds(202),
				_ => throw new ArgumentOutOfRangeException(nameof(limit), limit, null),
			});
			switch (limit)
			{
				case RateLimits.Global:
					_nextGlobalRequestTime = nextTime;
					break;
				case RateLimits.MarketData:
					_nextMarketRequestTime = nextTime;
					break;
				case RateLimits.CreateOrder:
					_nextCreateOrderTime = nextTime;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(limit), limit, null);
			}
		}
		finally
		{
			sync.Release();
		}
	}

	private Uri CreateUri(string path)
		=> new(_endpoint, path.TrimStart('/'));

	private static string EscapeSegment(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static void AppendQuery(StringBuilder query, string name, string value)
	{
		if (value.IsEmpty())
			return;
		query.Append(query.Length == 0 ? '?' : '&')
			.Append(name)
			.Append('=')
			.Append(Uri.EscapeDataString(value));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			(response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
			TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var response = JsonConvert.DeserializeObject<BitpandaFusionErrorResponse>(
				body ?? string.Empty, _jsonSettings);
			var errors = (response?.Errors ?? [])
				.Select(static error => new[]
				{
					error.Code,
					error.Field,
					error.Title,
					error.Detail,
					error.Message,
				}.Where(static value => !value.IsEmpty()).Join(": "))
				.Where(static value => !value.IsEmpty())
				.Join("; ");
			details = new[] { errors, response?.Message, response?.Error }
				.Where(static value => !value.IsEmpty()).Join(": ").IsEmpty(details);
		}
		catch (JsonException)
		{
		}

		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Bitpanda Fusion HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}
}
