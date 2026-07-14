namespace StockSharp.Mexc.Native.Futures;

using StockSharp.Mexc.Native.Futures.Model;

class HttpClient : BaseLogReceiver
{
	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;
	private const string _version = "v1";

	public HttpClient(MexcMessageAdapter adapter, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseUrl = adapter.IsDemo ? "https://contract.mexc.com" : $"https://{adapter.FuturesRestDomain}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(Mexc) + "_" + nameof(Futures) + nameof(HttpClient);

	public Task<ExchangeInfo> GetExchangeInfo(CancellationToken cancellationToken)
	{
		return MakeRequest<ExchangeInfo>(CreateUrl("exchangeInfo"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<Candle[]> GetCandles(string symbol, string interval, long? startTime, long? endTime, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddParameter("symbol", symbol)
			.AddParameter("interval", interval);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<Candle[]>(CreateUrl("klines"), request, cancellationToken);
	}

	public Task<OrderBook> GetOrderBook(string symbol, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("symbol", symbol);

		if (limit.HasValue)
			request.AddQueryParameter("limit", limit.Value);

		return MakeRequest<OrderBook>(CreateUrl("depth"), request, cancellationToken);
	}

	public Task<AccountInfo> GetAccountInfo(CancellationToken cancellationToken)
	{
		var url = CreateUrl("account");
		var request = CreateRequest(Method.Get);

		return MakeRequest<AccountInfo>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Position[]> GetPositions(CancellationToken cancellationToken)
	{
		var url = CreateUrl("positionRisk");
		var request = CreateRequest(Method.Get);

		return MakeRequest<Position[]>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order[]> GetOpenOrders(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl("openOrders");
		var request = CreateRequest(Method.Get);

		if (!symbol.IsEmpty())
			request.AddQueryParameter("symbol", symbol);

		return MakeRequest<Order[]>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<OrderResponse> PlaceOrder(string symbol, string side, string type, string timeInForce,
		decimal quantity, decimal? price, bool? reduceOnly, string positionSide, string clientOrderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("order");
		var request = CreateRequest(Method.Post)
			.AddParameter("symbol", symbol)
			.AddParameter("side", side)
			.AddParameter("type", type)
			.AddParameter("quantity", quantity.ToString())
			.AddParameter("newClientOrderId", clientOrderId);

		if (!timeInForce.IsEmpty())
			request.AddParameter("timeInForce", timeInForce);

		if (price.HasValue)
			request.AddParameter("price", price.Value.ToString());

		if (reduceOnly.HasValue)
			request.AddParameter("reduceOnly", reduceOnly.Value.ToString().ToLowerInvariant());

		if (!positionSide.IsEmpty())
			request.AddParameter("positionSide", positionSide);

		return MakeRequest<OrderResponse>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<OrderResponse> CancelOrder(string symbol, long? orderId, string clientOrderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("order");
		var request = CreateRequest(Method.Delete)
			.AddParameter("symbol", symbol);

		if (orderId.HasValue)
			request.AddParameter("orderId", orderId.Value.ToString());

		if (!clientOrderId.IsEmpty())
			request.AddParameter("origClientOrderId", clientOrderId);

		return MakeRequest<OrderResponse>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<OrderResponse[]> CancelAllOrders(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl("allOpenOrders");
		var request = CreateRequest(Method.Delete)
			.AddParameter("symbol", symbol);

		return MakeRequest<OrderResponse[]>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<UserTrade[]> GetUserTrades(string symbol, long? fromId, long? startTime, long? endTime, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl("userTrades");
		var request = CreateRequest(Method.Get)
			.AddParameter("symbol", symbol);

		if (fromId.HasValue)
			request.AddParameter("fromId", fromId.Value);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<UserTrade[]>(url, ApplySecret(request, url), cancellationToken);
	}

	private Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/api/{_version}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Uri url)
		=> request.ApplySecret(url, _authenticator);

	private Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
		=> request.InvokeAsync<T>(url, this, this.AddVerboseLog, cancellationToken);
}

class ExchangeInfo
{
	[JsonProperty("timezone")]
	public string Timezone { get; set; }

	[JsonProperty("serverTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime ServerTime { get; set; }

	[JsonProperty("symbols")]
	public Symbol[] Symbols { get; set; }
}