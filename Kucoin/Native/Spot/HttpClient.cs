namespace StockSharp.Kucoin.Native.Spot;

using System.Dynamic;

using Newtonsoft.Json.Linq;

using RestSharp;

using StockSharp.Kucoin.Native.Spot.Model;

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

	public Task<IEnumerable<Symbol>> GetSymbols(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("symbols"), CreateRequest(Method.Get), false, null, cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandles(string symbol, string type, long startAt, long endAt, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("type", type)
			.AddParameter("startAt", startAt)
			.AddParameter("endAt", endAt);

		return MakeRequest<IEnumerable<Ohlc>>(CreateUrl($"market/candles?symbol={symbol}"), request, false, null, cancellationToken);
	}

	public Task<Level2> GetLevel2(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequest<Level2>(CreateUrl($"market/orderbook/level2?symbol={symbol}", 3), request, true, null, cancellationToken);
	}

	public Task<IEnumerable<Position>> GetPositions(string currency, string type, CancellationToken cancellationToken)
	{
		var url = CreateUrl("accounts");
		var request = CreateRequest(Method.Get);

		if (!currency.IsEmpty())
			request.AddParameter("currency", currency);

		if (!type.IsEmpty())
			request.AddParameter("type", type);

		return MakeRequest<IEnumerable<Position>>(url, request, true, null, cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetOrders(bool stop, CancellationToken cancellationToken, string status = "active", string symbol = null, string side = null, string type = null, long? startAt = null, long? endAt = null)
	{
		var url = CreateUrl(stop ? "stop-order" : "orders");
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
		bool? postOnly, decimal? visibleVolume, MarginModes? isMargin, CancellationToken cancellationToken)
	{
		var prefix = isMargin is not null ? "margin/" : string.Empty;
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

	public async Task<string> RegisterStopOrder(string clientOid, string side, string symbol, string type, string comment,
		decimal? price, decimal volume, decimal? stopPrice, string stop, string timeInForce, long? cancelAfter,
		bool? postOnly, decimal? visibleVolume, MarginModes? marginMode, CancellationToken cancellationToken)
	{
		var url = CreateUrl("stop-order");

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

		if (!stop.IsEmpty())
			body.stop = stop;

		if (stopPrice != null)
			body.stopPrice = stopPrice.Value;

		if (marginMode is not null)
			body.tradeType = marginMode == MarginModes.Cross ? "MARGIN_TRADE" : "MARGIN_ISOLATED_TRADE";

		dynamic response = await MakeRequest<object>(url, request, true, (object)body, cancellationToken);

		return (string)response.orderId;
	}

	public Task CancelOrder(string clientOid, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"order/client-order/{clientOid}");
		return MakeRequest<object>(url, CreateRequest(Method.Delete), true, null, cancellationToken);
	}

	public Task CancelStopOrder(string clientOid, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"stop-order/cancelOrderByClientOid?clientOid={clientOid}");
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
		var url = CreateUrl("stop-order/cancel");
		var request = CreateRequest(Method.Delete);

		if (!symbol.IsEmpty())
			request.AddParameter("symbol", symbol);

		return MakeRequest<object>(url, request, true, null, cancellationToken);
	}

	public async Task<string> Withdraw(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = CreateUrl("withdrawals");
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.currency = symbol;
		body.address = info.CryptoAddress;
		body.amount = volume;

		if (!info.Comment.IsEmpty())
			body.remark = info.Comment;

		dynamic response = await MakeRequest<object>(url, request, true, (object)body, cancellationToken);

		return (string)response.withdrawalId;
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