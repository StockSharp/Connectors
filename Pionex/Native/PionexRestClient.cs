namespace StockSharp.Pionex.Native;

readonly record struct PionexParameter(string Name, string Value);

readonly record struct PionexQuery(string Canonical, string Encoded);

readonly record struct PionexPreparedRequest(string Query, string Body, string Signature);

sealed class PionexRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	public PionexRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(NormalizeEndpoint(endpoint), UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Pionex-Connector/1.0");
	}

	public override string Name => nameof(Pionex) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _hasher is not null;

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<PionexSymbol[]> GetSpotSymbolsAsync(CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexSymbolsData<PionexSymbol>>(HttpMethod.Get,
			"/api/v1/common/symbols", [new("type", "SPOT")], cancellationToken)).Data?.Symbols ?? [];

	public async ValueTask<PionexFuturesSymbol[]> GetFuturesSymbolsAsync(CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexSymbolsData<PionexFuturesSymbol>>(HttpMethod.Get,
			"/api/v1/common/symbols", [new("type", "PERP"), new("status", "TRADING")],
			cancellationToken)).Data?.Symbols ?? [];

	public async ValueTask<PionexTicker[]> GetTickersAsync(PionexSections section, string symbol,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexTickersData>(HttpMethod.Get, "/api/v1/market/tickers",
			[
				new("symbol", symbol),
				new("type", section == PionexSections.Spot ? "SPOT" : "PERP"),
			], cancellationToken)).Data?.Tickers ?? [];

	public async ValueTask<PionexBookTicker[]> GetBookTickersAsync(PionexSections section, string symbol,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexBookTickersData>(HttpMethod.Get,
			"/api/v1/market/bookTickers",
			[
				new("symbol", symbol),
				new("type", section == PionexSections.Spot ? "SPOT" : "PERP"),
			], cancellationToken)).Data?.Tickers ?? [];

	public async ValueTask<PionexMarketTrade[]> GetTradesAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexTradesData>(HttpMethod.Get, "/api/v1/market/trades",
			[
				new("symbol", symbol),
				new("limit", limit.Min(500).Max(10).ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data?.Trades ?? [];

	public async ValueTask<PionexDepthData> GetDepthAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexDepthData>(HttpMethod.Get, "/api/v1/market/depth",
			[
				new("symbol", symbol),
				new("limit", limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data;

	public async ValueTask<PionexKline[]> GetKlinesAsync(string symbol, TimeSpan timeFrame,
		DateTime? to, int limit, CancellationToken cancellationToken)
		=> (await SendPublicAsync<PionexKlinesData>(HttpMethod.Get, "/api/v1/market/klines",
			[
				new("symbol", symbol),
				new("interval", timeFrame.ToPionexInterval()),
				new("endTime", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("limit", limit.Min(500).Max(1).ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data?.Klines ?? [];

	public async ValueTask<PionexBalance[]> GetSpotBalancesAsync(CancellationToken cancellationToken)
		=> (await SendPrivateGetAsync<PionexBalancesData>("/api/v1/account/balances", [],
			cancellationToken)).Data?.Balances ?? [];

	public async ValueTask<PionexFuturesBalancesData> GetFuturesBalancesAsync(
		CancellationToken cancellationToken)
		=> (await SendPrivateGetAsync<PionexFuturesBalancesData>("/uapi/v1/account/balances", [],
			cancellationToken)).Data;

	public async ValueTask<PionexPosition[]> GetFuturesPositionsAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendPrivateGetAsync<PionexPositionsData>("/uapi/v1/account/positions",
			[new("symbol", symbol)], cancellationToken)).Data?.Positions ?? [];

	public async ValueTask<PionexOrderResult> PlaceSpotOrderAsync(PionexSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendPrivateBodyAsync<PionexOrderResult, PionexSpotOrderRequest>(HttpMethod.Post,
			"/api/v1/trade/order", request, cancellationToken)).Data;

	public async ValueTask<PionexOrderResult> PlaceFuturesOrderAsync(PionexFuturesOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendPrivateBodyAsync<PionexOrderResult, PionexFuturesOrderRequest>(HttpMethod.Post,
			"/uapi/v1/trade/order", request, cancellationToken)).Data;

	public async ValueTask CancelOrderAsync(PionexSections section, PionexCancelOrderRequest request,
		CancellationToken cancellationToken)
	{
		_ = await SendPrivateBodyAsync<PionexEmptyData, PionexCancelOrderRequest>(HttpMethod.Delete,
			section == PionexSections.Spot ? "/api/v1/trade/order" : "/uapi/v1/trade/order",
			request, cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(PionexSections section,
		PionexCancelAllOrdersRequest request, CancellationToken cancellationToken)
	{
		_ = await SendPrivateBodyAsync<PionexEmptyData, PionexCancelAllOrdersRequest>(HttpMethod.Delete,
			section == PionexSections.Spot ? "/api/v1/trade/allOrders" : "/uapi/v1/trade/allOrders",
			request, cancellationToken);
	}

	public async ValueTask<PionexOrder[]> GetOpenOrdersAsync(PionexSections section, string symbol,
		int limit, CancellationToken cancellationToken)
		=> (await SendPrivateGetAsync<PionexOrdersData>(
			section == PionexSections.Spot ? "/api/v1/trade/openOrders" : "/uapi/v1/trade/openOrders",
			[
				new("symbol", symbol),
				new("limit", section == PionexSections.Futures
					? limit.Min(200).Max(1).ToString(CultureInfo.InvariantCulture)
					: null),
			], cancellationToken)).Data?.Orders ?? [];

	public async ValueTask<PionexOrder[]> GetOrderHistoryAsync(PionexSections section, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
		=> (await SendPrivateGetAsync<PionexOrdersData>(
			section == PionexSections.Spot ? "/api/v1/trade/allOrders" : "/uapi/v1/trade/historyOrders",
			[
				new("symbol", symbol),
				new("startTime", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("endTime", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("limit", limit.Min(200).Max(1).ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data?.Orders ?? [];

	public async ValueTask<PionexFill[]> GetFillsAsync(PionexSections section, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
		=> (await SendPrivateGetAsync<PionexFillsData>(
			section == PionexSections.Spot ? "/api/v1/trade/fills" : "/uapi/v1/trade/fills",
			[
				new("symbol", symbol),
				new("startTime", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("endTime", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("limit", section == PionexSections.Futures
					? limit.Min(100).Max(1).ToString(CultureInfo.InvariantCulture)
					: null),
			], cancellationToken)).Data?.Fills ?? [];

	private ValueTask<PionexResponse<TData>> SendPublicAsync<TData>(HttpMethod method, string path,
		PionexParameter[] parameters, CancellationToken cancellationToken)
		where TData : class
	{
		var query = BuildQuery(parameters);
		return SendAsync<TData>(method, path, true, true,
			() => new(query.Encoded, null, null), cancellationToken);
	}

	private ValueTask<PionexResponse<TData>> SendPrivateGetAsync<TData>(string path,
		PionexParameter[] parameters, CancellationToken cancellationToken)
		where TData : class
	{
		EnsureCredentials();
		return SendAsync<TData>(HttpMethod.Get, path, false, true, () =>
		{
			var query = BuildQuery(AddTimestamp(parameters));
			var payload = HttpMethod.Get.Method + path + "?" + query.Canonical;
			return new(query.Encoded, null, Sign(payload));
		}, cancellationToken);
	}

	private ValueTask<PionexResponse<TData>> SendPrivateBodyAsync<TData, TRequest>(HttpMethod method,
		string path, TRequest request, CancellationToken cancellationToken)
		where TData : class
		where TRequest : class
	{
		EnsureCredentials();
		ArgumentNullException.ThrowIfNull(request);
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendAsync<TData>(method, path, false, false, () =>
		{
			var query = BuildQuery(AddTimestamp([]));
			var payload = method.Method.ToUpperInvariant() + path + "?" + query.Canonical + body;
			return new(query.Encoded, body, Sign(payload));
		}, cancellationToken);
	}

	private async ValueTask<PionexResponse<TData>> SendAsync<TData>(HttpMethod method, string path,
		bool isPublic, bool isSafe, Func<PionexPreparedRequest> prepare,
		CancellationToken cancellationToken)
		where TData : class
	{
		for (var attempt = 1; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			var prepared = prepare();
			var relative = path + (prepared.Query.IsEmpty() ? string.Empty : "?" + prepared.Query);
			using var request = new HttpRequestMessage(method, new Uri(_endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (!isPublic)
			{
				request.Headers.TryAddWithoutValidation("PIONEX-KEY", _apiKey);
				request.Headers.TryAddWithoutValidation("PIONEX-SIGNATURE", prepared.Signature);
			}
			if (prepared.Body is not null)
				request.Content = new StringContent(prepared.Body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException error) when (isSafe && attempt < _maxAttempts)
			{
				this.AddWarningLog("Pionex {0} transport error. Retrying read request: {1}",
					relative, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(CreateTransportError(relative, isSafe, error.Message), error);
			}

			using (response)
			{
				var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
				if (isSafe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateHttpError(response.StatusCode, relative, responseBody, isSafe);

				PionexResponse<TData> result;
				try
				{
					result = JsonConvert.DeserializeObject<PionexResponse<TData>>(responseBody, _jsonSettings)
						?? throw new InvalidDataException($"Pionex {relative} returned no JSON value.");
				}
				catch (JsonException error)
				{
					throw new InvalidDataException($"Pionex {relative} returned invalid JSON.", error);
				}
				if (!result.IsSuccess)
					throw new InvalidOperationException($"Pionex {relative} failed (code {result.Code}): " +
						$"{result.Message}. " + (isSafe ? "The request was read-only." :
						"The write was not retried; inspect exchange state before retrying."));
				return result;
			}
		}
	}

	private async ValueTask WaitForRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(110);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static PionexParameter[] AddTimestamp(PionexParameter[] parameters)
		=> [.. parameters, new("timestamp",
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))];

	private static PionexQuery BuildQuery(IEnumerable<PionexParameter> parameters)
	{
		var ordered = parameters
			.Where(static parameter => !parameter.Value.IsEmpty())
			.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)
			.ToArray();
		return new(
			ordered.Select(static parameter => parameter.Name + "=" + parameter.Value).Join("&"),
			ordered.Select(static parameter => Escape(parameter.Name) + "=" + Escape(parameter.Value)).Join("&"));
	}

	private string Sign(string payload)
	{
		byte[] hash;
		using (_signSync.EnterScope())
			hash = _hasher.ComputeHash(payload.UTF8());
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException("Pionex API key and secret are required for private requests.");
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan delay && delay > TimeSpan.Zero)
			return delay.Min(TimeSpan.FromSeconds(30));
		return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
	}

	private static Exception CreateHttpError(HttpStatusCode status, string path, string body, bool isSafe)
		=> new InvalidOperationException($"Pionex {path} returned HTTP {(int)status}: {body}. " +
			(isSafe ? "The read request failed." :
			"The write was not retried; inspect exchange state before retrying."));

	private static string CreateTransportError(string path, bool isSafe, string message)
		=> $"Pionex {path} transport error: {message}. " +
			(isSafe ? "The read request failed." :
			"The write may have reached Pionex; inspect exchange state before retrying.");

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
