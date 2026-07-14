namespace StockSharp.ByBit.Native;

using System.Runtime.CompilerServices;

class HttpClient(string baseMdUrl, string baseTsUrl, Authenticator authenticator, int recvWindow, int timeStampOffset) : BaseLogReceiver
{
	private readonly string _baseMdUrl = baseMdUrl.ThrowIfEmpty(nameof(baseMdUrl));
	private readonly string _baseTsUrl = baseTsUrl.ThrowIfEmpty(nameof(baseTsUrl));
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	private readonly int _recvWindow = recvWindow;
	private readonly int _timeStampOffset = timeStampOffset;

    // to get readable name after obfuscation
    public override string Name => nameof(ByBit) + "_" + nameof(HttpClient);

	public IAsyncEnumerable<Instrument> GetInstruments(string category, string symbol, string baseCoin, string status, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "market/instruments-info");
		request.AddParameter("category", category);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		if (!baseCoin.IsEmpty())
			request.AddParameter("baseCoin", baseCoin);

		if (!status.IsEmpty())
			request.AddParameter("status", status);

		return MakeRequest<Instrument>(_baseMdUrl, request, false, cancellationToken);
	}

	public IAsyncEnumerable<Kline> GetKlines(string category, string symbol, string interval, int? limit, long? startTime, long? endTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "market/kline");
		request.AddParameter("category", category);
		request.AddParameter("symbol", symbol);
		request.AddParameter("interval", interval);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		return MakeRequest<Kline>(_baseMdUrl, request, false, cancellationToken);
	}

	public IAsyncEnumerable<Trade> GetTrades(string category, string symbol, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "market/recent-trade");
		request.AddParameter("category", category);
		request.AddParameter("symbol", symbol);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<Trade>(_baseMdUrl, request, false, cancellationToken);
	}

	public IAsyncEnumerable<OpenInterest> GetOpenInterest(string category, string symbol, string interval, int? limit, long? startTime, long? endTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "market/open-interest");
		request.AddParameter("category", category);
		request.AddParameter("symbol", symbol);
		request.AddParameter("intervalTime", interval);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		return MakeRequest<OpenInterest>(_baseMdUrl, request, false, cancellationToken);
	}

	public IAsyncEnumerable<Volatility> GetHistoricalVolatility(string category, string baseCoin, int? limit, long? startTime, long? endTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "market/historical-volatility");
		request.AddParameter("category", category);

		if (!baseCoin.IsEmpty())
			request.AddParameter("baseCoin", baseCoin);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		if (startTime.HasValue)
			request.AddParameter("startTime", startTime.Value);

		if (endTime.HasValue)
			request.AddParameter("endTime", endTime.Value);

		return MakeRequest<Volatility>(_baseMdUrl, request, false, cancellationToken);
	}

	public IAsyncEnumerable<Order> GetOrders(string category, string symbol, string baseCoin, string settleCoin, string orderId, string orderLinkId, long? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "order/realtime");
		request.AddParameter("category", category);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		if (!baseCoin.IsEmpty())
			request.AddParameter("baseCoin", baseCoin);

		if (!settleCoin.IsEmpty())
			request.AddParameter("settleCoin", settleCoin);

		if (!orderId.IsEmpty())
			request.AddParameter("orderId", orderId);

		if (!orderLinkId.IsEmpty())
			request.AddParameter("orderLinkId", orderLinkId);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<Order>(_baseTsUrl, request, true, cancellationToken);
	}

	public IAsyncEnumerable<Position> GetPositions(string category, string symbol, string baseCoin, string settleCoin, long? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "position/list");
		request.AddParameter("category", category);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		if (!baseCoin.IsEmpty())
			request.AddParameter("baseCoin", baseCoin);

		if (!settleCoin.IsEmpty())
			request.AddParameter("settleCoin", settleCoin);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<Position>(_baseTsUrl, request, true, cancellationToken);
	}

	public IAsyncEnumerable<Wallet> GetWallets(string accountType, string coin, long? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "account/wallet-balance");
		request.AddParameter("accountType", accountType);

		if (!coin.IsEmpty())
			request.AddParameter("coin", coin);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<Wallet>(_baseTsUrl, request, true, cancellationToken);
	}

	public async Task<Order> CreateOrder(string category, string symbol, string side, string orderType, int? isLeverage, string orderLinkId, string price, string qty, string marketUnit, int? triggerDirection, string triggerPrice, string triggerBy, string orderIv, string timeInForce, int? positionIdx, string takeProfit, string stopLoss, string tpTriggerBy, string slTriggerBy, bool? reduceOnly, bool? closeOnTrigger, string smpType, bool? mmp, string tpslMode, string tpLimitPrice, string slLimitPrice, string tpOrderType, string slOrderType, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "order/create");
		request.AddParameter("category", category);
		request.AddParameter("symbol", symbol);
		request.AddParameter("side", side);
		request.AddParameter("orderType", orderType);
		if (isLeverage.HasValue) request.AddParameter("isLeverage", isLeverage.Value);
		if (!orderLinkId.IsEmpty()) request.AddParameter("orderLinkId", orderLinkId);
		if (!price.IsEmpty()) request.AddParameter("price", price);
		if (!qty.IsEmpty()) request.AddParameter("qty", qty);
		if (!marketUnit.IsEmpty()) request.AddParameter("marketUnit", marketUnit);
		if (triggerDirection.HasValue) request.AddParameter("triggerDirection", triggerDirection.Value);
		if (!triggerPrice.IsEmpty()) request.AddParameter("triggerPrice", triggerPrice);
		if (!triggerBy.IsEmpty()) request.AddParameter("triggerBy", triggerBy);
		if (!orderIv.IsEmpty()) request.AddParameter("orderIv", orderIv);
		if (!timeInForce.IsEmpty()) request.AddParameter("timeInForce", timeInForce);
		if (positionIdx.HasValue) request.AddParameter("positionIdx", positionIdx.Value);
		if (!takeProfit.IsEmpty()) request.AddParameter("takeProfit", takeProfit);
		if (!stopLoss.IsEmpty()) request.AddParameter("stopLoss", stopLoss);
		if (!tpTriggerBy.IsEmpty()) request.AddParameter("tpTriggerBy", tpTriggerBy);
		if (!slTriggerBy.IsEmpty()) request.AddParameter("slTriggerBy", slTriggerBy);
		if (reduceOnly.HasValue) request.AddParameter("reduceOnly", reduceOnly.Value);
		if (closeOnTrigger.HasValue) request.AddParameter("closeOnTrigger", closeOnTrigger.Value);
		if (!smpType.IsEmpty()) request.AddParameter("smpType", smpType);
		if (mmp.HasValue) request.AddParameter("mmp", mmp.Value);
		if (!tpslMode.IsEmpty()) request.AddParameter("tpslMode", tpslMode);
		if (!tpLimitPrice.IsEmpty()) request.AddParameter("tpLimitPrice", tpLimitPrice);
		if (!slLimitPrice.IsEmpty()) request.AddParameter("slLimitPrice", slLimitPrice);
		if (!tpOrderType.IsEmpty()) request.AddParameter("tpOrderType", tpOrderType);
		if (!slOrderType.IsEmpty()) request.AddParameter("slOrderType", slOrderType);

		var url = CreateUrl(_baseTsUrl, request);
		var result = await MakeRequest(request, url, true, cancellationToken);

		return result.DeserializeObject<Order>();
	}

	public async Task<Order> AmendOrder(string category, string symbol, string orderId, string orderLinkId, string price, string qty, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "order/amend");
		request.AddParameter("category", category);
		request.AddParameter("symbol", symbol);
		if (!orderId.IsEmpty()) request.AddParameter("orderId", orderId);
		if (!orderLinkId.IsEmpty()) request.AddParameter("orderLinkId", orderLinkId);
		if (!price.IsEmpty()) request.AddParameter("price", price);
		if (!qty.IsEmpty()) request.AddParameter("qty", qty);

		var url = CreateUrl(_baseTsUrl, request);
		var result = await MakeRequest(request, url, true, cancellationToken);

		return result.DeserializeObject<Order>();
	}

	public async Task<Order> CancelOrder(string category, string symbol, string orderId, string orderLinkId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "order/cancel");
		request.AddParameter("category", category);
		request.AddParameter("symbol", symbol);
		if (!orderId.IsEmpty()) request.AddParameter("orderId", orderId);
		if (!orderLinkId.IsEmpty()) request.AddParameter("orderLinkId", orderLinkId);

		var url = CreateUrl(_baseTsUrl, request);
		var result = await MakeRequest(request, url, true, cancellationToken);

		return result.DeserializeObject<Order>();
	}

	private RestRequest Sign(RestRequest request)
	{
		var timestamp = (long)DateTime.UtcNow.AddMilliseconds(_timeStampOffset).ToUnix(false);

		var queryString = request
			.Parameters
			.Where(p => p.Type is ParameterType.GetOrPost or ParameterType.QueryString && p.Value is not null)
			.Select(p => $"{p.Name}={p.Value.To<string>().EncodeUrl().UrlEncodeToUpperCase()}")
			.JoinAnd();

		var key = _authenticator.Key.UnSecure();

		var signature = _authenticator.Sign($"{timestamp}{key}{_recvWindow}{queryString}");

		request
			.AddHeader("X-BAPI-API-KEY", key)
			.AddHeader("X-BAPI-SIGN", signature)
			.AddHeader("X-BAPI-TIMESTAMP", timestamp)
			.AddHeader("X-BAPI-RECV-WINDOW", _recvWindow)
			//.AddHeader("Referer", nameof(StockSharp))
		;

		return request;
	}

	private static RestRequest CreateRequest(Method method, string endpoint)
	{
		return new(endpoint, method);
	}

	private async Task<JToken> MakeRequest(RestRequest request, Uri url, bool sign, CancellationToken cancellationToken)
	{
		if (sign)
			request = Sign(request);

		var resp = await request.InvokeAsync<Model.RestResponse>(url, this, this.AddVerboseLog, cancellationToken);

		if (resp.RetCode != 0)
			throw new InvalidOperationException($"(code={resp.RetCode}) {resp.RetMsg}");

		if (resp.Result is null)
			throw new InvalidOperationException("Result is null.");

		return resp.Result;
	}

	private static Uri CreateUrl(string baseUrl, RestRequest request)
		=> $"{baseUrl}{request.Resource}".To<Uri>();

	private async IAsyncEnumerable<T> MakeRequest<T>(string baseUrl, RestRequest request, bool sign, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = CreateUrl(baseUrl, request);

		while (true)
		{
			var result = await MakeRequest(request, url, sign, cancellationToken);

			if (result is JArray arr)
			{
				foreach (var item in arr.DeserializeObject<IEnumerable<T>>())
					yield return item;
			}
			else
			{
				var res = result.DeserializeObject<RestResponseResult>();

				if (res.List is null)
					throw new InvalidOperationException("List is null.");

				foreach (var item in res.List.DeserializeObject<IEnumerable<T>>())
					yield return item;

				if (res.NextPageCursor.IsEmpty())
					break;

				if (sign)
					request.RemoveWhere(p => p.Type == ParameterType.HttpHeader);

				request.AddParameter("cursor", res.NextPageCursor);
			}
		}
	}
}