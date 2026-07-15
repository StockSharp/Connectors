namespace StockSharp.Robinhood.Native;

sealed class RobinhoodMcpClient : Disposable
{
	private const string _protocolVersion = "2025-06-18";

	private readonly HttpClient _httpClient;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		Converters = { new StringEnumConverter() },
	};
	private readonly HashSet<string> _tools = new(StringComparer.Ordinal);
	private long _requestId;
	private string _sessionId;

	public RobinhoodMcpClient(Uri address, SecureString token)
	{
		_httpClient = new() { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.UnSecure());
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
	}

	public async Task Initialize(CancellationToken cancellationToken)
	{
		var result = await Send<McpInitializeParams, McpInitializeResult>("initialize", new()
		{
			ProtocolVersion = _protocolVersion,
			ClientInfo = new() { Name = "StockSharp", Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0" },
		}, cancellationToken);

		if (result.ProtocolVersion.IsEmpty())
			throw new InvalidOperationException("Robinhood MCP did not return a protocol version.");

		await Notify("notifications/initialized", new McpEmptyParams(), cancellationToken);

		var tools = await Send<McpEmptyParams, McpToolsResult>("tools/list", new(), cancellationToken);
		foreach (var tool in tools.Tools ?? [])
			_tools.Add(tool.Name);
	}

	public async Task<RobinhoodAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> (await Call<McpEmptyParams, RobinhoodAccountsData>("get_accounts", new(), cancellationToken))?.Accounts ?? [];

	public Task<RobinhoodPortfolio> GetPortfolio(string accountNumber, CancellationToken cancellationToken)
		=> Call<RobinhoodAccountRequest, RobinhoodPortfolio>("get_portfolio", new() { AccountNumber = accountNumber }, cancellationToken);

	public async Task<RobinhoodPosition[]> GetPositions(string accountNumber, CancellationToken cancellationToken)
		=> (await Call<RobinhoodAccountRequest, RobinhoodPositionsData>("get_equity_positions", new() { AccountNumber = accountNumber }, cancellationToken))?.Positions ?? [];

	public async Task<RobinhoodOrder[]> GetOrders(string accountNumber, CancellationToken cancellationToken)
		=> (await Call<RobinhoodAccountRequest, RobinhoodOrdersData>("get_equity_orders", new() { AccountNumber = accountNumber }, cancellationToken))?.Orders ?? [];

	public async Task<RobinhoodQuoteResult[]> GetQuotes(IEnumerable<string> symbols, CancellationToken cancellationToken)
	{
		var result = await Call<RobinhoodSymbolsRequest, RobinhoodQuotesData>("get_equity_quotes", new() { Symbols = symbols.Take(20).ToArray() }, cancellationToken);
		if (result?.Results?.Length > 0)
			return result.Results;
		return result?.Quotes?.Select(q => new RobinhoodQuoteResult { Quote = q }).ToArray() ?? [];
	}

	public async Task<RobinhoodSearchResult[]> Search(string query, CancellationToken cancellationToken)
		=> (await Call<RobinhoodSearchRequest, RobinhoodSearchData>("search", new() { Query = query }, cancellationToken))?.Results ?? [];

	public async Task<RobinhoodHistoricalResult[]> GetHistoricals(RobinhoodHistoricalRequest request, CancellationToken cancellationToken)
	{
		var toolName = _tools.Contains("get_equity_historicals") ? "get_equity_historicals" : "get_equity-historicals";
		var result = await Call<RobinhoodHistoricalRequest, RobinhoodHistoricalsData>(toolName, request, cancellationToken);
		if (result?.Results?.Length > 0)
			return result.Results;
		return result?.Historicals is null ? [] : [new() { Symbol = result.Symbol, Bars = result.Historicals }];
	}

	public Task<RobinhoodOrderReview> ReviewOrder(RobinhoodOrderRequest request, CancellationToken cancellationToken)
		=> Call<RobinhoodOrderRequest, RobinhoodOrderReview>("review_equity_order", request, cancellationToken);

	public async Task<string> PlaceOrder(RobinhoodOrderRequest request, CancellationToken cancellationToken)
	{
		var result = await Call<RobinhoodOrderRequest, RobinhoodOrder>("place_equity_order", request, cancellationToken);
		return result.OrderId.IsEmpty(result.Id) ?? throw new InvalidOperationException("Robinhood did not return an order identifier.");
	}

	public Task CancelOrder(string accountNumber, string orderId, CancellationToken cancellationToken)
		=> Call<RobinhoodCancelRequest, RobinhoodOrder>("cancel_equity_order", new() { AccountNumber = accountNumber, OrderId = orderId }, cancellationToken);

	private async Task<TData> Call<TArguments, TData>(string toolName, TArguments arguments, CancellationToken cancellationToken)
	{
		if (_tools.Count > 0 && !_tools.Contains(toolName))
			throw new NotSupportedException($"Robinhood MCP tool '{toolName}' is not available for this account.");

		var result = await Send<McpCallParams<TArguments>, McpCallResult<TData>>("tools/call", new()
		{
			Name = toolName,
			Arguments = arguments,
		}, cancellationToken);

		if (result.IsError)
			throw new InvalidOperationException(GetError(result.Content));

		if (result.StructuredContent is not null)
			return result.StructuredContent.Data;

		var text = result.Content?.FirstOrDefault(c => c.Type == McpContentType.Text && !c.Text.IsEmpty())?.Text;
		if (text.IsEmpty())
			return default;

		var envelope = JsonConvert.DeserializeObject<McpDataEnvelope<TData>>(text, _jsonSettings);
		if (envelope is not null)
			return envelope.Data;

		return JsonConvert.DeserializeObject<TData>(text, _jsonSettings);
	}

	private static string GetError(IEnumerable<McpContent> content)
		=> content?.Where(c => c.Type == McpContentType.Text).Select(c => c.Text).Where(t => !t.IsEmpty()).Join("; ") ?? "Robinhood MCP tool returned an error.";

	private Task Notify<TParams>(string method, TParams parameters, CancellationToken cancellationToken)
		=> Post(new McpRequest<TParams> { Method = method, Params = parameters }, cancellationToken);

	private async Task<TResult> Send<TParams, TResult>(string method, TParams parameters, CancellationToken cancellationToken)
	{
		var responseText = await Post(new McpRequest<TParams>
		{
			Id = Interlocked.Increment(ref _requestId),
			Method = method,
			Params = parameters,
		}, cancellationToken);

		var response = DeserializeResponse<TResult>(responseText);

		if (response.Error is not null)
			throw new InvalidOperationException($"Robinhood MCP error {response.Error.Code}: {response.Error.Message}");

		return response.Result;
	}

	private async Task<string> Post<TRequest>(TRequest payload, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
		request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
		if (!_sessionId.IsEmpty())
			request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
		request.Content = new StringContent(JsonConvert.SerializeObject(payload, _jsonSettings), Encoding.UTF8, "application/json");

		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"Robinhood MCP returned HTTP {(int)response.StatusCode}: {text}", null, response.StatusCode);

		if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
			_sessionId = values.FirstOrDefault();

		return text;
	}

	private McpResponse<TResult> DeserializeResponse<TResult>(string text)
	{
		if (!text.TrimStart().StartsWith("data:", StringComparison.Ordinal))
			return JsonConvert.DeserializeObject<McpResponse<TResult>>(text, _jsonSettings)
				?? throw new InvalidOperationException("Robinhood MCP returned an empty response.");

		foreach (var line in text.Split('\n'))
		{
			var value = line.Trim();
			if (value.StartsWith("data:", StringComparison.Ordinal))
			{
				var response = JsonConvert.DeserializeObject<McpResponse<TResult>>(value[5..].Trim(), _jsonSettings);
				if (response is not null && (response.Error is not null || response.Result is not null))
					return response;
			}
		}

		throw new InvalidOperationException("Robinhood MCP returned no JSON-RPC result in the SSE response.");
	}

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		base.DisposeManaged();
	}
}
