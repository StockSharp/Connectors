namespace StockSharp.TradeStation.Native;

sealed class TradeStationClient : Disposable
{
	private readonly HttpClient _httpClient;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		Converters = { new StringEnumConverter() },
	};

	public TradeStationClient(bool isDemo, SecureString token)
	{
		_httpClient = new()
		{
			BaseAddress = new Uri(isDemo ? "https://sim-api.tradestation.com/v3/" : "https://api.tradestation.com/v3/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.UnSecure());
	}

	public Task<TradeStationAccounts> GetAccounts(CancellationToken cancellationToken)
		=> Get<TradeStationAccounts>("brokerage/accounts", cancellationToken);

	public Task<TradeStationBalances> GetBalances(IEnumerable<string> accounts, CancellationToken cancellationToken)
		=> Get<TradeStationBalances>($"brokerage/accounts/{Join(accounts)}/balances", cancellationToken);

	public Task<TradeStationPositions> GetPositions(IEnumerable<string> accounts, CancellationToken cancellationToken)
		=> Get<TradeStationPositions>($"brokerage/accounts/{Join(accounts)}/positions", cancellationToken);

	public Task<TradeStationOrders> GetOrders(IEnumerable<string> accounts, CancellationToken cancellationToken)
		=> Get<TradeStationOrders>($"brokerage/accounts/{Join(accounts)}/orders", cancellationToken);

	public Task<TradeStationSymbols> GetSymbols(IEnumerable<string> symbols, CancellationToken cancellationToken)
		=> Get<TradeStationSymbols>($"marketdata/symbols/{Join(symbols)}", cancellationToken);

	public Task<TradeStationQuotes> GetQuotes(IEnumerable<string> symbols, CancellationToken cancellationToken)
		=> Get<TradeStationQuotes>($"marketdata/quotes/{Join(symbols)}", cancellationToken);

	public Task<TradeStationBars> GetBars(string symbol, int interval, string unit, DateTime? from, DateTime? to, long? count, CancellationToken cancellationToken)
	{
		var query = new List<string> { $"interval={interval}", $"unit={unit}" };
		if (count is > 0)
			query.Add($"barsback={count.Value}");
		if (from is DateTime fromDate)
			query.Add($"firstdate={fromDate.ToUniversalTime():O}");
		if (to is DateTime toDate)
			query.Add($"lastdate={toDate.ToUniversalTime():O}");
		return Get<TradeStationBars>($"marketdata/barcharts/{symbol.DataEscape()}?{query.Join("&")}", cancellationToken);
	}

	public async Task<string> PlaceOrder(TradeStationOrderRequest order, CancellationToken cancellationToken)
	{
		var response = await Post<TradeStationOrderResponses>("orderexecution/orders", order, cancellationToken);
		var error = response.Errors?.FirstOrDefault();
		error?.ThrowIfError();
		var result = response.Orders?.FirstOrDefault() ?? throw new InvalidOperationException("TradeStation did not return an order identifier.");
		result.ThrowIfError();
		return result.OrderId;
	}

	public async Task<string> ReplaceOrder(string orderId, TradeStationOrderReplaceRequest order, CancellationToken cancellationToken)
	{
		var response = await Send<TradeStationOrderResponse>(HttpMethod.Put, $"orderexecution/orders/{orderId.DataEscape()}", order, cancellationToken);
		response.ThrowIfError();
		return response.OrderId;
	}

	public async Task CancelOrder(string orderId, CancellationToken cancellationToken)
	{
		var response = await Send<TradeStationOrderResponse>(HttpMethod.Delete, $"orderexecution/orders/{orderId.DataEscape()}", null, cancellationToken);
		response.ThrowIfError();
	}

	public Task StreamQuotes(IEnumerable<string> symbols, Func<TradeStationQuote, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
		=> Stream($"marketdata/stream/quotes/{Join(symbols)}", handler, cancellationToken);

	public Task StreamOrders(IEnumerable<string> accounts, Func<TradeStationOrder, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
		=> Stream($"brokerage/stream/accounts/{Join(accounts)}/orders", handler, cancellationToken);

	public Task StreamPositions(IEnumerable<string> accounts, Func<TradeStationPosition, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
		=> Stream($"brokerage/stream/accounts/{Join(accounts)}/positions", handler, cancellationToken);

	private static string Join(IEnumerable<string> values)
		=> values.Where(v => !v.IsEmpty()).Select(v => v.DataEscape()).JoinComma();

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, cancellationToken);

	private Task<T> Post<T>(string path, object body, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Post, path, body, cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");
		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var error = text.IsEmpty() ? null : JsonConvert.DeserializeObject<TradeStationError>(text, _jsonSettings);
			throw new HttpRequestException(error?.Message.IsEmpty(error.Error) ?? $"TradeStation request failed with HTTP {(int)response.StatusCode}.", null, response.StatusCode);
		}
		return text.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(text, _jsonSettings);
	}

	private async Task Stream<T>(string path, Func<T, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream);
		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null)
				break;
			if (line.IsEmpty())
				continue;
			await handler(JsonConvert.DeserializeObject<T>(line, _jsonSettings), cancellationToken);
		}
	}

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		base.DisposeManaged();
	}
}
