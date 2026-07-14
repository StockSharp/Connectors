namespace StockSharp.DXtrade.Native;

using System.Dynamic;

class HttpClient : BaseLogReceiver
{
	private class MarketDataList<T>
	{
		[JsonProperty("events")]
		public T[] Events { get; set; }
	}

	public class OrderResponse
	{
		[JsonProperty("orderId")]
		public long OrderId { get; set; }

		[JsonProperty("updateOrderId")]
		public long UpdateOrderId { get; set; }
	}

	private readonly string _baseUrl;
	private SecureString _token;

	public HttpClient(string baseUrl)
	{
		_baseUrl = baseUrl.ThrowIfEmpty(nameof(baseUrl));
	}

	public override string Name => nameof(DXtrade) + "_" + nameof(HttpClient);

	public async Task<IEnumerable<Instrument>> GetInstruments(string symbol, string type, CancellationToken cancellationToken)
	{
		RestRequest request;

		if (!symbol.IsEmpty() && type.IsEmpty())
		{
			request = CreateRequest(Method.Get, $"instruments/{symbol}");
		}
		else if (symbol.IsEmpty() && !type.IsEmpty())
		{
			request = CreateRequest(Method.Get, $"instruments/type/{type}");
		}
		else
		{
			request = CreateRequest(Method.Get, "instruments/query");

			if (!symbol.IsEmpty())
				request.AddQueryParameter("symbols", symbol);

			if (!type.IsEmpty())
				request.AddQueryParameter("types", type);
		}

		dynamic response = await MakeRequest<object>(request, true, cancellationToken);
		return ((JToken)response.instruments).DeserializeObject<IEnumerable<Instrument>>();
	}

	public async Task<Candle[]> GetCandles(string account, string symbol, string timeFrame, object fromTime, object toTime, int? count, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "marketdata");

		var body = new MarketDataRequest
		{
			Account = account,
			Symbols = [symbol],
			EventTypes =
			[
				new()
				{
					Type = MarketDataType.Candle,
					CandleType = timeFrame,
					FromTime = fromTime,
					ToTime = toTime,
					Count = count,
					Format = "COMPACT",
				}
			]
		};

		request.AddJsonBody(body);

