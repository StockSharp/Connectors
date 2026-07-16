namespace StockSharp.KoreaInvestment.Native;

sealed class KoreaInvestmentRestClient : BaseLogReceiver
{
	private static readonly Uri _productionRoot = new("https://openapi.koreainvestment.com:9443/");
	private static readonly Uri _simulationRoot = new("https://openapivts.koreainvestment.com:29443/");

	private readonly HttpClient _http = new();
	private readonly string _appKey;
	private readonly string _appSecret;
	private readonly string _accountNumber;
	private readonly string _productCode;
	private readonly bool _isDemo;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _authenticationLock = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private string _accessToken;
	private DateTime _accessTokenExpiry;

	public KoreaInvestmentRestClient(string appKey, string appSecret, string accountNumber, string productCode,
		bool isDemo, int maxAttempts)
	{
		_appKey = appKey.ThrowIfEmpty(nameof(appKey));
		_appSecret = appSecret.ThrowIfEmpty(nameof(appSecret));
		_accountNumber = accountNumber;
		_productCode = productCode.IsEmpty("01");
		_isDemo = isDemo;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(KoreaInvestment) + "_REST";

	public string ApprovalKey { get; private set; }

	public async Task Connect(CancellationToken cancellationToken)
	{
		await Authenticate(cancellationToken);
		ApprovalKey = await RequestApprovalKey(cancellationToken);
	}

	public async Task<KisQuoteSnapshot> GetQuote(KisSecurityInfo security, CancellationToken cancellationToken)
	{
		switch (security.AssetClass)
		{
			case KisAssetClasses.DomesticStock:
			{
				var response = await Send<KisDomesticQuoteResponse>(KisOperations.DomesticQuote,
					new KisDomesticQuoteQuery { MarketCode = security.RestMarketCode, SecurityCode = security.Code }, null,
					cancellationToken);
				var quote = response.Output ?? throw Empty(KisOperations.DomesticQuote);
				return new(quote.LastPrice.ToDecimal(), quote.OpenPrice.ToDecimal(), quote.HighPrice.ToDecimal(),
					quote.LowPrice.ToDecimal(), quote.PreviousClose.ToDecimal(), quote.Volume.ToDecimal(),
					quote.Turnover.ToDecimal(), quote.BidPrice.ToDecimal(), quote.BidVolume.ToDecimal(),
					quote.AskPrice.ToDecimal(), quote.AskVolume.ToDecimal(), null, DateTime.UtcNow);
			}
			case KisAssetClasses.DomesticDerivative:
			{
				var response = await Send<KisDerivativeQuoteResponse>(KisOperations.DerivativeQuote,
					new KisDerivativeQuoteQuery
					{
						MarketCode = security.SecurityType == SecurityTypes.Option ? "O" : "F",
						SecurityCode = security.Code,
					}, null, cancellationToken);
				var quote = response.Output1 ?? throw Empty(KisOperations.DerivativeQuote);
				var isOption = security.SecurityType == SecurityTypes.Option;
				return new((isOption ? quote.OptionPrice : quote.FuturesPrice).ToDecimal(),
					(isOption ? quote.OptionOpen : quote.FuturesOpen).ToDecimal(),
					(isOption ? quote.OptionHigh : quote.FuturesHigh).ToDecimal(),
					(isOption ? quote.OptionLow : quote.FuturesLow).ToDecimal(),
					(isOption ? response.Output2?.OptionPreviousClose : response.Output2?.FuturesPreviousClose).ToDecimal(),
					quote.Volume.ToDecimal(), quote.Turnover.ToDecimal(),
					(isOption ? quote.OptionBid : quote.FuturesBid).ToDecimal(), quote.BidVolume.ToDecimal(),
					(isOption ? quote.OptionAsk : quote.FuturesAsk).ToDecimal(), quote.AskVolume.ToDecimal(),
					quote.OpenInterest.ToDecimal(), DateTime.UtcNow);
			}
			case KisAssetClasses.OverseasStock:
			{
				var response = await Send<KisOverseasQuoteResponse>(KisOperations.OverseasQuote,
					new KisOverseasQuoteQuery
					{
						ExchangeCode = security.RestMarketCode,
						Symbol = security.Code,
					}, null, cancellationToken);
				var quote = response.Output ?? throw Empty(KisOperations.OverseasQuote);
				return new(quote.LastPrice.ToDecimal(), quote.OpenPrice.ToDecimal(), quote.HighPrice.ToDecimal(),
					quote.LowPrice.ToDecimal(), quote.PreviousClose.ToDecimal(), quote.Volume.ToDecimal(),
					quote.Turnover.ToDecimal(), quote.BidPrice.ToDecimal(), quote.BidVolume.ToDecimal(),
					quote.AskPrice.ToDecimal(), quote.AskVolume.ToDecimal(), null,
					quote.Date.ToKisUtc(quote.Time, security));
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(security), security.AssetClass, null);
		}
	}

	public async Task<KisCandleBar[]> GetCandles(KisSecurityInfo security, TimeSpan timeFrame,
		DateTime? from, DateTime? to, long? count, CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).UtcKind();
		var start = (from ?? end - (timeFrame >= TimeSpan.FromDays(1) ? TimeSpan.FromDays(365) : TimeSpan.FromDays(5))).UtcKind();
		IEnumerable<KisCandleBar> candles = security.AssetClass switch
		{
			KisAssetClasses.DomesticStock => await GetDomesticCandles(security, timeFrame, start, end, cancellationToken),
			KisAssetClasses.DomesticDerivative => await GetDerivativeCandles(security, timeFrame, start, end, cancellationToken),
			KisAssetClasses.OverseasStock => await GetOverseasCandles(security, timeFrame, start, end, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(security), security.AssetClass, null),
		};

		candles = candles.Where(c => c.OpenTime >= start && c.OpenTime <= end).OrderBy(c => c.OpenTime);
		if (count is > 0)
			candles = candles.TakeLast((int)Math.Min(count.Value, int.MaxValue));
		return [.. candles];
	}

