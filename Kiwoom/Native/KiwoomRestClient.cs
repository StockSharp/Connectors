namespace StockSharp.Kiwoom.Native;

sealed class KiwoomRestClient : BaseLogReceiver
{
	private static readonly Uri _productionRoot = new("https://api.kiwoom.com/");
	private static readonly Uri _simulationRoot = new("https://mockapi.kiwoom.com/");

	private readonly HttpClient _http = new();
	private readonly string _appKey;
	private readonly string _appSecret;
	private readonly bool _isDemo;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _authenticationLock = new(1, 1);
	private readonly SemaphoreSlim _rateLock = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private string _accessToken;
	private DateTime _accessTokenExpiry;
	private DateTime _nextRequestAt;

	public KiwoomRestClient(string appKey, string appSecret, bool isDemo, int maxAttempts)
	{
		_appKey = appKey.ThrowIfEmpty(nameof(appKey));
		_appSecret = appSecret.ThrowIfEmpty(nameof(appSecret));
		_isDemo = isDemo;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(Kiwoom) + "_REST";

	public string AccessToken => _accessToken;

	public async Task<string> GetAccessToken(CancellationToken cancellationToken)
	{
		await EnsureAuthenticated(cancellationToken);
		return _accessToken;
	}

	public Task Connect(CancellationToken cancellationToken)
		=> Authenticate(cancellationToken);

	public async Task<KiwoomSecurityDefinition[]> GetSecurities(CancellationToken cancellationToken)
	{
		var result = new List<KiwoomSecurityDefinition>();
		foreach (var marketType in new[] { "0", "10", "50", "6", "8", "60", "3" })
		{
			var continuation = default(string);
			var nextKey = default(string);
			for (var pageIndex = 0; pageIndex < 100; pageIndex++)
			{
				var page = await Send<KiwoomDomesticStockListRequest, KiwoomDomesticStockListResponse>(
					KiwoomRoutes.DomesticStockInfo, KiwoomRoutes.DomesticStockList,
					new() { MarketType = marketType }, continuation, nextKey, cancellationToken);
				foreach (var item in page.Body.Securities ?? [])
				{
					if (item.Code.IsEmpty())
						continue;
					var info = KiwoomSecurityInfo.Create(item.Code, KiwoomMarkets.Krx);
					result.Add(new(info, item.Name.IsEmpty(item.Code), item.Name.IsEmpty(item.Code), InferDomesticType(marketType, item)));
				}
				if (!IsContinuation(page))
					break;
				continuation = page.Continuation;
				nextKey = page.NextKey;
			}
		}

		var usContinuation = default(string);
		var usNextKey = default(string);
		for (var pageIndex = 0; pageIndex < 100; pageIndex++)
		{
			var page = await Send<KiwoomUsStockListRequest, KiwoomUsStockListResponse>(
				KiwoomRoutes.UsStockInfo, KiwoomRoutes.UsStockList, new(), usContinuation, usNextKey, cancellationToken);
			foreach (var item in page.Body.Securities ?? [])
			{
				if (item.Code.IsEmpty() || !TryGetUsMarket(item.ExchangeType, out var market))
					continue;
				var info = KiwoomSecurityInfo.Create(item.Code, market);
				var name = item.EnglishName.IsEmpty(item.Name).IsEmpty(item.Code);
				result.Add(new(info, name, item.Name.IsEmpty(name), item.IsEtf.EqualsIgnoreCase("Y") ? SecurityTypes.Etf : SecurityTypes.Stock));
			}
			if (!IsContinuation(page))
				break;
			usContinuation = page.Continuation;
			usNextKey = page.NextKey;
		}

		return [.. result.GroupBy(item => item.Security.ToSecurityId()).Select(group => group.First())];
	}

	public async Task<KiwoomQuoteSnapshot> GetQuote(KiwoomSecurityInfo security, CancellationToken cancellationToken)
	{
		if (security.AssetClass == KiwoomAssetClasses.DomesticStock)
		{
			var quote = (await Send<KiwoomDomesticSecurityRequest, KiwoomDomesticQuoteResponse>(
				KiwoomRoutes.DomesticStockInfo, KiwoomRoutes.DomesticSecurityInfo,
				new() { SecurityCode = security.Code }, null, null, cancellationToken)).Body;
			var depth = await GetDepth(security, cancellationToken);
			return new(quote.LastPrice.ToPrice(), quote.OpenPrice.ToPrice(), quote.HighPrice.ToPrice(), quote.LowPrice.ToPrice(),
				quote.PreviousClose.ToPrice(), quote.Volume.ToDecimal(), null,
				depth.BidPrices.FirstOrDefault(), depth.BidVolumes.FirstOrDefault(),
				depth.AskPrices.FirstOrDefault(), depth.AskVolumes.FirstOrDefault(), DateTime.UtcNow);
		}

		var usQuote = (await Send<KiwoomUsSecurityRequest, KiwoomUsQuoteResponse>(
			KiwoomRoutes.UsMarket, KiwoomRoutes.UsQuote,
			new() { ExchangeType = security.ExchangeCode, SecurityCode = security.Code }, null, null, cancellationToken)).Body;
		var usDepth = await GetDepth(security, cancellationToken);
		return new(usQuote.LastPrice.ToPrice(), usQuote.OpenPrice.ToPrice(), usQuote.HighPrice.ToPrice(), usQuote.LowPrice.ToPrice(),
			usQuote.PreviousClose.ToPrice(), usQuote.Volume.ToDecimal(), null,
			usDepth.BidPrices.FirstOrDefault(), usDepth.BidVolumes.FirstOrDefault(),
			usDepth.AskPrices.FirstOrDefault(), usDepth.AskVolumes.FirstOrDefault(), DateTime.UtcNow);
	}

	public async Task<KiwoomDepthSnapshot> GetDepth(KiwoomSecurityInfo security, CancellationToken cancellationToken)
	{
		if (security.AssetClass == KiwoomAssetClasses.DomesticStock)
		{
			var depth = (await Send<KiwoomDomesticSecurityRequest, KiwoomDomesticDepthResponse>(
				KiwoomRoutes.DomesticMarket, KiwoomRoutes.DomesticDepth,
				new() { SecurityCode = security.Code }, null, null, cancellationToken)).Body;
			return new(ToPrices(depth.BidPrices), ToValues(depth.BidVolumes), ToPrices(depth.AskPrices), ToValues(depth.AskVolumes),
				string.Empty.ToKiwoomUtc(depth.Time, security));
		}

		var usDepth = (await Send<KiwoomUsSecurityRequest, KiwoomUsDepthResponse>(
			KiwoomRoutes.UsMarket, KiwoomRoutes.UsDepth,
			new() { ExchangeType = security.ExchangeCode, SecurityCode = security.Code }, null, null, cancellationToken)).Body;
		return new(ToPrices(usDepth.BidPrices), ToValues(usDepth.BidVolumes), ToPrices(usDepth.AskPrices), ToValues(usDepth.AskVolumes),
			usDepth.Date.ToKiwoomUtc(usDepth.Time, security));
	}

	public async Task<KiwoomCandleBar[]> GetCandles(KiwoomSecurityInfo security, TimeSpan timeFrame,
		DateTime? from, DateTime? to, long? count, CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).UtcKind();
		var start = (from ?? end - (timeFrame >= TimeSpan.FromDays(1) ? TimeSpan.FromDays(365) : TimeSpan.FromDays(7))).UtcKind();
		var result = security.AssetClass == KiwoomAssetClasses.DomesticStock
			? await GetDomesticCandles(security, timeFrame, start, end, cancellationToken)
			: await GetUsCandles(security, timeFrame, start, end, cancellationToken);
		IEnumerable<KiwoomCandleBar> filtered = result
			.Where(candle => candle.OpenTime >= start && candle.OpenTime <= end)
			.GroupBy(candle => candle.OpenTime)
			.Select(group => group.First())
			.OrderBy(candle => candle.OpenTime);
		if (count is > 0)
			filtered = filtered.TakeLast((int)Math.Min(count.Value, int.MaxValue));
		return [.. filtered];
	}

