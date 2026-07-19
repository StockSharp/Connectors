namespace StockSharp.Bitkub.Native;

sealed class BitkubRestClient : BaseLogReceiver
{
	private enum RateBuckets
	{
		Public,
		Private,
	}

	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _publicRateSync = new(1, 1);
	private readonly SemaphoreSlim _privateRateSync = new(1, 1);
	private readonly Lock _timestampSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextPublicRequest;
	private DateTime _nextPrivateRequest;
	private long _serverOffsetMilliseconds;
	private long _lastTimestamp;

	public BitkubRestClient(string endpoint, SecureString key, SecureString secret)
	{
		var value = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out _endpoint) ||
			!_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException(
				"Bitkub endpoint must be an absolute HTTPS URI.", nameof(endpoint));

		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"Bitkub API key and secret must be configured together.");
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
			"StockSharp-Bitkub-Connector/1.0");
	}

	public override string Name => "Bitkub_Rest";

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && _secret is not null;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_publicRateSync.Dispose();
		_privateRateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask SynchronizeTimeAsync(
		CancellationToken cancellationToken)
	{
		var started = DateTime.UtcNow.ToMilliseconds();
		var body = await SendRawAsync(HttpMethod.Get, "api/v3/servertime", null,
			null, false, RateBuckets.Public, true, cancellationToken);
		var completed = DateTime.UtcNow.ToMilliseconds();
		if (!long.TryParse(body.Trim(), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var serverTime))
			throw new InvalidDataException(
				"Bitkub returned an invalid server timestamp.");
		var midpoint = started + (completed - started) / 2;
		using (_timestampSync.EnterScope())
			_serverOffsetMilliseconds = serverTime - midpoint;
	}

	public async ValueTask<BitkubSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
		=> DeserializeV3<BitkubSymbol[]>(await SendRawAsync(HttpMethod.Get,
			"api/v3/market/symbols", null, null, false, RateBuckets.Public, true,
			cancellationToken), true) ?? [];

	public async ValueTask<BitkubTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "sym", symbol.NormalizeSymbol());
		return Deserialize<BitkubTicker>(await SendRawAsync(HttpMethod.Get,
			"api/v3/market/ticker", query, null, false, RateBuckets.Public, true,
			cancellationToken));
	}

	public async ValueTask<BitkubOrderBook> GetDepthAsync(string symbol, int depth,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "sym", symbol.NormalizeSymbol());
		Append(query, "lmt", depth.Min(200).Max(1));
		return DeserializeV3<BitkubOrderBook>(await SendRawAsync(HttpMethod.Get,
			"api/v3/market/depth", query, null, false, RateBuckets.Public, true,
			cancellationToken), true);
	}

	public async ValueTask<BitkubPublicTrade[]> GetTradesAsync(string symbol,
		int limit, CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "sym", symbol.NormalizeSymbol());
		Append(query, "lmt", limit.Min(1000).Max(1));
		return DeserializeV3<BitkubPublicTrade[]>(await SendRawAsync(HttpMethod.Get,
			"api/v3/market/trades", query, null, false, RateBuckets.Public, true,
			cancellationToken), true) ?? [];
	}

	public async ValueTask<BitkubBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		var response = Deserialize<BitkubV4Response<BitkubBalance[]>>(
			await SendRawAsync(HttpMethod.Get, "api/v4/wallet/balances", null,
				null, true, RateBuckets.Private, true, cancellationToken));
		if (!response.Code.EqualsIgnoreCase("0"))
			throw new InvalidOperationException(
				$"Bitkub API error {response.Code}: {response.Message}");
		return response.Data ?? [];
	}

	public ValueTask<BitkubPlaceOrderResult> PlaceOrderAsync(BitkubSides side,
		BitkubPlaceOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendPrivateV3Async<BitkubPlaceOrderResult>(HttpMethod.Post,
			side == BitkubSides.Buy ? "api/v3/market/place-bid" :
				"api/v3/market/place-ask", null, request, false, cancellationToken);
	}

	public async ValueTask CancelOrderAsync(BitkubCancelOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var body = Serialize(request);
		var response = await SendRawAsync(HttpMethod.Post,
			"api/v3/market/cancel-order", null, body, true, RateBuckets.Private,
			false, cancellationToken);
		_ = DeserializeV3<BitkubPlaceOrderResult>(response, false);
	}

	public async ValueTask<BitkubOpenOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "sym", symbol.NormalizeSymbol());
		return DeserializeV3<BitkubOpenOrder[]>(await SendRawAsync(HttpMethod.Get,
			"api/v3/market/my-open-orders", query, null, true,
			RateBuckets.Private, true, cancellationToken), true) ?? [];
	}

	public async ValueTask<BitkubResponse<BitkubOrderHistoryItem[]>>
		GetOrderHistoryAsync(string symbol, int limit, string cursor,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "sym", symbol.NormalizeSymbol());
		Append(query, "lmt", limit.Min(100).Max(1));
		Append(query, "pagination_type", "keyset");
		Append(query, "cursor", cursor);
		if (from is DateTime fromTime)
			Append(query, "start", fromTime.ToMilliseconds());
		if (to is DateTime toTime)
			Append(query, "end", toTime.ToMilliseconds());
		var response = Deserialize<BitkubResponse<BitkubOrderHistoryItem[]>>(
			await SendRawAsync(HttpMethod.Get, "api/v3/market/my-order-history",
				query, null, true, RateBuckets.Private, true, cancellationToken));
		EnsureSuccess(response.Error);
		response.Result ??= [];
		return response;
	}

	public async ValueTask<BitkubOrderInfo> GetOrderInfoAsync(string symbol,
		string orderId, BitkubSides side, CancellationToken cancellationToken)
	{
		var query = new StringBuilder();
		Append(query, "sym", symbol.NormalizeSymbol());
		Append(query, "id", orderId.ThrowIfEmpty(nameof(orderId)));
		Append(query, "sd", side == BitkubSides.Buy ? "buy" : "sell");
		return DeserializeV3<BitkubOrderInfo>(await SendRawAsync(HttpMethod.Get,
			"api/v3/market/order-info", query, null, true, RateBuckets.Private,
			true, cancellationToken), true);
	}

	public BitkubWebSocketAuthenticationData CreateWebSocketAuthentication()
	{
		EnsureCredentials();
		var timestamp = CreateTimestamp();
		return new()
		{
			ApiKey = _apiKey,
			Timestamp = timestamp,
			Signature = Sign(timestamp),
		};
	}

	private async ValueTask<TPayload> SendPrivateV3Async<TPayload>(HttpMethod method,
		string path, StringBuilder query, BitkubPlaceOrderRequest request,
		bool isRetrySafe, CancellationToken cancellationToken)
	{
		var body = request is null ? null : Serialize(request);
		return DeserializeV3<TPayload>(await SendRawAsync(method, path, query, body,
			true, RateBuckets.Private, isRetrySafe, cancellationToken), true);
	}

	private string Serialize<TRequest>(TRequest request)
		=> JsonConvert.SerializeObject(request, _jsonSettings);

	private async ValueTask<string> SendRawAsync(HttpMethod method, string path,
		StringBuilder query, string body, bool isPrivate, RateBuckets bucket,
		bool isRetrySafe, CancellationToken cancellationToken)
	{
		if (isPrivate)
			EnsureCredentials();
		var uri = CreateUri(path, query?.ToString());
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(bucket, cancellationToken);
			using var request = new HttpRequestMessage(method, uri);
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			if (isPrivate)
				Authenticate(request, body);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				if (responseBody.IsEmpty())
					throw new InvalidDataException(
						"Bitkub returned an empty response.");
				return responseBody;
			}
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private void Authenticate(HttpRequestMessage request, string body)
	{
		var timestamp = CreateTimestamp();
		var payload = timestamp + request.Method.Method.ToUpperInvariant() +
			request.RequestUri.PathAndQuery + (body ?? string.Empty);
		request.Headers.TryAddWithoutValidation("X-BTK-APIKEY", _apiKey);
		request.Headers.TryAddWithoutValidation("X-BTK-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("X-BTK-SIGN", Sign(payload));
	}

	private string Sign(string payload)
	{
		using var hmac = new HMACSHA256(_secret);
		return Convert.ToHexString(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
	}

	private string CreateTimestamp()
	{
		using (_timestampSync.EnterScope())
		{
			var timestamp = DateTime.UtcNow.ToMilliseconds() +
				_serverOffsetMilliseconds;
			if (timestamp <= _lastTimestamp)
				timestamp = _lastTimestamp + 1;
			_lastTimestamp = timestamp;
			return timestamp.ToString(CultureInfo.InvariantCulture);
		}
	}

	private Uri CreateUri(string path, string query)
	{
		var uri = new Uri(_endpoint, path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
		if (query.IsEmpty())
			return uri;
		return new UriBuilder(uri) { Query = query }.Uri;
	}

	private TPayload DeserializeV3<TPayload>(string body, bool requireResult)
	{
		var response = Deserialize<BitkubResponse<TPayload>>(body);
		EnsureSuccess(response.Error);
		if (requireResult && response.Result is null)
			throw new InvalidDataException("Bitkub response has no result.");
		return response.Result;
	}

	private TPayload Deserialize<TPayload>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TPayload>(body, _jsonSettings)
				?? throw new InvalidDataException(
					"Bitkub returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitkub returned an unexpected response shape.", error);
		}
	}

	private static void EnsureSuccess(int errorCode)
	{
		if (errorCode == 0)
			return;
		throw new BitkubApiException(errorCode,
			$"Bitkub API error {errorCode}: {GetErrorMessage(errorCode)}");
	}

	private static string GetErrorMessage(int errorCode)
		=> errorCode switch
		{
			1 => "invalid JSON payload",
			2 => "missing API key",
			3 => "invalid API key",
			4 => "API key pending activation",
			5 => "IP address is not allowed",
			6 => "missing or invalid signature",
			7 => "missing timestamp",
			8 => "invalid timestamp",
			9 => "invalid user or operation is frozen",
			10 => "invalid parameter",
			11 => "invalid symbol",
			12 => "invalid amount",
			13 => "invalid rate",
			14 => "improper rate",
			15 => "amount is too low",
			16 => "failed to get balance",
			17 => "wallet is empty",
			18 => "insufficient balance",
			19 => "failed to insert order",
			20 => "failed to deduct balance",
			21 => "failed to add pending order",
			22 => "failed to get open orders",
			23 => "failed to deduct pending balance",
			24 => "order not found",
			25 => "failed to cancel order",
			_ => "unknown error",
		};

	private async ValueTask WaitRateLimitAsync(RateBuckets bucket,
		CancellationToken cancellationToken)
	{
		var sync = bucket == RateBuckets.Public
			? _publicRateSync
			: _privateRateSync;
		await sync.WaitAsync(cancellationToken);
		try
		{
			var next = bucket == RateBuckets.Public
				? _nextPublicRequest
				: _nextPrivateRequest;
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			var nextTime = DateTime.UtcNow + (bucket == RateBuckets.Public
				? TimeSpan.FromMilliseconds(55)
				: TimeSpan.FromMilliseconds(105));
			if (bucket == RateBuckets.Public)
				_nextPublicRequest = nextTime;
			else
				_nextPrivateRequest = nextTime;
		}
		finally
		{
			sync.Release();
		}
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
			(response.StatusCode == (HttpStatusCode)429
				? TimeSpan.FromSeconds(2)
				: TimeSpan.FromMilliseconds(500 * (1 << attempt)));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(HttpStatusCode statusCode,
		string body)
	{
		var details = body?.Trim();
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Bitkub HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitkub API key and secret are required for private operations.");
	}
}
