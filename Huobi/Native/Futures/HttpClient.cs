namespace StockSharp.Huobi.Native.Futures;

using StockSharp.Huobi.Native.Futures.Model;

class HttpClient(Authenticator authenticator, string domain) : BaseLogReceiver
{
	private readonly string _baseUrl = $"https://{domain}";
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

    // to get readable name after obfuscation
    public override string Name => nameof(Huobi) + "_" + nameof(Futures) + "_" + nameof(HttpClient);

	public async Task<IEnumerable<Contract>> GetContractsAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_contract_info");
		var request = CreateRequest(Method.Get);

		return await MakeRequestAsync<IEnumerable<Contract>>(url, request, cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string symbol, string period, int? size, long? from, long? to, CancellationToken cancellationToken)
	{
		var url = CreateUrl("market/history/kline", string.Empty, string.Empty);
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("period", period);

		if (size != null)
			request.AddParameter("size", size.Value);

		if (from != null)
			request.AddParameter("from", from.Value);

		if (to != null)
			request.AddParameter("to", to.Value);

		return MakeRequestAsync<IEnumerable<Ohlc>>(url, request, cancellationToken);
	}

	public async Task<IEnumerable<Trade>> GetTradesAsync(string symbol, int size, CancellationToken cancellationToken)
	{
		var url = CreateUrl("market/history/trade", string.Empty, string.Empty);
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("size", size);

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

	public Task<IEnumerable<Balance>> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_account_info");
		var request = CreateRequest(Method.Post);

		return MakeRequestAsync<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Position>> GetPositionsAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_position_info");
		var request = CreateRequest(Method.Post);

		return MakeRequestAsync<IEnumerable<Position>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetOpenOrdersAsync(string asset, int? size = null, CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("contract_openorders");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.symbol = asset;

		if (size != null)
			body.page_size = size.Value;

		RestRequestExtensions.AddJsonBody(request, body);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.orders).DeserializeObject<IEnumerable<Order>>();
	}

	public Task<IEnumerable<OwnTrade>> GetOrderMatchesAsync(long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_order_detail");
		var request = CreateRequest(Method.Post);

		RestRequestExtensions.AddJsonBody(request, new { });

		return MakeRequestAsync<IEnumerable<OwnTrade>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task RegisterOrderAsync(long transactionId, string contractCode, string type,
		string direction, decimal? price, decimal volume, int levelRate, string offset, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_order");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.client_order_id = transactionId.To<string>();
		body.contract_code = contractCode;

		if (price != null)
			body.price = price.Value;

		body.volume = (long)volume;
		body.direction = direction;
		body.order_price_type = type;

		body.offset = offset;
		body.lever_rate = levelRate;

		// TODO TP SL

		RestRequestExtensions.AddJsonBody(request, body);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task CancelOrderAsync(long transactionId, string asset, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_cancel");
		var request = CreateRequest(Method.Post);

		request.AddJsonBody(new
		{
			client_order_id = transactionId,
			symbol = asset,
		});

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task BatchCancelOrderAsync(string underlyingAsset, string contractCode, string contractType, string direction, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_cancelall");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.symbol = underlyingAsset;

		if (!contractCode.IsEmpty())
			body.contract_code = contractCode;

		if (!contractType.IsEmpty())
			body.contract_type = contractType;

		if (!direction.IsEmpty())
			body.direction = direction;

		RestRequestExtensions.AddJsonBody(request, body);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<string> RegisterTriggerOrderAsync(long transactionId, string contractCode, string type,
		string offset, int leverage, decimal? price, decimal volume, string triggerType, decimal triggerPrice, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_trigger_order");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.client_order_id = transactionId.To<string>();
		body.contract_code = contractCode;

		body.trigger_type = triggerType;
		body.trigger_price = triggerPrice;

		if (price != null)
			body.order_price = price.Value;

		if (!type.IsEmpty())
			body.order_price_type = type;

		body.volume = (long)volume;

		body.offset = offset;
		body.lever_rate = leverage;

		RestRequestExtensions.AddJsonBody(request, body);

		return MakeRequestAsync<string>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task CancelTriggerOrderAsync(string asset, string orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_trigger_cancel");
		var request = CreateRequest(Method.Post);

		request.AddJsonBody(new
		{
			symbol = asset,
			order_id = orderId,
		});

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task CancelAllTriggerOrdersAsync(string asset, string contractCode, string contractType, CancellationToken cancellationToken)
	{
		var url = CreateUrl("contract_trigger_cancelall");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();
		body.symbol = asset;

		if (!contractCode.IsEmpty())
			body.contract_code = contractCode;

		if (!contractType.IsEmpty())
			body.contract_type = contractType;

		RestRequestExtensions.AddJsonBody(request, body);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<IEnumerable<SocketOrder>> GetOpenTriggerOrdersAsync(string asset, int? size = null, CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("contract_trigger_openorders");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.symbol = asset;

		if (size != null)
			body.page_size = size.Value;

		RestRequestExtensions.AddJsonBody(request, body);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
		return ((JToken)response.orders).DeserializeObject<IEnumerable<SocketOrder>>();
	}

	private Url CreateUrl(string methodName, string version = "v1/", string api = "api")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"{_baseUrl}/{api}/{version}{methodName}") { Encode = UrlEncodes.None };
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
			throw new InvalidOperationException((string)obj.err_msg);

		return ((JToken)obj.data).DeserializeObject<T>();
	}
}