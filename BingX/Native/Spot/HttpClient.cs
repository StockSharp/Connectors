namespace StockSharp.BingX.Native.Spot;

using StockSharp.BingX.Native.Spot.Model;

class HttpClient : BaseLogReceiver
{
	private readonly Authenticator _authenticator;

	private readonly string _baseUrl = "https://open-api.bingx.com";
	private const string _version = "v1";

	public HttpClient(BingXMessageAdapter adapter, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		if (adapter.IsDemo)
			throw new NotSupportedException(LocalizedStrings.DemoMode);

		_baseUrl = $"https://{adapter.RestDomain}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(BingX) + "_" + nameof(Spot) + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("spot/v1/common/symbols"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<RestCandle[]> GetCandles(string symbol, string interval, long? startTime, long? endTime, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/{_version}/market/kline");

		var request = CreateRequest(Method.Get)
			.AddParameter("symbol", symbol)
			.AddParameter("interval", interval);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<RestCandle[]>(url, request, cancellationToken);
	}

	public Task<RestOrderBook> GetOrderBook(string symbol, int limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("symbol", symbol)
			.AddQueryParameter("limit", limit);

		return MakeRequest<RestOrderBook>(CreateUrl($"spot/{_version}/market/depth"), request, cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrders(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/{_version}/trade/openOrders");
		var request = CreateRequest(Method.Get);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalance(CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/{_version}/account");
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<OrderResponse> PlaceOrder(string symbol, string side, string type, decimal? quantity, decimal? price, string timeInForce, string clientOrderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/{_version}/trade/order");
		var request = CreateRequest(Method.Post)
			.AddParameter("symbol", symbol)
			.AddParameter("side", side)
			.AddParameter("type", type);

		if (quantity.HasValue)
			request.AddParameter("quantity", quantity.Value.ToString());

		if (price.HasValue)
			request.AddParameter("price", price.Value.ToString());

		if (!timeInForce.IsEmpty())
			request.AddParameter("timeInForce", timeInForce);

		if (!clientOrderId.IsEmpty())
			request.AddParameter("newClientOrderId", clientOrderId);

		return MakeRequest<OrderResponse>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<OrderResponse> CancelOrder(string symbol, long? orderId, string clientOrderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/{_version}/trade/cancel");
		var request = CreateRequest(Method.Post)
			.AddParameter("symbol", symbol);

		if (orderId.HasValue)
			request.AddParameter("orderId", orderId.Value.ToString());

		if (!clientOrderId.IsEmpty())
			request.AddParameter("origClientOrderId", clientOrderId);

		return MakeRequest<OrderResponse>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<OrderResponse>> CancelAllOrders(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/{_version}/trade/cancelOpenOrders");
		var request = CreateRequest(Method.Post);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		return MakeRequest<IEnumerable<OrderResponse>>(url, ApplySecret(request, url), cancellationToken);
	}

	private Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/openApi/{methodName}".To<Uri>();
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