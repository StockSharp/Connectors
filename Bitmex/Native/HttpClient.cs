namespace StockSharp.Bitmex.Native;

using System.Dynamic;

using Newtonsoft.Json.Linq;

class HttpClient : BaseLogReceiver
{
	private readonly string _baseUrl;

	private readonly Authenticator _authenticator;

	public HttpClient(Authenticator authenticator, string subDomain)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_baseUrl = $"https://{subDomain}.bitmex.com/api/v1";
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Bitmex) + "_" + nameof(HttpClient);

	public Task<Symbol[]> GetInstrumentsActiveAndIndices(CancellationToken cancellationToken)
	{
		return MakeRequest<Symbol[]>(CreateUrl("instrument/activeAndIndices"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<TradeOhlc[]> GetCandles(string symbol, string interval, long? count, long? start, DateTime? startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("binSize", interval);

		if (count != null)
			request.AddParameter("count", count.Value);

		if (start != null)
			request.AddParameter("start", start.Value);

		if (startTime != null)
			request.AddParameter("startTime", startTime.Value);

		if (endTime != null)
			request.AddParameter("endTime", endTime.Value);

		return MakeRequest<TradeOhlc[]>(CreateUrl("trade/bucketed"), request, cancellationToken);
	}

	public Task<Trade[]> GetTrades(string symbol, long? count, long? start, DateTime? startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol);

		if (count != null)
			request.AddParameter("count", count.Value);

		if (start != null)
			request.AddParameter("start", start.Value);

		if (startTime != null)
			request.AddParameter("startTime", startTime.Value);

		if (endTime != null)
			request.AddParameter("endTime", endTime.Value);

		return MakeRequest<Trade[]>(CreateUrl("trade"), request, cancellationToken);
	}

	public Task<Position[]> GetPositions(object filter, long? count, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("position");

		if (filter != null)
			url.QueryString.Append("filter", JsonConvert.SerializeObject(filter, _serializerSettings));

		if (count != null)
			url.QueryString.Append("count", count.Value);

		return MakeRequest<Position[]>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order[]> GetOrders(string symbol, object filter, long? count, long? start, DateTime? startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("order");

		if (!symbol.IsEmpty())
			url.QueryString.Append("symbol", symbol);

		if (filter != null)
			url.QueryString.Append("filter", JsonConvert.SerializeObject(filter, _serializerSettings));

		if (count != null)
			url.QueryString.Append("count", count.Value);

		if (start != null)
			url.QueryString.Append("start", start.Value);

		if (startTime != null)
			url.QueryString.Append("startTime", startTime.Value);

		if (endTime != null)
			url.QueryString.Append("endTime", endTime.Value);

		return MakeRequest<Order[]>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> RegisterOrder(string symbol, string type, string side, decimal? price, decimal volume, decimal? visibleVolume, decimal? stopPrice, string timeInForce, string clientOrderId, string clOrdLinkId, decimal? pegOffsetValue, string pegPriceType, string execInst, string contingencyType, string comment, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl("order");

		dynamic body = new ExpandoObject();

		body.symbol = symbol;
		body.side = side;
		body.orderQty = volume;

		if (price != null)
			body.price = price.Value;

		if (visibleVolume != null)
			body.displayQty = visibleVolume.Value;

		if (stopPrice != null)
			body.stopPx = stopPrice.Value;

		body.clOrdID = clientOrderId;
		body.ordType = type;
		body.timeInForce = timeInForce;

		if (!clOrdLinkId.IsEmpty())
			body.clOrdLinkID = clOrdLinkId;

		if (pegOffsetValue != null)
			body.pegOffsetValue = pegOffsetValue.Value;

		if (!pegPriceType.IsEmpty())
			body.pegPriceType = pegPriceType;

		if (!execInst.IsEmpty())
			body.execInst = execInst;

		if (!contingencyType.IsEmpty())
			body.contingencyType = contingencyType;

		if (!comment.IsEmpty())
			body.text = comment;

		return MakeRequest<Order>(url, ApplySecret(request, url, (object)body), cancellationToken);
	}

	public Task AmendOrder(string originClientOrderId, string orderId, string clientOrderId, decimal? price, decimal? volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Put);
		var url = CreateUrl("order");

		dynamic body = new ExpandoObject();

		if (!originClientOrderId.IsEmpty())
		{
			body.origClOrdID = originClientOrderId;

			if (!clientOrderId.IsEmpty())
				body.clOrdID = clientOrderId;
		}
		else if (!orderId.IsEmpty())
			body.orderID = orderId;

		if (volume != null)
			body.orderQty = volume.Value;

		if (price != null)
			body.price = price.Value;

		return MakeRequest<Order>(url, ApplySecret(request, url, (object)body), cancellationToken);
	}

	public async Task<Order> CancelOrder(string clientOrderId, string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);
		var url = CreateUrl("order");

		dynamic body = new ExpandoObject();

		if (!orderId.IsEmpty())
			body.orderID = orderId;
		else if (!clientOrderId.IsEmpty())
			body.clOrdID = clientOrderId;

		return (await MakeRequest<Order[]>(url, ApplySecret(request, url, (object)body), cancellationToken)).First();
	}

	public Task<Order[]> CancelAllOrder(string symbol, object filter, string comment, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);
		var url = CreateUrl("order/all");

		dynamic body = new ExpandoObject();

		if (!symbol.IsEmpty())
			body.symbol = symbol;

		if (filter != null)
			body.filter = filter;

		if (!comment.IsEmpty())
			body.text = comment;

		return MakeRequest<Order[]>(url, ApplySecret(request, url, (object)body), cancellationToken);
	}

	public async Task<string> Withdraw(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);
		var url = CreateUrl("user/requestWithdrawal");

		dynamic body = new ExpandoObject();

		body.currency = symbol;
		body.address = info.CryptoAddress;
		body.amount = volume;

		if (info.ChargeFee != null)
			body.fee = info.ChargeFee.Value;

		dynamic response = await MakeRequest<object>(url, ApplySecret(request, url, (object)body), cancellationToken);

		return (string)response.transactID;
	}

	public Task WithdrawConfirm(string token, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl("user/confirmWithdrawal");

		return MakeRequest<object>(url, ApplySecret(request, url, new { token }), cancellationToken);
	}

	public Task<IDictionary<string, Commission>> GetCommission(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("user/commission");

		return MakeRequest<IDictionary<string, Commission>>(url, ApplySecret(request, url), cancellationToken);
	}

	private Url CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new($"{_baseUrl}/{methodName}") { Encode = UrlEncodes.Upper };
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private static readonly JsonSerializerSettings _serializerSettings = JsonHelper.CreateJsonSerializerSettings();

	private RestRequest ApplySecret(RestRequest request, Uri url, object body = null)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var urlStr = url.PathAndQuery;

		string encodedArgs;

		if (request.Method == Method.Get)
		{
			var qs = request
			    .Parameters
			    .Where(p => p.Type == ParameterType.QueryString && p.Value != null)
			    .Select(p => $"{p.Name}={p.Value.ToString().EncodeUrl().UrlEncodeToUpperCase()}")
			    .JoinAnd();

			if (!qs.IsEmpty())
				urlStr += "?" + qs;

			encodedArgs = string.Empty;
		}
		else
		{
			encodedArgs = JsonConvert.SerializeObject(body, _serializerSettings);
		}

		var expires = (long)DateTime.UtcNow.ToUnix() + 10 /*10sec plus*/;

		var signature = _authenticator.Sign(request.Method, urlStr, expires, encodedArgs);

		request
			.AddParameter("api-expires", expires, ParameterType.HttpHeader)
			.AddHeader("api-key", _authenticator.Key.UnSecure())
			.AddHeader("api-signature", signature);

		if (request.Method != Method.Get)
		{
			//request.RequestFormat = DataFormat.Json;
			request.AddBodyAsStr(encodedArgs);
		}

		return request;
	}

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (((JToken)obj).Type == JTokenType.Object && obj.error != null)
			throw new InvalidOperationException((string)obj.error.ToString());

		return ((JToken)obj).DeserializeObject<T>();
	}
}