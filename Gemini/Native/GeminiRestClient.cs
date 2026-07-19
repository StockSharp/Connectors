namespace StockSharp.Gemini.Native;

sealed class GeminiRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly string _account;
	private readonly SemaphoreSlim _publicRateSync = new(1, 1);
	private readonly SemaphoreSlim _privateRateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextPublicRequestTime;
	private DateTime _nextPrivateRequestTime;
	private long _lastNonceMicroseconds;

	public GeminiRestClient(string endpoint, SecureString key, SecureString secret,
		string account)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"Gemini API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);
		_account = account?.Trim();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Gemini-Connector/1.0");
	}

	public override string Name => nameof(Gemini) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _secret is { Length: > 0 };

	public bool IsAccountKey => IsCredentialsAvailable &&
		_apiKey.StartsWith("account-", StringComparison.OrdinalIgnoreCase);

	public string ApiKey => _apiKey;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_publicRateSync.Dispose();
		_privateRateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<string[]> GetSymbolsAsync(CancellationToken cancellationToken)
		=> SendPublicAsync<string[]>("v1/symbols", cancellationToken);

	public ValueTask<GeminiSymbolDetails> GetSymbolDetailsAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendPublicAsync<GeminiSymbolDetails>(
			$"v1/symbols/details/{EscapeSymbol(symbol)}", cancellationToken);

	public ValueTask<GeminiTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendPublicAsync<GeminiTicker>($"v2/ticker/{EscapeSymbol(symbol)}",
			cancellationToken);

	public ValueTask<GeminiOrderBook> GetOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> SendPublicAsync<GeminiOrderBook>(
			$"v1/book/{EscapeSymbol(symbol)}?limit_bids={depth}&limit_asks={depth}",
			cancellationToken);

	public ValueTask<GeminiPublicTrade[]> GetTradesAsync(string symbol, long? timestamp,
		long? sinceTradeId, int limit, CancellationToken cancellationToken)
	{
		var path = $"v1/trades/{EscapeSymbol(symbol)}?limit_trades={limit}";
		if (timestamp is long time)
			path += "&timestamp=" + time.ToString(CultureInfo.InvariantCulture);
		if (sinceTradeId is long tradeId)
			path += "&since_tid=" + tradeId.ToString(CultureInfo.InvariantCulture);
		return SendPublicAsync<GeminiPublicTrade[]>(path, cancellationToken);
	}

	public ValueTask<GeminiCandle[]> GetCandlesAsync(string symbol, string interval,
		bool isDerivative, CancellationToken cancellationToken)
		=> SendPublicAsync<GeminiCandle[]>(
			$"v2/{(isDerivative ? "derivatives/" : string.Empty)}candles/" +
			$"{EscapeSymbol(symbol)}/{Uri.EscapeDataString(interval)}", cancellationToken);

	public ValueTask<GeminiBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendPrivateReadAsync<GeminiBalance[]>("/v1/balances",
			new GeminiBalancesRequest(), cancellationToken);

	public ValueTask<GeminiOrder[]> GetActiveOrdersAsync(
		CancellationToken cancellationToken)
		=> SendPrivateReadAsync<GeminiOrder[]>("/v1/orders",
			new GeminiOrdersRequest(), cancellationToken);

	public ValueTask<GeminiPositionsResponse> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendPrivateReadAsync<GeminiPositionsResponse>("/v1/positions",
			new GeminiPositionsRequest(), cancellationToken);

	public ValueTask<GeminiOrder[]> GetOrderHistoryAsync(string symbol, long? timestamp,
		int limit, CancellationToken cancellationToken)
		=> SendPrivateReadAsync<GeminiOrder[]>("/v1/orders/history",
			new GeminiOrderHistoryRequest
			{
				Symbol = symbol,
				Timestamp = timestamp,
				Limit = limit.Min(500).Max(1),
			}, cancellationToken);

	public ValueTask<GeminiMyTrade[]> GetMyTradesAsync(string symbol, long? timestamp,
		int limit, CancellationToken cancellationToken)
		=> SendPrivateReadAsync<GeminiMyTrade[]>("/v1/mytrades",
			new GeminiTradesHistoryRequest
			{
				Symbol = symbol,
				Timestamp = timestamp,
				Limit = limit.Min(500).Max(1),
			}, cancellationToken);

	public (string Nonce, string Payload, string Signature) CreateWebSocketAuthentication()
	{
		EnsureCredentials();
		var nonce = NextNonce();
		var payload = Convert.ToBase64String(Encoding.ASCII.GetBytes(nonce));
		return (nonce, payload, Sign(payload));
	}

	private async ValueTask<TResponse> SendPublicAsync<TResponse>(string path,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(false, cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path.TrimStart('/')));
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

	private async ValueTask<TResponse> SendPrivateReadAsync<TResponse>(string path,
		GeminiPrivateRequest payload, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(payload);
		EnsureCredentials();
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(true, cancellationToken);
			payload.Request = path;
			payload.Nonce = NextNonce();
			payload.Account = _account;
			var json = JsonConvert.SerializeObject(payload, _jsonSettings);
			var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
			using var request = new HttpRequestMessage(HttpMethod.Post,
				new Uri(_endpoint, path.TrimStart('/')))
			{
				Content = new ByteArrayContent([]),
			};
			request.Content.Headers.ContentType = new("text/plain");
			request.Headers.CacheControl = new() { NoCache = true };
			request.Headers.TryAddWithoutValidation("X-GEMINI-APIKEY", _apiKey);
			request.Headers.TryAddWithoutValidation("X-GEMINI-PAYLOAD", encoded);
			request.Headers.TryAddWithoutValidation("X-GEMINI-SIGNATURE", Sign(encoded));
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

	private string Sign(string payload)
	{
		using var hmac = new HMACSHA384(_secret);
		return Convert.ToHexString(hmac.ComputeHash(Encoding.ASCII.GetBytes(payload)))
			.ToLowerInvariant();
	}

	private string NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonceMicroseconds);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(ref _lastNonceMicroseconds, next, current) != current)
				continue;
			var seconds = Math.DivRem(next, 1_000_000, out var fraction);
			return seconds.ToString(CultureInfo.InvariantCulture) + "." +
				fraction.ToString("D6", CultureInfo.InvariantCulture);
		}
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		try
		{
			if (body.AsSpan().TrimStart().StartsWith("{"))
			{
				var protocolError = JsonConvert.DeserializeObject<GeminiError>(body,
					_jsonSettings);
				if (protocolError is not null &&
					(protocolError.Result.EqualsIgnoreCase("error") ||
					!protocolError.Reason.IsEmpty() || !protocolError.Message.IsEmpty()))
					throw new InvalidOperationException(FormatError(protocolError));
			}
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException("Gemini returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Gemini returned an unexpected response shape.", error);
		}
	}

	private async ValueTask WaitRateLimitAsync(bool isPrivate,
		CancellationToken cancellationToken)
	{
		var sync = isPrivate ? _privateRateSync : _publicRateSync;
		await sync.WaitAsync(cancellationToken);
		try
		{
			var next = isPrivate ? _nextPrivateRequestTime : _nextPublicRequestTime;
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			if (isPrivate)
				_nextPrivateRequestTime = DateTime.UtcNow.AddMilliseconds(200);
			else
				_nextPublicRequestTime = DateTime.UtcNow.AddMilliseconds(550);
		}
		finally
		{
			sync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Gemini API key and secret are required for private operations.");
	}

	private static string EscapeSymbol(string symbol)
		=> Uri.EscapeDataString(symbol.ThrowIfEmpty(nameof(symbol)).ToLowerInvariant());

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(500 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		string details;
		try
		{
			var error = JsonConvert.DeserializeObject<GeminiError>(body, _jsonSettings);
			details = error is null ? body?.Trim() : FormatError(error);
		}
		catch (JsonException)
		{
			details = body?.Trim();
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Gemini HTTP {(int)statusCode} ({statusCode}): {details}".Trim(), null,
			statusCode);
	}

	private static string FormatError(GeminiError error)
		=> new[] { error.Result, error.Reason, error.Message }
			.Where(static value => !value.IsEmpty()).Join(": ");
}
