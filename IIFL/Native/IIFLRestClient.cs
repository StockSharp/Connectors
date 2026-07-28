namespace StockSharp.IIFL.Native;

sealed class IIFLRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly string _endpoint;
	private readonly string _clientId;
	private readonly string _authCode;
	private readonly string _appSecret;
	private string _accessToken;

	public IIFLRestClient(string endpoint, string clientId,
		string authCode, SecureString appSecret, SecureString accessToken,
		HttpMessageHandler handler = null)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim()
			.TrimEnd('/');
		_clientId = clientId?.Trim();
		_authCode = authCode?.Trim();
		_appSecret = appSecret.IsEmpty()
			? null
			: appSecret.UnSecure();
		_accessToken = accessToken.IsEmpty()
			? null
			: accessToken.UnSecure();
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromSeconds(60);
	}

	public string AccessToken => _accessToken;

	public string UserId
		=> GetUserId(_accessToken).IsEmpty(_clientId);

	public async ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty())
			return;
		var clientId = _clientId.ThrowIfEmpty(nameof(_clientId));
		var authCode = _authCode.ThrowIfEmpty(nameof(_authCode));
		var secret = _appSecret.ThrowIfEmpty(nameof(_appSecret));
		var checksum = Convert.ToHexStringLower(
			SHA256.HashData(Encoding.UTF8.GetBytes(
				clientId + authCode + secret)));
		var response = await SendCoreAsync(HttpMethod.Post,
			"/getusersession", new JObject
			{
				["checkSum"] = checksum,
			}, false, cancellationToken);
		var value = response as JObject;
		_accessToken = value?.Value<string>("userSession") ??
			(value?["result"] as JObject)?.Value<string>("userSession");
		_accessToken.ThrowIfEmpty("userSession");
	}

	public async ValueTask ValidateTokenAsync(string endpoint,
		CancellationToken cancellationToken)
	{
		var token = _accessToken.ThrowIfEmpty(nameof(_accessToken));
		var userId = UserId.ThrowIfEmpty(nameof(UserId));
		var response = await SendAbsoluteAsync(HttpMethod.Post,
			endpoint.ThrowIfEmpty(nameof(endpoint)), new JObject
			{
				["userId"] = userId,
				["token"] = token,
			}, false, cancellationToken);
		var result = response.UnwrapIIFLResult() as JObject;
		var status = result?.Value<string>("status");
		if (!status.EqualsIgnoreCase("Success"))
			throw new InvalidOperationException(
				result?.Value<string>("message")
					.IsEmpty("IIFL session-token validation failed."));
	}

	public async ValueTask<IIFLInstrument[]> GetInstrumentsAsync(
		string exchange, CancellationToken cancellationToken)
	{
		var response = await SendCoreAsync(HttpMethod.Get,
			$"/contractfiles/{exchange.ThrowIfEmpty(nameof(exchange))}.json",
			null, false, cancellationToken);
		return response is JArray values
			? values.ToObject<IIFLInstrument[]>() ?? []
			: [];
	}

	public ValueTask<JToken> GetProfileAsync(
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get, "/profile", null,
			cancellationToken);

	public ValueTask<JToken> GetLimitsAsync(
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get, "/limits", null,
			cancellationToken);

	public ValueTask<JToken> GetHoldingsAsync(
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get, "/holdings", null,
			cancellationToken);

	public ValueTask<JToken> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get, "/positions", null,
			cancellationToken);

	public ValueTask<JToken> GetOrdersAsync(
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get, "/orders", null,
			cancellationToken);

	public ValueTask<JToken> GetOrderHistoryAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get,
			$"/orders/{orderId.ThrowIfEmpty(nameof(orderId)).DataEscape()}",
			null, cancellationToken);

	public ValueTask<JToken> GetTradesAsync(
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Get, "/trades", null,
			cancellationToken);

	public ValueTask<JToken> GetQuotesAsync(
		IEnumerable<IIFLInstrumentRef> instruments,
		CancellationToken cancellationToken)
	{
		var body = new JArray(instruments.Select(static instrument =>
			new JObject
			{
				["exchange"] = instrument.Exchange,
				["instrumentId"] = instrument.InstrumentId,
			}));
		return SendAsync(HttpMethod.Post,
			"/marketdata/marketquotes", body, cancellationToken);
	}

	public ValueTask<JToken> GetDepthAsync(
		IIFLInstrumentRef instrument,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Post, "/marketdata/marketdepth",
			ToRequest(instrument), cancellationToken);

	public ValueTask<JToken> GetOpenInterestAsync(
		IIFLInstrumentRef instrument,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Post, "/marketdata/openinterest",
			ToRequest(instrument), cancellationToken);

	public ValueTask<JToken> GetCandlesAsync(
		IIFLInstrumentRef instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Post, "/marketdata/historicaldata",
			new JObject
			{
				["exchange"] = instrument.Exchange,
				["instrumentId"] = instrument.InstrumentId,
				["interval"] = timeFrame.ToIIFLInterval(),
				["fromDate"] = FormatDate(from),
				["toDate"] = FormatDate(to),
			}, cancellationToken);

	public async ValueTask<JObject> PlaceOrderAsync(JObject order,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync(HttpMethod.Post, "/orders",
			new JArray(order), cancellationToken);
		return GetOperationResult(response);
	}

	public async ValueTask<JObject> ModifyOrderAsync(string orderId,
		JObject changes, CancellationToken cancellationToken)
	{
		var response = await SendAsync(HttpMethod.Put,
			$"/orders/{orderId.ThrowIfEmpty(nameof(orderId)).DataEscape()}",
			changes, cancellationToken);
		return GetOperationResult(response);
	}

	public async ValueTask<JObject> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync(HttpMethod.Delete,
			$"/orders/{orderId.ThrowIfEmpty(nameof(orderId)).DataEscape()}",
			null, cancellationToken);
		return GetOperationResult(response);
	}

	private static JObject ToRequest(IIFLInstrumentRef instrument)
		=> new()
		{
			["exchange"] = instrument.Exchange,
			["instrumentId"] = instrument.InstrumentId,
		};

	private static string FormatDate(DateTime value)
		=> value.ToString("dd-MMM-yyyy",
			CultureInfo.InvariantCulture);

	private static JObject GetOperationResult(JToken response)
	{
		var result = response.UnwrapIIFLResult();
		var value = result is JArray array
			? array.OfType<JObject>().FirstOrDefault()
			: result as JObject;
		if (value is null)
			throw new InvalidDataException(
				"IIFL order response contains no result.");
		var status = value.Value<string>("status");
		if (!status.IsEmpty() &&
			!status.EqualsIgnoreCase("Success") &&
			!status.EqualsIgnoreCase("Ok"))
			throw new InvalidOperationException(
				value.Value<string>("message")
					.IsEmpty($"IIFL order request failed ({status})."));
		return value;
	}

	private async ValueTask<JToken> SendAsync(HttpMethod method,
		string path, JToken body,
		CancellationToken cancellationToken)
	{
		if (_accessToken.IsEmpty())
			await AuthenticateAsync(cancellationToken);
		return await SendCoreAsync(method, path, body, true,
			cancellationToken);
	}

	private ValueTask<JToken> SendCoreAsync(HttpMethod method,
		string path, JToken body, bool authorize,
		CancellationToken cancellationToken)
		=> SendAbsoluteAsync(method, _endpoint + path, body, authorize,
			cancellationToken);

	private async ValueTask<JToken> SendAbsoluteAsync(HttpMethod method,
		string url, JToken body, bool authorize,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, url);
		request.Headers.Accept.Add(new(
			"application/json"));
		request.Headers.UserAgent.ParseAdd("StockSharp.IIFL/1.0");
		if (authorize)
			request.Headers.Authorization = new("Bearer",
				_accessToken.ThrowIfEmpty(nameof(_accessToken)));
		if (body is not null)
			request.Content = new StringContent(body.ToString(
				Formatting.None), Encoding.UTF8, "application/json");
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var message = TryReadError(text);
			throw new HttpRequestException(
				message.IsEmpty(
					$"IIFL returned HTTP {(int)response.StatusCode}."),
				null, response.StatusCode);
		}
		if (text.IsEmpty())
			return new JObject();
		JToken value;
		try
		{
			value = JToken.Parse(text);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"IIFL returned invalid JSON.", error);
		}
		if (value is JObject obj)
		{
			var status = obj.Value<string>("status");
			if (!status.IsEmpty() &&
				!status.EqualsIgnoreCase("Ok") &&
				!status.EqualsIgnoreCase("Success"))
				throw new InvalidOperationException(
					obj.Value<string>("message")
						.IsEmpty($"IIFL request failed ({status})."));
		}
		return value;
	}

	private static string TryReadError(string text)
	{
		try
		{
			var value = JObject.Parse(text);
			return value.Value<string>("message") ??
				value.Value<string>("error");
		}
		catch (JsonException)
		{
			return text;
		}
	}

	internal static string GetUserId(string token)
	{
		if (token.IsEmpty())
			return null;
		try
		{
			var parts = token.Split('.');
			if (parts.Length < 2)
				return null;
			var encoded = parts[1]
				.Replace('-', '+')
				.Replace('_', '/');
			encoded = encoded.PadRight(
				encoded.Length + (4 - encoded.Length % 4) % 4, '=');
			var payload = JObject.Parse(Encoding.UTF8.GetString(
				Convert.FromBase64String(encoded)));
			return payload.Value<string>("preferred_username") ??
				payload.Value<string>("clientId") ??
				payload.Value<string>("sub");
		}
		catch (Exception error)
			when (error is FormatException or JsonException)
		{
			return null;
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
