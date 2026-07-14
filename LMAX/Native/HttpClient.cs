namespace StockSharp.LMAX.Native;

using RestSharp;

class HttpClient(
	Authenticator authenticator,
	string accountApiBaseUrl,
	string marketDataApiBaseUrl) : BaseLogReceiver
{
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	private readonly Uri _accountApiBaseUrl = accountApiBaseUrl?.To<Uri>() ?? throw new ArgumentNullException(nameof(accountApiBaseUrl));
	private readonly Uri _marketDataApiBaseUrl = marketDataApiBaseUrl?.To<Uri>() ?? throw new ArgumentNullException(nameof(marketDataApiBaseUrl));

	private SecureString _token;

	// to get readable name after obfuscation
	public override string Name => nameof(LMAX) + "_" + nameof(HttpClient);

	public async Task<string> ConnectAsync(CancellationToken cancellationToken)
	{
		var (timestamp, nonce, signature) = _authenticator.CreateSignature();

		var request = new AuthenticationRequest
		{
			ClientKeyId = _authenticator.ClientKeyId.UnSecure(),
			Timestamp = timestamp,
			Nonce = nonce,
			Signature = signature
		};

		var response = await PostAsync<AuthenticationRequest, AuthenticationResponse>(
			_accountApiBaseUrl,
			"/v1/authenticate",
			request,
			authenticated: false,
			cancellationToken);

		_token = response.Token.Secure();

		this.AddInfoLog("Authenticated successfully");

		return response.Token;
	}

	public void Disconnect()
	{
		_token = null;
		this.AddInfoLog("Disconnected");
	}

	// Account API methods

	public Task<InstrumentDataResponse> GetInstrumentDataAsync(CancellationToken cancellationToken)
		=> GetAsync<InstrumentDataResponse>(_accountApiBaseUrl, "/v1/account/instrument-data", cancellationToken);

	public Task<WorkingOrdersResponse> GetWorkingOrdersAsync(string instrumentId = null, int? limit = null, string offset = null, CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_token);

		if (!instrumentId.IsEmpty())
			request.AddQueryParameter("instrument_id", instrumentId);

		if (limit != null)
			request.AddQueryParameter("limit", limit.Value.ToString());

		if (!offset.IsEmpty())
			request.AddQueryParameter("offset", offset);

		return MakeRequestAsync<WorkingOrdersResponse>(_accountApiBaseUrl, "/v1/account/working-orders", request, cancellationToken);
	}

	public Task<PlaceOrderResponse> PlaceOrderAsync(PlaceOrderRequest body, CancellationToken cancellationToken)
		=> PostAsync<PlaceOrderRequest, PlaceOrderResponse>(_accountApiBaseUrl, "/v1/account/place-order", body, true, cancellationToken);

	public Task<CancelOrderResponse> CancelOrderAsync(CancelOrderRequest body, CancellationToken cancellationToken)
		=> PostAsync<CancelOrderRequest, CancelOrderResponse>(_accountApiBaseUrl, "/v1/account/cancel-order", body, true, cancellationToken);

	public Task<CancelAndReplaceOrderResponse> CancelAndReplaceOrderAsync(CancelAndReplaceOrderRequest body, CancellationToken cancellationToken)
		=> PostAsync<CancelAndReplaceOrderRequest, CancelAndReplaceOrderResponse>(_accountApiBaseUrl, "/v1/account/cancel-and-replace-order", body, true, cancellationToken);

	public Task<CloseOrderResponse> CloseOrderAsync(CloseOrderRequest body, CancellationToken cancellationToken)
		=> PostAsync<CloseOrderRequest, CloseOrderResponse>(_accountApiBaseUrl, "/v1/account/close-order", body, true, cancellationToken);

	public Task<CancelAllOrdersResponse> CancelAllOrdersAsync(CancelAllOrdersRequest body, CancellationToken cancellationToken)
		=> PostAsync<CancelAllOrdersRequest, CancelAllOrdersResponse>(_accountApiBaseUrl, "/v1/account/cancel-all-orders", body, true, cancellationToken);

	public Task<InstrumentPositionsResponse> GetInstrumentPositionsAsync(CancellationToken cancellationToken)
		=> GetAsync<InstrumentPositionsResponse>(_accountApiBaseUrl, "/v1/account/positions", cancellationToken);

	public Task<OrderPositionsResponse> GetOrderPositionsAsync(string instrumentId = null, int? limit = null, string offset = null, CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_token);

		if (!instrumentId.IsEmpty())
			request.AddQueryParameter("instrument_id", instrumentId);

		if (limit != null)
			request.AddQueryParameter("limit", limit.Value.ToString());

		if (!offset.IsEmpty())
			request.AddQueryParameter("offset", offset);

		return MakeRequestAsync<OrderPositionsResponse>(_accountApiBaseUrl, "/v1/account/order-positions", request, cancellationToken);
	}

	public Task<WalletBalancesResponse> GetWalletBalancesAsync(CancellationToken cancellationToken)
		=> GetAsync<WalletBalancesResponse>(_accountApiBaseUrl, "/v1/account/wallets", cancellationToken);

	public Task<TradeHistoryResponse> GetTradeHistoryAsync(
		string instrumentId = null,
		string from = null,
		string to = null,
		int? limit = null,
		string offset = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_token);

		if (!instrumentId.IsEmpty())
			request.AddQueryParameter("instrument_id", instrumentId);

		if (!from.IsEmpty())
			request.AddQueryParameter("from", from);

		if (!to.IsEmpty())
			request.AddQueryParameter("to", to);

		if (limit != null)
			request.AddQueryParameter("limit", limit.Value.ToString());

		if (!offset.IsEmpty())
			request.AddQueryParameter("offset", offset);

		return MakeRequestAsync<TradeHistoryResponse>(_accountApiBaseUrl, "/v1/account/trades", request, cancellationToken);
	}

	public Task<AccountTransactionResponse> GetAccountTransactionsAsync(
		string from = null,
		string to = null,
		int? limit = null,
		string offset = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_token);

		if (!from.IsEmpty())
			request.AddQueryParameter("from", from);

		if (!to.IsEmpty())
			request.AddQueryParameter("to", to);

		if (limit != null)
			request.AddQueryParameter("limit", limit.Value.ToString());

		if (!offset.IsEmpty())
			request.AddQueryParameter("offset", offset);

		return MakeRequestAsync<AccountTransactionResponse>(_accountApiBaseUrl, "/v1/account/transactions", request, cancellationToken);
	}

	public Task<OrderStateResponse> GetOrderStateAsync(string instructionId, CancellationToken cancellationToken)
		=> GetAsync<OrderStateResponse>(_accountApiBaseUrl, $"/v1/account/order-state/{instructionId}", cancellationToken);

	// Market Data API methods

	public Task<OrderBookSnapshot> GetOrderBookAsync(string instrumentId, CancellationToken cancellationToken)
		=> GetAsync<OrderBookSnapshot>(_marketDataApiBaseUrl, $"/v1/marketdata/{instrumentId}", cancellationToken, authenticated: false);

	public Task<HistoricClosingPricesResponse> GetHistoricClosingPricesAsync(
		string instrumentId,
		string from = null,
		string to = null,
		int? limit = null,
		string offset = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_token);

		if (!from.IsEmpty())
			request.AddQueryParameter("from", from);

		if (!to.IsEmpty())
			request.AddQueryParameter("to", to);

		if (limit != null)
			request.AddQueryParameter("limit", limit.Value.ToString());

		if (!offset.IsEmpty())
			request.AddQueryParameter("offset", offset);

		return MakeRequestAsync<HistoricClosingPricesResponse>(_marketDataApiBaseUrl, $"/v1/marketdata/{instrumentId}/historic-closing-prices", request, cancellationToken);
	}

	public Task<TimeResponse> GetServerTimeAsync(CancellationToken cancellationToken)
		=> GetAsync<TimeResponse>(_accountApiBaseUrl, "/v1/time", cancellationToken, authenticated: false);

	public Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken)
		=> GetAsync<VersionResponse>(_accountApiBaseUrl, "/v1/version", cancellationToken, authenticated: false);

	// Private methods

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private Task<TResponse> GetAsync<TResponse>(Uri baseUrl, string path, CancellationToken cancellationToken, bool authenticated = true)
	{
		var request = CreateRequest(Method.Get);

		if (authenticated)
			request.SetBearer(_token);

		return MakeRequestAsync<TResponse>(baseUrl, path, request, cancellationToken);
	}

	private Task<TResponse> PostAsync<TRequest, TResponse>(Uri baseUrl, string path, TRequest body, bool authenticated, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (authenticated)
			request.SetBearer(_token);

		var json = body.ToJson();
		request.AddBodyAsStr(json);

		return MakeRequestAsync<TResponse>(baseUrl, path, request, cancellationToken);
	}

	private async Task<TResponse> MakeRequestAsync<TResponse>(Uri baseUrl, string path, RestRequest request, CancellationToken cancellationToken)
	{
		var url = new Uri(baseUrl, path);

		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject jObj)
		{
			// Check for error response
			if (jObj["error"] != null || jObj["error_code"] != null)
			{
				var errorMsg = jObj["error_message"]?.ToString() ?? jObj["error"]?.ToString() ?? "Unknown error";
				throw new InvalidOperationException($"API error: {errorMsg}");
			}
		}

		return ((JToken)obj).DeserializeObject<TResponse>();
	}
}