	public async Task<KiwoomPosition[]> GetPositions(CancellationToken cancellationToken)
	{
		var result = new List<KiwoomPosition>();
		var domestic = await GetAllPages<KiwoomDomesticPositionsRequest, KiwoomDomesticPositionsResponse, KiwoomDomesticPosition>(
			KiwoomRoutes.DomesticAccount, KiwoomRoutes.DomesticPositions, new(), response => response.Positions,
			cancellationToken);
		result.AddRange(domestic.Where(item => !item.SecurityCode.IsEmpty()).Select(item =>
		{
			var info = KiwoomSecurityInfo.Create(CleanDomesticCode(item.SecurityCode), KiwoomMarkets.Krx);
			return new KiwoomPosition(info, item.Quantity.ToDecimal() ?? 0, item.AveragePrice.ToPrice(), item.CurrentPrice.ToPrice(),
				item.UnrealizedPnL.ToDecimal(), item.MarketValue.ToDecimal());
		}));

		try
		{
			var overseas = await GetAllPages<KiwoomUsPositionsRequest, KiwoomUsPositionsResponse, KiwoomUsPosition>(
				KiwoomRoutes.UsAccount, KiwoomRoutes.UsPositions, new(), response => response.Positions,
				cancellationToken);
			result.AddRange(overseas.Where(item => !item.SecurityCode.IsEmpty()).Select(item =>
			{
				var market = GetUsMarket(item.ExchangeName);
				var info = KiwoomSecurityInfo.Create(item.SecurityCode, market);
				return new KiwoomPosition(info, item.Quantity.ToDecimal() ?? 0, item.AveragePrice.ToPrice(), item.CurrentPrice.ToPrice(),
					item.UnrealizedPnL.ToDecimal(), item.MarketValue.ToDecimal());
			}));
		}
		catch (HttpRequestException ex)
		{
			this.AddWarningLog("Kiwoom US positions are unavailable: {0}", ex.Message);
		}
		return [.. result];
	}

