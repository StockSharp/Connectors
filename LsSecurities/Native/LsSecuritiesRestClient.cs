namespace StockSharp.LsSecurities.Native;

internal sealed class LsPortfolioSnapshot
{
	public LsPortfolioSummary Summary { get; init; }
	public LsPosition[] Positions { get; init; }
}

internal sealed class LsSecuritiesRestClient : BaseLogReceiver
{
	private static readonly Uri _root = new("https://openapi.ls-sec.co.kr:8080/");
	private static readonly TimeSpan _koreaOffset = TimeSpan.FromHours(9);

	private readonly HttpClient _http;
	private readonly string _appKey;
	private readonly string _appSecret;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _authenticationLock = new(1, 1);
	private readonly SemaphoreSlim _requestLock = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private string _accessToken;
	private DateTime _accessTokenExpiry;
	private DateTime _lastRequest;
	private LsInstrument[] _instruments;

	public LsSecuritiesRestClient(string appKey, string appSecret, int maxAttempts)
	{
		_appKey = appKey.ThrowIfEmpty(nameof(appKey));
		_appSecret = appSecret.ThrowIfEmpty(nameof(appSecret));
		_maxAttempts = Math.Max(1, maxAttempts);
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
				DecompressionMethods.Brotli,
		};
		_http = new(handler)
		{
			BaseAddress = _root,
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public override string Name => nameof(LsSecurities) + "_REST";

	public string AccessToken => _accessToken;

	public async Task<string> GetAccessToken(CancellationToken cancellationToken)
	{
		await Authenticate(cancellationToken);
		return _accessToken;
	}

	public Task Connect(CancellationToken cancellationToken)
		=> Authenticate(cancellationToken);

	public async Task<LsInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;
		var response = await Send<LsInstrumentRequest, LsInstrumentResponse>("stock/etc", "t8436",
			new(), true, null, cancellationToken);
		return _instruments = response.Instruments ?? [];
	}

	public async Task<LsQuote> GetQuote(string code, CancellationToken cancellationToken)
	{
		var response = await Send<LsQuoteRequest, LsQuoteResponse>("stock/market-data", "t1101",
			new() { Data = new() { Code = code.NormalizeCode() } }, true, null, cancellationToken);
		return response.Quote ?? throw new InvalidDataException("LS Securities returned no quote.");
	}

