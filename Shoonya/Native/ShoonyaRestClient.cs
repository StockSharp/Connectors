namespace StockSharp.Shoonya.Native;

sealed class ShoonyaRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://api.shoonya.com/NorenWClientTP/";
	private const string _instrumentUrl = "https://api.shoonya.com/{0}_symbols.txt.zip";
	private static readonly string[] _segments = ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX"];
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _userId;
	private readonly string _accountId;
	private readonly string _sessionToken;
	private readonly HttpClient _httpClient = new() { BaseAddress = new(_apiUrl) };
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private ShoonyaInstrument[] _instruments;
	private IReadOnlyDictionary<string, ShoonyaInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, ShoonyaInstrument> _instrumentsBySymbol;

	public ShoonyaRestClient(string userId, string accountId, SecureString sessionToken)
	{
		_userId = userId.ThrowIfEmpty(nameof(userId));
		_accountId = accountId.ThrowIfEmpty(nameof(accountId));
		_sessionToken = sessionToken.ThrowIfEmpty(nameof(sessionToken)).UnSecure();
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Shoonya/1.0");
	}

	public override string Name => nameof(Shoonya) + "_" + nameof(ShoonyaRestClient);

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<ShoonyaInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var pages = await Task.WhenAll(_segments.Select(segment => DownloadInstruments(segment, cancellationToken)));
			_instruments = [.. pages.SelectMany(page => page)];
			_instrumentsByKey = _instruments
				.GroupBy(i => i.Exchange.ToInstrumentKey(i.Token), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			_instrumentsBySymbol = _instruments
				.Where(i => !i.TradingSymbol.IsEmpty())
				.GroupBy(i => ToSymbolKey(i.Exchange, i.TradingSymbol), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<ShoonyaInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public async Task<ShoonyaInstrument> FindInstrument(string exchange, string tradingSymbol, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsBySymbol.TryGetValue(ToSymbolKey(exchange, tradingSymbol), out var instrument) ? instrument : null;
	}

	public async Task<string> PlaceOrder(ShoonyaPlaceOrderRequest order, CancellationToken cancellationToken)
	{
		var response = await SendObject<ShoonyaOrderResult, ShoonyaPlaceOrderRequest>("PlaceOrder", order, cancellationToken);
		return response.OrderId.IsEmpty(response.Result).ThrowIfEmpty(nameof(response.OrderId));
	}

	public Task ModifyOrder(ShoonyaModifyOrderRequest order, CancellationToken cancellationToken)
		=> SendStatus("ModifyOrder", order, cancellationToken);

	public Task CancelOrder(string orderId, CancellationToken cancellationToken)
		=> SendStatus("CancelOrder", new ShoonyaCancelOrderRequest
		{
			UserId = _userId,
			OrderId = orderId.ThrowIfEmpty(nameof(orderId)),
		}, cancellationToken);

	public Task<ShoonyaOrder[]> GetOrders(CancellationToken cancellationToken)
		=> SendArray<ShoonyaOrder, ShoonyaUserRequest>("OrderBook",
			new() { UserId = _userId }, cancellationToken, true);

	public Task<ShoonyaTrade[]> GetTrades(CancellationToken cancellationToken)
		=> SendArray<ShoonyaTrade, ShoonyaAccountRequest>("TradeBook",
			new() { UserId = _userId, AccountId = _accountId }, cancellationToken, true);

	public Task<ShoonyaPosition[]> GetPositions(CancellationToken cancellationToken)
		=> SendArray<ShoonyaPosition, ShoonyaAccountRequest>("PositionBook",
			new() { UserId = _userId, AccountId = _accountId }, cancellationToken, true);

	public Task<ShoonyaHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> SendArray<ShoonyaHolding, ShoonyaAccountRequest>("Holdings",
			new() { UserId = _userId, AccountId = _accountId, Product = ShoonyaProducts.Delivery.ToNative() },
			cancellationToken, true);

	public Task<ShoonyaLimits> GetLimits(CancellationToken cancellationToken)
		=> SendObject<ShoonyaLimits, ShoonyaAccountRequest>("Limits",
			new() { UserId = _userId, AccountId = _accountId }, cancellationToken);

	public async Task<ShoonyaCandle[]> GetCandles(ShoonyaInstrument instrument, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var end = NormalizeUtc(to ?? DateTime.UtcNow);
		var start = NormalizeUtc(from ?? end.AddDays(timeFrame == TimeSpan.FromDays(1) ? -3650 : -30));
		if (start > end)
			return [];

		if (timeFrame == TimeSpan.FromDays(1))
		{
			var request = new ShoonyaDailyCandleRequest
			{
				UserId = _userId,
				Symbol = $"{instrument.Exchange}:{instrument.TradingSymbol}",
				From = start.ToUnixSeconds().ToString(CultureInfo.InvariantCulture),
				To = end.ToUnixSeconds().ToString(CultureInfo.InvariantCulture),
			};
			return await SendDailyCandles(request, cancellationToken);
		}

		var minutes = Convert.ToInt32(timeFrame.TotalMinutes);
		if (minutes is not (1 or 3 or 5 or 10 or 15 or 30 or 60 or 120 or 240))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Shoonya candle interval.");

		return await SendArray<ShoonyaCandle, ShoonyaCandleRequest>("TPSeries", new()
		{
			UserId = _userId,
			Exchange = instrument.Exchange,
			Token = instrument.Token,
			From = start.ToUnixSeconds().ToString(CultureInfo.InvariantCulture),
			To = end.ToUnixSeconds().ToString(CultureInfo.InvariantCulture),
			Interval = minutes.ToString(CultureInfo.InvariantCulture),
		}, cancellationToken, true);
	}

	private async Task SendStatus<TRequest>(string path, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
		=> await SendObject<ShoonyaOrderResult, TRequest>(path, body, cancellationToken);

	private async Task<TResponse> SendObject<TResponse, TRequest>(string path, TRequest body,
		CancellationToken cancellationToken)
		where TResponse : ShoonyaResponse
		where TRequest : class
	{
		var content = await SendRaw(path, body, cancellationToken);
		var response = JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
			?? throw new InvalidOperationException($"Shoonya returned an empty response for {path}.");
		EnsureSuccess(response, path);
		return response;
	}

	private async Task<TItem[]> SendArray<TItem, TRequest>(string path, TRequest body,
		CancellationToken cancellationToken, bool allowNoData)
		where TItem : ShoonyaResponse
		where TRequest : class
	{
		var content = await SendRaw(path, body, cancellationToken);
		if (FirstToken(content) != '[')
		{
			var error = JsonConvert.DeserializeObject<ShoonyaResponse>(content, _jsonSettings)
				?? throw new InvalidOperationException($"Shoonya returned an invalid response for {path}.");
			if (allowNoData && IsNoData(error))
				return [];
			EnsureSuccess(error, path);
			throw new InvalidOperationException($"Shoonya returned an unexpected object response for {path}.");
		}

		var items = JsonConvert.DeserializeObject<TItem[]>(content, _jsonSettings) ?? [];
		foreach (var item in items)
			EnsureSuccess(item, path);
		return items;
	}

	private async Task<ShoonyaCandle[]> SendDailyCandles(ShoonyaDailyCandleRequest body,
		CancellationToken cancellationToken)
	{
		const string path = "EODChartData";
		var content = await SendRaw(path, body, cancellationToken);
		if (FirstToken(content) != '[')
		{
			var error = JsonConvert.DeserializeObject<ShoonyaResponse>(content, _jsonSettings)
				?? throw new InvalidOperationException("Shoonya returned an invalid daily-candle response.");
			if (IsNoData(error))
				return [];
			EnsureSuccess(error, path);
		}

		var arrayStart = content.IndexOf('[');
		var itemStart = arrayStart + 1;
		while (itemStart < content.Length && char.IsWhiteSpace(content[itemStart]))
			itemStart++;

		if (itemStart < content.Length && content[itemStart] == '"')
		{
			var encoded = JsonConvert.DeserializeObject<string[]>(content, _jsonSettings) ?? [];
			return [.. encoded
				.Where(value => !value.IsEmpty())
				.Select(value => JsonConvert.DeserializeObject<ShoonyaCandle>(value, _jsonSettings))
				.Where(candle => candle != null)];
		}

		return JsonConvert.DeserializeObject<ShoonyaCandle[]>(content, _jsonSettings) ?? [];
	}

	private async Task<string> SendRaw<TRequest>(string path, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
	{
		var json = JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings);
		var form = $"jData={Uri.EscapeDataString(json)}&jKey={Uri.EscapeDataString(_sessionToken)}";
		using var request = new HttpRequestMessage(HttpMethod.Post, path)
		{
			Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
		};
		this.AddVerboseLog("Shoonya POST {0}.", path);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"Shoonya {path} returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
		if (content.IsEmpty())
			throw new InvalidOperationException($"Shoonya returned an empty response for {path}.");
		return content;
	}

	private async Task<ShoonyaInstrument[]> DownloadInstruments(string segment, CancellationToken cancellationToken)
	{
		var bytes = await _httpClient.GetByteArrayAsync(string.Format(CultureInfo.InvariantCulture, _instrumentUrl, segment), cancellationToken);
		using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
		var entry = archive.Entries.FirstOrDefault(e => !e.Name.IsEmpty())
			?? throw new InvalidDataException($"Shoonya {segment} security master is empty.");
		using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true, 1 << 16);
		var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
		if (!await csv.NextLineAsync(cancellationToken))
			return [];

		var instruments = new List<ShoonyaInstrument>();
		while (await csv.NextLineAsync(cancellationToken))
		{
			var instrument = ReadInstrument(segment, csv);
			if (instrument != null)
				instruments.Add(instrument);
		}
		return [.. instruments];
	}

	private static ShoonyaInstrument ReadInstrument(string segment, FastCsvReader csv)
	{
		string[] values;
		var instrument = new ShoonyaInstrument();
		switch (segment)
		{
			case "NSE":
			case "BSE":
				values = ReadColumns(csv, 7);
				instrument.Exchange = values[0];
				instrument.Token = values[1];
				instrument.LotSize = values[2].ToDecimal();
				instrument.Symbol = values[3];
				instrument.TradingSymbol = values[4];
				instrument.Instrument = values[5];
				instrument.TickSize = values[6].ToDecimal();
				break;

			case "NFO":
			case "BFO":
				values = ReadColumns(csv, 10);
				instrument.Exchange = values[0];
				instrument.Token = values[1];
				instrument.LotSize = values[2].ToDecimal();
				instrument.Symbol = values[3];
				instrument.TradingSymbol = values[4];
				instrument.Expiry = ParseExpiry(values[5]);
				instrument.Instrument = values[6];
				instrument.OptionType = values[7];
				instrument.StrikePrice = values[8].ToDecimal();
				instrument.TickSize = values[9].ToDecimal();
				break;

			case "CDS":
				values = ReadColumns(csv, 12);
				instrument.Exchange = values[0];
				instrument.Token = values[1];
				instrument.LotSize = values[2].ToDecimal();
				instrument.Precision = values[3].ToInt();
				instrument.Multiplier = values[4].ToDecimal();
				instrument.Symbol = values[5];
				instrument.TradingSymbol = values[6];
				instrument.Expiry = ParseExpiry(values[7]);
				instrument.Instrument = values[8];
				instrument.OptionType = values[9];
				instrument.StrikePrice = values[10].ToDecimal();
				instrument.TickSize = values[11].ToDecimal();
				break;

			case "MCX":
				values = ReadColumns(csv, 11);
				instrument.Exchange = values[0];
				instrument.Token = values[1];
				instrument.LotSize = values[2].ToDecimal();
				instrument.Multiplier = values[3].ToDecimal();
				instrument.Symbol = values[4];
				instrument.TradingSymbol = values[5];
				instrument.Expiry = ParseExpiry(values[6]);
				instrument.Instrument = values[7];
				instrument.OptionType = values[8];
				instrument.StrikePrice = values[9].ToDecimal();
				instrument.TickSize = values[10].ToDecimal();
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(segment), segment, null);
		}

		if (instrument.Exchange.IsEmpty() || instrument.Token.IsEmpty() || instrument.TradingSymbol.IsEmpty())
			return null;
		instrument.LotSize = instrument.LotSize > 0 ? instrument.LotSize : 1;
		instrument.Multiplier = instrument.Multiplier > 0 ? instrument.Multiplier : instrument.LotSize;
		return instrument;
	}

	private static string[] ReadColumns(FastCsvReader csv, int count)
	{
		var values = new string[count];
		for (var i = 0; i < count; i++)
			values[i] = csv.ReadString()?.Trim();
		return values;
	}

	private static DateTime? ParseExpiry(string value)
		=> DateTime.TryParseExact(value, "dd-MMM-yyyy", CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var expiry)
			? expiry.ToUtcFromIndia()
			: null;

	private static string ToSymbolKey(string exchange, string tradingSymbol)
		=> $"{exchange?.ToUpperInvariant()}|{tradingSymbol?.ToUpperInvariant()}";

	private static char FirstToken(string content)
		=> content.SkipWhile(char.IsWhiteSpace).FirstOrDefault();

	private static bool IsNoData(ShoonyaResponse response)
		=> response.ErrorMessage?.Contains("no data", StringComparison.OrdinalIgnoreCase) == true ||
			response.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true;

	private static void EnsureSuccess(ShoonyaResponse response, string operation)
	{
		if (response == null)
			throw new InvalidOperationException($"Shoonya returned an empty response for {operation}.");
		if (response.Status.EqualsIgnoreCase("Ok") ||
			response.Status.IsEmpty() && response.ErrorMessage.IsEmpty())
			return;
		throw new InvalidOperationException($"Shoonya {operation} error: {response.ErrorMessage.IsEmpty(response.Status)}");
	}

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
