namespace StockSharp.Trading212.Native;

sealed class Trading212Client : BaseLogReceiver
{
	private readonly Uri _origin;
	private readonly AuthenticationHeaderValue _authorization;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private readonly Trading212RateGate _accountGate = new(TimeSpan.FromSeconds(5));
	private readonly Trading212RateGate _exchangesGate = new(TimeSpan.FromSeconds(30));
	private readonly Trading212RateGate _instrumentsGate = new(TimeSpan.FromSeconds(50));
	private readonly Trading212RateGate _ordersGate = new(TimeSpan.FromSeconds(5));
	private readonly Trading212RateGate _singleOrderGate = new(TimeSpan.FromSeconds(1));
	private readonly Trading212RateGate _positionsGate = new(TimeSpan.FromSeconds(1));
	private readonly Trading212RateGate _historyGate = new(TimeSpan.FromSeconds(10));
	private readonly Trading212RateGate _limitOrderGate = new(TimeSpan.FromSeconds(2));
	private readonly Trading212RateGate _marketOrderGate = new(TimeSpan.FromMilliseconds(1200));
	private readonly Trading212RateGate _stopOrderGate = new(TimeSpan.FromSeconds(2));
	private readonly Trading212RateGate _stopLimitOrderGate = new(TimeSpan.FromSeconds(2));
	private readonly Trading212RateGate _cancelOrderGate = new(TimeSpan.FromMilliseconds(1200));

	public Trading212Client(string apiKey, string apiSecret, bool isDemo, int maxAttempts)
	{
		apiKey.ThrowIfEmpty(nameof(apiKey));
		apiSecret.ThrowIfEmpty(nameof(apiSecret));
		_origin = new(isDemo ? "https://demo.trading212.com/" : "https://live.trading212.com/");
		_authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}")));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public Task<Trading212AccountSummary> GetAccountSummary(CancellationToken cancellationToken)
		=> Get<Trading212AccountSummary>("/api/v0/equity/account/summary", _accountGate, cancellationToken);

	public Task<Trading212Exchange[]> GetExchanges(CancellationToken cancellationToken)
		=> Get<Trading212Exchange[]>("/api/v0/equity/metadata/exchanges", _exchangesGate, cancellationToken);

	public Task<Trading212TradableInstrument[]> GetInstruments(CancellationToken cancellationToken)
		=> Get<Trading212TradableInstrument[]>("/api/v0/equity/metadata/instruments", _instrumentsGate, cancellationToken);

	public Task<Trading212Order[]> GetOrders(CancellationToken cancellationToken)
		=> Get<Trading212Order[]>("/api/v0/equity/orders", _ordersGate, cancellationToken);

	public Task<Trading212Order> GetOrder(long orderId, CancellationToken cancellationToken)
		=> Get<Trading212Order>($"/api/v0/equity/orders/{orderId.ToString(CultureInfo.InvariantCulture)}",
			_singleOrderGate, cancellationToken);

	public Task<Trading212Position[]> GetPositions(CancellationToken cancellationToken)
		=> Get<Trading212Position[]>("/api/v0/equity/positions", _positionsGate, cancellationToken);

	public Task<Trading212HistoricalOrderPage> GetHistoricalOrders(string ticker, int limit,
		CancellationToken cancellationToken)
	{
		var query = $"?limit={Math.Clamp(limit, 1, 50).ToString(CultureInfo.InvariantCulture)}";
		if (!ticker.IsEmpty())
			query += $"&ticker={Uri.EscapeDataString(ticker)}";
		return GetHistoricalOrders("/api/v0/equity/history/orders" + query, cancellationToken);
	}

	public Task<Trading212HistoricalOrderPage> GetHistoricalOrders(string nextPagePath,
		CancellationToken cancellationToken)
	{
		if (nextPagePath.IsEmpty() ||
			!nextPagePath.StartsWith("/api/v0/equity/history/orders", StringComparison.Ordinal))
			throw new ArgumentException("Trading 212 returned an invalid order-history page path.", nameof(nextPagePath));
		return Get<Trading212HistoricalOrderPage>(nextPagePath, _historyGate, cancellationToken);
	}