	public Task<KisOrderResult> PlaceOrder(KisOperations operation, object request, CancellationToken cancellationToken)
		=> SendOrder(operation, request, cancellationToken);

	public Task<KisOrderResult> CancelOrder(KisOperations operation, object request, CancellationToken cancellationToken)
		=> SendOrder(operation, request, cancellationToken);

	public async Task<KisPosition[]> GetDomesticPositions(CancellationToken cancellationToken)
	{
		EnsureAccount();
		var response = await Send<KisDomesticBalanceResponse>(KisOperations.DomesticBalance,
			new KisDomesticBalanceQuery { AccountNumber = _accountNumber, ProductCode = _productCode }, null, cancellationToken);
		return [.. (response.Output1 ?? []).Where(p => p.Quantity.ToDecimal() != 0).Select(p => new KisPosition(
			KisSecurityInfo.Create(p.ProductNumber, KoreaInvestmentMarkets.Krx, SecurityTypes.Stock),
			p.Quantity.ToDecimal() ?? 0, p.AveragePrice.ToDecimal(), p.CurrentPrice.ToDecimal(),
			p.UnrealizedPnL.ToDecimal(), p.MarketValue.ToDecimal(), "KRW"))];
	}

	public async Task<KisPosition[]> GetDerivativePositions(CancellationToken cancellationToken)
	{
		EnsureAccount();
		var response = await Send<KisDerivativeBalanceResponse>(KisOperations.DerivativeBalance,
			new KisDerivativeBalanceQuery { AccountNumber = _accountNumber, ProductCode = _productCode }, null, cancellationToken);
		return [.. (response.Output1 ?? []).Where(p => p.Quantity.ToDecimal() != 0).Select(p => new KisPosition(
			KisSecurityInfo.Create(p.ProductNumber, KoreaInvestmentMarkets.KrxDerivatives, null),
			p.Quantity.ToDecimal() ?? 0, p.AveragePrice.ToDecimal() ?? p.AlternativeAveragePrice.ToDecimal(),
			p.CurrentPrice.ToDecimal(), p.UnrealizedPnL.ToDecimal(), p.MarketValue.ToDecimal(), "KRW"))];
	}