	public async Task<KiwoomOrderExecution[]> GetOrders(DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var result = new List<KiwoomOrderExecution>();
		var domesticOpen = await GetAllPages<KiwoomDomesticOpenOrdersRequest, KiwoomDomesticOpenOrdersResponse, KiwoomDomesticOrderRow>(
			KiwoomRoutes.DomesticAccount, KiwoomRoutes.DomesticOpenOrders, new(), response => response.Orders,
			cancellationToken);
		result.AddRange(domesticOpen.Select(ConvertDomesticOrder));
		var domesticExecutions = await GetAllPages<KiwoomDomesticExecutionsRequest, KiwoomDomesticExecutionsResponse, KiwoomDomesticOrderRow>(
			KiwoomRoutes.DomesticAccount, KiwoomRoutes.DomesticExecutions, new(), response => response.Orders,
			cancellationToken);
		result.AddRange(domesticExecutions.Select(ConvertDomesticOrder));

		for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
		{
			try
			{
				var value = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
				var usOpen = await GetAllPages<KiwoomUsOpenOrdersRequest, KiwoomUsOpenOrdersResponse, KiwoomUsOrderRow>(
					KiwoomRoutes.UsAccount, KiwoomRoutes.UsOpenOrders, new() { OrderDate = value }, response => response.Orders,
					cancellationToken);
				result.AddRange(usOpen.Select(ConvertUsOrder));
				var usExecutions = await GetAllPages<KiwoomUsExecutionsRequest, KiwoomUsExecutionsResponse, KiwoomUsOrderRow>(
					KiwoomRoutes.UsAccount, KiwoomRoutes.UsExecutions, new() { OrderDate = value }, response => response.Orders,
					cancellationToken);
				result.AddRange(usExecutions.Select(ConvertUsOrder));
			}
			catch (HttpRequestException ex)
			{
				this.AddWarningLog("Kiwoom US orders for {0:yyyy-MM-dd} are unavailable: {1}", date, ex.Message);
			}
		}

		return [.. result.Where(order => !order.OrderNumber.IsEmpty())
			.GroupBy(order => $"{order.OrderNumber}:{order.TradeNumber}:{order.FilledQuantity}:{order.Balance}")
			.Select(group => group.OrderByDescending(order => order.Time).First())];
	}

