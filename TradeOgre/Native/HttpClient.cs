namespace StockSharp.TradeOgre.Native;

using RestSharp.Authenticators;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;
	private readonly SecureString _secret = secret;

	private const string _baseUrl = "https://tradeogre.com/api";

	// to get readable name after obfuscation
	public override string Name => nameof(TradeOgre) + "_" + nameof(HttpClient);

	public async Task<IDictionary<string, Ticker>> GetTickersAsync(CancellationToken cancellationToken)
	{
		var response = await MakeRequestAsync<JArray>(CreateUrl("markets"), CreateRequest(Method.Get), false, cancellationToken);

		return response.Cast<JObject>().Select(o =>
		{
			var p = o.Properties().First();
			return new KeyValuePair<string, Ticker>(p.Name, p.Value.DeserializeObject<Ticker>());
		}).ToDictionary();
	}

	public async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken)
	{
		return await MakeRequestAsync<Ticker>(CreateUrl($"ticker/{symbol}"), CreateRequest(Method.Get), false, cancellationToken);
	}

	public async Task<OrderBook> GetOrderBookAsync(string symbol, CancellationToken cancellationToken)
	{
		return await MakeRequestAsync<OrderBook>(CreateUrl($"orders/{symbol}"), CreateRequest(Method.Get), false, cancellationToken);
	}

	public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(string symbol, CancellationToken cancellationToken)
	{
		return await MakeRequestAsync<IEnumerable<Trade>>(CreateUrl($"history/{symbol}"), CreateRequest(Method.Get), false, cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetOrdersAsync(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (!symbol.IsEmpty())
			request.AddParameter("market", symbol);

		return await MakeRequestAsync<IEnumerable<Order>>(CreateUrl("account/orders"), request, true, cancellationToken);
	}

	public async Task<Order> GetOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		return await MakeRequestAsync<Order>(CreateUrl($"account/order/{orderId}"), CreateRequest(Method.Get), true, cancellationToken);
	}

	public async Task<IDictionary<string, double>> GetBalancesAsync(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequestAsync<object>(CreateUrl("account/balances"), CreateRequest(Method.Get), true, cancellationToken);

		return ((JToken)response.balances).DeserializeObject<IDictionary<string, double>>();
	}

	public async Task<string> RegisterOrderAsync(string symbol, Sides side, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("market", symbol)
			.AddParameter("quantity", volume);

		if (price != null)
			request.AddParameter("price", price.Value);

		dynamic response = await MakeRequestAsync<object>(CreateUrl($"order/{side.ToNative()}"), request, true, cancellationToken);

		return (string)response.uuid;
	}

	public async Task CancelOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (!orderId.IsEmpty())
			request.AddParameter("uuid", orderId);

		await MakeRequestAsync<object>(CreateUrl("order/cancel"), request, true, cancellationToken);
	}

	private static Uri CreateUrl(string methodName, string version = "v1/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, bool isPrivate, CancellationToken cancellationToken)
	{
		var auth = isPrivate ? new HttpBasicAuthenticator(_key.UnSecure(), _secret.UnSecure()) : null;

		dynamic obj = (await request.InvokeAsync2<object>(url, this, this.AddVerboseLog, cancellationToken, auth: auth)).Data;

		if (obj is JObject)
		{
			if ((bool?)obj.success == false)
				throw new InvalidOperationException((string)obj.error);
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}