	public Task<Trading212Order> PlaceLimitOrder(Trading212LimitOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<Trading212LimitOrderRequest, Trading212Order>("/api/v0/equity/orders/limit",
			request, _limitOrderGate, cancellationToken);

	public Task<Trading212Order> PlaceMarketOrder(Trading212MarketOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<Trading212MarketOrderRequest, Trading212Order>("/api/v0/equity/orders/market",
			request, _marketOrderGate, cancellationToken);

	public Task<Trading212Order> PlaceStopOrder(Trading212StopOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<Trading212StopOrderRequest, Trading212Order>("/api/v0/equity/orders/stop",
			request, _stopOrderGate, cancellationToken);

	public Task<Trading212Order> PlaceStopLimitOrder(Trading212StopLimitOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<Trading212StopLimitOrderRequest, Trading212Order>("/api/v0/equity/orders/stop_limit",
			request, _stopLimitOrderGate, cancellationToken);

	public async Task CancelOrder(long orderId, CancellationToken cancellationToken)
	{
		using var response = await Send(HttpMethod.Delete,
			$"/api/v0/equity/orders/{orderId.ToString(CultureInfo.InvariantCulture)}",
			_cancelOrderGate, null, false, cancellationToken);
	}

	private Task<T> Get<T>(string path, Trading212RateGate gate, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, gate, null, true, cancellationToken);

	private async Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body,
		Trading212RateGate gate, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(body);
		using var content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8,
			"application/json");
		return await Send<TResponse>(HttpMethod.Post, path, gate, content, false, cancellationToken);
	}

	private async Task<T> Send<T>(HttpMethod method, string path, Trading212RateGate gate,
		HttpContent content, bool isRetryEnabled, CancellationToken cancellationToken)
	{
		var response = await Send(method, path, gate, content, isRetryEnabled, cancellationToken);
		using (response)
		{
			var payload = await response.Content.ReadAsStringAsync(cancellationToken);
			if (payload.IsEmpty())
				throw new InvalidOperationException($"Trading 212 {path} returned an empty response.");
			try
			{
				return JsonConvert.DeserializeObject<T>(payload, _jsonSettings)
					?? throw new InvalidOperationException($"Trading 212 {path} returned an empty JSON value.");
			}
			catch (JsonException error)
			{
				throw new InvalidOperationException($"Trading 212 {path} returned invalid JSON.", error);
			}
		}
	}

	private async Task<HttpResponseMessage> Send(HttpMethod method, string path,
		Trading212RateGate gate, HttpContent content, bool isRetryEnabled,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			await gate.Wait(cancellationToken);
			using var request = new HttpRequestMessage(method, new Uri(_origin, path));
			request.Headers.Authorization = _authorization;
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.UserAgent.ParseAdd("StockSharp-Trading212/1.0");
			request.Content = content;

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException) when (isRetryEnabled && attempt < _maxAttempts)
			{
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5))),
					cancellationToken);
				continue;
			}
			if (response.IsSuccessStatusCode)
				return response;

			if (isRetryEnabled && attempt < _maxAttempts && IsTransient(response.StatusCode))
			{
				var delay = GetRetryDelay(response, attempt);
				response.Dispose();
				await Task.Delay(delay, cancellationToken);
				continue;
			}

			var payload = await response.Content.ReadAsStringAsync(cancellationToken);
			var statusCode = response.StatusCode;
			response.Dispose();
			throw CreateError(statusCode, payload);
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout || statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
			return ClampDelay(delta);
		if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) &&
			long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var reset))
			return ClampDelay(DateTime.UnixEpoch.AddSeconds(reset) - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay < TimeSpan.Zero ? TimeSpan.Zero
			: delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;

	private Exception CreateError(HttpStatusCode statusCode, string payload)
	{
		Trading212ErrorResponse error = null;
		try
		{
			error = payload.IsEmpty()
				? null
				: JsonConvert.DeserializeObject<Trading212ErrorResponse>(payload, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var message = error?.Message.IsEmpty(error?.Detail).IsEmpty(error?.Error).IsEmpty(payload);
		if (message?.Length > 1000)
			message = message[..1000];
		return new HttpRequestException($"Trading 212 API error {(int)statusCode}" +
			(error?.Code.IsEmpty() == false ? $"/{error.Code}" : string.Empty) +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, statusCode);
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
