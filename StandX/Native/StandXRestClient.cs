namespace StockSharp.StandX.Native;

sealed class StandXRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly Uri _authEndpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private readonly StandXSigner _signer;
	private DateTime _nextRequestTime;
	private TimeSpan _serverTimeOffset;

	public StandXRestClient(string endpoint, string authEndpoint,
		StandXChains chain, string walletAddress, SecureString privateKey)
	{
		_endpoint = CreateEndpoint(endpoint, nameof(endpoint));
		_authEndpoint = CreateEndpoint(authEndpoint, nameof(authEndpoint));
		if (!privateKey.IsEmpty())
			_signer = new(chain, walletAddress, privateKey, _jsonSettings);
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-StandX-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "STANDX_REST";

	public bool IsSigningAvailable => _signer is not null;

	public bool IsAuthenticated => !AccessToken.IsEmpty();

	public string AccessToken { get; private set; }

	public string WalletAddress => _signer?.Address;

	public DateTime ServerTime => DateTime.UtcNow + _serverTimeOffset;

	public long CurrentTimestamp => ServerTime.ToStandXMilliseconds();

	public StandXRequestSignature SignRequest(string payload, string requestId)
		=> (_signer ?? throw new InvalidOperationException(
			"StandX wallet credentials are required."))
			.SignRequest(payload, requestId, CurrentTimestamp);

	public async ValueTask SynchronizeTimeAsync(
		CancellationToken cancellationToken)
	{
		var before = DateTime.UtcNow;
		var seconds = await GetAsync<long>(_endpoint, "/api/kline/time", false,
			cancellationToken);
		var after = DateTime.UtcNow;
		if (seconds <= 0)
			throw new InvalidDataException(
				"StandX returned an invalid server timestamp.");
		var midpoint = before + TimeSpan.FromTicks((after - before).Ticks / 2);
		_serverTimeOffset = seconds.ToStandXSeconds() - midpoint;
	}

	public async ValueTask AuthenticateAsync(TimeSpan tokenLifetime,
		CancellationToken cancellationToken)
	{
		if (_signer is null)
			return;
		var chain = _signer.Chain.ToWire();
		var prepare = await PostAsync<StandXPrepareSignInRequest,
			StandXPrepareSignInResponse>(_authEndpoint,
			"/v1/offchain/prepare-signin?chain=" + chain, new()
			{
				Address = _signer.Address,
				RequestId = _signer.RequestId,
			}, cancellationToken);
		if (!prepare.IsSuccess || prepare.SignedData.IsEmpty())
			throw new InvalidOperationException(
				"StandX rejected sign-in preparation: " +
				(prepare.Message.IsEmpty() ? "no signed challenge" :
					prepare.Message));

		var keys = await GetAsync<StandXJsonWebKeySet>(_authEndpoint,
			"/v1/offchain/certs", false, cancellationToken);
		var payload = VerifyChallenge(prepare.SignedData, keys);
		_signer.ValidateSignedData(payload);
		var login = await PostAsync<StandXLoginRequest, StandXLoginResponse>(
			_authEndpoint, "/v1/offchain/login?chain=" + chain, new()
			{
				Signature = _signer.SignLogin(payload),
				SignedData = prepare.SignedData,
				ExpiresSeconds = decimal.ToInt32(decimal.Truncate(
					(decimal)tokenLifetime.TotalSeconds)),
			}, cancellationToken);
		if (login.Code is not null and not 0 || login.Token.IsEmpty())
			throw new InvalidOperationException(
				$"StandX login failed{(login.Code is null ? string.Empty :
					$" ({login.Code})")}: " +
				(login.Message.IsEmpty() ? "no access token" : login.Message));
		if (!login.Chain.EqualsIgnoreCase(chain))
			throw new CryptographicException(
				"StandX login response contains an unexpected wallet chain.");
		var addressMatches = _signer.Chain == StandXChains.Bsc
			? AddressUtil.Current.AreAddressesTheSame(login.Address,
				_signer.Address)
			: login.Address.Equals(_signer.Address, StringComparison.Ordinal);
		if (!addressMatches)
			throw new CryptographicException(
				"StandX login response contains a different wallet address.");
		AccessToken = login.Token;
	}

	public ValueTask<StandXSymbolInfo[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
		=> GetAsync<StandXSymbolInfo[]>(_endpoint, "/api/query_symbol_info",
			false, cancellationToken);

	public ValueTask<StandXMarketOverview> GetMarketOverviewAsync(
		CancellationToken cancellationToken)
		=> GetAsync<StandXMarketOverview>(_endpoint,
			"/api/query_market_overview", false, cancellationToken);

	public ValueTask<StandXMarket> GetMarketAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<StandXMarket>(_endpoint, AddQuery(
			"/api/query_symbol_market", "symbol", symbol), false,
			cancellationToken);

	public ValueTask<StandXOrderBook> GetOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<StandXOrderBook>(_endpoint, AddQuery(
			"/api/query_depth_book", "symbol", symbol), false,
			cancellationToken);

	public ValueTask<StandXRecentTrade[]> GetRecentTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<StandXRecentTrade[]>(_endpoint, AddQuery(
			"/api/query_recent_trades", "symbol", symbol), false,
			cancellationToken);

	public ValueTask<StandXCandleSeries> GetCandlesAsync(string symbol,
		DateTime from, DateTime to, TimeSpan timeFrame, int count,
		CancellationToken cancellationToken)
	{
		var path = AddQuery("/api/kline/history", "symbol", symbol);
		path = AddQuery(path, "from", from.ToStandXUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		path = AddQuery(path, "to", to.ToStandXUnixSeconds()
			.ToString(CultureInfo.InvariantCulture));
		path = AddQuery(path, "resolution", timeFrame.ToStandXResolution());
		path = AddQuery(path, "countback", count.ToString(
			CultureInfo.InvariantCulture));
		return GetAsync<StandXCandleSeries>(_endpoint, path, false,
			cancellationToken);
	}

	public ValueTask<StandXBalance> GetBalanceAsync(
		CancellationToken cancellationToken)
		=> GetAsync<StandXBalance>(_endpoint, "/api/query_balance", true,
			cancellationToken);

	public ValueTask<StandXPosition[]> GetPositionsAsync(string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<StandXPosition[]>(_endpoint, symbol.IsEmpty()
			? "/api/query_positions"
			: AddQuery("/api/query_positions", "symbol", symbol), true,
			cancellationToken);

	public ValueTask<StandXPage<StandXOrder>> GetOpenOrdersAsync(string symbol,
		int limit, CancellationToken cancellationToken)
	{
		var path = symbol.IsEmpty()
			? "/api/query_open_orders"
			: AddQuery("/api/query_open_orders", "symbol", symbol);
		path = AddQuery(path, "limit", limit.ToString(
			CultureInfo.InvariantCulture));
		return GetAsync<StandXPage<StandXOrder>>(_endpoint, path, true,
			cancellationToken);
	}

	public ValueTask<StandXPage<StandXOrder>> GetOrdersAsync(string symbol,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var path = "/api/query_orders";
		if (!symbol.IsEmpty())
			path = AddQuery(path, "symbol", symbol);
		if (from is DateTime fromValue)
			path = AddQuery(path, "start", fromValue.ToUniversalTime()
				.ToString("O", CultureInfo.InvariantCulture));
		if (to is DateTime toValue)
			path = AddQuery(path, "end", toValue.ToUniversalTime()
				.ToString("O", CultureInfo.InvariantCulture));
		path = AddQuery(path, "limit", limit.ToString(
			CultureInfo.InvariantCulture));
		return GetAsync<StandXPage<StandXOrder>>(_endpoint, path, true,
			cancellationToken);
	}

	public ValueTask<StandXPage<StandXUserTrade>> GetUserTradesAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var path = "/api/query_trades";
		if (!symbol.IsEmpty())
			path = AddQuery(path, "symbol", symbol);
		if (from is DateTime fromValue)
			path = AddQuery(path, "start", fromValue.ToUniversalTime()
				.ToString("O", CultureInfo.InvariantCulture));
		if (to is DateTime toValue)
			path = AddQuery(path, "end", toValue.ToUniversalTime()
				.ToString("O", CultureInfo.InvariantCulture));
		path = AddQuery(path, "limit", limit.ToString(
			CultureInfo.InvariantCulture));
		return GetAsync<StandXPage<StandXUserTrade>>(_endpoint, path, true,
			cancellationToken);
	}

	private StandXSignedData VerifyChallenge(string token,
		StandXJsonWebKeySet keySet)
	{
		var parts = token.ThrowIfEmpty(nameof(token)).Split('.');
		if (parts.Length != 3)
			throw new CryptographicException(
				"StandX sign-in challenge is not a compact JWT.");
		var header = DeserializeBytes<StandXJwtHeader>(
			parts[0].DecodeBase64Url("JWT header"), "JWT header");
		var payload = DeserializeBytes<StandXSignedData>(
			parts[1].DecodeBase64Url("JWT payload"), "JWT payload");
		if (!header.Algorithm.Equals("ES256", StringComparison.Ordinal) ||
			header.KeyId.IsEmpty())
			throw new CryptographicException(
				"StandX sign-in challenge uses an unsupported JWT algorithm.");
		var key = (keySet?.Keys ?? []).SingleOrDefault(item =>
			item?.KeyId.Equals(header.KeyId, StringComparison.Ordinal) == true);
		if (key is null || !key.KeyType.Equals("EC", StringComparison.Ordinal) ||
			!key.Algorithm.Equals("ES256", StringComparison.Ordinal) ||
			!key.Curve.Equals("P-256", StringComparison.Ordinal))
			throw new CryptographicException(
				"StandX JWT verification key is missing or unsupported.");
		var x = key.X.DecodeBase64Url("JWK x coordinate");
		var y = key.Y.DecodeBase64Url("JWK y coordinate");
		var signature = parts[2].DecodeBase64Url("JWT signature");
		if (x.Length != 32 || y.Length != 32 || signature.Length != 64)
			throw new CryptographicException(
				"StandX JWT key or signature has an invalid ES256 size.");
		using var algorithm = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new() { X = x, Y = y },
		});
		var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
		if (!algorithm.VerifyData(signed, signature, HashAlgorithmName.SHA256,
			DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
			throw new CryptographicException(
				"StandX sign-in challenge signature verification failed.");
		var now = ServerTime.ToStandXUnixSeconds();
		if (payload.ExpiresAt <= now || payload.IssuedAtUnix > now + 60)
			throw new CryptographicException(
				"StandX sign-in challenge is expired or not yet valid.");
		return payload;
	}

	private T DeserializeBytes<T>(byte[] bytes, string operation)
	{
		try
		{
			return Deserialize<T>(Encoding.UTF8.GetString(bytes), operation);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
		}
	}

	private async ValueTask<T> GetAsync<T>(Uri endpoint, string path,
		bool isPrivate, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(endpoint, path.TrimStart('/')));
			if (isPrivate)
				AddAuthorization(request);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<T>(body, path);
			if (attempt < 3 && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body, path);
		}
	}

	private async ValueTask<TResponse> PostAsync<TRequest, TResponse>(
		Uri endpoint, string path, TRequest payload,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(payload);
		var body = JsonConvert.SerializeObject(payload, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await ReadBodyAsync(response.Content,
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response.StatusCode, responseBody, path);
		return Deserialize<TResponse>(responseBody, path);
	}

	private void AddAuthorization(HttpRequestMessage request)
	{
		if (!IsAuthenticated)
			throw new InvalidOperationException(
				"StandX wallet authentication is required.");
		request.Headers.Authorization = new("Bearer", AccessToken);
	}

	private T Deserialize<T>(string body, string operation)
	{
		if (body.IsEmpty())
			throw new InvalidDataException(
				$"StandX returned an empty response for '{operation}'.");
		try
		{
			return JsonConvert.DeserializeObject<T>(body, _jsonSettings) ??
				throw new InvalidDataException(
					$"StandX returned no data for '{operation}'.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"StandX returned malformed JSON for '{operation}'.", error);
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
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
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"StandX endpoint must use HTTP or HTTPS.", parameterName);
		return uri;
	}

	private static string AddQuery(string path, string name, string value)
	{
		name = name.ThrowIfEmpty(nameof(name));
		value = value.ThrowIfEmpty(nameof(value));
		return path + (path.Contains('?', StringComparison.Ordinal) ? '&' : '?') +
			Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromSeconds(Math.Pow(2, attempt));
		await Task.Delay(delay.Min(TimeSpan.FromSeconds(30)), cancellationToken);
	}

	private static Exception CreateException(HttpStatusCode statusCode,
		string body, string operation)
	{
		StandXError error = null;
		try
		{
			if (!body.IsEmpty())
				error = JsonConvert.DeserializeObject<StandXError>(body);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message.IsEmpty() == false
			? error.Message
			: error?.Detail.IsEmpty() == false
				? error.Detail
				: body?.Trim().Truncate(512, string.Empty);
		return new InvalidOperationException(
			$"StandX HTTP {(int)statusCode} ({statusCode}) for '{operation}'" +
			$"{(error?.Code is int code ? $" code {code}" : string.Empty)}: " +
			(detail.IsEmpty() ? "request rejected" : detail));
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"StandX response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"StandX response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		AccessToken = null;
		_signer?.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
