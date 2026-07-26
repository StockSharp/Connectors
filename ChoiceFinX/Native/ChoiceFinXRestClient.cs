namespace StockSharp.ChoiceFinX.Native;

sealed class ChoiceFinXRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings =
        new()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

    private readonly HttpClient _client;
    private readonly string _token;
    private readonly string _authorizationHeader;
    private readonly string _authorizationScheme;
    private readonly string _vendorId;
    private readonly string _vendorKey;
    private readonly int _attempts;
    private readonly decimal _defaultPriceDivisor;

    public ChoiceFinXRestClient(
        Uri address,
        string token,
        string authorizationHeader,
        string authorizationScheme,
        string vendorId,
        string vendorKey,
        int attempts,
        decimal defaultPriceDivisor)
    {
        _token = token.ThrowIfEmpty(nameof(token));
        _authorizationHeader =
            authorizationHeader.ThrowIfEmpty(
                nameof(authorizationHeader));
        _authorizationScheme = authorizationScheme;
        _vendorId = vendorId;
        _vendorKey = vendorKey;
        _attempts = Math.Max(1, attempts);
        _defaultPriceDivisor =
            defaultPriceDivisor > 0
                ? defaultPriceDivisor
                : throw new ArgumentOutOfRangeException(
                    nameof(defaultPriceDivisor));

        _client = new()
        {
            BaseAddress = EnsureTrailingSlash(
                address ??
                    throw new ArgumentNullException(
                        nameof(address))),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "application/json");
    }

    public override string Name =>
        nameof(ChoiceFinX) + "_" +
        nameof(ChoiceFinXRestClient);

    protected override void DisposeManaged()
    {
        _client.Dispose();
        base.DisposeManaged();
    }

    public async Task<JObject> GetProfile(
        CancellationToken cancellationToken)
        => AsObject(await Send(
            HttpMethod.Get,
            "api/OpenAPI/UserProfileV2",
            null,
            "user profile",
            cancellationToken));

    public async Task<ChoiceFinXInstrument> GetInstrument(
        int segmentId,
        long token,
        CancellationToken cancellationToken)
    {
        var response = await Send(
            HttpMethod.Post,
            "api/OpenAPI/ScripDetails",
            new ChoiceFinXScripRequest
            {
                SegmentId = segmentId,
                Token = token,
            },
            "scrip details",
            cancellationToken);
        return ParseInstrument(
            response, segmentId, token,
            _defaultPriceDivisor);
    }

    public async Task<ChoiceFinXTick[]> GetTouchlines(
        IEnumerable<(int segmentId, long token)> instruments,
        CancellationToken cancellationToken)
    {
        var nativeIds = instruments
            .Distinct()
            .ToArray();
        var keys = nativeIds
            .Select(item => string.Create(
                CultureInfo.InvariantCulture,
                $"{item.segmentId}@{item.token}"))
            .ToArray();
        if (keys.Length == 0)
            return [];

        var response = await Send(
            HttpMethod.Post,
            "api/OpenAPI/MultipleTouchline",
            new ChoiceFinXTouchlineRequest
            {
                Instruments = keys.JoinCommaSpace()
                    .Replace(", ", ","),
            },
            "multiple touchline",
            cancellationToken);
        var ticks = ParseTouchlines(
            response, _defaultPriceDivisor);
        if (ticks.Length == nativeIds.Length)
        {
            for (var index = 0;
                index < ticks.Length;
                index++)
            {
                if (ticks[index].SegmentId <= 0)
                {
                    ticks[index].SegmentId =
                        nativeIds[index].segmentId;
                }
                if (ticks[index].Token <= 0)
                {
                    ticks[index].Token =
                        nativeIds[index].token;
                }
            }
        }
        else if (nativeIds.Length == 1 &&
            ticks.Length == 1)
        {
            if (ticks[0].SegmentId <= 0)
                ticks[0].SegmentId =
                    nativeIds[0].segmentId;
            if (ticks[0].Token <= 0)
                ticks[0].Token = nativeIds[0].token;
        }
        return
        [
            .. ticks.Where(value =>
                value.SegmentId > 0 &&
                value.Token > 0)
        ];
    }

    public async Task<ChoiceFinXCandle[]> GetCandles(
        int segmentId,
        long token,
        TimeSpan timeFrame,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var response = await Send(
            HttpMethod.Post,
            "api/OpenGraph/ChartData",
            new ChoiceFinXChartRequest
            {
                SegmentId = segmentId,
                Token = token,
                FromDate = from.ToChoiceEpoch(),
                ToDate = to.ToChoiceEpoch(),
                Interval = ToInterval(timeFrame),
            },
            "chart history",
            cancellationToken);
        return ParseCandles(
            response, _defaultPriceDivisor);
    }

    public async Task<string> PlaceOrder(
        ChoiceFinXOrderRequest request,
        CancellationToken cancellationToken)
        => ExtractOrderId(
            await Send(
                HttpMethod.Post,
                "api/OpenAPI/V2/NewOrder",
                request,
                "new order",
                cancellationToken),
            "new order");

    public async Task<string> ModifyOrder(
        ChoiceFinXModifyOrderRequest request,
        CancellationToken cancellationToken)
        => ExtractOrderId(
            await Send(
                HttpMethod.Post,
                "api/OpenAPI/V2/ModifyOrder",
                request,
                "modify order",
                cancellationToken),
            "modify order",
            request.ClientOrderNo);

    public async Task CancelOrder(
        ChoiceFinXCancelOrderRequest request,
        CancellationToken cancellationToken)
        => await Send(
            HttpMethod.Post,
            "api/OpenAPI/V2/CancelOrder",
            request,
            "cancel order",
            cancellationToken);

    public async Task<ChoiceFinXOrder[]> GetOrders(
        CancellationToken cancellationToken)
        => ParseOrders(await Send(
            HttpMethod.Get,
            "api/OpenAPI/V2/OrderBook",
            null,
            "order book",
            cancellationToken));

    public async Task<ChoiceFinXTrade[]> GetTrades(
        CancellationToken cancellationToken)
        => ParseTrades(await Send(
            HttpMethod.Get,
            "api/OpenAPI/V2/TradeBook",
            null,
            "trade book",
            cancellationToken));

    public async Task<ChoiceFinXPosition[]> GetPositions(
        CancellationToken cancellationToken)
        => ParsePositions(await Send(
            HttpMethod.Get,
            "api/OpenAPI/V2/NetPosition",
            null,
            "net positions",
            cancellationToken));

    public async Task<ChoiceFinXHolding[]> GetHoldings(
        CancellationToken cancellationToken)
        => ParseHoldings(await Send(
            HttpMethod.Get,
            "api/OpenAPI/Holdings",
            null,
            "holdings",
            cancellationToken));

    public async Task<ChoiceFinXFunds> GetFunds(
        CancellationToken cancellationToken)
        => ParseFunds(await Send(
            HttpMethod.Get,
            "api/OpenAPI/FundsViewNew",
            null,
            "funds view",
            cancellationToken));

    private async Task<JToken> Send(
        HttpMethod method,
        string path,
        object body,
        string operation,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1;
            attempt <= _attempts;
            attempt++)
        {
            try
            {
                using var request =
                    new HttpRequestMessage(method, path);
                var authorization = _authorizationScheme.IsEmpty()
                    ? _token
                    : $"{_authorizationScheme} {_token}";
                request.Headers.TryAddWithoutValidation(
                    _authorizationHeader, authorization);
                if (!_vendorId.IsEmpty())
                {
                    request.Headers.TryAddWithoutValidation(
                        "VendorId", _vendorId);
                }
                if (!_vendorKey.IsEmpty())
                {
                    request.Headers.TryAddWithoutValidation(
                        "VendorKey", _vendorKey);
                }
                if (body != null)
                {
                    request.Content = new StringContent(
                        JsonConvert.SerializeObject(
                            body,
                            Formatting.None,
                            _jsonSettings),
                        Encoding.UTF8,
                        "application/json");
                }

                this.AddVerboseLog(
                    "{0} {1}.", method, path);
                using var response = await _client.SendAsync(
                    request, cancellationToken);
                var content = await response.Content
                    .ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = new HttpRequestException(
                        $"Choice FinX {operation} returned HTTP {(int)response.StatusCode}: {Truncate(content, 400).IsEmpty(response.ReasonPhrase)}");
                    if (attempt < _attempts &&
                        (response.StatusCode ==
                            HttpStatusCode.TooManyRequests ||
                            (int)response.StatusCode >= 500))
                    {
                        lastError = error;
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(
                                200 * attempt),
                            cancellationToken);
                        continue;
                    }
                    throw error;
                }
                return ParseResponse(content, operation);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or
                    TaskCanceledException &&
                attempt < _attempts &&
                !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                await Task.Delay(
                    TimeSpan.FromMilliseconds(200 * attempt),
                    cancellationToken);
            }
        }

        throw lastError ??
            new InvalidOperationException(
                $"Choice FinX {operation} failed.");
    }

    internal static JToken ParseResponse(
        string content, string operation)
    {
        if (content.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Choice FinX returned an empty response for {operation}.");
        }

        JToken root;
        try
        {
            root = JToken.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Choice FinX returned invalid JSON for {operation}.",
                ex);
        }

        if (root is JObject obj)
        {
            var status = obj.GetText(
                "Status", "status", "Success",
                "success", "Stat", "stat");
            if (!status.IsEmpty() &&
                !IsSuccess(status))
            {
                throw new InvalidOperationException(
                    $"Choice FinX {operation} error: {obj.GetText("Reason", "reason", "Message", "message", "Error", "error").IsEmpty(status)}");
            }

            var response = obj.GetToken(
                "Response", "response",
                "Data", "data", "Result", "result");
            if (response != null &&
                response.Type is not JTokenType.Null and
                    not JTokenType.Undefined)
            {
                return ParseNested(response);
            }
        }
        return ParseNested(root);
    }

    internal static ChoiceFinXInstrument ParseInstrument(
        JToken token,
        int fallbackSegmentId,
        long fallbackToken,
        decimal defaultPriceDivisor)
    {
        var value = EnumerateObjects(
            token,
            "ScripDetails",
            "ScripDetail",
            "Instrument",
            "Instruments")
            .FirstOrDefault() ?? AsObject(token);
        if (value == null)
            return null;

        var divisor = Positive(
            value.GetDecimal(
                "PriceDivisor", "PriceDivider",
                "Divider", "Divisor"),
            FindDecimal(token, "PriceDivisor"),
            defaultPriceDivisor);
        var segmentId = value.GetInt(
            "SegmentId", "SegmentID",
            "Segment", "ExchangeSegment");
        var nativeToken = value.GetLong(
            "Token", "ScripToken",
            "ScripCode", "SecurityId");
        var tickSize = value.GetDecimal(
            "TickSize", "Tick", "MinimumTick");
        if (tickSize >= 1 && divisor > 1)
            tickSize /= divisor;

        return new()
        {
            SegmentId = segmentId > 0
                ? segmentId
                : fallbackSegmentId,
            Token = nativeToken > 0
                ? nativeToken
                : fallbackToken,
            Symbol = value.GetText(
                "TradingSymbol", "Symbol",
                "ScripName", "DisplayName",
                "InstrumentName"),
            Name = value.GetText(
                "FullName", "Description",
                "CompanyName", "ScripName",
                "InstrumentName"),
            Series = value.GetText(
                "Series", "GroupName", "Group"),
            Instrument = value.GetText(
                "Instrument", "InstrumentType",
                "ScripType", "AssetType"),
            Isin = value.GetText("ISIN", "Isin"),
            Underlying = value.GetText(
                "Underlying", "UnderlyingSymbol",
                "UnderlyingName"),
            TickSize = tickSize,
            LotSize = Positive(
                value.GetDecimal(
                    "LotSize", "MarketLot",
                    "QtyMultiplier", "Multiplier"),
                1),
            StrikePrice = ScalePrice(
                value.GetNullableDecimal(
                    "StrikePrice", "Strike"),
                divisor) ?? 0,
            OptionType = value.GetText(
                "OptionType", "Option_Type",
                "PutCall", "CPType"),
            ExpiryDate = value.GetChoiceTime(
                "ExpiryDate", "Expiry",
                "ExpirationDate"),
            PriceDivisor = divisor,
        };
    }

    internal static ChoiceFinXTick[] ParseTouchlines(
        JToken token, decimal defaultPriceDivisor)
    {
        var commonDivisor = Positive(
            FindDecimal(token, "PriceDivisor"),
            defaultPriceDivisor);
        return
        [
            .. EnumerateObjects(
                token,
                "MultipleTouchline",
                "Touchline",
                "TouchlineData",
                "BroadcastData",
                "lstTouchline")
            .Select(value =>
            {
                var divisor = Positive(
                    value.GetDecimal(
                        "PriceDivisor",
                        "PriceDivider"),
                    commonDivisor);
                var segmentId = value.GetInt(
                    "SegmentId", "SegmentID",
                    "Segment");
                var nativeToken = value.GetLong(
                    "Token", "ScripToken",
                    "ScripCode", "SecurityId");
                var composite = value.GetText(
                    "SegmentToken",
                    "SegmentAndToken",
                    "SegToken");
                if ((segmentId <= 0 ||
                        nativeToken <= 0) &&
                    !composite.IsEmpty())
                {
                    try
                    {
                        var parsed =
                            composite.ParseInstrumentKey();
                        segmentId = parsed.segmentId;
                        nativeToken = parsed.token;
                    }
                    catch (FormatException)
                    {
                    }
                }
                return new ChoiceFinXTick
                {
                    SegmentId = segmentId,
                    Token = nativeToken,
                    ServerTime =
                        value.GetChoiceTime(
                            "LastUpdateTime",
                            "UpdateTime",
                            "ServerTime",
                            "TimeStamp") ??
                        DateTime.UtcNow,
                    LastTradeTime = value.GetChoiceTime(
                        "LastTradeTime", "LTT",
                        "TradeTime"),
                    LastPrice = ScalePrice(
                        value.GetNullableDecimal(
                            "LastPrice", "LTP",
                            "LastTradedPrice",
                            "CloseRate"),
                        divisor),
                    LastQuantity = value.GetNullableDecimal(
                        "LastQuantity", "LTQ",
                        "LastTradedQty"),
                    AveragePrice = ScalePrice(
                        value.GetNullableDecimal(
                            "AveragePrice", "ATP",
                            "AvgTradePrice"),
                        divisor),
                    Volume = value.GetNullableDecimal(
                        "Volume", "TotalTradedQty",
                        "TotalVolume"),
                    TotalBuyQuantity =
                        value.GetNullableDecimal(
                            "TotalBuyQuantity",
                            "TotalBuyQty", "TBQ"),
                    TotalSellQuantity =
                        value.GetNullableDecimal(
                            "TotalSellQuantity",
                            "TotalSellQty", "TSQ"),
                    Open = ScalePrice(
                        value.GetNullableDecimal(
                            "Open", "OpenPrice"),
                        divisor),
                    High = ScalePrice(
                        value.GetNullableDecimal(
                            "High", "HighPrice"),
                        divisor),
                    Low = ScalePrice(
                        value.GetNullableDecimal(
                            "Low", "LowPrice"),
                        divisor),
                    Close = ScalePrice(
                        value.GetNullableDecimal(
                            "Close", "ClosePrice",
                            "PreviousClose"),
                        divisor),
                    OpenInterest =
                        value.GetNullableDecimal(
                            "OpenInterest", "OI"),
                    OpenInterestChange =
                        value.GetNullableDecimal(
                            "OpenInterestChange",
                            "OIChange"),
                    Bids = ParseDepth(
                        value, true, divisor),
                    Asks = ParseDepth(
                        value, false, divisor),
                };
            })
        ];
    }

    internal static ChoiceFinXCandle[] ParseCandles(
        JToken token, decimal defaultPriceDivisor)
    {
        var divisor = Positive(
            FindDecimal(token, "PriceDivisor"),
            defaultPriceDivisor);
        var history = FindToken(
            token,
            "lstChartHistory",
            "ChartHistory",
            "History",
            "Candles");
        history ??= token;
        var result = new List<ChoiceFinXCandle>();

        if (history is JArray rows)
        {
            foreach (var row in rows)
            {
                if (row is JArray values &&
                    values.Count >= 6)
                {
                    var time = values[0].Value<decimal>();
                    result.Add(new()
                    {
                        Time =
                            ChoiceFinXExtensions
                                .FromChoiceEpoch(time),
                        Open = ScalePrice(
                            values[1].Value<decimal>(),
                            divisor),
                        High = ScalePrice(
                            values[2].Value<decimal>(),
                            divisor),
                        Low = ScalePrice(
                            values[3].Value<decimal>(),
                            divisor),
                        Close = ScalePrice(
                            values[4].Value<decimal>(),
                            divisor),
                        Volume = values[5]
                            .Value<decimal>(),
                        OpenInterest =
                            values.Count > 6 &&
                            values[6].Type !=
                                JTokenType.Null
                                ? values[6]
                                    .Value<decimal>()
                                : null,
                    });
                }
                else if (row is JObject obj)
                {
                    var time = obj.GetDecimal(
                        "PriceDate", "Date",
                        "Time", "Timestamp");
                    if (time <= 0)
                        continue;
                    result.Add(new()
                    {
                        Time =
                            ChoiceFinXExtensions
                                .FromChoiceEpoch(time),
                        Open = ScalePrice(
                            obj.GetDecimal(
                                "OpenPrice", "Open"),
                            divisor),
                        High = ScalePrice(
                            obj.GetDecimal(
                                "HighPrice", "High"),
                            divisor),
                        Low = ScalePrice(
                            obj.GetDecimal(
                                "LowPrice", "Low"),
                            divisor),
                        Close = ScalePrice(
                            obj.GetDecimal(
                                "ClosePrice", "Close"),
                            divisor),
                        Volume = obj.GetDecimal(
                            "VolumeTraded", "Volume"),
                        OpenInterest =
                            obj.GetNullableDecimal(
                                "OpenInterest", "OI"),
                    });
                }
            }
        }
        return [.. result];
    }

    internal static ChoiceFinXOrder[] ParseOrders(
        JToken token, bool socketPricesArePaise = false)
        =>
        [
            .. EnumerateObjects(
                token,
                "OrderBook", "Orders",
                "lstOrderBook", "OrderDetails")
            .Select(value => ParseOrder(
                value, socketPricesArePaise))
            .Where(value => !value.OrderId.IsEmpty())
        ];

    internal static ChoiceFinXOrder ParseOrder(
        JObject value, bool socketPricesArePaise)
    {
        var divisor = socketPricesArePaise ? 100m : 1m;
        return new()
        {
            OrderId = value.GetText(
                "ClientOrderNo", "ClientOrderNumber",
                "GatewayOrderNo", "UniqueCode",
                "OrderId", "OrderNumber"),
            ExchangeOrderId = value.GetText(
                "ExchangeOrderNo",
                "ExchangeOrderNumber"),
            Remarks = value.GetText(
                "Remarks", "UserRemarks"),
            SegmentId = value.GetInt(
                "SegmentId", "Segment",
                "Exchange"),
            Token = value.GetLong(
                "Token", "ScripCode",
                "SecurityId"),
            Symbol = value.GetText(
                "TradingSymbol", "Symbol",
                "InstrumentName", "ScripName"),
            Series = value.GetText("Series"),
            Side = value.GetInt(
                "BS", "BuySell", "Buy_Sell",
                "TransactionType"),
            OrderType = value.GetText(
                "OrderType", "PriceType"),
            ProductType = value.GetText(
                "ProductType", "Product"),
            Validity = value.GetInt(
                "Validity", "OrderValidity"),
            Quantity = value.GetDecimal(
                "Qty", "Quantity",
                "OrderOriginalQty",
                "OriginalQty"),
            PendingQuantity = value.GetDecimal(
                "PendingQty", "PendingQuantity",
                "RemainingQty", "Balance"),
            TradedQuantity = value.GetDecimal(
                "TradedQty", "TradedQTY",
                "FilledQty", "ExecutedQty"),
            DisclosedQuantity = value.GetDecimal(
                "DisclosedQty", "DQ"),
            Price = value.GetDecimal(
                "Price", "OrderPrice") / divisor,
            TriggerPrice = value.GetDecimal(
                "TriggerPrice") / divisor,
            AveragePrice = value.GetDecimal(
                "AveragePrice", "AvgPrice",
                "TradedPrice") / divisor,
            Status = value.GetText(
                "OrderStatus", "Status",
                "DisplayStatus", "Misc"),
            RejectReason = value.GetText(
                "RejectReason", "RejectionReason",
                "Reason", "ErrorMessage"),
            OrderTime = value.GetChoiceTime(
                "OrderEntryTime", "OrderTime",
                "OrderDateTime"),
            ModifiedTime = value.GetChoiceTime(
                "LastModifiedTime",
                "ModifiedTime",
                "ExchangeOrderTime"),
        };
    }

    internal static ChoiceFinXTrade[] ParseTrades(
        JToken token, bool socketPricesArePaise = false)
        =>
        [
            .. EnumerateObjects(
                token,
                "TradeBook", "Trades",
                "lstTradeBook", "TradeDetails")
            .Select(value => ParseTrade(
                value, socketPricesArePaise))
            .Where(value =>
                !value.TradeId.IsEmpty() ||
                !value.OrderId.IsEmpty())
        ];

    internal static ChoiceFinXTrade ParseTrade(
        JObject value, bool socketPricesArePaise)
    {
        var divisor = socketPricesArePaise ? 100m : 1m;
        return new()
        {
            TradeId = value.GetText(
                "TradeNumber", "TradeNo",
                "TradeId", "FillId"),
            OrderId = value.GetText(
                "ClientOrderNo", "OrderId",
                "UniqueCode", "OrderNumber"),
            SegmentId = value.GetInt(
                "SegmentId", "Segment",
                "Exchange"),
            Token = value.GetLong(
                "Token", "ScripCode",
                "SecurityId"),
            Symbol = value.GetText(
                "TradingSymbol", "Symbol",
                "InstrumentName", "ScripName"),
            Side = value.GetInt(
                "BS", "BuySell", "Buy_Sell"),
            Price = value.GetDecimal(
                "TradePrice", "TradedPrice",
                "Price", "FillPrice") / divisor,
            Quantity = value.GetDecimal(
                "TradeQty", "TradedQty",
                "Quantity", "FillQty"),
            TradeTime = value.GetChoiceTime(
                "TradeTime", "TradeTimeStamp",
                "ExchangeTradeTime",
                "FillTime"),
        };
    }

    internal static ChoiceFinXPosition[] ParsePositions(
        JToken token)
        =>
        [
            .. EnumerateObjects(
                token,
                "NetPosition", "NetPositions",
                "Positions", "lstPosition")
            .Select(value => new ChoiceFinXPosition
            {
                SegmentId = value.GetInt(
                    "SegmentId", "Segment",
                    "Exchange"),
                Token = value.GetLong(
                    "Token", "ScripCode",
                    "SecurityId"),
                Symbol = value.GetText(
                    "TradingSymbol", "Symbol",
                    "InstrumentName", "ScripName"),
                NetQuantity = value.GetDecimal(
                    "NetQty", "NetQuantity",
                    "Quantity"),
                AveragePrice = value.GetDecimal(
                    "NetAveragePrice",
                    "AveragePrice", "AvgPrice"),
                LastPrice = value.GetDecimal(
                    "LastPrice", "LTP"),
                RealizedPnL = value.GetDecimal(
                    "RealizedPnL", "RealizedPL",
                    "BookedProfit"),
                UnrealizedPnL = value.GetDecimal(
                    "UnrealizedPnL",
                    "UnrealizedPL",
                    "MTM", "MarkToMarket"),
            })
            .Where(value =>
                value.SegmentId > 0 &&
                value.Token > 0)
        ];

    internal static ChoiceFinXHolding[] ParseHoldings(
        JToken token)
        =>
        [
            .. EnumerateObjects(
                token,
                "Holdings", "Holding",
                "lstHolding", "HoldingDetails")
            .Select(value => new ChoiceFinXHolding
            {
                SegmentId = PositiveInt(
                    value.GetInt(
                        "SegmentId", "Segment",
                        "Exchange"), 1),
                Token = value.GetLong(
                    "Token", "ScripCode",
                    "SecurityId", "NSEToken",
                    "NseSecurityId"),
                Symbol = value.GetText(
                    "TradingSymbol", "Symbol",
                    "ScripName", "NSESymbol"),
                Quantity = value.GetDecimal(
                    "Quantity", "HoldingQty",
                    "TotalQty", "NetQty"),
                BlockedQuantity = value.GetDecimal(
                    "BlockedQty", "UsedQty",
                    "PledgedQty"),
                AveragePrice = value.GetDecimal(
                    "AveragePrice", "AvgPrice",
                    "CostPrice"),
                LastPrice = value.GetDecimal(
                    "LastPrice", "LTP"),
            })
            .Where(value => value.Token > 0)
        ];

    internal static ChoiceFinXFunds ParseFunds(JToken token)
    {
        var value = EnumerateObjects(
            token,
            "FundsView", "Funds",
            "FundDetails", "MarginDetails")
            .FirstOrDefault() ?? AsObject(token);
        if (value == null)
            return new();
        return new()
        {
            OpeningBalance = value.GetDecimal(
                "OpeningBalance", "OpeningBal",
                "LedgerBalance"),
            CurrentBalance = value.GetDecimal(
                "CurrentBalance", "NetBalance",
                "TotalBalance"),
            AvailableBalance = value.GetDecimal(
                "AvailableBalance",
                "AvailableMargin",
                "CashAvailable"),
            UtilizedAmount = value.GetDecimal(
                "UtilizedAmount", "UsedMargin",
                "MarginUsed", "BlockedAmount"),
        };
    }

    private static ChoiceFinXDepthLevel[] ParseDepth(
        JObject value, bool bids, decimal divisor)
    {
        var aliases = bids
            ? new[]
            {
                "Bids", "BuyDepth", "BidDepth",
                "Buy", "BestBuy",
            }
            : new[]
            {
                "Asks", "SellDepth", "AskDepth",
                "Sell", "BestSell",
            };
        var token = value.GetToken(aliases);
        var result = new List<ChoiceFinXDepthLevel>();
        if (token is JArray rows)
        {
            foreach (var row in rows)
            {
                if (row is JObject obj)
                {
                    var price = ScalePrice(
                        obj.GetDecimal(
                            "Price", "Rate",
                            bids ? "BidPrice" : "AskPrice"),
                        divisor);
                    if (price <= 0)
                        continue;
                    result.Add(new()
                    {
                        Price = price,
                        Quantity = obj.GetDecimal(
                            "Quantity", "Qty",
                            "Volume"),
                        Orders = NullablePositiveInt(
                            obj.GetInt(
                                "Orders", "OrderCount",
                                "NoOfOrders")),
                    });
                }
                else if (row is JArray values &&
                    values.Count >= 2)
                {
                    result.Add(new()
                    {
                        Price = ScalePrice(
                            values[0].Value<decimal>(),
                            divisor),
                        Quantity =
                            values[1].Value<decimal>(),
                        Orders =
                            values.Count > 2
                                ? NullablePositiveInt(
                                    values[2]
                                        .Value<int>())
                                : null,
                    });
                }
            }
        }

        for (var level = 1;
            level <= 5 && result.Count < 5;
            level++)
        {
            var price = value.GetDecimal(
                bids
                    ? $"BidPrice{level}"
                    : $"AskPrice{level}",
                bids
                    ? $"BuyPrice{level}"
                    : $"SellPrice{level}");
            if (price <= 0)
                continue;
            result.Add(new()
            {
                Price = ScalePrice(price, divisor),
                Quantity = value.GetDecimal(
                    bids
                        ? $"BidQty{level}"
                        : $"AskQty{level}",
                    bids
                        ? $"BuyQty{level}"
                        : $"SellQty{level}"),
                Orders = NullablePositiveInt(
                    value.GetInt(
                        bids
                            ? $"BidOrders{level}"
                            : $"AskOrders{level}",
                        bids
                            ? $"BuyOrders{level}"
                            : $"SellOrders{level}")),
            });
        }
        return [.. result];
    }

    private static IEnumerable<JObject> EnumerateObjects(
        JToken token, params string[] collectionNames)
    {
        var collection = FindToken(
            token, collectionNames);
        collection ??= token;
        if (collection is JArray array)
        {
            return array
                .Select(ParseNested)
                .OfType<JObject>();
        }
        if (collection is JObject obj)
        {
            var arrays = obj.Properties()
                .Select(property => property.Value)
                .OfType<JArray>()
                .FirstOrDefault();
            if (arrays != null)
            {
                return arrays
                    .Select(ParseNested)
                    .OfType<JObject>();
            }
            return [obj];
        }
        return [];
    }

    private static JToken FindToken(
        JToken token, params string[] names)
    {
        if (token is JObject obj)
        {
            var direct = obj.GetToken(names);
            if (direct != null)
                return ParseNested(direct);
            foreach (var property in obj.Properties())
            {
                var nested = FindToken(
                    property.Value, names);
                if (nested != null)
                    return nested;
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
            {
                var nested = FindToken(child, names);
                if (nested != null)
                    return nested;
            }
        }
        return null;
    }

    private static decimal FindDecimal(
        JToken token, string name)
    {
        var found = FindToken(token, name);
        var text = found?.Type == JTokenType.String
            ? found.Value<string>()
            : found?.ToString(Formatting.None);
        return decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var value)
                ? value
                : 0;
    }

    private static JToken ParseNested(JToken token)
    {
        while (token?.Type == JTokenType.String)
        {
            var text = token.Value<string>()?.Trim();
            if (text.IsEmpty() ||
                !(text.StartsWith(
                    "{", StringComparison.Ordinal) ||
                    text.StartsWith(
                        "[", StringComparison.Ordinal)))
            {
                break;
            }
            try
            {
                token = JToken.Parse(text);
            }
            catch (JsonException)
            {
                break;
            }
        }
        return token;
    }

    private static JObject AsObject(JToken token)
    {
        token = ParseNested(token);
        if (token is JObject obj)
            return obj;
        if (token is JArray array)
            return array.OfType<JObject>().FirstOrDefault();
        return null;
    }

    private static string ExtractOrderId(
        JToken token,
        string operation,
        string fallback = null)
    {
        var value = AsObject(token);
        var id = value?.GetText(
            "ClientOrderNo", "ClientOrderNumber",
            "GatewayOrderNo", "OrderId",
            "OrderNumber", "UniqueCode");
        if (id.IsEmpty() &&
            token?.Type is
                JTokenType.String or
                JTokenType.Integer)
        {
            id = token.ToString().Trim();
        }
        id = id.IsEmpty(fallback);
        return !id.IsEmpty()
            ? id
            : throw new InvalidOperationException(
                $"Choice FinX {operation} response did not contain an order id.");
    }

    private static bool IsSuccess(string status)
        => status.EqualsIgnoreCase("SUCCESS") ||
            status.EqualsIgnoreCase("OK") ||
            status.EqualsIgnoreCase("TRUE") ||
            status is "1" or "200" or "0";

    private static string ToInterval(TimeSpan timeFrame)
        => timeFrame switch
        {
            var value when value ==
                TimeSpan.FromMinutes(1) => "1",
            var value when value ==
                TimeSpan.FromMinutes(5) => "5",
            var value when value ==
                TimeSpan.FromMinutes(10) => "10",
            var value when value ==
                TimeSpan.FromMinutes(15) => "15",
            var value when value ==
                TimeSpan.FromMinutes(30) => "30",
            var value when value ==
                TimeSpan.FromHours(1) => "60",
            var value when value ==
                TimeSpan.FromDays(1) => "D",
            _ => throw new ArgumentOutOfRangeException(
                nameof(timeFrame),
                timeFrame,
                "Choice FinX supports 1, 5, 10, 15, 30, 60 minute and daily candles."),
        };

    private static decimal ScalePrice(
        decimal value, decimal divisor)
        => divisor > 0 ? value / divisor : value;

    private static decimal? ScalePrice(
        decimal? value, decimal divisor)
        => value is decimal price
            ? ScalePrice(price, divisor)
            : null;

    private static decimal Positive(
        params decimal[] values)
        => values.FirstOrDefault(value => value > 0);

    private static int PositiveInt(
        int value, int fallback)
        => value > 0 ? value : fallback;

    private static int? NullablePositiveInt(int value)
        => value > 0 ? value : null;

    private static Uri EnsureTrailingSlash(Uri address)
        => address.AbsoluteUri.EndsWith(
            "/", StringComparison.Ordinal)
                ? address
                : new Uri(address.AbsoluteUri + "/");

    private static string Truncate(
        string value, int length)
        => value.IsEmpty() || value.Length <= length
            ? value
            : value[..length];
}
