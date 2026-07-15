namespace StockSharp.Public.Native;

sealed class PublicClient : Disposable
{
	private readonly HttpClient _httpClient;
	private readonly string _secret;
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		Converters = { new StringEnumConverter() },
	};
	private DateTime _tokenExpiresAt;
	private DateTime _lastRequestAt;

	public PublicClient(SecureString secret)
	{
		_secret = secret.UnSecure();
		_httpClient = new()
		{
			BaseAddress = new Uri("https://api.public.com/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public async Task Authenticate(CancellationToken cancellationToken)
	{
		if (_tokenExpiresAt > DateTime.UtcNow)
			return;

		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (_tokenExpiresAt > DateTime.UtcNow)
				return;

			using var request = new HttpRequestMessage(HttpMethod.Post, "userapiauthservice/personal/access-tokens")
			{
				Content = CreateContent(new PublicAccessTokenRequest
				{
					Secret = _secret,
					ValidityInMinutes = 15,
				}),
			};
			await WaitRateLimit(cancellationToken);
			using var response = await _httpClient.SendAsync(request, cancellationToken);
			var token = await Read<PublicAccessTokenResponse>(response, cancellationToken);
			if (token?.AccessToken.IsEmpty() != false)
				throw new InvalidOperationException("Public.com did not return an access token.");

			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
			_tokenExpiresAt = DateTime.UtcNow.AddMinutes(10);
		}
		finally
		{
			_authSync.Release();
		}
	}

	public async Task<PublicAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> (await Get<PublicAccountsResponse>("userapigateway/trading/account", cancellationToken))?.Accounts ?? [];

	public Task<PublicPortfolio> GetPortfolio(string accountId, CancellationToken cancellationToken)
		=> Get<PublicPortfolio>($"userapigateway/trading/{Escape(accountId)}/portfolio/v2", cancellationToken);

	public async Task<PublicInstrument[]> GetInstruments(IEnumerable<PublicInstrumentTypes> types, CancellationToken cancellationToken)
	{
		var filters = types?.Distinct().Select(t => $"typeFilter={Escape(ToText(t))}").ToArray() ?? [];
		var path = "userapigateway/trading/instruments";
		if (filters.Length > 0)
			path += $"?{filters.Join("&")}";
		return (await Get<PublicInstrumentsResponse>(path, cancellationToken))?.Instruments ?? [];
	}

	public Task<PublicInstrument> GetInstrument(string symbol, PublicInstrumentTypes type, CancellationToken cancellationToken)
		=> Get<PublicInstrument>($"userapigateway/trading/instruments/{Escape(symbol)}/{Escape(ToText(type))}", cancellationToken);

	public async Task<PublicQuote[]> GetQuotes(string accountId, PublicInstrumentKey[] instruments, CancellationToken cancellationToken)
		=> (await Post<PublicQuotesRequest, PublicQuotesResponse>($"userapigateway/marketdata/{Escape(accountId)}/quotes", new()
		{
			Instruments = instruments,
		}, cancellationToken))?.Quotes ?? [];

	public Task<PublicOptionExpirationsResponse> GetOptionExpirations(string accountId, string symbol, CancellationToken cancellationToken)
		=> Post<PublicOptionExpirationsRequest, PublicOptionExpirationsResponse>($"userapigateway/marketdata/{Escape(accountId)}/option-expirations", new()
		{
			Instrument = new() { Symbol = symbol, Type = PublicInstrumentTypes.Equity },
		}, cancellationToken);

	public Task<PublicOptionChainResponse> GetOptionChain(string accountId, string symbol, DateTime expiration, CancellationToken cancellationToken)
		=> Post<PublicOptionChainRequest, PublicOptionChainResponse>($"userapigateway/marketdata/{Escape(accountId)}/option-chain", new()
		{
			Instrument = new() { Symbol = symbol, Type = PublicInstrumentTypes.Equity },
			ExpirationDate = expiration.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		}, cancellationToken);

	public Task<PublicBarsResponse> GetBars(string symbol, PublicInstrumentTypes type, PublicBarPeriods period, PublicBarAggregations aggregation, PublicTradingSessionToggles sessions, CancellationToken cancellationToken)
		=> Get<PublicBarsResponse>($"userapigateway/historicdata/{Escape(ToText(type))}/{Escape(symbol)}/{Escape(ToText(period))}/{Escape(ToText(aggregation))}?tradingSessionToggle={Escape(ToText(sessions))}", cancellationToken);

	public async Task<string> PlaceOrder(string accountId, PublicOrderRequest order, CancellationToken cancellationToken)
		=> (await Post<PublicOrderRequest, PublicOrderResult>($"userapigateway/trading/{Escape(accountId)}/order", order, cancellationToken))?.OrderId
			?? throw new InvalidOperationException("Public.com did not return an order identifier.");

	public async Task<string> PlaceMultiLegOrder(string accountId, PublicMultiLegOrderRequest order, CancellationToken cancellationToken)
		=> (await Post<PublicMultiLegOrderRequest, PublicOrderResult>($"userapigateway/trading/{Escape(accountId)}/order/multileg", order, cancellationToken))?.OrderId
			?? throw new InvalidOperationException("Public.com did not return an order identifier.");

	public async Task<string> ReplaceOrder(string accountId, PublicReplaceOrderRequest order, CancellationToken cancellationToken)
		=> (await Put<PublicReplaceOrderRequest, PublicOrderResult>($"userapigateway/trading/{Escape(accountId)}/order", order, cancellationToken))?.OrderId
			?? throw new InvalidOperationException("Public.com did not return a replacement order identifier.");

	public Task<PublicOrder> GetOrder(string accountId, string orderId, CancellationToken cancellationToken)
		=> Get<PublicOrder>($"userapigateway/trading/{Escape(accountId)}/order/{Escape(orderId)}", cancellationToken);

	public Task CancelOrder(string accountId, string orderId, CancellationToken cancellationToken)
		=> Delete($"userapigateway/trading/{Escape(accountId)}/order/{Escape(orderId)}", cancellationToken);

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
		=> Send<TRequest, TResponse>(HttpMethod.Post, path, body, cancellationToken);

	private Task<TResponse> Put<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
		=> Send<TRequest, TResponse>(HttpMethod.Put, path, body, cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, CancellationToken cancellationToken)
	{
		await Authenticate(cancellationToken);
		await WaitRateLimit(cancellationToken);
		using var request = new HttpRequestMessage(method, path);
		using var response = await _httpClient.SendAsync(request, cancellationToken);
		return await Read<T>(response, cancellationToken);
	}

	private async Task<TResponse> Send<TRequest, TResponse>(HttpMethod method, string path, TRequest body, CancellationToken cancellationToken)
	{
		await Authenticate(cancellationToken);
		await WaitRateLimit(cancellationToken);
		using var request = new HttpRequestMessage(method, path) { Content = CreateContent(body) };
		using var response = await _httpClient.SendAsync(request, cancellationToken);
		return await Read<TResponse>(response, cancellationToken);
	}

	private async Task Delete(string path, CancellationToken cancellationToken)
	{
		await Authenticate(cancellationToken);
		await WaitRateLimit(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete, path);
		using var response = await _httpClient.SendAsync(request, cancellationToken);
		await Read<object>(response, cancellationToken);
	}

	private StringContent CreateContent<T>(T body)
		=> new(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");

	private async Task<T> Read<T>(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var error = text.IsEmpty() ? null : JsonConvert.DeserializeObject<PublicError>(text, _jsonSettings);
			throw new HttpRequestException(error?.Message.IsEmpty(error.Error).IsEmpty(error?.Code) ?? $"Public.com request failed with HTTP {(int)response.StatusCode}.", null, response.StatusCode);
		}
		return text.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(text, _jsonSettings);
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private async Task WaitRateLimit(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = TimeSpan.FromMilliseconds(100) - (DateTime.UtcNow - _lastRequestAt);
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_lastRequestAt = DateTime.UtcNow;
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private string ToText<T>(T value)
		=> JsonConvert.SerializeObject(value, _jsonSettings).Trim('"');

	protected override void DisposeManaged()
	{
		_authSync.Dispose();
		_rateSync.Dispose();
		_httpClient.Dispose();
		base.DisposeManaged();
	}
}
