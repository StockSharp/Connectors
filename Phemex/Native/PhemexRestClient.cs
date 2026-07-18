namespace StockSharp.Phemex.Native;

readonly record struct PhemexParameter(string Name, string Value);

readonly record struct PhemexQuery(string Canonical, string Encoded);

readonly record struct PhemexPreparedRequest(string Query, string Body, string Expiry,
	string Signature);

readonly record struct PhemexSymbolKey(string Value);

readonly record struct PhemexCurrencyKey(string Value);

sealed class PhemexProductContext
{
	public string Symbol { get; init; }
	public PhemexSections Section { get; init; }
	public string BaseCurrency { get; init; }
	public string QuoteCurrency { get; init; }
	public string SettleCurrency { get; init; }
	public int PriceScale { get; init; }
	public int BaseScale { get; init; }
	public int QuoteScale { get; init; }
}

sealed class PhemexRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly Lock _productSync = new();
	private readonly SemaphoreSlim _productLoadSync = new(1, 1);
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly Dictionary<PhemexSymbolKey, PhemexProductContext> _products = [];
	private readonly Dictionary<PhemexCurrencyKey, int> _currencyScales = [];
	private PhemexSymbol[] _spotSymbols = [];
	private PhemexFuturesSymbol[] _futuresSymbols = [];
	private string[] _settleCurrencies = [];
	private bool _areProductsLoaded;
	private DateTime _nextRequestTime;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	public PhemexRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(NormalizeEndpoint(endpoint), UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Phemex-Connector/1.0");
	}

	public override string Name => nameof(Phemex) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _hasher is not null;

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_productLoadSync.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<PhemexSymbol[]> GetSpotSymbolsAsync(CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		return _spotSymbols;
	}

	public async ValueTask<PhemexFuturesSymbol[]> GetFuturesSymbolsAsync(
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		return _futuresSymbols;
	}

	public async ValueTask<PhemexTicker[]> GetTickersAsync(PhemexSections section, string symbol,
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		if (section == PhemexSections.Spot)
		{
			var ticker = await SendMarketPublicAsync<PhemexWireSpotTicker>(HttpMethod.Get,
				"/md/spot/ticker/24hr", [new("symbol", symbol)], cancellationToken);
			return ticker is null ? [] : [NormalizeSpotTicker(ticker)];
		}

		var futuresTicker = await SendMarketPublicAsync<PhemexWireFuturesTicker>(HttpMethod.Get,
			"/md/v3/ticker/24hr", [new("symbol", symbol)], cancellationToken);
		return futuresTicker is null ? [] : [NormalizeFuturesTicker(futuresTicker)];
	}

	public async ValueTask<PhemexBookTicker[]> GetBookTickersAsync(PhemexSections section,
		string symbol, CancellationToken cancellationToken)
	{
		var depth = await GetDepthAsync(symbol, 1, cancellationToken);
		var bid = depth?.Bids?.FirstOrDefault();
		var ask = depth?.Asks?.FirstOrDefault();
		return depth is null ? [] :
		[
			new()
			{
				Symbol = symbol,
				BidPrice = bid?.Price,
				BidSize = bid?.Size,
				AskPrice = ask?.Price,
				AskSize = ask?.Size,
				Timestamp = depth.UpdateTime,
			},
		];
	}

	public async ValueTask<PhemexMarketTrade[]> GetTradesAsync(string symbol, int limit,
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var path = ResolveSection(symbol) == PhemexSections.Spot ? "/md/trade" : "/md/v2/trade";
		var result = await SendMarketPublicAsync<PhemexWireTradeResult>(HttpMethod.Get, path,
			[new("symbol", symbol)], cancellationToken);
		var trades = result?.SpotTrades ?? result?.FuturesTrades ?? [];
		return [.. trades.Take(limit.Min(200).Max(1)).Select((trade, index) =>
			NormalizeTrade(symbol, trade, index))];
	}

	public async ValueTask<PhemexDepthData> GetDepthAsync(string symbol, int limit,
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var path = ResolveSection(symbol) == PhemexSections.Spot
			? "/md/orderbook"
			: "/md/v2/orderbook";
		var result = await SendMarketPublicAsync<PhemexWireDepthResult>(HttpMethod.Get, path,
			[new("symbol", symbol)], cancellationToken);
		return result is null ? null : NormalizeDepth(symbol, result, limit);
	}

	public async ValueTask<PhemexKline[]> GetKlinesAsync(string symbol, TimeSpan timeFrame,
		DateTime? to, int limit, CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var resolution = timeFrame.ToPhemexInterval();
		var requestedLimit = limit.Min(1000).Max(1);
		var wireLimit = requestedLimit <= 100 ? 100 : requestedLimit <= 500 ? 500 : 1000;
		var data = await SendApiPublicAsync<PhemexRowsData<PhemexWireKline>>(HttpMethod.Get,
			"/exchange/public/md/v2/kline/last",
			[
				new("symbol", symbol),
				new("resolution", resolution),
				new("limit", wireLimit.ToString(CultureInfo.InvariantCulture)),
			], cancellationToken);
		return [.. (data?.Rows ?? [])
			.Where(row => to is null || row.Time * 1000 <= to.Value.ToUnixMilliseconds())
			.TakeLast(requestedLimit)
			.Select(row => NormalizeKline(symbol, row))];
	}

	public async ValueTask<PhemexBalance[]> GetSpotBalancesAsync(
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var wallets = await SendApiPrivateAsync<PhemexWireSpotWallet[]>(HttpMethod.Get,
			"/spot/wallets", [], true, cancellationToken) ?? [];
		return [.. wallets.Select(NormalizeSpotBalance)];
	}

	public async ValueTask<PhemexFuturesBalancesData> GetFuturesBalancesAsync(
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var balances = new List<PhemexBalance>();
		foreach (var currency in _settleCurrencies)
		{
			var data = await SendApiPrivateAsync<PhemexWireFuturesAccountResult>(HttpMethod.Get,
				"/g-accounts/accountPositions", [new("currency", currency)], true,
				cancellationToken);
			if (data?.Account is not null)
				balances.Add(NormalizeFuturesBalance(data.Account));
		}
		return new() { Balances = [.. balances], Isolates = [] };
	}

	public async ValueTask<PhemexPosition[]> GetFuturesPositionsAsync(string symbol,
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var currencies = symbol.IsEmpty()
			? _settleCurrencies
			: [GetProduct(symbol).SettleCurrency];
		var positions = new List<PhemexPosition>();
		foreach (var currency in currencies.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var data = await SendApiPrivateAsync<PhemexWireFuturesAccountResult>(HttpMethod.Get,
				"/g-accounts/positions", [new("currency", currency)], true,
				cancellationToken);
			positions.AddRange((data?.Positions ?? [])
				.Where(position => symbol.IsEmpty() || position.Symbol.EqualsIgnoreCase(symbol))
				.Select(NormalizePosition));
		}
		return [.. positions];
	}

	public async ValueTask<PhemexOrderResult> PlaceSpotOrderAsync(PhemexSpotOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		await EnsureProductsAsync(cancellationToken);
		var context = GetProduct(request.Symbol);
		var isQuote = !request.Amount.IsEmpty();
		var wire = new PhemexWireSpotOrderRequest
		{
			Symbol = request.Symbol,
			ClientOrderId = request.ClientOrderId,
			Side = NormalizeRequestSide(request.Side),
			QuantityType = isQuote ? "ByQuote" : "ByBase",
			QuoteQuantity = isQuote ? ScaleToInt64(request.Amount, context.QuoteScale,
				"spot quote quantity") : null,
			BaseQuantity = isQuote ? null : ScaleToInt64(request.Size, context.BaseScale,
				"spot base quantity"),
			Price = request.Price.IsEmpty() ? null : ScaleToInt64(request.Price,
				context.PriceScale, "spot price"),
			OrderType = request.Type.EqualsIgnoreCase("MARKET") ? "Market" : "Limit",
			TimeInForce = request.Policy.ToPhemexTimeInForce(),
		};
		var result = await SendApiPrivateBodyAsync<PhemexWireOrder, PhemexWireSpotOrderRequest>(
			HttpMethod.Post, "/spot/orders", [], wire, false, cancellationToken);
		return result is null ? null : new()
		{
			OrderId = result.OrderId,
			ClientOrderId = result.ClientOrderId.IsEmpty(request.ClientOrderId),
		};
	}

	public async ValueTask<PhemexOrderResult> PlaceFuturesOrderAsync(
		PhemexFuturesOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		await EnsureProductsAsync(cancellationToken);
		_ = GetProduct(request.Symbol);
		var wire = new PhemexWireFuturesOrderRequest
		{
			ClientOrderId = request.ClientOrderId,
			Symbol = request.Symbol,
			IsReduceOnly = request.IsReduceOnly,
			Quantity = request.Size,
			OrderType = request.Type.EqualsIgnoreCase("MARKET") ? "Market" : "Limit",
			Price = request.Price,
			Side = NormalizeRequestSide(request.Side),
			PositionSide = request.PositionSide,
			TimeInForce = request.Policy.ToPhemexTimeInForce(),
		};
		var result = await SendApiPrivateBodyAsync<PhemexWireOrder, PhemexWireFuturesOrderRequest>(
			HttpMethod.Post, "/g-orders", [], wire, false, cancellationToken);
		return result is null ? null : new()
		{
			OrderId = result.OrderId,
			ClientOrderId = result.ClientOrderId.IsEmpty(request.ClientOrderId),
		};
	}

	public async ValueTask CancelOrderAsync(PhemexSections section,
		PhemexCancelOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var parameters = section == PhemexSections.Spot
			? new[] { new PhemexParameter("symbol", request.Symbol), new("orderID", request.OrderId) }
			: [new("orderID", request.OrderId), new("posSide", request.PositionSide.IsEmpty("Merged")),
				new("symbol", request.Symbol)];
		_ = await SendApiPrivateAsync<PhemexWireOrder>(HttpMethod.Delete,
			section == PhemexSections.Spot ? "/spot/orders" : "/g-orders/cancel",
			parameters, false, cancellationToken);
	}

	public async ValueTask<PhemexOrderResult> AmendOrderAsync(PhemexSections section,
		PhemexAmendOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		await EnsureProductsAsync(cancellationToken);
		var context = GetProduct(request.Symbol);
		PhemexParameter[] parameters;
		string path;
		if (section == PhemexSections.Spot)
		{
			var isQuote = !request.QuoteAmount.IsEmpty();
			parameters =
			[
				new("symbol", request.Symbol),
				new("orderID", request.OrderId),
				new("priceEp", ScaleToInt64(request.Price, context.PriceScale,
					"spot amend price").ToString(CultureInfo.InvariantCulture)),
				new("baseQtyEv", isQuote ? null : ScaleToInt64(request.Size, context.BaseScale,
					"spot amend base quantity").ToString(CultureInfo.InvariantCulture)),
				new("quoteQtyEv", isQuote ? ScaleToInt64(request.QuoteAmount, context.QuoteScale,
					"spot amend quote quantity").ToString(CultureInfo.InvariantCulture) : null),
			];
			path = "/spot/orders";
		}
		else
		{
			parameters =
			[
				new("symbol", request.Symbol),
				new("orderID", request.OrderId),
				new("priceRp", request.Price),
				new("orderQtyRq", request.Size),
				new("posSide", request.PositionSide.IsEmpty("Merged")),
			];
			path = "/g-orders/replace";
		}

		var result = await SendApiPrivateAsync<PhemexWireOrder>(HttpMethod.Put, path,
			parameters, false, cancellationToken);
		return result is null ? null : new()
		{
			OrderId = result.OrderId.IsEmpty(request.OrderId),
			ClientOrderId = result.ClientOrderId,
		};
	}

	public async ValueTask CancelAllOrdersAsync(PhemexSections section,
		PhemexCancelAllOrdersRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		_ = await SendApiPrivateAsync<PhemexIgnoredValue>(HttpMethod.Delete,
			section == PhemexSections.Spot ? "/spot/orders/all" : "/g-orders/all",
			[new("symbol", request.Symbol), new("untriggered", "false")], false,
			cancellationToken);
	}

	public async ValueTask<PhemexOrder[]> GetOpenOrdersAsync(PhemexSections section,
		string symbol, int limit, CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var data = await SendApiPrivateAsync<PhemexOrderCollection>(HttpMethod.Get,
			section == PhemexSections.Spot ? "/spot/orders" : "/g-orders/activeList",
			[new("symbol", symbol)], true, cancellationToken);
		return [.. (data?.Items ?? []).Take(limit.Min(200).Max(1))
			.Select(order => NormalizeOrder(section, order))];
	}

	public async ValueTask<PhemexOrder[]> GetOrderHistoryAsync(PhemexSections section,
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var parameters = new List<PhemexParameter>
		{
			new("symbol", symbol),
			new("start", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			new("end", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			new("offset", "0"),
			new("limit", limit.Min(200).Max(1).ToString(CultureInfo.InvariantCulture)),
		};
		PhemexWireOrder[] orders;
		if (section == PhemexSections.Spot)
		{
			orders = await SendRawPrivateAsync<PhemexWireOrder[]>(HttpMethod.Get,
				"/api-data/spots/orders", [.. parameters], true, cancellationToken) ?? [];
		}
		else
		{
			parameters.Add(new("currency", GetProduct(symbol).SettleCurrency));
			parameters.Add(new("withCount", "false"));
			var data = await SendApiPrivateAsync<PhemexOrderCollection>(HttpMethod.Get,
				"/exchange/order/v2/orderList", [.. parameters], true, cancellationToken);
			orders = data?.Items ?? [];
		}
		return [.. orders.Select(order => NormalizeOrder(section, order))];
	}

	public async ValueTask<PhemexFill[]> GetFillsAsync(PhemexSections section, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		await EnsureProductsAsync(cancellationToken);
		var parameters = new List<PhemexParameter>
		{
			new("symbol", symbol),
			new("start", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			new("end", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			new("offset", "0"),
			new("limit", limit.Min(200).Max(1).ToString(CultureInfo.InvariantCulture)),
		};
		PhemexWireFill[] fills;
		if (section == PhemexSections.Spot)
		{
			fills = await SendRawPrivateAsync<PhemexWireFill[]>(HttpMethod.Get,
				"/api-data/spots/trades", [.. parameters], true, cancellationToken) ?? [];
		}
		else
		{
			parameters.Add(new("currency", GetProduct(symbol).SettleCurrency));
			parameters.Add(new("withCount", "false"));
			var data = await SendApiPrivateAsync<PhemexFillCollection>(HttpMethod.Get,
				"/exchange/order/v2/tradingList", [.. parameters], true, cancellationToken);
			fills = data?.Items ?? [];
		}
		return [.. fills.Select(fill => NormalizeFill(section, symbol, fill))];
	}

	private async ValueTask EnsureProductsAsync(CancellationToken cancellationToken)
	{
		if (_areProductsLoaded)
			return;
		await _productLoadSync.WaitAsync(cancellationToken);
		try
		{
			if (_areProductsLoaded)
				return;
			var data = await SendApiPublicAsync<PhemexProductsData>(HttpMethod.Get,
				"/public/products", [], cancellationToken)
				?? throw new InvalidDataException("Phemex product response has no data.");
			var currencyScales = (data.Currencies ?? [])
				.Where(static currency => !currency.Currency.IsEmpty())
				.ToDictionary(static currency => new PhemexCurrencyKey(
					currency.Currency.ToUpperInvariant()), static currency => currency.ValueScale);
			var contexts = new List<PhemexProductContext>();
			var spotSymbols = new List<PhemexSymbol>();
			foreach (var product in data.Products ?? [])
			{
				if (!product.Type.EqualsIgnoreCase("Spot") || product.Symbol.IsEmpty())
					continue;
				var baseScale = GetScale(currencyScales, product.BaseCurrency);
				var quoteScale = GetScale(currencyScales, product.QuoteCurrency);
				contexts.Add(new()
				{
					Symbol = product.Symbol.ToUpperInvariant(),
					Section = PhemexSections.Spot,
					BaseCurrency = product.BaseCurrency?.ToUpperInvariant(),
					QuoteCurrency = product.QuoteCurrency?.ToUpperInvariant(),
					SettleCurrency = product.QuoteCurrency?.ToUpperInvariant(),
					PriceScale = product.PriceScale,
					BaseScale = baseScale,
					QuoteScale = quoteScale,
				});
				spotSymbols.Add(new()
				{
					Symbol = product.Symbol.ToUpperInvariant(),
					Name = product.DisplaySymbol,
					BaseCurrency = product.BaseCurrency?.ToUpperInvariant(),
					QuoteCurrency = product.QuoteCurrency?.ToUpperInvariant(),
					BasePrecision = product.BaseQuantityPrecision,
					QuotePrecision = product.PricePrecision,
					MinTradeSize = Unscale(product.BaseTickSizeEv, baseScale),
					IsEnabled = product.Status.EqualsIgnoreCase("Listed"),
				});
			}

			var futuresSymbols = new List<PhemexFuturesSymbol>();
			foreach (var product in data.PerpetualProducts ?? [])
			{
				if (product.Symbol.IsEmpty() || !product.Type.EqualsIgnoreCase("PerpetualV2") ||
					!product.SubType.EqualsIgnoreCase("Normal"))
					continue;
				var settle = product.SettleCurrency.IsEmpty(product.QuoteCurrency)?.ToUpperInvariant();
				contexts.Add(new()
				{
					Symbol = product.Symbol.ToUpperInvariant(),
					Section = PhemexSections.Futures,
					BaseCurrency = product.BaseCurrency?.ToUpperInvariant(),
					QuoteCurrency = product.QuoteCurrency?.ToUpperInvariant(),
					SettleCurrency = settle,
				});
				futuresSymbols.Add(new()
				{
					Symbol = product.Symbol.ToUpperInvariant(),
					Name = product.DisplaySymbol,
					BaseCurrency = product.BaseCurrency?.ToUpperInvariant(),
					QuoteCurrency = product.QuoteCurrency?.ToUpperInvariant(),
					SettleCurrency = settle,
					BasePrecision = product.QuantityPrecision,
					QuotePrecision = product.PricePrecision,
					BaseStep = product.QuantityStepSize,
					QuoteStep = product.TickSize,
					MinSizeLimit = product.QuantityStepSize,
					Status = product.Status,
				});
			}

			using (_productSync.EnterScope())
			{
				_currencyScales.Clear();
				foreach (var pair in currencyScales)
					_currencyScales.Add(pair.Key, pair.Value);
				_products.Clear();
				foreach (var context in contexts)
					_products[new(context.Symbol)] = context;
				_spotSymbols = [.. spotSymbols];
				_futuresSymbols = [.. futuresSymbols];
				_settleCurrencies = [.. futuresSymbols
					.Where(static symbol => symbol.Status.EqualsIgnoreCase("Listed"))
					.Select(static symbol => symbol.SettleCurrency)
					.Where(static currency => !currency.IsEmpty())
					.Distinct(StringComparer.OrdinalIgnoreCase)];
				_areProductsLoaded = true;
			}
		}
		finally
		{
			_productLoadSync.Release();
		}
	}

	public PhemexSections ResolveSection(string symbol)
		=> GetProduct(symbol).Section;

	private PhemexProductContext GetProduct(string symbol)
	{
		var key = new PhemexSymbolKey(symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant());
		using (_productSync.EnterScope())
		{
			if (_products.TryGetValue(key, out var product))
				return product;
		}
		throw new InvalidOperationException($"Unknown Phemex symbol '{symbol}'. Refresh securities first.");
	}

	public PhemexMarketTrade NormalizeTrade(string symbol, PhemexWireTrade trade, int index)
	{
		var context = GetProduct(symbol);
		return new()
		{
			Symbol = symbol.ToUpperInvariant(),
			TradeId = $"{trade.Timestamp.ToString(CultureInfo.InvariantCulture)}-{index.ToString(CultureInfo.InvariantCulture)}",
			Price = context.Section == PhemexSections.Spot
				? Unscale(trade.Price, context.PriceScale)
				: trade.Price,
			Size = context.Section == PhemexSections.Spot
				? Unscale(trade.Size, context.BaseScale)
				: trade.Size,
			Side = NormalizeResponseSide(trade.Side),
			Timestamp = NanosecondsToMilliseconds(trade.Timestamp),
		};
	}

	public PhemexDepthData NormalizeDepth(string symbol, PhemexWireDepthResult result, int limit)
	{
		var context = GetProduct(symbol);
		var book = result.SpotBook ?? result.FuturesBook;
		return new()
		{
			Bids = NormalizeLevels(book?.Bids, context, limit),
			Asks = NormalizeLevels(book?.Asks, context, limit),
			UpdateTime = NanosecondsToMilliseconds(result.Timestamp > 0
				? result.Timestamp
				: result.DispatchTimestamp),
		};
	}

	public PhemexDepthData NormalizeDepth(string symbol, PhemexWireBook book, long timestamp,
		int limit)
	{
		var context = GetProduct(symbol);
		return new()
		{
			Bids = NormalizeLevels(book?.Bids, context, limit),
			Asks = NormalizeLevels(book?.Asks, context, limit),
			UpdateTime = NanosecondsToMilliseconds(timestamp),
		};
	}

	private static PhemexBookLevel[] NormalizeLevels(PhemexBookLevel[] levels,
		PhemexProductContext context, int limit)
		=> [.. (levels ?? []).Take(limit.Min(30).Max(1)).Select(level => new PhemexBookLevel
		{
			Price = context.Section == PhemexSections.Spot
				? Unscale(level.Price, context.PriceScale)
				: level.Price,
			Size = context.Section == PhemexSections.Spot
				? Unscale(level.Size, context.BaseScale)
				: level.Size,
		})];

	public PhemexTicker NormalizeSpotTicker(PhemexWireSpotTicker ticker)
	{
		var context = GetProduct(ticker.Symbol);
		return new()
		{
			Symbol = ticker.Symbol?.ToUpperInvariant(),
			Time = NanosecondsToMilliseconds(ticker.Timestamp),
			Open = Unscale(ticker.Open, context.PriceScale),
			Close = Unscale(ticker.Close, context.PriceScale),
			High = Unscale(ticker.High, context.PriceScale),
			Low = Unscale(ticker.Low, context.PriceScale),
			Volume = Unscale(ticker.Volume, context.BaseScale),
			Amount = Unscale(ticker.Turnover, context.QuoteScale),
			IndexPrice = Unscale(ticker.IndexPrice, context.PriceScale),
			BidPrice = Unscale(ticker.BidPrice, context.PriceScale),
			AskPrice = Unscale(ticker.AskPrice, context.PriceScale),
		};
	}

	public static PhemexTicker NormalizeFuturesTicker(PhemexWireFuturesTicker ticker)
		=> new()
		{
			Symbol = ticker.Symbol?.ToUpperInvariant(),
			Time = NanosecondsToMilliseconds(ticker.Timestamp),
			Open = ticker.Open,
			Close = ticker.Close,
			High = ticker.High,
			Low = ticker.Low,
			Volume = ticker.Volume,
			Amount = ticker.Turnover,
			IndexPrice = ticker.IndexPrice,
			MarkPrice = ticker.MarkPrice,
			FundingRate = ticker.FundingRate,
			BidPrice = ticker.BidPrice,
			AskPrice = ticker.AskPrice,
		};

	private PhemexKline NormalizeKline(string symbol, PhemexWireKline candle)
	{
		var context = GetProduct(symbol);
		var isSpot = context.Section == PhemexSections.Spot;
		return new()
		{
			Time = candle.Time * 1000,
			Open = isSpot ? Unscale(candle.Open, context.PriceScale) : candle.Open,
			Close = isSpot ? Unscale(candle.Close, context.PriceScale) : candle.Close,
			High = isSpot ? Unscale(candle.High, context.PriceScale) : candle.High,
			Low = isSpot ? Unscale(candle.Low, context.PriceScale) : candle.Low,
			Volume = isSpot ? Unscale(candle.Volume, context.BaseScale) : candle.Volume,
		};
	}

	public PhemexBalance NormalizeSpotBalance(PhemexWireSpotWallet wallet)
	{
		var scale = GetCurrencyScale(wallet.Currency);
		var total = ParseScaled(wallet.Balance, scale);
		var frozen = ParseScaled(wallet.LockedTradingBalance, scale) +
			ParseScaled(wallet.LockedWithdrawBalance, scale);
		return new()
		{
			Coin = wallet.Currency?.ToUpperInvariant(),
			Free = (total - frozen).Max(0m).ToWire(),
			Frozen = frozen.ToWire(),
		};
	}

	public static PhemexBalance NormalizeFuturesBalance(PhemexWireFuturesAccount account)
	{
		var total = account.Balance.ToDecimal() ?? 0m;
		var used = account.UsedBalance.ToDecimal() ?? 0m;
		return new()
		{
			Coin = account.Currency?.ToUpperInvariant(),
			Free = (total - used).Max(0m).ToWire(),
			Frozen = used.ToWire(),
		};
	}

	public static PhemexPosition NormalizePosition(PhemexWirePosition position)
	{
		var size = position.SizeRq.ToDecimal() ?? position.Size.ToDecimal() ?? 0m;
		var positionSide = position.PositionSide;
		var direction = positionSide.IsEmpty() || positionSide.EqualsIgnoreCase("Merged")
			? position.Side
			: positionSide;
		var isShort = direction.EqualsIgnoreCase("Short") || direction.EqualsIgnoreCase("Sell");
		var normalizedSide = isShort ? "SHORT" :
			direction.EqualsIgnoreCase("Long") || direction.EqualsIgnoreCase("Buy") ? "LONG" : null;
		var leverage = (position.Leverage.ToDecimal() ?? 0m).Abs();
		return new()
		{
			PositionId = $"{position.Symbol}:{positionSide.IsEmpty(direction)}",
			Symbol = position.Symbol?.ToUpperInvariant(),
			IsolatedMode = position.PositionMode,
			PositionSide = normalizedSide,
			NetSize = (isShort ? -size : size).ToWire(),
			AveragePrice = position.AveragePrice,
			UnrealizedPnl = position.UnrealizedPnl,
			LongSize = isShort ? "0" : size.ToWire(),
			ShortSize = isShort ? size.ToWire() : "0",
			MarkPrice = position.MarkPrice,
			LiquidationPrice = position.LiquidationPrice,
			Leverage = leverage.ToWire(),
			UpdateTime = ToMilliseconds(position.UpdateTime),
		};
	}

	public PhemexOrder NormalizeOrder(PhemexSections section, PhemexWireOrder order)
	{
		var context = GetProduct(order.Symbol);
		var isSpot = section == PhemexSections.Spot;
		var price = isSpot
			? Unscale(order.SpotPrice, context.PriceScale)
			: order.FuturesPrice.IsEmpty(order.ExecutionPrice);
		var quantity = isSpot
			? Unscale(order.SpotQuantity, context.BaseScale)
			: order.FuturesQuantity;
		var filled = isSpot
			? Unscale(order.SpotFilledQuantity, context.BaseScale)
			: order.FuturesFilledQuantity;
		var amount = isSpot
			? Unscale(order.SpotFilledAmount, context.QuoteScale)
			: order.FuturesFilledAmount;
		var fee = isSpot
			? Unscale(order.SpotFee, GetCurrencyScale(order.FeeCurrency.IsEmpty(context.QuoteCurrency)))
			: order.FuturesFee;
		var updateTime = order.UpdateTime > 0 ? order.UpdateTime : order.ActionTime;
		if (updateTime <= 0)
			updateTime = order.CreatedAtMilliseconds;
		var createTime = order.CreateTime > 0 ? order.CreateTime : order.ActionTime;
		if (createTime <= 0)
			createTime = order.CreatedAtMilliseconds;
		var timeInForce = order.TimeInForce;
		return new()
		{
			OrderId = order.OrderId,
			Symbol = order.Symbol?.ToUpperInvariant(),
			Type = NormalizeOrderType(order.OrderType),
			Side = NormalizeResponseSide(order.Side),
			PositionSide = order.PositionSide,
			Price = price,
			OriginalSize = quantity,
			Size = quantity,
			FilledSize = filled,
			FilledAmount = amount,
			Fee = fee,
			FeeCoin = order.FeeCurrency,
			Status = NormalizeOrderStatus(order.Status),
			IsImmediateOrCancel = timeInForce.EqualsIgnoreCase("ImmediateOrCancel"),
			IsReduceOnly = order.IsReduceOnly || order.ExecutionInstruction.EqualsIgnoreCase("ReduceOnly"),
			ClientOrderId = order.ClientOrderId,
			CreateTime = ToMilliseconds(createTime),
			UpdateTime = ToMilliseconds(updateTime),
			TimeInForce = timeInForce,
		};
	}

	public PhemexFill NormalizeFill(PhemexSections section, string symbol, PhemexWireFill fill)
	{
		var context = GetProduct(fill.Symbol.IsEmpty(symbol));
		var isSpot = section == PhemexSections.Spot;
		return new()
		{
			Id = fill.ExecutionId,
			OrderId = fill.OrderId,
			Symbol = fill.Symbol.IsEmpty(symbol)?.ToUpperInvariant(),
			Side = NormalizeResponseSide(fill.Side),
			Price = isSpot ? Unscale(fill.SpotPrice, context.PriceScale) : fill.FuturesPrice,
			Size = isSpot ? Unscale(fill.SpotQuantity, context.BaseScale) : fill.FuturesQuantity,
			Fee = isSpot
				? Unscale(fill.SpotFee, GetCurrencyScale(fill.FeeCurrency.IsEmpty(context.QuoteCurrency)))
				: fill.FuturesFee,
			FeeCoin = fill.FeeCurrency.IsEmpty(context.SettleCurrency),
			Timestamp = ToMilliseconds(fill.Timestamp > 0 ? fill.Timestamp : fill.CreatedAtMilliseconds),
		};
	}

	private int GetCurrencyScale(string currency)
	{
		if (currency.IsEmpty())
			return 0;
		using (_productSync.EnterScope())
			return _currencyScales.TryGetValue(new(currency.ToUpperInvariant()), out var scale)
				? scale
				: 0;
	}

	private static int GetScale(IReadOnlyDictionary<PhemexCurrencyKey, int> scales,
		string currency)
		=> !currency.IsEmpty() && scales.TryGetValue(new(currency.ToUpperInvariant()), out var scale)
			? scale
			: 0;

	private static string NormalizeRequestSide(string side)
		=> side.EqualsIgnoreCase("BUY") ? "Buy" : "Sell";

	private static string NormalizeResponseSide(string side)
		=> side?.ToUpperInvariant() switch
		{
			"1" or "BUY" or "LONG" => "BUY",
			_ => "SELL",
		};

	private static string NormalizeOrderType(string type)
		=> type?.ToUpperInvariant() switch
		{
			"1" or "MARKET" or "MARKETASLIMIT" => "MARKET",
			_ => "LIMIT",
		};

	private static string NormalizeOrderStatus(string status)
		=> status?.ToUpperInvariant() switch
		{
			"1" => "UNTRIGGERED",
			"5" => "NEW",
			"6" => "PARTIALLY_FILLED",
			"7" => "FILLED",
			"8" => "CANCELED",
			_ => status?.ToUpperInvariant(),
		};

	private async ValueTask<TData> SendApiPublicAsync<TData>(HttpMethod method, string path,
		PhemexParameter[] parameters, CancellationToken cancellationToken)
	{
		var query = BuildQuery(parameters);
		var body = await SendRawAsync(method, path, true, true,
			() => new(query.Encoded, null, null, null), cancellationToken);
		var response = Deserialize<PhemexApiResponse<TData>>(body, path);
		if (response.Code != 0)
			throw CreateApiError(path, response.Code, response.Message, true);
		return response.Data;
	}

	private async ValueTask<TData> SendMarketPublicAsync<TData>(HttpMethod method, string path,
		PhemexParameter[] parameters, CancellationToken cancellationToken)
	{
		var query = BuildQuery(parameters);
		var body = await SendRawAsync(method, path, true, true,
			() => new(query.Encoded, null, null, null), cancellationToken);
		var response = Deserialize<PhemexMarketResponse<TData>>(body, path);
		if (response.Error is not null)
			throw CreateApiError(path, response.Error.Code, response.Error.Message, true);
		return response.Result;
	}

	private async ValueTask<TData> SendApiPrivateAsync<TData>(HttpMethod method, string path,
		PhemexParameter[] parameters, bool isSafe,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var responseBody = await SendRawAsync(method, path, false, isSafe,
			() => PreparePrivate(path, parameters, null), cancellationToken);
		var response = Deserialize<PhemexApiResponse<TData>>(responseBody, path);
		if (response.Code != 0)
			throw CreateApiError(path, response.Code, response.Message, isSafe);
		return response.Data;
	}

	private async ValueTask<TData> SendApiPrivateBodyAsync<TData, TRequest>(HttpMethod method,
		string path, PhemexParameter[] parameters, TRequest requestBody, bool isSafe,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		EnsureCredentials();
		ArgumentNullException.ThrowIfNull(requestBody);
		var body = JsonConvert.SerializeObject(requestBody, _jsonSettings);
		var responseBody = await SendRawAsync(method, path, false, isSafe,
			() => PreparePrivate(path, parameters, body), cancellationToken);
		var response = Deserialize<PhemexApiResponse<TData>>(responseBody, path);
		if (response.Code != 0)
			throw CreateApiError(path, response.Code, response.Message, isSafe);
		return response.Data;
	}

	private async ValueTask<TData> SendRawPrivateAsync<TData>(HttpMethod method, string path,
		PhemexParameter[] parameters, bool isSafe, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var responseBody = await SendRawAsync(method, path, false, isSafe,
			() => PreparePrivate(path, parameters, null), cancellationToken);
		return Deserialize<TData>(responseBody, path);
	}

	private PhemexPreparedRequest PreparePrivate(string path, PhemexParameter[] parameters,
		string body)
	{
		var query = BuildQuery(parameters);
		var expiry = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60)
			.ToString(CultureInfo.InvariantCulture);
		var signature = Sign(path + query.Encoded + expiry + body);
		return new(query.Encoded, body, expiry, signature);
	}

	private async ValueTask<string> SendRawAsync(HttpMethod method, string path, bool isPublic,
		bool isSafe, Func<PhemexPreparedRequest> prepare, CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			var prepared = prepare();
			var relative = path + (prepared.Query.IsEmpty() ? string.Empty : "?" + prepared.Query);
			using var request = new HttpRequestMessage(method, new Uri(_endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (!isPublic)
			{
				request.Headers.TryAddWithoutValidation("x-phemex-access-token", _apiKey);
				request.Headers.TryAddWithoutValidation("x-phemex-request-expiry", prepared.Expiry);
				request.Headers.TryAddWithoutValidation("x-phemex-request-signature", prepared.Signature);
			}
			if (prepared.Body is not null)
				request.Content = new StringContent(prepared.Body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException error) when (isSafe && attempt < _maxAttempts)
			{
				this.AddWarningLog("Phemex {0} transport error. Retrying read request: {1}",
					relative, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(CreateTransportError(relative, isSafe,
					error.Message), error);
			}

			using (response)
			{
				var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
				if (isSafe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateHttpError(response.StatusCode, relative, responseBody, isSafe);
				return responseBody;
			}
		}
	}

	private async ValueTask WaitForRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(210);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static PhemexQuery BuildQuery(IEnumerable<PhemexParameter> parameters)
	{
		var values = parameters.Where(static parameter => !parameter.Value.IsEmpty()).ToArray();
		return new(
			values.Select(static parameter => parameter.Name + "=" + parameter.Value).Join("&"),
			values.Select(static parameter => Escape(parameter.Name) + "=" +
				Escape(parameter.Value)).Join("&"));
	}

	private string Sign(string payload)
	{
		byte[] hash;
		using (_signSync.EnterScope())
			hash = _hasher.ComputeHash(payload.UTF8());
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Phemex API key and secret are required for private requests.");
	}

	private TData Deserialize<TData>(string payload, string path)
	{
		try
		{
			return JsonConvert.DeserializeObject<TData>(payload, _jsonSettings)
				?? throw new InvalidDataException($"Phemex {path} returned no JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException($"Phemex {path} returned invalid JSON.", error);
		}
	}

	private static Exception CreateApiError(string path, int code, string message, bool isSafe)
		=> new InvalidOperationException($"Phemex {path} failed (code {code}): {message}. " +
			(isSafe ? "The request was read-only." :
				"The write was not retried; inspect exchange state before retrying."));

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan delay && delay > TimeSpan.Zero)
			return delay.Min(TimeSpan.FromSeconds(30));
		return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
	}

	private static Exception CreateHttpError(HttpStatusCode status, string path, string body,
		bool isSafe)
		=> new InvalidOperationException($"Phemex {path} returned HTTP {(int)status}: {body}. " +
			(isSafe ? "The read request failed." :
				"The write was not retried; inspect exchange state before retrying."));

	private static string CreateTransportError(string path, bool isSafe, string message)
		=> $"Phemex {path} transport error: {message}. " +
			(isSafe ? "The read request failed." :
				"The write may have reached Phemex; inspect exchange state before retrying.");

	private static long ScaleToInt64(string value, int scale, string valueName)
	{
		if (value.ToDecimal() is not decimal number || number < 0m)
			throw new InvalidOperationException($"Phemex {valueName} is invalid.");
		var scaled = number * Pow10(scale);
		if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue)
			throw new InvalidOperationException($"Phemex {valueName} exceeds exchange precision.");
		return checked((long)scaled);
	}

	private static decimal ParseScaled(string value, int scale)
		=> (value.ToDecimal() ?? 0m) / Pow10(scale);

	private static string Unscale(string value, int scale)
		=> value.IsEmpty() ? null : ParseScaled(value, scale).ToWire();

	private static decimal Pow10(int scale)
	{
		var value = 1m;
		for (var i = 0; i < scale; i++)
			value *= 10m;
		return value;
	}

	private static long NanosecondsToMilliseconds(long value)
		=> value <= 0 ? 0 : value / 1_000_000;

	private static long ToMilliseconds(long value)
	{
		if (value <= 0)
			return 0;
		if (value >= 10_000_000_000_000)
			return value / 1_000_000;
		if (value >= 10_000_000_000)
			return value;
		return value * 1000;
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
