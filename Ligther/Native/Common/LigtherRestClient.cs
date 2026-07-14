namespace StockSharp.Ligther.Native.Common;

sealed class LigtherRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly SecureString _key;
	private readonly SecureString _secret;

	public LigtherRestClient(string endpoint, SecureString key, SecureString secret)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_endpoint = endpoint.To<Uri>();
		_key = key;
		_secret = secret;
	}

	public override string Name => nameof(Ligther) + "_" + nameof(LigtherRestClient);

	public ValueTask<JObject> GetOrderBooksAsync(string filter, int? marketId, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/orderBooks", request =>
		{
			if (!filter.IsEmpty())
				request.AddParameter("filter", filter);

			if (marketId is int id)
				request.AddParameter("market_id", id);
		}, cancellationToken));

	public ValueTask<JObject> GetOrderBookDetailsAsync(string filter, int? marketId, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/orderBookDetails", request =>
		{
			if (!filter.IsEmpty())
				request.AddParameter("filter", filter);

			if (marketId is int id)
				request.AddParameter("market_id", id);
		}, cancellationToken));

	public ValueTask<JObject> GetOrderBookOrdersAsync(int marketId, int? limit, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/orderBookOrders", request =>
		{
			request.AddParameter("market_id", marketId);

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken));

	public ValueTask<JObject> GetRecentTradesAsync(int marketId, int? limit, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/recentTrades", request =>
		{
			request.AddParameter("market_id", marketId);

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken));

	public ValueTask<JObject> GetExchangeStatsAsync(CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/exchangeStats", null, cancellationToken));

	public ValueTask<JObject> GetAccountAsync(string by, string value, string authToken, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/account", request =>
		{
			request.AddParameter("by", by);
			request.AddParameter("value", value);
			AttachAuth(request, authToken);
		}, cancellationToken));

	public ValueTask<JObject> GetAccountActiveOrdersAsync(int accountIndex, int marketId, string authToken, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/accountActiveOrders", request =>
		{
			request.AddParameter("account_index", accountIndex);
			request.AddParameter("market_id", marketId);
			AttachAuth(request, authToken, true);
		}, cancellationToken));

	public ValueTask<JObject> GetAccountInactiveOrdersAsync(int accountIndex, int limit, int? marketId, string authToken, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/accountInactiveOrders", request =>
		{
			request.AddParameter("account_index", accountIndex);
			request.AddParameter("limit", limit);

			if (marketId is int id)
				request.AddParameter("market_id", id);

			AttachAuth(request, authToken, true);
		}, cancellationToken));

	public ValueTask<JObject> GetAccountTxsAsync(int limit, int accountIndex, int? marketId, string authToken, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/accountTxs", request =>
		{
			request.AddParameter("limit", limit);
			request.AddParameter("by", "index");
			request.AddParameter("value", accountIndex.ToString(CultureInfo.InvariantCulture));

			if (marketId is int id)
				request.AddParameter("market_id", id);

			AttachAuth(request, authToken, true);
		}, cancellationToken));

	public ValueTask<JObject> GetNextNonceAsync(int accountIndex, int apiKeyIndex, CancellationToken cancellationToken)
		=> new(GetAsync("/api/v1/nextNonce", request =>
		{
			request.AddParameter("account_index", accountIndex);
			request.AddParameter("api_key_index", apiKeyIndex);
		}, cancellationToken));

	public ValueTask<JObject> SendTxAsync(JObject payload, string authToken, CancellationToken cancellationToken)
		=> new(PostAsync("/api/v1/sendTx", request =>
		{
			AttachAuth(request, authToken, true);
			request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json);
		}, cancellationToken));

	private Task<JObject> GetAsync(string resource, Action<RestRequest> requestBuilder, CancellationToken cancellationToken)
	{
		var request = new RestRequest(resource, Method.Get);
		requestBuilder?.Invoke(request);
		return request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private Task<JObject> PostAsync(string resource, Action<RestRequest> requestBuilder, CancellationToken cancellationToken)
	{
		var request = new RestRequest(resource, Method.Post);
		requestBuilder?.Invoke(request);
		return request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private void AttachAuth(RestRequest request, string authTokenOverride, bool required = false)
	{
		var authToken = !authTokenOverride.IsEmpty() ? authTokenOverride : _key.UnSecure();

		if (!authToken.IsEmpty())
		{
			var value = authToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
				? authToken
				: $"Bearer {authToken}";

			request.AddHeader("authorization", value);
		}

		var authQuery = _secret.UnSecure();
		if (!authQuery.IsEmpty())
			request.AddParameter("auth", authQuery);

		if (required && authToken.IsEmpty() && authQuery.IsEmpty())
			throw new InvalidOperationException("Authorization token is required for private Lighter API endpoint.");
	}
}
