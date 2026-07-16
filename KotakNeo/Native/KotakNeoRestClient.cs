namespace StockSharp.KotakNeo.Native;

sealed class KotakNeoRestClient : BaseLogReceiver
{
	private const string _loginUrl = "https://mis.kotaksecurities.com/login/1.0/tradeApiLogin";
	private const string _validateUrl = "https://mis.kotaksecurities.com/login/1.0/tradeApiValidate";
	private const string _finKey = "neotradeapi";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _consumerKey;
	private readonly HttpClient _fileClient = new();
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private KotakNeoSession _session;
	private KotakNeoInstrument[] _instruments;
	private IReadOnlyDictionary<string, KotakNeoInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, KotakNeoInstrument> _instrumentsByTradingSymbol;

	public KotakNeoRestClient(string consumerKey)
	{
		_consumerKey = consumerKey.ThrowIfEmpty(nameof(consumerKey));
	}

	public override string Name => nameof(KotakNeo) + "_" + nameof(KotakNeoRestClient);

	public KotakNeoSession Session => _session ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	protected override void DisposeManaged()
	{
		_fileClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<KotakNeoSession> Login(string mobileNumber, string userCode, string totp, SecureString mpin,
		CancellationToken cancellationToken)
	{
		var loginRequest = new RestRequest((string)null, Method.Post)
			.AddHeader("Authorization", _consumerKey)
			.AddHeader("neo-fin-key", _finKey)
			.AddHeader("Content-Type", "application/json")
			.AddStringBody(JsonConvert.SerializeObject(new KotakNeoLoginRequest
			{
				MobileNumber = mobileNumber.ThrowIfEmpty(nameof(mobileNumber)),
				UserCode = userCode.ThrowIfEmpty(nameof(userCode)),
				Totp = totp.ThrowIfEmpty(nameof(totp)),
			}, _jsonSettings), DataFormat.Json);

		var view = await loginRequest.InvokeAsync<KotakNeoLoginResponse>(new Uri(_loginUrl), this, this.AddVerboseLog, cancellationToken);
		EnsureLogin(view, "TOTP login");

		var validateRequest = new RestRequest((string)null, Method.Post)
			.AddHeader("Authorization", _consumerKey)
			.AddHeader("sid", view.Data.Sid)
			.AddHeader("Auth", view.Data.Token)
			.AddHeader("neo-fin-key", _finKey)
			.AddHeader("Content-Type", "application/json")
			.AddStringBody(JsonConvert.SerializeObject(new KotakNeoMpinRequest
			{
				Mpin = mpin.ThrowIfEmpty(nameof(mpin)).UnSecure(),
			}, _jsonSettings), DataFormat.Json);

		var trade = await validateRequest.InvokeAsync<KotakNeoLoginResponse>(new Uri(_validateUrl), this, this.AddVerboseLog, cancellationToken);
		EnsureLogin(trade, "MPIN validation");
		if (!Uri.TryCreate(trade.Data.BaseUrl, UriKind.Absolute, out _))
			throw new InvalidOperationException("Kotak Neo MPIN validation did not return a valid baseUrl.");
		if (trade.Data.ServerId.IsEmpty())
			throw new InvalidOperationException("Kotak Neo MPIN validation did not return hsServerId.");

		return _session = trade.Data;
	}

	public async Task<KotakNeoInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var request = CreateRequest(Method.Get, false)
				.AddHeader("Authorization", _consumerKey);
			var response = await request.InvokeAsync<KotakNeoScripFilesResponse>(CreateUri("script-details/1.0/masterscrip/file-paths"), this, this.AddVerboseLog, cancellationToken);
			var paths = response?.Data?.FilePaths ?? response?.FilePaths;
			if (paths == null || paths.Length == 0)
				throw new InvalidOperationException(response?.Message ?? "Kotak Neo did not return scrip-master file paths.");

			var instruments = new List<KotakNeoInstrument>();
			foreach (var path in paths.Where(p => !p.IsEmpty()))
				await ReadInstruments(path, instruments, cancellationToken);

			_instruments = [.. instruments
				.Where(i => !i.Token.IsEmpty() && !i.ExchangeSegment.IsEmpty())
				.GroupBy(i => i.ExchangeSegment.ToInstrumentKey(i.Token), StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())];
			_instrumentsByKey = _instruments.ToDictionary(i => i.ExchangeSegment.ToInstrumentKey(i.Token), StringComparer.OrdinalIgnoreCase);
			_instrumentsByTradingSymbol = _instruments
				.Where(i => !i.TradingSymbol.IsEmpty())
				.GroupBy(i => $"{i.ExchangeSegment}|{i.TradingSymbol}", StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<KotakNeoInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public async Task<KotakNeoInstrument> GetInstrument(string exchangeSegment, string token, string tradingSymbol,
		CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		if (!token.IsEmpty() && _instrumentsByKey.TryGetValue(exchangeSegment.ToInstrumentKey(token), out var instrument))
			return instrument;
		return !tradingSymbol.IsEmpty() && _instrumentsByTradingSymbol.TryGetValue($"{exchangeSegment}|{tradingSymbol}", out instrument)
			? instrument
			: null;
	}

	public Task<KotakNeoResponse<KotakNeoOrder[]>> GetOrders(CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoOrder[]>>("quick/user/orders", Method.Get, cancellationToken);

	public Task<KotakNeoResponse<KotakNeoTrade[]>> GetTrades(CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoTrade[]>>("quick/user/trades", Method.Get, cancellationToken);

	public Task<KotakNeoResponse<KotakNeoPosition[]>> GetPositions(CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoPosition[]>>("quick/user/positions", Method.Get, cancellationToken);

	public Task<KotakNeoResponse<KotakNeoHolding[]>> GetHoldings(CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoHolding[]>>("portfolio/v1/holdings", Method.Get, cancellationToken, false);

	public Task<KotakNeoLimits> GetLimits(CancellationToken cancellationToken)
		=> Send<KotakNeoLimits, KotakNeoLimitsRequest>("quick/user/limits", Method.Post, new(), cancellationToken);

	public Task<KotakNeoResponse<KotakNeoNoData>> PlaceOrder(KotakNeoOrderRequest body, CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoNoData>, KotakNeoOrderRequest>("quick/order/rule/ms/place", Method.Post, body, cancellationToken);

	public Task<KotakNeoResponse<KotakNeoNoData>> ModifyOrder(KotakNeoModifyOrderRequest body, CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoNoData>, KotakNeoModifyOrderRequest>("quick/order/vr/modify", Method.Post, body, cancellationToken);

	public Task<KotakNeoResponse<KotakNeoNoData>> CancelOrder(KotakNeoCancelOrderRequest body, KotakNeoProducts product,
		CancellationToken cancellationToken)
		=> Send<KotakNeoResponse<KotakNeoNoData>, KotakNeoCancelOrderRequest>(product switch
		{
			KotakNeoProducts.Cover => "quick/order/co/exit",
			KotakNeoProducts.Bracket => "quick/order/bo/exit",
			_ => "quick/order/cancel",
		}, Method.Post, body, cancellationToken);

	private Task<T> Send<T>(string path, Method method, CancellationToken cancellationToken, bool includeServerId = true)
		=> Send<T, KotakNeoNoRequest>(path, method, null, cancellationToken, includeServerId);

	private async Task<TResponse> Send<TResponse, TRequest>(string path, Method method, TRequest body,
		CancellationToken cancellationToken, bool includeServerId = true)
		where TRequest : class
	{
		var request = CreateRequest(method, true);
		if (includeServerId)
			request.AddQueryParameter("sId", Session.ServerId);
		if (body != null)
		{
			request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
			request.AddParameter("jData", JsonConvert.SerializeObject(body, _jsonSettings));
		}

		return await request.InvokeAsync<TResponse>(CreateUri(path), this, this.AddVerboseLog, cancellationToken);
	}

	private RestRequest CreateRequest(Method method, bool authenticated)
	{
		var request = new RestRequest((string)null, method)
			.AddHeader("Accept", "application/json");
		if (authenticated)
		{
			request.AddHeader("Sid", Session.Sid);
			request.AddHeader("Auth", Session.Token);
		}
		return request;
	}

	private Uri CreateUri(string path)
		=> new(new Uri(Session.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

	private async Task ReadInstruments(string url, ICollection<KotakNeoInstrument> instruments, CancellationToken cancellationToken)
	{
		await using var stream = await _fileClient.GetStreamAsync(url, cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);
		var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
		if (!await csv.NextLineAsync(cancellationToken))
			return;

		while (await csv.NextLineAsync(cancellationToken))
		{
			var token = csv.ReadString();
			var group = csv.ReadString();
			var exchangeSegment = csv.ReadString();
			var instrumentType = csv.ReadString();
			var symbol = csv.ReadString();
			var tradingSymbol = csv.ReadString();
			var optionType = csv.ReadString();
			csv.ReadString();
			var isin = csv.ReadString();
			var assetCode = csv.ReadString();
			for (var i = 0; i < 5; i++)
				csv.ReadString();
			var rawTickSize = ParseDecimal(csv.ReadString());
			var lotSize = ParseDecimal(csv.ReadString());
			var rawExpiry = ParseLong(csv.ReadString());
			var multiplier = ParseDecimal(csv.ReadString());
			var precision = ParseInt(csv.ReadString());
			var rawStrike = ParseDecimal(csv.ReadString());
			var exchange = csv.ReadString();
			var instrumentName = csv.ReadString();
			var alternateExpiry = ParseLong(csv.ReadString());

			if (token.IsEmpty() || exchangeSegment.IsEmpty())
				continue;
			try
			{
				exchangeSegment.ToBoardCode();
			}
			catch (ArgumentOutOfRangeException)
			{
				continue;
			}

			var scale = Pow10(precision);
			instruments.Add(new KotakNeoInstrument
			{
				Token = token,
				Group = group,
				ExchangeSegment = exchangeSegment,
				InstrumentType = instrumentType,
				Symbol = symbol,
				TradingSymbol = tradingSymbol,
				OptionType = optionType,
				Isin = isin,
				AssetCode = assetCode,
				TickSize = rawTickSize is > 0 ? rawTickSize / scale : null,
				LotSize = lotSize is > 0 ? lotSize : null,
				ExpiryDate = ToDate(alternateExpiry > 0 ? alternateExpiry : rawExpiry),
				Multiplier = multiplier is > 0 ? multiplier : null,
				Precision = precision,
				StrikePrice = rawStrike is > 0 ? rawStrike / scale : null,
				Exchange = exchange,
				InstrumentName = instrumentName,
			});
		}
	}

	private static void EnsureLogin(KotakNeoLoginResponse response, string operation)
	{
		if (response?.Data != null && !response.Data.Token.IsEmpty() && !response.Data.Sid.IsEmpty())
			return;
		var error = response?.Errors?.FirstOrDefault();
		throw new InvalidOperationException($"Kotak Neo {operation} failed: {error?.Message ?? response?.Message ?? "empty response"}.");
	}

	private static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	private static long ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

	private static int ParseInt(string value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? Math.Clamp(result, 0, 12) : 0;

	private static decimal Pow10(int precision)
	{
		var value = 1m;
		for (var i = 0; i < precision; i++)
			value *= 10m;
		return value;
	}

	private static DateTime? ToDate(long epochSeconds)
	{
		if (epochSeconds <= 0)
			return null;
		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}
}
