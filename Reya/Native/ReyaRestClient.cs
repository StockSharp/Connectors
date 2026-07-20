namespace StockSharp.Reya.Native;

sealed class ReyaRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public ReyaRestClient(string endpoint)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') +
			"/", UriKind.Absolute);
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Reya-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
	}

	public override string Name => "Reya_REST";

	public async ValueTask<ReyaPerpetualMarketDefinition[]>
		GetPerpetualMarketsAsync(CancellationToken cancellationToken)
		=> await GetAsync<ReyaPerpetualMarketDefinition[]>("marketDefinitions",
			cancellationToken) ?? [];

	public async ValueTask<ReyaSpotMarketDefinition[]> GetSpotMarketsAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaSpotMarketDefinition[]>("spotMarketDefinitions",
			cancellationToken) ?? [];

	public async ValueTask<ReyaMarketSummary[]> GetPerpetualSummariesAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaMarketSummary[]>("markets/summary",
			cancellationToken) ?? [];

	public async ValueTask<ReyaSpotMarketSummary[]> GetSpotSummariesAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaSpotMarketSummary[]>("spotMarkets/summary",
			cancellationToken) ?? [];

	public ValueTask<ReyaMarketSummary> GetPerpetualSummaryAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<ReyaMarketSummary>("market/" + Escape(symbol) + "/summary",
			cancellationToken);

	public ValueTask<ReyaSpotMarketSummary> GetSpotSummaryAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<ReyaSpotMarketSummary>("spotMarket/" + Escape(symbol) +
			"/summary", cancellationToken);

	public async ValueTask<ReyaPrice[]> GetPricesAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaPrice[]>("prices", cancellationToken) ?? [];

	public ValueTask<ReyaPrice> GetPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<ReyaPrice>("prices/" + Escape(symbol), cancellationToken);

	public ValueTask<ReyaDepth> GetDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<ReyaDepth>("market/" + Escape(symbol) + "/depth",
			cancellationToken);

	public ValueTask<ReyaPerpetualExecutionPage> GetPerpetualExecutionsAsync(
		string symbol, DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
		=> GetAsync<ReyaPerpetualExecutionPage>(AddTimeQuery("market/" +
			Escape(symbol) + "/perpExecutions", from, to), cancellationToken);

	public ValueTask<ReyaSpotExecutionPage> GetSpotExecutionsAsync(string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
		=> GetAsync<ReyaSpotExecutionPage>(AddTimeQuery("market/" +
			Escape(symbol) + "/spotExecutions", from, to), cancellationToken);

	public ValueTask<ReyaCandleHistory> GetCandlesAsync(string symbol,
		string resolution, DateTime? to, CancellationToken cancellationToken)
	{
		var path = "candleHistory/" + Escape(symbol) + "/" +
			Escape(resolution);
		if (to is DateTime end)
			path += "?endTime=" + end.EnsureReyaUtc().ToReyaMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		return GetAsync<ReyaCandleHistory>(path, cancellationToken);
	}

	public async ValueTask<ReyaAccount[]> GetAccountsAsync(string address,
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaAccount[]>(WalletPath(address, "accounts"),
			cancellationToken) ?? [];

	public async ValueTask<ReyaPosition[]> GetPositionsAsync(string address,
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaPosition[]>(WalletPath(address, "positions"),
			cancellationToken) ?? [];

	public async ValueTask<ReyaAccountBalance[]> GetBalancesAsync(string address,
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaAccountBalance[]>(WalletPath(address,
			"accountBalances"), cancellationToken) ?? [];

	public async ValueTask<ReyaOrder[]> GetOpenOrdersAsync(string address,
		CancellationToken cancellationToken)
		=> await GetAsync<ReyaOrder[]>(WalletPath(address, "openOrders"),
			cancellationToken) ?? [];

	public ValueTask<ReyaPerpetualExecutionPage>
		GetWalletPerpetualExecutionsAsync(string address, DateTime? from,
			DateTime? to, CancellationToken cancellationToken)
		=> GetAsync<ReyaPerpetualExecutionPage>(AddTimeQuery(WalletPath(address,
			"perpExecutions"), from, to), cancellationToken);

	public ValueTask<ReyaSpotExecutionPage> GetWalletSpotExecutionsAsync(
		string address, DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
		=> GetAsync<ReyaSpotExecutionPage>(AddTimeQuery(WalletPath(address,
			"spotExecutions"), from, to), cancellationToken);

	public ValueTask<ReyaCreateOrderResponse> CreateOrderAsync(
		ReyaCreateOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<ReyaCreateOrderResponse, ReyaCreateOrderRequest>("createOrder",
			request ?? throw new ArgumentNullException(nameof(request)),
			cancellationToken);

	public ValueTask<ReyaCancelOrderResponse> CancelOrderAsync(
		ReyaCancelOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<ReyaCancelOrderResponse, ReyaCancelOrderRequest>("cancelOrder",
			request ?? throw new ArgumentNullException(nameof(request)),
			cancellationToken);

	public ValueTask<ReyaMassCancelResponse> CancelAllAsync(
		ReyaMassCancelRequest request, CancellationToken cancellationToken)
		=> PostAsync<ReyaMassCancelResponse, ReyaMassCancelRequest>("cancelAll",
			request ?? throw new ArgumentNullException(nameof(request)),
			cancellationToken);

	private ValueTask<TResponse> GetAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse, ReyaEmptyRequest>(HttpMethod.Get, path, null, true,
			cancellationToken);

	private ValueTask<TResponse> PostAsync<TResponse, TRequest>(string path,
		TRequest request, CancellationToken cancellationToken)
		where TRequest : class
		=> SendAsync<TResponse, TRequest>(HttpMethod.Post, path, request, false,
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
				new Uri(_endpoint, pathWithQuery));
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (isRetryAllowed && attempt + 1 < _maximumReadAttempts &&
				IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * (1 << attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateHttpError(response.StatusCode, responseBody);
			return Deserialize<TResponse>(responseBody);
		}
	}

	private TResponse Deserialize<TResponse>(string responseBody)
	{
		if (responseBody.IsEmpty())
			throw new InvalidDataException("Reya returned an empty REST response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(responseBody,
				_jsonSettings) ?? throw new InvalidDataException(
					"Reya returned an empty REST JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Reya returned malformed REST JSON.", error);
		}
	}

	private HttpRequestException CreateHttpError(HttpStatusCode statusCode,
		string responseBody)
	{
		ReyaApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<ReyaApiError>(responseBody,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = error is null
			? Limit(responseBody, 2048)
			: (error.Error.IsEmpty() ? string.Empty : error.Error + ": ") +
				(error.Message.IsEmpty() ? "unknown error" : error.Message);
		return new("Reya HTTP " + (int)statusCode + " (" + statusCode + "): " +
			details, null, statusCode);
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(70);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static string WalletPath(string address, string resource)
		=> "wallet/" + Escape(address) + "/" +
			resource.ThrowIfEmpty(nameof(resource));

	private static string AddTimeQuery(string path, DateTime? from, DateTime? to)
	{
		var separator = '?';
		var result = new StringBuilder(path);
		if (from is DateTime start)
		{
			result.Append(separator).Append("startTime=").Append(start
				.EnsureReyaUtc().ToReyaMilliseconds().ToString(
					CultureInfo.InvariantCulture));
			separator = '&';
		}
		if (to is DateTime end)
			result.Append(separator).Append("endTime=").Append(end.EnsureReyaUtc()
				.ToReyaMilliseconds().ToString(CultureInfo.InvariantCulture));
		return result.ToString();
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
				"Reya response exceeds the 32 MiB safety limit.");
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
					"Reya response exceeds the 32 MiB safety limit.");
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

sealed class ReyaEmptyRequest
{
}
