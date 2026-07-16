namespace StockSharp.Zerodha.Native;

internal sealed class ZerodhaRestClient : BaseLogReceiver, IDisposable
{
	private static readonly TimeSpan _minimumRequestInterval = TimeSpan.FromMilliseconds(105);
	private static readonly TimeSpan _historicalRequestInterval = TimeSpan.FromMilliseconds(340);
	private static readonly TimeSpan _indiaOffset = TimeSpan.FromHours(5.5);

	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly SemaphoreSlim _instrumentGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTimeOffset _lastRequest;
	private DateTimeOffset _lastHistoricalRequest;
	private KiteInstrument[] _instruments;

	public ZerodhaRestClient(string apiKey, string accessToken, int maxAttempts)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_maxAttempts = Math.Max(1, maxAttempts);
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
		};
		_http = new(handler)
		{
			BaseAddress = new("https://api.kite.trade/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Add("X-Kite-Version", "3");
		SetAccessToken(accessToken);
	}

	public override string Name => nameof(Zerodha) + "_" + nameof(ZerodhaRestClient);

	public string AccessToken { get; private set; }

	public void SetAccessToken(string accessToken)
	{
		AccessToken = accessToken;
		_http.DefaultRequestHeaders.Authorization = accessToken.IsEmpty()
			? null
			: new AuthenticationHeaderValue("token", $"{_apiKey}:{accessToken}");
	}

	public async Task<KiteSession> ExchangeToken(string requestToken, string apiSecret,
		CancellationToken cancellationToken)
	{
		requestToken.ThrowIfEmpty(nameof(requestToken));
		apiSecret.ThrowIfEmpty(nameof(apiSecret));
		var checksum = Convert.ToHexString(SHA256.HashData(
			Encoding.UTF8.GetBytes(_apiKey + requestToken + apiSecret))).ToLowerInvariant();
		var session = await Post<KiteSession, KiteTokenRequest>("session/token", new()
		{
			ApiKey = _apiKey,
			RequestToken = requestToken,
			Checksum = checksum,
		}, false, false, cancellationToken);
		if (session?.AccessToken.IsEmpty() != false)
			throw new InvalidDataException("Zerodha token exchange returned no access token.");
		SetAccessToken(session.AccessToken);
		return session;
	}

	public Task<KiteProfile> GetProfile(CancellationToken cancellationToken)
		=> Get<KiteProfile, KiteEmptyData>("user/profile", new(), false, cancellationToken);

	public Task<KiteMarginsData> GetMargins(CancellationToken cancellationToken)
		=> Get<KiteMarginsData, KiteEmptyData>("user/margins", new(), false, cancellationToken);

	public Task<KitePosition[]> GetNetPositions(CancellationToken cancellationToken)
		=> GetPositions(cancellationToken);

	private async Task<KitePosition[]> GetPositions(CancellationToken cancellationToken)
		=> (await Get<KitePositionsData, KiteEmptyData>("portfolio/positions", new(), false,
			cancellationToken))?.Net ?? [];

	public async Task<KiteHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> await Get<KiteHolding[], KiteEmptyData>("portfolio/holdings", new(), false,
			cancellationToken) ?? [];

	public async Task<KiteOrder[]> GetOrders(CancellationToken cancellationToken)
		=> await Get<KiteOrder[], KiteEmptyData>("orders", new(), false, cancellationToken) ?? [];

	public async Task<KiteTrade[]> GetTrades(CancellationToken cancellationToken)
		=> await Get<KiteTrade[], KiteEmptyData>("trades", new(), false, cancellationToken) ?? [];

	public async Task<KiteCandle[]> GetCandles(long instrumentToken, string interval, DateTime from,
		DateTime to, bool isOpenInterest, CancellationToken cancellationToken)
	{
		var request = new KiteHistoricalRequest
		{
			From = FormatIndiaTime(from),
			To = FormatIndiaTime(to),
			IsContinuous = false,
			IsOpenInterest = isOpenInterest,
		};
		var data = await Get<KiteCandlesData, KiteHistoricalRequest>(
			$"instruments/historical/{instrumentToken.ToString(CultureInfo.InvariantCulture)}/" +
			Uri.EscapeDataString(interval), request, true, cancellationToken);
		return data?.Candles ?? [];
	}

	public Task<KiteOrderResult> PlaceOrder(string variety, KitePlaceOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<KiteOrderResult, KitePlaceOrderRequest>($"orders/{Uri.EscapeDataString(variety)}",
			request, true, false, cancellationToken);

	public Task<KiteOrderResult> ModifyOrder(string variety, string orderId,
		KiteModifyOrderRequest request, CancellationToken cancellationToken)
		=> Put<KiteOrderResult, KiteModifyOrderRequest>(
			$"orders/{Uri.EscapeDataString(variety)}/{Uri.EscapeDataString(orderId)}", request,
			cancellationToken);

	public Task<KiteOrderResult> CancelOrder(string variety, string orderId,
		CancellationToken cancellationToken)
		=> Delete<KiteOrderResult, KiteEmptyData>(
			$"orders/{Uri.EscapeDataString(variety)}/{Uri.EscapeDataString(orderId)}", new(),
			cancellationToken);

	public async Task<KiteInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentGate.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var content = await SendRaw(HttpMethod.Get, "instruments", null, true, false,
				cancellationToken);
			using var reader = new StringReader(content);
			var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
			if (!await csv.NextLineAsync(cancellationToken))
				return _instruments = [];

			var result = new List<KiteInstrument>();
			while (await csv.NextLineAsync(cancellationToken))
			{
				var instrument = new KiteInstrument
				{
					InstrumentToken = ParseLong(csv.ReadString()) ?? 0,
					ExchangeToken = ParseLong(csv.ReadString()),
					TradingSymbol = csv.ReadString(),
					Name = csv.ReadString(),
					LastPrice = ParseDecimal(csv.ReadString()) ?? 0,
					ExpiryDate = ParseDate(csv.ReadString()),
					Strike = ParseDecimal(csv.ReadString()),
					TickSize = ParseDecimal(csv.ReadString()) ?? 0,
					LotSize = ParseDecimal(csv.ReadString()) ?? 0,
					InstrumentType = csv.ReadString(),
					Segment = csv.ReadString(),
					Exchange = csv.ReadString(),
				};
				if (instrument.InstrumentToken > 0 && !instrument.TradingSymbol.IsEmpty() &&
					!instrument.Exchange.IsEmpty())
					result.Add(instrument);
			}

			return _instruments = [.. result];
		}
		finally
		{
			_instrumentGate.Release();
		}
	}

	private async Task<TData> Get<TData, TRequest>(string path, TRequest request, bool isHistorical,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		var query = ZerodhaFormEncoder.ToQueryString(request);
		var content = await SendRaw(HttpMethod.Get, query.IsEmpty() ? path : $"{path}?{query}", null,
			true, isHistorical, cancellationToken);
		return Deserialize<TData>(content);
	}

	private async Task<TData> Post<TData, TRequest>(string path, TRequest request, bool isAuthenticated,
		bool isHistorical,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		var content = await SendRaw(HttpMethod.Post, path,
			new FormUrlEncodedContent(ZerodhaFormEncoder.Encode(request)), isAuthenticated, isHistorical,
			cancellationToken);
		return Deserialize<TData>(content);
	}

	private async Task<TData> Put<TData, TRequest>(string path, TRequest request,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		var content = await SendRaw(HttpMethod.Put, path,
			new FormUrlEncodedContent(ZerodhaFormEncoder.Encode(request)), true, false, cancellationToken);
		return Deserialize<TData>(content);
	}

	private async Task<TData> Delete<TData, TRequest>(string path, TRequest request,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		var query = ZerodhaFormEncoder.ToQueryString(request);
		var content = await SendRaw(HttpMethod.Delete, query.IsEmpty() ? path : $"{path}?{query}", null,
			true, false, cancellationToken);
		return Deserialize<TData>(content);
	}

	private async Task<string> SendRaw(HttpMethod method, string path, HttpContent body,
		bool isAuthenticated, bool isHistorical, CancellationToken cancellationToken)
	{
		if (isAuthenticated && AccessToken.IsEmpty())
			throw new InvalidOperationException("Zerodha access token is not set.");

		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				await WaitForRateLimit(isHistorical, cancellationToken);
				using var request = new HttpRequestMessage(method, path) { Content = body };
				try
				{
					using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
					_lastRequest = DateTimeOffset.UtcNow;
					if (isHistorical)
						_lastHistoricalRequest = _lastRequest;
					var content = await response.Content.ReadAsStringAsync(cancellationToken);
					if (!response.IsSuccessStatusCode)
						throw CreateException(response.StatusCode, content);
					if (content.IsEmpty())
						throw new InvalidDataException("Zerodha returned an empty response.");
					return content;
				}
				catch (Exception ex) when (method == HttpMethod.Get && attempt < _maxAttempts &&
					IsTransient(ex))
				{
					await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
				}
			}
		}
		finally
		{
			_requestGate.Release();
			if (body != null)
				body.Dispose();
		}
	}

	private async Task WaitForRateLimit(bool isHistorical, CancellationToken cancellationToken)
	{
		var wait = _minimumRequestInterval - (DateTimeOffset.UtcNow - _lastRequest);
		if (isHistorical)
		{
			var historicalWait = _historicalRequestInterval -
				(DateTimeOffset.UtcNow - _lastHistoricalRequest);
			if (historicalWait > wait)
				wait = historicalWait;
		}
		if (wait > TimeSpan.Zero)
			await Task.Delay(wait, cancellationToken);
	}

	private TData Deserialize<TData>(string content)
	{
		var envelope = JsonConvert.DeserializeObject<KiteEnvelope<TData>>(content, _jsonSettings)
			?? throw new InvalidDataException("Zerodha returned an invalid response.");
		if (!envelope.Status.EqualsIgnoreCase("success"))
			throw new InvalidOperationException(FormatError(envelope.ErrorType, envelope.Message));
		return envelope.Data;
	}

	private static Exception CreateException(HttpStatusCode statusCode, string content)
	{
		string errorType = null;
		var message = content;
		try
		{
			var error = JsonConvert.DeserializeObject<KiteEnvelope<KiteEmptyData>>(content);
			errorType = error?.ErrorType;
			message = error?.Message.IsEmpty(content);
		}
		catch (JsonException)
		{
		}
		return new HttpRequestException(
			$"Zerodha request failed ({(int)statusCode}): {FormatError(errorType, message)}", null,
			statusCode);
	}

	private static string FormatError(string errorType, string message)
		=> errorType.IsEmpty() ? message.IsEmpty("Unknown Zerodha API error.") :
			$"{errorType}: {message.IsEmpty("Unknown Zerodha API error.")}";

	private static bool IsTransient(Exception exception)
		=> exception is HttpRequestException http &&
			(http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
			 (int?)http.StatusCode >= 500);

	private static string FormatIndiaTime(DateTime time)
	{
		var utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
		return new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(_indiaOffset)
			.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
	}

	private static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result : null;

	private static long? ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result : null;

	private static DateTime? ParseDate(string value)
		=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var date)
			? DateTime.SpecifyKind(date, DateTimeKind.Utc)
			: null;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		_instrumentGate.Dispose();
		base.DisposeManaged();
	}
}
