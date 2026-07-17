namespace StockSharp.OpenMarkets.Native;

sealed class OpenMarketsClient : Disposable
{
	public const string OmsScope = "oms-api";
	public const string MarketDataScope = "market-data-api";
	public const string OmsStreamScope = "oms-streams-api";
	public const string MarketDataStreamScope = "md-streams-api";

	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly HttpClient _identity;
	private readonly HttpClient _oms;
	private readonly HttpClient _marketData;
	private readonly SemaphoreSlim _tokenSync = new(1, 1);
	private readonly SynchronizedDictionary<string, OpenMarketsTokenCache> _tokens =
		new(StringComparer.Ordinal);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public OpenMarketsClient(bool isTest, string clientId, string clientSecret)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret));

		_identity = CreateClient(isTest
			? "https://stage-identity.openmarkets.com.au/"
			: "https://identity.openmarkets.com.au/");
		_oms = CreateClient(isTest
			? "https://test-oms-api.openmarkets.com.au/"
			: "https://oms-api.openmarkets.com.au/");
		_marketData = CreateClient(isTest
			? "https://test-market-data-api.openmarkets.com.au/"
			: "https://market-data-api.openmarkets.com.au/");
	}

	public string GetMarketStreamAddress(bool isTest)
		=> isTest
			? "https://test-md-streams-api.openmarkets.com.au/streams"
			: "https://md-streams-api.openmarkets.com.au/streams";

	public string GetOmsStreamAddress(bool isTest)
		=> isTest
			? "https://test-oms-streams-api.openmarkets.com.au/streams"
			: "https://oms-streams-api.openmarkets.com.au/streams";

	public Task<OpenMarketsAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> Get<OpenMarketsAccount[]>(_oms, OmsScope, "accounts/v1", [], cancellationToken);

	public Task<OpenMarketsPortfolioLink[]> GetPortfolioLinks(CancellationToken cancellationToken)
		=> Get<OpenMarketsPortfolioLink[]>(_oms, OmsScope, "accounts/portfoliolinks/v1", [],
			cancellationToken);

	public Task<OpenMarketsPortfolioDetail[]> GetPortfolioDetails(string[] portfolioCodes,
		CancellationToken cancellationToken)
		=> Post<OpenMarketsPortfolioRequest, OpenMarketsPortfolioDetail[]>(_oms, OmsScope,
			"portfolios/details/by-portfolio/v2", new()
			{
				PortfolioCodes = portfolioCodes,
				IsIncludingPortfoliosWithSameCashAccount = false,
			}, true, cancellationToken);

	public Task<OpenMarketsPortfolioCash[]> GetPortfolioCash(string[] portfolioCodes,
		CancellationToken cancellationToken)
		=> Post<OpenMarketsPortfolioRequest, OpenMarketsPortfolioCash[]>(_oms, OmsScope,
			"portfolios/cash/by-portfolio/v2", new() { PortfolioCodes = portfolioCodes }, true,
			cancellationToken);

	public Task<OpenMarketsPortfolioPosition[]> GetPortfolioPositions(string[] portfolioCodes,
		CancellationToken cancellationToken)
		=> Post<OpenMarketsPortfolioRequest, OpenMarketsPortfolioPosition[]>(_oms, OmsScope,
			"portfolios/positions/v2", new() { PortfolioCodes = portfolioCodes }, true,
			cancellationToken);

	public Task<OpenMarketsOrder[]> GetOrders(string accountCode,
		CancellationToken cancellationToken)
		=> Get<OpenMarketsOrder[]>(_oms, OmsScope,
			$"orders/by-account/{Escape(accountCode)}/v4",
			[
				new("orderFilter", "AllOrdersForTheLast3Days"),
				new("includeChildOrders", "true"),
			], cancellationToken);

	public Task<OpenMarketsTrade[]> GetTrades(string accountCode, DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> Get<OpenMarketsTrade[]>(_oms, OmsScope,
			$"trades/by-account/{Escape(accountCode)}/v1",
			[
				new("dateTimeFrom", ToQueryTime(from)),
				new("dateTimeTo", ToQueryTime(to)),
			], cancellationToken);

	public Task<OpenMarketsCreatedOrder> PlaceOrder(OpenMarketsCreateOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<OpenMarketsCreateOrderRequest, OpenMarketsCreatedOrder>(_oms, OmsScope,
			"orders/v4", request, false, cancellationToken);

	public Task AmendOrder(long orderNumber, OpenMarketsAmendOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<OpenMarketsAmendOrderRequest, OpenMarketsNoContent>(_oms, OmsScope,
			HttpMethod.Put, $"orders/{orderNumber}/v4", request, [], false, cancellationToken);

	public Task CancelOrder(long orderNumber, CancellationToken cancellationToken)
		=> Send<OpenMarketsNoContent, OpenMarketsNoContent>(_oms, OmsScope,
			HttpMethod.Delete, $"orders/{orderNumber}/v1", null, [], false, cancellationToken);

	public Task<OpenMarketsSecurity[]> GetSecurities(string exchange,
		CancellationToken cancellationToken)
		=> Get<OpenMarketsSecurity[]>(_marketData, MarketDataScope, "securities/v1",
			exchange.IsEmpty() ? [] : [new("exchanges", exchange)], cancellationToken);

	public Task<OpenMarketsSecurityInformation[]> GetSecurityInformation(string[] securities,
		CancellationToken cancellationToken)
		=> Post<OpenMarketsSecurityInformationRequest, OpenMarketsSecurityInformation[]>(
			_marketData, MarketDataScope, "securities/information/v2",
			new() { Securities = securities }, true, cancellationToken);

	public Task<OpenMarketsQuotesResponse> GetQuotes(string[] securities,
		CancellationToken cancellationToken)
		=> Post<OpenMarketsQuotesRequest, OpenMarketsQuotesResponse>(_marketData, MarketDataScope,
			"pricing/quotes/v3", new() { Securities = securities }, true, cancellationToken);

	public Task<OpenMarketsDepth[]> GetDepth(string security, int depth,
		CancellationToken cancellationToken)
		=> Get<OpenMarketsDepth[]>(_marketData, MarketDataScope,
			$"pricing/depth/{Escape(security)}/v1",
			[
				new("askLevelMax", depth.ToString(CultureInfo.InvariantCulture)),
				new("bidLevelMax", depth.ToString(CultureInfo.InvariantCulture)),
				new("groupBy", "Price"),
			], cancellationToken);

	public Task<OpenMarketsTimeSeries[]> GetTimeSeries(string security, string frequency,
		DateTime from, DateTime to, CancellationToken cancellationToken)
		=> Get<OpenMarketsTimeSeries[]>(_marketData, MarketDataScope,
			$"pricing/timeseries/{Escape(security)}/v2",
			[
				new("frequency", frequency),
				new("dateFrom", ToQueryTime(from)),
				new("dateTo", ToQueryTime(to)),
			], cancellationToken);

	public Task<OpenMarketsIntradayTimeSeries[]> GetIntradayTimeSeries(string security,
		int interval, DateTime from, DateTime to, CancellationToken cancellationToken)
		=> Get<OpenMarketsIntradayTimeSeries[]>(_marketData, MarketDataScope,
			$"pricing/timeseriesintraday/{Escape(security)}/v1",
			[
				new("frequency", "Minutes"),
				new("consolidationInterval", interval.ToString(CultureInfo.InvariantCulture)),
				new("dateTimeFrom", ToQueryTime(from)),
				new("dateTimeTo", ToQueryTime(to)),
			], cancellationToken);

	public Task<OpenMarketsMarketTrade[]> GetMarketTrades(string security, DateTime from,
		DateTime to, CancellationToken cancellationToken)
		=> Post<OpenMarketsMarketTradeRequest, OpenMarketsMarketTrade[]>(_marketData,
			MarketDataScope, $"pricing/trades/{Escape(security)}/v2",
			new()
			{
				DateTimeFrom = from,
				DateTimeTo = to,
			}, true, cancellationToken);

	public async Task<string> GetAccessToken(string scope, CancellationToken cancellationToken)
	{
		if (_tokens.TryGetValue(scope, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
			return cached.AccessToken;

		await _tokenSync.WaitAsync(cancellationToken);
		try
		{
			if (_tokens.TryGetValue(scope, out cached) && cached.ExpiresAt > DateTime.UtcNow)
				return cached.AccessToken;

			var token = await RequestToken(new()
			{
				GrantType = "client_credentials",
				Scope = scope,
			}, cancellationToken);
			if (token?.AccessToken.IsEmpty() != false)
				throw new InvalidOperationException("OpenMarkets returned an empty OAuth access token.");

			cached = new()
			{
				AccessToken = token.AccessToken,
				ExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn - 60)),
			};
			_tokens[scope] = cached;
			return cached.AccessToken;
		}
		finally
		{
			_tokenSync.Release();
		}
	}

	private async Task<OpenMarketsTokenResponse> RequestToken(OpenMarketsTokenRequest request,
		CancellationToken cancellationToken)
	{
		using var message = new HttpRequestMessage(HttpMethod.Post, "connect/token");
		message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
			Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_clientId}:{_clientSecret}")));
		var form = $"grant_type={Encode(request.GrantType)}&scope={Encode(request.Scope)}";
		message.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");

		using var response = await _identity.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response, content);
		return JsonConvert.DeserializeObject<OpenMarketsTokenResponse>(content, _jsonSettings);
	}

	private Task<TResponse> Get<TResponse>(HttpClient client, string scope, string path,
		OpenMarketsQueryParameter[] query, CancellationToken cancellationToken)
		where TResponse : class
		=> Send<OpenMarketsNoContent, TResponse>(client, scope, HttpMethod.Get, path, null, query,
			true, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(HttpClient client, string scope, string path,
		TRequest request, bool isRetryable, CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
		=> Send<TRequest, TResponse>(client, scope, HttpMethod.Post, path, request, [], isRetryable,
			cancellationToken);

	private async Task<TResponse> Send<TRequest, TResponse>(HttpClient client, string scope,
		HttpMethod method, string path, TRequest requestBody, OpenMarketsQueryParameter[] query,
		bool isRetryable, CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
	{
		var body = requestBody == null ? null : JsonConvert.SerializeObject(requestBody, _jsonSettings);
		for (var attempt = 1; ; attempt++)
		{
			using var message = new HttpRequestMessage(method, BuildPath(path, query));
			message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
				await GetAccessToken(scope, cancellationToken));
			message.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString("D"));
			if (body != null)
				message.Content = new StringContent(body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (Exception error) when (isRetryable && attempt < 3 &&
				error is HttpRequestException or TaskCanceledException &&
				!cancellationToken.IsCancellationRequested)
			{
				await Delay(attempt, null, cancellationToken);
				continue;
			}

			using (response)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
					return content.IsEmpty()
						? null
						: JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings);

				if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
				{
					_tokens.Remove(scope);
					continue;
				}
				if (isRetryable && attempt < 3 && IsTransient(response.StatusCode))
				{
					await Delay(attempt, GetRetryDelay(response), cancellationToken);
					continue;
				}
				throw CreateException(response, content);
			}
		}
	}

	private static HttpClient CreateClient(string address)
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri(address),
			Timeout = TimeSpan.FromSeconds(60),
		};
		client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
		client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-OpenMarkets");
		return client;
	}

	private static string BuildPath(string path, OpenMarketsQueryParameter[] query)
	{
		var parameters = (query ?? [])
			.Where(parameter => parameter != null && !parameter.Name.IsEmpty() && parameter.Value != null)
			.Select(parameter => $"{Encode(parameter.Name)}={Encode(parameter.Value)}")
			.ToArray();
		return parameters.Length == 0 ? path : $"{path}?{parameters.Join("&")}";
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout || (int)statusCode == 429 ||
			(int)statusCode >= 500;

	private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
	{
		if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
			return delta;
		return null;
	}

	private static Task Delay(int attempt, TimeSpan? requested,
		CancellationToken cancellationToken)
	{
		var delay = requested ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
		if (delay < TimeSpan.FromMilliseconds(250))
			delay = TimeSpan.FromMilliseconds(250);
		if (delay > TimeSpan.FromSeconds(30))
			delay = TimeSpan.FromSeconds(30);
		return Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateException(HttpResponseMessage response, string content)
	{
		OpenMarketsErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<OpenMarketsErrorResponse>(content);
		}
		catch (JsonException)
		{
		}

		var detail = error?.Detail.IsEmpty(error?.ErrorDescription)
			.IsEmpty(error?.Message).IsEmpty(error?.Title).IsEmpty(error?.Error)
			.IsEmpty(response.ReasonPhrase);
		var requestId = response.Headers.TryGetValues("X-Request-ID", out var values)
			? values.FirstOrDefault()
			: null;
		var suffix = requestId.IsEmpty() ? string.Empty : $" Request ID: {requestId}.";
		return new HttpRequestException(
			$"OpenMarkets HTTP {(int)response.StatusCode}: {detail}.{suffix}", null,
			response.StatusCode);
	}

	private static string ToQueryTime(DateTime value)
		=> value.ToString("O", CultureInfo.InvariantCulture);

	private static string Escape(string value)
		=> Encode(value.ThrowIfEmpty(nameof(value)));

	private static string Encode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_identity.Dispose();
		_oms.Dispose();
		_marketData.Dispose();
		_tokenSync.Dispose();
		base.DisposeManaged();
	}
}
