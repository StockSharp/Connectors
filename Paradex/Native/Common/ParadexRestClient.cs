namespace StockSharp.Paradex.Native.Common;

sealed class ParadexRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly string _starknetAccount;
	private readonly SecureString _starknetKey;
	private readonly string _authPath;

	private string _cachedToken;
	private DateTime _tokenExpiresAt;

	public ParadexRestClient(string endpoint, SecureString key, SecureString secret, string starknetAccount, SecureString starknetKey, string authPath)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_endpoint = endpoint.To<Uri>();
		_key = key;
		_secret = secret;
		_starknetAccount = starknetAccount;
		_starknetKey = starknetKey;
		_authPath = authPath.IsEmpty() ? "/v1/auth" : authPath;
	}

	public override string Name => nameof(Paradex) + "_" + nameof(ParadexRestClient);

	public ValueTask<JObject> GetMarketsAsync(CancellationToken cancellationToken)
		=> new(GetAsync("/v1/markets", null, cancellationToken));

	public ValueTask<JObject> GetMarketSummaryAsync(string market, CancellationToken cancellationToken)
		=> new(GetAsync("/v1/markets/summary", request => request.AddParameter("market", market), cancellationToken));

	public ValueTask<JObject> GetOrderBookAsync(string market, CancellationToken cancellationToken)
		=> new(GetAsync($"/v1/orderbook/{market}", null, cancellationToken));

	public ValueTask<JObject> GetBboAsync(string market, CancellationToken cancellationToken)
		=> new(GetAsync($"/v1/bbo/{market}", null, cancellationToken));

	public ValueTask<JObject> GetTradesAsync(string market, int? limit, CancellationToken cancellationToken)
		=> new(GetAsync("/v1/trades", request =>
		{
			request.AddParameter("market", market);

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken));

	public ValueTask<JObject> GetSystemTimeAsync(CancellationToken cancellationToken)
		=> new(GetAsync("/v1/system/time", null, cancellationToken));

	public ValueTask<JObject> GetBalancesAsync(string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync("/v1/balances", Method.Get, null, tokenOverride, cancellationToken));

	public ValueTask<JObject> GetPositionsAsync(string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync("/v1/positions", Method.Get, null, tokenOverride, cancellationToken));

	public ValueTask<JObject> GetOrdersAsync(string market, string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync("/v1/orders", Method.Get, request =>
		{
			if (!market.IsEmpty())
				request.AddParameter("market", market);
		}, tokenOverride, cancellationToken));

	public ValueTask<JObject> GetFillsAsync(string market, DateTime? from, DateTime? to, int? limit, string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync("/v1/fills", Method.Get, request =>
		{
			if (!market.IsEmpty())
				request.AddParameter("market", market);

			if (from is DateTime fromTime)
				request.AddParameter("from", (long)fromTime.ToUnix(false));

			if (to is DateTime toTime)
				request.AddParameter("to", (long)toTime.ToUnix(false));

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, tokenOverride, cancellationToken));

	public ValueTask<JObject> CreateOrderAsync(JObject payload, string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync("/v1/orders", Method.Post, request => request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json), tokenOverride, cancellationToken));

	public ValueTask<JObject> CancelOrderAsync(string orderId, string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync($"/v1/orders/{orderId}", Method.Delete, null, tokenOverride, cancellationToken));

	public ValueTask<JObject> CancelByClientIdAsync(string clientId, string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync($"/v1/orders/by_client_id/{clientId}", Method.Delete, null, tokenOverride, cancellationToken));

	public ValueTask<JObject> CancelAllOrdersAsync(string market, string tokenOverride, CancellationToken cancellationToken)
		=> new(SendAuthenticatedAsync("/v1/orders", Method.Delete, request =>
		{
			if (!market.IsEmpty())
				request.AddParameter("market", market);
		}, tokenOverride, cancellationToken));

	private Task<JObject> GetAsync(string resource, Action<RestRequest> requestBuilder, CancellationToken cancellationToken)
	{
		var request = new RestRequest(resource, Method.Get);
		requestBuilder?.Invoke(request);
		return request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private async Task<JObject> SendAuthenticatedAsync(string resource, Method method, Action<RestRequest> requestBuilder, string tokenOverride, CancellationToken cancellationToken)
	{
		var request = new RestRequest(resource, method);
		requestBuilder?.Invoke(request);

		var token = await ResolveBearerTokenAsync(tokenOverride, cancellationToken);
		request.AddHeader("Authorization", token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : $"Bearer {token}");

		return await request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private async ValueTask<string> ResolveBearerTokenAsync(string tokenOverride, CancellationToken cancellationToken)
	{
		if (!tokenOverride.IsEmpty())
			return tokenOverride;

		var keyToken = _key.UnSecure();
		if (!keyToken.IsEmpty())
			return keyToken;

		if (!_cachedToken.IsEmpty() && DateTime.UtcNow < _tokenExpiresAt)
			return _cachedToken;

		if (_starknetAccount.IsEmpty())
			throw new InvalidOperationException("Starknet account is not specified for Paradex auth bootstrap.");

		var signature = !_secret.IsEmpty() ? _secret.UnSecure() : _starknetKey.UnSecure();

		if (signature.IsEmpty())
			throw new InvalidOperationException("Starknet signature material is not specified for Paradex auth bootstrap.");

		var timestamp = (long)DateTime.UtcNow.ToUnix(false);
		var expiration = timestamp + 60_000;

		var request = new RestRequest(_authPath, Method.Post);
		request
			.AddHeader("PARADEX-STARKNET-ACCOUNT", _starknetAccount)
			.AddHeader("PARADEX-STARKNET-SIGNATURE", signature)
			.AddHeader("PARADEX-TIMESTAMP", timestamp.ToString())
			.AddHeader("PARADEX-SIGNATURE-EXPIRATION", expiration.ToString())
			.AddStringBody("{}", DataFormat.Json);

		var response = await request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
		var token = response["jwt_token"]?.Value<string>()
			?? response["token"]?.Value<string>()
			?? response["access_token"]?.Value<string>()
			?? response["results"]?["jwt_token"]?.Value<string>()
			?? response["results"]?["token"]?.Value<string>();

		if (token.IsEmpty())
			throw new InvalidOperationException("Paradex auth endpoint did not return bearer token.");

		_cachedToken = token;

		var expiresUnix = response["expires_at"]?.Value<long?>()
			?? response["results"]?["expires_at"]?.Value<long?>();

		_tokenExpiresAt = expiresUnix is long eu && eu > 0
			? eu.FromUnix(false).AddSeconds(-30)
			: DateTime.UtcNow.AddMinutes(10);

		return token;
	}
}
