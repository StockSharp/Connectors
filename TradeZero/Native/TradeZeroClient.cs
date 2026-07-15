namespace StockSharp.TradeZero.Native;

sealed class TradeZeroClient : BaseLogReceiver
{
	private const string _baseUrl = "https://webapi.tradezero.com/v1/api/";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _apiKey;
	private readonly SecureString _apiSecret;

	public TradeZeroClient(string apiKey, SecureString apiSecret)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret));
	}

	public override string Name => nameof(TradeZero) + "_" + nameof(TradeZeroClient);

	public async Task<TradeZeroAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> (await Get<TradeZeroAccountsResponse>("accounts", cancellationToken))?.Accounts ?? [];

	public Task<TradeZeroAccount> GetAccount(string accountId, CancellationToken cancellationToken)
		=> Get<TradeZeroAccount>($"account/{accountId.DataEscape()}", cancellationToken);

	public async Task<TradeZeroOrder[]> GetOrders(string accountId, CancellationToken cancellationToken)
		=> (await Get<TradeZeroOrdersResponse>($"accounts/{accountId.DataEscape()}/orders", cancellationToken))?.Orders ?? [];

	public Task<TradeZeroOrder> GetOrder(string accountId, string clientOrderId, CancellationToken cancellationToken)
		=> Get<TradeZeroOrder>($"accounts/{accountId.DataEscape()}/order/{clientOrderId.DataEscape()}", cancellationToken);

	public async Task<TradeZeroPosition[]> GetPositions(string accountId, CancellationToken cancellationToken)
		=> (await Get<TradeZeroPositionsResponse>($"accounts/{accountId.DataEscape()}/positions", cancellationToken))?.Positions ?? [];

	public Task<TradeZeroPnl> GetPnl(string accountId, CancellationToken cancellationToken)
		=> Get<TradeZeroPnl>($"accounts/{accountId.DataEscape()}/pnl", cancellationToken);

	public async Task<TradeZeroRoute[]> GetRoutes(string accountId, CancellationToken cancellationToken)
		=> (await Get<TradeZeroRoutesResponse>($"accounts/{accountId.DataEscape()}/routes", cancellationToken))?.Routes ?? [];

	public Task<TradeZeroOrder> PlaceOrder(string accountId, TradeZeroOrderRequest order, CancellationToken cancellationToken)
		=> Post<TradeZeroOrder>($"accounts/{accountId.DataEscape()}/order", order, cancellationToken);

	public Task<TradeZeroOrder> CancelOrder(string accountId, string clientOrderId, CancellationToken cancellationToken)
		=> Invoke<TradeZeroOrder>($"accounts/{accountId.DataEscape()}/orders/{clientOrderId.DataEscape()}", CreateRequest(Method.Delete), cancellationToken);

	public Task<TradeZeroQuote[]> GetQuotes(string symbols, CancellationToken cancellationToken)
		=> Get<TradeZeroQuote[]>($"quotes/{symbols.DataEscape()}", cancellationToken);

	public Task<TradeZeroDom> GetDom(string symbol, CancellationToken cancellationToken)
		=> Get<TradeZeroDom>($"quotes/dom/{symbol.DataEscape()}", cancellationToken);

	public Task<TradeZeroBars> GetBars(string symbol, long intervalMilliseconds, int maxCandles, CancellationToken cancellationToken)
		=> Get<TradeZeroBars>($"quotes/symbol/{symbol.DataEscape()}/bar", cancellationToken, request => request
			.AddQueryParameter("msInterval", intervalMilliseconds)
			.AddQueryParameter("maxCandles", maxCandles)
			.AddQueryParameter("ignorePrePostMarket", false));

	public Task<TradeZeroScannerResponse> Search(string symbol, CancellationToken cancellationToken)
		=> Get<TradeZeroScannerResponse>($"scanner/search/{symbol.DataEscape()}", cancellationToken);

	private Task<T> Get<T>(string path, CancellationToken cancellationToken, Action<RestRequest> configure = null)
	{
		var request = CreateRequest(Method.Get);
		configure?.Invoke(request);
		return Invoke<T>(path, request, cancellationToken);
	}

	private Task<T> Post<T>(string path, object body, CancellationToken cancellationToken)
		=> Invoke<T>(path, CreateRequest(Method.Post).AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json), cancellationToken);

	private RestRequest CreateRequest(Method method)
		=> new RestRequest((string)null, method)
			.AddHeader("Accept", "application/json")
			.AddHeader("TZ-API-KEY-ID", _apiKey)
			.AddHeader("TZ-API-SECRET-KEY", _apiSecret.UnSecure());

	private Task<T> Invoke<T>(string path, RestRequest request, CancellationToken cancellationToken)
		=> request.InvokeAsync<T>(new Uri(new Uri(_baseUrl), path), this, this.AddVerboseLog, cancellationToken);
}
