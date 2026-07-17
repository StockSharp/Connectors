namespace StockSharp.LemonMarkets.Native;

sealed class LemonClient : BaseLogReceiver
{
	private readonly Uri _origin;
	private readonly AuthenticationHeaderValue _authorization;
	private readonly string _privacyPrincipal;
	private readonly string _privacyJustification;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public LemonClient(string apiKey, bool isDemo, string privacyPrincipal,
		string privacyJustification, int maxAttempts)
	{
		apiKey.ThrowIfEmpty(nameof(apiKey));
		privacyPrincipal.ThrowIfEmpty(nameof(privacyPrincipal));
		privacyJustification.ThrowIfEmpty(nameof(privacyJustification));

		_origin = new(isDemo
			? "https://sandbox.api.lemon.markets/v1/"
			: "https://api.lemon.markets/v1/");
		_authorization = new("Bearer", apiKey);
		_privacyPrincipal = privacyPrincipal;
		_privacyJustification = privacyJustification;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public Task<LemonAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> GetAll<LemonAccount>("accounts?limit=100", int.MaxValue, cancellationToken);

	public Task<LemonAccount> GetAccount(string accountId, CancellationToken cancellationToken)
		=> Get<LemonAccount>($"accounts/{Escape(accountId)}", cancellationToken);

	public Task<LemonFinancials> GetFinancials(string accountId,
		CancellationToken cancellationToken)
		=> Get<LemonFinancials>($"accounts/{Escape(accountId)}/financials", cancellationToken);

	public Task<LemonSecuritiesAccount[]> GetSecuritiesAccounts(string accountId,
		CancellationToken cancellationToken)
		=> GetAll<LemonSecuritiesAccount>(
			$"accounts/{Escape(accountId)}/securities_accounts?limit=100", int.MaxValue,
			cancellationToken);

	public Task<LemonPosition[]> GetPositions(string accountId, string securitiesAccountId,
		CancellationToken cancellationToken)
		=> GetAll<LemonPosition>(AddOptionalQuery(
			$"accounts/{Escape(accountId)}/positions?limit=100", "securities_account",
			securitiesAccountId), int.MaxValue, cancellationToken);

	public Task<LemonInstrument[]> GetInstruments(CancellationToken cancellationToken)
		=> GetAll<LemonInstrument>("instruments?limit=100", int.MaxValue, cancellationToken);

	public Task<LemonInstrument> GetInstrument(string isin, CancellationToken cancellationToken)
		=> Get<LemonInstrument>($"instruments/{Escape(isin)}", cancellationToken);

	public Task<LemonPrice[]> GetPrices(string isin, CancellationToken cancellationToken)
		=> Get<LemonPrice[]>($"instruments/{Escape(isin)}/prices", cancellationToken);

	public Task<LemonOrder[]> GetOrders(string accountId, string securitiesAccountId,
		int maxItems, CancellationToken cancellationToken)
		=> GetAll<LemonOrder>(AddOptionalQuery(
			$"accounts/{Escape(accountId)}/orders?limit=100", "securities_account",
			securitiesAccountId), maxItems, cancellationToken);

	public Task<LemonOrder> GetOrder(string accountId, string orderId,
		CancellationToken cancellationToken)
		=> Get<LemonOrder>($"accounts/{Escape(accountId)}/orders/{Escape(orderId)}",
			cancellationToken);

	public Task<LemonTrade[]> GetTrades(string accountId, string securitiesAccountId,
		int maxItems, CancellationToken cancellationToken)
		=> GetAll<LemonTrade>(AddOptionalQuery(
			$"accounts/{Escape(accountId)}/trades?limit=100", "securities_account",
			securitiesAccountId), maxItems, cancellationToken);

	public Task<LemonTrade> GetTrade(string accountId, string tradeId,
		CancellationToken cancellationToken)
		=> Get<LemonTrade>($"accounts/{Escape(accountId)}/trades/{Escape(tradeId)}",
			cancellationToken);

	public Task<LemonEvent[]> GetEvents(string accountId, DateTime since,
		CancellationToken cancellationToken)
	{
		var path = "events?limit=100&sort=asc" +
			$"&context.account={Escape(accountId)}" +
			$"&since={Escape(since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";
		return GetAll<LemonEvent>(path, int.MaxValue, cancellationToken);
	}

	public Task<LemonOrder> CreateOrder(string accountId, LemonCreateOrderRequest request,
		string idempotencyKey, CancellationToken cancellationToken)
		=> Post<LemonCreateOrderRequest, LemonOrder>(
			$"accounts/{Escape(accountId)}/orders", request, idempotencyKey, true,
			cancellationToken);

	public Task<LemonOrder> ConfirmOrder(string accountId, string orderId,
		LemonConfirmOrderRequest request, CancellationToken cancellationToken)
		=> Post<LemonConfirmOrderRequest, LemonOrder>(
			$"accounts/{Escape(accountId)}/orders/{Escape(orderId)}/confirm", request,
			null, false, cancellationToken);

	public Task<LemonOrder> CancelOrder(string accountId, string orderId,
		LemonCancelOrderRequest request, CancellationToken cancellationToken)
		=> Post<LemonCancelOrderRequest, LemonOrder>(
			$"accounts/{Escape(accountId)}/orders/{Escape(orderId)}/cancel", request,
			null, false, cancellationToken);

	private async Task<T[]> GetAll<T>(string path, int maxItems,
		CancellationToken cancellationToken)
	{
		if (maxItems <= 0)
			return [];

		var result = new List<T>();
		var basePath = path.Split('?')[0];
		var requestPath = path;
		var cursors = new HashSet<string>(StringComparer.Ordinal);
		while (result.Count < maxItems)
		{
			var page = await Get<LemonPage<T>>(requestPath, cancellationToken);
			foreach (var item in page?.Data ?? [])
			{
				result.Add(item);
				if (result.Count >= maxItems)
					break;
			}

			var cursor = page?.Pagination?.NextCursor;
			if (cursor.IsEmpty() || !cursors.Add(cursor))
				break;
			requestPath = $"{basePath}?cursor={Escape(cursor)}";
		}
		return [.. result];
	}

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, null, true, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body,
		string idempotencyKey, bool isRetryEnabled, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(body);
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		return Send<TResponse>(HttpMethod.Post, path, json, idempotencyKey,
			isRetryEnabled, cancellationToken);
	}

