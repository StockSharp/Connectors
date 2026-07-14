namespace StockSharp.GateIO.Native.Futures;

using Ecng.ComponentModel;

using StockSharp.GateIO.Native.Futures.Model;

class HttpClient : BaseLogReceiver
{
	private readonly string _coin;
	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;
	private const string _version = "v4";

	public HttpClient(GateIOMessageAdapter adapter, string coin, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseUrl = adapter.IsDemo ? "https://fx-api-testnet.gateio.ws" : $"https://{adapter.RestDomain}";
		_coin = coin.ThrowIfEmpty(nameof(coin));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(GateIO) + "_" + nameof(Futures) + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl($"futures/{_coin}/contracts"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<RestOrderBook> GetOrderBook(string contract, int limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("contract", contract)
			.AddQueryParameter("limit", limit)
			.AddQueryParameter("with_id", "true")
			;

		return MakeRequest<RestOrderBook>(CreateUrl($"futures/{_coin}/order_book"), request, cancellationToken);
	}

	public Task<RestCandle[]> GetCandles(string symbol, string interval, long? from, long? to, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"futures/{_coin}/candlesticks");

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
		var url = CreateUrl($"futures/{_coin}/orders");

		var request = CreateRequest(Method.Get)
			.AddQueryParameter("status", "open");

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Balance> GetBalance(CancellationToken cancellationToken)
	{
		var url = CreateUrl($"futures/{_coin}/accounts");
		var request = CreateRequest(Method.Get);

		return MakeRequest<Balance>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Position>> GetPositions(CancellationToken cancellationToken)
	{
		var url = CreateUrl($"futures/{_coin}/positions");
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Position>>(url, ApplySecret(request, url), cancellationToken);
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