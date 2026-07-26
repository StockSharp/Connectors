namespace StockSharp.Bcs.Native;

sealed class BcsRestClient : BaseLogReceiver
{
	private const string _information = "trade-api-information-service/api/v1/";
	private const string _marketData = "trade-api-market-data-connector/api/v1/";
	private const string _operations = "trade-api-bff-operations/api/v1/";
	private const string _orderDetails = "trade-api-bff-order-details/api/v1/";
	private const string _tradeDetails = "trade-api-bff-trade-details/api/v1/";
	private const string _portfolio = "trade-api-bff-portfolio/api/v1/";
	private const string _limits = "trade-api-bff-limit/api/v1/";

	private readonly HttpClient _http = new();
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		DateFormatHandling = DateFormatHandling.IsoDateFormat,
	};
	private readonly string _clientId;
	private readonly int _maxAttempts;
	private readonly Action<string> _refreshTokenChanged;
	private string _refreshToken;
	private string _accessToken;
	private DateTime _accessExpiresAt;
	private DateTime _lastRequestAt;

	public BcsRestClient(string endpoint, SecureString refreshToken, bool isReadOnly,
		int maxAttempts, Action<string> refreshTokenChanged)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/", UriKind.Absolute,
			out var uri) || uri.Scheme is not ("http" or "https"))
			throw new ArgumentException("A valid BCS REST endpoint is required.",
				nameof(endpoint));

		_refreshToken = refreshToken?.UnSecure().ThrowIfEmpty(nameof(refreshToken));
		_clientId = isReadOnly ? "trade-api-read" : "trade-api-write";
		_maxAttempts = Math.Max(1, maxAttempts);
		_refreshTokenChanged = refreshTokenChanged;
		_http.BaseAddress = uri;
		_http.Timeout = TimeSpan.FromSeconds(30);
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-BCS/1.0");
	}

	public override string Name => "BCS_REST";

	public string AccessToken
		=> _accessToken.ThrowIfEmpty(nameof(AccessToken));

	public async Task Authenticate(CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty() &&
			_accessExpiresAt > DateTime.UtcNow.AddMinutes(1))
			return;

		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() &&
				_accessExpiresAt > DateTime.UtcNow.AddMinutes(1))
				return;

			using var request = new HttpRequestMessage(HttpMethod.Post,
				"trade-api-keycloak/realms/tradeapi/protocol/openid-connect/token")
			{
				Content = new FormUrlEncodedContent(
				[
					new("client_id", _clientId),
					new("refresh_token", _refreshToken),
					new("grant_type", "refresh_token"),
				]),
			};

			await WaitRateLimit(cancellationToken);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var payload = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, payload);

			var token = Deserialize<BcsTokenResponse>(payload);
			if (token?.AccessToken.IsEmpty() != false)
				throw new InvalidDataException("BCS did not return an access token.");

			_accessToken = token.AccessToken;
			_accessExpiresAt = DateTime.UtcNow.AddSeconds(
				Math.Max(60, token.ExpiresIn));

			if (!token.RefreshToken.IsEmpty() &&
				!string.Equals(token.RefreshToken, _refreshToken,
					StringComparison.Ordinal))
			{
				_refreshToken = token.RefreshToken;
				_refreshTokenChanged?.Invoke(_refreshToken);
			}
		}
		finally
		{
			_authSync.Release();
		}
	}

	public Task<BcsInstrument[]> GetInstrumentsByTickers(string[] tickers,
		int page, int size, CancellationToken cancellationToken)
		=> Post<BcsInstrumentLookupRequest, BcsInstrument[]>(
			$"{_information}instruments/by-tickers?page={page}&size={size}",
			new() { Tickers = tickers }, cancellationToken);

	public Task<BcsInstrument[]> GetInstrumentsByType(string type, int page,
		int size, CancellationToken cancellationToken)
		=> Get<BcsInstrument[]>(
			$"{_information}instruments/by-type?type={Escape(type)}&page={page}&size={size}",
			cancellationToken);

	public Task<BcsCandlesResponse> GetCandles(string ticker, string classCode,
		DateTime from, DateTime to, string timeFrame,
		CancellationToken cancellationToken)
		=> Get<BcsCandlesResponse>(
			$"{_marketData}candles-chart?classCode={Escape(classCode)}" +
			$"&ticker={Escape(ticker)}&startDate={Escape(FormatDate(from))}" +
			$"&endDate={Escape(FormatDate(to))}&timeFrame={Escape(timeFrame)}",
			cancellationToken);

	public async Task<BcsQuote[]> GetQuotes(BcsInstrumentKey[] instruments,
		CancellationToken cancellationToken)
		=> (await Post<BcsQuotesRequest, BcsQuotesResponse>(
			$"{_marketData}quotes", new() { Instruments = instruments },
			cancellationToken))?.Records ?? [];

	public Task<BcsOrderBook> GetOrderBook(string ticker, string classCode,
		int depth, CancellationToken cancellationToken)
		=> Get<BcsOrderBook>(
			$"{_marketData}order-book?ticker={Escape(ticker)}" +
			$"&classCode={Escape(classCode)}&depth={Math.Clamp(depth, 1, 20)}",
			cancellationToken);

	public async Task<BcsTrade[]> GetLastTrades(BcsLastTradesRequest request,
		CancellationToken cancellationToken)
		=> (await Post<BcsLastTradesRequest, BcsLastTradesResponse>(
			$"{_marketData}last-trades", request, cancellationToken))?.Records ?? [];

	public Task<BcsPortfolioItem[]> GetPortfolio(
		CancellationToken cancellationToken)
		=> Get<BcsPortfolioItem[]>($"{_portfolio}portfolio", cancellationToken);

	public Task<BcsLimits> GetLimits(CancellationToken cancellationToken)
		=> Get<BcsLimits>($"{_limits}limits", cancellationToken);

	public Task<BcsShortOrderResponse> CreateOrder(BcsCreateOrderRequest order,
		CancellationToken cancellationToken)
		=> Post<BcsCreateOrderRequest, BcsShortOrderResponse>(
			$"{_operations}orders", order, cancellationToken);

	public Task<BcsShortOrderResponse> UpdateOrder(BcsUpdateOrderRequest order,
		CancellationToken cancellationToken)
		=> Post<BcsUpdateOrderRequest, BcsShortOrderResponse>(
			$"{_operations}orders/edit", order, cancellationToken);

	public Task<BcsShortOrderResponse> CancelOrder(BcsCancelOrderRequest order,
		CancellationToken cancellationToken)
		=> Post<BcsCancelOrderRequest, BcsShortOrderResponse>(
			$"{_operations}orders/cancel", order, cancellationToken);

	public Task<BcsOrderStatusResponse> GetOrder(string orderId,
		CancellationToken cancellationToken)
		=> Get<BcsOrderStatusResponse>(
			$"{_operations}orders?orderIdType={GetOrderIdType(orderId)}" +
			$"&orderId={Escape(orderId)}", cancellationToken);

	public Task<BcsOrderSearchResponse> SearchOrders(BcsOrderSearchRequest request,
		int page, int size, CancellationToken cancellationToken)
		=> Post<BcsOrderSearchRequest, BcsOrderSearchResponse>(
			$"{_orderDetails}orders/search?page={page}&size={size}" +
			"&sort=orderDateTime%2Cdesc", request, cancellationToken);

	public Task<BcsTradeSearchResponse> SearchTrades(BcsTradeSearchRequest request,
		int page, int size, CancellationToken cancellationToken)
		=> Post<BcsTradeSearchRequest, BcsTradeSearchResponse>(
			$"{_tradeDetails}trades/search?page={page}&size={size}" +
			"&sort=tradeDateTime%2Cdesc", request, cancellationToken);

	internal static string GetOrderIdType(string orderId)
		=> Guid.TryParse(orderId, out _) ? "1" : "2";

	internal static string SerializeBody<T>(T body)
		=> JsonConvert.SerializeObject(body, new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Ignore,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
		});

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body,
		CancellationToken cancellationToken)
		=> Send<TResponse>(HttpMethod.Post, path,
			JsonConvert.SerializeObject(body, _jsonSettings), cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, string body,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			await Authenticate(cancellationToken);
			await WaitRateLimit(cancellationToken);

			using var request = new HttpRequestMessage(method, path);
			request.Headers.Authorization =
				new AuthenticationHeaderValue("Bearer", _accessToken);
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseContentRead, cancellationToken);
			}
			catch (HttpRequestException error) when (attempt < _maxAttempts)
			{
				this.AddWarningLog(
					"BCS {0} retry {1} after a transport error: {2}",
					method, attempt, error.Message);
				await DelayRetry(null, attempt, cancellationToken);
				continue;
			}

			using (response)
			{
				var payload = await response.Content.ReadAsStringAsync(
					cancellationToken);
				if (response.IsSuccessStatusCode)
					return payload.IsEmpty() ? default : Deserialize<T>(payload);

				if (response.StatusCode == HttpStatusCode.Unauthorized &&
					attempt < _maxAttempts)
				{
					_accessToken = null;
					_accessExpiresAt = default;
					continue;
				}

				if (attempt < _maxAttempts && IsTransient(response.StatusCode))
				{
					this.AddWarningLog(
						"BCS {0} {1} retry {2} after HTTP {3}.",
						method, SafePath(path), attempt, (int)response.StatusCode);
					await DelayRetry(response, attempt, cancellationToken);
					continue;
				}

				throw CreateError(response.StatusCode, payload);
			}
		}
	}

	private T Deserialize<T>(string payload)
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings);

	private static Exception CreateError(HttpStatusCode statusCode,
		string payload)
	{
		BcsErrorResponse error = null;
		try
		{
			if (!payload.IsEmpty())
				error = JsonConvert.DeserializeObject<BcsErrorResponse>(payload);
		}
		catch (JsonException)
		{
		}

		var details = error?.Errors?
			.Select(e => new[] { e.Field, e.Type }
				.Where(s => !s.IsEmpty()).Join(": "))
			.Where(s => !s.IsEmpty())
			.Join("; ");
		var message = new[]
		{
			error?.ErrorDescription,
			error?.Error,
			error?.Type,
			details,
		}.Where(s => !s.IsEmpty()).Join(": ");

		if (message.IsEmpty())
			message = payload?.Length > 1000 ? payload[..1000] : payload;

		return new HttpRequestException(
			$"BCS API error {(int)statusCode} {statusCode}" +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, statusCode);
	}

	private async Task WaitRateLimit(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = TimeSpan.FromMilliseconds(110) -
				(DateTime.UtcNow - _lastRequestAt);
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_lastRequestAt = DateTime.UtcNow;
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout ||
			statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async Task DelayRetry(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response?.Headers.RetryAfter?.Delta ??
			TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		else if (delay > TimeSpan.FromSeconds(60))
			delay = TimeSpan.FromSeconds(60);
		await Task.Delay(delay, cancellationToken);
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string FormatDate(DateTime value)
		=> value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static string SafePath(string path)
	{
		var query = path.IndexOf('?');
		return query < 0 ? path : path[..query];
	}

	protected override void DisposeManaged()
	{
		_authSync.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
