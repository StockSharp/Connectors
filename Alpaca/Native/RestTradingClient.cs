namespace StockSharp.Alpaca.Native;

using System.Dynamic;

class RestTradingClient : RestAlpacaClient
{
	public RestTradingClient(bool isDemo, SecureString key, SecureString secret)
		: base("https://{0}api.alpaca.markets".Put(isDemo ? "paper-" : string.Empty), key, secret)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(RestTradingClient);

	public Task<IEnumerable<Asset>> GetAssets(CancellationToken cancellationToken)
		=> MakeRequest<IEnumerable<Asset>>("v2/assets", CreateRequest(Method.Get), cancellationToken);

	public Task<Account> GetAccount(CancellationToken cancellationToken)
		=> MakeRequest<Account>("v2/account", CreateRequest(Method.Get), cancellationToken);

	public Task<IEnumerable<Order>> GetOrders(CancellationToken cancellationToken)
		=> MakeRequest<IEnumerable<Order>>(GetOrderUrlPart(), CreateRequest(Method.Get), cancellationToken);

	public Task<Order> CreateOrder(long transactionId, string symbol, decimal qty,
		string side, string type, string tif, decimal? limitPrice, decimal? stopPrice,
		decimal? trailPrice, decimal? trailPercent, bool? extendedHours,
		string orderClass,
		CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.symbol = symbol;
		body.qty = qty.ToString();
		body.side = side;
		body.type = type;
		body.time_in_force = tif;

		if (limitPrice is not null)
			body.limit_price = limitPrice.Value.ToString();

		if (stopPrice is not null)
			body.stop_price = stopPrice.Value.ToString();

		if (trailPrice is not null)
			body.trail_price = trailPrice.Value.ToString();

		if (trailPercent is not null)
			body.trail_percent = trailPercent.Value.ToString();

		if (extendedHours is not null)
			body.extended_hours = extendedHours.Value;

		if (!orderClass.IsEmpty())
			body.order_class = orderClass;

		body.client_order_id = transactionId.ToString();

		request.AddJsonBody((object)body);

		return MakeRequest<Order>(GetOrderUrlPart(), request, cancellationToken);
	}

	public Task<Order> ReplaceOrder(long transactionId, string id, decimal qty, string tif,
		decimal? limitPrice, decimal? stopPrice, decimal? trail, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Patch);

		dynamic body = new ExpandoObject();

		body.qty = qty.ToString();
		body.time_in_force = tif;

		if (limitPrice is not null)
			body.limit_price = limitPrice.Value.ToString();

		if (stopPrice is not null)
			body.stop_price = stopPrice.Value.ToString();

		if (trail is not null)
			body.trail = trail.Value.ToString();

		body.client_order_id = transactionId.ToString();

		request.AddJsonBody((object)body);

		return MakeRequest<Order>(GetOrderUrlPart($"/{id}"), request, cancellationToken);
	}

	public async Task DeleteOrder(string id, CancellationToken cancellationToken)
	{
		try
		{
			await MakeRequest(GetOrderUrlPart($"/{id}"), CreateRequest(Method.Delete), cancellationToken);
		}
		catch (Ecng.Net.RestSharpException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NoContent)
		{
			// Alpaca returns HTTP 204 No Content for a successful DELETE /orders/{id},
			// which the generic REST helper treats as a non-OK response. It's a success —
			// swallow it so CancelOrderAsync doesn't raise OrderCancelFailReceived.
		}
	}

	public async Task DeleteOrders(CancellationToken cancellationToken)
	{
		try
		{
			await MakeRequest(GetOrderUrlPart(), CreateRequest(Method.Delete), cancellationToken);
		}
		catch (Ecng.Net.RestSharpException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NoContent)
		{
		}
	}

	private static string GetOrderUrlPart(string urlPart = default)
		=> $"v2/orders{urlPart}";

	public Task<IEnumerable<Position>> GetPositions(CancellationToken cancellationToken)
		=> MakeRequest<IEnumerable<Position>>("v2/positions", CreateRequest(Method.Get), cancellationToken);
}