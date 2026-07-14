namespace StockSharp.Bitget.Native.Spot;

using StockSharp.Bitget.Native.Spot.Model;

class HttpClient : BaseLogReceiver
{
	private readonly Authenticator _authenticator;

	private readonly string _baseUrl = "https://api.bitget.com";
	private const string _version = "v2";

	public HttpClient(BitgetMessageAdapter adapter, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseUrl = $"https://{adapter.RestDomain}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(Bitget) + "_" + nameof(Spot) + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("spot/public/symbols"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<RestCandle[]> GetCandles(string symbol, string granularity, long? startTime, long? endTime, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/market/candles");

		var request = CreateRequest(Method.Get)
			.AddParameter("symbol", symbol)
			.AddParameter("granularity", granularity);

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
			.AddQueryParameter("limit", limit)
			;

		return MakeRequest<RestOrderBook>(CreateUrl("spot/market/orderbook"), request, cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrders(CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/trade/unfilled-orders");
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalance(CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/account/assets");
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async ValueTask Withdraw(string coin, decimal amount, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = CreateUrl("spot/wallet/withdrawal");
		var request = CreateRequest(Method.Post);

		request.AddJsonBody(new
		{
			coin,
			amount = amount.ToString(),
			address = info.CryptoAddress,
			tag = info.Comment,
			transferType = "on_chain"
		});

		await MakeRequest<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> PlaceOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/trade/place-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			symbol = regMsg.SecurityId.ToSymbol(),
			side = regMsg.Side.ToNative(),
			orderType = regMsg.OrderType.ToNative(),
			price = regMsg.OrderType == OrderTypes.Market ? null : (regMsg.Price != 0 ? regMsg.Price.ToString() : null),
			size = regMsg.Volume.ToString(),
			clientOid = regMsg.TransactionId.ToRequestId(),
			force = regMsg.TimeInForce.ToNative(),
		});

		return MakeRequest<Order>(url, request, cancellationToken);
	}

	public Task CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/trade/cancel-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			symbol = cancelMsg.SecurityId.ToSymbol(),
			orderId = cancelMsg.OrderId.Value.To<string>(),
		});

		return MakeRequest<object>(url, request, cancellationToken);
	}

	public Task AmendOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/trade/cancel-replace-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			symbol = replaceMsg.SecurityId.ToSymbol(),
			orderId = replaceMsg.OldOrderId.Value.To<string>(),
			price = replaceMsg.Price.ToString(),
			size = replaceMsg.Volume.ToString(),
			newClientOid = replaceMsg.TransactionId.ToRequestId(),
		});

		return MakeRequest<object>(url, request, cancellationToken);
	}

	public Task BatchCancelOrders(IEnumerable<object> orderList, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/trade/batch-cancel-order");
		var request = ApplySecret(CreateRequest(Method.Post), url);

		request.AddJsonBody(new
		{
			orderList,
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