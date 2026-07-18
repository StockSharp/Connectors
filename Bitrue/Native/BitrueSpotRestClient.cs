namespace StockSharp.Bitrue.Native;

sealed class BitrueSpotRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly Uri _streamEndpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _secret;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = { new BitrueSingleOrArrayConverter<BitrueSpotTicker>() },
	};
	private DateTime _nextRequestTime;
	private long _timeOffsetMilliseconds;

	public BitrueSpotRestClient(string endpoint, string streamEndpoint, SecureString key,
		SecureString secret)
	{
		_endpoint = CreateEndpoint(endpoint, nameof(endpoint));
		_streamEndpoint = CreateEndpoint(streamEndpoint, nameof(streamEndpoint));
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secret = secret.IsEmpty() ? null : secret.UnSecure();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bitrue-Connector/1.0");
	}

	public override string Name => nameof(Bitrue) + "_SpotRest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && !_secret.IsEmpty();

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask SynchronizeTimeAsync(CancellationToken cancellationToken)
	{
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var result = await GetServerTimeAsync(cancellationToken);
		var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		if (result?.ServerTime <= 0)
			throw new InvalidDataException("Bitrue spot returned an invalid server time.");
		_timeOffsetMilliseconds = result.ServerTime - ((before + after) / 2);
	}

	public ValueTask<BitrueServerTime> GetServerTimeAsync(CancellationToken cancellationToken)
		=> SendReadAsync<BitrueServerTime>(_endpoint, HttpMethod.Get, "api/v1/time",
			new BitrueEmptyQuery(), false, cancellationToken);

	public ValueTask<BitrueSpotExchangeInfo> GetExchangeInfoAsync(
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotExchangeInfo>(_endpoint, HttpMethod.Get,
			"api/v1/exchangeInfo", new BitrueEmptyQuery(), false, cancellationToken);

	public ValueTask<BitrueSpotTicker[]> GetTickersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotTicker[]>(_endpoint, HttpMethod.Get,
			"api/v1/ticker/24hr", new BitrueSymbolQuery { Symbol = symbol }, false,
			cancellationToken);

	public ValueTask<BitrueBook> GetBookAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueBook>(_endpoint, HttpMethod.Get, "api/v1/depth",
			new BitrueSymbolQuery { Symbol = symbol, Limit = limit }, false,
			cancellationToken);

	public ValueTask<BitrueSpotTrade[]> GetTradesAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotTrade[]>(_endpoint, HttpMethod.Get, "api/v1/trades",
			new BitrueSymbolQuery { Symbol = symbol, Limit = limit }, false,
			cancellationToken);

	public ValueTask<BitrueSpotCandlesResponse> GetCandlesAsync(
		BitrueSpotCandlesQuery query, CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotCandlesResponse>(_endpoint, HttpMethod.Get,
			"api/v1/market/kline", query, false, cancellationToken);

	public ValueTask<BitrueSpotAccount> GetAccountAsync(CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotAccount>(_endpoint, HttpMethod.Get, "api/v1/account",
			new BitrueEmptyQuery(), true, cancellationToken);

	public ValueTask<BitrueSpotOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotOrder[]>(_endpoint, HttpMethod.Get,
			"api/v1/openOrders", new BitrueSpotOrdersQuery { Symbol = symbol }, true,
			cancellationToken);

	public ValueTask<BitrueSpotOrder[]> GetOrdersAsync(BitrueSpotOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotOrder[]>(_endpoint, HttpMethod.Get,
			"api/v1/allOrders", query, true, cancellationToken);

	public ValueTask<BitrueSpotOrder> GetOrderAsync(BitrueSpotOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotOrder>(_endpoint, HttpMethod.Get,
			"api/v1/order", query, true, cancellationToken);

	public ValueTask<BitrueSpotFill[]> GetFillsAsync(BitrueSpotTradesQuery query,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueSpotFill[]>(_endpoint, HttpMethod.Get,
			"api/v2/myTrades", query, true, cancellationToken);

	public ValueTask<BitrueSpotOrderAccepted> PlaceOrderAsync(
		BitrueSpotPlaceOrderQuery query, CancellationToken cancellationToken)
		=> SendWriteAsync<BitrueSpotOrderAccepted>(_endpoint, HttpMethod.Post,
			"api/v1/order", query, true, cancellationToken);

	public ValueTask<BitrueSpotOrderAccepted> CancelOrderAsync(
		BitrueSpotCancelOrderQuery query, CancellationToken cancellationToken)
		=> SendWriteAsync<BitrueSpotOrderAccepted>(_endpoint, HttpMethod.Delete,
			"api/v1/order", query, true, cancellationToken);

	public ValueTask<BitrueListenKeyResponse> AcquireListenKeyAsync(
		CancellationToken cancellationToken)
		=> SendWriteAsync<BitrueListenKeyResponse>(_streamEndpoint, HttpMethod.Post,
			"poseidon/api/v1/listenKey", new BitrueEmptyQuery(), false, cancellationToken,
			true);

	public ValueTask<BitrueListenKeyResponse> ExtendListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
		=> SendWriteAsync<BitrueListenKeyResponse>(_streamEndpoint, HttpMethod.Put,
			$"poseidon/api/v1/listenKey/{Uri.EscapeDataString(listenKey.ThrowIfEmpty(nameof(listenKey)))}",
			new BitrueEmptyQuery(), false, cancellationToken, true);

	public ValueTask<BitrueListenKeyResponse> CloseListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
		=> SendWriteAsync<BitrueListenKeyResponse>(_streamEndpoint, HttpMethod.Delete,
			$"poseidon/api/v1/listenKey/{Uri.EscapeDataString(listenKey.ThrowIfEmpty(nameof(listenKey)))}",
			new BitrueEmptyQuery(), false, cancellationToken, true);

	private async ValueTask<TData> SendReadAsync<TData>(Uri endpoint, HttpMethod method,
		string path, IBitrueQuery query, bool isSigned, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isSigned)
			EnsureCredentials();

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var requestPath = CreateRequestPath(path, query, isSigned);
			using var request = CreateRequest(endpoint, method, requestPath, isSigned);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TData>(body);

			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TData> SendWriteAsync<TData>(Uri endpoint, HttpMethod method,
		string path, IBitrueQuery query, bool isSigned, CancellationToken cancellationToken,
		bool isStreamRequest = false)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isSigned || isStreamRequest)
			EnsureCredentials();
		await WaitRateLimitAsync(cancellationToken);
		var requestPath = CreateRequestPath(path, query, isSigned);
		using var request = CreateRequest(endpoint, method, requestPath,
			isSigned || isStreamRequest);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return Deserialize<TData>(body);
	}

	private string CreateRequestPath(string path, IBitrueQuery query, bool isSigned)
	{
		path = "/" + path.TrimStart('/');
		var queryString = query.ToQueryString();
		if (isSigned)
		{
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
				_timeOffsetMilliseconds;
			queryString = AppendQuery(queryString, "recvWindow=5000");
			queryString = AppendQuery(queryString,
				"timestamp=" + timestamp.ToString(CultureInfo.InvariantCulture));
			var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
				_secret.UTF8(), queryString.UTF8()));
			queryString = AppendQuery(queryString, "signature=" + signature);
		}
		return path + (queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private HttpRequestMessage CreateRequest(Uri endpoint, HttpMethod method,
		string requestPath, bool addApiKey)
	{
		var request = new HttpRequestMessage(method,
			new Uri(endpoint, requestPath.TrimStart('/')));
		if (addApiKey)
			request.Headers.TryAddWithoutValidation("X-MBX-APIKEY", _apiKey);
		return request;
	}

	private TData Deserialize<TData>(string body)
	{
		try
		{
			if (body?.TrimStart().StartsWith('{') == true)
			{
				var error = JsonConvert.DeserializeObject<BitrueSpotError>(body, _jsonSettings);
				if (error?.Code is int code && code != 200)
					throw new InvalidOperationException(
						$"Bitrue spot API error {code}: {error.Message}".Trim());
			}
			var result = JsonConvert.DeserializeObject<TData>(body, _jsonSettings);
			return result is null
				? throw new InvalidDataException("Bitrue spot returned an empty response.")
				: result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Bitrue spot returned malformed JSON.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitrue API key and secret are required for private spot operations.");
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
		=> new(endpoint.ThrowIfEmpty(parameterName).TrimEnd('/') + "/", UriKind.Absolute);

	private static string AppendQuery(string query, string value)
		=> query.IsEmpty() ? value : query + "&" + value;

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		var text = body?.Trim();
		if (text?.Length > 512)
			text = text[..512];
		return new HttpRequestException(
			$"Bitrue spot HTTP {(int)statusCode} ({statusCode}): {text}".Trim(), null,
			statusCode);
	}
}
