namespace StockSharp.CoinEx.Native.Spot;

using System.Dynamic;

using Ecng.ComponentModel;

using StockSharp.CoinEx.Native.Spot.Model;

class HttpClient : BaseLogReceiver
{
	private readonly Authenticator _authenticator;

	private const string _baseUrl = "https://api.coinex.com";
	private const string _version = "v2";

	public HttpClient(Authenticator authenticator)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	// to get readable name after obfuscation
	public override string Name => nameof(CoinEx) + "_" + nameof(Spot) + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("spot/market"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandles(string symbol, string period, int limit, CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Ohlc>>(CreateUrl($"spot/kline?market={symbol}&period={period}&limit={limit}"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<Order> RegisterOrder(long clientId, string symbol, bool isMargin, string side, string type, decimal? price, decimal volume, bool isHide, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/order");
		var request = CreateRequest(Method.Post);

		IDictionary<string, object> body = new ExpandoObject();

		body.Add("market", symbol);
		body.Add("market_type", isMargin ? "MARGIN" : "SPOT");
		body.Add("side", side);
		body.Add("type", type);
		body.Add("amount", volume.To<string>());
		body.Add("client_id", clientId.To<string>());

		if (price != null)
			body.Add("price", price.Value.To<string>());

		if (isHide)
			body.Add("is_hide", true);

		request.AddBody(body);

		return MakeRequest<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> ModifyOrder(string symbol, long orderId, bool isMargin, string type, decimal price, decimal volume, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/modify-order");
		var request = CreateRequest(Method.Post);

		request.AddBody(new
		{
			market = symbol,
			market_type = isMargin ? "MARGIN" : "SPOT",
			order_id = orderId,
			type,
			amount = volume.To<string>(),
			price = price.To<string>()
		});

		return MakeRequest<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task CancelOrder(string symbol, long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/cancel-order");
		var request = CreateRequest(Method.Delete);

		request
			.AddParameter("id", orderId)
			.AddParameter("market", symbol);

		return MakeRequest<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrders(bool isMargin, int limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl("spot/pending-order");

		var request = CreateRequest(Method.Get);

		request
			.AddParameter("market_type", isMargin ? "MARGIN" : "SPOT")
			.AddParameter(nameof(limit), limit);

		return MakeRequest<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalance(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("assets/spot/balance");

		return MakeRequest<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<long> Withdraw(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = CreateUrl("assets/withdraw");
		var request = CreateRequest(Method.Post);

		request.AddBody(new
		{
			ccy = currency,
			to_address = info.CryptoAddress,
			amount = volume.ToString(),
			memo = info.Comment
		});

		dynamic response = await MakeRequest<object>(url, ApplySecret(request, url), cancellationToken);

		return (long)response.coin_withdraw_id;
	}

	private static Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{_version}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Uri url)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var timestamp = (long)DateTime.UtcNow.ToUnix(false);

		var parameters = request
			.Parameters
			.Where(p => p.Type is ParameterType.GetOrPost or ParameterType.RequestBody && p.Value != null)
			.ToArray();

		var body = string.Empty;

		if (parameters.Length > 0)
		{
			if (request.Method == Method.Post)
				body = parameters.First().Value.ToJson(false);
			else
				body = $"?{parameters.ToQueryString()}";
		}

		var signature = _authenticator.Sign($"{request.Method.ToString().ToUpperInvariant()}{url.PathAndQuery}{body}{timestamp}");

		request
			.AddHeader("X-COINEX-KEY", _authenticator.Key.UnSecure())
			.AddHeader("X-COINEX-SIGN", signature)
			.AddHeader("X-COINEX-TIMESTAMP", timestamp);

		return request;
	}

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj.code != null && obj.code > 0)
			throw new InvalidOperationException((string)obj.message);

		return ((JToken)obj.data).DeserializeObject<T>();
	}
}