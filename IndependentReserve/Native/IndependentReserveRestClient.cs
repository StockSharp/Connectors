namespace StockSharp.IndependentReserve.Native;

sealed class IndependentReserveRestClient : BaseLogReceiver
{
	private sealed class RateGate : IDisposable
	{
		private readonly SemaphoreSlim _sync = new(1, 1);
		private readonly TimeSpan _interval;
		private DateTime _nextRequest;

		public RateGate(TimeSpan interval) => _interval = interval;

		public async ValueTask WaitAsync(CancellationToken cancellationToken)
		{
			await _sync.WaitAsync(cancellationToken);
			try
			{
				var delay = _nextRequest - DateTime.UtcNow;
				if (delay > TimeSpan.Zero)
					await Task.Delay(delay, cancellationToken);
				_nextRequest = DateTime.UtcNow + _interval;
			}
			finally
			{
				_sync.Release();
			}
		}

		public void Dispose() => _sync.Dispose();
	}

	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly RateGate _publicGate = new(TimeSpan.FromMilliseconds(120));
	private readonly RateGate _privateGate = new(TimeSpan.FromMilliseconds(220));
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};

	public IndependentReserveRestClient(string endpoint, SecureString key,
		SecureString secret)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		_apiSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != _apiSecret.IsEmpty())
			throw new ArgumentException(
				"Independent Reserve API key and secret must be configured together.");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-IndependentReserve-Connector/1.0");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "IndependentReserve_REST";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_publicGate.Dispose();
		_privateGate.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<IndependentReserveCurrencyConfig[]> GetCurrenciesAsync(
		CancellationToken cancellationToken)
		=> GetPublicAsync<IndependentReserveCurrencyConfig[]>(
			"/Public/GetPrimaryCurrencyConfig2", null, cancellationToken);

	public ValueTask<string[]> GetSecondaryCurrenciesAsync(
		CancellationToken cancellationToken)
		=> GetPublicAsync<string[]>("/Public/GetValidSecondaryCurrencyCodes",
			null, cancellationToken);

	public ValueTask<IndependentReserveMarketSummary> GetMarketSummaryAsync(
		IndependentReserveMarketRequest request,
		CancellationToken cancellationToken)
		=> GetPublicAsync<IndependentReserveMarketSummary>(
			"/Public/GetMarketSummary", IndependentReserveQueryWriter.Create(request),
			cancellationToken);

	public ValueTask<IndependentReserveOrderBook> GetOrderBookAsync(
		IndependentReserveMarketRequest request,
		CancellationToken cancellationToken)
		=> GetPublicAsync<IndependentReserveOrderBook>("/Public/GetAllOrders",
			IndependentReserveQueryWriter.Create(request), cancellationToken);

	public ValueTask<IndependentReserveRecentTrades> GetRecentTradesAsync(
		IndependentReserveRecentTradesRequest request,
		CancellationToken cancellationToken)
		=> GetPublicAsync<IndependentReserveRecentTrades>(
			"/Public/GetRecentTrades", IndependentReserveQueryWriter.Create(request),
			cancellationToken);

	public ValueTask<IndependentReserveTradeHistory> GetTradeHistoryAsync(
		IndependentReserveHistoryRequest request,
		CancellationToken cancellationToken)
		=> GetPublicAsync<IndependentReserveTradeHistory>(
			"/Public/GetTradeHistorySummary",
			IndependentReserveQueryWriter.Create(request), cancellationToken);

	public ValueTask<IndependentReserveAccount[]> GetAccountsAsync(
		CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReserveAccount[]>(
			"/Private/GetAccounts", new IndependentReserveAccountsRequest(),
			cancellationToken);

	public ValueTask<IndependentReservePage<IndependentReserveHistoryOrder>>
		GetOpenOrdersAsync(IndependentReserveOpenOrdersRequest request,
			CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReservePage<IndependentReserveHistoryOrder>>(
			"/Private/GetOpenOrders", request, cancellationToken);

	public ValueTask<IndependentReservePage<IndependentReserveHistoryOrder>>
		GetClosedOrdersAsync(IndependentReserveClosedOrdersRequest request,
			CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReservePage<IndependentReserveHistoryOrder>>(
			"/Private/GetClosedOrders", request, cancellationToken);

	public ValueTask<IndependentReserveOrder> GetOrderAsync(
		IndependentReserveOrderLookupRequest request,
		CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReserveOrder>(
			"/Private/GetOrderDetails", request, cancellationToken);

	public ValueTask<IndependentReservePage<IndependentReserveUserTrade>>
		GetTradesAsync(IndependentReserveTradesRequest request,
			CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReservePage<IndependentReserveUserTrade>>(
			"/Private/GetTrades", request, cancellationToken);

	public ValueTask<IndependentReservePage<IndependentReserveUserTrade>>
		GetTradesByOrderAsync(IndependentReserveTradesByOrderRequest request,
			CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReservePage<IndependentReserveUserTrade>>(
			"/Private/GetTradesByOrder", request, cancellationToken);

	public ValueTask<IndependentReserveOrder> PlaceLimitOrderAsync(
		IndependentReserveLimitOrderRequest request,
		CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReserveOrder>(
			"/Private/PlaceLimitOrder", request, cancellationToken);

	public ValueTask<IndependentReserveOrder> PlaceMarketOrderAsync(
		IndependentReserveMarketOrderRequest request,
		CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReserveOrder>(
			"/Private/PlaceMarketOrder", request, cancellationToken);

	public ValueTask<IndependentReserveOrder> CancelOrderAsync(
		IndependentReserveCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> PostPrivateAsync<IndependentReserveOrder>(
			"/Private/CancelOrder", request, cancellationToken);

	private async ValueTask<TResponse> GetPublicAsync<TResponse>(string path,
		string query, CancellationToken cancellationToken)
	{
		var requestPath = query.IsEmpty() ? path : $"{path}?{query}";
		for (var attempt = 0; ; attempt++)
		{
			await _publicGate.WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, requestPath.TrimStart('/')));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (attempt < _maximumReadAttempts - 1 &&
				IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body);
			return Deserialize<TResponse>(body);
		}
	}

	private async ValueTask<TResponse> PostPrivateAsync<TResponse>(string path,
		IndependentReservePrivateRequest payload,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		ArgumentNullException.ThrowIfNull(payload);
		payload.ApiKey = _apiKey;
		payload.Expiry = DateTimeOffset.UtcNow.AddSeconds(30)
			.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		payload.Signature = IndependentReserveSignatureWriter.Create(_endpoint,
			path, _apiSecret, payload);
		var json = JsonConvert.SerializeObject(payload, payload.GetType(),
			_jsonSettings);

		await _privateGate.WaitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateApiError(response.StatusCode, body);
		return Deserialize<TResponse>(body);
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException(
				"Independent Reserve returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
				throw new InvalidDataException(
					"Independent Reserve returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Independent Reserve returned an unexpected response shape.",
				error);
		}
	}

	private Exception CreateApiError(HttpStatusCode statusCode, string body)
	{
		IndependentReserveErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<IndependentReserveErrorResponse>(
				body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message;
		if (message.IsEmpty())
		{
			message = body?.Trim();
			if (message?.Length > 512)
				message = message[..512];
		}
		if (message.IsEmpty())
			message = "The API rejected the request.";
		return new IndependentReserveApiException(statusCode, error?.ErrorCode,
			$"Independent Reserve HTTP {(int)statusCode}" +
			(error?.ErrorCode.IsEmpty() == false
				? $" ({error.ErrorCode})"
				: string.Empty) + $": {message}");
	}

	private static Uri ValidateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"Independent Reserve REST endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Independent Reserve API key and secret are required for this operation.");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			(response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
			TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}
}
