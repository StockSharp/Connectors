namespace StockSharp.AngelOne.Native;

sealed class AngelOneRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://apiconnect.angelone.in";
	private const string _instrumentUrl = "https://margincalculator.angelone.in/OpenAPI_File/files/OpenAPIScripMaster.json";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		Converters = [new AngelOneCandleArrayConverter()],
	};

	private readonly string _clientCode;
	private readonly SecureString _pin;
	private readonly SecureString _apiKey;
	private readonly SecureString _totpSecret;
	private readonly string _clientLocalIp;
	private readonly string _clientPublicIp;
	private readonly string _macAddress;
	private readonly HttpClient _instrumentClient = new();
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private AngelOneInstrument[] _instruments;
	private string _jwtToken;

	public AngelOneRestClient(string clientCode, SecureString pin, SecureString apiKey, SecureString totpSecret,
		string clientLocalIp, string clientPublicIp, string macAddress)
	{
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		_pin = pin.ThrowIfEmpty(nameof(pin));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_totpSecret = totpSecret.ThrowIfEmpty(nameof(totpSecret));
		_clientLocalIp = clientLocalIp.ThrowIfEmpty(nameof(clientLocalIp));
		_clientPublicIp = clientPublicIp.ThrowIfEmpty(nameof(clientPublicIp));
		_macAddress = macAddress.ThrowIfEmpty(nameof(macAddress));
	}

	public override string Name => nameof(AngelOne) + "_" + nameof(AngelOneRestClient);

	public string ClientCode => _clientCode;
	public string JwtToken => _jwtToken;
	public string FeedToken { get; private set; }

	protected override void DisposeManaged()
	{
		_instrumentClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task Login(CancellationToken cancellationToken)
	{
		var session = await Send<AngelOneSession, AngelOneLoginRequest>("/rest/auth/angelbroking/user/v1/loginByPassword", Method.Post,
			new AngelOneLoginRequest
			{
				ClientCode = _clientCode,
				Password = _pin.UnSecure(),
				Totp = AngelOneTotp.Generate(_totpSecret, DateTime.UtcNow),
			}, false, cancellationToken);

		if (session == null)
			throw new InvalidOperationException("Angel One did not return session tokens.");

		_jwtToken = session.JwtToken.ThrowIfEmpty(nameof(session.JwtToken));
		FeedToken = session.FeedToken.ThrowIfEmpty(nameof(session.FeedToken));
	}

	public async Task Logout(CancellationToken cancellationToken)
	{
		if (_jwtToken.IsEmpty())
			return;

		await Send<AngelOneLogoutResult, AngelOneLogoutRequest>("/rest/secure/angelbroking/user/v1/logout", Method.Post,
			new AngelOneLogoutRequest { ClientCode = _clientCode }, true, cancellationToken);
		_jwtToken = null;
		FeedToken = null;
	}

	public Task<AngelOneProfile> GetProfile(CancellationToken cancellationToken)
		=> Send<AngelOneProfile>("/rest/secure/angelbroking/user/v1/getProfile", Method.Get, true, cancellationToken);

	public Task<AngelOneFunds> GetFunds(CancellationToken cancellationToken)
		=> Send<AngelOneFunds>("/rest/secure/angelbroking/user/v1/getRMS", Method.Get, true, cancellationToken);

	public async Task<AngelOnePosition[]> GetPositions(CancellationToken cancellationToken)
		=> await Send<AngelOnePosition[]>("/rest/secure/angelbroking/order/v1/getPosition", Method.Get, true, cancellationToken) ?? [];

	public async Task<AngelOneHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> (await Send<AngelOneAllHoldings>("/rest/secure/angelbroking/portfolio/v1/getAllHolding", Method.Get, true, cancellationToken))?.Holdings ?? [];

	public async Task<AngelOneOrder[]> GetOrders(CancellationToken cancellationToken)
		=> await Send<AngelOneOrder[]>("/rest/secure/angelbroking/order/v1/getOrderBook", Method.Get, true, cancellationToken) ?? [];

	public async Task<AngelOneTrade[]> GetTrades(CancellationToken cancellationToken)
		=> await Send<AngelOneTrade[]>("/rest/secure/angelbroking/order/v1/getTradeBook", Method.Get, true, cancellationToken) ?? [];

	public async Task<AngelOneInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			await using var stream = await _instrumentClient.GetStreamAsync(_instrumentUrl, cancellationToken);
			using var streamReader = new StreamReader(stream, Encoding.UTF8);
			using var jsonReader = new JsonTextReader(streamReader);
			return _instruments = JsonSerializer.CreateDefault(_jsonSettings).Deserialize<AngelOneInstrument[]>(jsonReader) ?? [];
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<AngelOneCandle[]> GetCandles(string exchange, string token, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var (interval, maxDays) = timeFrame.ToNative();
		var end = ToIndiaTime(to ?? DateTime.UtcNow);
		var start = ToIndiaTime(from ?? end.AddDays(-maxDays));
		if (start > end)
			return [];

		var candles = new List<AngelOneCandle>();
		while (start <= end)
		{
			var pageEnd = start.AddDays(maxDays).AddMinutes(-1);
			if (pageEnd > end)
				pageEnd = end;

			var page = await Send<AngelOneCandle[], AngelOneCandleRequest>("/rest/secure/angelbroking/historical/v1/getCandleData", Method.Post,
				new AngelOneCandleRequest
				{
					Exchange = exchange,
					SymbolToken = token,
					Interval = interval,
					From = start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
					To = pageEnd.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
				}, true, cancellationToken);

			if (page != null)
				candles.AddRange(page);
			start = pageEnd.AddMinutes(1);
		}

		return [.. candles.GroupBy(c => c.Time).Select(g => g.Last()).OrderBy(c => c.Time)];
	}

	public async Task<AngelOneOrderResult> PlaceOrder(AngelOneOrderRequest request, CancellationToken cancellationToken)
		=> await Send<AngelOneOrderResult, AngelOneOrderRequest>("/rest/secure/angelbroking/order/v1/placeOrder", Method.Post, request, true, cancellationToken);

	public async Task<AngelOneOrderResult> ModifyOrder(AngelOneOrderRequest request, CancellationToken cancellationToken)
		=> await Send<AngelOneOrderResult, AngelOneOrderRequest>("/rest/secure/angelbroking/order/v1/modifyOrder", Method.Post, request, true, cancellationToken);

	public async Task<AngelOneOrderResult> CancelOrder(string orderId, AngelOneOrderVarieties variety, CancellationToken cancellationToken)
		=> await Send<AngelOneOrderResult, AngelOneCancelOrderRequest>("/rest/secure/angelbroking/order/v1/cancelOrder", Method.Post,
			new AngelOneCancelOrderRequest { OrderId = orderId, Variety = variety.ToNative() }, true, cancellationToken);

	private Task<T> Send<T>(string path, Method method, bool authorized, CancellationToken cancellationToken)
		=> Send<T, AngelOneNoRequest>(path, method, null, authorized, cancellationToken);

	private async Task<TResponse> Send<TResponse, TRequest>(string path, Method method, TRequest body, bool authorized, CancellationToken cancellationToken)
		where TRequest : class
	{
		var request = new RestRequest((string)null, method);
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("Accept", "application/json");
		request.AddHeader("X-UserType", "USER");
		request.AddHeader("X-SourceID", "WEB");
		request.AddHeader("X-ClientLocalIP", _clientLocalIp);
		request.AddHeader("X-ClientPublicIP", _clientPublicIp);
		request.AddHeader("X-MACAddress", _macAddress);
		request.AddHeader("X-PrivateKey", _apiKey.UnSecure());

		if (authorized)
			request.AddHeader("Authorization", $"Bearer {_jwtToken.ThrowIfEmpty(nameof(_jwtToken))}");
		if (body != null)
			request.AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json);

		var response = await request.InvokeAsync<AngelOneResponse<TResponse>>(new Uri(new Uri(_apiUrl), path), this, this.AddVerboseLog, cancellationToken)
			?? throw new InvalidOperationException("Angel One returned an empty response.");

		if (!response.Status)
			throw new InvalidOperationException($"Angel One error {response.ErrorCode}: {response.Message}");

		return response.Data;
	}

	private static DateTime ToIndiaTime(DateTime value)
	{
		if (value.Kind == DateTimeKind.Unspecified)
			return value;
		return new DateTimeOffset(value.ToUniversalTime()).ToOffset(TimeSpan.FromMinutes(330)).DateTime;
	}
}
