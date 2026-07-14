namespace StockSharp.Tradier.Native;

using Newtonsoft.Json.Linq;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _bearer;

	private readonly string _baseUrl;
	private const string _version = "v1";

	public HttpClient(bool sandbox, SecureString bearer)
	{
		if (bearer.IsEmpty())
			throw new ArgumentNullException(nameof(bearer));

		var url = sandbox ? "sandbox" : "api";
		_baseUrl = $"https://{url}.tradier.com";
		_bearer = bearer;
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Tradier) + "_" + nameof(HttpClient);

	public async Task<(string url, string sessionId)> CreateMarketStreaming(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl("markets/events/session"), CreateRequest(Method.Post), cancellationToken);

		var url = (string)response.stream.url;
		var sessionId = (string)response.stream.sessionid;

		return (url, sessionId);
	}

	public async Task<(string url, string sessionId)> CreateAccountStreaming(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl("accounts/events/session"), CreateRequest(Method.Post), cancellationToken);

		var url = (string)response.stream.url;
		var sessionId = (string)response.stream.sessionid;

		return (url, sessionId);
	}

	private static IEnumerable<T> Deserialize<T>(JToken token)
	{
		if (token is JArray arr)
			return arr.DeserializeObject<IEnumerable<T>>() ?? [];
		else
		{
			var t = token.DeserializeObject<T>();
			return t == null ? [] : [t];
		}
	}

	public async Task<IEnumerable<Symbol>> GetSymbols(string query, string exchanges, string types, CancellationToken cancellationToken)
	{
		var url = CreateUrl("markets/lookup");
		var qs = url.QueryString;

		if (!query.IsEmpty())
			qs.Append("q", query);

		if (!exchanges.IsEmpty())
			qs.Append("exchanges", exchanges);

		if (!types.IsEmpty())
			qs.Append("types", types);

		dynamic response = await MakeRequest<object>(url, CreateRequest(Method.Get), cancellationToken);

		return Deserialize<Symbol>((JToken)response.securities.security);
	}

	public async Task<IEnumerable<DateTime>> GetOptionExpirations(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("includeAllRoots", true)
			//.AddParameter("strikes", true)
			//.AddParameter("contractSize", true)
			//.AddParameter("expirationType", true)
			;

		dynamic response = await MakeRequest<object>(CreateUrl("markets/options/expirations"), request, cancellationToken);

		return Deserialize<DateTime>((JToken)response.expirations.date);
	}

	public async Task<IEnumerable<Option>> GetOptionChain(string symbol, DateTime expiration, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("expiration", expiration.ToString("yyyy-MM-dd"));

		dynamic response = await MakeRequest<object>(CreateUrl("markets/options/chains"), request, cancellationToken);

		return Deserialize<Option>((JToken)response.options.option);
	}

	public async Task<IEnumerable<Ohlc>> GetCandles(bool isIntraday, string symbol, string interval, DateTime? start, DateTime? end, bool isRth, CancellationToken cancellationToken)
	{
		var url = CreateUrl(isIntraday ? "markets/timesales" : "markets/history");
		var qs = url.QueryString;

		qs
			.Append("symbol", symbol)
			.Append("interval", interval);

		if (start != null)
			qs.Append("start", start.Value.ToString(isIntraday ? "yyyy-MM-dd hh:mm" : "yyyy-MM-dd"));

		if (end != null)
			qs.Append("end", end.Value.ToString(isIntraday ? "yyyy-MM-dd hh:mm" : "yyyy-MM-dd"));

		if (isIntraday && isRth)
			qs.Append("session_filter", "open");

		dynamic response = await MakeRequest<object>(url, CreateRequest(Method.Get), cancellationToken);

		response = isIntraday ? response.series.data : response.history.day;

		return ((JToken)response).DeserializeObject<IEnumerable<Ohlc>>();
	}

	public async Task<IEnumerable<Dividend>> GetDividends(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbols", symbol);

		var response = await MakeRequest<JArray>(CreateUrl("markets/fundamentals/dividends", "beta"), request, cancellationToken);

		if (response.Count == 0)
			return [];

		var results = (JArray)((dynamic)response[0]).results;

		return results.SelectMany(item => Deserialize<Dividend>((JToken)((dynamic)item).tables.cash_dividends));
	}

	public async Task<IEnumerable<Account>> GetAccounts(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl("user/profile"), CreateRequest(Method.Get), cancellationToken);

		return Deserialize<Account>((JToken)response.profile.account);
	}

	public async Task<(Balance balance, Cash cash)> GetBalances(string accountId, CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl($"accounts/{accountId}/balances"), CreateRequest(Method.Get), cancellationToken);

		return (((JToken)response.balances).DeserializeObject<Balance>(), ((JToken)response.cash).DeserializeObject<Cash>());
	}

	public async Task<IEnumerable<Position>> GetPositions(string accountId, CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl($"accounts/{accountId}/positions"), CreateRequest(Method.Get), cancellationToken);

		var positions = response.positions;

		if (positions is JValue jv && jv.Value is string str && str == "null")
			return [];

		return Deserialize<Position>((JToken)positions.position);
	}

	public async Task<IEnumerable<Order>> GetOrders(string accountId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("includeTags", true);

		dynamic response = await MakeRequest<object>(CreateUrl($"accounts/{accountId}/orders"), request, cancellationToken);

		var orders = response.orders;

		if (orders is JValue jv && jv.Value is string str && str == "null")
			return [];

		return Deserialize<Order>((JToken)orders.order);
	}

	public async Task<long> RegisterOrder(string accountId, TradierOrderClasses @class, string symbol, TradierOrderDurations duration, TradierOrderSides side,
		decimal? price, decimal quantity, TradierOrderTypes type, decimal? stop, string optionSymbol, string tag, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("class", @class.ToNative())
			.AddParameter("symbol", symbol)
			.AddParameter("duration", duration.ToNative())
			.AddParameter("side", side.ToNative())
			.AddParameter("quantity", quantity)
			.AddParameter("type", type.ToNative());

		if (price != null)
			request.AddParameter("price", price.Value);

		if (stop != null)
			request.AddParameter("stop", stop.Value);

		if (!optionSymbol.IsEmpty())
			request.AddParameter("option_symbol", optionSymbol);

		if (!tag.IsEmpty())
			request.AddParameter("tag", tag);

		dynamic response = await MakeRequest<object>(CreateUrl($"accounts/{accountId}/orders"), request, cancellationToken);

		if (response.errors is not null)
			throw new InvalidOperationException(((JArray)response.errors.error).DeserializeObject<string[]>().JoinSpace());

		var order = response.order;

		if (order.status != null && order.status != "ok")
			throw new InvalidOperationException((string)order.status);

		return (long)order.id;
	}

	public async Task<long> ChangeOrder(string accountId, long orderId, TradierOrderDurations duration, decimal? price, TradierOrderTypes type, decimal? stop, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Put);

		request
			.AddParameter("duration", duration.ToNative())
			.AddParameter("type", type.ToNative());

		if (price != null)
			request.AddParameter("price", price.Value);

		if (stop != null)
			request.AddParameter("stop", stop.Value);

		dynamic response = await MakeRequest<object>(CreateUrl($"accounts/{accountId}/orders/{orderId}"), request, cancellationToken);

		response = response.order;

		if (response.status != null && response.status != "ok")
			throw new InvalidOperationException((string)response.status);

		return (long)response.id;
	}

	public async Task CancelOrder(string accountId, long orderId, CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequest<object>(CreateUrl($"accounts/{accountId}/orders/{orderId}"), CreateRequest(Method.Delete), cancellationToken);

		response = response.order;

		if (response.status != null && response.status != "ok")
			throw new InvalidOperationException((string)response.status);
	}

	private Url CreateUrl(string methodName, string version = "v1")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"{_baseUrl}/{version}/{methodName}");
	}

	private RestRequest CreateRequest(Method method)
	{
		var request = new RestRequest((string)null, method);

		request.SetBearer(_bearer);

		return request;
	}

	private Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
		=> request.InvokeAsync<T>(url, this, this.AddVerboseLog, cancellationToken);
}
