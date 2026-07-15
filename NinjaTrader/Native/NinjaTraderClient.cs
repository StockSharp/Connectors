namespace StockSharp.NinjaTrader.Native;

sealed class NinjaTraderClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _baseUrl;
	private readonly string _login;
	private readonly SecureString _password;
	private readonly string _appId;
	private readonly string _appVersion;
	private readonly string _deviceId;
	private readonly string _clientId;
	private readonly SecureString _secret;
	private SecureString _accessToken;
	private SecureString _marketDataAccessToken;

	public NinjaTraderClient(bool isDemo, string login, SecureString password, string appId, string appVersion, string deviceId, string clientId, SecureString secret)
	{
		_login = login.ThrowIfEmpty(nameof(login));
		_password = password.ThrowIfEmpty(nameof(password));
		_appId = appId.ThrowIfEmpty(nameof(appId));
		_appVersion = appVersion.ThrowIfEmpty(nameof(appVersion));
		_deviceId = deviceId.ThrowIfEmpty(nameof(deviceId));
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_secret = secret.ThrowIfEmpty(nameof(secret));
		_baseUrl = isDemo ? "https://demo.tradovateapi.com/v1/" : "https://live.tradovateapi.com/v1/";
	}

	public override string Name => nameof(NinjaTrader) + "_" + nameof(NinjaTraderClient);

	public long UserId { get; private set; }
	public SecureString AccessToken => _accessToken;
	public SecureString MarketDataAccessToken => _marketDataAccessToken;

	public async Task Authenticate(bool isMarketData, CancellationToken cancellationToken)
	{
		var request = new AccessTokenRequest
		{
			Name = _login,
			Password = _password.UnSecure(),
			AppId = _appId,
			AppVersion = _appVersion,
			DeviceId = _deviceId,
			Cid = _clientId,
			Sec = _secret.UnSecure(),
		};
		var result = await Invoke<AccessTokenResponse>("auth/accesstokenrequest", CreateRequest(Method.Post).AddStringBody(JsonConvert.SerializeObject(request, _jsonSettings), DataFormat.Json), false, cancellationToken);

		if (!result.ErrorText.IsEmpty())
			throw new InvalidOperationException(result.ErrorText);
		if (result.AccessToken.IsEmpty())
			throw new InvalidOperationException("NinjaTrader did not return an access token.");
		if (isMarketData && result.MarketDataAccessToken.IsEmpty())
			throw new InvalidOperationException("NinjaTrader did not return a market-data access token.");

		_accessToken = result.AccessToken.Secure();
		_marketDataAccessToken = result.MarketDataAccessToken?.Secure();
		UserId = result.UserId;
	}

	public Task<NinjaTraderAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> Get<NinjaTraderAccount[]>("account/list", cancellationToken);

	public Task<NinjaTraderContract[]> SuggestContracts(string text, int limit, CancellationToken cancellationToken)
		=> Get<NinjaTraderContract[]>("contract/suggest", cancellationToken, request => request.AddQueryParameter("t", text ?? string.Empty).AddQueryParameter("l", limit));

	public Task<NinjaTraderContract> FindContract(string symbol, CancellationToken cancellationToken)
		=> Get<NinjaTraderContract>("contract/find", cancellationToken, request => request.AddQueryParameter("name", symbol));

	public Task<NinjaTraderContract> GetContract(long id, CancellationToken cancellationToken)
		=> Get<NinjaTraderContract>("contract/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<NinjaTraderContractMaturity> GetContractMaturity(long id, CancellationToken cancellationToken)
		=> Get<NinjaTraderContractMaturity>("contractMaturity/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<NinjaTraderProduct> GetProduct(long id, CancellationToken cancellationToken)
		=> Get<NinjaTraderProduct>("product/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<NinjaTraderExchange> GetExchange(long id, CancellationToken cancellationToken)
		=> Get<NinjaTraderExchange>("exchange/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<NinjaTraderOrder[]> GetOrders(CancellationToken cancellationToken)
		=> Get<NinjaTraderOrder[]>("order/list", cancellationToken);

	public Task<NinjaTraderOrderVersion[]> GetOrderVersions(CancellationToken cancellationToken)
		=> Get<NinjaTraderOrderVersion[]>("orderVersion/list", cancellationToken);

	public Task<NinjaTraderFill[]> GetFills(CancellationToken cancellationToken)
		=> Get<NinjaTraderFill[]>("fill/list", cancellationToken);

	public Task<NinjaTraderPosition[]> GetPositions(CancellationToken cancellationToken)
		=> Get<NinjaTraderPosition[]>("position/list", cancellationToken);

	public Task<NinjaTraderCashBalance[]> GetCashBalances(CancellationToken cancellationToken)
		=> Get<NinjaTraderCashBalance[]>("cashBalance/list", cancellationToken);

	public async Task<long> PlaceOrder(PlaceOrderRequest order, CancellationToken cancellationToken)
	{
		var result = await Post<PlaceOrderResult>("order/placeorder", order, cancellationToken);
		result.ThrowIfError();
		return result.OrderId ?? throw new InvalidOperationException("NinjaTrader did not return an order identifier.");
	}

	public async Task ModifyOrder(ModifyOrderRequest order, CancellationToken cancellationToken)
	{
		var result = await Post<CommandResult>("order/modifyorder", order, cancellationToken);
		result.ThrowIfError();
	}

	public async Task CancelOrder(CancelOrderRequest order, CancellationToken cancellationToken)
	{
		var result = await Post<CommandResult>("order/cancelorder", order, cancellationToken);
		result.ThrowIfError();
	}

	private Task<T> Get<T>(string path, CancellationToken cancellationToken, Action<RestRequest> configure = null)
	{
		var request = CreateRequest(Method.Get);
		configure?.Invoke(request);
		return Invoke<T>(path, request, true, cancellationToken);
	}

	private Task<T> Post<T>(string path, object body, CancellationToken cancellationToken)
		=> Invoke<T>(path, CreateRequest(Method.Post).AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json), true, cancellationToken);

	private RestRequest CreateRequest(Method method)
		=> new((string)null, method);

	private Task<T> Invoke<T>(string path, RestRequest request, bool isAuthorized, CancellationToken cancellationToken)
	{
		if (isAuthorized)
			request.SetBearer(_accessToken ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk));
		return request.InvokeAsync<T>(new Uri(new Uri(_baseUrl), path), this, this.AddVerboseLog, cancellationToken);
	}
}
