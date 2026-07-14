namespace StockSharp.Kucoin.Native.Futures;

using System.Dynamic;

using Newtonsoft.Json.Linq;

using RestSharp;

using StockSharp.Kucoin.Native.Futures.Model;

class HttpClient : BaseLogReceiver
{
	private readonly bool _isDemo;
	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;

	public HttpClient(bool isDemo, string baseUrl, Authenticator authenticator)
	{
		_isDemo = isDemo;
		_baseUrl = baseUrl.ThrowIfEmpty(nameof(baseUrl));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Kucoin) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Contract>> GetContracts(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Contract>>(CreateUrl("contracts/active"), CreateRequest(Method.Get), false, null, cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandles(string symbol, int minutes, long startAt, long endAt, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("granularity", minutes)
			.AddParameter("from", startAt)
			.AddParameter("to", endAt);

		return MakeRequest<IEnumerable<Ohlc>>(CreateUrl($"kline/query?symbol={symbol}"), request, false, null, cancellationToken);
	}

	public Task<Level2> GetLevel2(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequest<Level2>(CreateUrl($"level2/snapshot?symbol={symbol}"), request, false, null, cancellationToken);
	}

	public Task<IEnumerable<Position>> GetPositions(string currency, CancellationToken cancellationToken)
	{
		var url = CreateUrl("positions");
		var request = CreateRequest(Method.Get);

		if (!currency.IsEmpty())
			request.AddParameter("currency", currency);

		return MakeRequest<IEnumerable<Position>>(url, request, true, null, cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetOrders(bool stop, CancellationToken cancellationToken, string status = "active", string symbol = null, string side = null, string type = null, long? startAt = null, long? endAt = null)
	{
		var url = CreateUrl(stop ? "stopOrders" : "orders");
		var request = CreateRequest(Method.Get);

		if (!status.IsEmpty())
			url.QueryString.Append("status", status);

		if (!symbol.IsEmpty())
			url.QueryString.Append("symbol", symbol);

		if (!side.IsEmpty())
			url.QueryString.Append("side", side);

		if (!type.IsEmpty())
			url.QueryString.Append("type", type);

		if (startAt != null)
			url.QueryString.Append("startAt", startAt.Value);

		if (endAt != null)
			url.QueryString.Append("endAt", endAt.Value);

		dynamic response = await MakeRequest<object>(url, request, true, null, cancellationToken);

		return ((JToken)response.items).DeserializeObject<IEnumerable<Order>>();
	}

	public async Task<string> RegisterOrder(string clientOid, string side, string symbol, string type, string comment,
		decimal? price, decimal volume, string timeInForce, long? cancelAfter,
		bool? postOnly, decimal? visibleVolume, MarginModes? marginMode, CancellationToken cancellationToken)
	{
		var prefix = marginMode is not null ? "margin/" : string.Empty;
		var url = CreateUrl($"{prefix}{(_isDemo ? "orders/test" : "orders")}");

		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.clientOid = clientOid;
		body.side = side;
		body.symbol = symbol;
		body.type = type;

		if (price != null)
			body.price = price.Value;

		body.size = volume;

		if (!timeInForce.IsEmpty())
		{
			body.timeInForce = timeInForce;

			if (cancelAfter != null)
				body.cancelAfter = cancelAfter.Value;
		}

		if (postOnly != null)
			body.postOnly = postOnly.Value;

		if (visibleVolume != null)
		{
			if (visibleVolume == 0)
			{
				body.hidden = true;
			}
			else
			{
				body.iceberg = true;
				body.visibleSize = visibleVolume.Value;
			}
		}

		if (!comment.IsEmpty())
			body.remark = comment;

		dynamic response = await MakeRequest<object>(url, request, true, (object)body, cancellationToken);

		return (string)response.orderId;
	}

	public Task CancelOrder(string clientOid, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"orders/client-order/{clientOid}");
		return MakeRequest<object>(url, CreateRequest(Method.Delete), true, null, cancellationToken);
	}

	public Task CancelAllOrders(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl("orders");
		var request = CreateRequest(Method.Delete);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		return MakeRequest<object>(url, request, true, null, cancellationToken);
	}

	public Task CancelAllStopOrders(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl("stopOrders");
		var request = CreateRequest(Method.Delete);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		return MakeRequest<object>(url, request, true, null, cancellationToken);
	}

	public Task<PublicInfo> GetPublicInfo(CancellationToken cancellationToken)
	{
		return MakeRequest<PublicInfo>(CreateUrl("bullet-public"), CreateRequest(Method.Post), false, null, cancellationToken);
	}

	public Task<PrivateInfo> GetPrivateInfo(CancellationToken cancellationToken)
	{
		return MakeRequest<PrivateInfo>(CreateUrl("bullet-private"), CreateRequest(Method.Post), true, null, cancellationToken);
	}

	private Url CreateUrl(string methodName, int version = 1)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"{_baseUrl}/api/v{version}/{methodName}");
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private async Task<T> MakeRequest<T>(Url url, RestRequest request, bool auth, object body, CancellationToken cancellationToken)
	{
		if (auth)
			_authenticator.Sign(request, url, body);

		var response = await request.InvokeAsync2<object>(url, this, this.AddVerboseLog, cancellationToken);

		dynamic obj = response.Data;

		if (obj is JObject && obj.msg != null)
			throw new InvalidOperationException((string)obj.msg);

		return ((JToken)obj.data).DeserializeObject<T>();
	}
}