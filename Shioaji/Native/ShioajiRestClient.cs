namespace StockSharp.Shioaji.Native;

sealed class ShioajiApiException : InvalidOperationException
{
	public ShioajiApiException(HttpStatusCode statusCode, int? code, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}

sealed class ShioajiRestClient : BaseLogReceiver
{
	private sealed class RequestPacer : IDisposable
	{
		private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(100);
		private readonly SemaphoreSlim _sync = new(1, 1);
		private DateTime _nextRequest;

		public async Task Wait(CancellationToken cancellationToken)
		{
			TimeSpan delay;
			await _sync.WaitAsync(cancellationToken);
			try
			{
				var now = DateTime.UtcNow;
				var slot = _nextRequest > now ? _nextRequest : now;
				delay = slot - now;
				_nextRequest = slot + _interval;
			}
			finally
			{
				_sync.Release();
			}
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
		}

		public void Dispose() => _sync.Dispose();
	}

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _http = new();
	private readonly Uri _apiRoot;
	private readonly RequestPacer _marketDataPacer = new();

	public ShioajiRestClient(string address, SecureString key, SecureString secret)
	{
		var root = new Uri(address.ThrowIfEmpty(nameof(address)), UriKind.Absolute);
		_apiRoot = new(root.AbsoluteUri.EndsWith('/') ? root : new Uri(root.AbsoluteUri + '/'), "api/v1/");
		_http.Timeout = TimeSpan.FromSeconds(60);
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Shioaji/1.0");

		var apiKey = key.IsEmpty() ? null : key.UnSecure();
		var secretKey = secret.IsEmpty() ? null : secret.UnSecure();
		if (apiKey.IsEmpty() != secretKey.IsEmpty())
			throw new InvalidOperationException("Both Shioaji API key and secret must be specified for remote authentication.");
		if (!root.IsLoopback && apiKey.IsEmpty())
			throw new InvalidOperationException("A remote Shioaji server requires an API key and secret.");
		if (!apiKey.IsEmpty())
			_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{apiKey}:{secretKey}");
	}

	public override string Name => nameof(Shioaji) + "_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_marketDataPacer.Dispose();
		base.DisposeManaged();
	}

	public async Task<ShioajiInfo> Validate(CancellationToken cancellationToken)
	{
		var health = await Get<ShioajiHealth>("health", cancellationToken);
		if (!health.Status.EqualsIgnoreCase("healthy"))
			throw new InvalidOperationException($"Shioaji server health is '{health.Status.IsEmpty("unknown")}'.");
		return await GetInfo(cancellationToken);
	}

	public Task<ShioajiInfo> GetInfo(CancellationToken cancellationToken)
		=> Get<ShioajiInfo>("info", cancellationToken);

	public Task<ShioajiAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> Get<ShioajiAccount[]>("auth/accounts", cancellationToken);

	public Task<ShioajiContractList> GetContracts(string securityType, CancellationToken cancellationToken)
		=> Get<ShioajiContractList>($"data/contracts?security_type={Escape(securityType)}&region=TW", cancellationToken);

