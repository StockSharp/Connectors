namespace StockSharp.Samco.Native;

sealed class SamcoRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly string _endpoint;
	private readonly string _instrumentEndpoint;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private string _sessionToken;

	public SamcoRestClient(string endpoint, string instrumentEndpoint,
		SecureString apiKey, SecureString apiSecret,
		SecureString sessionToken, HttpMessageHandler handler = null)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint))
			.Trim().TrimEnd('/');
		_instrumentEndpoint = instrumentEndpoint
			.ThrowIfEmpty(nameof(instrumentEndpoint)).Trim();
		_apiKey = apiKey.IsEmpty() ? null : apiKey.UnSecure();
		_apiSecret = apiSecret.IsEmpty() ? null : apiSecret.UnSecure();
		_sessionToken = sessionToken.IsEmpty()
			? null
			: sessionToken.UnSecure();
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromSeconds(60);
	}

	public string SessionToken => _sessionToken;

	public string AccountId { get; private set; }

	public async ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
	{
		if (!_sessionToken.IsEmpty())
			return;
		var response = await SendJsonAsync(HttpMethod.Post,
			"/session/token", new JObject
			{
				["apiKey"] = _apiKey.ThrowIfEmpty(nameof(_apiKey)),
				["apiSecret"] =
					_apiSecret.ThrowIfEmpty(nameof(_apiSecret)),
			}, false, cancellationToken);
		_sessionToken = response.Value<string>("sessionToken")
			.ThrowIfEmpty("sessionToken");
		AccountId = response.Value<string>("accountID")
			.IsEmpty(response.Value<string>("accountId"));
	}

	public async ValueTask<SamcoInstrument[]> GetInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get,
			_instrumentEndpoint);
		request.Headers.UserAgent.ParseAdd("StockSharp.Samco/1.0");
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var text = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException(
				$"Samco instrument master returned HTTP " +
					$"{(int)response.StatusCode}.",
				null, response.StatusCode);
		return SamcoExtensions.ParseInstruments(text);
	}

	public ValueTask<JObject> GetQuoteAsync(
		SamcoInstrumentRef instrument,
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get,
			"/quote/getQuote?" + Query(
				("symbolName", instrument.OrderSymbol),
				("exchange", instrument.Exchange)),
			null, true, cancellationToken);

	public ValueTask<JObject> GetDepthAsync(
		SamcoInstrumentRef instrument,
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Post, "/marketDepth",
			new JObject
			{
				["exchange"] = instrument.Exchange,
				["symbolName"] = instrument.OrderSymbol,
			}, true, cancellationToken);

	public ValueTask<JObject> GetCandlesAsync(
		SamcoInstrumentRef instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var daily = timeFrame == TimeSpan.FromDays(1);
		var path = daily
			? "/history/candleData?"
			: "/intraday/candleData?";
		var parameters = new List<(string Name, string Value)>
		{
			("exchange", instrument.Exchange),
			("symbolName", instrument.OrderSymbol),
			("fromDate", from.ToString(
				daily ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss",
				CultureInfo.InvariantCulture)),
			("toDate", to.ToString(
				daily ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss",
				CultureInfo.InvariantCulture)),
		};
		if (!daily)
			parameters.Add(("interval", timeFrame.ToSamcoInterval()));
		return SendJsonAsync(HttpMethod.Get,
			path + Query([.. parameters]), null, true,
			cancellationToken);
	}

	public ValueTask<JObject> GetOrdersAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get, "/order/orderBook",
			null, true, cancellationToken);

	public ValueTask<JObject> GetTradesAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get, "/trade/tradeBook",
			null, true, cancellationToken);

	public ValueTask<JObject> GetPositionsAsync(string type,
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get,
			"/position/getPositions?" +
				Query(("positionType",
					type.ThrowIfEmpty(nameof(type)))),
			null, true, cancellationToken);

	public ValueTask<JObject> GetHoldingsAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get, "/holding/getHoldings",
			null, true, cancellationToken);

	public ValueTask<JObject> GetLimitsAsync(
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Get, "/limit/getLimits",
			null, true, cancellationToken);

	public ValueTask<JObject> PlaceOrderAsync(JObject order,
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Post, "/order/placeOrder",
			order, true, cancellationToken);

	public ValueTask<JObject> ModifyOrderAsync(string orderId,
		JObject changes, CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Put,
			"/order/modifyOrder/" +
				orderId.ThrowIfEmpty(nameof(orderId)).DataEscape(),
			changes, true, cancellationToken);

	public ValueTask<JObject> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendJsonAsync(HttpMethod.Delete,
			"/order/cancelOrder?" +
				Query(("orderNumber",
					orderId.ThrowIfEmpty(nameof(orderId)))),
			null, true, cancellationToken);

	private async ValueTask<JObject> SendJsonAsync(HttpMethod method,
		string path, JToken body, bool authorize,
		CancellationToken cancellationToken)
	{
		if (authorize && _sessionToken.IsEmpty())
			await AuthenticateAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			_endpoint + path);
		request.Headers.Accept.Add(new("application/json"));
		request.Headers.UserAgent.ParseAdd("StockSharp.Samco/1.0");
		if (authorize)
			request.Headers.TryAddWithoutValidation(
				"x-session-token", _sessionToken.ThrowIfEmpty(
					nameof(_sessionToken)));
		if (body is not null)
			request.Content = new StringContent(body.ToString(
				Formatting.None), Encoding.UTF8, "application/json");

		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var text = await response.Content.ReadAsStringAsync(
			cancellationToken);
		JObject value = null;
		if (!text.IsEmpty())
		{
			try
			{
				value = JObject.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Samco returned invalid JSON.", error);
			}
		}
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException(
				ReadError(value).IsEmpty(
					$"Samco returned HTTP " +
						$"{(int)response.StatusCode}."),
				null, response.StatusCode);
		value ??= new();
		Validate(value);
		return value;
	}

	private static void Validate(JObject value)
	{
		var status = value.Value<string>("status");
		if (status.IsEmpty() ||
			status.EqualsIgnoreCase("success") ||
			status.EqualsIgnoreCase("ok"))
			return;
		throw new InvalidOperationException(
			ReadError(value).IsEmpty("Samco request failed."));
	}

	private static string ReadError(JObject value)
		=> value?.Value<string>("statusMessage")
			.IsEmpty(value?.Value<string>("message"))
			.IsEmpty(value?.Value<string>("error"));

	private static string Query(
		params (string Name, string Value)[] parameters)
		=> parameters
			.Where(static pair => !pair.Value.IsEmpty())
			.Select(static pair =>
				$"{Uri.EscapeDataString(pair.Name)}=" +
					Uri.EscapeDataString(pair.Value))
			.Join("&");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
