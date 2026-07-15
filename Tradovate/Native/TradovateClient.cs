namespace StockSharp.Tradovate.Native;

sealed class TradovateClient : BaseLogReceiver
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

	public TradovateClient(bool isDemo, string login, SecureString password, string appId, string appVersion, string deviceId, string clientId, SecureString secret)
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

	public override string Name => nameof(Tradovate) + "_" + nameof(TradovateClient);

	public long UserId { get; private set; }
	public SecureString AccessToken => _accessToken;

	public async Task Authenticate(CancellationToken cancellationToken)
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
			throw new InvalidOperationException("Tradovate did not return an access token.");

		_accessToken = result.AccessToken.Secure();
		UserId = result.UserId;
	}

	public Task<TradovateAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> Get<TradovateAccount[]>("account/list", cancellationToken);

	public Task<TradovateContract[]> SuggestContracts(string text, int limit, CancellationToken cancellationToken)
		=> Get<TradovateContract[]>("contract/suggest", cancellationToken, request => request.AddQueryParameter("t", text ?? string.Empty).AddQueryParameter("l", limit));

	public Task<TradovateContract> FindContract(string symbol, CancellationToken cancellationToken)
		=> Get<TradovateContract>("contract/find", cancellationToken, request => request.AddQueryParameter("name", symbol));

	public Task<TradovateContract> GetContract(long id, CancellationToken cancellationToken)
		=> Get<TradovateContract>("contract/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<TradovateContractMaturity> GetContractMaturity(long id, CancellationToken cancellationToken)
		=> Get<TradovateContractMaturity>("contractMaturity/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<TradovateProduct> GetProduct(long id, CancellationToken cancellationToken)
		=> Get<TradovateProduct>("product/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<TradovateExchange> GetExchange(long id, CancellationToken cancellationToken)
		=> Get<TradovateExchange>("exchange/item", cancellationToken, request => request.AddQueryParameter("id", id));

	public Task<TradovateOrder[]> GetOrders(CancellationToken cancellationToken)
		=> Get<TradovateOrder[]>("order/list", cancellationToken);

	public Task<TradovateOrderVersion[]> GetOrderVersions(CancellationToken cancellationToken)
		=> Get<TradovateOrderVersion[]>("orderVersion/list", cancellationToken);

	public Task<TradovateFill[]> GetFills(CancellationToken cancellationToken)
		=> Get<TradovateFill[]>("fill/list", cancellationToken);

	public Task<TradovatePosition[]> GetPositions(CancellationToken cancellationToken)
		=> Get<TradovatePosition[]>("position/list", cancellationToken);

	public Task<TradovateCashBalance[]> GetCashBalances(CancellationToken cancellationToken)
		=> Get<TradovateCashBalance[]>("cashBalance/list", cancellationToken);

	public async Task<long> PlaceOrder(PlaceOrderRequest order, CancellationToken cancellationToken)
	{
		var result = await Post<PlaceOrderResult>("order/placeorder", order, cancellationToken);
		result.ThrowIfError();
		return result.OrderId ?? throw new InvalidOperationException("Tradovate did not return an order identifier.");
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
