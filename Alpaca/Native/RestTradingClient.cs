namespace StockSharp.Alpaca.Native;

using System.Dynamic;
using System.Runtime.CompilerServices;

class RestTradingClient : RestAlpacaClient
{
	public RestTradingClient(string endpoint, SecureString key, SecureString secret)
		: base(endpoint, key, secret)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(RestTradingClient);

	private const string _dateFormat = "yyyy-MM-dd";

	public Task<IEnumerable<Asset>> GetAssets(CancellationToken cancellationToken)
		=> MakeRequest<IEnumerable<Asset>>("v2/assets", CreateRequest(Method.Get), cancellationToken);

	public Task<Account> GetAccount(CancellationToken cancellationToken)
		=> MakeRequest<Account>("v2/account", CreateRequest(Method.Get), cancellationToken);

	/// <summary>
	/// Lists option contracts, page by page.
	/// </summary>
	/// <remarks>
	/// There are hundreds of thousands of listed contracts, which is why they are not part of
	/// <c>v2/assets</c> and why this always pages rather than asking for everything at once. The caller
	/// stops reading when it has enough.
	/// </remarks>
	public async IAsyncEnumerable<OptionContract> GetOptionContracts(string underlying, string status,
		string type, DateTime? expirationFrom, DateTime? expirationTo,
		[EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var token = string.Empty;

		while (true)
		{
			var request = CreateRequest(Method.Get);

			if (!underlying.IsEmpty())
				request.AddQueryParameter("underlying_symbols", underlying);

			if (!status.IsEmpty())
				request.AddQueryParameter("status", status);

			// Narrowing here rather than after the fact: a chain runs to thousands of contracts, and
			// fetching all of them to keep the puts costs a request per thousand thrown away.
			if (!type.IsEmpty())
				request.AddQueryParameter("type", type);

			// Without an explicit range the venue defaults the upper bound to the coming weekend, so a
			// caller asking for a chain receives only whatever expires within days of asking.
			if (expirationFrom is not null)
				request.AddQueryParameter("expiration_date_gte", expirationFrom.Value.ToString(_dateFormat));

			if (expirationTo is not null)
				request.AddQueryParameter("expiration_date_lte", expirationTo.Value.ToString(_dateFormat));

			// The maximum the endpoint accepts. A smaller page only means more round trips.
			request.AddQueryParameter("limit", 10000);

			if (!token.IsEmpty())
				request.AddQueryParameter("page_token", token);

			dynamic result = await MakeRequest<object>("v2/options/contracts", request, cancellationToken);

			var contracts = ((JToken)result.option_contracts).DeserializeObject<IEnumerable<OptionContract>>();

			foreach (var contract in contracts)
			{
				cancellationToken.ThrowIfCancellationRequested();
				yield return contract;
			}

			token = (string)result.next_page_token;

			if (token.IsEmpty())
				break;
		}
	}

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
		body.qty = qty.To<string>();
		body.side = side;
		body.type = type;
		body.time_in_force = tif;

		if (limitPrice is not null)
			body.limit_price = limitPrice.Value.To<string>();

		if (stopPrice is not null)
			body.stop_price = stopPrice.Value.To<string>();

		if (trailPrice is not null)
			body.trail_price = trailPrice.Value.To<string>();

		if (trailPercent is not null)
			body.trail_percent = trailPercent.Value.To<string>();

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

		body.qty = qty.To<string>();
		body.time_in_force = tif;

		if (limitPrice is not null)
			body.limit_price = limitPrice.Value.To<string>();

		if (stopPrice is not null)
			body.stop_price = stopPrice.Value.To<string>();

		if (trail is not null)
			body.trail = trail.Value.To<string>();

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
