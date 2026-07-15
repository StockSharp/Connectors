namespace StockSharp.TastyTrade.Native;

sealed class TastyTradeClient : Disposable
{
	private const string _ordersVersion = "20260427";
	private readonly HttpClient _httpClient;
	private readonly SecureString _refreshToken;
	private readonly SecureString _clientSecret;
	private readonly TastyTradeScopes _scopes;
	private readonly SemaphoreSlim _tokenSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		Converters = { new StringEnumConverter() },
	};
	private string _accessToken;
	private DateTime _accessTokenExpiresAt;

	public TastyTradeClient(bool isDemo, SecureString refreshToken, SecureString clientSecret, TastyTradeScopes scopes)
	{
		_refreshToken = refreshToken;
		_clientSecret = clientSecret;
		_scopes = scopes;
		_httpClient = new()
		{
			BaseAddress = new Uri(isDemo ? "https://api.cert.tastyworks.com/" : "https://api.tastyworks.com/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public string AccountStreamerUrl(bool isDemo)
		=> isDemo ? "wss://streamer.cert.tastyworks.com" : "wss://streamer.tastyworks.com";

	public async Task<string> GetAuthorization(CancellationToken cancellationToken)
		=> $"Bearer {await GetAccessToken(cancellationToken)}";

	public Task<TastyAccountAuthority[]> GetAccounts(CancellationToken cancellationToken)
		=> GetItems<TastyAccountAuthority>("customers/me/accounts", false, cancellationToken);

	public Task<TastyBalance> GetBalance(string accountNumber, CancellationToken cancellationToken)
		=> GetData<TastyBalance>($"accounts/{accountNumber.DataEscape()}/balances", false, cancellationToken);

	public Task<TastyPosition[]> GetPositions(string accountNumber, CancellationToken cancellationToken)
		=> GetItems<TastyPosition>($"accounts/{accountNumber.DataEscape()}/positions", false, cancellationToken);

	public Task<TastyOrder[]> GetOrders(string accountNumber, CancellationToken cancellationToken)
		=> GetItems<TastyOrder>($"accounts/{accountNumber.DataEscape()}/orders", true, cancellationToken);

	public Task<TastySymbol[]> SearchSymbols(string text, CancellationToken cancellationToken)
		=> GetArray<TastySymbol>($"symbols/search/{text.DataEscape()}", false, cancellationToken);

	public Task<TastyQuoteToken> GetQuoteToken(CancellationToken cancellationToken)
		=> GetData<TastyQuoteToken>("api-quote-tokens", false, cancellationToken);

	public async Task<TastyInstrument> GetInstrument(string symbol, SecurityTypes? securityType, CancellationToken cancellationToken)
	{
		var path = securityType switch
		{
			SecurityTypes.Option => symbol.StartsWith("./", StringComparison.Ordinal) ? "instruments/future-options/" : "instruments/equity-options/",
			SecurityTypes.Future => "instruments/futures/",
			SecurityTypes.CryptoCurrency => "instruments/cryptocurrencies/",
			_ => "instruments/equities/",
		};
		return await GetData<TastyDerivativeInstrument>(path + symbol.DataEscape(), false, cancellationToken);
	}

	public async Task<TastyOrder> PlaceOrder(string accountNumber, TastyOrderRequest order, CancellationToken cancellationToken)
	{
		var result = await SendData<TastyPlacedOrder>(HttpMethod.Post, $"accounts/{accountNumber.DataEscape()}/orders", order, true, cancellationToken);
		ThrowNotices(result?.Errors);
		return result?.Order ?? throw new InvalidOperationException("tastytrade did not return the placed order.");
	}

	public async Task<TastyOrder> ReplaceOrder(string accountNumber, string orderId, TastyOrderReplaceRequest order, CancellationToken cancellationToken)
	{
		return await SendData<TastyOrder>(HttpMethod.Put, $"accounts/{accountNumber.DataEscape()}/orders/{orderId.DataEscape()}", order, true, cancellationToken)
			?? throw new InvalidOperationException("tastytrade did not return the replacement order.");
	}

	public Task CancelOrder(string accountNumber, string orderId, CancellationToken cancellationToken)
		=> Send(HttpMethod.Delete, $"accounts/{accountNumber.DataEscape()}/orders/{orderId.DataEscape()}", null, true, cancellationToken);

	private Task<T> GetData<T>(string path, bool isOrdersApi, CancellationToken cancellationToken)
		=> SendData<T>(HttpMethod.Get, path, null, isOrdersApi, cancellationToken);

	private async Task<T[]> GetItems<T>(string path, bool isOrdersApi, CancellationToken cancellationToken)
		=> (await SendData<TastyItems<T>>(HttpMethod.Get, path, null, isOrdersApi, cancellationToken))?.Items ?? [];

	private async Task<T[]> GetArray<T>(string path, bool isOrdersApi, CancellationToken cancellationToken)
	{
		var text = await Send(HttpMethod.Get, path, null, isOrdersApi, cancellationToken);
		try
		{
			return JsonConvert.DeserializeObject<TastyDataResponse<T[]>>(text, _jsonSettings)?.Data ?? [];
		}
		catch (JsonSerializationException)
		{
			return JsonConvert.DeserializeObject<TastyDataResponse<TastyItems<T>>>(text, _jsonSettings)?.Data?.Items ?? [];
		}
	}

	private async Task<T> SendData<T>(HttpMethod method, string path, object body, bool isOrdersApi, CancellationToken cancellationToken)
	{
		var text = await Send(method, path, body, isOrdersApi, cancellationToken);
		return text.IsEmpty() ? default : JsonConvert.DeserializeObject<TastyDataResponse<T>>(text, _jsonSettings).Data;
	}

	private async Task<string> Send(HttpMethod method, string path, object body, bool isOrdersApi, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessToken(cancellationToken));
		if (isOrdersApi)
			request.Headers.TryAddWithoutValidation("Accept-Version", _ordersVersion);
		if (body is not null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");

		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var error = text.IsEmpty() ? null : JsonConvert.DeserializeObject<TastyErrorResponse>(text, _jsonSettings);
			var details = error?.Error?.Message ?? error?.Errors?.Select(e => e.Message).Where(m => !m.IsEmpty()).Join("; ");
			throw new HttpRequestException(details.IsEmpty($"tastytrade request failed with HTTP {(int)response.StatusCode}.") , null, response.StatusCode);
		}
		return text;
	}

	private async Task<string> GetAccessToken(CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty() && DateTime.UtcNow < _accessTokenExpiresAt)
			return _accessToken;

		await _tokenSync.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() && DateTime.UtcNow < _accessTokenExpiresAt)
				return _accessToken;

			var requestBody = new TastyTokenRequest
			{
				RefreshToken = _refreshToken.UnSecure(),
				ClientSecret = _clientSecret.UnSecure(),
				Scope = _scopes.ToScope(),
				GrantType = "refresh_token",
			};
			using var request = new HttpRequestMessage(HttpMethod.Post, "oauth/token")
			{
				Content = new StringContent(JsonConvert.SerializeObject(requestBody, _jsonSettings), Encoding.UTF8, "application/json"),
			};
			using var response = await _httpClient.SendAsync(request, cancellationToken);
			var text = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"tastytrade OAuth request failed with HTTP {(int)response.StatusCode}: {text}", null, response.StatusCode);
			var token = JsonConvert.DeserializeObject<TastyTokenResponse>(text, _jsonSettings);
			_accessToken = token?.AccessToken.ThrowIfEmpty(nameof(TastyTokenResponse.AccessToken));
			_accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn - 30));
			return _accessToken;
		}
		finally
		{
			_tokenSync.Release();
		}
	}

	private static void ThrowNotices(TastyError[] errors)
	{
		var message = errors?.Select(e => e.Message).Where(m => !m.IsEmpty()).Join("; ");
		if (!message.IsEmpty())
			throw new InvalidOperationException(message);
	}

	protected override void DisposeManaged()
	{
		_tokenSync.Dispose();
		_httpClient.Dispose();
		base.DisposeManaged();
	}
}
