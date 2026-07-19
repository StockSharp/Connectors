namespace StockSharp.CoinDCX.Native;

sealed class CoinDCXRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly Uri _publicEndpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private readonly Lock _timestampSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private DateTime _nextRequest;
	private long _lastTimestamp;

	public CoinDCXRestClient(string endpoint, string publicEndpoint,
		SecureString key, SecureString secret)
	{
		_endpoint = CreateEndpoint(endpoint, nameof(endpoint));
		_publicEndpoint = CreateEndpoint(publicEndpoint, nameof(publicEndpoint));
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"CoinDCX API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);

		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinDCX-Connector/1.0");
	}

	public override string Name => "CoinDCX_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && _secret is not null;

	public string ApiKey => _apiKey;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_requestSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<CoinDCXMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendPublicAsync<CoinDCXMarket[]>(_endpoint,
			"exchange/v1/markets_details", null, cancellationToken);

	public ValueTask<CoinDCXTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> SendPublicAsync<CoinDCXTicker[]>(_endpoint, "exchange/ticker", null,
			cancellationToken);

	public ValueTask<CoinDCXOrderBook> GetOrderBookAsync(string pair,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "pair", pair.ThrowIfEmpty(nameof(pair)));
		return SendPublicAsync<CoinDCXOrderBook>(_publicEndpoint,
			"market_data/orderbook", query, cancellationToken);
	}

	public ValueTask<CoinDCXPublicTrade[]> GetTradesAsync(string pair, int limit,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "pair", pair.ThrowIfEmpty(nameof(pair)));
		Append(query, "limit", limit.Min(500).Max(1));
		return SendPublicAsync<CoinDCXPublicTrade[]>(_publicEndpoint,
			"market_data/trade_history", query, cancellationToken);
	}

	public ValueTask<CoinDCXCandle[]> GetCandlesAsync(string pair, string interval,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "pair", pair.ThrowIfEmpty(nameof(pair)));
		Append(query, "interval", interval.ThrowIfEmpty(nameof(interval)));
		if (from is DateTime fromValue)
			Append(query, "startTime", fromValue.ToUniversalTime().ToMilliseconds());
		if (to is DateTime toValue)
			Append(query, "endTime", toValue.ToUniversalTime().ToMilliseconds());
		Append(query, "limit", limit.Min(1000).Max(1));
		return SendPublicAsync<CoinDCXCandle[]>(_publicEndpoint,
			"market_data/candles", query, cancellationToken);
	}

	public ValueTask<CoinDCXBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXTimestampRequest, CoinDCXBalance[]>(
			"exchange/v1/users/balances", new(), true, cancellationToken);

	public ValueTask<CoinDCXPlaceOrderResponse> PlaceOrderAsync(
		CoinDCXPlaceOrderRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXPlaceOrderRequest, CoinDCXPlaceOrderResponse>(
			"exchange/v1/orders/create", request, false, cancellationToken);

	public ValueTask<CoinDCXOrder> GetOrderAsync(CoinDCXOrderIdRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXOrderIdRequest, CoinDCXOrder>(
			"exchange/v1/orders/status", request, true, cancellationToken);

	public ValueTask<CoinDCXOrder[]> GetOrdersAsync(CoinDCXOrderIdsRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXOrderIdsRequest, CoinDCXOrder[]>(
			"exchange/v1/orders/status_multiple", request, true, cancellationToken);

	public ValueTask<CoinDCXOrder[]> GetActiveOrdersAsync(
		CoinDCXActiveOrdersRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXActiveOrdersRequest, CoinDCXOrder[]>(
			"exchange/v1/orders/active_orders", request, true, cancellationToken);

	public ValueTask<CoinDCXAccountTrade[]> GetAccountTradesAsync(
		CoinDCXTradeHistoryRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXTradeHistoryRequest, CoinDCXAccountTrade[]>(
			"exchange/v1/orders/trade_history", request, true, cancellationToken);

	public ValueTask<CoinDCXOrder> EditOrderAsync(CoinDCXEditOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<CoinDCXEditOrderRequest, CoinDCXOrder>(
			"exchange/v1/orders/edit", request, false, cancellationToken);

	public ValueTask CancelOrderAsync(CoinDCXOrderIdRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync("exchange/v1/orders/cancel", request, cancellationToken);

	public ValueTask CancelOrdersAsync(CoinDCXOrderIdsRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync("exchange/v1/orders/cancel_by_ids", request,
			cancellationToken);

	public ValueTask CancelAllAsync(CoinDCXCancelAllRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync("exchange/v1/orders/cancel_all", request,
			cancellationToken);

	public string CreateSocketSignature()
	{
		EnsureCredentials();
		var payload = Serialize(new CoinDCXSocketAuthenticationPayload
		{
			Channel = "coindcx",
		});
		return Sign(payload);
	}

	private async ValueTask<TResponse> SendPublicAsync<TResponse>(Uri endpoint,
		string path, StringBuilder query, CancellationToken cancellationToken)
	{
		var uri = CreateUri(endpoint, path, query?.ToString());
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendPrivateAsync<TRequest, TResponse>(
		string path, TRequest requestBody, bool isRetrySafe,
		CancellationToken cancellationToken)
		where TRequest : CoinDCXPrivateRequest
	{
		ArgumentNullException.ThrowIfNull(requestBody);
		EnsureCredentials();
		for (var attempt = 0; ; attempt++)
		{
			requestBody.Timestamp = CreateTimestamp();
			var body = Serialize(requestBody);
			using var response = await SendPrivateRequestAsync(path, body,
				cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(responseBody);
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask SendPrivateAsync<TRequest>(string path,
		TRequest requestBody, CancellationToken cancellationToken)
		where TRequest : CoinDCXPrivateRequest
	{
		ArgumentNullException.ThrowIfNull(requestBody);
		EnsureCredentials();
		requestBody.Timestamp = CreateTimestamp();
		var body = Serialize(requestBody);
		using var response = await SendPrivateRequestAsync(path, body,
			cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
	}

	private async ValueTask<HttpResponseMessage> SendPrivateRequestAsync(string path,
		string body, CancellationToken cancellationToken)
	{
		await WaitRateLimitAsync(cancellationToken);
		var request = new HttpRequestMessage(HttpMethod.Post,
			CreateUri(_endpoint, path, null))
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		request.Headers.TryAddWithoutValidation("X-AUTH-APIKEY", _apiKey);
		request.Headers.TryAddWithoutValidation("X-AUTH-SIGNATURE", Sign(body));
		try
		{
			return await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		}
		finally
		{
			request.Dispose();
		}
	}

	private string Serialize<TRequest>(TRequest request)
		=> JsonConvert.SerializeObject(request, _jsonSettings);

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("CoinDCX returned an empty response.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
				throw new InvalidDataException(
					"CoinDCX returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinDCX returned an unexpected response shape.", error);
		}
	}

	private string Sign(string payload)
	{
		using var hmac = new HMACSHA256(_secret);
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
	}

	private long CreateTimestamp()
	{
		using (_timestampSync.EnterScope())
		{
			var timestamp = DateTime.UtcNow.ToMilliseconds();
			if (timestamp <= _lastTimestamp)
				timestamp = _lastTimestamp + 1;
			_lastTimestamp = timestamp;
			return timestamp;
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(35);
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<CoinDCXApiResult>(
				body ?? string.Empty, _jsonSettings);
			if (error is not null)
				details = new[] { error.Code, error.Status, error.Message }
					.Where(static value => !value.IsEmpty()).Join(": ")
					.IsEmpty(details);
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"CoinDCX HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"CoinDCX API key and secret are required for private operations.");
	}

	private static Uri CreateEndpoint(string value, string parameterName)
	{
		value = value.ThrowIfEmpty(parameterName).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"CoinDCX endpoint must be an absolute HTTPS URI.", parameterName);
		return endpoint;
	}

	private static Uri CreateUri(Uri endpoint, string path, string query)
	{
		var uri = new Uri(endpoint, path.ThrowIfEmpty(nameof(path)));
		return query.IsEmpty() ? uri : new UriBuilder(uri) { Query = query }.Uri;
	}

	private static void Append(StringBuilder query, string name, string value)
	{
		if (value.IsEmpty())
			return;
		if (query.Length > 0)
			query.Append('&');
		query.Append(name).Append('=').Append(Uri.EscapeDataString(value));
	}

	private static void Append(StringBuilder query, string name, int value)
		=> Append(query, name, value.ToString(CultureInfo.InvariantCulture));

	private static void Append(StringBuilder query, string name, long value)
		=> Append(query, name, value.ToString(CultureInfo.InvariantCulture));

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

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
