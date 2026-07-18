namespace StockSharp.Bullish.Native;

readonly record struct BullishParameter(string Name, string Value);

sealed class BullishRestClient : BaseLogReceiver
{
	private const int _maxReadAttempts = 4;
	private const string _apiPrefix = "/trading-api";
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _publicKey;
	private string _rateLimitToken;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly SemaphoreSlim _loginSync = new(1, 1);
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private long _lastNonce;
	private string _jwt;
	private DateTime _jwtExpiresAt;

	public BullishRestClient(string endpoint, SecureString key, SecureString secret,
		SecureString rateLimitToken)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/'), UriKind.Absolute);
		_publicKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_rateLimitToken = rateLimitToken.IsEmpty() ? null : rateLimitToken.UnSecure();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bullish-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => nameof(Bullish) + "_Rest";

	public bool IsCredentialsAvailable => !_publicKey.IsEmpty() && _hasher is not null;

	public void SetRateLimitToken(string token)
	{
		if (_rateLimitToken.IsEmpty() && !token.IsEmpty())
			_rateLimitToken = token;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_loginSync.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BullishMarket[]> GetMarketsAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BullishMarket[]>($"{_apiPrefix}/v1/markets", [], false,
			cancellationToken);

	public ValueTask<BullishOrderBook> GetOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BullishOrderBook>(
			$"{_apiPrefix}/v1/markets/{EscapePath(symbol)}/orderbook/hybrid", [], false,
			cancellationToken);

	public ValueTask<BullishTrade[]> GetRecentTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BullishTrade[]>(
			$"{_apiPrefix}/v1/markets/{EscapePath(symbol)}/trades", [], false,
			cancellationToken);

	public ValueTask<BullishTrade[]> GetHistoricalTradesAsync(string symbol, DateTime from,
		DateTime to, CancellationToken cancellationToken)
		=> SendGetAsync<BullishTrade[]>(
			$"{_apiPrefix}/v1/history/markets/{EscapePath(symbol)}/trades",
			[
				new("createdAtDatetime[gte]", from.ToWireTime()),
				new("createdAtDatetime[lte]", to.ToWireTime()),
			], false, cancellationToken);

	public ValueTask<BullishTick> GetTickAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BullishTick>(
			$"{_apiPrefix}/v1/markets/{EscapePath(symbol)}/tick", [], false,
			cancellationToken);

	public ValueTask<BullishCandle[]> GetCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
		=> SendGetAsync<BullishCandle[]>(
			$"{_apiPrefix}/v1/markets/{EscapePath(symbol)}/candle",
			[
				new("createdAtDatetime[gte]", from.ToWireTime()),
				new("createdAtDatetime[lte]", to.ToWireTime()),
				new("timeBucket", timeFrame.ToBullishBucket()),
			], false, cancellationToken);

	public ValueTask<BullishTradingAccount[]> GetTradingAccountsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<BullishTradingAccount[]>(
			$"{_apiPrefix}/v1/accounts/trading-accounts", [], true, cancellationToken);

	public ValueTask<BullishAssetAccount[]> GetAssetAccountsAsync(string tradingAccountId,
		CancellationToken cancellationToken)
		=> SendGetAsync<BullishAssetAccount[]>($"{_apiPrefix}/v1/accounts/asset",
			[new("tradingAccountId", tradingAccountId)], true, cancellationToken);

	public ValueTask<BullishDerivativePosition[]> GetDerivativePositionsAsync(
		string tradingAccountId, CancellationToken cancellationToken)
		=> SendGetAsync<BullishDerivativePosition[]>($"{_apiPrefix}/v1/derivatives-positions",
			[new("tradingAccountId", tradingAccountId)], true, cancellationToken);

	public ValueTask<BullishOrder[]> GetOpenOrdersAsync(string tradingAccountId, string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BullishOrder[]>($"{_apiPrefix}/v2/orders",
			[
				new("tradingAccountId", tradingAccountId),
				new("symbol", symbol),
				new("status", "OPEN"),
			], true, cancellationToken);

	public ValueTask<BullishOrder[]> GetOrderHistoryAsync(string tradingAccountId, string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
		=> SendGetAsync<BullishOrder[]>($"{_apiPrefix}/v2/history/orders",
			[
				new("tradingAccountId", tradingAccountId),
				new("symbol", symbol),
				new("createdAtDatetime[gte]", from?.ToWireTime()),
				new("createdAtDatetime[lte]", to?.ToWireTime()),
			], true, cancellationToken);

	public ValueTask<BullishTrade[]> GetFillsAsync(string tradingAccountId, string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var isHistory = from is not null || to is not null;
		return SendGetAsync<BullishTrade[]>(
			$"{_apiPrefix}/v1/{(isHistory ? "history/" : string.Empty)}trades",
			[
				new("tradingAccountId", tradingAccountId),
				new("symbol", symbol),
				new("createdAtDatetime[gte]", from?.ToWireTime()),
				new("createdAtDatetime[lte]", to?.ToWireTime()),
			], true, cancellationToken);
	}

	public ValueTask<BullishCommandResponse> PlaceOrderAsync(BullishCreateOrderRequest request,
		CancellationToken cancellationToken)
		=> SendSignedAsync<BullishCommandResponse, BullishCreateOrderRequest>(
			$"{_apiPrefix}/v2/orders", request, cancellationToken);

	public ValueTask<BullishCommandResponse> AmendOrderAsync(BullishAmendOrderRequest request,
		CancellationToken cancellationToken)
		=> SendSignedAsync<BullishCommandResponse, BullishAmendOrderRequest>(
			$"{_apiPrefix}/v2/command", request, cancellationToken);

	public ValueTask<BullishCommandResponse> CancelOrderAsync(BullishCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> SendSignedAsync<BullishCommandResponse, BullishCancelOrderRequest>(
			$"{_apiPrefix}/v2/command", request, cancellationToken);

	public ValueTask<BullishCommandResponse> CancelAllOrdersAsync(
		BullishCancelAllOrdersRequest request, CancellationToken cancellationToken)
		=> SendSignedAsync<BullishCommandResponse, BullishCancelAllOrdersRequest>(
			$"{_apiPrefix}/v2/command", request, cancellationToken);

	public ValueTask<BullishCommandResponse> CancelAllByMarketAsync(
		BullishCancelAllByMarketRequest request, CancellationToken cancellationToken)
		=> SendSignedAsync<BullishCommandResponse, BullishCancelAllByMarketRequest>(
			$"{_apiPrefix}/v2/command", request, cancellationToken);

	public async ValueTask<string> GetJwtAsync(CancellationToken cancellationToken)
	{
		await EnsureJwtAsync(cancellationToken);
		return _jwt;
	}

	private async ValueTask<TResponse> SendGetAsync<TResponse>(string path,
		BullishParameter[] parameters, bool isPrivate, CancellationToken cancellationToken)
	{
		if (isPrivate)
			await EnsureJwtAsync(cancellationToken);

		var query = BuildQuery(parameters);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
			if (isPrivate)
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
			AddRateLimitToken(request);

			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body);

			if (isPrivate && response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
			{
				InvalidateJwt();
				await EnsureJwtAsync(cancellationToken);
				continue;
			}

			if (attempt + 1 < _maxReadAttempts && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}

			throw CreateException(response.StatusCode, body);
		}
	}

	private async ValueTask<TResponse> SendSignedAsync<TResponse, TRequest>(string path,
		TRequest command, CancellationToken cancellationToken)
		where TRequest : BullishSignedCommand
	{
		ArgumentNullException.ThrowIfNull(command);
		await EnsureJwtAsync(cancellationToken);
		var body = JsonConvert.SerializeObject(command, _jsonSettings);
		var nonce = NextNonce().ToString(CultureInfo.InvariantCulture);
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var signature = SignTradingRequest(timestamp, nonce, "POST", path, body);

		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path, null))
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
		request.Headers.Add("BX-SIGNATURE", signature);
		request.Headers.Add("BX-TIMESTAMP", timestamp);
		request.Headers.Add("BX-NONCE", nonce);
		AddRateLimitToken(request);

		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response.StatusCode, responseBody);
		return Deserialize<TResponse>(responseBody);
	}

