namespace StockSharp.SSI.Native;

sealed class SSIRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly string _endpoint;
	private readonly string _clientId;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly SecureString _otp;
	private readonly SSISigner _signer;
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private string _tokenType = "Bearer";
	private string _accessToken;
	private string _refreshToken;
	private DateTimeOffset _expiresAt;

	public SSIRestClient(string endpoint, string clientId,
		SecureString apiKey, SecureString apiSecret,
		SecureString privateKey, SecureString otp,
		HttpMessageHandler handler = null)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim()
			.TrimEnd('/');
		_clientId = clientId?.Trim();
		_apiKey = apiKey.UnSecure().ThrowIfEmpty(nameof(apiKey));
		_apiSecret = apiSecret.UnSecure().ThrowIfEmpty(
			nameof(apiSecret));
		_otp = otp;
		if (!privateKey.IsEmpty())
			_signer = new(privateKey);
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromSeconds(60);
	}

	public string TokenType => _tokenType.IsEmpty()
		? "Bearer"
		: _tokenType;

	public string AccessToken => _accessToken;

	public async ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
	{
		await _authSync.WaitAsync(cancellationToken);
		try
		{
			await AuthenticateCoreAsync(cancellationToken);
		}
		finally
		{
			_authSync.Release();
		}
	}

	private async ValueTask AuthenticateCoreAsync(
		CancellationToken cancellationToken)
	{
		var body = new JObject
		{
			["apiKey"] = _apiKey,
			["apiSecret"] = _apiSecret,
		};
		if (!_otp.IsEmpty())
			body["otp"] = _otp.UnSecure();
		var response = await SendCoreAsync(HttpMethod.Post,
			"/api/v3/auth/token", null, body, false, null,
			cancellationToken);
		SetTokens(response);
	}

	public async ValueTask EnsureTokenAsync(
		CancellationToken cancellationToken)
	{
		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (_accessToken.IsEmpty())
			{
				await AuthenticateCoreAsync(cancellationToken);
				return;
			}
			if (_expiresAt - DateTimeOffset.UtcNow >
				TimeSpan.FromSeconds(100))
				return;
			if (_refreshToken.IsEmpty())
			{
				await AuthenticateCoreAsync(cancellationToken);
				return;
			}
			await RefreshCoreAsync(cancellationToken);
		}
		finally
		{
			_authSync.Release();
		}
	}

	private async ValueTask RefreshCoreAsync(
		CancellationToken cancellationToken)
	{
		var response = await SendCoreAsync(HttpMethod.Post,
			"/api/v3/auth/refresh", null, new JObject
			{
				["refreshToken"] = _refreshToken,
			}, false, null, cancellationToken);
		SetTokens(response);
	}

	private async ValueTask RenewTokenAsync(
		CancellationToken cancellationToken)
	{
		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (!_refreshToken.IsEmpty())
			{
				try
				{
					await RefreshCoreAsync(cancellationToken);
					return;
				}
				catch (HttpRequestException error)
					when (error.StatusCode is
						HttpStatusCode.Unauthorized or
						HttpStatusCode.Forbidden)
				{
				}
			}
			await AuthenticateCoreAsync(cancellationToken);
		}
		finally
		{
			_authSync.Release();
		}
	}

	private void SetTokens(JToken response)
	{
		var value = response.UnwrapSSIData() ??
			throw new InvalidDataException(
				"SSI authentication response is not an object.");
		_tokenType = value.Value<string>("tokenType") ??
			(_tokenType.IsEmpty() ? "Bearer" : _tokenType);
		_accessToken = value.Value<string>("accessToken")
			.ThrowIfEmpty("accessToken");
		var refreshToken = value.Value<string>("refreshToken");
		if (!refreshToken.IsEmpty())
			_refreshToken = refreshToken;
		_expiresAt = ParseExpiry(value["expiresAt"]) ??
			DateTimeOffset.UtcNow.AddMinutes(5);
	}

	private static DateTimeOffset? ParseExpiry(JToken value)
	{
		if (value is null)
			return null;
		if (value.Type == JTokenType.Date)
			return value.Value<DateTimeOffset>();
		if (value.Type == JTokenType.Integer)
		{
			var timestamp = value.Value<long>();
			return timestamp > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
				: DateTimeOffset.FromUnixTimeSeconds(timestamp);
		}
		var text = value.Value<string>();
		if (long.TryParse(text, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numeric))
			return numeric > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
				: DateTimeOffset.FromUnixTimeSeconds(numeric);
		return DateTimeOffset.TryParse(text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out var parsed)
				? parsed
				: null;
	}

	public async ValueTask<JObject[]> GetSecuritiesAsync(string symbol,
		string board, CancellationToken cancellationToken)
	{
		var query = new Dictionary<string, string>();
		if (!symbol.IsEmpty())
			query["symbol"] = symbol.Trim().ToUpperInvariant();
		if (!board.IsEmpty())
			query["board"] = board.Trim().ToUpperInvariant();
		return (await SendAsync(HttpMethod.Get,
			"/api/v3/data/securitiesByBoard", query, null, null,
			cancellationToken)).ToSSIObjects("securities");
	}

	public async ValueTask<JObject[]> GetIndexesAsync(string board,
		CancellationToken cancellationToken)
	{
		var query = new Dictionary<string, string>();
		if (!board.IsEmpty())
			query["board"] = board.Trim().ToUpperInvariant();
		return (await SendAsync(HttpMethod.Get,
			"/api/v3/data/indexList", query, null, null,
			cancellationToken)).ToSSIObjects("indexes");
	}

	public async ValueTask<JObject[]> GetSecuritiesSummaryAsync(
		string symbol, DateTime from, DateTime to, int page, int size,
		CancellationToken cancellationToken)
		=> (await SendAsync(HttpMethod.Get,
			"/api/v3/data/securitiesSummary",
			new Dictionary<string, string>
			{
				["symbol"] = symbol.ThrowIfEmpty(nameof(symbol))
					.Trim().ToUpperInvariant(),
				["from"] = FormatDate(from),
				["to"] = FormatDate(to),
				["pageIndex"] = page.ToString(
					CultureInfo.InvariantCulture),
				["pageSize"] = size.ToString(
					CultureInfo.InvariantCulture),
			}, null, null, cancellationToken))
			.ToSSIObjects("summaries");

	public async ValueTask<SSICandle[]> GetCandlesAsync(string symbol,
		TimeSpan timeFrame, DateTime from, DateTime to, int page,
		int size, CancellationToken cancellationToken)
		=> (await SendAsync(HttpMethod.Get, "/api/v3/data/ohlc",
			new Dictionary<string, string>
			{
				["symbol"] = symbol.ThrowIfEmpty(nameof(symbol))
					.Trim().ToUpperInvariant(),
				["from"] = FormatDate(from),
				["to"] = FormatDate(to),
				["timeFrame"] = timeFrame.ToSSIInterval(),
				["pageIndex"] = page.ToString(
					CultureInfo.InvariantCulture),
				["pageSize"] = size.ToString(
					CultureInfo.InvariantCulture),
			}, null, null, cancellationToken)).ToSSICandles();

	private static string FormatDate(DateTime value)
		=> value.ToString("yyyy/MM/dd HH:mm:ss",
			CultureInfo.InvariantCulture);

	public async ValueTask<JObject[]> GetAccountsAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync(HttpMethod.Get, "/api/v3/account/info",
			null, null, null, cancellationToken))
			.ToSSIObjects("accounts");

	public async ValueTask<JObject> GetBalanceAsync(string account,
		CancellationToken cancellationToken)
		=> await GetObjectAsync("/api/v3/trading/accountBalance",
			new Dictionary<string, string>
			{
				["clientId"] = _clientId.ThrowIfEmpty(
					nameof(_clientId)),
				["accountNo"] = account.ThrowIfEmpty(nameof(account)),
			}, cancellationToken);

	public async ValueTask<JObject> GetPositionsAsync(string account,
		CancellationToken cancellationToken)
		=> await GetObjectAsync("/api/v3/trading/position",
			new Dictionary<string, string>
			{
				["clientId"] = _clientId.ThrowIfEmpty(
					nameof(_clientId)),
				["accountNo"] = account.ThrowIfEmpty(nameof(account)),
			}, cancellationToken);

	public async ValueTask<JObject[]> GetOrdersAsync(string account,
		DateTime from, DateTime to, CancellationToken cancellationToken)
		=> (await SendAsync(HttpMethod.Get,
			"/api/v3/trading/orderBook",
			new Dictionary<string, string>
			{
				["accountNo"] = account.ThrowIfEmpty(nameof(account)),
				["from"] = FormatDate(from),
				["to"] = FormatDate(to),
				["pageIndex"] = "1",
				["pageSize"] = "1000",
			}, null, null, cancellationToken))
			.ToSSIObjects("orderList", "orders");

	private async ValueTask<JObject> GetObjectAsync(string path,
		Dictionary<string, string> query,
		CancellationToken cancellationToken)
	{
		var result = await SendAsync(HttpMethod.Get, path, query, null,
			null, cancellationToken);
		return result.UnwrapSSIData() ?? new();
	}

	public ValueTask<JObject> PlaceOrderAsync(JObject body,
		CancellationToken cancellationToken)
		=> SendSignedObjectAsync(HttpMethod.Post,
			"/api/v3/trading/order", body, cancellationToken);

	public ValueTask<JObject> ReplaceOrderAsync(JObject body,
		CancellationToken cancellationToken)
		=> SendSignedObjectAsync(HttpMethod.Put,
			"/api/v3/trading/order", body, cancellationToken);

	public ValueTask<JObject> CancelOrderAsync(JObject body,
		CancellationToken cancellationToken)
		=> SendSignedObjectAsync(HttpMethod.Delete,
			"/api/v3/trading/order", body, cancellationToken);

	private async ValueTask<JObject> SendSignedObjectAsync(
		HttpMethod method, string path, JObject body,
		CancellationToken cancellationToken)
	{
		if (_signer is null)
			throw new InvalidOperationException(
				"SSI RSA private key is required for trading.");
		var json = body.ToString(Formatting.None);
		var result = await SendAsync(method, path, null, body,
			_signer.Sign(json), cancellationToken);
		return result.UnwrapSSIData() ?? new();
	}

	private async ValueTask<JToken> SendAsync(HttpMethod method,
		string path, Dictionary<string, string> query, JToken body,
		string signature, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				await EnsureTokenAsync(cancellationToken);
				return await SendCoreAsync(method, path, query, body,
					true, signature, cancellationToken);
			}
			catch (HttpRequestException error)
				when (attempt == 0 &&
					error.StatusCode is
						HttpStatusCode.Unauthorized or
						HttpStatusCode.Forbidden)
			{
				await RenewTokenAsync(cancellationToken);
			}
		}
	}

	private async ValueTask<JToken> SendCoreAsync(HttpMethod method,
		string path, Dictionary<string, string> query, JToken body,
		bool authorize, string signature,
		CancellationToken cancellationToken)
	{
		var uri = _endpoint + path;
		if (query?.Count > 0)
			uri += "?" + query
				.Where(static pair => pair.Value is not null)
				.Select(static pair =>
					$"{pair.Key.DataEscape()}=" +
					$"{pair.Value.DataEscape()}").Join("&");
		using var request = new HttpRequestMessage(method, uri);
		request.Headers.Accept.Add(new("application/json"));
		request.Headers.UserAgent.ParseAdd("StockSharp.SSI/1.0");
		if (authorize)
			request.Headers.Authorization = new(TokenType,
				_accessToken);
		if (!signature.IsEmpty())
			request.Headers.TryAddWithoutValidation("X-Signature",
				signature);
		if (body is not null)
			request.Content = new StringContent(body.ToString(
				Formatting.None), Encoding.UTF8, "application/json");
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string message = null;
			try
			{
				var error = JObject.Parse(text);
				message = error.Value<string>("msg") ??
					error.Value<string>("message");
			}
			catch (JsonException)
			{
			}
			throw new HttpRequestException(
				$"SSI HTTP {(int)response.StatusCode}: " +
					(message.IsEmpty() ? text : message),
				null, response.StatusCode);
		}
		if (text.IsEmpty())
			return new JObject();
		try
		{
			return JToken.Parse(text);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"SSI returned invalid JSON.", error);
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_signer?.Dispose();
		_authSync.Dispose();
		base.DisposeManaged();
	}
}
