namespace StockSharp.EdgeX.Native.Common;

sealed class EdgeXRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly string _clearingAccount;
	private readonly SecureString _passphrase;
	private readonly HMACSHA256 _hmac;

	public EdgeXRestClient(string endpoint, SecureString key, SecureString secret, string clearingAccount, SecureString passphrase)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_endpoint = endpoint.To<Uri>();
		_key = key;
		_secret = secret;
		_clearingAccount = clearingAccount;
		_passphrase = passphrase;
		_hmac = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
	}

	public override string Name => nameof(EdgeX) + "_" + nameof(EdgeXRestClient);

	protected override void DisposeManaged()
	{
		_hmac?.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<JObject> GetMetaDataAsync(CancellationToken cancellationToken)
		=> new(GetPublicAsync("/api/v1/public/meta/getMetaData", null, cancellationToken));

	public ValueTask<JObject> GetServerTimeAsync(CancellationToken cancellationToken)
		=> new(GetPublicAsync("/api/v1/public/meta/getServerTime", null, cancellationToken));

	public ValueTask<JObject> GetTickerAsync(string contractId, CancellationToken cancellationToken)
		=> new(GetPublicAsync("/api/v1/public/quote/getTicker", request => request.AddParameter("contractId", contractId), cancellationToken));

	public ValueTask<JObject> GetDepthAsync(string contractId, int level, CancellationToken cancellationToken)
		=> new(GetPublicAsync("/api/v1/public/quote/getDepth", request =>
		{
			request
				.AddParameter("contractId", contractId)
				.AddParameter("level", level);
		}, cancellationToken));

	public ValueTask<JObject> GetKlineAsync(string contractId, string klineType, string priceType, int size, DateTime? from, DateTime? to, CancellationToken cancellationToken)
		=> new(GetPublicAsync("/api/v1/public/quote/getKline", request =>
		{
			request
				.AddParameter("contractId", contractId)
				.AddParameter("klineType", klineType)
				.AddParameter("priceType", priceType)
				.AddParameter("size", size);

			if (from is DateTime fromTime)
				request.AddParameter("filterBeginKlineTimeInclusive", ((long)fromTime.ToUnix(false)).ToString());

			if (to is DateTime toTime)
				request.AddParameter("filterEndKlineTimeExclusive", ((long)toTime.ToUnix(false)).ToString());
		}, cancellationToken));

	public ValueTask<JObject> GetAccountAssetAsync(string accountId, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/account/getAccountAsset",
			Method.Get,
			request => request.AddParameter("accountId", accountId),
			null,
			cancellationToken));

	public ValueTask<JObject> GetActiveOrdersAsync(string accountId, string contractId, int? size, DateTime? from, DateTime? to, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/order/getActiveOrderPage",
			Method.Get,
			request =>
			{
				request.AddParameter("accountId", accountId);

				if (!contractId.IsEmpty())
					request.AddParameter("filterContractIdList", contractId);

				if (size is int s && s > 0)
					request.AddParameter("size", s);

				if (from is DateTime fromTime)
					request.AddParameter("filterStartCreatedTimeInclusive", (long)fromTime.ToUnix(false));

				if (to is DateTime toTime)
					request.AddParameter("filterEndCreatedTimeExclusive", (long)toTime.ToUnix(false));
			},
			null,
			cancellationToken));

	public ValueTask<JObject> GetFillTransactionsAsync(string accountId, string contractId, int? size, DateTime? from, DateTime? to, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/order/getHistoryOrderFillTransactionPage",
			Method.Get,
			request =>
			{
				request.AddParameter("accountId", accountId);

				if (!contractId.IsEmpty())
					request.AddParameter("filterContractIdList", contractId);

				if (size is int s && s > 0)
					request.AddParameter("size", s);

				if (from is DateTime fromTime)
					request.AddParameter("filterStartCreatedTimeInclusive", (long)fromTime.ToUnix(false));

				if (to is DateTime toTime)
					request.AddParameter("filterEndCreatedTimeExclusive", (long)toTime.ToUnix(false));
			},
			null,
			cancellationToken));

	public ValueTask<JObject> CreateOrderAsync(JObject payload, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/order/createOrder",
			Method.Post,
			request => request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json),
			payload,
			cancellationToken));

	public ValueTask<JObject> CancelOrderByIdAsync(string accountId, IEnumerable<string> orderIdList, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/order/cancelOrderById",
			Method.Post,
			request =>
			{
				var payload = new JObject
				{
					["accountId"] = accountId,
					["orderIdList"] = new JArray(orderIdList),
				};

				request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json);
			},
			new JObject
			{
				["accountId"] = accountId,
				["orderIdList"] = new JArray(orderIdList),
			},
			cancellationToken));

	public ValueTask<JObject> CancelOrderByClientIdAsync(string accountId, IEnumerable<string> clientOrderIds, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/order/cancelOrderByClientOrderId",
			Method.Post,
			request =>
			{
				var payload = new JObject
				{
					["accountId"] = accountId,
					["clientOrderIdList"] = new JArray(clientOrderIds),
				};

				request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json);
			},
			new JObject
			{
				["accountId"] = accountId,
				["clientOrderIdList"] = new JArray(clientOrderIds),
			},
			cancellationToken));

	public ValueTask<JObject> CancelAllOrdersAsync(string accountId, IEnumerable<string> contractIds, CancellationToken cancellationToken)
		=> new(SendPrivateAsync(
			"/api/v1/private/order/cancelAllOrder",
			Method.Post,
			request =>
			{
				var payload = new JObject
				{
					["accountId"] = accountId,
					["filterContractIdList"] = new JArray(contractIds ?? []),
				};

				request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json);
			},
			new JObject
			{
				["accountId"] = accountId,
				["filterContractIdList"] = new JArray(contractIds ?? []),
			},
			cancellationToken));

	public IDictionary<string, string> CreateWebSocketAuthHeaders(string wsPath)
	{
		EnsurePrivateCredentials();

		wsPath = wsPath.IsEmpty() ? "/api/v1/private/ws" : wsPath.Trim();

		if (!wsPath.StartsWith("/", StringComparison.Ordinal))
			wsPath = "/" + wsPath;

		var timestamp = ((long)DateTime.UtcNow.ToUnix(false)).ToString();
		var signature = Sign($"{timestamp}GET{wsPath}");
		var apiKey = _key.UnSecure();
		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["X-edgeX-Api-Timestamp"] = timestamp,
			["X-edgeX-Api-Signature"] = signature,
			["X-edgeX-Api-Key"] = apiKey,
			["EDGE_TIMESTAMP"] = timestamp,
			["EDGE_SIGNATURE"] = signature,
			["EDGE_API_KEY"] = apiKey,
		};

		if (!_clearingAccount.IsEmpty())
			headers["EDGE_CLEARING_ACCOUNT"] = _clearingAccount;

		if (!_passphrase.IsEmpty())
			headers["EDGE_PASSPHRASE"] = _passphrase.UnSecure();

		return headers;
	}

	private Task<JObject> GetPublicAsync(string resource, Action<RestRequest> requestBuilder, CancellationToken cancellationToken)
	{
		var request = new RestRequest(resource, Method.Get);
		requestBuilder?.Invoke(request);
		return request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private Task<JObject> SendPrivateAsync(string resource, Method method, Action<RestRequest> requestBuilder, JToken bodyForSign, CancellationToken cancellationToken)
	{
		EnsurePrivateCredentials();

		var request = new RestRequest(resource, method);
		requestBuilder?.Invoke(request);

		var timestamp = ((long)DateTime.UtcNow.ToUnix(false)).ToString();
		var path = resource.StartsWith("/") ? resource : "/" + resource;
		var payload = bodyForSign is null ? BuildQueryString(request) : Flatten(bodyForSign);
		var signContent = $"{timestamp}{method.ToString().ToUpperInvariant()}{path}{payload}";
		var signature = Sign(signContent);
		var apiKey = _key.UnSecure();

		request
			.AddHeader("X-edgeX-Api-Timestamp", timestamp)
			.AddHeader("X-edgeX-Api-Signature", signature)
			.AddHeader("X-edgeX-Api-Key", apiKey)
			.AddHeader("EDGE_TIMESTAMP", timestamp)
			.AddHeader("EDGE_SIGNATURE", signature)
			.AddHeader("EDGE_API_KEY", apiKey);

		if (!_clearingAccount.IsEmpty())
			request.AddHeader("EDGE_CLEARING_ACCOUNT", _clearingAccount);

		if (!_passphrase.IsEmpty())
			request.AddHeader("EDGE_PASSPHRASE", _passphrase.UnSecure());

		return request.InvokeAsync<JObject>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private void EnsurePrivateCredentials()
	{
		if (_key.IsEmpty())
			throw new InvalidOperationException("API key is not specified.");

		if (_secret.IsEmpty())
			throw new InvalidOperationException("API secret is not specified.");

		if (_hmac is null)
			throw new InvalidOperationException("Signature provider is not initialized.");
	}

	private string Sign(string content)
		=> _hmac
			.ComputeHash(content.UTF8())
			.Digest()
			.ToLowerInvariant();

	private static string BuildQueryString(RestRequest request)
	{
		var parts = request.Parameters
			.Where(static p => p.Type == ParameterType.GetOrPost && p.Value is not null)
			.Select(static p => (Name: p.Name ?? string.Empty, Value: p.Value.ToString()))
			.OrderBy(static p => p.Name, StringComparer.Ordinal)
			.Select(static p => $"{p.Name}={p.Value}")
			.ToArray();

		return parts.Length == 0 ? string.Empty : string.Join("&", parts);
	}

	private static string Flatten(JToken token)
		=> token switch
		{
			null => string.Empty,
			JValue value => value.Value?.ToString() ?? string.Empty,
			JArray array => string.Join("&", array.Select(Flatten)),
			JObject obj => string.Join("&", obj.Properties().OrderBy(static p => p.Name, StringComparer.Ordinal).Select(static p => $"{p.Name}={Flatten(p.Value)}")),
			_ => token.ToString(Formatting.None),
		};
}
