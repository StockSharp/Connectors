namespace StockSharp.GateIO.Native.Options;

using System.Net;

using Ecng.ComponentModel;

using StockSharp.GateIO.Native.Options.Model;

class HttpClient : BaseLogReceiver
{
	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;
	private const string _version = "v4";

	public HttpClient(GateIOMessageAdapter adapter, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseUrl = adapter.IsDemo ? "https://fx-api-testnet.gateio.ws" : $"https://{adapter.RestDomain}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(GateIO) + "_" + nameof(Options) + nameof(HttpClient);

	public async Task<IEnumerable<string>> GetUnderlyings(CancellationToken cancellationToken)
	{
		var arr = await MakeRequest<IEnumerable<object>>(CreateUrl("options/underlyings"), CreateRequest(Method.Get), cancellationToken);

		return arr.Select(i => (string)((dynamic)i).name).ToArray();
	}

	public Task<IEnumerable<Symbol>> GetSymbols(string underlying, long? expiration, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddParameter("underlying", underlying)
		;

		if (expiration is long e)
			request.AddParameter("expiration", e);

		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("options/contracts"), request, cancellationToken);
	}

	public Task<RestOrderBook> GetOrderBook(string contract, int limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("contract", contract)
			.AddQueryParameter("limit", limit)
			.AddQueryParameter("with_id", "true")
			;

		return MakeRequest<RestOrderBook>(CreateUrl("options/order_book"), request, cancellationToken);
	}

	public Task<RestCandle[]> GetCandles(string symbol, string interval, long? from, long? to, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/candlesticks");

		var request = CreateRequest(Method.Get)
			.AddParameter("contract", symbol)
			.AddParameter("interval", interval);

		if (from.HasValue)
			request.AddParameter("from", from.Value);

		if (to.HasValue)
			request.AddParameter("to", to.Value);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<RestCandle[]>(url, request, cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrders(CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/orders");

		var request = CreateRequest(Method.Get)
			.AddQueryParameter("status", "open");

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Balance> GetBalance(CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/accounts");
		var request = CreateRequest(Method.Get);

		return MakeRequest<Balance>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Position>> GetPositions(CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/positions");
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Position>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> CreateOrder(string text, string contract, decimal size, decimal price, string timeInForce, decimal? iceberg, bool? reduceOnly, CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/orders");

		var body = new Dictionary<string, object>
		{
			{ "text", text },
			{ "contract", contract },
			{ "size", (long)size },
			{ "price", price.ToString() },
		};

		if (!timeInForce.IsEmpty())
			body.Add("tif", timeInForce);

		if (reduceOnly is bool ro)
			body.Add("reduce_only", ro);

		if (iceberg is decimal ib)
			body.Add("iceberg", (long)ib);

		var request = CreateRequest(Method.Post)
			.AddJsonBody(body.ToJson())
			.ApplyBrokerRef();

		return MakeRequest<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> CancelOrder(long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/orders/{orderId}");
		return MakeRequest<Order>(url, ApplySecret(CreateRequest(Method.Delete), url), cancellationToken);
	}

	public Task<IEnumerable<Order>> CancelAllOrders(string contract, CancellationToken cancellationToken)
	{
		var url = CreateUrl("options/orders");

		var request = CreateRequest(Method.Delete);

		if (!contract.IsEmpty())
			request.AddQueryParameter("contract", contract);

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
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

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
		=> (await request.InvokeAsync3<T>(url, this, this.AddVerboseLog, cancellationToken, handleErrorStatus: status => status == HttpStatusCode.Created)).Data;
}