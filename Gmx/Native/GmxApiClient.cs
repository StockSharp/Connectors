namespace StockSharp.Gmx.Native;

sealed class GmxApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private const int _maximumReadAttempts = 4;
	private readonly Uri[] _endpoints;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private DateTime _nextRequestTime;

	public GmxApiClient(string primaryEndpoint, string secondaryEndpoint)
	{
		var primary = CreateEndpoint(primaryEndpoint, nameof(primaryEndpoint));
		var secondary = CreateEndpoint(secondaryEndpoint,
			nameof(secondaryEndpoint));
		_endpoints = primary == secondary ? [primary] : [primary, secondary];
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-GMX-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
	}

	public override string Name => "GMX_API";

	public async ValueTask<GmxApiMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<GmxApiMarket[]>("v1/markets", cancellationToken) ?? [];

	public async ValueTask<GmxToken[]> GetTokensAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<GmxToken[]>("v1/tokens/info", cancellationToken) ?? [];

	public async ValueTask<GmxMarketTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<GmxMarketTicker[]>("v1/markets/tickers",
			cancellationToken) ?? [];

	public async ValueTask<GmxCandle[]> GetCandlesAsync(string symbol,
		string timeFrame, int limit, long? since,
		CancellationToken cancellationToken)
	{
		var path = new StringBuilder("v1/prices/ohlcv?symbol=")
			.Append(Escape(symbol)).Append("&timeframe=").Append(Escape(timeFrame))
			.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		if (since is long timestamp)
			path.Append("&since=").Append(timestamp.ToString(
				CultureInfo.InvariantCulture));
		return await GetAsync<GmxCandle[]>(path.ToString(), cancellationToken) ?? [];
	}

	public async ValueTask<GmxWalletBalance[]> GetWalletBalancesAsync(
		string address, CancellationToken cancellationToken)
		=> await GetAsync<GmxWalletBalance[]>("v1/balances/wallet?address=" +
			Escape(address), cancellationToken) ?? [];

	public async ValueTask<GmxPosition[]> GetPositionsAsync(string address,
		CancellationToken cancellationToken)
		=> await GetAsync<GmxPosition[]>("v1/positions?address=" +
			Escape(address), cancellationToken) ?? [];

	public async ValueTask<GmxOrder[]> GetOrdersAsync(string address,
		CancellationToken cancellationToken)
		=> await GetAsync<GmxOrder[]>("v1/orders?address=" + Escape(address),
			cancellationToken) ?? [];

	public ValueTask<GmxTradeSearchResponse> SearchTradesAsync(
		GmxTradeSearchRequest request, CancellationToken cancellationToken)
		=> PostAsync<GmxTradeSearchResponse, GmxTradeSearchRequest>(
			"v1/trades/search", request, cancellationToken);

	public ValueTask<GmxPrepareOrderResponse> PrepareOrderAsync(
		GmxPrepareOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<GmxPrepareOrderResponse, GmxPrepareOrderRequest>(
			"v1/orders/txns/prepare", request, cancellationToken);

	public ValueTask<GmxPrepareOrderResponse> PrepareEditAsync(
		GmxPrepareEditRequest request, CancellationToken cancellationToken)
		=> PostAsync<GmxPrepareOrderResponse, GmxPrepareEditRequest>(
			"v1/orders/txns/edit/prepare", request, cancellationToken);

	public ValueTask<GmxPrepareOrderResponse> PrepareCancelAsync(
		GmxPrepareCancelRequest request, CancellationToken cancellationToken)
		=> PostAsync<GmxPrepareOrderResponse, GmxPrepareCancelRequest>(
			"v1/orders/txns/cancel/prepare", request, cancellationToken);

	public ValueTask<GmxSubmitOrderResponse> SubmitOrderAsync(
		GmxSubmitOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<GmxSubmitOrderResponse, GmxSubmitOrderRequest>(
			"v1/orders/txns/submit", request, cancellationToken);

	public ValueTask<GmxOrderStatusResponse> GetOrderStatusAsync(
		string requestId, CancellationToken cancellationToken)
		=> PostAsync<GmxOrderStatusResponse, GmxOrderStatusRequest>(
			"v1/orders/txns/status", new() { RequestId = requestId },
			cancellationToken);

	private ValueTask<TResponse> GetAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse, GmxEmptyRequest>(HttpMethod.Get, path, null, true,
			cancellationToken);

	private ValueTask<TResponse> PostAsync<TResponse, TRequest>(string path,
		TRequest request, CancellationToken cancellationToken)
		where TRequest : class
		=> SendAsync<TResponse, TRequest>(HttpMethod.Post, path,
			request ?? throw new ArgumentNullException(nameof(request)), false,
			cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse, TRequest>(
		HttpMethod method, string pathWithQuery, TRequest body, bool isRetryAllowed,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		pathWithQuery = pathWithQuery.ThrowIfEmpty(nameof(pathWithQuery))
			.TrimStart('/');
		var json = body is null ? null : JsonConvert.SerializeObject(body,
			_jsonSettings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoints[attempt % _endpoints.Length], pathWithQuery));
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			try
			{
				using var response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				var responseBody = await ReadBodyAsync(response.Content,
					cancellationToken);
				if (isRetryAllowed && attempt + 1 < _maximumReadAttempts &&
					IsTransient(response.StatusCode))
				{
					await DelayRetryAsync(attempt, response.Headers.RetryAfter?.Delta,
						cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateHttpError(response.StatusCode, responseBody);
				return Deserialize<TResponse>(responseBody);
			}
			catch (Exception error) when (isRetryAllowed &&
				attempt + 1 < _maximumReadAttempts &&
				(error is HttpRequestException or TaskCanceledException) &&
				!cancellationToken.IsCancellationRequested)
			{
				await DelayRetryAsync(attempt, null, cancellationToken);
			}
		}
	}

	private TResponse Deserialize<TResponse>(string responseBody)
	{
		if (responseBody.IsEmpty())
			throw new InvalidDataException("GMX returned an empty API response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(responseBody,
				_jsonSettings) ?? throw new InvalidDataException(
				"GMX returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("GMX returned malformed JSON.", error);
		}
	}

	private static HttpRequestException CreateHttpError(HttpStatusCode statusCode,
		string responseBody)
		=> new("GMX HTTP " + (int)statusCode + " (" + statusCode + "): " +
			Limit(responseBody, 2048), null, statusCode);

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(25);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static ValueTask DelayRetryAsync(int attempt, TimeSpan? retryAfter,
		CancellationToken cancellationToken)
		=> new(Task.Delay((retryAfter ?? TimeSpan.FromMilliseconds(
			250 * (1 << attempt))).Min(TimeSpan.FromSeconds(5)),
			cancellationToken));

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout ||
			statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"GMX response exceeds the 32 MiB safety limit.");
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
					"GMX response exceeds the 32 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).TrimEnd('/') + "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"GMX API endpoint must use HTTP or HTTPS.", parameterName);
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

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

sealed class GmxEmptyRequest
{
}