	public async Task<KisPosition[]> GetOverseasPositions(KoreaInvestmentMarkets market,
		CancellationToken cancellationToken)
	{
		EnsureAccount();
		var info = KisSecurityInfo.Create("_", market, SecurityTypes.Stock);
		var response = await Send<KisOverseasBalanceResponse>(KisOperations.OverseasBalance,
			new KisOverseasBalanceQuery
			{
				AccountNumber = _accountNumber,
				ProductCode = _productCode,
				ExchangeCode = info.OrderExchangeCode,
				CurrencyCode = info.Currency.ToString(),
			}, null, cancellationToken);
		return [.. (response.Output1 ?? []).Where(p => p.Quantity.ToDecimal() != 0).Select(p =>
		{
			var itemMarket = TryGetOverseasMarket(p.ExchangeCode, market);
			return new KisPosition(KisSecurityInfo.Create(p.ProductNumber, itemMarket, SecurityTypes.Stock),
				p.Quantity.ToDecimal() ?? 0, p.AveragePrice.ToDecimal(), p.CurrentPrice.ToDecimal(),
				p.UnrealizedPnL.ToDecimal(), p.MarketValue.ToDecimal(), p.Currency.IsEmpty(info.Currency.ToString()));
		})];
	}

	public async Task<KisOrderExecution[]> GetDomesticExecutions(DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		EnsureAccount();
		var response = await Send<KisDomesticExecutionsResponse>(KisOperations.DomesticExecutions,
			new KisDomesticExecutionsQuery
			{
				AccountNumber = _accountNumber,
				ProductCode = _productCode,
				From = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				To = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			}, null, cancellationToken);
		return ConvertExecutions(response.Output1, KoreaInvestmentMarkets.Krx);
	}

	public async Task<KisOrderExecution[]> GetDerivativeExecutions(DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		EnsureAccount();
		var response = await Send<KisDerivativeExecutionsResponse>(KisOperations.DerivativeExecutions,
			new KisDerivativeExecutionsQuery
			{
				AccountNumber = _accountNumber,
				ProductCode = _productCode,
				From = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				To = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			}, null, cancellationToken);
		return ConvertExecutions(response.Output1, KoreaInvestmentMarkets.KrxDerivatives);
	}

	public async Task<KisOrderExecution[]> GetOverseasExecutions(DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		EnsureAccount();
		var response = await Send<KisOverseasExecutionsResponse>(KisOperations.OverseasExecutions,
			new KisOverseasExecutionsQuery
			{
				AccountNumber = _accountNumber,
				ProductCode = _productCode,
				From = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				To = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			}, null, cancellationToken);
		return ConvertExecutions(response.Output, KoreaInvestmentMarkets.Nasdaq);
	}

	private async Task<KisOrderResult> SendOrder(KisOperations operation, object request,
		CancellationToken cancellationToken)
	{
		EnsureAccount();
		var response = await Send<KisOrderResponse>(operation, null, request, cancellationToken);
		return response.Output ?? throw Empty(operation);
	}

	private async Task<KisCandleBar[]> GetDomesticCandles(KisSecurityInfo security, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		if (timeFrame >= TimeSpan.FromDays(1))
		{
			var response = await Send<KisDomesticCandleResponse>(KisOperations.DomesticDailyCandles,
				new KisDomesticCandleQuery
				{
					MarketCode = security.RestMarketCode,
					SecurityCode = security.Code,
					From = ToLocalDate(from, security),
					To = ToLocalDate(to, security),
					Period = ToPeriod(timeFrame),
				}, null, cancellationToken);
			return [.. (response.Output2 ?? []).Select(c => Convert(c, security))];
		}

		var result = new List<KisCandleBar>();
		var cursor = TimeZoneInfo.ConvertTimeFromUtc(to, security.TimeZone);
		for (var page = 0; page < 20 && cursor >= TimeZoneInfo.ConvertTimeFromUtc(from, security.TimeZone); page++)
		{
			var response = await Send<KisDomesticCandleResponse>(KisOperations.DomesticMinuteCandles,
				new KisDomesticMinuteCandleQuery
				{
					MarketCode = security.RestMarketCode,
					SecurityCode = security.Code,
					Date = cursor.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
					Time = cursor.ToString("HHmmss", CultureInfo.InvariantCulture),
				}, null, cancellationToken);
			var pageItems = (response.Output2 ?? []).Select(c => Convert(c, security)).ToArray();
			if (pageItems.Length == 0)
				break;
			result.AddRange(pageItems);
			var earliest = pageItems.Min(c => c.OpenTime);
			if (earliest <= from)
				break;
			cursor = TimeZoneInfo.ConvertTimeFromUtc(earliest.AddMinutes(-1), security.TimeZone);
		}
		return Aggregate(result, timeFrame);
	}

