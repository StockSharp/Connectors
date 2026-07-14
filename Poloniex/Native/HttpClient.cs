namespace StockSharp.Poloniex.Native;

class HttpClient(Authenticator authenticator) : BaseLogReceiver
{
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

	// to get readable name after obfuscation
	public override string Name => nameof(Poloniex) + "_" + nameof(HttpClient);

	public Task<IDictionary<string, PoloniexCurrency>> GetCurrenciesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("command", "returnCurrencies");

		return MakeRequestAsync<IDictionary<string, PoloniexCurrency>>(CreateUrl("public"), request, cancellationToken);
	}

	public Task<IDictionary<string, Ticker>> GetTickersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("command", "returnTicker");

		return MakeRequestAsync<IDictionary<string, Ticker>>(CreateUrl("public"), request, cancellationToken);
	}

	//public Task<OrderBook> GetOrderBookAsync(string currencyPair, int depth, CancellationToken cancellationToken)
	//{
	//	var request = CreateRequest(Method.Get);

	//	request
	//		.AddParameter("command", "returnOrderBook")
	//		.AddParameter("currencyPair", currencyPair)
	//		.AddParameter("depth", depth);

	//	return MakeRequest<OrderBook>(CreateUrl("public"), request, cancellationToken);
	//}

	public Task<IEnumerable<HttpTrade>> GetTradeHistoryAsync(string currencyPair, long? start, long? end, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("command", "returnTradeHistory")
			.AddParameter("currencyPair", currencyPair);

		if (start != null)
			request.AddParameter("start", start.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		return MakeRequestAsync<IEnumerable<HttpTrade>>(CreateUrl("public"), request, cancellationToken);
	}

	public Task<IEnumerable<MarketChartData>> GetChartDataAsync(string currencyPair, int period, double start, double end, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("command", "returnChartData")
			.AddParameter("currencyPair", currencyPair)
			.AddParameter("period", period);

		request.AddParameter("start", start);
		request.AddParameter("end", end);

		return MakeRequestAsync<IEnumerable<MarketChartData>>(CreateUrl("public"), request, cancellationToken);
	}

	public Task<IDictionary<string, Order[]>> GetOpenOrdersAsync(string currencyPair, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "returnOpenOrders")
			.AddParameter("currencyPair", currencyPair);

		var url = CreateUrl("tradingApi");

		return MakeRequestAsync<IDictionary<string, Order[]>>(url, ApplySecret(request), cancellationToken);
	}

	public Task<OwnTrade[]> GetOwnTradesAsync(string currencyPair, double? start, double? end, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "returnTradeHistory")
			.AddParameter("currencyPair", currencyPair);

		if (start != null)
			request.AddParameter("start", start.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		var url = CreateUrl("tradingApi");

		return MakeRequestAsync<OwnTrade[]>(url, ApplySecret(request), cancellationToken);
	}

	public async Task<OwnTrade[]> GetOrderTradesAsync(long orderNumber, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "returnOrderTrades")
			.AddParameter("orderNumber", orderNumber);

		var url = CreateUrl("tradingApi");

		try
		{
			return await MakeRequestAsync<OwnTrade[]>(url, ApplySecret(request), cancellationToken) ?? [];
		}
		catch (InvalidOperationException e)
		{
			// if order do not have trades this issue will be occured
			if (e.Message == "Order not found, or you are not the person who placed it.")
				return [];

			throw;
		}
	}

	public async Task<long> NewOrderAsync(long transactionId, string currencyPair, string type, decimal rate, decimal amount, bool fillOrKill, bool immediateOrCancel, bool? postOnly, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", type)
			.AddParameter("currencyPair", currencyPair)
			.AddParameter("rate", rate)
			.AddParameter("amount", amount);

		if (fillOrKill)
			request.AddParameter("fillOrKill", 1);

		if (immediateOrCancel)
			request.AddParameter("immediateOrCancel", 1);

		if (postOnly != null)
			request.AddParameter("postOnly", postOnly.Value ? 1 : 0);

		request.AddParameter("clientOrderId", transactionId);

		var url = CreateUrl("tradingApi");

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request), cancellationToken);

		return (long)response.orderNumber;
	}

	public Task CancelOrderAsync(long transactionId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "cancelOrder")
			.AddParameter("clientOrderId", transactionId);

		var url = CreateUrl("tradingApi");

		return MakeRequestAsync<object>(url, ApplySecret(request), cancellationToken);
	}

	public Task CancelAllOrdersAsync(string currencyPair, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "cancelAllOrders");

		if (!currencyPair.IsEmpty())
			request.AddParameter("currencyPair", currencyPair);

		var url = CreateUrl("tradingApi");

		return MakeRequestAsync<object>(url, ApplySecret(request), cancellationToken);
	}

	public Task MoveOrderAsync(long transactionId, long orderNumber, decimal rate, decimal? amount, bool fillOrKill, bool immediateOrCancel, bool? postOnly, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "moveOrder")
			.AddParameter("orderNumber", orderNumber)
			.AddParameter("clientOrderId", transactionId)
			.AddParameter("rate", rate);

		if (amount != null)
			request.AddParameter("amount", amount.Value);

		if (fillOrKill)
			request.AddParameter("fillOrKill", 1);

		if (immediateOrCancel)
			request.AddParameter("immediateOrCancel", 1);

		if (postOnly != null)
			request.AddParameter("postOnly", postOnly.Value ? 1 : 0);

		var url = CreateUrl("tradingApi");

		return MakeRequestAsync<object>(url, ApplySecret(request), cancellationToken);
	}

	public Task<IDictionary<string, Balance>> GetCompleteBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "returnCompleteBalances")
			.AddParameter("account", "all");

		var url = CreateUrl("tradingApi");

		return MakeRequestAsync<IDictionary<string, Balance>>(url, ApplySecret(request), cancellationToken);
	}

	public async Task<long> WithdrawAsync(string currency, decimal amount, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("command", "withdraw")
			.AddParameter("currency", currency)
			.AddParameter("amount", amount)
			.AddParameter("address", info.CryptoAddress);

		if (!info.PaymentId.IsEmpty())
			request.AddParameter("paymentId", info.PaymentId);

		var url = CreateUrl("tradingApi");

		dynamic responce = await MakeRequestAsync<object>(url, ApplySecret(request), cancellationToken);

		return (long)responce.withdrawalNumber;
	}

	private static Url CreateUrl(string methodName, string version = "")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"https://poloniex.com/{version}{methodName}");
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		request.AddParameter("nonce", _authenticator.GetNonce());

		var encodedArgs = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
			.ToQueryString();

		var signature = _authenticator.Sign(encodedArgs);

		request
			.AddHeader("Key", _authenticator.Key.UnSecure())
			.AddHeader("Sign", signature);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync<JToken>(url, this, this.AddVerboseLog, cancellationToken);

		if (((JToken)obj).Type == JTokenType.Object)
		{
			if (obj.error != null)
				throw new InvalidOperationException((string)obj.error.ToString());

			if (obj.success != null && obj.success == 0)
				throw new InvalidOperationException((string)obj.message.ToString());
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}