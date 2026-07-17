namespace StockSharp.MotilalOswal.Native;

sealed class MotilalOswalRestClient : BaseLogReceiver
{
	private const string _liveUrl = "https://openapi.motilaloswal.com/";
	private const string _demoUrl = "https://openapi.motilaloswaluat.com/";

	private static readonly string[] _supportedExchanges = ["NSE", "BSE", "NSEFO", "NSECD", "MCX", "NCDEX", "BSEFO", "BSECD"];
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _baseUri;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly string _authToken;
	private readonly string _accessToken;
	private readonly string _clientCode;
	private readonly string _localIp;
	private readonly string _publicIp;
	private readonly string _macAddress;
	private readonly string _vendorInfo;
	private readonly string _installedAppId;
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);

	private MotilalOswalProfile _profile;
	private MotilalOswalInstrument[] _instruments;
	private IReadOnlyDictionary<string, MotilalOswalInstrument> _instrumentsByKey;

	public MotilalOswalRestClient(bool isDemo, SecureString apiKey, SecureString apiSecret,
		SecureString authToken, SecureString accessToken, string clientCode,
		string localIp, string publicIp, string macAddress, string vendorInfo, string installedAppId)
	{
		_baseUri = new(isDemo ? _demoUrl : _liveUrl);
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
		_apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret)).UnSecure();
		_authToken = authToken.ThrowIfEmpty(nameof(authToken)).UnSecure();
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken)).UnSecure();
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		_localIp = ValidateIpv4(localIp, nameof(localIp));
		_publicIp = ValidateIpv4(publicIp, nameof(publicIp));
		_macAddress = ValidateMacAddress(macAddress);
		_vendorInfo = vendorInfo.IsEmpty() ? _clientCode : vendorInfo;
		_installedAppId = installedAppId.ThrowIfEmpty(nameof(installedAppId));
	}

	public override string Name => nameof(MotilalOswal) + "_" + nameof(MotilalOswalRestClient);

	protected override void DisposeManaged()
	{
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<MotilalOswalProfile> GetProfile(CancellationToken cancellationToken)
	{
		if (_profile != null)
			return _profile;

		_profile = await Send<MotilalOswalProfile, MotilalOswalClientRequest>(
			"rest/login/v5/getprofile", new() { ClientCode = _clientCode }, cancellationToken);
		return _profile;
	}

	public async Task<int> GetBroadcastLimit(CancellationToken cancellationToken)
	{
		try
		{
			var limit = await Send<MotilalOswalBroadcastLimit, MotilalOswalClientRequest>(
				"rest/report/v3/getbroadcastmaxlimit", new() { ClientCode = _clientCode }, cancellationToken);
			return limit?.Maximum > 0 ? limit.Maximum : 200;
		}
		catch (InvalidOperationException ex)
		{
			this.AddWarningLog("Unable to read the MO broadcast limit; using the documented fallback of 200. {0}", ex.Message);
			return 200;
		}
	}

	public async Task<MotilalOswalInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var profile = await GetProfile(cancellationToken);
			var allowed = profile.Exchanges?.Where(e => !e.IsEmpty()).Select(e => e.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var exchanges = allowed?.Count > 0
				? _supportedExchanges.Where(allowed.Contains).ToArray()
				: _supportedExchanges;

			var instruments = new List<MotilalOswalInstrument>();
			foreach (var exchange in exchanges)
			{
				var rows = await Send<MotilalOswalInstrument[], MotilalOswalExchangeRequest>(
					"rest/report/v3/getscripsbyexchangename",
					new() { ClientCode = _clientCode, ExchangeName = exchange }, cancellationToken, true);

				foreach (var instrument in rows ?? [])
				{
					if (instrument == null || instrument.ScripCode <= 0)
						continue;
					instrument.ExchangeName = instrument.ExchangeName.IsEmpty() ? exchange : instrument.ExchangeName.ToUpperInvariant();
					try
					{
						instrument.ExchangeName.ToBoardCode();
					}
					catch (ArgumentOutOfRangeException)
					{
						continue;
					}
					instruments.Add(instrument);
				}
			}

			foreach (var exchange in new[] { "NSE", "BSE" }.Where(e => allowed?.Count is not > 0 || allowed.Contains(e)))
			{
				var indexes = await Send<MotilalOswalIndex[], MotilalOswalExchangeRequest>(
					"rest/report/v3/getindexdatabyexchangename",
					new() { ClientCode = _clientCode, ExchangeName = exchange }, cancellationToken, true);
				foreach (var index in indexes ?? [])
				{
					if (index == null || index.IndexCode <= 0)
						continue;
					var indexExchange = index.ExchangeName.IsEmpty() ? index.Exchange.IsEmpty(exchange) : index.ExchangeName;
					instruments.Add(new()
					{
						ExchangeName = indexExchange.ToUpperInvariant(),
						ScripCode = index.IndexCode,
						Name = index.IndexName,
						ShortName = index.IndexName,
						FullName = index.IndexName,
						MarketLot = 1,
						IsIndex = true,
					});
				}
			}

			_instruments = [.. instruments
				.GroupBy(i => i.ExchangeName.ToInstrumentKey(i.ScripCode), StringComparer.OrdinalIgnoreCase)
				.Select(g => g.OrderByDescending(i => i.IsIndex).First())];
			_instrumentsByKey = _instruments.ToDictionary(
				i => i.ExchangeName.ToInstrumentKey(i.ScripCode), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<MotilalOswalInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public Task<MotilalOswalOrder[]> GetOrders(CancellationToken cancellationToken)
		=> Send<MotilalOswalOrder[], MotilalOswalClientRequest>(
			"rest/book/v5/getorderbook", new() { ClientCode = _clientCode }, cancellationToken, true);

	public Task<MotilalOswalTrade[]> GetTrades(CancellationToken cancellationToken)
		=> Send<MotilalOswalTrade[], MotilalOswalClientRequest>(
			"rest/book/v4/gettradebook", new() { ClientCode = _clientCode }, cancellationToken, true);

	public Task<MotilalOswalPosition[]> GetPositions(CancellationToken cancellationToken)
		=> Send<MotilalOswalPosition[], MotilalOswalClientRequest>(
			"rest/book/v4/getposition", new() { ClientCode = _clientCode }, cancellationToken, true);

	public Task<MotilalOswalHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> Send<MotilalOswalHolding[], MotilalOswalClientRequest>(
			"rest/report/v3/getdpholding", new() { ClientCode = _clientCode }, cancellationToken, true);

	public Task<MotilalOswalMarginRow[]> GetMargin(CancellationToken cancellationToken)
		=> Send<MotilalOswalMarginRow[], MotilalOswalClientRequest>(
			"rest/report/v3/getreportmargindetail", new() { ClientCode = _clientCode }, cancellationToken, true);

	public async Task<string> PlaceOrder(MotilalOswalPlaceOrderRequest order, CancellationToken cancellationToken)
	{
		var response = await SendRoot<MotilalOswalPlaceOrderResponse, MotilalOswalPlaceOrderRequest>(
			"rest/trans/v2/placeorder", order, cancellationToken);
		ValidateStatus(response.Status, response.Message, response.ErrorCode, "place order");
		return response.UniqueOrderId.ThrowIfEmpty(nameof(response.UniqueOrderId));
	}

	public async Task ModifyOrder(MotilalOswalModifyOrderRequest order, CancellationToken cancellationToken)
	{
		var response = await SendRoot<MotilalOswalStatusResponse, MotilalOswalModifyOrderRequest>(
			"rest/trans/v5/modifyorder", order, cancellationToken);
		ValidateStatus(response.Status, response.Message, response.ErrorCode, "modify order");
	}

	public async Task CancelOrder(string uniqueOrderId, CancellationToken cancellationToken)
	{
		var response = await SendRoot<MotilalOswalStatusResponse, MotilalOswalCancelOrderRequest>(
			"rest/trans/v2/cancelorder", new()
			{
				ClientCode = _clientCode,
				UniqueOrderId = uniqueOrderId.ThrowIfEmpty(nameof(uniqueOrderId)),
			}, cancellationToken);
		ValidateStatus(response.Status, response.Message, response.ErrorCode, "cancel order");
	}

	private async Task<TResponse> Send<TResponse, TRequest>(string path, TRequest body,
		CancellationToken cancellationToken, bool allowNoData = false)
		where TResponse : class
		where TRequest : class
	{
		var response = await SendRoot<MotilalOswalResponse<TResponse>, TRequest>(path, body, cancellationToken);
		if (!IsSuccess(response.Status))
		{
			if (allowNoData && IsNoData(response.ErrorCode, response.Message))
				return typeof(TResponse).IsArray ? Array.CreateInstance(typeof(TResponse).GetElementType(), 0) as TResponse : null;
			throw CreateApiError(response.ErrorCode, response.Message, path);
		}
		return response.Data;
	}

	private async Task<TResponse> SendRoot<TResponse, TRequest>(string path, TRequest body, CancellationToken cancellationToken)
		where TResponse : class
		where TRequest : class
	{
		var request = CreateRequest(path);
		request.AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json);
		var response = await request.InvokeAsync<TResponse>(new Uri(_baseUri, path), this, this.AddVerboseLog, cancellationToken);
		return response ?? throw new InvalidOperationException($"Motilal Oswal returned an empty response for {path}.");
	}

	private RestRequest CreateRequest(string path)
	{
		var request = new RestRequest(path, Method.Post);
		request.AddHeader("Accept", "application/json");
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("User-Agent", "MOSL/V.1.1.0");
		request.AddHeader("Authorization", _authToken);
		request.AddHeader("ApiKey", _apiKey);
		request.AddHeader("ClientLocalIp", _localIp);
		request.AddHeader("ClientPublicIp", _publicIp);
		request.AddHeader("MacAddress", _macAddress);
		request.AddHeader("SourceId", "DESKTOP");
		request.AddHeader("vendorinfo", _vendorInfo);
		request.AddHeader("osname", RuntimeInformation.OSDescription);
		request.AddHeader("osversion", Environment.OSVersion.Version.ToString());
		request.AddHeader("devicemodel", Environment.MachineName);
		request.AddHeader("manufacturer", "StockSharp");
		request.AddHeader("productname", "StockSharp");
		request.AddHeader("productversion", "1");
		request.AddHeader("installedappid", _installedAppId);
		request.AddHeader("apisecretkey", _apiSecret);
		request.AddHeader("accesstoken", _accessToken);
		request.AddHeader("sdkversion", ".NET StockSharp");
		return request;
	}

	private static void ValidateStatus(string status, string message, string errorCode, string operation)
	{
		if (!IsSuccess(status))
			throw CreateApiError(errorCode, message, operation);
	}

	private static bool IsSuccess(string status)
		=> status.EqualsIgnoreCase("SUCCESS");

	private static bool IsNoData(string errorCode, string message)
		=> errorCode?.ToUpperInvariant() is "MO1013" or "MO1014" or "MO1015" or "MO1016" ||
			message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
			message?.Contains("no data", StringComparison.OrdinalIgnoreCase) == true;

	private static InvalidOperationException CreateApiError(string errorCode, string message, string operation)
		=> new($"Motilal Oswal {operation} error {errorCode.IsEmpty("UNKNOWN")}: {message.IsEmpty("No error message was returned.")}");

	private static string ValidateIpv4(string value, string paramName)
	{
		if (!IPAddress.TryParse(value, out var address) || address.GetAddressBytes().Length != 4)
			throw new ArgumentException("A valid IPv4 address is required by Motilal Oswal headers.", paramName);
		return value;
	}

	private static string ValidateMacAddress(string value)
	{
		if (value.IsEmpty() || !Regex.IsMatch(value, @"^[0-9A-Fa-f]{2}([:-][0-9A-Fa-f]{2}){5}$"))
			throw new ArgumentException("A MAC address in 00:00:00:00:00:00 format is required.", nameof(value));
		return value;
	}
}
