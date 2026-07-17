namespace StockSharp.FivePaisa.Native;

sealed class FivePaisaRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://Openapi.5paisa.com/VendorsAPI/Service1.svc/";
	private const string _historyUrl = "https://openapi.5paisa.com/";
	private const string _instrumentUrl = _apiUrl + "ScripMaster/segment/all";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _appKey;
	private readonly string _clientCode;
	private readonly SecureString _token;
	private readonly HttpClient _instrumentClient;
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private FivePaisaInstrument[] _instruments;
	private IReadOnlyDictionary<string, FivePaisaInstrument> _instrumentsByKey;

	public FivePaisaRestClient(string appKey, string clientCode, SecureString token)
	{
		_appKey = appKey.ThrowIfEmpty(nameof(appKey));
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		_token = token.ThrowIfEmpty(nameof(token));

		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
		};
		_instrumentClient = new(handler);
		_instrumentClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; StockSharp 5paisa connector)");
		_instrumentClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
	}

	public override string Name => nameof(FivePaisa) + "_" + nameof(FivePaisaRestClient);

	protected override void DisposeManaged()
	{
		_instrumentClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<FivePaisaInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			await using var stream = await _instrumentClient.GetStreamAsync(_instrumentUrl, cancellationToken);
			using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);
			var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
			if (!await csv.NextLineAsync(cancellationToken))
				return _instruments = [];

			var instruments = new List<FivePaisaInstrument>();
			while (await csv.NextLineAsync(cancellationToken))
			{
				var instrument = new FivePaisaInstrument
				{
					Exchange = csv.ReadString(),
					ExchangeType = csv.ReadString(),
					ScripCode = ParseLong(csv.ReadString()),
					Name = NullIfMissing(csv.ReadString()),
					Expiry = ParseDate(csv.ReadString()),
					ScripType = NullIfMissing(csv.ReadString()),
					StrikeRate = ParseDecimal(csv.ReadString()),
					FullName = NullIfMissing(csv.ReadString()),
					TickSize = ParseDecimal(csv.ReadString()),
					LotSize = ParseDecimal(csv.ReadString()),
					QuantityLimit = ParseDecimal(csv.ReadString()),
					Multiplier = ParseDecimal(csv.ReadString()),
					SymbolRoot = NullIfMissing(csv.ReadString()),
				};

				csv.ReadString();
				instrument.Isin = NullIfMissing(csv.ReadString());
				instrument.ScripData = NullIfMissing(csv.ReadString());
				instrument.Series = NullIfMissing(csv.ReadString());

				if (instrument.ScripCode <= 0 || instrument.Exchange.IsEmpty() || instrument.ExchangeType.IsEmpty())
					continue;

				try
				{
					instrument.Exchange.ToBoardCode(instrument.ExchangeType);
				}
				catch (ArgumentOutOfRangeException)
				{
					continue;
				}

				instruments.Add(instrument);
			}

			_instruments = [.. instruments];
			_instrumentsByKey = _instruments
				.GroupBy(i => i.Exchange.ToInstrumentKey(i.ExchangeType, i.ScripCode), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<FivePaisaInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public async Task<FivePaisaEquityMargin[]> GetMargin(CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaMarginBody, FivePaisaAccountRequest>("V4/Margin", new() { ClientCode = _clientCode }, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "margin");
		return body.EquityMargins ?? [];
	}

	public async Task<FivePaisaHolding[]> GetHoldings(CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaHoldingBody, FivePaisaAccountRequest>("V3/Holding", new() { ClientCode = _clientCode }, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "holdings", true);
		return body.Holdings ?? [];
	}

	public async Task<FivePaisaPosition[]> GetPositions(CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaPositionBody, FivePaisaAccountRequest>("V2/NetPositionNetWise", new() { ClientCode = _clientCode }, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "positions", true);
		return body.Positions ?? [];
	}

	public async Task<FivePaisaOrder[]> GetOrders(CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaOrderBookBody, FivePaisaAccountRequest>("V3/OrderBook", new() { ClientCode = _clientCode }, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "order book", true);
		return body.Orders ?? [];
	}

	public async Task<FivePaisaTrade[]> GetTrades(CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaTradeBookBody, FivePaisaAccountRequest>("V1/TradeBook", new() { ClientCode = _clientCode }, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "trade book", true);
		return body.Trades ?? [];
	}

	public async Task<FivePaisaOrderResult> PlaceOrder(FivePaisaOrderRequest order, CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaOrderResult, FivePaisaOrderRequest>("V1/PlaceOrderRequest", order, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "place order");
		return body;
	}

	public async Task<FivePaisaOrderResult> ModifyOrder(FivePaisaModifyOrderRequest order, CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaOrderResult, FivePaisaModifyOrderRequest>("V1/ModifyOrderRequest", order, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "modify order");
		return body;
	}

	public async Task<FivePaisaOrderResult> CancelOrder(string exchangeOrderId, CancellationToken cancellationToken)
	{
		var body = await Send<FivePaisaOrderResult, FivePaisaCancelOrderRequest>("V1/CancelOrderRequest", new()
		{
			ExchangeOrderId = exchangeOrderId.ThrowIfEmpty(nameof(exchangeOrderId)),
		}, cancellationToken);
		ValidateStatus(body?.Status ?? -1, body?.Message, "cancel order");
		return body;
	}

	public async Task<FivePaisaCandle[]> GetCandles(string instrumentKey, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var (exchange, exchangeType, scripCode) = instrumentKey.ParseInstrumentKey();
		var interval = timeFrame.ToCandleInterval();
		var utcEnd = (to ?? DateTime.UtcNow).ToUniversalTime();
		var utcStart = (from ?? (timeFrame == TimeSpan.FromDays(1) ? utcEnd.AddYears(-10) : utcEnd.AddMonths(-6))).ToUniversalTime();
		if (utcStart > utcEnd)
			return [];

		var localStart = utcStart.ToIndiaTime().Date;
		var localEnd = utcEnd.ToIndiaTime().Date;
		var candles = new List<FivePaisaCandle>();
		while (localStart <= localEnd)
		{
			var pageEnd = timeFrame == TimeSpan.FromDays(1) ? localEnd : localStart.AddMonths(6).AddDays(-1);
			if (pageEnd > localEnd)
				pageEnd = localEnd;

			var path = $"V2/historical/{exchange}/{exchangeType}/{scripCode.ToString(CultureInfo.InvariantCulture)}/{interval}";
			var request = CreateRequest(path, Method.Get);
			request.AddQueryParameter("from", localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			request.AddQueryParameter("end", pageEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			var response = await request.InvokeAsync<FivePaisaCandleResponse>(new Uri(new Uri(_historyUrl), path), this, this.AddVerboseLog, cancellationToken);
			if (response == null)
				throw new InvalidOperationException("5paisa returned an empty candle response.");
			if (response.Status?.Contains("error", StringComparison.OrdinalIgnoreCase) == true || response.Status?.Contains("fail", StringComparison.OrdinalIgnoreCase) == true)
				throw new InvalidOperationException($"5paisa candle API error: {response.Status}");

			candles.AddRange(response.Data?.Candles ?? []);
			localStart = pageEnd.AddDays(1);
		}

		return [.. candles
			.Where(c => c.OpenTime.ToFivePaisaTime() is DateTime time && time >= utcStart && time <= utcEnd)
			.GroupBy(c => c.OpenTime)
			.Select(g => g.Last())
			.OrderBy(c => c.OpenTime.ToFivePaisaTime())];
	}

	private async Task<TResponse> Send<TResponse, TRequest>(string path, TRequest body, CancellationToken cancellationToken)
		where TResponse : class
		where TRequest : class
	{
		var request = CreateRequest(path, Method.Post);
		request.AddStringBody(JsonConvert.SerializeObject(new FivePaisaRequest<TRequest>
		{
			Head = new() { Key = _appKey },
			Body = body,
		}, _jsonSettings), DataFormat.Json);

		var response = await request.InvokeAsync<FivePaisaResponse<TResponse>>(new Uri(new Uri(_apiUrl), path), this, this.AddVerboseLog, cancellationToken);
		if (response == null)
			throw new InvalidOperationException("5paisa returned an empty response.");
		if (response.Head != null && !response.Head.Status.IsEmpty() && response.Head.Status != "0" && !response.Head.Status.EqualsIgnoreCase("success"))
			throw new InvalidOperationException($"5paisa API error {response.Head.ResponseCode}: {response.Head.StatusDescription}");
		if (response.Body == null)
			throw new InvalidOperationException($"5paisa returned no response body for {response.Head?.ResponseCode ?? path}.");
		return response.Body;
	}

	private RestRequest CreateRequest(string path, Method method)
	{
		var request = new RestRequest(path, method);
		request.AddHeader("Accept", "application/json");
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("Authorization", $"Bearer {_token.UnSecure()}");
		return request;
	}

	private static void ValidateStatus(int status, string message, string operation, bool allowNoData = false)
	{
		if (allowNoData && status == 1)
			return;
		if (status != 0)
			throw new InvalidOperationException($"5paisa {operation} error {status}: {message}");
	}

	private static long ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

	private static decimal ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;

	private static DateTime? ParseDate(string value)
	{
		if (value.IsEmpty())
			return null;
		if (value.ToFivePaisaTime() is DateTime timestamp)
			return timestamp;
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
			return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
		return null;
	}

	private static string NullIfMissing(string value)
		=> value.IsEmpty() || value.EqualsIgnoreCase("NA") || value.EqualsIgnoreCase("NULL") ? null : value;
}
