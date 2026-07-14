namespace StockSharp.Bitget.Native.Futures;

using StockSharp.Bitget.Native.Futures.Model;

class HttpClient : BaseLogReceiver
{
	private readonly string _productType;
	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;
	private const string _version = "v2";

	public HttpClient(BitgetMessageAdapter adapter, string productType, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseUrl = $"https://{adapter.RestDomain}";
		_productType = productType.ThrowIfEmpty(nameof(productType));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(Bitget) + "_" + nameof(Futures) + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("productType", _productType);

		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("mix/market/contracts"), request, cancellationToken);
	}

	public Task<RestOrderBook> GetOrderBook(string symbol, int limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("symbol", symbol)
			.AddQueryParameter("limit", limit)
			;

		return MakeRequest<RestOrderBook>(CreateUrl("mix/market/orderbook"), request, cancellationToken);
	}

	public Task<RestCandle[]> GetCandles(string symbol, string granularity, long? startTime, long? endTime, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"mix/market/candles");

		var request = CreateRequest(Method.Get)
			.AddParameter("symbol", symbol)
			.AddParameter("granularity", granularity)
			.AddParameter("productType", _productType);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<RestCandle[]>(url, request, cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrders(CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/order/orders-pending");

		var request = CreateRequest(Method.Get)
			.AddQueryParameter("productType", _productType);

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Balance> GetBalance(CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/account/accounts");

		var request = CreateRequest(Method.Get)
			.AddQueryParameter("productType", _productType);

		return MakeRequest<Balance>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Position>> GetPositions(CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/position/all-position");

		var request = CreateRequest(Method.Get)
			.AddQueryParameter("productType", _productType);

		return MakeRequest<IEnumerable<Position>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> PlaceOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/order/place-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			productType = _productType,
			symbol = regMsg.SecurityId.ToSymbol(),
			side = regMsg.Side.ToNative(),
			orderType = regMsg.OrderType.ToNative(),
			price = regMsg.Price != 0 ? regMsg.Price.ToString() : null,
			size = regMsg.Volume.ToString(),
			clientOid = regMsg.TransactionId.ToRequestId(),
			force = regMsg.TimeInForce.ToNative(),
			reduceOnly = regMsg.PositionEffect is not null ? (bool?)regMsg.PositionEffect.Value.ToNative() : null,
		});

		return MakeRequest<Order>(url, request, cancellationToken);
	}

	public Task CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/order/cancel-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			productType = _productType,
			symbol = cancelMsg.SecurityId.ToSymbol(),
			orderId = cancelMsg.OrderId.Value.ToString(),
		});

		return MakeRequest<object>(url, request, cancellationToken);
	}

	public Task AmendOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/order/modify-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			productType = _productType,
			symbol = replaceMsg.SecurityId.ToSymbol(),
			orderId = replaceMsg.OldOrderId.Value.ToString(),
			newPrice = replaceMsg.OldOrderPrice != replaceMsg.Price ? replaceMsg.Price.ToString() : null,
			newSize = replaceMsg.OldOrderVolume != replaceMsg.Volume ? replaceMsg.Volume.ToString() : null,
			newClientOid = replaceMsg.TransactionId.ToRequestId(),
		});

		return MakeRequest<object>(url, request, cancellationToken);
	}

	public Task BatchCancelOrders(IEnumerable<object> orderList, CancellationToken cancellationToken)
	{
		var url = CreateUrl("mix/order/batch-cancel-orders");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			orderList = orderList.ToArray(),
			productType = _productType
		});

		return MakeRequest<object>(url, request, cancellationToken);
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
