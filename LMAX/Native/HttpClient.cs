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

	private SecureString _accountToken;
	private SecureString _marketDataToken;

	// to get readable name after obfuscation
	public override string Name => nameof(LMAX) + "_" + nameof(HttpClient);

	public async Task<(string AccountToken, string MarketDataToken)>
		ConnectAsync(CancellationToken cancellationToken)
	{
		var accountToken = await AuthenticateAsync(
			_accountApiBaseUrl,
			cancellationToken);
		var marketDataToken = await AuthenticateAsync(
			_marketDataApiBaseUrl,
			cancellationToken);

		_accountToken = accountToken.Secure();
		_marketDataToken = marketDataToken.Secure();

		this.AddInfoLog("Authenticated successfully");

		return (accountToken, marketDataToken);
	}

	private async Task<string> AuthenticateAsync(
		Uri baseUrl,
		CancellationToken cancellationToken)
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
			baseUrl,
			"/v1/authenticate",
			request,
			authenticated: false,
			cancellationToken);

		if (response.Token.IsEmpty())
			throw new InvalidDataException(
				"LMAX authentication returned an empty token.");

		return response.Token;
	}

	public void Disconnect()
	{
		_accountToken = null;
		_marketDataToken = null;
		this.AddInfoLog("Disconnected");
	}

	// Account API methods

	public Task<InstrumentDataResponse> GetInstrumentDataAsync(CancellationToken cancellationToken)
		=> GetAsync<InstrumentDataResponse>(_accountApiBaseUrl, "/v1/account/instrument-data", cancellationToken);

	public Task<WorkingOrdersResponse> GetWorkingOrdersAsync(
		int? pageSize = null,
		string after = null,
		string before = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_accountToken);

		if (pageSize != null)
			request.AddQueryParameter("page_size", pageSize.Value.ToString());

		if (!after.IsEmpty())
			request.AddQueryParameter("after", after);

		if (!before.IsEmpty())
			request.AddQueryParameter("before", before);

		return MakeRequestAsync<WorkingOrdersResponse>(
			_accountApiBaseUrl,
			"/v1/account/working-orders",
			request,
			cancellationToken);
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

	public Task<OrderPositionsResponse> GetOrderPositionsAsync(
		int? pageSize = null,
		string after = null,
		string before = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_accountToken);

		if (pageSize != null)
			request.AddQueryParameter("page_size", pageSize.Value.ToString());

		if (!after.IsEmpty())
			request.AddQueryParameter("after", after);

		if (!before.IsEmpty())
			request.AddQueryParameter("before", before);

		return MakeRequestAsync<OrderPositionsResponse>(
			_accountApiBaseUrl,
			"/v1/account/order-positions",
			request,
			cancellationToken);
	}

	public Task<WalletBalancesResponse> GetWalletBalancesAsync(CancellationToken cancellationToken)
		=> GetAsync<WalletBalancesResponse>(_accountApiBaseUrl, "/v1/account/wallets", cancellationToken);

	public Task<TradeHistoryResponse> GetTradeHistoryAsync(
		bool? orderInformation = null,
		string startTime = null,
		string endTime = null,
		int? pageSize = null,
		string after = null,
		string before = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_accountToken);

		if (orderInformation != null)
			request.AddQueryParameter(
				"order_information",
				orderInformation.Value.ToString().ToLowerInvariant());

		if (!startTime.IsEmpty())
			request.AddQueryParameter("start_time", startTime);

		if (!endTime.IsEmpty())
			request.AddQueryParameter("end_time", endTime);

		if (pageSize != null)
			request.AddQueryParameter("page_size", pageSize.Value.ToString());

		if (!after.IsEmpty())
			request.AddQueryParameter("after", after);

		if (!before.IsEmpty())
			request.AddQueryParameter("before", before);

		return MakeRequestAsync<TradeHistoryResponse>(
			_accountApiBaseUrl,
			"/v1/account/trades",
			request,
			cancellationToken);
	}

	public Task<AccountTransactionResponse> GetAccountTransactionsAsync(
		string startTime = null,
		string endTime = null,
		int? pageSize = null,
		string after = null,
		string before = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_accountToken);

		if (!startTime.IsEmpty())
			request.AddQueryParameter("start_time", startTime);

		if (!endTime.IsEmpty())
			request.AddQueryParameter("end_time", endTime);

		if (pageSize != null)
			request.AddQueryParameter("page_size", pageSize.Value.ToString());

		if (!after.IsEmpty())
			request.AddQueryParameter("after", after);

		if (!before.IsEmpty())
			request.AddQueryParameter("before", before);

		return MakeRequestAsync<AccountTransactionResponse>(
			_accountApiBaseUrl,
			"/v1/account/account-transactions",
			request,
			cancellationToken);
	}

	public Task<OrderStateResponse> GetOrderStateAsync(
		string instructionId,
		string instrumentId,
		CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_accountToken);
		request.AddQueryParameter("instruction_id", instructionId);
		request.AddQueryParameter("instrument_id", instrumentId);

		return MakeRequestAsync<OrderStateResponse>(
			_accountApiBaseUrl,
			"/v1/account/order-state",
			request,
			cancellationToken);
	}

	// Market Data API methods

	public Task<OrderBookSnapshot> GetOrderBookAsync(string instrumentId, CancellationToken cancellationToken)
		=> GetAsync<OrderBookSnapshot>(
			_marketDataApiBaseUrl,
			$"/v1/marketdata/{instrumentId}",
			cancellationToken);

	public Task<HistoricClosingPricesResponse> GetHistoricClosingPricesAsync(
		string instrumentId,
		string startDate = null,
		string endDate = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(_marketDataToken);

		if (!startDate.IsEmpty())
			request.AddQueryParameter("start_date", startDate);

		if (!endDate.IsEmpty())
			request.AddQueryParameter("end_date", endDate);

		return MakeRequestAsync<HistoricClosingPricesResponse>(
			_marketDataApiBaseUrl,
			$"/v1/marketdata/{instrumentId}/historic-closing-prices",
			request,
			cancellationToken);
	}

	public Task<TimeResponse> GetServerTimeAsync(CancellationToken cancellationToken)
		=> GetAsync<TimeResponse>(_accountApiBaseUrl, "/v1/time", cancellationToken, authenticated: false);

	public Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken)
		=> GetAsync<VersionResponse>(_accountApiBaseUrl, "/v1/version", cancellationToken, authenticated: false);

	public async Task HeartbeatAsync(CancellationToken cancellationToken)
	{
		await HeartbeatAsync(
			_accountApiBaseUrl,
			_accountToken,
			cancellationToken);
		await HeartbeatAsync(
			_marketDataApiBaseUrl,
			_marketDataToken,
			cancellationToken);
	}

	// Private methods

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private Task<TResponse> GetAsync<TResponse>(Uri baseUrl, string path, CancellationToken cancellationToken, bool authenticated = true)
	{
		var request = CreateRequest(Method.Get);

		if (authenticated)
			request.SetBearer(GetToken(baseUrl));

		return MakeRequestAsync<TResponse>(baseUrl, path, request, cancellationToken);
	}

	private Task<TResponse> PostAsync<TRequest, TResponse>(Uri baseUrl, string path, TRequest body, bool authenticated, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (authenticated)
			request.SetBearer(GetToken(baseUrl));

		var json = body.ToJson();
		request.AddBodyAsStr(json);

		return MakeRequestAsync<TResponse>(baseUrl, path, request, cancellationToken);
	}

	private async Task HeartbeatAsync(
		Uri baseUrl,
		SecureString token,
		CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		request.SetBearer(token);

		await request.InvokeAsync<object>(
			new Uri(baseUrl, "/v1/heartbeat"),
			this,
			this.AddVerboseLog,
			cancellationToken,
			throwIfEmptyResponse: false);
	}

	private SecureString GetToken(Uri baseUrl)
		=> baseUrl == _marketDataApiBaseUrl
			? _marketDataToken
			: _accountToken;

	private async Task<TResponse> MakeRequestAsync<TResponse>(Uri baseUrl, string path, RestRequest request, CancellationToken cancellationToken)
	{
		var url = new Uri(baseUrl, path);
		var response = await request.InvokeAsync<TResponse>(
			url,
			this,
			this.AddVerboseLog,
			cancellationToken);

		return response ?? throw new InvalidDataException(
			$"LMAX returned an empty response for '{path}'.");
	}
}