	private async ValueTask EnsureJwtAsync(CancellationToken cancellationToken)
	{
		EnsureCredentials();
		if (!_jwt.IsEmpty() && DateTime.UtcNow < _jwtExpiresAt)
			return;

		await _loginSync.WaitAsync(cancellationToken);
		try
		{
			if (!_jwt.IsEmpty() && DateTime.UtcNow < _jwtExpiresAt)
				return;

			const string path = "/trading-api/v1/users/hmac/login";
			var now = DateTimeOffset.UtcNow;
			var nonce = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
			var timestamp = now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
			var signature = SignHmac(timestamp + nonce + "GET" + path);

			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, null));
			request.Headers.Add("BX-PUBLIC-KEY", _publicKey);
			request.Headers.Add("BX-NONCE", nonce);
			request.Headers.Add("BX-SIGNATURE", signature);
			request.Headers.Add("BX-TIMESTAMP", timestamp);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateException(response.StatusCode, body);
			var login = Deserialize<BullishLoginResponse>(body);
			_jwt = login?.Token.ThrowIfEmpty("Bullish JWT token");
			_jwtExpiresAt = DateTime.UtcNow.AddHours(23);
		}
		finally
		{
			_loginSync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bullish HMAC public key and secret are required for private operations.");
	}

	private void InvalidateJwt()
	{
		_jwt = null;
		_jwtExpiresAt = default;
	}

	private long NextNonce()
	{
		using (_signSync.EnterScope())
		{
			var current = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
			_lastNonce = Math.Max(current, _lastNonce + 1);
			return _lastNonce;
		}
	}

	private string SignTradingRequest(string timestamp, string nonce, string method,
		string path, string body)
	{
		var payload = timestamp + nonce + method + path + body;
		var digest = Convert.ToHexString(SHA256.HashData(payload.UTF8())).ToLowerInvariant();
		return SignHmac(digest);
	}

	private string SignHmac(string value)
	{
		using (_signSync.EnterScope())
			return Convert.ToHexString(_hasher.ComputeHash(value.UTF8())).ToLowerInvariant();
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(30);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private void AddRateLimitToken(HttpRequestMessage request)
	{
		if (!_rateLimitToken.IsEmpty())
			request.Headers.Add("BX-RATELIMIT-TOKEN", _rateLimitToken);
	}

	private Uri BuildUri(string path, string query)
	{
		var builder = new UriBuilder(new Uri(_endpoint, path));
		if (!query.IsEmpty())
			builder.Query = query;
		return builder.Uri;
	}

	private static string BuildQuery(BullishParameter[] parameters)
		=> (parameters ?? [])
			.Where(static parameter => !parameter.Name.IsEmpty() && !parameter.Value.IsEmpty())
			.Select(static parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}")
			.Join("&");

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).ToUpperInvariant());

	private TResponse Deserialize<TResponse>(string body)
	{
		if (body.IsEmpty())
			return default;
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Bullish returned malformed JSON.", error);
		}
	}

	private static Exception CreateException(HttpStatusCode statusCode, string body)
	{
		BullishErrorResponse error = null;
		try
		{
			if (!body.IsEmpty())
				error = JsonConvert.DeserializeObject<BullishErrorResponse>(body);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message.IsEmpty() == false ? error.Message : body;
		return new InvalidOperationException(
			$"Bullish HTTP {(int)statusCode} ({statusCode}): {error?.ErrorCodeName} {error?.ErrorCode} {message}".Trim());
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
