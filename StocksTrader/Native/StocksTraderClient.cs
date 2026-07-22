namespace StockSharp.StocksTrader.Native;

sealed class StocksTraderClient : BaseLogReceiver
{
	private readonly Uri _address;
	private readonly AuthenticationHeaderValue _authorization;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new();
	private readonly StocksTraderRateGate _rateGate = new();

	public StocksTraderClient(Uri address, SecureString token, int maxAttempts)
	{
		if (address is null || !address.IsAbsoluteUri ||
			address.Scheme is not ("http" or "https"))
		{
			throw new ArgumentException(
				"StocksTrader address must be an absolute HTTP or HTTPS URI.",
				nameof(address));
		}

		_address = new(address.ToString().TrimEnd('/') + "/", UriKind.Absolute);
		_authorization = new("Bearer", token?.UnSecure().ThrowIfEmpty(nameof(token)));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.Timeout = TimeSpan.FromSeconds(30);
	}

	public override string Name => nameof(StocksTrader) + "_REST";

	public Task<StocksTraderAccount[]> GetAccountsAsync(
		CancellationToken cancellationToken)
		=> GetAsync<StocksTraderAccount[]>("api/v1/accounts", cancellationToken);

	public Task<StocksTraderAccountState> GetAccountStateAsync(string accountId,
		CancellationToken cancellationToken)
		=> GetAsync<StocksTraderAccountState>(AccountPath(accountId), cancellationToken);

	public Task<StocksTraderInstrument[]> GetInstrumentsAsync(string accountId,
		CancellationToken cancellationToken)
		=> GetAsync<StocksTraderInstrument[]>(
			$"{AccountPath(accountId)}/instruments", cancellationToken);

	public Task<StocksTraderQuote> GetQuoteAsync(string accountId, string ticker,
		CancellationToken cancellationToken)
		=> GetAsync<StocksTraderQuote>(
			$"{AccountPath(accountId)}/instruments/{Escape(ticker)}/quote",
			cancellationToken);

	public Task<StocksTraderOrder[]> GetOrdersAsync(string accountId,
		StocksTraderHistoryQuery query, CancellationToken cancellationToken)
		=> GetAsync<StocksTraderOrder[]>(
			$"{AccountPath(accountId)}/orders{BuildQuery(query)}", cancellationToken);

	public Task<StocksTraderDeal[]> GetDealsAsync(string accountId,
		StocksTraderHistoryQuery query, CancellationToken cancellationToken)
		=> GetAsync<StocksTraderDeal[]>(
			$"{AccountPath(accountId)}/deals{BuildQuery(query)}", cancellationToken);

	public Task<StocksTraderOrderResult> PlaceOrderAsync(string accountId,
		StocksTraderOrderRequest order, CancellationToken cancellationToken)
		=> SendAsync<StocksTraderOrderResult>(HttpMethod.Post,
			$"{AccountPath(accountId)}/orders", order?.ToForm(), false,
			cancellationToken);

	public Task ModifyOrderAsync(string accountId, string orderId,
		StocksTraderModifyOrderRequest order, CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Put,
			$"{AccountPath(accountId)}/orders/{Escape(orderId)}", order?.ToForm(),
			false, cancellationToken);

	public Task CancelOrderAsync(string accountId, string orderId,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Delete,
			$"{AccountPath(accountId)}/orders/{Escape(orderId)}", null, false,
			cancellationToken);

	public Task ModifyDealAsync(string accountId, string dealId,
		StocksTraderModifyDealRequest deal, CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Put,
			$"{AccountPath(accountId)}/deals/{Escape(dealId)}", deal?.ToForm(),
			false, cancellationToken);

	public Task CloseDealAsync(string accountId, string dealId,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Delete,
			$"{AccountPath(accountId)}/deals/{Escape(dealId)}", null, false,
			cancellationToken);

	private Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
		where T : class
		=> SendAsync<T>(HttpMethod.Get, path, null, true, cancellationToken);

	private async Task<T> SendAsync<T>(HttpMethod method, string path,
		KeyValuePair<string, string>[] form, bool isRetryEnabled,
		CancellationToken cancellationToken)
		where T : class
	{
		var payload = await SendPayloadAsync(method, path, form, isRetryEnabled,
			cancellationToken);
		return StocksTraderProtocol.Parse<T>(payload);
	}

	private async Task SendAsync(HttpMethod method, string path,
		KeyValuePair<string, string>[] form, bool isRetryEnabled,
		CancellationToken cancellationToken)
	{
		var payload = await SendPayloadAsync(method, path, form, isRetryEnabled,
			cancellationToken);
		StocksTraderProtocol.EnsureSuccess(payload);
	}

	private async Task<string> SendPayloadAsync(HttpMethod method, string path,
		KeyValuePair<string, string>[] form, bool isRetryEnabled,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			await _rateGate.WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, new Uri(_address, path));
			request.Headers.Authorization = _authorization;
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.UserAgent.ParseAdd("StockSharp-StocksTrader/1.0");
			if (form is not null)
				request.Content = new FormUrlEncodedContent(form);

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseContentRead, cancellationToken);
			}
			catch (HttpRequestException error) when (isRetryEnabled &&
				attempt < _maxAttempts)
			{
				this.AddWarningLog("StocksTrader {0} retry {1} after transport error: {2}",
					method, attempt, error.Message);
				await DelayRetryAsync(null, attempt, cancellationToken);
				continue;
			}

			using (response)
			{
				var payload = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					this.AddDebugLog("StocksTrader {0} {1} completed with HTTP {2}.",
						method, GetSafePath(path), (int)response.StatusCode);
					return payload;
				}

				if (isRetryEnabled && attempt < _maxAttempts &&
					IsTransient(response.StatusCode))
				{
					this.AddWarningLog(
						"StocksTrader {0} {1} retry {2} after HTTP {3}.",
						method, GetSafePath(path), attempt, (int)response.StatusCode);
					await DelayRetryAsync(response, attempt, cancellationToken);
					continue;
				}

				var error = CreateError(response.StatusCode, payload);
				this.AddErrorLog(error);
				throw error;
			}
		}
	}

	private static string AccountPath(string accountId)
		=> $"api/v1/accounts/{Escape(accountId)}";

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string BuildQuery(StocksTraderHistoryQuery query)
	{
		if (query is null)
			return string.Empty;

		var values = new List<string>();
		if (query.From is DateTime from)
			values.Add($"history_from={checked((long)from.ToUniversalTime().ToUnix())}");
		if (query.To is DateTime to)
			values.Add($"history_to={checked((long)to.ToUniversalTime().ToUnix())}");
		if (query.Skip is long skip)
			values.Add($"skip={Math.Max(0, skip).ToString(CultureInfo.InvariantCulture)}");
		if (query.Limit is int limit)
			values.Add($"limit={Math.Clamp(limit, 1, 500).ToString(CultureInfo.InvariantCulture)}");
		return values.Count == 0 ? string.Empty : "?" + values.Join("&");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout ||
			statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async Task DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response?.Headers.RetryAfter?.Delta ??
			TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		else if (delay > TimeSpan.FromSeconds(60))
			delay = TimeSpan.FromSeconds(60);
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateError(HttpStatusCode statusCode, string payload)
	{
		var message = StocksTraderProtocol.GetError(payload);
		if (message?.Length > 1000)
			message = message[..1000];
		return new HttpRequestException(
			$"StocksTrader API error {(int)statusCode} {statusCode}" +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, statusCode);
	}

	private static string GetSafePath(string path)
	{
		var queryIndex = path.IndexOf('?');
		return queryIndex < 0 ? path : path[..queryIndex];
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
