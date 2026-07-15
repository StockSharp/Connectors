namespace StockSharp.Fyers.Native;

sealed class FyersRestClient : BaseLogReceiver
{
	private const string _baseUrl = "https://api-t1.fyers.in/";
	private const string _defaultTbtUrl = "wss://rtsocket-api.fyers.in/versova";

	private static readonly (string boardCode, string url)[] _masterFiles =
	[
		("NSE", "https://public.fyers.in/sym_details/NSE_CM.csv"),
		("NFO", "https://public.fyers.in/sym_details/NSE_FO.csv"),
		("CDS", "https://public.fyers.in/sym_details/NSE_CD.csv"),
		("BSE", "https://public.fyers.in/sym_details/BSE_CM.csv"),
		("BFO", "https://public.fyers.in/sym_details/BSE_FO.csv"),
		("MCX", "https://public.fyers.in/sym_details/MCX_COM.csv"),
	];

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _clientId;
	private readonly SecureString _token;
	private readonly HttpClient _instrumentClient = new();
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private FyersInstrument[] _instruments;
	private IReadOnlyDictionary<string, FyersInstrument> _instrumentsBySymbol;

	public FyersRestClient(string clientId, SecureString token)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_token = token.ThrowIfEmpty(nameof(token));
		_instrumentClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; StockSharp FYERS connector)");
		_instrumentClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
	}

	public override string Name => nameof(Fyers) + "_" + nameof(FyersRestClient);

	protected override void DisposeManaged()
	{
		_instrumentClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public async Task<FyersInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var instruments = new List<FyersInstrument>();
			var errors = new List<Exception>();
			foreach (var source in _masterFiles)
			{
				try
				{
					await ReadInstrumentFile(source.url, instruments, cancellationToken);
				}
				catch (Exception ex) when (ex is HttpRequestException or IOException)
				{
					errors.Add(new InvalidOperationException($"Unable to download FYERS symbol master '{source.boardCode}'.", ex));
					this.AddWarningLog("Unable to download {0}: {1}", source.url, ex.Message);
				}
			}

			if (instruments.Count == 0 && errors.Count > 0)
				throw new AggregateException("FYERS symbol master files could not be downloaded.", errors);

			_instruments = [.. instruments];
			_instrumentsBySymbol = _instruments
				.GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<FyersInstrument> GetInstrument(string symbol, CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		return _instrumentsBySymbol.TryGetValue(symbol, out var instrument) ? instrument : null;
	}

	private async Task ReadInstrumentFile(string url, List<FyersInstrument> instruments, CancellationToken cancellationToken)
	{
		await using var stream = await _instrumentClient.GetStreamAsync(url, cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);
		var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };

		while (await csv.NextLineAsync(cancellationToken))
		{
			var token = csv.ReadString();
			var name = csv.ReadString();
			var instrumentType = ParseInt(csv.ReadString());
			var lotSize = ParseDecimal(csv.ReadString()) ?? 0;
			var tickSize = ParseDecimal(csv.ReadString()) ?? 0;
			var isin = NullIfMissing(csv.ReadString());
			csv.ReadString();
			csv.ReadString();
			var expiry = ParseEpoch(csv.ReadString());
			var symbol = csv.ReadString();
			var exchange = ParseEnum<FyersExchanges>(csv.ReadString());
			var segment = ParseEnum<FyersSegments>(csv.ReadString());
			csv.ReadString();
			var shortName = csv.ReadString();
			var underlyingToken = NullIfMissing(csv.ReadString());
			var strike = ParseDecimal(csv.ReadString());
			var optionType = NullIfMissing(csv.ReadString());

			if (token.IsEmpty() || symbol.IsEmpty() || exchange == null || segment == null)
				continue;

			instruments.Add(new FyersInstrument
			{
				Token = token,
				Name = name,
				InstrumentType = instrumentType,
				LotSize = lotSize,
				TickSize = tickSize,
				Isin = isin,
				ExpiryDate = expiry,
				Symbol = symbol,
				Exchange = exchange.Value,
				Segment = segment.Value,
				ShortName = shortName,
				UnderlyingToken = underlyingToken,
				Strike = strike is > 0 ? strike : null,
				OptionType = optionType,
			});
		}
	}

	public async Task<FyersProfile> GetProfile(CancellationToken cancellationToken)
		=> (await Send<FyersProfileResponse>("api/v3/profile", Method.Get, cancellationToken)).Data;

	public async Task<FyersFund[]> GetFunds(CancellationToken cancellationToken)
		=> (await Send<FyersFundsResponse>("api/v3/funds", Method.Get, cancellationToken)).Funds ?? [];

	public async Task<FyersPosition[]> GetPositions(CancellationToken cancellationToken)
		=> (await Send<FyersPositionsResponse>("api/v3/positions", Method.Get, cancellationToken)).Positions ?? [];

	public async Task<FyersHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> (await Send<FyersHoldingsResponse>("api/v3/holdings", Method.Get, cancellationToken)).Holdings ?? [];

	public async Task<FyersOrder[]> GetOrders(CancellationToken cancellationToken)
		=> (await Send<FyersOrdersResponse>("api/v3/orders", Method.Get, cancellationToken)).Orders ?? [];

	public async Task<FyersTrade[]> GetTrades(CancellationToken cancellationToken)
		=> (await Send<FyersTradesResponse>("api/v3/tradebook", Method.Get, cancellationToken)).Trades ?? [];

	public Task<FyersOrderResult> PlaceOrder(FyersOrderRequest request, CancellationToken cancellationToken)
		=> Send<FyersOrderResult, FyersOrderRequest>("api/v3/orders/sync", Method.Post, request, cancellationToken);

	public Task<FyersOrderResult> ModifyOrder(FyersModifyOrderRequest request, CancellationToken cancellationToken)
		=> Send<FyersOrderResult, FyersModifyOrderRequest>("api/v3/orders/sync", Method.Patch, request, cancellationToken);

	public Task<FyersOrderResult> CancelOrder(string orderId, CancellationToken cancellationToken)
		=> Send<FyersOrderResult, FyersCancelOrderRequest>("api/v3/orders/sync", Method.Delete, new() { Id = orderId }, cancellationToken);

	public Task<FyersOrderResult> PlaceGttOrder(FyersGttOrderRequest request, CancellationToken cancellationToken)
		=> Send<FyersOrderResult, FyersGttOrderRequest>("api/v3/gtt/orders/sync", Method.Post, request, cancellationToken);

	public Task<FyersOrderResult> ModifyGttOrder(FyersGttModifyRequest request, CancellationToken cancellationToken)
		=> Send<FyersOrderResult, FyersGttModifyRequest>("api/v3/gtt/orders/sync", Method.Patch, request, cancellationToken);

	public Task<FyersOrderResult> CancelGttOrder(string orderId, CancellationToken cancellationToken)
		=> Send<FyersOrderResult, FyersCancelOrderRequest>("api/v3/gtt/orders/sync", Method.Delete, new() { Id = orderId }, cancellationToken);

	public async Task<string> GetTbtUrl(CancellationToken cancellationToken)
	{
		try
		{
			var response = await Send<FyersTbtUrlResponse>("indus/home/tbtws", Method.Get, cancellationToken);
			return response.Data?.SocketUrl.IsEmpty() == false ? response.Data.SocketUrl : _defaultTbtUrl;
		}
		catch (Exception ex)
		{
			this.AddWarningLog("Unable to discover FYERS TBT endpoint: {0}", ex.Message);
			return _defaultTbtUrl;
		}
	}

	public async Task<FyersCandle[]> GetCandles(string symbol, TimeSpan timeFrame, DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var start = (from ?? (timeFrame == TimeSpan.FromDays(1) ? end.AddYears(-10) : end.AddDays(-90))).ToUniversalTime();
		if (start > end)
			return [];
		if (start == end)
			end = end.Add(timeFrame);

		var candles = new List<FyersCandle>();
		var pageSize = timeFrame == TimeSpan.FromDays(1) ? TimeSpan.FromDays(365) : TimeSpan.FromDays(90);
		while (start <= end)
		{
			var pageEnd = start + pageSize;
			if (pageEnd > end)
				pageEnd = end;

			var request = CreateRequest("data/history", Method.Get);
			request.AddQueryParameter("symbol", symbol);
			request.AddQueryParameter("resolution", timeFrame.ToNative());
			request.AddQueryParameter("date_format", "0");
			request.AddQueryParameter("range_from", ((long)start.ToUnix()).ToString(CultureInfo.InvariantCulture));
			request.AddQueryParameter("range_to", ((long)pageEnd.ToUnix()).ToString(CultureInfo.InvariantCulture));
			request.AddQueryParameter("cont_flag", "1");

			var response = Validate(await request.InvokeAsync<FyersHistoryResponse>(new Uri(new Uri(_baseUrl), "data/history"), this, this.AddVerboseLog, cancellationToken));
			foreach (var row in response.Candles ?? [])
			{
				if (row?.Length < 6)
					continue;
				candles.Add(new FyersCandle
				{
					Time = ((long)row[0]).FromUnix(),
					Open = row[1],
					High = row[2],
					Low = row[3],
					Close = row[4],
					Volume = row[5],
				});
			}

			if (pageEnd >= end)
				break;
			start = pageEnd.AddSeconds(1);
		}

		return [.. candles.GroupBy(c => c.Time).Select(g => g.Last()).OrderBy(c => c.Time)];
	}

	private Task<T> Send<T>(string path, Method method, CancellationToken cancellationToken)
		where T : FyersResponse
		=> Send<T, FyersCancelOrderRequest>(path, method, null, cancellationToken);

	private async Task<TResponse> Send<TResponse, TRequest>(string path, Method method, TRequest body, CancellationToken cancellationToken)
		where TResponse : FyersResponse
		where TRequest : class
	{
		var request = CreateRequest(path, method);
		if (body != null)
			request.AddStringBody(JsonConvert.SerializeObject(body, _jsonSettings), DataFormat.Json);
		return Validate(await request.InvokeAsync<TResponse>(new Uri(new Uri(_baseUrl), path), this, this.AddVerboseLog, cancellationToken));
	}

	private RestRequest CreateRequest(string path, Method method)
	{
		var request = new RestRequest(path, method);
		request.AddHeader("Accept", "application/json");
		request.AddHeader("Content-Type", "application/json");
		request.AddHeader("Authorization", $"{_clientId}:{_token.UnSecure()}");
		request.AddHeader("version", "3");
		return request;
	}

	private static T Validate<T>(T response)
		where T : FyersResponse
	{
		if (response == null)
			throw new InvalidOperationException("FYERS returned an empty response.");
		if (response.Status == FyersResponseStatuses.Error || response.Code is < 0 or >= 400)
			throw new InvalidOperationException($"FYERS API error {response.Code}: {response.Message}");
		return response;
	}

	private static int ParseInt(string value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

	private static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	private static T? ParseEnum<T>(string value)
		where T : struct, Enum
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && Enum.IsDefined(typeof(T), number)
			? (T)Enum.ToObject(typeof(T), number)
			: null;

	private static DateTime? ParseEpoch(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
			? seconds.FromUnix()
			: null;

	private static string NullIfMissing(string value)
		=> value.IsEmpty() || value.EqualsIgnoreCase("None") || value.EqualsIgnoreCase("NA") ? null : value;
}
