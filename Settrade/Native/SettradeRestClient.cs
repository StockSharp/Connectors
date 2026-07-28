namespace StockSharp.Settrade.Native;

sealed class SettradeRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly string _baseEndpoint;
	private readonly string _marketEndpoint;
	private readonly string _appId;
	private readonly SettradeSigner _signer;
	private readonly string _appCode;
	private readonly string _brokerId;
	private readonly string _parameters;
	private readonly string _userAgent;
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private string _tokenType;
	private string _accessToken;
	private string _refreshToken;
	private DateTime _expiresAt;

	public SettradeRestClient(string baseEndpoint, string marketEndpoint,
		string appId, SecureString secret, string appCode, string brokerId,
		string parameters, HttpMessageHandler handler = null)
	{
		_baseEndpoint = NormalizeEndpoint(baseEndpoint, nameof(baseEndpoint));
		_marketEndpoint = NormalizeEndpoint(marketEndpoint,
			nameof(marketEndpoint));
		_appId = appId.ThrowIfEmpty(nameof(appId)).Trim();
		_signer = new(secret);
		_appCode = appCode.ThrowIfEmpty(nameof(appCode)).Trim();
		_brokerId = brokerId.ThrowIfEmpty(nameof(brokerId)).Trim();
		_parameters = parameters ?? string.Empty;
		_userAgent = "StockSharp.Settrade/1.0";
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromSeconds(30);
	}

	private static string NormalizeEndpoint(string endpoint, string name)
		=> endpoint.ThrowIfEmpty(name).Trim().TrimEnd('/');

	public string BrokerId => _brokerId;
	public string TokenType => _tokenType.IsEmpty() ? "Bearer" :
		_tokenType;

	public async ValueTask LoginAsync(CancellationToken cancellationToken)
	{
		await _authSync.WaitAsync(cancellationToken);
		try
		{
			await LoginCoreAsync(cancellationToken);
		}
		finally
		{
			_authSync.Release();
		}
	}

	private async ValueTask LoginCoreAsync(
		CancellationToken cancellationToken)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var response = await SendCoreAsync(HttpMethod.Post,
			$"{_baseEndpoint}/api/oam/v1/{_brokerId}/broker-apps/" +
				$"{_appCode.DataEscape()}/login",
			new JObject
			{
				["apiKey"] = _appId,
				["params"] = _parameters,
				["signature"] = _signer.Sign(_appId, _parameters,
					timestamp),
				["timestamp"] = timestamp.ToString(
					CultureInfo.InvariantCulture),
			}, false, cancellationToken);
		SetTokens(response);
	}

	public async ValueTask EnsureTokenAsync(
		CancellationToken cancellationToken)
	{
		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (_accessToken.IsEmpty() || _refreshToken.IsEmpty())
			{
				await LoginCoreAsync(cancellationToken);
				return;
			}
			if (_expiresAt - DateTime.UtcNow > TimeSpan.FromSeconds(100))
				return;
			var response = await SendCoreAsync(HttpMethod.Post,
				$"{_baseEndpoint}/api/oam/v1/{_brokerId}/broker-apps/" +
					$"{_appCode.DataEscape()}/refresh-token",
				new JObject
				{
					["apiKey"] = _appId,
					["refreshToken"] = _refreshToken,
				}, true, cancellationToken);
			SetTokens(response);
		}
		finally
		{
			_authSync.Release();
		}
	}

	private void SetTokens(JToken response)
	{
		response = response["data"] ?? response;
		_tokenType = response.Value<string>("token_type") ??
			response.Value<string>("tokenType") ?? "Bearer";
		_accessToken = (response.Value<string>("access_token") ??
			response.Value<string>("accessToken")).ThrowIfEmpty(
				"access_token");
		_refreshToken = response.Value<string>("refresh_token") ??
			response.Value<string>("refreshToken");
		var expires = response.Value<int?>("expires_in") ??
			response.Value<int?>("expiresIn") ?? 300;
		_expiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(
			Math.Max(expires, 1));
	}

	public async ValueTask<JObject> GetQuoteAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendAsync(HttpMethod.Get,
			$"{_marketEndpoint}/api/marketdata/v3/{_brokerId}/quote/" +
				symbol.ThrowIfEmpty(nameof(symbol)).DataEscape(),
			null, cancellationToken)) as JObject ?? new();

	public async ValueTask<JToken> GetCandlesAsync(string symbol,
		string interval, int? limit, DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
	{
		var query = new Dictionary<string, string>
		{
			["symbol"] = symbol.ThrowIfEmpty(nameof(symbol)),
			["interval"] = interval.ThrowIfEmpty(nameof(interval)),
		};
		if (limit > 0)
			query["limit"] = limit.Value.ToString(
				CultureInfo.InvariantCulture);
		if (from is DateTime start)
			query["start"] = start.ToUniversalTime().ToString(
				"yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		if (to is DateTime end)
			query["end"] = end.ToUniversalTime().ToString(
				"yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		return await SendAsync(HttpMethod.Get,
			$"{_marketEndpoint}/api/techchart/v3/{_brokerId}/candlesticks" +
				"?" + query.Select(static pair =>
					$"{pair.Key.DataEscape()}={pair.Value.DataEscape()}")
					.Join("&"),
			null, cancellationToken);
	}

	public async ValueTask<SettradeDispatcher> GetDispatcherAsync(
		CancellationToken cancellationToken)
	{
		var response = await SendAsync(HttpMethod.Get,
			$"{_baseEndpoint}/api/dispatcher/v3/{_brokerId}/token",
			null, cancellationToken);
		response = response["data"] ?? response;
		var hosts = response["hosts"] as JArray;
		return new()
		{
			Host = hosts?.Values<string>().FirstOrDefault()
				.ThrowIfEmpty("dispatcher host"),
			Token = response.Value<string>("token")
				.ThrowIfEmpty("dispatcher token"),
		};
	}

	public ValueTask<JObject> GetAccountInfoAsync(string account,
		SettradeAccountTypes accountType,
		CancellationToken cancellationToken)
		=> SendObjectAsync(HttpMethod.Get,
			AccountPath(account, accountType, "account-info"), null,
			cancellationToken);

	public ValueTask<JObject[]> GetPortfoliosAsync(string account,
		SettradeAccountTypes accountType,
		CancellationToken cancellationToken)
		=> SendArrayAsync(HttpMethod.Get,
			AccountPath(account, accountType, "portfolios"), null,
			cancellationToken);

	public ValueTask<JObject[]> GetOrdersAsync(string account,
		SettradeAccountTypes accountType,
		CancellationToken cancellationToken)
		=> SendArrayAsync(HttpMethod.Get,
			AccountPath(account, accountType, "orders"), null,
			cancellationToken);

	public ValueTask<JObject[]> GetTradesAsync(string account,
		SettradeAccountTypes accountType,
		CancellationToken cancellationToken)
	{
		var version = accountType == SettradeAccountTypes.Equity
			? "v4"
			: "v3";
		return SendArrayAsync(HttpMethod.Get,
			AccountPath(account, accountType, "trades", version), null,
			cancellationToken);
	}

	public ValueTask<JObject> PlaceOrderAsync(string account,
		SettradeAccountTypes accountType, JObject body,
		CancellationToken cancellationToken)
		=> SendObjectAsync(HttpMethod.Post,
			AccountPath(account, accountType, "orders"), body,
			cancellationToken);

	public ValueTask<JObject> ChangeOrderAsync(string account,
		SettradeAccountTypes accountType, string orderNo, JObject body,
		CancellationToken cancellationToken)
		=> SendObjectAsync(new HttpMethod("PATCH"),
			AccountPath(account, accountType,
				$"orders/{orderNo.DataEscape()}/change"), body,
			cancellationToken);

	public ValueTask<JObject> CancelOrderAsync(string account,
		SettradeAccountTypes accountType, string orderNo, SecureString pin,
		CancellationToken cancellationToken)
	{
		var body = new JObject();
		if (!pin.IsEmpty())
			body["pin"] = pin.UnSecure();
		return SendObjectAsync(new HttpMethod("PATCH"),
			AccountPath(account, accountType,
				$"orders/{orderNo.DataEscape()}/cancel"), body,
			cancellationToken);
	}

	private string AccountPath(string account,
		SettradeAccountTypes accountType, string suffix,
		string version = "v3")
	{
		var service = accountType == SettradeAccountTypes.Equity
			? "seos"
			: "seosd";
		return $"{_baseEndpoint}/api/{service}/{version}/{_brokerId}/" +
			$"accounts/{account.ThrowIfEmpty(nameof(account)).DataEscape()}/" +
			suffix;
	}

	private async ValueTask<JObject> SendObjectAsync(HttpMethod method,
		string uri, JToken body, CancellationToken cancellationToken)
	{
		var result = await SendAsync(method, uri, body,
			cancellationToken);
		return result as JObject ?? new();
	}

	private async ValueTask<JObject[]> SendArrayAsync(HttpMethod method,
		string uri, JToken body, CancellationToken cancellationToken)
	{
		var result = await SendAsync(method, uri, body,
			cancellationToken);
		if (result is JArray array)
			return array.OfType<JObject>().ToArray();
		if (result is JObject obj)
		{
			foreach (var name in new[]
				{
					"data", "items", "portfolio", "portfolioList",
					"portfolios", "positionList", "positions",
					"orderList", "orders", "tradeList", "trades",
				})
			{
				if (obj[name] is JArray nested)
					return nested.OfType<JObject>().ToArray();
				if (obj[name] is JObject nestedObject)
				{
					var nestedResult = ExtractObjects(nestedObject);
					if (nestedResult.Length > 0)
						return nestedResult;
				}
			}
		}
		return [];
	}

	private static JObject[] ExtractObjects(JToken result)
	{
		if (result is JArray array)
			return array.OfType<JObject>().ToArray();
		if (result is not JObject obj)
			return [];
		foreach (var name in new[]
			{
				"data", "items", "portfolio", "portfolioList",
				"portfolios", "positionList", "positions",
				"orderList", "orders", "tradeList", "trades",
			})
		{
			var nested = ExtractObjects(obj[name]);
			if (nested.Length > 0)
				return nested;
		}
		return obj.Properties().All(static property =>
			property.Value is JObject)
				? obj.Properties().Select(static property =>
					(JObject)property.Value).ToArray()
				: [];
	}

	private async ValueTask<JToken> SendAsync(HttpMethod method, string uri,
		JToken body, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				await EnsureTokenAsync(cancellationToken);
				return await SendCoreAsync(method, uri, body, true,
					cancellationToken);
			}
			catch (HttpRequestException error)
				when (attempt == 0 &&
					error.StatusCode == HttpStatusCode.Unauthorized)
			{
				await LoginAsync(cancellationToken);
			}
		}
	}

	private async ValueTask<JToken> SendCoreAsync(HttpMethod method,
		string uri, JToken body, bool authorize,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, uri);
		request.Headers.UserAgent.ParseAdd(_userAgent);
		if (authorize)
			request.Headers.Authorization = new(
				_tokenType.IsEmpty() ? "Bearer" : _tokenType,
				_accessToken);
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
				message = JObject.Parse(text).Value<string>("message");
			}
			catch (JsonException)
			{
			}
			throw new HttpRequestException(
				$"Settrade HTTP {(int)response.StatusCode}: " +
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
				"Settrade returned invalid JSON.", error);
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authSync.Dispose();
		base.DisposeManaged();
	}
}
