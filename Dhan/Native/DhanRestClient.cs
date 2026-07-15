namespace StockSharp.Dhan.Native;

sealed class DhanRestClient : BaseLogReceiver
{
	private const string _apiUrl = "https://api.dhan.co/v2/";
	private const string _instrumentUrl = "https://images.dhan.co/api-data/api-scrip-master-detailed.csv";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _clientId;
	private readonly SecureString _token;
	private readonly HttpClient _instrumentClient = new();
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private DhanInstrument[] _instruments;
	private IReadOnlyDictionary<string, DhanInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, DhanInstrument> _equitiesByIsin;
	private IReadOnlyDictionary<string, DhanInstrument> _equitiesByExchangeAndId;
	private IReadOnlyDictionary<string, DhanInstrument> _equitiesById;

	public DhanRestClient(string clientId, SecureString token)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_token = token.ThrowIfEmpty(nameof(token));
	}

	public override string Name => nameof(Dhan) + "_" + nameof(DhanRestClient);

	protected override void DisposeManaged()
	{
		_instrumentClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<DhanInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			await using var stream = await _instrumentClient.GetStreamAsync(_instrumentUrl, cancellationToken);
			using var streamReader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);
			var csv = new FastCsvReader(streamReader, StringHelper.N) { ColumnSeparator = ',' };
			if (!await csv.NextLineAsync(cancellationToken))
				return _instruments = [];

			var instruments = new List<DhanInstrument>();
			while (await csv.NextLineAsync(cancellationToken))
			{
				var instrument = new DhanInstrument
				{
					Exchange = csv.ReadString(),
					Segment = csv.ReadString(),
					SecurityId = csv.ReadString(),
					Isin = NullIfNotAvailable(csv.ReadString()),
					Instrument = csv.ReadString(),
					UnderlyingSecurityId = NullIfNotAvailable(csv.ReadString()),
					UnderlyingSymbol = NullIfNotAvailable(csv.ReadString()),
					SymbolName = NullIfNotAvailable(csv.ReadString()),
					DisplayName = NullIfNotAvailable(csv.ReadString()),
					InstrumentType = NullIfNotAvailable(csv.ReadString()),
					Series = NullIfNotAvailable(csv.ReadString()),
					LotSize = ParseDecimal(csv.ReadString()),
					ExpiryDate = ParseDate(csv.ReadString()),
					StrikePrice = ParseDecimal(csv.ReadString()),
					OptionType = NullIfNotAvailable(csv.ReadString()),
					TickSize = ParseDecimal(csv.ReadString()),
				};

				if (instrument.SecurityId.IsEmpty() || instrument.Exchange.IsEmpty() || instrument.Segment.IsEmpty())
					continue;

				try
				{
					instrument.Exchange.ToBoardCode(instrument.Segment);
				}
				catch (ArgumentOutOfRangeException)
				{
					continue;
				}

				instruments.Add(instrument);
			}

			_instruments = [.. instruments];
			_instrumentsByKey = _instruments
				.GroupBy(i => i.Exchange.ToBoardCode(i.Segment).ToInstrumentKey(i.SecurityId), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var equities = _instruments.Where(i => i.Instrument.EqualsIgnoreCase("EQUITY")).ToArray();
			_equitiesByIsin = equities
				.Where(i => !i.Isin.IsEmpty())
				.GroupBy(i => i.Isin, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			_equitiesByExchangeAndId = equities
				.GroupBy(i => $"{i.Exchange}|{i.SecurityId}", StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			_equitiesById = equities
				.GroupBy(i => i.SecurityId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<DhanInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument) ? instrument : null;
	}

	public async Task<DhanInstrument> GetEquityInstrument(string securityId, string isin, string exchange, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		if (!isin.IsEmpty() && _equitiesByIsin.TryGetValue(isin, out var instrument))
			return instrument;
		if (!exchange.IsEmpty() && !exchange.EqualsIgnoreCase("ALL") && _equitiesByExchangeAndId.TryGetValue($"{exchange}|{securityId}", out instrument))
			return instrument;
		return _equitiesById.TryGetValue(securityId, out instrument) ? instrument : null;
	}

	public Task<DhanFunds> GetFunds(CancellationToken cancellationToken)
		=> Send<DhanFunds>("/fundlimit", Method.Get, cancellationToken);

	public async Task<DhanPosition[]> GetPositions(CancellationToken cancellationToken)
		=> await Send<DhanPosition[]>("/positions", Method.Get, cancellationToken) ?? [];

	public async Task<DhanHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> await Send<DhanHolding[]>("/holdings", Method.Get, cancellationToken) ?? [];

	public async Task<DhanOrder[]> GetOrders(CancellationToken cancellationToken)
		=> await Send<DhanOrder[]>("/orders", Method.Get, cancellationToken) ?? [];

	public async Task<DhanForeverOrder[]> GetForeverOrders(CancellationToken cancellationToken)
		=> await Send<DhanForeverOrder[]>("/forever/orders", Method.Get, cancellationToken) ?? [];

	public async Task<DhanTrade[]> GetTrades(CancellationToken cancellationToken)
		=> await Send<DhanTrade[]>("/trades", Method.Get, cancellationToken) ?? [];

	public Task<DhanOrderResult> PlaceOrder(DhanOrderRequest request, CancellationToken cancellationToken)
		=> Send<DhanOrderResult, DhanOrderRequest>("/orders", Method.Post, request, cancellationToken);

	public Task<DhanOrderResult> ModifyOrder(string orderId, DhanModifyOrderRequest request, CancellationToken cancellationToken)
		=> Send<DhanOrderResult, DhanModifyOrderRequest>($"/orders/{Uri.EscapeDataString(orderId)}", Method.Put, request, cancellationToken);

	public Task<DhanOrderResult> CancelOrder(string orderId, CancellationToken cancellationToken)
		=> Send<DhanOrderResult>($"/orders/{Uri.EscapeDataString(orderId)}", Method.Delete, cancellationToken);

	public Task<DhanOrderResult> PlaceForeverOrder(DhanForeverOrderRequest request, CancellationToken cancellationToken)
		=> Send<DhanOrderResult, DhanForeverOrderRequest>("/forever/orders", Method.Post, request, cancellationToken);

	public Task<DhanOrderResult> ModifyForeverOrder(string orderId, DhanForeverModifyRequest request, CancellationToken cancellationToken)
		=> Send<DhanOrderResult, DhanForeverModifyRequest>($"/forever/orders/{Uri.EscapeDataString(orderId)}", Method.Put, request, cancellationToken);

	public Task<DhanOrderResult> CancelForeverOrder(string orderId, CancellationToken cancellationToken)
		=> Send<DhanOrderResult>($"/forever/orders/{Uri.EscapeDataString(orderId)}", Method.Delete, cancellationToken);

	public async Task<DhanCandle[]> GetCandles(string instrumentKey, string instrumentType, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var (boardCode, securityId) = instrumentKey.ParseInstrumentKey();
		var end = (to ?? DateTime.UtcNow).ToIndiaTime();
		var isDaily = timeFrame == TimeSpan.FromDays(1);
		var start = (from ?? end.AddYears(isDaily ? -30 : 0).AddDays(isDaily ? 0 : -90)).ToIndiaTime();
		if (start > end)
			return [];
		if (start == end)
			end = end.Add(timeFrame);

		var candles = new List<DhanCandle>();
		while (start <= end)
		{
			var pageEnd = isDaily ? start.AddYears(10) : start.AddDays(90);
			if (pageEnd > end)
				pageEnd = end;
			if (pageEnd <= start)
				break;

			var request = new DhanHistoryRequest
			{
				ClientId = _clientId,
				SecurityId = securityId,
				ExchangeSegment = boardCode,
				Instrument = instrumentType,
				Interval = isDaily ? null : timeFrame.ToNativeMinutes(),
				ExpiryCode = isDaily ? 0 : null,
				IncludeOpenInterest = true,
				From = isDaily ? start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
				To = isDaily ? pageEnd.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : pageEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
			};

			var data = await Send<DhanCandleData, DhanHistoryRequest>(isDaily ? "/charts/historical" : "/charts/intraday", Method.Post, request, cancellationToken);
			candles.AddRange(ToCandles(data));
			start = isDaily ? pageEnd.AddDays(1) : pageEnd.AddSeconds(1);
		}

		return [.. candles.GroupBy(c => c.Time).Select(g => g.Last()).OrderBy(c => c.Time)];
	}

	private static IEnumerable<DhanCandle> ToCandles(DhanCandleData data)
	{
		if (data?.Timestamp == null || data.Open == null || data.High == null || data.Low == null || data.Close == null)
			yield break;

		var count = new[] { data.Timestamp.Length, data.Open.Length, data.High.Length, data.Low.Length, data.Close.Length }.Min();
		for (var i = 0; i < count; i++)
		{
			yield return new DhanCandle
			{
				Time = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp[i]).UtcDateTime,
				Open = data.Open[i],
				High = data.High[i],
				Low = data.Low[i],
				Close = data.Close[i],
				Volume = data.Volume?.ElementAtOrDefault(i) ?? 0,
				OpenInterest = data.OpenInterest != null && i < data.OpenInterest.Length ? data.OpenInterest[i] : null,
			};
		}
	}

	private Task<T> Send<T>(string path, Method method, CancellationToken cancellationToken)
		=> Send<T, DhanNoRequest>(path, method, null, cancellationToken);

	private Task<TResponse> Send<TResponse, TRequest>(string path, Method method, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
	{
		var request = new RestRequest((string)null, method);
		request.AddHeader("Accept", "application/json");
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("access-token", _token.UnSecure());
		request.AddHeader("client-id", _clientId);
		if (body != null)
			request.AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json);

		return request.InvokeAsync<TResponse>(new Uri(new Uri(_apiUrl), path.TrimStart('/')), this, this.AddVerboseLog, cancellationToken);
	}

	private static string NullIfNotAvailable(string value)
		=> value.IsEmpty() || value.EqualsIgnoreCase("NA") ? null : value;

	private static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	private static DateTime? ParseDate(string value)
		=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
			? DateTime.SpecifyKind(result, DateTimeKind.Utc)
			: null;
}
