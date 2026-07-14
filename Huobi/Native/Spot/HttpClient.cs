namespace StockSharp.Huobi.Native.Spot;

using StockSharp.Huobi.Native.Spot.Model;

class HttpClient(Authenticator authenticator, string domain) : BaseLogReceiver
{
	private readonly string _baseUrl = $"https://{domain}";
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

    // to get readable name after obfuscation
    public override string Name => nameof(Huobi) + "_" + nameof(Spot) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("common/symbols");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IEnumerable<Symbol>>(url, request, cancellationToken);
	}

	//public IEnumerable<Ohlc> GetCandles(string symbol, string period, int? size)
	//{
	//	var url = CreateUrl("market/history/kline", string.Empty);
	//	var request = CreateRequest(Method.Get);

	//	request
	//		.AddParameter("symbol", symbol)
	//		.AddParameter("period", period);

	//	if (size != null)
	//		request.AddParameter("size", size.Value);

	//	return MakeRequest<IEnumerable<Ohlc>>(url, request);
	//}

	public async Task<IEnumerable<Trade>> GetTradesAsync(string symbol, int? size, CancellationToken cancellationToken)
	{
		var url = CreateUrl("market/history/trade", string.Empty);
		var request = CreateRequest(Method.Get);

		request.AddParameter("symbol", symbol);

		if (size != null)
			request.AddParameter("size", size.Value);

		var trades = new List<Trade>();

		dynamic response = await MakeRequestAsync<object>(url, request, cancellationToken);

		foreach (var item in response)
		{
			foreach (var tradeToken in item.data)
			{
				trades.Add(((JToken)tradeToken).DeserializeObject<Trade>());
			}
		}

		return trades;
	}

	public Task<IEnumerable<Account>> GetAccountsAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("account/accounts");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IEnumerable<Account>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Account> GetAccountAsync(long accountId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"account/accounts/{accountId}/balance");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<Account>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOrdersAsync(string symbol, string states, CancellationToken cancellationToken)
	{
		var url = CreateUrl("order/orders");
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("states", states)
			//.AddParameter("states", isActive ? "pre-submitted,submitted,partial-filled,partial-canceled" : "filled,canceled")
			;

		return MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrdersAsync(long? accountId = null, string symbol = null, string side = null, int? size = null, CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("order/openOrders");
		var request = CreateRequest(Method.Get);

		if (accountId != null)
			request.AddParameter("account-id", accountId.Value.To<string>());

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		if (!side.IsEmpty())
			request.AddParameter("side", side);

		if (size != null)
			request.AddParameter("size", size.Value);

		return MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> GetOrderInfoAsync(long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"order/orders/{orderId}");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<OwnTrade>> GetOrderMatchesAsync(long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"order/orders/{orderId}/matchresults");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IEnumerable<OwnTrade>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task RegisterOrderAsync(long transactionId, long accountId, string symbol, string type,
		string source, decimal? price, decimal volume, decimal? stopPrice, string stopOperator, CancellationToken cancellationToken)
	{
		var url = CreateUrl("order/orders/place");
		var request = CreateRequest(Method.Post);

		var body = (IDictionary<string, object>)new ExpandoObject();

		body.Add("client-order-id", transactionId.To<string>());

		body.Add("account-id", accountId.To<string>());
		body.Add("amount", volume.To<string>());

		if (price != null)
			body.Add("price", price.Value);

		body.Add("source", source);
		body.Add("symbol", symbol);
		body.Add("type", type);

		if (stopPrice != null)
			body.Add("stop-price", stopPrice.Value);

		if (!stopOperator.IsEmpty())
			body.Add("operator", stopOperator);

		request.AddJsonBody(body);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task CancelOrderAsync(long transactionId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"order/orders/submitCancelClientOrder");
		var request = CreateRequest(Method.Post);

		var body = (IDictionary<string, object>)new ExpandoObject();
		body.Add("client-order-id", transactionId);

		request.AddJsonBody(body);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task BatchCancelOrderAsync(long accountId, string symbol, string side, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"order/orders/batchCancelOpenOrders");
		var request = CreateRequest(Method.Post);

		var body = (IDictionary<string, object>)new ExpandoObject();
		body.Add("account-id", accountId);

		if (!symbol.IsEmpty())
			body.Add("symbol", symbol);

		if (!side.IsEmpty())
			body.Add("side", side);

		request.AddJsonBody(body);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<string> RegisterAlgoOrderAsync(long transactionId, long accountId, string symbol,
		string type, string tif, string side, decimal? price, decimal volume, decimal stopPrice, CancellationToken cancellationToken)
	{
		var url = CreateUrl("algo-orders", "v2/");
		var request = CreateRequest(Method.Post);

		var body = (IDictionary<string, object>)new ExpandoObject();

		body.Add("accountId", accountId);
		body.Add("symbol", symbol);

		if (price != null)
			body.Add("orderPrice", price.Value.To<string>());

		body.Add("orderSide", side);
		body.Add("orderSize", volume.To<string>());

		body.Add("timeInForce", tif);
		body.Add("orderType", type);

		body.Add("clientOrderId", transactionId.To<string>());

		body.Add("stopPrice", stopPrice.To<string>());

		request.AddJsonBody(body);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return (string)response.clientOrderId;
	}

	public Task CancelAlgoOrderAsync(long transactionId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"algo-orders/cancellation", "v2/");
		var request = CreateRequest(Method.Post);

		request.AddJsonBody(new
		{
			clientOrderIds = new[] { transactionId.To<string>() }
		});

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<SocketOrder>> GetOpenAlgoOrdersAsync(long? accountId = null, string symbol = null, string side = null, string type = null, int? size = null, CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("algo-orders/opening", "v2/");
		var request = CreateRequest(Method.Get);

		if (accountId != null)
			request.AddParameter("accountId", accountId.Value.To<string>());

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		if (!side.IsEmpty())
			request.AddParameter("orderSide", side);

		if (!type.IsEmpty())
			request.AddParameter("orderType", type);

		if (size != null)
			request.AddParameter("limit", size.Value);

		return MakeRequestAsync<IEnumerable<SocketOrder>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<long> WithdrawAsync(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = CreateUrl("dw/withdraw/api/create");
		var request = CreateRequest(Method.Post);

		var body = (IDictionary<string, object>)new ExpandoObject();

		body.Add("address", info.CryptoAddress);
		body.Add("amount", volume);
		body.Add("currency", symbol);

		if (!info.PaymentId.IsEmpty())
			body.Add("addr-tag", info.PaymentId);

		if (info.ChargeFee != null)
			body.Add("fee", info.ChargeFee.Value);

		request.AddJsonBody(body);

		return MakeRequestAsync<long>(url, ApplySecret(request, url), cancellationToken);
	}

	private Url CreateUrl(string methodName, string version = "v1/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"{_baseUrl}/{version}{methodName}") { Encode = UrlEncodes.None };
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Url url)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var timestamp = Authenticator.GetTimestamp();

		url
			.QueryString
				.Append("AccessKeyId", _authenticator.Key.UnSecure())
				.Append("SignatureMethod", Authenticator.Method)
				.Append("SignatureVersion", Authenticator.Version2)
				.Append("Timestamp", timestamp.EncodeUrl().UrlEncodeToUpperCase());

		var parameters = url.QueryString.ToList();

		if (request.Method == Method.Get)
		{
			parameters.AddRange(request.Parameters.Select(p => new KeyValuePair<string, string>(p.Name, p.Value.To<string>().EncodeUrl().UrlEncodeToUpperCase())));
		}

		var encodedArgs = parameters
			//.Where(p => p.Type == ParameterType.QueryString)
			.OrderBy(p => p.Key, StringComparer.Ordinal)
			.ToQueryString();

		var signature = _authenticator.Sign(request.Method, url.Host, url.AbsolutePath, encodedArgs);

		url.QueryString.Append("Signature", signature.EncodeUrl().UrlEncodeToUpperCase());

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject && obj.status == "error")
			throw new InvalidOperationException((string)((JObject)obj).Property("err-msg").Value);

		return ((JToken)obj.data).DeserializeObject<T>();
	}
}