	public async Task<string> PlaceDomesticOrder(KiwoomDomesticOrderRequest request, Sides side,
		CancellationToken cancellationToken)
	{
		var apiId = side == Sides.Buy ? KiwoomRoutes.DomesticBuy : KiwoomRoutes.DomesticSell;
		var result = (await Send<KiwoomDomesticOrderRequest, KiwoomDomesticOrderResponse>(
			KiwoomRoutes.DomesticOrder, apiId, request, null, null, cancellationToken)).Body;
		return result.OrderNumber.ThrowIfEmpty(nameof(result.OrderNumber));
	}

	public async Task<string> PlaceUsOrder(KiwoomUsOrderRequest request, Sides side,
		CancellationToken cancellationToken)
	{
		var apiId = side == Sides.Buy ? KiwoomRoutes.UsBuy : KiwoomRoutes.UsSell;
		var result = (await Send<KiwoomUsOrderRequest, KiwoomUsOrderResponse>(
			KiwoomRoutes.UsOrder, apiId, request, null, null, cancellationToken)).Body;
		return result.OrderNumber.ThrowIfEmpty(nameof(result.OrderNumber));
	}

	public async Task<string> ReplaceDomesticOrder(KiwoomDomesticReplaceRequest request, CancellationToken cancellationToken)
	{
		var result = (await Send<KiwoomDomesticReplaceRequest, KiwoomDomesticOrderResponse>(
			KiwoomRoutes.DomesticOrder, KiwoomRoutes.DomesticReplace, request, null, null, cancellationToken)).Body;
		return result.OrderNumber.ThrowIfEmpty(nameof(result.OrderNumber));
	}

	public async Task<string> ReplaceUsOrder(KiwoomUsReplaceRequest request, CancellationToken cancellationToken)
	{
		var result = (await Send<KiwoomUsReplaceRequest, KiwoomUsOrderResponse>(
			KiwoomRoutes.UsOrder, KiwoomRoutes.UsReplace, request, null, null, cancellationToken)).Body;
		return result.OrderNumber.ThrowIfEmpty(nameof(result.OrderNumber));
	}

	public Task CancelDomesticOrder(KiwoomDomesticCancelRequest request, CancellationToken cancellationToken)
		=> SendWithoutResult<KiwoomDomesticCancelRequest, KiwoomDomesticOrderResponse>(
			KiwoomRoutes.DomesticOrder, KiwoomRoutes.DomesticCancel, request, cancellationToken);

	public Task CancelUsOrder(KiwoomUsCancelRequest request, CancellationToken cancellationToken)
		=> SendWithoutResult<KiwoomUsCancelRequest, KiwoomUsOrderResponse>(
			KiwoomRoutes.UsOrder, KiwoomRoutes.UsCancel, request, cancellationToken);

	private async Task SendWithoutResult<TRequest, TResponse>(string path, string apiId, TRequest request,
		CancellationToken cancellationToken)
		where TResponse : KiwoomResponse
		=> _ = await Send<TRequest, TResponse>(path, apiId, request, null, null, cancellationToken);

	private async Task<KiwoomCandleBar[]> GetDomesticCandles(KiwoomSecurityInfo security, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var result = new List<KiwoomCandleBar>();
		var continuation = default(string);
		var nextKey = default(string);
		for (var pageIndex = 0; pageIndex < 100; pageIndex++)
		{
			KiwoomRestPage<KiwoomResponse> page;
			KiwoomDomesticCandle[] candles;
			if (timeFrame >= TimeSpan.FromDays(1))
			{
				var typed = await Send<KiwoomDomesticDailyCandleRequest, KiwoomDomesticDailyCandleResponse>(
					KiwoomRoutes.DomesticChart, KiwoomRoutes.DomesticDailyCandles,
					new() { SecurityCode = security.Code, BaseDate = ToLocalDate(to, security) }, continuation, nextKey, cancellationToken);
				page = new(typed.Body, typed.Continuation, typed.NextKey);
				candles = typed.Body.Candles ?? [];
			}
			else
			{
				var typed = await Send<KiwoomDomesticMinuteCandleRequest, KiwoomDomesticMinuteCandleResponse>(
					KiwoomRoutes.DomesticChart, KiwoomRoutes.DomesticMinuteCandles,
					new() { SecurityCode = security.Code, BaseDate = ToLocalDate(to, security), Interval = ((int)timeFrame.TotalMinutes).ToString(CultureInfo.InvariantCulture) },
					continuation, nextKey, cancellationToken);
				page = new(typed.Body, typed.Continuation, typed.NextKey);
				candles = typed.Body.Candles ?? [];
			}
			result.AddRange(candles.Select(item => Convert(item, security)));
			if (!IsContinuation(page) || result.Count > 0 && result.Min(item => item.OpenTime) <= from)
				break;
			continuation = page.Continuation;
			nextKey = page.NextKey;
		}
		return [.. result];
	}