	public async Task<ShioajiContract> GetContract(string code, string securityType,
		CancellationToken cancellationToken)
	{
		var path = $"data/contracts/{Escape(code)}?region=TW";
		if (!securityType.IsEmpty())
			path += $"&security_type={Escape(securityType)}";
		try
		{
			return await Get<ShioajiContract>(path, cancellationToken);
		}
		catch (ShioajiApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async Task<ShioajiContractInfo> GetContractInfo(ShioajiContract contract,
		CancellationToken cancellationToken)
	{
		var path = $"data/contracts/{Escape(contract.Code)}/info?region=TW&security_type={Escape(contract.SecurityType)}";
		try
		{
			return await Get<ShioajiContractInfo>(path, cancellationToken);
		}
		catch (ShioajiApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async Task<ShioajiSnapshot[]> GetSnapshots(ShioajiContract[] contracts,
		CancellationToken cancellationToken)
	{
		await _marketDataPacer.Wait(cancellationToken);
		return await Post<ShioajiContractsRequest, ShioajiSnapshot[]>("data/snapshots",
			new() { Contracts = contracts }, cancellationToken);
	}

	public async Task<ShioajiTicks> GetTicks(ShioajiTicksRequest request, CancellationToken cancellationToken)
	{
		await _marketDataPacer.Wait(cancellationToken);
		return await Post<ShioajiTicksRequest, ShioajiTicks>("data/ticks", request, cancellationToken);
	}

	public async Task<ShioajiKBars> GetKBars(ShioajiKBarsRequest request, CancellationToken cancellationToken)
	{
		await _marketDataPacer.Wait(cancellationToken);
		return await Post<ShioajiKBarsRequest, ShioajiKBars>("data/kbars", request, cancellationToken);
	}

	public async Task Subscribe(ShioajiMarketSubscriptionRequest request, CancellationToken cancellationToken)
	{
		var response = await Post<ShioajiMarketSubscriptionRequest, ShioajiSubscriptionResponse>(
			"stream/subscribe", request, cancellationToken);
		if (!response.IsSuccess)
			throw new InvalidOperationException($"Shioaji rejected a market subscription: {response.Message.IsEmpty("unknown error")}.");
	}

	public async Task Unsubscribe(ShioajiMarketSubscriptionRequest request, CancellationToken cancellationToken)
	{
		var response = await Post<ShioajiMarketSubscriptionRequest, ShioajiSubscriptionResponse>(
			"stream/unsubscribe", request, cancellationToken);
		if (!response.IsSuccess)
			throw new InvalidOperationException($"Shioaji rejected a market unsubscription: {response.Message.IsEmpty("unknown error")}.");
	}

	public Task<ShioajiTradeSubscriptionResponse> SubscribeTrade(ShioajiAccount account,
		CancellationToken cancellationToken)
		=> Post<ShioajiTradeSubscriptionRequest, ShioajiTradeSubscriptionResponse>("auth/subscribe_trade",
			CreateTradeSubscription(account), cancellationToken);

	public Task<ShioajiTradeSubscriptionResponse> UnsubscribeTrade(ShioajiAccount account,
		CancellationToken cancellationToken)
		=> Post<ShioajiTradeSubscriptionRequest, ShioajiTradeSubscriptionResponse>("auth/unsubscribe_trade",
			CreateTradeSubscription(account), cancellationToken);

	public Task<ShioajiTrade> PlaceOrder(ShioajiPlaceOrderRequest request, CancellationToken cancellationToken)
		=> Post<ShioajiPlaceOrderRequest, ShioajiTrade>("order/place_order", request, cancellationToken);

	public Task<ShioajiTrade> UpdatePrice(string tradeId, decimal price, CancellationToken cancellationToken)
		=> Post<ShioajiUpdatePriceRequest, ShioajiTrade>("order/update_price",
			new() { TradeId = tradeId, Price = price }, cancellationToken);

	public Task<ShioajiTrade> UpdateQuantity(string tradeId, long quantity, CancellationToken cancellationToken)
		=> Post<ShioajiUpdateQuantityRequest, ShioajiTrade>("order/update_qty",
			new() { TradeId = tradeId, Quantity = quantity }, cancellationToken);

	public Task<ShioajiTrade> CancelOrder(string tradeId, CancellationToken cancellationToken)
		=> Post<ShioajiTradeIdRequest, ShioajiTrade>("order/cancel_order",
			new() { TradeId = tradeId }, cancellationToken);

	public Task<ShioajiTrade[]> GetTrades(ShioajiAccount account, CancellationToken cancellationToken)
		=> Post<ShioajiAccountRequest, ShioajiTrade[]>("order/trades", CreateAccountRequest(account), cancellationToken);

	public Task<ShioajiAccountBalance> GetAccountBalance(ShioajiAccount account,
		CancellationToken cancellationToken)
		=> Post<ShioajiAccountRequest, ShioajiAccountBalance>("portfolio/account_balance",
			CreateAccountRequest(account), cancellationToken);

	public Task<ShioajiMargin> GetMargin(ShioajiAccount account, CancellationToken cancellationToken)
		=> Post<ShioajiAccountRequest, ShioajiMargin>("portfolio/margin",
			CreateAccountRequest(account), cancellationToken);

	public Task<ShioajiPosition[]> GetPositions(ShioajiAccount account, CancellationToken cancellationToken)
		=> Post<ShioajiPositionsRequest, ShioajiPosition[]>("portfolio/position_unit", new()
		{
			AccountType = account.AccountType,
			BrokerId = account.BrokerId,
			AccountId = account.AccountId,
			PersonId = account.PersonId,
			Unit = "Common",
		}, cancellationToken);

	public async Task<HttpResponseMessage> OpenStream(CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_apiRoot, "stream/data"));
		request.Headers.Accept.Clear();
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.IsSuccessStatusCode)
			return response;

		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		var exception = CreateException(response.StatusCode, content);
		response.Dispose();
		throw exception;
	}

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		where T : class
		=> Send<T>(HttpMethod.Get, path, null, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body,
		CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
		=> Send<TResponse>(HttpMethod.Post, path, body, cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, object body,
		CancellationToken cancellationToken)
		where T : class
	{
		this.AddVerboseLog("Shioaji {0} {1}.", method, path);
		using var request = new HttpRequestMessage(method, new Uri(_apiRoot, path));
		if (body != null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");
		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response.StatusCode, content);
		if (content.IsEmpty())
			throw new InvalidDataException($"Shioaji returned an empty response for {path}.");
		return JsonConvert.DeserializeObject<T>(content, _jsonSettings)
			?? throw new InvalidDataException($"Shioaji returned an invalid response for {path}.");
	}

	private static ShioajiAccountRequest CreateAccountRequest(ShioajiAccount account)
		=> new()
		{
			AccountType = account.AccountType,
			BrokerId = account.BrokerId,
			AccountId = account.AccountId,
			PersonId = account.PersonId,
		};

	private static ShioajiTradeSubscriptionRequest CreateTradeSubscription(ShioajiAccount account)
		=> new()
		{
			AccountType = account.AccountType,
			BrokerId = account.BrokerId,
			AccountId = account.AccountId,
		};

	private static ShioajiApiException CreateException(HttpStatusCode statusCode, string content)
	{
		ShioajiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<ShioajiError>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message.IsEmpty(content).IsEmpty(statusCode.ToString());
		return new(statusCode, error?.Code,
			$"Shioaji API error{(error?.Code is { } code ? $" {code}" : string.Empty)}: {message}");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));
}
