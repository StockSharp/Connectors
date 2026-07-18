namespace StockSharp.Bitrue.Native;

sealed class BitrueFuturesRestClient : BaseLogReceiver
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
	};
	private DateTime _nextRequestTime;
	private long _timeOffsetMilliseconds;

	public BitrueFuturesRestClient(string endpoint, string streamEndpoint, SecureString key,
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

	public override string Name => nameof(Bitrue) + "_FuturesRest";

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
			throw new InvalidDataException("Bitrue futures returned an invalid server time.");
		_timeOffsetMilliseconds = result.ServerTime - ((before + after) / 2);
	}

	public ValueTask<BitrueServerTime> GetServerTimeAsync(CancellationToken cancellationToken)
		=> SendReadAsync<BitrueServerTime>(_endpoint, "fapi/v1/time",
			new BitrueEmptyQuery(), false, cancellationToken);

	public ValueTask<BitrueFuturesContract[]> GetContractsAsync(
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueFuturesContract[]>(_endpoint, "fapi/v1/contracts",
			new BitrueEmptyQuery(), false, cancellationToken);

	public ValueTask<BitrueFuturesTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueFuturesTicker>(_endpoint, "fapi/v1/ticker",
			new BitrueFuturesMarketQuery { ContractName = symbol }, false,
			cancellationToken);

	public ValueTask<BitrueBook> GetBookAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendReadAsync<BitrueBook>(_endpoint, "fapi/v1/depth",
			new BitrueFuturesMarketQuery { ContractName = symbol, Limit = limit }, false,
			cancellationToken);

	public ValueTask<BitrueFuturesCandle[]> GetCandlesAsync(
		BitrueFuturesCandlesQuery query, CancellationToken cancellationToken)
		=> SendReadAsync<BitrueFuturesCandle[]>(_endpoint, "fapi/v1/klines", query,
			false, cancellationToken);

	public async ValueTask<BitrueFuturesAccountData> GetAccountAsync(
		CancellationToken cancellationToken)
		=> (await SendSignedReadAsync<BitrueFuturesAccountData>("fapi/v2/account",
			new BitrueEmptyQuery(), cancellationToken)).Data;

	public async ValueTask<BitrueFuturesOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendSignedReadAsync<BitrueFuturesOrder[]>("fapi/v2/openOrders",
			new BitrueFuturesOrdersQuery { ContractName = symbol }, cancellationToken)).Data ?? [];

	public async ValueTask<BitrueFuturesOrder[]> GetOrderAsync(BitrueFuturesOrdersQuery query,
		CancellationToken cancellationToken)
	{
		var response = await SendReadAsync<BitrueFuturesOrderArrayResponse>(_endpoint,
			"fapi/v2/order", query, true, cancellationToken);
		EnsureSuccess(response.Code, response.Message);
		return response.Data ?? [];
	}

	public async ValueTask<BitrueFuturesFill[]> GetFillsAsync(BitrueFuturesTradesQuery query,
		CancellationToken cancellationToken)
		=> (await SendSignedReadAsync<BitrueFuturesFill[]>("fapi/v2/myTrades", query,
			cancellationToken)).Data ?? [];

	public async ValueTask<BitrueFuturesOperation> PlaceOrderAsync(
		BitrueFuturesPlaceOrderRequest body, CancellationToken cancellationToken)
		=> (await SendSignedWriteAsync<BitrueFuturesOperation, BitrueFuturesPlaceOrderRequest>(HttpMethod.Post,
			"fapi/v2/order", body, cancellationToken)).Data;

	public async ValueTask<BitrueFuturesOperation> CancelOrderAsync(
		BitrueFuturesCancelOrderRequest body, CancellationToken cancellationToken)
		=> (await SendSignedWriteAsync<BitrueFuturesOperation, BitrueFuturesCancelOrderRequest>(HttpMethod.Post,
			"fapi/v2/cancel", body, cancellationToken)).Data;

	public async ValueTask SetLeverageAsync(BitrueFuturesLeverageRequest body,
		CancellationToken cancellationToken)
		=> _ = await SendSignedWriteAsync<BitrueFuturesOperation, BitrueFuturesLeverageRequest>(HttpMethod.Post,
			"fapi/v2/level_edit", body, cancellationToken);

	public ValueTask<BitrueListenKeyResponse> AcquireListenKeyAsync(
		CancellationToken cancellationToken)
		=> SendStreamWriteAsync(HttpMethod.Post, "user_stream/api/v1/listenKey",
			cancellationToken);

	public ValueTask<BitrueListenKeyResponse> ExtendListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
		=> SendStreamWriteAsync(HttpMethod.Put,
			$"user_stream/api/v1/listenKey/{Uri.EscapeDataString(listenKey.ThrowIfEmpty(nameof(listenKey)))}",
			cancellationToken);

	public ValueTask<BitrueListenKeyResponse> CloseListenKeyAsync(string listenKey,
		CancellationToken cancellationToken)
		=> SendStreamWriteAsync(HttpMethod.Delete,
			$"user_stream/api/v1/listenKey/{Uri.EscapeDataString(listenKey.ThrowIfEmpty(nameof(listenKey)))}",
			cancellationToken);

	private async ValueTask<BitrueFuturesResponse<TData>> SendSignedReadAsync<TData>(
		string path, IBitrueQuery query, CancellationToken cancellationToken)
	{
		var response = await SendReadAsync<BitrueFuturesResponse<TData>>(_endpoint, path,
			query, true, cancellationToken);
		EnsureSuccess(response.Code, response.Message);
		return response;
	}

	private async ValueTask<BitrueFuturesResponse<TData>> SendSignedWriteAsync<TData,
		TRequest>(HttpMethod method, string path, TRequest body,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		var responseBody = await SendWriteCoreAsync(_endpoint, method, path, json,
			cancellationToken);
		var response = Deserialize<BitrueFuturesResponse<TData>>(responseBody);
		EnsureSuccess(response.Code, response.Message);
		return response;
	}

	private async ValueTask<TData> SendReadAsync<TData>(Uri endpoint, string path,
		IBitrueQuery query, bool isSigned, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isSigned)
			EnsureCredentials();
		var queryString = query.ToQueryString();
		var requestPath = "/" + path.TrimStart('/') +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(endpoint, requestPath.TrimStart('/')));
			if (isSigned)
				AddAuthentication(request, requestPath, string.Empty);
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

	private async ValueTask<string> SendWriteCoreAsync(Uri endpoint, HttpMethod method,
		string path, string json, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var requestPath = "/" + path.TrimStart('/');
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(endpoint, requestPath.TrimStart('/')))
		{
			Content = json.IsEmpty() ? null : new StringContent(json, Encoding.UTF8,
				"application/json"),
		};
		AddAuthentication(request, requestPath, json ?? string.Empty);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return body;
	}

	private async ValueTask<BitrueListenKeyResponse> SendStreamWriteAsync(HttpMethod method,
		string path, CancellationToken cancellationToken)
	{
		var body = await SendWriteCoreAsync(_streamEndpoint, method, path, string.Empty,
			cancellationToken);
		var response = Deserialize<BitrueListenKeyResponse>(body);
		if (response.Code != 200)
			throw new InvalidOperationException(
				$"Bitrue futures stream error {response.Code}: {response.Message}".Trim());
		return response;
	}

	private void AddAuthentication(HttpRequestMessage request, string requestPath, string body)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
			_timeOffsetMilliseconds;
		var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
		var prehash = timestampText + request.Method.Method.ToUpperInvariant() + requestPath + body;
		var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
			_secret.UTF8(), prehash.UTF8()));
		request.Headers.TryAddWithoutValidation("X-CH-APIKEY", _apiKey);
		request.Headers.TryAddWithoutValidation("X-CH-SIGN", signature);
		request.Headers.TryAddWithoutValidation("X-CH-TS", timestampText);
	}

	private TData Deserialize<TData>(string body)
	{
		try
		{
			if (body?.TrimStart().StartsWith('{') == true)
			{
				var header = JsonConvert.DeserializeObject<BitrueFuturesResponseHeader>(body,
					_jsonSettings);
				if (header?.Code.IsEmpty() == false && header.Code is not ("0" or "200"))
					throw new InvalidOperationException(
						$"Bitrue futures API error {header.Code}: {header.Message}".Trim());
			}
			var result = JsonConvert.DeserializeObject<TData>(body, _jsonSettings);
			return result is null
				? throw new InvalidDataException("Bitrue futures returned an empty response.")
				: result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Bitrue futures returned malformed JSON.", error);
		}
	}

	private static void EnsureSuccess(string code, string message)
	{
		if (!code.IsEmpty() && code is not ("0" or "200"))
			throw new InvalidOperationException(
				$"Bitrue futures API error {code}: {message}".Trim());
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitrue API key and secret are required for private futures operations.");
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(100);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
		=> new(endpoint.ThrowIfEmpty(parameterName).TrimEnd('/') + "/", UriKind.Absolute);

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
			$"Bitrue futures HTTP {(int)statusCode} ({statusCode}): {text}".Trim(), null,
			statusCode);
	}
}
