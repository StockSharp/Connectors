namespace StockSharp.Swissquote.Native;

internal sealed class SwissquoteRestClient : BaseLogReceiver
{
	private sealed class ApiResponse<T>
	{
		public T Value { get; init; }
		public string NextCursor { get; init; }
	}

	private static readonly Uri _productionTrading = new("https://bankingapi.swissquote.ch/ow-trading/api/v1/");
	private static readonly Uri _simulationTrading = new("https://bankingapi.simulator.swissquote.ch/ow-trading/api/v1/");
	private static readonly Uri _productionCustody = new("https://bankingapi.swissquote.ch/ow-custody/api/v1/");

	private readonly HttpClient _http;
	private readonly Uri _tradingAddress;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public SwissquoteRestClient(string token, bool isDemo, int maxAttempts)
	{
		token.ThrowIfEmpty(nameof(token));
		_tradingAddress = isDemo ? _simulationTrading : _productionTrading;
		_maxAttempts = Math.Max(1, maxAttempts);
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
				DecompressionMethods.Brotli,
		};
		_http = new(handler) { Timeout = TimeSpan.FromSeconds(30) };
		_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => nameof(Swissquote) + "_" + nameof(SwissquoteRestClient);

	public Task<SwissquoteCompleteOrder> GetOrder(string clientOrderId,
		CancellationToken cancellationToken)
		=> Get<SwissquoteCompleteOrder>(_tradingAddress,
			$"orders/{clientOrderId.ThrowIfEmpty(nameof(clientOrderId)).DataEscape()}", cancellationToken);

	public async Task<SwissquoteCompleteOrder[]> GetOrders(CancellationToken cancellationToken)
	{
		var result = new List<SwissquoteCompleteOrder>();
		string cursor = null;
		do
		{
			var path = AppendCursor("orders?limit=999", cursor);
			var page = await Send<SwissquoteCompleteOrder[]>(_tradingAddress, HttpMethod.Get, path,
				null, cancellationToken);
			result.AddRange(page.Value ?? []);
			cursor = page.NextCursor;
		}
		while (!cursor.IsEmpty());
		return [.. result];
	}