	private async Task<T> Send<T>(HttpMethod method, string path, string json,
		string idempotencyKey, bool isRetryEnabled, CancellationToken cancellationToken)
	{
		using var response = await Send(method, path, json, idempotencyKey,
			isRetryEnabled, cancellationToken);
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (payload.IsEmpty())
			throw new InvalidOperationException($"lemon.markets {path} returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings)
				?? throw new InvalidOperationException(
					$"lemon.markets {path} returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidOperationException(
				$"lemon.markets {path} returned invalid JSON.", error);
		}
	}

	private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string json,
		string idempotencyKey, bool isRetryEnabled, CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method,
				new Uri(_origin, path.TrimStart('/')));
			request.Headers.Authorization = _authorization;
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.UserAgent.ParseAdd("StockSharp-LemonMarkets/1.0");
			request.Headers.TryAddWithoutValidation("LMG-Data-Privacy-Access-Principal",
				_privacyPrincipal);
			request.Headers.TryAddWithoutValidation("LMG-Data-Privacy-Access-Justification",
				_privacyJustification);
			if (!idempotencyKey.IsEmpty())
				request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
			if (json != null)
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException) when (isRetryEnabled && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
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
		=> statusCode == HttpStatusCode.RequestTimeout ||
			statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan delta)
			return ClampDelay(delta);
		var retryDate = response?.Headers.RetryAfter?.Date;
		if (retryDate != null)
			return ClampDelay(retryDate.Value.UtcDateTime - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay < TimeSpan.Zero ? TimeSpan.Zero
			: delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;

	private HttpRequestException CreateError(HttpStatusCode statusCode, string payload)
	{
		LemonErrorResponse error = null;
		try
		{
			error = payload.IsEmpty()
				? null
				: JsonConvert.DeserializeObject<LemonErrorResponse>(payload, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var message = error?.Message.IsEmpty(payload);
		if (message?.Length > 1000)
			message = message[..1000];
		return new HttpRequestException($"lemon.markets API error {(int)statusCode}" +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, statusCode);
	}

	private static string AddOptionalQuery(string path, string name, string value)
		=> value.IsEmpty() ? path : $"{path}&{name}={Escape(value)}";

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