	private async Task<KisCandleBar[]> GetDerivativeCandles(KisSecurityInfo security, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var marketCode = security.SecurityType == SecurityTypes.Option ? "O" : "F";
		if (timeFrame >= TimeSpan.FromDays(1))
		{
			var response = await Send<KisDerivativeCandleResponse>(KisOperations.DerivativeDailyCandles,
				new KisDerivativeCandleQuery
				{
					MarketCode = marketCode,
					SecurityCode = security.Code,
					From = ToLocalDate(from, security),
					To = ToLocalDate(to, security),
					Period = ToPeriod(timeFrame),
				}, null, cancellationToken);
			return [.. (response.Output2 ?? []).Select(c => Convert(c, security))];
		}

		var result = new List<KisCandleBar>();
		var cursor = TimeZoneInfo.ConvertTimeFromUtc(to, security.TimeZone);
		for (var page = 0; page < 20 && cursor >= TimeZoneInfo.ConvertTimeFromUtc(from, security.TimeZone); page++)
		{
			var response = await Send<KisDerivativeCandleResponse>(KisOperations.DerivativeMinuteCandles,
				new KisDerivativeMinuteCandleQuery
				{
					MarketCode = marketCode,
					SecurityCode = security.Code,
					Date = cursor.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
					Time = cursor.ToString("HHmmss", CultureInfo.InvariantCulture),
				}, null, cancellationToken);
			var pageItems = (response.Output2 ?? []).Select(c => Convert(c, security)).ToArray();
			if (pageItems.Length == 0)
				break;
			result.AddRange(pageItems);
			var earliest = pageItems.Min(c => c.OpenTime);
			if (earliest <= from)
				break;
			cursor = TimeZoneInfo.ConvertTimeFromUtc(earliest.AddMinutes(-1), security.TimeZone);
		}
		return Aggregate(result, timeFrame);
	}

	private async Task<KisCandleBar[]> GetOverseasCandles(KisSecurityInfo security, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		if (timeFrame >= TimeSpan.FromDays(1))
		{
			var response = await Send<KisOverseasCandleResponse>(KisOperations.OverseasDailyCandles,
				new KisOverseasCandleQuery
				{
					ExchangeCode = security.RestMarketCode,
					Symbol = security.Code,
					Period = timeFrame >= TimeSpan.FromDays(30) ? "2" : timeFrame >= TimeSpan.FromDays(7) ? "1" : "0",
					BeforeDate = ToLocalDate(to, security),
				}, null, cancellationToken);
			return [.. (response.Output2 ?? []).Select(c => Convert(c, security))];
		}

		var minutes = Math.Clamp((int)timeFrame.TotalMinutes, 1, 60);
		var responseMinute = await Send<KisOverseasCandleResponse>(KisOperations.OverseasMinuteCandles,
			new KisOverseasMinuteCandleQuery
			{
				ExchangeCode = security.RestMarketCode,
				Symbol = security.Code,
				IntervalMinutes = minutes.ToString(CultureInfo.InvariantCulture),
			}, null, cancellationToken);
		return [.. (responseMinute.Output2 ?? []).Select(c => Convert(c, security))];
	}