	public async Task<SwissquoteCompleteOrder> SubmitOrder(SwissquoteOrderRequest order,
		bool isBestEffort, bool isDryRun, CancellationToken cancellationToken)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));
		var path = $"orders?bestEffort={ToBoolean(isBestEffort)}&dryRun={ToBoolean(isDryRun)}";
		return (await Send<SwissquoteCompleteOrder>(_tradingAddress, HttpMethod.Post, path,
			order, cancellationToken)).Value
			?? throw new InvalidDataException("Swissquote returned an empty order response.");
	}

	public async Task<SwissquoteCompleteOrder> CancelOrder(string clientOrderId,
		CancellationToken cancellationToken)
		=> (await Send<SwissquoteCompleteOrder>(_tradingAddress, HttpMethod.Delete,
			$"orders/{clientOrderId.ThrowIfEmpty(nameof(clientOrderId)).DataEscape()}", null,
			cancellationToken, true)).Value;

	public async Task<SwissquoteCustomerOverview[]> GetCustomerAccounts(string customerId,
		CancellationToken cancellationToken)
	{
		var result = new List<SwissquoteCustomerOverview>();
		string cursor = null;
		do
		{
			var path = customerId.IsEmpty()
				? "customerAccounts?limit=999"
				: $"customerAccounts/{customerId.DataEscape()}?limit=999";
			path = AppendCursor(path, cursor);
			var page = await Send<SwissquoteCustomerOverview[]>(_productionCustody, HttpMethod.Get,
				path, null, cancellationToken);
			result.AddRange(page.Value ?? []);
			cursor = page.NextCursor;
		}
		while (!cursor.IsEmpty());
		return [.. result];
	}

	public async Task<SwissquoteAccount> GetPositions(string accountId, DateTime date,
		TimeSpan utcOffset, CancellationToken cancellationToken)
	{
		SwissquoteAccountInformation information = null;
		var positions = new List<SwissquotePosition>();
		string cursor = null;
		do
		{
			var path = $"accounts/{accountId.ThrowIfEmpty(nameof(accountId)).DataEscape()}/positions" +
				$"?date={FormatDate(date, utcOffset).DataEscape()}&eodIndicator=false&dateType=valueDate&limit=999";
			path = AppendCursor(path, cursor);
			var page = await Send<SwissquoteCustomerPositionsResponse>(_productionCustody,
				HttpMethod.Get, path, null, cancellationToken);
			var account = page.Value?.Customer?.AccountList?.FirstOrDefault(item =>
				item?.AccountInformation?.AccountIdentification.EqualsIgnoreCase(accountId) == true)
				?? page.Value?.Customer?.AccountList?.FirstOrDefault();
			information ??= account?.AccountInformation;
			positions.AddRange(account?.PositionList ?? []);
			cursor = page.NextCursor;
		}
		while (!cursor.IsEmpty());

		return new()
		{
			AccountInformation = information ?? new()
			{
				AccountIdentification = accountId,
				AccountIdentificationType = "other",
				AccountType = "safekeepingAccount",
			},
			PositionList = [.. positions],
		};
	}

	public Task<SwissquoteTradingCapacityResponse> GetTradingCapacity(string accountId,
		string currency, CancellationToken cancellationToken)
		=> Get<SwissquoteTradingCapacityResponse>(_productionCustody,
			$"accounts/{accountId.ThrowIfEmpty(nameof(accountId)).DataEscape()}/trading-capacity/" +
			currency.ThrowIfEmpty(nameof(currency)).ToUpperInvariant().DataEscape(), cancellationToken);

	public async Task<SwissquoteTransaction[]> GetTransactions(string accountId,
		DateTime date, TimeSpan utcOffset, CancellationToken cancellationToken)
	{
		var result = new List<SwissquoteTransaction>();
		string cursor = null;
		do
		{
			var path = $"accounts/{accountId.ThrowIfEmpty(nameof(accountId)).DataEscape()}/transactions" +
				$"?date={FormatDate(date, utcOffset).DataEscape()}&dateType=transactionDate&eodIndicator=true&limit=999";
			path = AppendCursor(path, cursor);
			var page = await Send<SwissquoteTransactionsResponse>(_productionCustody,
				HttpMethod.Get, path, null, cancellationToken);
			result.AddRange(page.Value?.Transactions ?? []);
			cursor = page.NextCursor;
		}
		while (!cursor.IsEmpty());
		return [.. result];
	}

	private async Task<T> Get<T>(Uri address, string path, CancellationToken cancellationToken)
		where T : class
		=> (await Send<T>(address, HttpMethod.Get, path, null, cancellationToken)).Value;

	private async Task<ApiResponse<T>> Send<T>(Uri address, HttpMethod method, string path,
		object body, CancellationToken cancellationToken, bool allowEmpty = false)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				using var request = new HttpRequestMessage(method, new Uri(address, path));
				request.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString());
				if (body != null)
				{
					request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings),
						Encoding.UTF8, "application/json");
				}

				try
				{
					using var response = await _http.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead, cancellationToken);
					var content = await response.Content.ReadAsStringAsync(cancellationToken);
					if (!response.IsSuccessStatusCode)
						throw CreateException(response.StatusCode, content, request);
					var cursor = GetHeader(response, "nextCursor");
					if (content.IsEmpty())
					{
						if (allowEmpty)
							return new() { NextCursor = cursor };
						throw new InvalidDataException($"Swissquote returned an empty response for {method} {path}.");
					}
					return new()
					{
						Value = JsonConvert.DeserializeObject<T>(content, _jsonSettings)
							?? throw new InvalidDataException($"Swissquote returned invalid JSON for {method} {path}."),
						NextCursor = cursor,
					};
				}
				catch (Exception ex) when (method == HttpMethod.Get && attempt < _maxAttempts &&
					IsTransient(ex))
				{
					await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
				}
			}
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private Exception CreateException(HttpStatusCode statusCode, string content,
		HttpRequestMessage request)
	{
		var problem = TryDeserializeProblem(content);
		var correlationId = request.Headers.TryGetValues("X-Correlation-ID", out var values)
			? values.FirstOrDefault() : null;
		var message = problem?.Detail.IsEmpty(problem?.Title).IsEmpty(content)
			.IsEmpty("Unknown Swissquote API error.");
		var code = problem?.Type;
		if (!code.IsEmpty())
			message = $"{code}: {message}";
		if (!correlationId.IsEmpty())
			message += $" Correlation ID: {correlationId}.";
		return new HttpRequestException(
			$"Swissquote request failed ({(int)statusCode}): {message}", null, statusCode);
	}

	private SwissquoteProblem TryDeserializeProblem(string content)
	{
		if (content.IsEmpty())
			return null;
		try
		{
			return JsonConvert.DeserializeObject<SwissquoteProblem>(content, _jsonSettings);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string GetHeader(HttpResponseMessage response, string name)
	{
		if (response.Headers.TryGetValues(name, out var values))
			return values.FirstOrDefault();
		if (response.Content.Headers.TryGetValues(name, out values))
			return values.FirstOrDefault();
		return null;
	}

	private static bool IsTransient(Exception exception)
		=> exception is HttpRequestException http &&
			(http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
				(int?)http.StatusCode >= 500);

	private static string AppendCursor(string path, string cursor)
		=> cursor.IsEmpty() ? path : path + (path.Contains('?') ? "&" : "?") +
			"cursor=" + cursor.DataEscape();

	private static string FormatDate(DateTime date, TimeSpan utcOffset)
	{
		var sign = utcOffset < TimeSpan.Zero ? '-' : '+';
		var absolute = utcOffset.Duration();
		return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + sign +
			$"{absolute.Hours:00}:{absolute.Minutes:00}";
	}

	private static string ToBoolean(bool value) => value ? "true" : "false";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}
}
