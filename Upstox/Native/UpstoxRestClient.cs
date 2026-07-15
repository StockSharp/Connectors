namespace StockSharp.Upstox.Native;

sealed class UpstoxRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://api.upstox.com";
	private const string _instrumentUrl = "https://assets.upstox.com/market-quote/instruments/exchange/complete.json.gz";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly SecureString _token;
	private readonly string _orderUrl;
	private readonly System.Net.Http.HttpClient _instrumentClient = new();
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private UpstoxInstrument[] _instruments;

	public UpstoxRestClient(bool isDemo, SecureString token)
	{
		_token = token.ThrowIfEmpty(nameof(token));
		_orderUrl = isDemo ? "https://api-sandbox.upstox.com" : "https://api-hft.upstox.com";
	}

	public override string Name => nameof(Upstox) + "_" + nameof(UpstoxRestClient);

	protected override void DisposeManaged()
	{
		_instrumentClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<UpstoxInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			await using var compressed = await _instrumentClient.GetStreamAsync(_instrumentUrl, cancellationToken);
			await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
			using var streamReader = new StreamReader(gzip, Encoding.UTF8);
			using var jsonReader = new JsonTextReader(streamReader);

			return _instruments = JsonSerializer.CreateDefault(_jsonSettings).Deserialize<UpstoxInstrument[]>(jsonReader) ?? [];
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<UpstoxProfile> GetProfile(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxProfile>>("/v2/user/profile", cancellationToken)).Data;

	public async Task<UpstoxFunds> GetFunds(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxFunds>>("/v3/user/get-funds-and-margin", cancellationToken)).Data;

	public async Task<UpstoxPosition[]> GetPositions(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxPosition[]>>("/v2/portfolio/short-term-positions", cancellationToken)).Data ?? [];

	public async Task<UpstoxHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxHolding[]>>("/v2/portfolio/long-term-holdings", cancellationToken)).Data ?? [];

	public async Task<UpstoxOrder[]> GetOrders(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxOrder[]>>("/v2/order/retrieve-all", cancellationToken)).Data ?? [];

	public async Task<UpstoxTrade[]> GetTrades(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxTrade[]>>("/v2/order/trades/get-trades-for-day", cancellationToken)).Data ?? [];

	public async Task<UpstoxCandle[]> GetCandles(string instrumentKey, TimeSpan timeFrame, DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var (unit, interval) = timeFrame.ToNative();
		var end = (to ?? DateTime.UtcNow).Date;

		if (from is null)
			return await GetCandlePage(instrumentKey, unit, interval, null, end, cancellationToken);

		var start = from.Value.Date;
		if (start > end)
			return [];

		var candles = new List<UpstoxCandle>();
		while (start <= end)
		{
			var pageEnd = unit switch
			{
				"minutes" when interval <= 15 => start.AddMonths(1).AddDays(-1),
				"minutes" or "hours" => start.AddMonths(3).AddDays(-1),
				"days" => start.AddYears(10).AddDays(-1),
				_ => end,
			};
			if (pageEnd > end)
				pageEnd = end;

			candles.AddRange(await GetCandlePage(instrumentKey, unit, interval, start, pageEnd, cancellationToken));
			start = pageEnd.AddDays(1);
		}

		return [.. candles.GroupBy(c => c.Time).Select(g => g.Last()).OrderBy(c => c.Time)];
	}

	private async Task<UpstoxCandle[]> GetCandlePage(string instrumentKey, string unit, int interval, DateTime? from, DateTime to, CancellationToken cancellationToken)
	{
		var path = $"/v3/historical-candle/{Uri.EscapeDataString(instrumentKey)}/{unit}/{interval}/{to:yyyy-MM-dd}";
		if (from is not null)
			path += $"/{from:yyyy-MM-dd}";

		return (await Get<UpstoxResponse<UpstoxCandleData>>(path, cancellationToken)).Data?.Candles ?? [];
	}

	public async Task<string> GetMarketDataStreamUrl(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxWebSocketData>>("/v3/feed/market-data-feed/authorize", cancellationToken)).Data?.AuthorizedRedirectUri
			?? throw new InvalidOperationException("Upstox did not return a market-data WebSocket URL.");

	public async Task<string> GetPortfolioStreamUrl(CancellationToken cancellationToken)
		=> (await Get<UpstoxResponse<UpstoxWebSocketData>>("/v2/feed/portfolio-stream-feed/authorize?update_types=order%2Cposition%2Cholding", cancellationToken)).Data?.AuthorizedRedirectUri
			?? throw new InvalidOperationException("Upstox did not return a portfolio WebSocket URL.");

	public async Task<string[]> PlaceOrder(UpstoxPlaceOrderRequest body, CancellationToken cancellationToken)
		=> (await Send<UpstoxResponse<UpstoxOrderIds>>(_orderUrl, "/v3/order/place", Method.Post, body, cancellationToken)).Data?.OrderIds ?? [];

	public async Task<string> ModifyOrder(UpstoxModifyOrderRequest body, CancellationToken cancellationToken)
		=> (await Send<UpstoxResponse<UpstoxOrderId>>(_orderUrl, "/v3/order/modify", Method.Put, body, cancellationToken)).Data?.OrderId;

	public async Task<string> CancelOrder(string orderId, CancellationToken cancellationToken)
		=> (await Send<UpstoxResponse<UpstoxOrderId>>(_orderUrl, $"/v3/order/cancel?order_id={Uri.EscapeDataString(orderId)}", Method.Delete, null, cancellationToken)).Data?.OrderId;

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(_apiUrl, path, Method.Get, null, cancellationToken);

	private Task<T> Send<T>(string baseUrl, string path, Method method, object body, CancellationToken cancellationToken)
	{
		var request = new RestRequest((string)null, method);
		request.SetBearer(_token);
		request.AddHeader("Accept", "application/json");
		request.AddHeader("Api-Version", "2.0");

		if (body != null)
			request.AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json);

		return request.InvokeAsync<T>(new Uri(new Uri(baseUrl), path), this, this.AddVerboseLog, cancellationToken);
	}
}