	private async Task Authenticate(CancellationToken cancellationToken)
	{
		await _authenticationLock.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() && _accessTokenExpiry > DateTime.UtcNow.AddMinutes(5))
				return;
			var token = await SendAuthentication<KisTokenResponse>("oauth2/tokenP", new KisTokenRequest
			{
				AppKey = _appKey,
				AppSecret = _appSecret,
			}, cancellationToken);
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(token.AccessToken));
			_accessTokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(300, token.ExpiresIn));
		}
		finally
		{
			_authenticationLock.Release();
		}
	}

	private async Task<string> RequestApprovalKey(CancellationToken cancellationToken)
	{
		var response = await SendAuthentication<KisApprovalResponse>("oauth2/Approval", new KisApprovalRequest
		{
			AppKey = _appKey,
			SecretKey = _appSecret,
		}, cancellationToken);
		return response.ApprovalKey.ThrowIfEmpty(nameof(response.ApprovalKey));
	}

	private async Task<T> SendAuthentication<T>(string path, object body, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Root, path));
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		request.Content = JsonContent(body);
		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateError(response.StatusCode, content, path);
		return Deserialize<T>(content) ?? throw new InvalidOperationException($"KIS {path} returned an empty response.");
	}

	private async Task<T> Send<T>(KisOperations operation, object query, object body,
		CancellationToken cancellationToken)
		where T : KisResponse
	{
		await Authenticate(cancellationToken);
		var route = KisRoutes.Get(operation);
		var path = route.Path + BuildQuery(query);
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(route.Method, new Uri(Root, path));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
			request.Headers.TryAddWithoutValidation("appkey", _appKey);
			request.Headers.TryAddWithoutValidation("appsecret", _appSecret);
			request.Headers.TryAddWithoutValidation("tr_id", route.GetTrId(_isDemo));
			request.Headers.TryAddWithoutValidation("custtype", "P");
			if (body != null)
				request.Content = JsonContent(body);

			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) &&
				attempt < _maxAttempts && (route.Method == HttpMethod.Get || response.StatusCode == HttpStatusCode.TooManyRequests))
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, operation.ToString());
			var result = Deserialize<T>(content) ?? throw new InvalidOperationException($"KIS {operation} returned an empty response.");
			if (!result.IsSuccess)
				throw new InvalidOperationException($"KIS {operation} error {result.MessageCode}: {result.Message}");
			return result;
		}
	}

	private StringContent JsonContent(object body)
		=> new(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");

	private T Deserialize<T>(string content)
		=> content.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(content, _jsonSettings);

	private Exception CreateError(HttpStatusCode status, string content, string operation)
	{
		KisErrorResponse error = null;
		try { error = Deserialize<KisErrorResponse>(content); }
		catch (JsonException) { }
		var message = error?.Message.IsEmpty(content);
		if (message?.Length > 1000)
			message = message[..1000];
		return new HttpRequestException($"KIS {operation} HTTP {(int)status}" +
			(error?.MessageCode.IsEmpty() == false ? $"/{error.MessageCode}" : string.Empty) +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, status);
	}

	private static string BuildQuery(object query)
	{
		if (query == null)
			return string.Empty;
		var values = query.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Select(property => (property, attribute: property.GetCustomAttribute<JsonPropertyAttribute>()))
			.Where(item => item.attribute != null)
			.Select(item => $"{Uri.EscapeDataString(item.attribute.PropertyName)}={Uri.EscapeDataString(
				System.Convert.ToString(item.property.GetValue(query), CultureInfo.InvariantCulture) ?? string.Empty)}");
		return "?" + string.Join("&", values);
	}

	private KisOrderExecution[] ConvertExecutions(IEnumerable<KisExecutionItem> items,
		KoreaInvestmentMarkets defaultMarket)
		=> [.. (items ?? []).Where(item => !item.OrderNumber.IsEmpty()).Select(item =>
		{
			var market = TryGetOverseasMarket(item.ExchangeCode, defaultMarket);
			var security = KisSecurityInfo.Create(item.ProductNumber.IsEmpty(item.ShortProductNumber), market, null);
			return new KisOrderExecution(item.OrderNumber, item.OriginalOrderNumber, security,
				item.SideCode == "02" ? Sides.Buy : Sides.Sell, item.OrderQuantity.ToDecimal() ?? 0,
				item.OrderPrice.ToDecimal() ?? item.OverseasOrderPrice.ToDecimal(),
				item.FilledQuantity.ToDecimal() ?? item.AlternativeFilledQuantity.ToDecimal() ?? 0,
				item.AveragePrice.ToDecimal() ?? item.AlternativeAveragePrice.ToDecimal(),
				item.OrderDate.ToKisUtc(item.OrderTime, security), item.IsCanceled == "Y",
				item.Name.IsEmpty(item.OverseasName));
		})];

	private static KisCandleBar Convert(KisDomesticCandle candle, KisSecurityInfo security)
		=> new(candle.Date.ToKisUtc(candle.Time, security),
			candle.Open.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.High.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.Low.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.Close.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.MinuteVolume.ToDecimal() ?? candle.Volume.ToDecimal() ?? 0,
			candle.Turnover.ToDecimal());

	private static KisCandleBar Convert(KisDerivativeCandle candle, KisSecurityInfo security)
		=> new(candle.StockDate.IsEmpty(candle.Date).ToKisUtc(candle.StockTime.IsEmpty(candle.Time), security),
			candle.Open.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.High.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.Low.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.Close.ToDecimal() ?? candle.CurrentPrice.ToDecimal() ?? 0,
			candle.MinuteVolume.ToDecimal() ?? candle.Volume.ToDecimal() ?? 0,
			candle.Turnover.ToDecimal());

	private static KisCandleBar Convert(KisOverseasCandle candle, KisSecurityInfo security)
	{
		var date = candle.Date;
		var time = candle.Time;
		if (!candle.DateTime.IsEmpty() && candle.DateTime.Length >= 12)
		{
			date = candle.DateTime[..8];
			time = candle.DateTime[8..];
		}
		return new(date.ToKisUtc(time, security), candle.Open.ToDecimal() ?? candle.Last.ToDecimal() ?? 0,
			candle.High.ToDecimal() ?? candle.Last.ToDecimal() ?? 0,
			candle.Low.ToDecimal() ?? candle.Last.ToDecimal() ?? 0,
			candle.Close.ToDecimal() ?? candle.Last.ToDecimal() ?? 0,
			candle.MinuteVolume.ToDecimal() ?? candle.Volume.ToDecimal() ?? 0, candle.Turnover.ToDecimal());
	}

	private static KisCandleBar[] Aggregate(IEnumerable<KisCandleBar> candles, TimeSpan timeFrame)
		=> [.. candles.GroupBy(c => c.OpenTime.Floor(timeFrame)).Select(group =>
		{
			var ordered = group.OrderBy(c => c.OpenTime).ToArray();
			return new KisCandleBar(group.Key, ordered[0].Open, ordered.Max(c => c.High), ordered.Min(c => c.Low),
				ordered[^1].Close, ordered.Sum(c => c.Volume), ordered.All(c => c.Turnover is null) ? null : ordered.Sum(c => c.Turnover ?? 0));
		}).OrderBy(c => c.OpenTime)];

	private static KoreaInvestmentMarkets TryGetOverseasMarket(string exchange,
		KoreaInvestmentMarkets fallback)
		=> exchange?.ToUpperInvariant() switch
		{
			"NASD" or "NAS" => KoreaInvestmentMarkets.Nasdaq,
			"NYSE" or "NYS" => KoreaInvestmentMarkets.Nyse,
			"AMEX" or "AMS" => KoreaInvestmentMarkets.Amex,
			"SEHK" or "HKS" => KoreaInvestmentMarkets.HongKong,
			"SHAA" or "SHS" => KoreaInvestmentMarkets.Shanghai,
			"SZAA" or "SZS" => KoreaInvestmentMarkets.Shenzhen,
			"TKSE" or "TSE" => KoreaInvestmentMarkets.Tokyo,
			"HASE" or "HNX" => KoreaInvestmentMarkets.Hanoi,
			"VNSE" or "HSX" => KoreaInvestmentMarkets.HoChiMinh,
			_ => fallback,
		};

	private static string ToLocalDate(DateTime time, KisSecurityInfo security)
		=> TimeZoneInfo.ConvertTimeFromUtc(time.UtcKind(), security.TimeZone).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	private static string ToPeriod(TimeSpan timeFrame)
		=> timeFrame >= TimeSpan.FromDays(30) ? "M" : timeFrame >= TimeSpan.FromDays(7) ? "W" : "D";

	private static InvalidOperationException Empty(KisOperations operation)
		=> new($"KIS {operation} returned no output.");

	private void EnsureAccount()
	{
		_accountNumber.ThrowIfEmpty(nameof(_accountNumber));
		_productCode.ThrowIfEmpty(nameof(_productCode));
	}

	private Uri Root => _isDemo ? _simulationRoot : _productionRoot;

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is { } delta)
			return delta < TimeSpan.Zero ? TimeSpan.Zero : delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authenticationLock.Dispose();
		base.DisposeManaged();
	}
}
