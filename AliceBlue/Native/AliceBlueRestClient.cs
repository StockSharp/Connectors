namespace StockSharp.AliceBlue.Native;

sealed class AliceBlueRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://a3.aliceblueonline.com/";
	private const string _masterUrl = "https://v2api.aliceblueonline.com/restpy/static/contract_master/V2/";
	private static readonly string[] _segments = ["NSE", "BSE", "NFO", "BFO", "CDS", "BCD", "MCX", "INDICES"];
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _userId;
	private readonly string _sessionToken;
	private readonly HttpClient _httpClient = new() { BaseAddress = new(_apiUrl) };
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private AliceBlueInstrument[] _instruments;
	private IReadOnlyDictionary<string, AliceBlueInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, AliceBlueInstrument> _instrumentsBySymbol;

	public AliceBlueRestClient(string userId, SecureString sessionToken)
	{
		_userId = userId.ThrowIfEmpty(nameof(userId));
		_sessionToken = NormalizeToken(sessionToken.ThrowIfEmpty(nameof(sessionToken)).UnSecure());
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-AliceBlue/1.0");
	}

	public override string Name => nameof(AliceBlue) + "_" + nameof(AliceBlueRestClient);

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public Task<AliceBlueProfile> GetProfile(CancellationToken cancellationToken)
		=> GetSingle<AliceBlueProfile>("open-api/od/v1/profile/", cancellationToken);

	public async Task CreateMarketSession(CancellationToken cancellationToken)
	{
		var result = await Send<AliceBlueSocketSessionResult[], AliceBlueSocketSessionRequest>(
			"open-api/od/v1/profile/createWsSess", HttpMethod.Post,
			new() { UserId = _userId }, cancellationToken);
		if (result?.FirstOrDefault()?.Status.EqualsIgnoreCase("OK") != true)
			throw new InvalidOperationException("Alice Blue did not create a market WebSocket session.");
	}

	public async Task InvalidateMarketSession(CancellationToken cancellationToken)
	{
		var result = await Send<AliceBlueSocketSessionResult[], AliceBlueSocketSessionRequest>(
			"open-api/od/v1/profile/invalidateWsSess", HttpMethod.Post,
			new() { UserId = _userId }, cancellationToken);
		if (result?.FirstOrDefault()?.Status.EqualsIgnoreCase("OK") != true)
			this.AddWarningLog("Alice Blue did not confirm market WebSocket session invalidation.");
	}

	public async Task<string> GetOrderToken(CancellationToken cancellationToken)
	{
		var result = await Send<AliceBlueOrderToken[], object>(
			"open-api/order-notify/ws/createWsToken", HttpMethod.Get, null, cancellationToken);
		var token = result?.FirstOrDefault()?.Token;
		return token.ThrowIfEmpty(nameof(AliceBlueOrderToken.Token));
	}

	public async Task<AliceBlueInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var pages = await Task.WhenAll(_segments.Select(segment => DownloadInstruments(segment, cancellationToken)));
			_instruments = [.. pages.SelectMany(page => page)
				.Where(instrument => instrument != null && !instrument.Exchange.IsEmpty() && !instrument.Token.IsEmpty())
				.GroupBy(instrument => instrument.Exchange.ToInstrumentKey(instrument.Token), StringComparer.OrdinalIgnoreCase)
				.Select(group => group.Last())];
			_instrumentsByKey = _instruments.ToDictionary(
				instrument => instrument.Exchange.ToInstrumentKey(instrument.Token),
				StringComparer.OrdinalIgnoreCase);
			_instrumentsBySymbol = _instruments
				.Where(instrument => !instrument.TradingSymbol.IsEmpty())
				.GroupBy(instrument => ToSymbolKey(instrument.Exchange, instrument.TradingSymbol), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<AliceBlueInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public async Task<AliceBlueInstrument> FindInstrument(string exchange, string tradingSymbol,
		CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsBySymbol.TryGetValue(ToSymbolKey(exchange, tradingSymbol), out var instrument)
			? instrument
			: null;
	}

	public async Task<string> PlaceOrder(AliceBlueOrderRequest order, CancellationToken cancellationToken)
	{
		var result = await Send<AliceBlueOrderResult[], AliceBlueOrderRequest[]>(
			"open-api/od/v1/orders/placeorder", HttpMethod.Post, [order], cancellationToken);
		var orderId = result?.FirstOrDefault()?.OrderId;
		return orderId.ThrowIfEmpty(nameof(AliceBlueOrderResult.OrderId));
	}

	public Task ModifyOrder(AliceBlueModifyOrderRequest order, CancellationToken cancellationToken)
		=> SendStatus("open-api/od/v1/orders/modify", order, cancellationToken);

	public Task CancelOrder(string orderId, CancellationToken cancellationToken)
		=> SendStatus("open-api/od/v1/orders/cancel",
			new AliceBlueCancelOrderRequest { OrderId = orderId.ThrowIfEmpty(nameof(orderId)) }, cancellationToken);

	public Task<AliceBlueOrder[]> GetOrders(CancellationToken cancellationToken)
		=> GetArray<AliceBlueOrder>("open-api/od/v1/orders/book", cancellationToken);

	public Task<AliceBlueTrade[]> GetTrades(CancellationToken cancellationToken)
		=> GetArray<AliceBlueTrade>("open-api/od/v1/orders/trades", cancellationToken);

	public Task<AliceBluePosition[]> GetPositions(CancellationToken cancellationToken)
		=> GetArray<AliceBluePosition>("open-api/od/v1/positions", cancellationToken);

	public async Task<AliceBlueHolding[]> GetHoldings(CancellationToken cancellationToken)
	{
		var cnc = await GetArray<AliceBlueHolding>("open-api/od/v1/holdings/CNC", cancellationToken, true);
		var mtf = await GetArray<AliceBlueHolding>("open-api/od/v1/holdings/MTF", cancellationToken, true);
		return [.. cnc.Concat(mtf)];
	}

	public Task<AliceBlueLimits> GetLimits(CancellationToken cancellationToken)
		=> GetSingle<AliceBlueLimits>("open-api/od/v1/limits/", cancellationToken);

	public async Task<AliceBlueCandle[]> GetCandles(AliceBlueInstrument instrument, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		if (instrument.Exchange.ToUpperInvariant() is not ("NSE" or "NFO" or "CDS" or "MCX"))
			throw new NotSupportedException($"Alice Blue history is not documented for {instrument.Exchange}.");
		var resolution = timeFrame == TimeSpan.FromMinutes(1) ? "1"
			: timeFrame == TimeSpan.FromDays(1) ? "1D"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Alice Blue history supports one-minute and daily candles.");
		var end = NormalizeUtc(to ?? DateTime.UtcNow);
		var start = NormalizeUtc(from ?? end.AddDays(timeFrame == TimeSpan.FromDays(1) ? -730 : -30));
		if (start > end)
			return [];

		var response = await SendRaw("open-api/od/ChartAPIService/api/chart/history", HttpMethod.Post,
			new AliceBlueHistoryRequest
			{
				Token = instrument.Token,
				Resolution = resolution,
				From = start.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture),
				To = end.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture),
				Exchange = instrument.Exchange,
			}, cancellationToken);
		var history = JsonConvert.DeserializeObject<AliceBlueHistoryResponse>(response, _jsonSettings)
			?? throw new InvalidOperationException("Alice Blue returned an empty history response.");
		if (!history.Status.EqualsIgnoreCase("Ok"))
			throw new InvalidOperationException($"Alice Blue history error: {history.ErrorMessage.IsEmpty(history.Status)}");
		return history.Result ?? [];
	}

	private async Task SendStatus<TRequest>(string path, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
		=> await Send<AliceBlueOrderResult[], TRequest>(path, HttpMethod.Post, body, cancellationToken);

	private async Task<TItem[]> GetArray<TItem>(string path, CancellationToken cancellationToken, bool allowNoData = false)
		where TItem : class
		=> await Send<TItem[], object>(path, HttpMethod.Get, null, cancellationToken, allowNoData) ?? [];

	private async Task<TItem> GetSingle<TItem>(string path, CancellationToken cancellationToken)
		where TItem : class
	{
		var items = await GetArray<TItem>(path, cancellationToken);
		return items.FirstOrDefault()
			?? throw new InvalidOperationException($"Alice Blue returned no data for {path}.");
	}

	private async Task<TResult> Send<TResult, TRequest>(string path, HttpMethod method, TRequest body,
		CancellationToken cancellationToken, bool allowNoData = false)
		where TRequest : class
	{
		var content = await SendRaw(path, method, body, cancellationToken);
		var response = JsonConvert.DeserializeObject<AliceBlueResponse<TResult>>(content, _jsonSettings)
			?? throw new InvalidOperationException($"Alice Blue returned an empty response for {path}.");
		if (!IsSuccess(response))
		{
			if (allowNoData && IsNoData(response))
				return default;
			throw CreateError(path, response);
		}
		return response.Result;
	}

	private async Task<string> SendRaw<TRequest>(string path, HttpMethod method, TRequest body,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		using var request = new HttpRequestMessage(method, path);
		if (body != null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings),
				Encoding.UTF8, "application/json");
		this.AddVerboseLog("Alice Blue {0} {1}.", method.Method, path);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"Alice Blue {path} returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
		if (content.IsEmpty())
			throw new InvalidOperationException($"Alice Blue returned an empty response for {path}.");
		return content;
	}

	private async Task<AliceBlueInstrument[]> DownloadInstruments(string segment,
		CancellationToken cancellationToken)
	{
		var content = await _httpClient.GetStringAsync($"{_masterUrl}{segment}", cancellationToken);
		var envelope = JsonConvert.DeserializeObject<AliceBlueInstrumentEnvelope>(content, _jsonSettings)
			?? throw new InvalidDataException($"Alice Blue {segment} contract master is empty.");
		var instruments = segment switch
		{
			"NSE" => envelope.Nse,
			"BSE" => envelope.Bse,
			"NFO" => envelope.Nfo,
			"BFO" => envelope.Bfo,
			"CDS" => envelope.Cds,
			"BCD" => envelope.Bcd,
			"MCX" => envelope.Mcx,
			"INDICES" => envelope.Nse,
			_ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
		} ?? [];

		if (segment == "INDICES")
		{
			foreach (var instrument in instruments)
			{
				instrument.Exchange = "NSE";
				instrument.TradingSymbol = instrument.Symbol;
				instrument.FormattedName = instrument.Symbol;
				instrument.LotSize = "1";
				instrument.IsIndex = true;
			}
		}
		return instruments;
	}

	private static bool IsSuccess<TResult>(AliceBlueResponse<TResult> response)
		=> response.Status.EqualsIgnoreCase("Ok") || response.LegacyStatus.EqualsIgnoreCase("Ok");

	private static bool IsNoData<TResult>(AliceBlueResponse<TResult> response)
		=> response.Message?.Contains("no data", StringComparison.OrdinalIgnoreCase) == true ||
			response.ErrorMessage?.Contains("no data", StringComparison.OrdinalIgnoreCase) == true ||
			response.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true;

	private static InvalidOperationException CreateError<TResult>(string operation, AliceBlueResponse<TResult> response)
		=> new($"Alice Blue {operation} error: {response.ErrorMessage.IsEmpty(response.Message).IsEmpty(response.Status).IsEmpty(response.LegacyStatus)}");

	private static string ToSymbolKey(string exchange, string tradingSymbol)
		=> $"{exchange?.ToUpperInvariant()}|{tradingSymbol?.ToUpperInvariant()}";

	private static string NormalizeToken(string token)
		=> token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token[7..].Trim() : token.Trim();

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
