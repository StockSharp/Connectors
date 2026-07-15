namespace StockSharp.Breeze.Native;

sealed class BreezeRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://api.icicidirect.com/breezeapi/api/v1/";
	private const string _historyUrl = "https://breezeapi.icicidirect.com/api/v2/historicalcharts";
	private const string _instrumentUrl = "https://directlink.icicidirect.com/MotherAppMaster/SecurityMaster.zip";

	private static readonly JsonSerializerSettings _jsonSettings = new() { NullValueHandling = NullValueHandling.Ignore };
	private readonly string _apiKey;
	private readonly SecureString _secretKey;
	private readonly SecureString _apiSession;
	private readonly HttpClient _httpClient = new();
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private BreezeInstrument[] _instruments;
	private IReadOnlyDictionary<string, BreezeInstrument> _instrumentsByNative;
	private IReadOnlyDictionary<string, BreezeInstrument> _instrumentsByContract;
	private string _sessionToken;
	private string _socketUser;
	private string _socketToken;

	public BreezeRestClient(string apiKey, SecureString secretKey, SecureString apiSession)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_secretKey = secretKey.ThrowIfEmpty(nameof(secretKey));
		_apiSession = apiSession.ThrowIfEmpty(nameof(apiSession));
	}

	public override string Name => nameof(Breeze) + "_" + nameof(BreezeRestClient);
	public string SocketUser => _socketUser;
	public string SocketToken => _socketToken;

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<BreezeCustomer> Authenticate(CancellationToken cancellationToken)
	{
		var request = new BreezeCustomerRequest { SessionToken = _apiSession.UnSecure(), AppKey = _apiKey };
		var response = await SendCore<BreezeCustomer, BreezeCustomerRequest>("customerdetails", Method.Get, request, false, cancellationToken);
		_sessionToken = response.SessionToken.ThrowIfEmpty(nameof(response.SessionToken));
		var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(_sessionToken));
		var delimiter = decoded.IndexOf(':');
		if (delimiter <= 0 || delimiter == decoded.Length - 1)
			throw new InvalidDataException("Breeze returned an invalid streaming session token.");
		_socketUser = decoded[..delimiter];
		_socketToken = decoded[(delimiter + 1)..];
		return response;
	}

	public async Task<BreezeInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;
		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;
			var bytes = await _httpClient.GetByteArrayAsync(_instrumentUrl, cancellationToken);
			using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
			var instruments = new List<BreezeInstrument>();
			await ReadEquities(archive, instruments, cancellationToken);
			await ReadDerivatives(archive, instruments, cancellationToken);
			_instruments = [.. instruments];
			_instrumentsByNative = _instruments.ToDictionary(i => i.ToNativeId(), StringComparer.OrdinalIgnoreCase);
			_instrumentsByContract = _instruments
				.GroupBy(ToContractKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<BreezeInstrument> GetInstrument(SecurityId securityId, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		if (securityId.Native is string native && _instrumentsByNative.TryGetValue(native, out var instrument))
			return instrument;
		var parsed = securityId.ToBreezeInstrument();
		return _instrumentsByContract.TryGetValue(ToContractKey(parsed), out instrument) ? instrument : parsed;
	}

	public async Task<BreezeInstrument> FindInstrument(string exchangeCode, string stockCode, string product,
		string expiryDate, string right, decimal? strikePrice, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		var isDerivatives = exchangeCode.EqualsIgnoreCase("NFO");
		var kind = product.EqualsIgnoreCase("options") || product.EqualsIgnoreCase("o") ||
			(isDerivatives && !right.IsEmpty() && !right.EqualsIgnoreCase("others")) ? BreezeInstrumentKinds.Option
			: product.EqualsIgnoreCase("futures") || product.EqualsIgnoreCase("f") || isDerivatives ? BreezeInstrumentKinds.Future
			: BreezeInstrumentKinds.Equity;
		var instrument = new BreezeInstrument
		{
			BoardCode = exchangeCode.EqualsIgnoreCase("NFO") ? "NFO" : "NSE",
			StockCode = stockCode,
			Kind = kind,
			ExpiryDate = expiryDate.ParseBreezeTime()?.Date,
			OptionType = right.EqualsIgnoreCase("call") || right.EqualsIgnoreCase("ce") || right.EqualsIgnoreCase("c") ? OptionTypes.Call
				: right.EqualsIgnoreCase("put") || right.EqualsIgnoreCase("pe") || right.EqualsIgnoreCase("p") ? OptionTypes.Put : null,
			StrikePrice = strikePrice,
		};
		return _instrumentsByContract.TryGetValue(ToContractKey(instrument), out var found) ? found : instrument;
	}

	public Task<BreezeOrderResult> PlaceOrder(BreezeOrderRequest request, CancellationToken cancellationToken)
		=> Send<BreezeOrderResult, BreezeOrderRequest>("order", Method.Post, request, cancellationToken);

	public Task<BreezeOrderResult> ModifyOrder(BreezeModifyOrderRequest request, CancellationToken cancellationToken)
		=> Send<BreezeOrderResult, BreezeModifyOrderRequest>("order", Method.Put, request, cancellationToken);

	public Task<BreezeOrderResult> CancelOrder(string exchangeCode, string orderId, CancellationToken cancellationToken)
		=> Send<BreezeOrderResult, BreezeCancelOrderRequest>("order", Method.Delete, new() { ExchangeCode = exchangeCode, OrderId = orderId }, cancellationToken);

	public async Task<BreezeOrder[]> GetOrders(string exchangeCode, DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var orders = new List<BreezeOrder>();
		var cursor = from;
		while (cursor <= to)
		{
			var pageEnd = cursor.AddDays(9);
			if (pageEnd > to)
				pageEnd = to;
			orders.AddRange(await Send<BreezeOrder[], BreezeOrderQuery>("order", Method.Get, new()
			{
				ExchangeCode = exchangeCode,
				From = FormatDate(cursor),
				To = FormatDate(pageEnd),
			}, cancellationToken) ?? []);
			if (pageEnd >= to)
				break;
			cursor = pageEnd.AddSeconds(1);
		}
		return [.. orders.GroupBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase).Select(g => g.Last())];
	}

	public async Task<BreezeTrade[]> GetTrades(string exchangeCode, DateTime from, DateTime to, CancellationToken cancellationToken)
		=> await Send<BreezeTrade[], BreezeOrderQuery>("trades", Method.Get, new()
		{
			ExchangeCode = exchangeCode,
			From = FormatDate(from),
			To = FormatDate(to),
		}, cancellationToken) ?? [];

	public Task<BreezeFunds> GetFunds(CancellationToken cancellationToken)
		=> Send<BreezeFunds, BreezeEmptyRequest>("funds", Method.Get, new(), cancellationToken);

	public async Task<BreezePosition[]> GetPositions(CancellationToken cancellationToken)
		=> await Send<BreezePosition[], BreezeEmptyRequest>("portfoliopositions", Method.Get, new(), cancellationToken) ?? [];

	public async Task<BreezeHolding[]> GetHoldings(string exchangeCode, CancellationToken cancellationToken)
		=> await Send<BreezeHolding[], BreezePortfolioQuery>("portfolioholdings", Method.Get, new() { ExchangeCode = exchangeCode }, cancellationToken) ?? [];

	public async Task<BreezeCandle[]> GetCandles(BreezeInstrument instrument, TimeSpan timeFrame, DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).ToIndiaTime();
		var start = (from ?? end.AddDays(timeFrame >= TimeSpan.FromDays(1) ? -3650 : -30)).ToIndiaTime();
		if (start > end)
			return [];
		var interval = timeFrame.ToHistoryInterval();
		var result = new List<BreezeCandle>();
		var pageSpan = TimeSpan.FromTicks(timeFrame.Ticks * 990);
		while (start <= end)
		{
			var pageEnd = start.Add(pageSpan);
			if (pageEnd > end)
				pageEnd = end;
			var items = await SendHistory(instrument, interval, start, pageEnd, cancellationToken);
			foreach (var item in items)
			{
				if (item.DateTime.ParseBreezeTime() is not DateTime time)
					continue;
				result.Add(new BreezeCandle { Time = time, Open = item.Open, High = item.High, Low = item.Low, Close = item.Close, Volume = item.Volume, OpenInterest = item.OpenInterest });
			}
			if (pageEnd >= end)
				break;
			start = pageEnd.Add(timeFrame);
		}
		return [.. result.GroupBy(c => c.Time).Select(g => g.Last()).OrderBy(c => c.Time)];
	}

	private async Task<BreezeHistoryItem[]> SendHistory(BreezeInstrument instrument, string interval, DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var request = new RestRequest((string)null, Method.Get);
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("X-SessionToken", _sessionToken);
		request.AddHeader("apikey", _apiKey);
		request.AddQueryParameter("interval", interval);
		request.AddQueryParameter("from_date", FormatTimestamp(from));
		request.AddQueryParameter("to_date", FormatTimestamp(to));
		request.AddQueryParameter("stock_code", instrument.StockCode);
		request.AddQueryParameter("exch_code", instrument.ToExchangeCode());
		if (instrument.Kind != BreezeInstrumentKinds.Equity)
		{
			request.AddQueryParameter("product_type", instrument.ToProduct().ToNative());
			request.AddQueryParameter("expiry_date", FormatTimestamp(instrument.ExpiryDate ?? throw new InvalidOperationException("Derivative expiry is required.")));
			if (instrument.Kind == BreezeInstrumentKinds.Option)
			{
				request.AddQueryParameter("right", instrument.OptionType == OptionTypes.Call ? "call" : "put");
				request.AddQueryParameter("strike_price", FormatDecimal(instrument.StrikePrice ?? 0));
			}
		}
		var response = await request.InvokeAsync<BreezeResponse<BreezeHistoryItem[]>>(new Uri(_historyUrl), this, this.AddVerboseLog, cancellationToken);
		return EnsureSuccess(response) ?? [];
	}

	private Task<TResponse> Send<TResponse, TRequest>(string path, Method method, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
	{
		if (_sessionToken.IsEmpty())
			throw new InvalidOperationException("Breeze REST session is not authenticated.");
		return SendCore<TResponse, TRequest>(path, method, body, true, cancellationToken);
	}

	private async Task<TResponse> SendCore<TResponse, TRequest>(string path, Method method, TRequest body, bool authenticated, CancellationToken cancellationToken)
		where TRequest : class
	{
		var json = JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings);
		var request = new RestRequest((string)null, method);
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("Accept", "application/json");
		if (authenticated)
		{
			var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
			var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(timestamp + json + _secretKey.UnSecure()))).ToLowerInvariant();
			request.AddHeader("X-Checksum", "token " + checksum);
			request.AddHeader("X-Timestamp", timestamp);
			request.AddHeader("X-AppKey", _apiKey);
			request.AddHeader("X-SessionToken", _sessionToken);
		}
		request.AddStringBody(json, DataFormat.Json);
		var response = await request.InvokeAsync<BreezeResponse<TResponse>>(new Uri(new Uri(_apiUrl), path), this, this.AddVerboseLog, cancellationToken);
		return EnsureSuccess(response);
	}

	private static T EnsureSuccess<T>(BreezeResponse<T> response)
	{
		if (response == null)
			throw new InvalidOperationException("Breeze returned an empty response.");
		if (!response.Error.IsEmpty())
			throw new InvalidOperationException($"Breeze error {response.Status}: {response.Error}");
		return response.Success;
	}

	private static async Task ReadEquities(ZipArchive archive, List<BreezeInstrument> instruments, CancellationToken cancellationToken)
	{
		var entry = archive.Entries.FirstOrDefault(e => e.Name.EqualsIgnoreCase("NSEScripMaster.txt"))
			?? throw new InvalidDataException("Breeze NSE security master is missing.");
		using var reader = new StreamReader(entry.Open());
		var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
		if (!await csv.NextLineAsync(cancellationToken))
			return;
		while (await csv.NextLineAsync(cancellationToken))
		{
			var values = ReadColumns(csv, 61);
			if (!long.TryParse(values[0], out var token) || token <= 0 || values[1].IsEmpty())
				continue;
			instruments.Add(new BreezeInstrument
			{
				Token = values[0], StockCode = values[1], Name = values[3], Isin = values[10], BoardCode = "NSE", Kind = BreezeInstrumentKinds.Equity,
				PriceStep = ParseDecimal(values[4], 0.01m), LotSize = ParseDecimal(values[5], 1m),
			});
		}
	}

	private static async Task ReadDerivatives(ZipArchive archive, List<BreezeInstrument> instruments, CancellationToken cancellationToken)
	{
		var entry = archive.Entries.FirstOrDefault(e => e.Name.EqualsIgnoreCase("FONSEScripMaster.txt"))
			?? throw new InvalidDataException("Breeze NSE F&O security master is missing.");
		using var reader = new StreamReader(entry.Open());
		var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
		if (!await csv.NextLineAsync(cancellationToken))
			return;
		while (await csv.NextLineAsync(cancellationToken))
		{
			var values = ReadColumns(csv, 69);
			if (!long.TryParse(values[0], out var token) || token <= 0 || values[2].IsEmpty())
				continue;
			var isOption = values[1].StartsWithIgnoreCase("OPT");
			if (!isOption && !values[1].StartsWithIgnoreCase("FUT"))
				continue;
			if (!DateTime.TryParseExact(values[4], "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
				continue;
			instruments.Add(new BreezeInstrument
			{
				Token = values[0], StockCode = values[2], Name = values[29], BoardCode = "NFO",
				Kind = isOption ? BreezeInstrumentKinds.Option : BreezeInstrumentKinds.Future,
				ExpiryDate = expiry, StrikePrice = isOption ? ParseDecimal(values[5], 0) : null,
				OptionType = isOption ? values[6].EqualsIgnoreCase("CE") ? OptionTypes.Call : OptionTypes.Put : null,
				LotSize = ParseDecimal(values[27], 1m), PriceStep = ParseDecimal(values[28], 1m) / 100m,
			});
		}
	}

	private static string[] ReadColumns(FastCsvReader csv, int count)
	{
		var values = new string[count];
		for (var i = 0; i < count; i++)
			values[i] = csv.ReadString()?.Trim();
		return values;
	}

	private static decimal ParseDecimal(string value, decimal defaultValue)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

	private static string ToContractKey(BreezeInstrument instrument)
		=> string.Join("|", instrument.BoardCode, instrument.StockCode, instrument.Kind, instrument.ExpiryDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture), instrument.OptionType, instrument.StrikePrice);

	private static string FormatTimestamp(DateTime value)
		=> value.ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);

	private static string FormatDate(DateTime value)
		=> FormatTimestamp(value.ToIndiaTime());

	private static string FormatDecimal(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
