namespace StockSharp.Digifinex.Native;

using System.Security.Cryptography;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;

	private readonly HashAlgorithm _hasher;

	private readonly string _baseUrl = "https://openapi.digifinex.{0}";

	public HttpClient(SecureString key, SecureString secret, string domain)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());

		_baseUrl = _baseUrl.Put(domain);
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Digifinex) + "_" + nameof(HttpClient);

	public async Task<IEnumerable<Symbol>> GetSpotSymbols(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl("spot/symbols"), CreateRequest(Method.Get), cancellationToken);

		return ((JToken)response.symbol_list).DeserializeObject<IEnumerable<Symbol>>();
	}

	public async Task<IEnumerable<Symbol>> GetMarginSymbols(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl("margin/symbols"), CreateRequest(Method.Get), cancellationToken);

		return ((JToken)response.symbol_list).DeserializeObject<IEnumerable<Symbol>>();
	}

	public Task<IEnumerable<Trade>> GetTrades(string symbol, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("symbol", symbol);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<IEnumerable<Trade>>(CreateUrl("trades"), request, cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandles(string symbol, string period, long? start, long? end, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("period", period);

		if (start != null)
			request.AddParameter("start_time", start.Value);

		if (end != null)
			request.AddParameter("end_time", end.Value);

		return MakeRequest<IEnumerable<Ohlc>>(CreateUrl("kline"), ApplySecret(request), cancellationToken);
	}

	public async Task<IEnumerable<SpotAsset>> GetSpotAssets(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		dynamic response = await MakeRequest<object>(CreateUrl("spot/assets"), ApplySecret(request), cancellationToken);

		var jt = (JToken)response.list;

		if (jt.Type == JTokenType.Object)
			return [];

		return jt.DeserializeObject<IEnumerable<SpotAsset>>();
	}

	public async Task<IEnumerable<MarginPosition>> GetPositions(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		dynamic response = await MakeRequest<object>(CreateUrl("margin/positions"), ApplySecret(request), cancellationToken);

		return ((JToken)response.positions).DeserializeObject<IEnumerable<MarginPosition>>();
	}

	public Task<IEnumerable<Order>> GetOpenOrders(string market, string symbol/*, string type = null, int? page = null*/, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		//if (!type.IsEmpty())
		//	request.AddParameter("type", type);

		//if (page != null)
		//	request.AddParameter("page", page.Value);
		
		return MakeRequest<IEnumerable<Order>>(CreateUrl($"{market}/order/current"), ApplySecret(request), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOrdersInfo(string market, IEnumerable<string> orderIds, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("order_id", orderIds.JoinComma());

		return MakeRequest<IEnumerable<Order>>(CreateUrl($"{market}/order"), ApplySecret(request), cancellationToken);
	}

	public async Task<string> RegisterOrder(string market, string symbol, string type, decimal? price, decimal volume, bool postOnly, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("type", type)
			.AddParameter("amount", volume);

		if (price != null)
			request.AddParameter("price", price.Value);

		request.AddParameter("post_only", postOnly ? 1 : 0);

		dynamic response = await MakeRequest<object>(CreateUrl($"{market}/order/new"), ApplySecret(request), cancellationToken);

		return (string)response.order_id;
	}

	public async Task CancelOrder(string market, string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("order_id", orderId);

		dynamic response = await MakeRequest<object>(CreateUrl($"{market}/order/cancel"), ApplySecret(request), cancellationToken);

		if (response.error != null && ((JToken)response.error).DeserializeObject<string[]>().Contains(orderId))
			throw new InvalidOperationException();
	}

	public Task<IEnumerable<OwnTrade>> GetOwnTrades(string market, string symbol, int? start, int? end, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		if (start != null)
			request.AddParameter("start_time", start.Value);

		if (end != null)
			request.AddParameter("end_time", end.Value);

		return MakeRequest<IEnumerable<OwnTrade>>(CreateUrl($"{market}/mytrades"), ApplySecret(request), cancellationToken);
	}

	private Uri CreateUrl(string methodName, string version = "v3/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var str = request
			.Parameters
			.Where(p => (p.Type == ParameterType.QueryString || p.Type == ParameterType.GetOrPost) && p.Value != null)
			.ToQueryString(false);

		var signature = _hasher
		    .ComputeHash(str.UTF8())
		    .Digest()
		    .ToLowerInvariant();

		request.AddHeader("ACCESS-KEY", _key.UnSecure());
		request.AddHeader("ACCESS-SIGN", signature);
		request.AddHeader("ACCESS-TIMESTAMP", ((long)DateTime.UtcNow.ToUnix()).To<string>());

		return request;
	}

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.code != null && obj.code != 0)
				throw new InvalidOperationException(((int)obj.code).GetErrorText());

			if (obj.data != null)
				obj = obj.data;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}