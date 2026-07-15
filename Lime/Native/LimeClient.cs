namespace StockSharp.Lime.Native;

sealed class LimeClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
		NullValueHandling = NullValueHandling.Ignore,
	};

	private static readonly Uri _authUri = new("https://auth.lime.co/connect/token");
	private static readonly Uri _baseUri = new("https://api.lime.co/");

	private readonly string _login;
	private readonly SecureString _password;
	private readonly string _clientId;
	private readonly SecureString _clientSecret;
	private SecureString _accessToken;

	public LimeClient(string login, SecureString password, string clientId, SecureString clientSecret)
	{
		_login = login.ThrowIfEmpty(nameof(login));
		_password = password.ThrowIfEmpty(nameof(password));
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret));
	}

	public override string Name => nameof(Lime) + "_" + nameof(LimeClient);

	public SecureString AccessToken => _accessToken;

	public async Task Authenticate(CancellationToken cancellationToken)
	{
		var body = new LimeAccessTokenRequest
		{
			GrantType = "password",
			ClientId = _clientId,
			ClientSecret = _clientSecret.UnSecure(),
			Username = _login,
			Password = _password.UnSecure(),
		};
		var request = new RestRequest((string)null, Method.Post)
			.AddParameter("grant_type", body.GrantType)
			.AddParameter("client_id", body.ClientId)
			.AddParameter("client_secret", body.ClientSecret)
			.AddParameter("username", body.Username)
			.AddParameter("password", body.Password);
		var token = await request.InvokeAsync<LimeAccessTokenResponse>(_authUri, this, this.AddVerboseLog, cancellationToken);
		_accessToken = token?.AccessToken.ThrowIfEmpty(nameof(token.AccessToken)).Secure();
	}

	public Task<LimeAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> Get<LimeAccount[]>("accounts", cancellationToken);

	public Task<LimePosition[]> GetPositions(string account, CancellationToken cancellationToken)
		=> Get<LimePosition[]>($"accounts/{account}/positions", cancellationToken);

	public Task<LimeTradesPage> GetTrades(string account, string date, CancellationToken cancellationToken)
		=> Get<LimeTradesPage>($"accounts/{account}/trades/{date}", cancellationToken, request => request
			.AddQueryParameter("limit", 1000)
			.AddQueryParameter("skip", 0));

	public Task<LimeOrder[]> GetActiveOrders(string account, CancellationToken cancellationToken)
		=> Get<LimeOrder[]>($"accounts/{account}/activeorders", cancellationToken);

	public Task<LimeOrder> GetOrder(string orderId, CancellationToken cancellationToken)
		=> Get<LimeOrder>($"orders/{orderId}", cancellationToken);

	public Task<LimeSecuritiesPage> LookupSecurities(string query, int limit, CancellationToken cancellationToken)
		=> Get<LimeSecuritiesPage>("securities", cancellationToken, request => request
			.AddQueryParameter("query", query ?? string.Empty)
			.AddQueryParameter("limit", limit)
			.AddQueryParameter("skip", 0));

	public Task<LimeQuote> GetQuote(string symbol, CancellationToken cancellationToken)
		=> Get<LimeQuote>("marketdata/quote", cancellationToken, request => request.AddQueryParameter("symbol", symbol));

	public Task<LimeQuoteHistory[]> GetHistory(string symbol, LimePeriods period, DateTime from, DateTime to, CancellationToken cancellationToken)
		=> Get<LimeQuoteHistory[]>("marketdata/history", cancellationToken, request => request
			.AddQueryParameter("symbol", symbol)
			.AddQueryParameter("period", period.ToNative())
			.AddQueryParameter("from", (long)from.ToUniversalTime().ToUnix())
			.AddQueryParameter("to", (long)to.ToUniversalTime().ToUnix()));

	public Task<LimeOptionSeries[]> GetOptionSeries(string symbol, CancellationToken cancellationToken)
		=> Get<LimeOptionSeries[]>($"securities/{symbol}/options/series", cancellationToken);

	public Task<LimeOptionChain> GetOptionChain(string symbol, string expiration, string series, CancellationToken cancellationToken)
		=> Get<LimeOptionChain>($"securities/{symbol}/options", cancellationToken, request => request
			.AddQueryParameter("expiration", expiration)
			.AddQueryParameter("series", series));

	public Task<LimePlaceOrderResponse> PlaceOrder(LimeOrderRequest body, CancellationToken cancellationToken)
		=> Post<LimePlaceOrderResponse>("orders/place", body, cancellationToken);

	public Task<LimeCancelOrderResponse> CancelOrder(string orderId, LimeCancelOrderRequest body, CancellationToken cancellationToken)
		=> Post<LimeCancelOrderResponse>($"orders/{orderId}/cancel", body, cancellationToken);

	private Task<T> Get<T>(string path, CancellationToken cancellationToken, Action<RestRequest> configure = null)
	{
		var request = CreateRequest(Method.Get);
		configure?.Invoke(request);
		return Invoke<T>(path, request, cancellationToken);
	}

	private Task<T> Post<T>(string path, object body, CancellationToken cancellationToken)
		=> Invoke<T>(path, CreateRequest(Method.Post).AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json), cancellationToken);

	private RestRequest CreateRequest(Method method)
	{
		var request = new RestRequest((string)null, method);
		request.SetBearer(_accessToken ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk));
		return request;
	}

	private Task<T> Invoke<T>(string path, RestRequest request, CancellationToken cancellationToken)
		=> request.InvokeAsync<T>(new Uri(_baseUri, path), this, this.AddVerboseLog, cancellationToken);
}