	private async Task<KiwoomCandleBar[]> GetUsCandles(KiwoomSecurityInfo security, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var result = new List<KiwoomCandleBar>();
		var continuation = default(string);
		var nextKey = default(string);
		for (var pageIndex = 0; pageIndex < 100; pageIndex++)
		{
			KiwoomRestPage<KiwoomUsCandleResponse> page;
			if (timeFrame >= TimeSpan.FromDays(1))
				page = await Send<KiwoomUsDailyCandleRequest, KiwoomUsCandleResponse>(
					KiwoomRoutes.UsChart, KiwoomRoutes.UsDailyCandles,
					new() { ExchangeType = security.ExchangeCode, SecurityCode = security.Code, StartDate = ToLocalDate(to, security) },
					continuation, nextKey, cancellationToken);
			else
				page = await Send<KiwoomUsMinuteCandleRequest, KiwoomUsCandleResponse>(
					KiwoomRoutes.UsChart, KiwoomRoutes.UsMinuteCandles,
					new() { ExchangeType = security.ExchangeCode, SecurityCode = security.Code, StartDate = ToLocalDate(to, security), Interval = ((int)timeFrame.TotalMinutes).ToString(CultureInfo.InvariantCulture) },
					continuation, nextKey, cancellationToken);
			result.AddRange((page.Body.Candles ?? []).Select(item => Convert(item, security)));
			if (!IsContinuation(page) || result.Count > 0 && result.Min(item => item.OpenTime) <= from)
				break;
			continuation = page.Continuation;
			nextKey = page.NextKey;
		}
		return [.. result];
	}

	private async Task<TItem[]> GetAllPages<TRequest, TResponse, TItem>(string path, string apiId,
		TRequest request, Func<TResponse, TItem[]> selector, CancellationToken cancellationToken)
		where TResponse : KiwoomResponse
	{
		var result = new List<TItem>();
		var continuation = default(string);
		var nextKey = default(string);
		for (var pageIndex = 0; pageIndex < 100; pageIndex++)
		{
			var page = await Send<TRequest, TResponse>(path, apiId, request, continuation, nextKey, cancellationToken);
			result.AddRange(selector(page.Body) ?? []);
			if (!IsContinuation(page))
				break;
			continuation = page.Continuation;
			nextKey = page.NextKey;
		}
		return [.. result];
	}

	private async Task<KiwoomRestPage<TResponse>> Send<TRequest, TResponse>(string path, string apiId,
		TRequest request, string continuation, string nextKey, CancellationToken cancellationToken)
		where TResponse : KiwoomResponse
	{
		await EnsureAuthenticated(cancellationToken);
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimit(cancellationToken);
			using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(Root, path));
			message.Headers.TryAddWithoutValidation("authorization", $"Bearer {_accessToken}");
			message.Headers.TryAddWithoutValidation("api-id", apiId);
			if (!continuation.IsEmpty())
				message.Headers.TryAddWithoutValidation("cont-yn", continuation);
			if (!nextKey.IsEmpty())
				message.Headers.TryAddWithoutValidation("next-key", nextKey);
			message.Content = new StringContent(JsonConvert.SerializeObject(request, _jsonSettings), Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync(message, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);

			if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
			{
				_accessTokenExpiry = default;
				await Authenticate(cancellationToken);
				continue;
			}
			if (!IsOrderMutation(apiId) &&
				(response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt + 1 < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, apiId);

			var result = Deserialize<TResponse>(content) ?? throw new InvalidOperationException($"Kiwoom {apiId} returned an empty response.");
			if (!result.IsSuccess)
				throw new InvalidOperationException($"Kiwoom {apiId} error {result.ReturnCode}: {result.ReturnMessage}");
			response.Headers.TryGetValues("cont-yn", out var continuationValues);
			response.Headers.TryGetValues("next-key", out var nextKeyValues);
			return new(result, continuationValues?.FirstOrDefault(), nextKeyValues?.FirstOrDefault());
		}
	}

