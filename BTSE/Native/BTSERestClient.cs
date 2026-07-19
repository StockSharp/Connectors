namespace StockSharp.BTSE.Native;

sealed class BTSERestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly BTSESections _section;
	private readonly string _versionPath;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private long _lastNonce;

	public BTSERestClient(string endpoint, BTSESections section, SecureString key,
		SecureString secret)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_section = section;
		_versionPath = section == BTSESections.Spot ? "api/v3.3" : "api/v2.3";
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException("BTSE API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-BTSE-Connector/1.0");
	}

	public override string Name => nameof(BTSE) + "_" + _section + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _secret is { Length: > 0 };

	public string ApiKey => _apiKey;

	public BTSESections Section => _section;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public string CreateWebSocketSignature(long nonce)
		=> Sign(_section == BTSESections.Spot ? "/ws/spot" : "/ws/futures", nonce,
			string.Empty);

	public long CreateWebSocketNonce() => NextNonce();

	public ValueTask<BTSEMarketSummary[]> GetMarketsAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEMarketSummary[]>($"{_versionPath}/market_summary",
			new BTSEMarketQuery
			{
				Symbol = symbol,
				IsFullAttributes = _section == BTSESections.Futures,
			}, false,
			cancellationToken);

	public ValueTask<BTSEMarketPrice[]> GetPricesAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEMarketPrice[]>($"{_versionPath}/price",
			new BTSEMarketQuery { Symbol = symbol }, false, cancellationToken);

	public ValueTask<BTSEOrderBook> GetDepthAsync(BTSEDepthQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEOrderBook>($"{_versionPath}/orderbook/L2", query, false,
			cancellationToken);

	public ValueTask<BTSEPublicTrade[]> GetTradesAsync(BTSETradesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEPublicTrade[]>($"{_versionPath}/trades", query, false,
			cancellationToken);

	public ValueTask<BTSECandle[]> GetCandlesAsync(BTSECandlesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSECandle[]>($"{_versionPath}/ohlcv", query, false,
			cancellationToken);

	public ValueTask<BTSEOrderResult[]> PlaceOrderAsync(BTSEOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BTSEOrderResult[], BTSEOrderRequest>(HttpMethod.Post,
			$"{_versionPath}/order", request, cancellationToken);

	public ValueTask<BTSEOrderResult[]> AmendOrderAsync(BTSEAmendOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BTSEOrderResult[], BTSEAmendOrderRequest>(HttpMethod.Put,
			$"{_versionPath}/order", request, cancellationToken);

	public ValueTask<BTSEOrderResult[]> CancelOrderAsync(BTSECancelOrderQuery query,
		CancellationToken cancellationToken)
		=> SendDeleteAsync<BTSEOrderResult[]>($"{_versionPath}/order", query,
			cancellationToken);

	public ValueTask<BTSEOrderResult> GetOrderAsync(BTSEOrderLookupQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEOrderResult>($"{_versionPath}/order", query, true,
			cancellationToken);

	public ValueTask<BTSEOrderResult[]> GetOpenOrdersAsync(BTSEOpenOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEOrderResult[]>($"{_versionPath}/user/open_orders", query,
			true, cancellationToken);

	public ValueTask<BTSEPrivateTrade[]> GetTradeHistoryAsync(BTSETradeHistoryQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BTSEPrivateTrade[]>($"{_versionPath}/user/trade_history", query,
			true, cancellationToken);

	public ValueTask<BTSESpotBalance[]> GetSpotWalletAsync(
		CancellationToken cancellationToken)
	{
		if (_section != BTSESections.Spot)
			throw new InvalidOperationException("BTSE spot wallet requires the spot client.");
		return SendGetAsync<BTSESpotBalance[]>("api/v3.2/user/wallet",
			BTSEEmptyQuery.Instance, true, cancellationToken);
	}

	public ValueTask<BTSEFuturesWallet[]> GetFuturesWalletAsync(
		CancellationToken cancellationToken)
	{
		if (_section != BTSESections.Futures)
			throw new InvalidOperationException("BTSE futures wallet requires the futures client.");
		return SendGetAsync<BTSEFuturesWallet[]>($"{_versionPath}/user/wallet",
			BTSEEmptyQuery.Instance, true, cancellationToken);
	}

	public ValueTask<BTSEFuturesPosition[]> GetPositionsAsync(BTSEPositionsQuery query,
		CancellationToken cancellationToken)
	{
		if (_section != BTSESections.Futures)
			throw new InvalidOperationException("BTSE positions require the futures client.");
		return SendGetAsync<BTSEFuturesPosition[]>($"{_versionPath}/user/positions", query,
			true, cancellationToken);
	}

	private async ValueTask<TResponse> SendGetAsync<TResponse>(string path,
		IBTSEQuery query, bool isAuthenticated, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (isAuthenticated)
			EnsureCredentials();
		var target = BuildTarget(path, query);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, target));
			if (isAuthenticated)
				AddAuthentication(request, "/" + path.TrimStart('/'), string.Empty);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return response.StatusCode == HttpStatusCode.NoContent || body.IsEmpty()
					? default
					: Deserialize<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendDeleteAsync<TResponse>(string path,
		IBTSEQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		EnsureCredentials();
		var target = BuildTarget(path, query);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Delete,
			new Uri(_endpoint, target));
		AddAuthentication(request, "/" + path.TrimStart('/'), string.Empty);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, body);
		return response.StatusCode == HttpStatusCode.NoContent || body.IsEmpty()
			? default
			: Deserialize<TResponse>(body);
	}

	private async ValueTask<TResponse> SendBodyAsync<TResponse, TRequest>(HttpMethod method,
		string path, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, "/" + path.TrimStart('/'), json);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		return response.StatusCode == HttpStatusCode.NoContent || responseBody.IsEmpty()
			? default
			: Deserialize<TResponse>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, string path, string body)
	{
		var nonce = NextNonce();
		request.Headers.TryAddWithoutValidation("request-api", _apiKey);
		request.Headers.TryAddWithoutValidation("request-nonce",
			nonce.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("request-sign", Sign(path, nonce, body));
	}

	private string Sign(string path, long nonce, string body)
	{
		EnsureCredentials();
		var payload = path + nonce.ToString(CultureInfo.InvariantCulture) + body;
		using var hmac = new HMACSHA384(_secret);
		return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
			.ToLowerInvariant();
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(ref _lastNonce, next, current) == current)
				return next;
		}
	}

	private static string BuildTarget(string path, IBTSEQuery query)
	{
		var queryString = query.GetParameters()
			.Where(static parameter => !parameter.Name.IsEmpty() && parameter.Value is not null)
			.Select(static parameter => Uri.EscapeDataString(parameter.Name) + "=" +
				Uri.EscapeDataString(parameter.Value))
			.Join("&");
		return path.TrimStart('/') + (queryString.IsEmpty() ? string.Empty : "?" + queryString);
	}

	private TResponse Deserialize<TResponse>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException("BTSE returned an empty response.");
		}
		catch (JsonException error)
		{
			try
			{
				var apiError = JsonConvert.DeserializeObject<BTSEError>(body, _jsonSettings);
				if (apiError is not null && (!apiError.Message.IsEmpty() ||
					!apiError.ShortMessage.IsEmpty() || apiError.ErrorCode is not null ||
					apiError.Code is not null))
					throw new InvalidOperationException(FormatError(apiError), error);
			}
			catch (JsonException)
			{
			}
			throw new InvalidDataException("BTSE returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BTSE API key and secret are required for private operations.");
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(75);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		string details;
		try
		{
			var error = JsonConvert.DeserializeObject<BTSEError>(body, _jsonSettings);
			details = error is null ? body?.Trim() : FormatError(error);
		}
		catch (JsonException)
		{
			details = body?.Trim();
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"BTSE HTTP {(int)statusCode} ({statusCode}): {details}".Trim(), null,
			statusCode);
	}

	private static string FormatError(BTSEError error)
	{
		var code = error.ErrorCode ?? error.Code ?? error.Status;
		var message = error.Message ?? error.ShortMessage;
		return code is null ? message : $"{code}: {message}".Trim();
	}
}
