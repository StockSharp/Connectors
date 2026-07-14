namespace StockSharp.GateIO.Native.Spot;

using Ecng.ComponentModel;

using StockSharp.GateIO.Native.Spot.Model;

class HttpClient : BaseLogReceiver
{
	private readonly Authenticator _authenticator;

	private readonly string _baseUrl = "https://api.gateio.ws";
	private const string _version = "v4";

	public HttpClient(GateIOMessageAdapter adapter, Authenticator authenticator)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_baseUrl = $"https://{adapter.RestDomain}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => nameof(GateIO) + "_" + nameof(Spot) + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("spot/currency_pairs"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<RestCandle[]> GetCandles(string symbol, string interval, long? from, long? to, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"spot/candlesticks");

		var request = CreateRequest(Method.Get)
			.AddParameter("currency_pair", symbol)
			.AddParameter("interval", interval);

		if (from.HasValue)
			request.AddParameter("from", from.Value);

		if (to.HasValue)
			request.AddParameter("to", to.Value);

		if (limit.HasValue)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<RestCandle[]>(url, request, cancellationToken);
	}

	public Task<RestOrderBook> GetOrderBook(string contract, int limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get)
			.AddQueryParameter("currency_pair", contract)
			.AddQueryParameter("limit", limit)
			.AddQueryParameter("with_id", "true")
			;

		return MakeRequest<RestOrderBook>(CreateUrl("spot/order_book"), request, cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetOpenOrders(CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/open_orders");
		var request = CreateRequest(Method.Get);

		var response = await MakeRequest<JArray>(url, ApplySecret(request, url), cancellationToken);
		return response.SelectMany(item => ((JToken)((dynamic)item).orders).DeserializeObject<IEnumerable<Order>>());
	}

	public Task<IEnumerable<Balance>> GetBalance(CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/accounts");
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async ValueTask Withdraw(string currency, decimal amount, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = CreateUrl("withdrawals");
		var request = CreateRequest(Method.Post);

		request.AddJsonBody(new
		{
			currency,
			amount = amount.ToString(),
			address = info.CryptoAddress,
			memo = info.Comment
		});

		await MakeRequest<object>(url, ApplySecret(request, url), cancellationToken);
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