		var list = await MakeRequest<MarketDataList<Candle>>(request, true, cancellationToken);
		return list.Events;
	}

	public async Task<Quote[]> GetQuotes(string account, string symbol, object fromTime, object toTime, int? count, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "marketdata");

		var body = new MarketDataRequest
		{
			Account = account,
			Symbols = [symbol],
			EventTypes =
			[
				new()
				{
					Type = MarketDataType.Quote,
					FromTime = fromTime,
					ToTime = toTime,
					Count = count,
					Format = "COMPACT",
				}
			]
		};

		request.AddJsonBody(body);

		var list = await MakeRequest<MarketDataList<Quote>>(request, true, cancellationToken);
		return list.Events;
	}

	public async Task<IEnumerable<Order>> GetOpenOrders(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "accounts/orders");
		dynamic response = await MakeRequest<object>(request, true, cancellationToken);
		return ((JToken)response.orders).DeserializeObject<IEnumerable<Order>>();
	}

	public Task<OrderResponse> PlaceOrder(
		string account, string orderCode, string type, string instrument,
		decimal quantity, string positionEffect, string positionCode, string side, decimal? limitPrice,
		decimal? stopPrice, decimal? priceOffset, string priceLink, string tif, decimal? marginRate,
		object expireDate, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, $"accounts/{account}/orders");

		var body = (IDictionary<string, object>)new ExpandoObject();

		body[nameof(account)] = account;
		body[nameof(orderCode)] = orderCode;
		body[nameof(type)] = type;
		body[nameof(instrument)] = instrument;
		body[nameof(quantity)] = quantity;
		body[nameof(side)] = side;

		if (!positionEffect.IsEmpty())
			body[nameof(positionEffect)] = positionEffect;

		if (!positionCode.IsEmpty())
			body[nameof(positionCode)] = positionCode;

		if (limitPrice is not null)
			body[nameof(limitPrice)] = limitPrice.Value;

		if (stopPrice is not null)
			body[nameof(stopPrice)] = stopPrice.Value;

		if (priceOffset is not null)
			body[nameof(priceOffset)] = priceOffset.Value;

		if (!priceLink.IsEmpty())
			body[nameof(priceLink)] = priceLink;

		if (!tif.IsEmpty())
			body[nameof(tif)] = tif;

		if (marginRate is not null)
			body[nameof(marginRate)] = marginRate.Value;

		if (expireDate is not null)
			body[nameof(expireDate)] = expireDate;

		request.AddJsonBody(body);

		return MakeRequest<OrderResponse>(request, true, cancellationToken);
	}

	public Task<OrderResponse> ModifyOrder(
		string account, string orderCode, string instrument,
		decimal? quantity, string side, decimal? limitPrice, decimal? stopPrice,
		string tif, object expireDate, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Put, $"accounts/{account}/orders");

		var body = new
		{
			account,
			orderCode,
			instrument,
			quantity,
			side,
			limitPrice,
			stopPrice,
			tif,
			expireDate
		};

		request.AddJsonBody(body);

		return MakeRequest<OrderResponse>(request, true, cancellationToken);
	}

	public Task<OrderResponse> CancelOrder(string account, long clientOrderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete, $"accounts/{account}/orders/{clientOrderId}");
		return MakeRequest<OrderResponse>(request, true, cancellationToken);
	}

	public async Task<IEnumerable<OrderResponse>> CancelOrderGroup(string account, IEnumerable<string> orderCodes, string contingencyType, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete, $"accounts/{account}/orders/group");

		if (orderCodes?.Any() == true)
		{
			request.AddQueryParameter("order-codes", orderCodes.Join(","));
		}

		if (!contingencyType.IsEmpty())
		{
			request.AddQueryParameter("contingency-type", contingencyType);
		}

		dynamic list = await MakeRequest<object>(request, true, cancellationToken);
		return ((JToken)list.orderResponses).DeserializeObject<IEnumerable<OrderResponse>>();
	}

	public Task<IEnumerable<Position>> GetPositions(string account, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, $"accounts/{account}/positions");
		return MakeRequest<IEnumerable<Position>>(request, true, cancellationToken);
	}

	public async Task<IEnumerable<AccountPortfolio>> GetPortfolio(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get, "accounts/portfolio");
		dynamic list = await MakeRequest<object>(request, true, cancellationToken);
		return ((JToken)list.portfolios).DeserializeObject<IEnumerable<AccountPortfolio>>();
	}

	public async Task<SecureString> CreateSessionToken(string username, string domain, string password, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "login");
		request.AddJsonBody(new { username, domain, password });

		dynamic response = await MakeRequest<object>(request, false, cancellationToken);
		var token = (string)response.sessionToken;
		return _token = token.Secure();
	}

	public Task Ping(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "ping");
		return MakeRequest<object>(request, true, cancellationToken);
	}

	public async Task Logout(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post, "logout");
		await MakeRequest<object>(request, true, cancellationToken, false);
		_token = default;
	}

	private RestRequest Sign(RestRequest request)
	{
		if (_token.IsEmpty())
			throw new InvalidOperationException();

		request.AddHeader("Authorization", $"DXAPI {_token.UnSecure()}");

		return request;
	}

	private static RestRequest CreateRequest(Method method, string endpoint)
	{
		return new(endpoint, method);
	}

	private async Task<T> MakeRequest<T>(RestRequest request, bool sign, CancellationToken cancellationToken, bool throwIfEmptyResponse = true)
	{
		var url = $"{_baseUrl}{request.Resource}".To<Uri>();

		if (sign)
			request = Sign(request);

		dynamic obj = await request.InvokeAsync<object>(url, this, this.AddVerboseLog, cancellationToken, throwIfEmptyResponse: throwIfEmptyResponse);

		if (obj is null)
			return default;

		if (((JToken)obj).Type == JTokenType.Object && obj.errorCode != null)
			throw new InvalidOperationException((string)obj.description);

		return ((JToken)obj).DeserializeObject<T>();
	}
}