	private async Task EnsureAuthenticated(CancellationToken cancellationToken)
	{
		if (_accessToken.IsEmpty() || _accessTokenExpiry <= DateTime.UtcNow.AddMinutes(2))
			await Authenticate(cancellationToken);
	}

	private async Task Authenticate(CancellationToken cancellationToken)
	{
		await _authenticationLock.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() && _accessTokenExpiry > DateTime.UtcNow.AddMinutes(2))
				return;
			using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(Root, KiwoomRoutes.Token))
			{
				Content = new StringContent(JsonConvert.SerializeObject(new KiwoomTokenRequest
				{
					AppKey = _appKey,
					SecretKey = _appSecret,
				}, _jsonSettings), Encoding.UTF8, "application/json"),
			};
			using var response = await _http.SendAsync(message, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, "OAuth");
			var token = Deserialize<KiwoomTokenResponse>(content) ?? throw new InvalidOperationException("Kiwoom OAuth returned an empty response.");
			if (token.ReturnCode != 0)
				throw new InvalidOperationException($"Kiwoom OAuth error {token.ReturnCode}: {token.ReturnMessage}");
			_accessToken = token.Token.ThrowIfEmpty(nameof(token.Token));
			_accessTokenExpiry = DateTime.UtcNow.AddHours(23);
		}
		finally
		{
			_authenticationLock.Release();
		}
	}

	private async Task WaitRateLimit(CancellationToken cancellationToken)
	{
		await _rateLock.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestAt - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestAt = DateTime.UtcNow + (_isDemo ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(200));
		}
		finally
		{
			_rateLock.Release();
		}
	}

	private T Deserialize<T>(string content)
		=> content.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(content, _jsonSettings);

	private Exception CreateError(HttpStatusCode status, string content, string operation)
	{
		KiwoomErrorResponse error = null;
		try { error = Deserialize<KiwoomErrorResponse>(content); }
		catch (JsonException) { }
		var detail = error?.ReturnMessage.IsEmpty(content);
		if (detail?.Length > 1000)
			detail = detail[..1000];
		return new HttpRequestException($"Kiwoom {operation} HTTP {(int)status}" +
			(error == null ? string.Empty : $"/{error.ReturnCode}") + (detail.IsEmpty() ? string.Empty : $": {detail}"), null, status);
	}

	private static KiwoomCandleBar Convert(KiwoomDomesticCandle item, KiwoomSecurityInfo security)
	{
		var dateTime = item.DateTime;
		var date = item.Date;
		var time = string.Empty;
		if (!dateTime.IsEmpty())
		{
			var digits = new string(dateTime.Where(char.IsDigit).ToArray());
			if (digits.Length >= 8)
			{
				date = digits[..8];
				time = digits.Length > 8 ? digits[8..] : string.Empty;
			}
		}
		var close = item.Close.ToPrice() ?? 0;
		return new(date.ToKiwoomUtc(time, security), item.Open.ToPrice() ?? close, item.High.ToPrice() ?? close,
			item.Low.ToPrice() ?? close, close, item.Volume.ToDecimal() ?? 0, item.Turnover.ToDecimal());
	}

	private static KiwoomCandleBar Convert(KiwoomUsCandle item, KiwoomSecurityInfo security)
	{
		var date = item.BusinessDate.IsEmpty(item.Date);
		var close = item.Close.ToPrice() ?? 0;
		return new(date.ToKiwoomUtc(item.Time, security), item.Open.ToPrice() ?? close, item.High.ToPrice() ?? close,
			item.Low.ToPrice() ?? close, close, item.Volume.ToDecimal() ?? item.AccumulatedVolume.ToDecimal() ?? 0,
			item.Turnover.ToDecimal());
	}

	private static KiwoomOrderExecution ConvertDomesticOrder(KiwoomDomesticOrderRow item)
	{
		var market = item.ExchangeType.EqualsIgnoreCase("NXT") || item.ExchangeName?.Contains("NXT", StringComparison.OrdinalIgnoreCase) == true
			? KiwoomMarkets.Nxt : KiwoomMarkets.Krx;
		var info = KiwoomSecurityInfo.Create(CleanDomesticCode(item.SecurityCode), market);
		var side = item.SideName?.Contains("매수", StringComparison.Ordinal) == true ? Sides.Buy : Sides.Sell;
		return new(item.OrderNumber, item.OriginalOrderNumber, info, side, item.OrderQuantity.ToDecimal() ?? 0,
			item.OrderPrice.ToPrice(), item.FillQuantity.ToDecimal() ?? 0, item.FillPrice.ToPrice(),
			item.Balance.ToDecimal() ?? 0, string.Empty.ToKiwoomUtc(item.OrderTime.IsEmpty(item.Time), info),
			item.Status, item.TradeNumber);
	}

	private static KiwoomOrderExecution ConvertUsOrder(KiwoomUsOrderRow item)
	{
		var info = KiwoomSecurityInfo.Create(item.SecurityCode, GetUsMarket(item.ExchangeName));
		var side = item.SideType is "2" or "02" || item.SideName?.Contains("매수", StringComparison.Ordinal) == true ? Sides.Buy : Sides.Sell;
		var filled = item.FillQuantity.ToDecimal() ?? 0;
		var volume = item.OrderQuantity.ToDecimal() ?? 0;
		return new(item.OrderNumber, item.OriginalOrderNumber, info, side, volume, item.OrderPrice.ToPrice(), filled,
			item.FillPrice.ToPrice(), item.Balance.ToDecimal() ?? Math.Max(0, volume - filled),
			string.Empty.ToKiwoomUtc(item.FillTime.IsEmpty(item.OrderTime), info), item.StatusName.IsEmpty(item.Status), string.Empty);
	}

	private static SecurityTypes InferDomesticType(string marketType, KiwoomDomesticStock item)
		=> marketType switch
		{
			"3" => SecurityTypes.Warrant,
			"6" => SecurityTypes.Stock,
			"8" => SecurityTypes.Etf,
			"60" => SecurityTypes.Etf,
			_ when item.ProductName?.Contains("ETF", StringComparison.OrdinalIgnoreCase) == true => SecurityTypes.Etf,
			_ => SecurityTypes.Stock,
		};

	private static decimal[] ToPrices(IEnumerable<string> values)
		=> [.. values.Select(value => value.ToPrice() ?? 0)];

	private static decimal[] ToValues(IEnumerable<string> values)
		=> [.. values.Select(value => value.ToDecimal() ?? 0)];

	private static bool IsContinuation<T>(KiwoomRestPage<T> page)
		=> page.Continuation.EqualsIgnoreCase("Y") && !page.NextKey.IsEmpty();

	private static string CleanDomesticCode(string code)
		=> code?.Length > 6 && char.IsLetter(code[0]) ? code[1..] : code;

	private static bool TryGetUsMarket(string value, out KiwoomMarkets market)
	{
		market = value?.ToUpperInvariant() switch
		{
			"ND" or "NASDAQ" => KiwoomMarkets.Nasdaq,
			"NY" or "NYSE" => KiwoomMarkets.Nyse,
			"NA" or "AMEX" or "NYSE AMERICAN" => KiwoomMarkets.Amex,
			_ => default,
		};
		return value?.ToUpperInvariant() is "ND" or "NASDAQ" or "NY" or "NYSE" or "NA" or "AMEX" or "NYSE AMERICAN";
	}

	private static KiwoomMarkets GetUsMarket(string value)
		=> TryGetUsMarket(value, out var market) ? market : KiwoomMarkets.Nasdaq;

	private static string ToLocalDate(DateTime time, KiwoomSecurityInfo security)
		=> TimeZoneInfo.ConvertTimeFromUtc(time.UtcKind(), security.TimeZone).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	private Uri Root => _isDemo ? _simulationRoot : _productionRoot;

	private static bool IsOrderMutation(string apiId)
		=> apiId is KiwoomRoutes.DomesticBuy or KiwoomRoutes.DomesticSell or KiwoomRoutes.DomesticReplace or
			KiwoomRoutes.DomesticCancel or KiwoomRoutes.UsBuy or KiwoomRoutes.UsSell or
			KiwoomRoutes.UsReplace or KiwoomRoutes.UsCancel;

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
		_rateLock.Dispose();
		base.DisposeManaged();
	}
}
