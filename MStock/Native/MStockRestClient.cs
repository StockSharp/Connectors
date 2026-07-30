namespace StockSharp.MStock.Native;

sealed class MStockRestClient : BaseLogReceiver
{
	private sealed record Response(string Content, string ContentType);

	private readonly HttpClient _http;
	private readonly string _endpoint;
	private readonly string _apiKey;
	private readonly string _clientCode;
	private readonly string _password;
	private readonly string _otp;
	private readonly bool _useTotp;
	private string _refreshToken;
	private string _accessToken;
	private string _feedToken;

	public MStockRestClient(string endpoint, SecureString apiKey,
		string clientCode, SecureString password, SecureString otp,
		bool useTotp, SecureString refreshToken,
		SecureString accessToken, HttpMessageHandler handler = null)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint))
			.Trim().TrimEnd('/');
		_apiKey = apiKey.IsEmpty() ? null : apiKey.UnSecure();
		_clientCode = clientCode?.Trim();
		_password = password.IsEmpty() ? null : password.UnSecure();
		_otp = otp.IsEmpty() ? null : otp.UnSecure();
		_useTotp = useTotp;
		_refreshToken = refreshToken.IsEmpty()
			? null
			: refreshToken.UnSecure();
		_accessToken = accessToken.IsEmpty()
			? null
			: accessToken.UnSecure();
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromSeconds(60);
	}

	public string AccessToken => _accessToken;

	public string RefreshToken => _refreshToken;

	public string FeedToken => _feedToken;

	public string ClientCode => _clientCode;

	public async ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
	{
		_apiKey.ThrowIfEmpty(nameof(_apiKey));
		if (!_accessToken.IsEmpty())
			return;

		var requestToken = _refreshToken;
		if (requestToken.IsEmpty())
		{
			var login = await SendJsonAsync(HttpMethod.Post,
				"/openapi/typeb/connect/login", new JObject
				{
					["clientcode"] = _clientCode.ThrowIfEmpty(
						nameof(_clientCode)),
					["password"] = _password.ThrowIfEmpty(
						nameof(_password)),
					["totp"] = string.Empty,
					["state"] = string.Empty,
				}, false, cancellationToken);
			var data = RequireData(login);
			_refreshToken = data.Value<string>("refreshToken")
				.IsEmpty(data.Value<string>("jwtToken"));
			requestToken = _refreshToken.ThrowIfEmpty(
				"refreshToken");
			if (_otp.IsEmpty() && LooksLikeJwt(requestToken))
			{
				SetTokens(data);
				return;
			}
		}

		var otp = _otp.ThrowIfEmpty(nameof(_otp));
		var path = _useTotp
			? "/openapi/typeb/session/verifytotp"
			: "/openapi/typeb/session/token";
		var session = await SendJsonAsync(HttpMethod.Post, path,
			new JObject
			{
				["refreshToken"] = requestToken,
				[_useTotp ? "totp" : "otp"] = otp,
			}, false, cancellationToken);
		SetTokens(RequireData(session));
		_accessToken.ThrowIfEmpty("jwtToken");
	}

	public async ValueTask<MStockInstrument[]> GetInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		var response = await SendCoreAsync(HttpMethod.Get,
			"/openapi/typeb/instruments/OpenAPIScripMaster", null,
			true, cancellationToken);
		var text = response.Content;
		if (text.IsEmpty())
			return [];
		try
		{
			var value = JToken.Parse(text);
			value = value.UnwrapMStockData();
			return value is JArray array
				? array.ToObject<MStockInstrument[]>() ?? []
				: [];
		}
		catch (JsonException)
		{
			return ParseInstrumentCsv(text);
		}
	}

	public ValueTask<JToken> GetQuotesAsync(
		IEnumerable<MStockInstrumentRef> instruments,
		CancellationToken cancellationToken)
	{
		var exchangeTokens = new JObject();

		foreach (var group in instruments.GroupBy(
			static value => value.Exchange))
			exchangeTokens[group.Key] = new JArray(
				group.Select(static value => value.Token));
		return SendJsonAsync(HttpMethod.Post,
			"/openapi/typeb/instruments/quote", new JObject
			{
				["mode"] = "FULL",
				["exchangeTokens"] = exchangeTokens,
			}, true, cancellationToken);
	}

	public ValueTask<JToken> GetCandlesAsync(
		MStockInstrumentRef instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Post,
			"/openapi/typeb/instruments/historical", new JObject
			{
				["exchange"] = instrument.Exchange,
				["symboltoken"] = instrument.Token,
				["interval"] = timeFrame.ToMStockInterval(),
				["fromdate"] = FormatDate(from),
				["todate"] = FormatDate(to),
			}, true, cancellationToken);

	public ValueTask<JToken> GetOrdersAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get,
			"/openapi/typeb/orders", null, true, cancellationToken);

	public ValueTask<JToken> GetTradesAsync(DateTime? from,
		DateTime? to, CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Post,
			"/openapi/typeb/trades", new JObject
			{
				["fromdate"] = FormatDate(
					from ?? DateTime.Today),
				["todate"] = FormatDate(
					to ?? DateTime.Today.AddDays(1)),
			}, true, cancellationToken);

	public ValueTask<JToken> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get,
			"/openapi/typeb/portfolio/positions", null, true,
			cancellationToken);

	public ValueTask<JToken> GetHoldingsAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get,
			"/openapi/typeb/portfolio/holdings", null, true,
			cancellationToken);

	public ValueTask<JToken> GetFundsAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get,
			"/openapi/typeb/user/fundsummary", null, true,
			cancellationToken);

	public async ValueTask<JObject> PlaceOrderAsync(JObject order,
		CancellationToken cancellationToken)
		=> RequireData(await SendJsonAsync(HttpMethod.Post,
			"/openapi/typeb/orders/regular", order, true,
			cancellationToken));

	public async ValueTask<JObject> ModifyOrderAsync(string orderId,
		JObject changes, CancellationToken cancellationToken)
		=> RequireData(await SendJsonAsync(HttpMethod.Put,
			$"/openapi/typeb/orders/regular/" +
				orderId.ThrowIfEmpty(nameof(orderId)).DataEscape(),
			changes, true, cancellationToken));

	public ValueTask<JToken> CancelOrderAsync(string orderId,
		string variety, CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Delete,
			$"/openapi/typeb/orders/regular/" +
				orderId.ThrowIfEmpty(nameof(orderId)).DataEscape(),
			new JObject
			{
				["variety"] = variety.IsEmpty("NORMAL"),
				["orderid"] = orderId,
			}, true, cancellationToken);

	private async ValueTask<JToken> SendJsonAsync(HttpMethod method,
		string path, JToken body, bool authorize,
		CancellationToken cancellationToken)
	{
		var response = await SendCoreAsync(method, path, body,
			authorize, cancellationToken);
		if (response.Content.IsEmpty())
			return new JObject();
		JToken value;
		try
		{
			value = JToken.Parse(response.Content);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"m.Stock returned invalid JSON.", error);
		}
		ValidateResponse(value);
		return value;
	}

	private async ValueTask<Response> SendCoreAsync(HttpMethod method,
		string path, JToken body, bool authorize,
		CancellationToken cancellationToken)
	{
		if (authorize && _accessToken.IsEmpty())
			await AuthenticateAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			_endpoint + path);
		request.Headers.Accept.Add(new(
			"application/json"));
		request.Headers.UserAgent.ParseAdd("StockSharp.MStock/1.0");
		request.Headers.TryAddWithoutValidation(
			"X-Mirae-Version", "1");
		if (!_apiKey.IsEmpty())
			request.Headers.TryAddWithoutValidation(
				"X-PrivateKey", _apiKey);
		if (authorize)
			request.Headers.Authorization = new("Bearer",
				_accessToken.ThrowIfEmpty(nameof(_accessToken)));
		if (body is not null)
			request.Content = new StringContent(body.ToString(
				Formatting.None), Encoding.UTF8, "application/json");

		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var text = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException(
				TryReadError(text).IsEmpty(
					$"m.Stock returned HTTP " +
						$"{(int)response.StatusCode}."),
				null, response.StatusCode);
		return new(text,
			response.Content.Headers.ContentType?.MediaType);
	}

	private void SetTokens(JObject data)
	{
		_accessToken = data.Value<string>("jwtToken")
			.IsEmpty(data.Value<string>("accessToken"));
		_refreshToken = data.Value<string>("refreshToken")
			.IsEmpty(_refreshToken);
		_feedToken = data.Value<string>("feedToken");
	}

	private static JObject RequireData(JToken value)
	{
		if (value is JArray source)
		{
			var first = source.FirstOrDefault();
			if (first is null)
				throw new InvalidDataException(
					"m.Stock response contains no result.");
			return RequireData(first);
		}
		ValidateResponse(value);
		var data = value.UnwrapMStockData();
		if (data is JArray array)
		{
			var first = array.FirstOrDefault();
			if (first is null)
				throw new InvalidDataException(
					"m.Stock response contains no result.");
			return RequireData(first);
		}
		return data as JObject ??
			throw new InvalidDataException(
				"m.Stock response contains no result.");
	}

	private static void ValidateResponse(JToken value)
	{
		if (value is not JObject obj)
			return;
		var status = obj.Value<string>("status");
		if (status.IsEmpty() &&
			obj["status"]?.Type == JTokenType.Boolean)
		{
			if (obj.Value<bool>("status"))
				return;
		}
		else if (status.EqualsIgnoreCase("true") ||
			status.EqualsIgnoreCase("success") ||
			status.EqualsIgnoreCase("ok"))
			return;
		if (!status.IsEmpty() || obj["status"] is not null)
			throw new InvalidOperationException(
				obj.Value<string>("message")
					.IsEmpty("m.Stock request failed."));
	}

	private static MStockInstrument[] ParseInstrumentCsv(string text)
	{
		var lines = text.Split(
			['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length < 2)
			return [];
		var headers = ParseCsvLine(lines[0])
			.Select(static value => value.Trim().ToLowerInvariant())
			.ToArray();
		var result = new List<MStockInstrument>(lines.Length - 1);
		foreach (var line in lines.Skip(1))
		{
			var fields = ParseCsvLine(line);
			string Field(string name)
			{
				var index = Array.IndexOf(headers, name);
				return index >= 0 && index < fields.Length
					? fields[index]
					: null;
			}
			var instrument = new MStockInstrument
			{
				Token = Field("token"),
				Symbol = Field("symbol"),
				TradingSymbol = Field("name")
					.IsEmpty(Field("tradingsymbol")),
				Expiry = Field("expiry"),
				Strike = Field("strike"),
				LotSize = Field("lotsize"),
				InstrumentType = Field("instrumenttype"),
				Exchange = Field("exch_seg")
					.IsEmpty(Field("exchange")),
				TickSize = Field("tick_size"),
			};
			if (!instrument.Token.IsEmpty() &&
				!instrument.Exchange.IsEmpty())
				result.Add(instrument);
		}
		return [.. result];
	}

	private static string[] ParseCsvLine(string line)
	{
		var result = new List<string>();
		var value = new StringBuilder();
		var quoted = false;
		for (var index = 0; index < line.Length; index++)
		{
			var character = line[index];
			if (character == '"')
			{
				if (quoted && index + 1 < line.Length &&
					line[index + 1] == '"')
				{
					value.Append('"');
					index++;
				}
				else
					quoted = !quoted;
			}
			else if (character == ',' && !quoted)
			{
				result.Add(value.ToString());
				value.Clear();
			}
			else
				value.Append(character);
		}
		result.Add(value.ToString());
		return [.. result];
	}

	private static string FormatDate(DateTime value)
		=> value.ToString("dd-MM-yyyy",
			CultureInfo.InvariantCulture);

	private static bool LooksLikeJwt(string value)
		=> value?.Count(character => character == '.') >= 2;

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

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
