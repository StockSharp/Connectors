namespace StockSharp.Etoro.Native;

sealed class EtoroRestClient : BaseLogReceiver
{
	private static readonly Uri _apiRoot = new("https://public-api.etoro.com/");
	private const string _instrumentFields = "instrumentId,displayname,instrumentTypeID,instrumentType,exchangeID," +
		"isOpen,internalSymbolFull,internalExchangeName,internalAssetClassName,isDelisted,isCurrentlyTradable," +
		"isExchangeOpen,isBuyEnabled,currentRate";

	private readonly HttpClient _http = new();
	private readonly string _apiKey;
	private readonly string _userKey;
	private readonly int _maxAttempts;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
		Converters = [new StringEnumConverter()],
	};

	public EtoroRestClient(string apiKey, string userKey, int maxAttempts)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_userKey = userKey.ThrowIfEmpty(nameof(userKey));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public async Task Connect(CancellationToken cancellationToken)
	{
		_ = await SearchInstruments(null, null, 1, 1, cancellationToken);
	}

	public Task<EtoroInstrumentSearchResponse> SearchInstruments(string symbol, string name, int pageNumber,
		int pageSize, CancellationToken cancellationToken)
	{
		var query = new List<string>
		{
			$"pageSize={Math.Clamp(pageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)}",
			$"pageNumber={Math.Max(1, pageNumber).ToString(CultureInfo.InvariantCulture)}",
			$"fields={Escape(_instrumentFields)}",
		};
		if (!symbol.IsEmpty())
			query.Add($"internalSymbolFull={Escape(symbol)}");
		else if (!name.IsEmpty())
			query.Add($"displayname={Escape(name)}");
		return Get<EtoroInstrumentSearchResponse>($"api/v1/market-data/search?{string.Join("&", query)}", cancellationToken);
	}

	public Task<EtoroInstrumentSearchResponse> SearchInstrument(int instrumentId, CancellationToken cancellationToken)
	{
		if (instrumentId <= 0)
			throw new ArgumentOutOfRangeException(nameof(instrumentId), instrumentId, null);

		return Get<EtoroInstrumentSearchResponse>("api/v1/market-data/search?pageSize=1&pageNumber=1" +
			$"&fields={Escape(_instrumentFields)}&instrumentId={instrumentId.ToString(CultureInfo.InvariantCulture)}",
			cancellationToken);
	}

	public Task<EtoroInstrumentDisplaysResponse> GetInstruments(IEnumerable<int> instrumentIds,
		CancellationToken cancellationToken)
	{
		var ids = instrumentIds?.Where(id => id > 0).Distinct().ToArray() ?? [];
		if (ids.Length == 0)
			throw new ArgumentOutOfRangeException(nameof(instrumentIds));

		return Get<EtoroInstrumentDisplaysResponse>("api/v1/market-data/instruments?instrumentIds=" +
			ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).JoinComma(), cancellationToken);
	}

	public Task<EtoroLiveRatesResponse> GetRates(IEnumerable<int> instrumentIds, CancellationToken cancellationToken)
	{
		var ids = instrumentIds?.Distinct().Take(100).ToArray() ?? [];
		if (ids.Length == 0)
			throw new ArgumentOutOfRangeException(nameof(instrumentIds));
		return Get<EtoroLiveRatesResponse>("api/v1/market-data/instruments/rates?instrumentIds=" +
			ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).JoinComma(), cancellationToken);
	}

	public Task<EtoroCandlesResponse> GetCandles(int instrumentId, EtoroCandleDirections direction,
		EtoroCandleIntervals interval, int count, CancellationToken cancellationToken)
		=> Get<EtoroCandlesResponse>($"api/v1/market-data/instruments/{instrumentId.ToString(CultureInfo.InvariantCulture)}" +
			$"/history/candles/{direction.ToNative()}/{interval.ToNative()}/" +
			Math.Clamp(count, 1, 1000).ToString(CultureInfo.InvariantCulture), cancellationToken);

	public Task<EtoroPortfolioResponse> GetPortfolio(bool isDemo, CancellationToken cancellationToken)
		=> Get<EtoroPortfolioResponse>(isDemo
			? "api/v1/trading/info/demo/pnl"
			: "api/v1/trading/info/real/pnl", cancellationToken);

	public Task<EtoroTradeHistoryItem[]> GetTradeHistory(bool isDemo, DateTime minDate, int page, int pageSize,
		CancellationToken cancellationToken)
	{
		var path = isDemo ? "api/v1/trading/info/trade/demo/history" : "api/v1/trading/info/trade/history";
		return Get<EtoroTradeHistoryItem[]>($"{path}?minDate={Escape(minDate.UtcKind().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}" +
			$"&page={Math.Max(1, page).ToString(CultureInfo.InvariantCulture)}" +
			$"&pageSize={Math.Clamp(pageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)}", cancellationToken);
	}

	public Task<EtoroUnifiedOrderResponse> PlaceOrder(bool isDemo, EtoroUnifiedOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<EtoroUnifiedOrderResponse>(HttpMethod.Post, isDemo
			? "api/v2/trading/execution/demo/orders"
			: "api/v2/trading/execution/orders", request, cancellationToken);

	public Task CancelOrder(bool isDemo, long orderId, CancellationToken cancellationToken)
		=> Send<EtoroEmptyResponse>(HttpMethod.Delete, (isDemo
			? "api/v2/trading/execution/demo/orders/"
			: "api/v2/trading/execution/orders/") + orderId.ToString(CultureInfo.InvariantCulture), null, cancellationToken);

	public Task<EtoroOrderInfoResponse> GetOrderInfo(bool isDemo, long orderId, CancellationToken cancellationToken)
		=> Get<EtoroOrderInfoResponse>((isDemo
			? "api/v2/trading/info/demo/orders:lookup?orderId="
			: "api/v2/trading/info/orders:lookup?orderId=") + orderId.ToString(CultureInfo.InvariantCulture), cancellationToken);

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid().ToString();
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, new Uri(_apiRoot, path));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.Add("x-request-id", requestId);
			request.Headers.Add("x-api-key", _apiKey);
			request.Headers.Add("x-user-key", _userKey);
			if (body != null)
				request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");

			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);

			if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}

			if ((int)response.StatusCode >= 500 && method == HttpMethod.Get && attempt < _maxAttempts)
			{
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5))), cancellationToken);
				continue;
			}

			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content);

			if (typeof(T) == typeof(EtoroEmptyResponse) && content.IsEmpty())
				return (T)(object)new EtoroEmptyResponse();

			return Deserialize<T>(content) ?? throw new InvalidOperationException($"eToro {path} returned an empty response.");
		}
	}

	private T Deserialize<T>(string content)
		=> content.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(content, _jsonSettings);

	private Exception CreateError(HttpStatusCode statusCode, string content)
	{
		EtoroErrorResponse error = null;
		try
		{
			error = Deserialize<EtoroErrorResponse>(content);
		}
		catch (JsonException)
		{
		}

		var message = error?.ErrorMessage.IsEmpty(error?.Detail).IsEmpty(error?.Message).IsEmpty(error?.Title).IsEmpty(content);
		if (message?.Length > 1000)
			message = message[..1000];
		return new HttpRequestException($"eToro API error {(int)statusCode}" +
			(error?.ErrorCode.IsEmpty() == false ? $"/{error.ErrorCode}" : string.Empty) +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, statusCode);
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is { } delta)
			return delta < TimeSpan.Zero ? TimeSpan.Zero : delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
		if (response.Headers.RetryAfter?.Date is { } date)
		{
			var delay = date.UtcDateTime - DateTime.UtcNow;
			return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
		}
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