	public async Task<LsHistoricalTick[]> GetTicks(string code, DateTime from, DateTime to,
		long? count, CancellationToken cancellationToken)
	{
		var result = new List<LsHistoricalTick>();
		var continuation = string.Empty;
		for (var page = 0; page < 20; page++)
		{
			var response = await Send<LsTickHistoryRequest, LsTickHistoryResponse>("stock/market-data",
				"t1301", new()
				{
					Data = new()
					{
						Code = code.NormalizeCode(),
						StartTime = ToKorea(from, "HHmmss"),
						EndTime = ToKorea(to, "HHmmss"),
						ContinuationTime = continuation,
					},
				}, true, continuation, cancellationToken);
			result.AddRange(response.Ticks ?? []);
			var next = response.Continuation?.Time?.Trim();
			if (next.IsEmpty() || next == continuation || count is > 0 && result.Count >= count.Value)
				break;
			continuation = next;
		}

		var koreaDate = (to.ToUniversalTime() + _koreaOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return [.. result
			.Where(t => koreaDate.ToKoreaUtc(t.Time) >= from.ToUniversalTime() &&
				koreaDate.ToKoreaUtc(t.Time) <= to.ToUniversalTime())
			.OrderBy(t => t.Time)
			.TakeLast((int)Math.Min(count ?? int.MaxValue, int.MaxValue))];
	}

	public async Task<LsCandle[]> GetCandles(string code, TimeSpan timeFrame, DateTime from,
		DateTime to, long? count, CancellationToken cancellationToken)
	{
		var result = timeFrame < TimeSpan.FromDays(1)
			? await GetMinuteCandles(code, timeFrame, from, to, count, cancellationToken)
			: await GetDayCandles(code, timeFrame, from, to, count, cancellationToken);
		IEnumerable<LsCandle> ordered = result.Where(c => c.Date.ToKoreaUtc(c.Time) >= from.ToUniversalTime() &&
			c.Date.ToKoreaUtc(c.Time) <= to.ToUniversalTime()).OrderBy(c => c.Date).ThenBy(c => c.Time);
		if (count is > 0)
			ordered = ordered.TakeLast((int)Math.Min(count.Value, int.MaxValue));
		return [.. ordered];
	}

	private async Task<List<LsCandle>> GetMinuteCandles(string code, TimeSpan timeFrame,
		DateTime from, DateTime to, long? count, CancellationToken cancellationToken)
	{
		var minutes = timeFrame.TotalMinutes;
		if (minutes < 1 || minutes > 60 || minutes != Math.Truncate(minutes))
			throw new NotSupportedException($"LS Securities does not support minute frame '{timeFrame}'.");
		var result = new List<LsCandle>();
		var continuationDate = string.Empty;
		var continuationTime = string.Empty;
		for (var page = 0; page < 20; page++)
		{
			var continuationKey = continuationDate + continuationTime;
			var response = await Send<LsMinuteChartRequest, LsMinuteChartResponse>("stock/chart", "t8412",
				new()
				{
					Data = new()
					{
						Code = code.NormalizeCode(),
						Minutes = (int)minutes,
						StartDate = ToKorea(from, "yyyyMMdd"),
						StartTime = ToKorea(from, "HHmmss"),
						EndDate = ToKorea(to, "yyyyMMdd"),
						EndTime = ToKorea(to, "HHmmss"),
						ContinuationDate = continuationDate,
						ContinuationTime = continuationTime,
					},
				}, true, continuationKey, cancellationToken);
			result.AddRange(response.Candles ?? []);
			var nextDate = response.Continuation?.Date?.Trim();
			var nextTime = response.Continuation?.Time?.Trim();
			if (nextDate.IsEmpty() || nextDate + nextTime == continuationKey ||
				count is > 0 && result.Count >= count.Value)
				break;
			continuationDate = nextDate;
			continuationTime = nextTime;
		}
		return result;
	}

	private async Task<List<LsCandle>> GetDayCandles(string code, TimeSpan timeFrame,
		DateTime from, DateTime to, long? count, CancellationToken cancellationToken)
	{
		var result = new List<LsCandle>();
		var continuationDate = string.Empty;
		for (var page = 0; page < 20; page++)
		{
			var response = await Send<LsDayChartRequest, LsDayChartResponse>("stock/chart", "t8410",
				new()
				{
					Data = new()
					{
						Code = code.NormalizeCode(),
						Period = timeFrame.ToChartKind(),
						StartDate = ToKorea(from, "yyyyMMdd"),
						EndDate = ToKorea(to, "yyyyMMdd"),
						ContinuationDate = continuationDate,
					},
				}, true, continuationDate, cancellationToken);
			result.AddRange(response.Candles ?? []);
			var next = response.Continuation?.Date?.Trim();
			if (next.IsEmpty() || next == continuationDate || count is > 0 && result.Count >= count.Value)
				break;
			continuationDate = next;
		}
		return result;
	}

	public async Task<LsOrderResult> PlaceOrder(LsPlaceOrderRequest request,
		CancellationToken cancellationToken)
	{
		var response = await Send<LsPlaceOrderRequest, LsPlaceOrderResponse>("stock/order",
			"CSPAT00601", request, false, null, cancellationToken);
		return response.Result ?? throw new InvalidDataException("LS Securities returned no order result.");
	}

	public async Task<LsOrderResult> ReplaceOrder(LsReplaceOrderRequest request,
		CancellationToken cancellationToken)
	{
		var response = await Send<LsReplaceOrderRequest, LsReplaceOrderResponse>("stock/order",
			"CSPAT00701", request, false, null, cancellationToken);
		return response.Result ?? throw new InvalidDataException("LS Securities returned no replace result.");
	}

	public async Task<LsOrderResult> CancelOrder(LsCancelOrderRequest request,
		CancellationToken cancellationToken)
	{
		var response = await Send<LsCancelOrderRequest, LsCancelOrderResponse>("stock/order",
			"CSPAT00801", request, false, null, cancellationToken);
		return response.Result ?? throw new InvalidDataException("LS Securities returned no cancel result.");
	}

	public async Task<LsPortfolioSnapshot> GetPortfolio(CancellationToken cancellationToken)
	{
		var positions = new List<LsPosition>();
		LsPortfolioSummary summary = null;
		var continuation = string.Empty;
		for (var page = 0; page < 20; page++)
		{
			var response = await Send<LsPositionsRequest, LsPositionsResponse>("stock/accno", "t0424",
				new() { Data = new() { ContinuationCode = continuation } }, true, continuation,
				cancellationToken);
			summary ??= response.Summary;
			positions.AddRange(response.Positions ?? []);
			var next = response.Summary?.ContinuationCode?.Trim();
			if (next.IsEmpty() || next == continuation)
				break;
			continuation = next;
		}
		return new() { Summary = summary, Positions = [.. positions] };
	}

	public async Task<LsOrder[]> GetOrders(CancellationToken cancellationToken)
	{
		var orders = new List<LsOrder>();
		var continuation = string.Empty;
		for (var page = 0; page < 20; page++)
		{
			var response = await Send<LsOrdersRequest, LsOrdersResponse>("stock/accno", "t0425",
				new() { Data = new() { ContinuationOrderNumber = continuation } }, true, continuation,
				cancellationToken);
			orders.AddRange(response.Orders ?? []);
			var next = response.Summary?.ContinuationOrderNumber?.Trim();
			if (next.IsEmpty() || next == continuation)
				break;
			continuation = next;
		}
		return [.. orders];
	}

	private async Task Authenticate(CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty() && _accessTokenExpiry > DateTime.UtcNow + TimeSpan.FromMinutes(2))
			return;
		await _authenticationLock.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() && _accessTokenExpiry > DateTime.UtcNow + TimeSpan.FromMinutes(2))
				return;
			var tokenRequest = new LsTokenRequest { AppKey = _appKey, AppSecret = _appSecret };
			using var body = new FormUrlEncodedContent(
			[
				new("grant_type", tokenRequest.GrantType),
				new("appkey", tokenRequest.AppKey),
				new("appsecretkey", tokenRequest.AppSecret),
				new("scope", tokenRequest.Scope),
			]);
			using var request = new HttpRequestMessage(HttpMethod.Post, "oauth2/token") { Content = body };
			using var response = await _http.SendAsync(request, cancellationToken);
			var json = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"LS Securities token request failed ({(int)response.StatusCode}): {json}");
			var token = JsonConvert.DeserializeObject<LsTokenResponse>(json, _jsonSettings)
				?? throw new InvalidDataException("LS Securities returned no token response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(token.AccessToken));
			_accessTokenExpiry = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn));
		}
		finally
		{
			_authenticationLock.Release();
		}
	}

	private async Task<TResponse> Send<TRequest, TResponse>(string path, string transactionCode,
		TRequest requestData, bool isSafe, string continuationKey, CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : LsResponse
	{
		await Authenticate(cancellationToken);
		await _requestLock.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				await WaitForLimit(transactionCode, cancellationToken);
				using var request = new HttpRequestMessage(HttpMethod.Post, path)
				{
					Content = new StringContent(JsonConvert.SerializeObject(requestData, _jsonSettings),
						Encoding.UTF8, "application/json"),
				};
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
				request.Headers.TryAddWithoutValidation("tr_cd", transactionCode);
				request.Headers.TryAddWithoutValidation("tr_cont", continuationKey.IsEmpty() ? "N" : "Y");
				request.Headers.TryAddWithoutValidation("tr_cont_key", continuationKey.IsEmpty() ? string.Empty : continuationKey);
				try
				{
					using var response = await _http.SendAsync(request, cancellationToken);
					_lastRequest = DateTime.UtcNow;
					var json = await response.Content.ReadAsStringAsync(cancellationToken);
					if (!response.IsSuccessStatusCode)
						throw new HttpRequestException($"LS Securities {transactionCode} failed " +
							$"({(int)response.StatusCode}): {json}", null, response.StatusCode);
					var result = JsonConvert.DeserializeObject<TResponse>(json, _jsonSettings)
						?? throw new InvalidDataException($"LS Securities {transactionCode} returned an empty response.");
					if (!result.ResponseCode.IsEmpty() && !result.ResponseCode.StartsWith("00", StringComparison.Ordinal))
						throw new InvalidOperationException($"LS Securities {transactionCode}: " +
							$"{result.ResponseCode} {result.ResponseMessage}");
					return result;
				}
				catch (Exception ex) when (isSafe && attempt < _maxAttempts && IsTransient(ex))
				{
					if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
					{
						_accessToken = null;
						await Authenticate(cancellationToken);
					}
					await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 300 * attempt)), cancellationToken);
				}
			}
		}
		finally
		{
			_requestLock.Release();
		}
	}

	private async Task WaitForLimit(string transactionCode, CancellationToken cancellationToken)
	{
		var interval = transactionCode switch
		{
			"t8410" or "t8412" or "t1301" => TimeSpan.FromMilliseconds(1050),
			"t8436" or "t0424" or "t0425" => TimeSpan.FromMilliseconds(550),
			"CSPAT00701" or "CSPAT00801" => TimeSpan.FromMilliseconds(350),
			_ => TimeSpan.FromMilliseconds(110),
		};
		var wait = interval - (DateTime.UtcNow - _lastRequest);
		if (wait > TimeSpan.Zero)
			await Task.Delay(wait, cancellationToken);
	}

	private static bool IsTransient(Exception exception)
		=> exception is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or
			HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout }
			or TaskCanceledException;

	private static string ToKorea(DateTime value, string format)
		=> (value.ToUniversalTime() + _koreaOffset).ToString(format, CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authenticationLock.Dispose();
		_requestLock.Dispose();
		base.DisposeManaged();
	}
}
