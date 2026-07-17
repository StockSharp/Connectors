namespace StockSharp.Flattrade.Native;

sealed class FlattradeRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://piconnect.flattrade.in/PiConnectAPI/";
	private const string _instrumentBaseUrl = "https://flattrade.s3.ap-south-1.amazonaws.com/scripmaster/";
	private static readonly string[] _instrumentFiles =
	[
		"NSE_Equity.csv",
		"Nfo_Equity_Derivatives.csv",
		"Nfo_Index_Derivatives.csv",
		"Currency_Derivatives.csv",
		"Commodity.csv",
		"BSE_Equity.csv",
		"Bfo_Index_Derivatives.csv",
		"Bfo_Equity_Derivatives.csv",
	];
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _userId;
	private readonly string _accountId;
	private readonly string _sessionToken;
	private readonly HttpClient _httpClient = new() { BaseAddress = new(_apiUrl) };
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private FlattradeInstrument[] _instruments;
	private IReadOnlyDictionary<string, FlattradeInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, FlattradeInstrument> _instrumentsBySymbol;

	public FlattradeRestClient(string userId, string accountId, SecureString sessionToken)
	{
		_userId = userId.ThrowIfEmpty(nameof(userId));
		_accountId = accountId.ThrowIfEmpty(nameof(accountId));
		_sessionToken = sessionToken.ThrowIfEmpty(nameof(sessionToken)).UnSecure();
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Flattrade/1.0");
	}

	public override string Name => nameof(Flattrade) + "_" + nameof(FlattradeRestClient);

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<FlattradeInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var pages = await Task.WhenAll(_instrumentFiles.Select(file => DownloadInstruments(file, cancellationToken)));
			_instruments = [.. pages.SelectMany(page => page)
				.GroupBy(instrument => instrument.Exchange.ToInstrumentKey(instrument.Token),
					StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())];
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

	public async Task<FlattradeInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public async Task<FlattradeInstrument> FindInstrument(string exchange, string tradingSymbol, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsBySymbol.TryGetValue(ToSymbolKey(exchange, tradingSymbol), out var instrument) ? instrument : null;
	}

	public async Task<string> PlaceOrder(FlattradePlaceOrderRequest order, CancellationToken cancellationToken)
	{
		var response = await SendObject<FlattradeOrderResult, FlattradePlaceOrderRequest>("PlaceOrder", order, cancellationToken);
		return response.OrderId.IsEmpty(response.Result).ThrowIfEmpty(nameof(response.OrderId));
	}

	public Task ModifyOrder(FlattradeModifyOrderRequest order, CancellationToken cancellationToken)
		=> SendStatus("ModifyOrder", order, cancellationToken);

	public Task CancelOrder(string orderId, CancellationToken cancellationToken)
		=> SendStatus("CancelOrder", new FlattradeCancelOrderRequest
		{
			UserId = _userId,
			OrderId = orderId.ThrowIfEmpty(nameof(orderId)),
		}, cancellationToken);

	public Task<FlattradeOrder[]> GetOrders(CancellationToken cancellationToken)
		=> SendArray<FlattradeOrder, FlattradeUserRequest>("OrderBook",
			new() { UserId = _userId }, cancellationToken, true);

	public Task<FlattradeTrade[]> GetTrades(CancellationToken cancellationToken)
		=> SendArray<FlattradeTrade, FlattradeAccountRequest>("TradeBook",
			new() { UserId = _userId, AccountId = _accountId }, cancellationToken, true);

	public Task<FlattradePosition[]> GetPositions(CancellationToken cancellationToken)
		=> SendArray<FlattradePosition, FlattradeAccountRequest>("PositionBook",
			new() { UserId = _userId, AccountId = _accountId }, cancellationToken, true);

	public Task<FlattradeHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> SendArray<FlattradeHolding, FlattradeAccountRequest>("Holdings",
			new() { UserId = _userId, AccountId = _accountId, Product = FlattradeProducts.Delivery.ToNative() },
			cancellationToken, true);

	public Task<FlattradeLimits> GetLimits(CancellationToken cancellationToken)
		=> SendObject<FlattradeLimits, FlattradeAccountRequest>("Limits",
			new() { UserId = _userId, AccountId = _accountId }, cancellationToken);

	public async Task<FlattradeCandle[]> GetCandles(FlattradeInstrument instrument, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var end = NormalizeUtc(to ?? DateTime.UtcNow);
		var start = NormalizeUtc(from ?? end.AddDays(timeFrame == TimeSpan.FromDays(1) ? -3650 : -30));
		if (start > end)
			return [];

		if (timeFrame == TimeSpan.FromDays(1))
		{
			var request = new FlattradeDailyCandleRequest
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
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Flattrade candle interval.");

		return await SendArray<FlattradeCandle, FlattradeCandleRequest>("TPSeries", new()
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
		=> await SendObject<FlattradeOrderResult, TRequest>(path, body, cancellationToken);

	private async Task<TResponse> SendObject<TResponse, TRequest>(string path, TRequest body,
		CancellationToken cancellationToken)
		where TResponse : FlattradeResponse
		where TRequest : class
	{
		var content = await SendRaw(path, body, cancellationToken);
		var response = JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
			?? throw new InvalidOperationException($"Flattrade returned an empty response for {path}.");
		EnsureSuccess(response, path);
		return response;
	}

	private async Task<TItem[]> SendArray<TItem, TRequest>(string path, TRequest body,
		CancellationToken cancellationToken, bool allowNoData)
		where TItem : FlattradeResponse
		where TRequest : class
	{
		var content = await SendRaw(path, body, cancellationToken);
		if (FirstToken(content) != '[')
		{
			var error = JsonConvert.DeserializeObject<FlattradeResponse>(content, _jsonSettings)
				?? throw new InvalidOperationException($"Flattrade returned an invalid response for {path}.");
			if (allowNoData && IsNoData(error))
				return [];
			EnsureSuccess(error, path);
			throw new InvalidOperationException($"Flattrade returned an unexpected object response for {path}.");
		}

		var items = JsonConvert.DeserializeObject<TItem[]>(content, _jsonSettings) ?? [];
		foreach (var item in items)
			EnsureSuccess(item, path);
		return items;
	}

	private async Task<FlattradeCandle[]> SendDailyCandles(FlattradeDailyCandleRequest body,
		CancellationToken cancellationToken)
	{
		const string path = "EODChartData";
		var content = await SendRaw(path, body, cancellationToken);
		if (FirstToken(content) != '[')
		{
			var error = JsonConvert.DeserializeObject<FlattradeResponse>(content, _jsonSettings)
				?? throw new InvalidOperationException("Flattrade returned an invalid daily-candle response.");
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
				.Select(value => JsonConvert.DeserializeObject<FlattradeCandle>(value, _jsonSettings))
				.Where(candle => candle != null)];
		}

		return JsonConvert.DeserializeObject<FlattradeCandle[]>(content, _jsonSettings) ?? [];
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
		this.AddVerboseLog("Flattrade POST {0}.", path);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"Flattrade {path} returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
		if (content.IsEmpty())
			throw new InvalidOperationException($"Flattrade returned an empty response for {path}.");
		return content;
	}

	private async Task<FlattradeInstrument[]> DownloadInstruments(string fileName,
		CancellationToken cancellationToken)
	{
		using var stream = await _httpClient.GetStreamAsync(_instrumentBaseUrl + fileName, cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);
		var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
		if (!await csv.NextLineAsync(cancellationToken))
			return [];

		var instruments = new List<FlattradeInstrument>();
		while (await csv.NextLineAsync(cancellationToken))
		{
			var instrument = ReadInstrument(csv);
			if (instrument != null)
				instruments.Add(instrument);
		}
		return [.. instruments];
	}

	private static FlattradeInstrument ReadInstrument(FastCsvReader csv)
	{
		var values = ReadColumns(csv, 9);
		var instrument = new FlattradeInstrument
		{
			Exchange = values[0],
			Token = values[1],
			LotSize = values[2].ToDecimal(),
			Symbol = values[3],
			TradingSymbol = values[4],
			Instrument = values[5],
			Expiry = ParseExpiry(values[6]),
			StrikePrice = values[7].ToDecimal(),
			OptionType = values[8],
		};

		if (instrument.Exchange.IsEmpty() || instrument.Token.IsEmpty() || instrument.TradingSymbol.IsEmpty())
			return null;
		instrument.LotSize = instrument.LotSize > 0 ? instrument.LotSize : 1;
		instrument.Multiplier = instrument.LotSize;
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

	private static bool IsNoData(FlattradeResponse response)
		=> response.ErrorMessage?.Contains("no data", StringComparison.OrdinalIgnoreCase) == true ||
			response.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true;

	private static void EnsureSuccess(FlattradeResponse response, string operation)
	{
		if (response == null)
			throw new InvalidOperationException($"Flattrade returned an empty response for {operation}.");
		if (response.Status.EqualsIgnoreCase("Ok") ||
			response.Status.IsEmpty() && response.ErrorMessage.IsEmpty())
			return;
		throw new InvalidOperationException($"Flattrade {operation} error: {response.ErrorMessage.IsEmpty(response.Status)}");
	}

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
