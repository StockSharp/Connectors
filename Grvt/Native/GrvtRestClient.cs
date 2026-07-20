namespace StockSharp.Grvt.Native;

sealed class GrvtRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _edgeEndpoint;
	private readonly Uri _marketEndpoint;
	private readonly Uri _tradingEndpoint;
	private readonly string _apiKey;
	private readonly string _configuredSubAccountId;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _loginSync = new(1, 1);
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private string _cookie;
	private string _accountId;
	private string _subAccountId;

	public GrvtRestClient(string edgeEndpoint, string marketEndpoint,
		string tradingEndpoint, SecureString apiKey, string subAccountId)
	{
		_edgeEndpoint = CreateEndpoint(edgeEndpoint, nameof(edgeEndpoint));
		_marketEndpoint = CreateEndpoint(marketEndpoint, nameof(marketEndpoint));
		_tradingEndpoint = CreateEndpoint(tradingEndpoint,
			nameof(tradingEndpoint));
		_apiKey = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
		_configuredSubAccountId = subAccountId?.Trim();
		if (!_configuredSubAccountId.IsEmpty() &&
			!ulong.TryParse(_configuredSubAccountId, NumberStyles.None,
				CultureInfo.InvariantCulture, out _))
			throw new ArgumentException(
				"GRVT subaccount ID must be an unsigned 64-bit integer.",
				nameof(subAccountId));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-GRVT-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "GRVT_REST";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty();

	public bool IsAuthenticated => !_cookie.IsEmpty() && !_accountId.IsEmpty();

	public string Cookie => _cookie;

	public string AccountId => _accountId;

	public string SubAccountId => _subAccountId.IsEmpty()
		? _configuredSubAccountId
		: _subAccountId;

	protected override void DisposeManaged()
	{
		_loginSync.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask LoginAsync(CancellationToken cancellationToken)
	{
		EnsureCredentials();
		await _loginSync.WaitAsync(cancellationToken);
		try
		{
			await LoginCoreAsync(cancellationToken);
		}
		finally
		{
			_loginSync.Release();
		}
	}

	public ValueTask<GrvtInstrument[]> GetAllInstrumentsAsync(
		CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtAllInstrumentsRequest,
			GrvtResultResponse<GrvtInstrument[]>>(_marketEndpoint,
			"/full/v1/all_instruments", new() { IsActive = true }, true,
			cancellationToken).AsResult();

	public ValueTask<GrvtInstrument> GetInstrumentAsync(string instrument,
		CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtInstrumentRequest,
			GrvtResultResponse<GrvtInstrument>>(_marketEndpoint,
			"/full/v1/instrument", new() { Instrument = instrument }, true,
			cancellationToken).AsResult();

	public ValueTask<GrvtTicker> GetTickerAsync(string instrument,
		CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtInstrumentRequest,
			GrvtResultResponse<GrvtTicker>>(_marketEndpoint, "/full/v1/ticker",
			new() { Instrument = instrument }, true,
			cancellationToken).AsResult();

	public ValueTask<GrvtOrderBook> GetBookAsync(string instrument, int depth,
		CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtBookRequest,
			GrvtResultResponse<GrvtOrderBook>>(_marketEndpoint, "/full/v1/book",
			new() { Instrument = instrument, Depth = depth }, true,
			cancellationToken).AsResult();

	public ValueTask<GrvtTrade[]> GetRecentTradesAsync(string instrument,
		int limit, CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtRecentTradesRequest,
			GrvtResultResponse<GrvtTrade[]>>(_marketEndpoint, "/full/v1/trade",
			new() { Instrument = instrument, Limit = limit }, true,
			cancellationToken).AsResult();

	public ValueTask<GrvtPageResponse<GrvtTrade>> GetTradeHistoryAsync(
		GrvtHistoryRequest request, CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtHistoryRequest, GrvtPageResponse<GrvtTrade>>(
			_marketEndpoint, "/full/v1/trade_history", request, true,
			cancellationToken);

	public ValueTask<GrvtPageResponse<GrvtCandlestick>> GetCandlesticksAsync(
		GrvtCandlestickRequest request, CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtCandlestickRequest,
			GrvtPageResponse<GrvtCandlestick>>(_marketEndpoint, "/full/v1/kline",
			request, true, cancellationToken);

	public ValueTask<GrvtPageResponse<GrvtFundingRate>> GetFundingRatesAsync(
		GrvtHistoryRequest request, CancellationToken cancellationToken)
		=> SendPublicAsync<GrvtHistoryRequest,
			GrvtPageResponse<GrvtFundingRate>>(_marketEndpoint, "/full/v1/funding",
			request, true, cancellationToken);

	public async ValueTask<DateTime> GetServerTimeAsync(
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_marketEndpoint, "/time"));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				var result = Deserialize<GrvtServerTimeResponse>(body,
					"server time");
				if (!long.TryParse(result.ServerTime, NumberStyles.None,
					CultureInfo.InvariantCulture, out var milliseconds) ||
					milliseconds < 0)
					throw new InvalidDataException(
						"GRVT returned an invalid server time.");
				return DateTime.UnixEpoch.AddMilliseconds(milliseconds);
			}
			if (attempt < 3 && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body);
		}
	}

	public ValueTask<GrvtOrder> CreateOrderAsync(GrvtCreateOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtCreateOrderRequest,
			GrvtResultResponse<GrvtOrder>>("/full/v1/create_order", request,
			false, cancellationToken).AsResult();

	public ValueTask<GrvtAck> CancelOrderAsync(GrvtCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtCancelOrderRequest,
			GrvtResultResponse<GrvtAck>>("/full/v1/cancel_order", request, false,
			cancellationToken).AsResult();

	public ValueTask<GrvtAck> CancelAllOrdersAsync(
		GrvtCancelAllOrdersRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtCancelAllOrdersRequest,
			GrvtResultResponse<GrvtAck>>("/full/v1/cancel_all_orders", request,
			false, cancellationToken).AsResult();

	public ValueTask<GrvtOrder[]> GetOpenOrdersAsync(GrvtOpenOrdersRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtOpenOrdersRequest,
			GrvtResultResponse<GrvtOrder[]>>("/full/v1/open_orders", request,
			true, cancellationToken).AsResult();

	public ValueTask<GrvtPageResponse<GrvtOrder>> GetOrderHistoryAsync(
		GrvtOrderHistoryRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtOrderHistoryRequest,
			GrvtPageResponse<GrvtOrder>>("/full/v1/order_history", request, true,
			cancellationToken);

	public ValueTask<GrvtPageResponse<GrvtFill>> GetFillHistoryAsync(
		GrvtFillHistoryRequest request, CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtFillHistoryRequest,
			GrvtPageResponse<GrvtFill>>("/full/v1/fill_history", request, true,
			cancellationToken);

	public ValueTask<GrvtPosition[]> GetPositionsAsync(GrvtPositionsRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtPositionsRequest,
			GrvtResultResponse<GrvtPosition[]>>("/full/v1/positions", request,
			true, cancellationToken).AsResult();

	public ValueTask<GrvtSubAccount> GetSubAccountSummaryAsync(
		GrvtSubAccountSummaryRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<GrvtSubAccountSummaryRequest,
			GrvtResultResponse<GrvtSubAccount>>("/full/v1/account_summary",
			request, true, cancellationToken).AsResult();

	private async ValueTask LoginCoreAsync(CancellationToken cancellationToken)
	{
		await WaitRateLimitAsync(cancellationToken);
		var body = Serialize(new GrvtApiKeyLoginRequest { ApiKey = _apiKey });
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_edgeEndpoint, "/auth/api_key/login"))
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		request.Headers.TryAddWithoutValidation("Cookie", "rm=true;");
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await ReadBodyAsync(response.Content,
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response.StatusCode, responseBody);
		var login = Deserialize<GrvtLoginResponse>(responseBody, "login");
		if (!login.Status.EqualsIgnoreCase("success"))
			throw new InvalidOperationException(
				$"GRVT login failed: {login.Status} {login.Location}".Trim());

		_cookie = ReadCookie(response);
		_accountId = ReadHeader(response, "X-Grvt-Account-Id");
		if (_cookie.IsEmpty() || _accountId.IsEmpty())
			throw new InvalidDataException(
				"GRVT login response omitted its session cookie or account ID.");
		if (!_configuredSubAccountId.IsEmpty() &&
			!login.SubAccountId.IsEmpty() &&
			!_configuredSubAccountId.Equals(login.SubAccountId,
				StringComparison.Ordinal))
			throw new InvalidOperationException(
				$"GRVT API key belongs to subaccount '{login.SubAccountId}', " +
				$"but '{_configuredSubAccountId}' is configured.");
		_subAccountId = _configuredSubAccountId.IsEmpty()
			? login.SubAccountId
			: _configuredSubAccountId;
	}

	private ValueTask<TResponse> SendPublicAsync<TRequest, TResponse>(
		Uri endpoint, string path, TRequest payload, bool isRead,
		CancellationToken cancellationToken)
		=> SendAsync<TRequest, TResponse>(endpoint, path, payload, isRead,
			false, cancellationToken);

	private async ValueTask<TResponse> SendPrivateAsync<TRequest, TResponse>(
		string path, TRequest payload, bool isRead,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		if (!IsAuthenticated)
			await LoginAsync(cancellationToken);
		for (var authenticationAttempt = 0; ; authenticationAttempt++)
		{
			try
			{
				return await SendAsync<TRequest, TResponse>(_tradingEndpoint,
					path, payload, isRead, true, cancellationToken);
			}
			catch (GrvtAuthenticationException) when (
				authenticationAttempt == 0)
			{
				InvalidateSession();
				await LoginAsync(cancellationToken);
			}
		}
	}

	private async ValueTask<TResponse> SendAsync<TRequest, TResponse>(
		Uri endpoint, string path, TRequest payload, bool isRead, bool isPrivate,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(payload);
		var requestBody = Serialize(payload);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Post,
				new Uri(endpoint, path))
			{
				Content = new StringContent(requestBody, Encoding.UTF8,
					"application/json"),
			};
			if (isPrivate)
			{
				request.Headers.TryAddWithoutValidation("Cookie", _cookie);
				request.Headers.TryAddWithoutValidation("X-Grvt-Account-Id",
					_accountId);
			}
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body, path);
			if (isPrivate && response.StatusCode == HttpStatusCode.Unauthorized)
				throw new GrvtAuthenticationException(
					CreateErrorMessage(response.StatusCode, body));
			if (isRead && attempt < 3 && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body);
		}
	}

	private string Serialize<TRequest>(TRequest request)
	{
		try
		{
			return JsonConvert.SerializeObject(request, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidOperationException(
				"Unable to serialize a GRVT request.", error);
		}
	}

	private TResponse Deserialize<TResponse>(string body, string operation)
	{
		if (body.IsEmpty())
			throw new InvalidDataException(
				$"GRVT returned an empty response for {operation}.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException(
					$"GRVT returned an empty payload for {operation}.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"GRVT returned malformed JSON for {operation}.", error);
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(20);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"A GRVT API key is required for private operations.");
	}

	private void InvalidateSession()
	{
		_cookie = null;
		_accountId = null;
	}

	private static Uri CreateEndpoint(string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim().TrimEnd('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			(!uri.Scheme.Equals(Uri.UriSchemeHttp,
				StringComparison.OrdinalIgnoreCase) &&
			 !uri.Scheme.Equals(Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase)))
			throw new ArgumentException(
				"GRVT endpoint must be an HTTP or HTTPS URI.", name);
		return uri;
	}

	private static string ReadCookie(HttpResponseMessage response)
	{
		if (!response.Headers.TryGetValues("Set-Cookie", out var values))
			return null;
		foreach (var value in values)
		{
			foreach (var part in value.Split(';',
				StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			{
				if (part.StartsWith("gravity=", StringComparison.OrdinalIgnoreCase))
					return part;
			}
		}
		return null;
	}

	private static string ReadHeader(HttpResponseMessage response, string name)
		=> response.Headers.TryGetValues(name, out var values)
			? values.FirstOrDefault()?.Trim()
			: null;

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
		await Task.Delay(delay.Min(TimeSpan.FromSeconds(10)), cancellationToken);
	}

	private static Exception CreateException(HttpStatusCode statusCode,
		string body)
		=> new InvalidOperationException(CreateErrorMessage(statusCode, body));

	private static string CreateErrorMessage(HttpStatusCode statusCode,
		string body)
	{
		GrvtErrorResponse error = null;
		try
		{
			if (!body.IsEmpty())
				error = JsonConvert.DeserializeObject<GrvtErrorResponse>(body);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message.IsEmpty() == false
			? error.Message
			: body?.Trim().Truncate(512, string.Empty);
		return $"GRVT HTTP {(int)statusCode} ({statusCode})" +
			$"{(error?.Code is int code ? $" code {code}" : string.Empty)}: " +
			(detail.IsEmpty() ? "request rejected" : detail);
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"GRVT response exceeds the 16 MiB safety limit.");
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
					"GRVT response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	private sealed class GrvtAuthenticationException(string message)
		: InvalidOperationException(message);
}

static class GrvtResponseExtensions
{
	public static async ValueTask<TResult> AsResult<TResult>(
		this ValueTask<GrvtResultResponse<TResult>> response)
	{
		var value = await response;
		return value is null
			? throw new InvalidDataException("GRVT response has no result wrapper.")
			: value.Result;
	}
}
