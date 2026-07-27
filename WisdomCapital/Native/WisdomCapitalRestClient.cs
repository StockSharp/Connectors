namespace StockSharp.WisdomCapital.Native;

sealed class WisdomCapitalRestClient : BaseLogReceiver
{
    private enum ApiChannel
    {
        Public,
        Interactive,
        MarketData,
    }

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static readonly string[] _segments =
        ["NSECM", "NSEFO", "NSECD", "BSECM", "BSEFO", "MCXFO"];

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private WisdomInstrument[] _instruments;
    private IReadOnlyDictionary<string, WisdomInstrument> _byKey;
    private IReadOnlyDictionary<string, WisdomInstrument> _bySymbol;
    private string _interactiveToken;
    private string _marketDataToken;

    public WisdomCapitalRestClient(
        Uri restAddress,
        HttpMessageHandler handler = null)
    {
        _httpClient = handler == null ? new() : new(handler);
        _httpClient.BaseAddress = restAddress ??
            throw new ArgumentNullException(nameof(restAddress));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-WisdomCapital-XTS/1.0");
    }

    public override string Name =>
        nameof(WisdomCapital) + "_" + nameof(WisdomCapitalRestClient);

    public string InteractiveToken => _interactiveToken;
    public string MarketDataToken => _marketDataToken;

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        base.DisposeManaged();
    }

    public void SetInteractiveToken(SecureString token)
        => _interactiveToken = token.IsEmpty() ? null : token.UnSecure();

    public void SetMarketDataToken(SecureString token)
        => _marketDataToken = token.IsEmpty() ? null : token.UnSecure();

    public async Task<WisdomAuthResult> LoginInteractive(
        SecureString key,
        SecureString secret,
        string source,
        CancellationToken cancellationToken)
    {
        var result = await Login(
            "interactive/user/session",
            key,
            secret,
            source,
            cancellationToken);
        _interactiveToken = result.Token;
        return result;
    }

    public async Task<WisdomAuthResult> LoginMarketData(
        SecureString key,
        SecureString secret,
        string source,
        CancellationToken cancellationToken)
    {
        var result = await Login(
            "apimarketdata/auth/login",
            key,
            secret,
            source,
            cancellationToken);
        _marketDataToken = result.Token;
        return result;
    }

    private async Task<WisdomAuthResult> Login(
        string path,
        SecureString key,
        SecureString secret,
        string source,
        CancellationToken cancellationToken)
    {
        var response = await Send(
            path,
            HttpMethod.Post,
            new
            {
                appKey = key.ThrowIfEmpty(nameof(key)).UnSecure(),
                secretKey = secret.ThrowIfEmpty(nameof(secret)).UnSecure(),
                source = source.ThrowIfEmpty(nameof(source)),
            },
            ApiChannel.Public,
            cancellationToken);
        var result = UnwrapResult(response).ToObject<WisdomAuthResult>();
        if (result == null ||
            result.Token.IsEmpty() ||
            result.UserId.IsEmpty())
        {
            throw new InvalidDataException(
                $"Wisdom Capital XTS {path} response contained no token or user ID.");
        }
        return result;
    }

    public Task<JToken> GetProfile(CancellationToken cancellationToken)
        => Send(
            "interactive/user/profile",
            HttpMethod.Get,
            null,
            ApiChannel.Interactive,
            cancellationToken);

    public async Task<WisdomInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            var response = await Send(
                "apimarketdata/instruments/master",
                HttpMethod.Post,
                new { exchangeSegmentList = _segments },
                ApiChannel.MarketData,
                cancellationToken);
            var raw = UnwrapResult(response).Value<string>();
            if (raw.IsEmpty())
            {
                throw new InvalidDataException(
                    "Wisdom Capital XTS instrument master contained no rows.");
            }

            var instruments = ParseInstrumentMaster(raw).ToList();
            foreach (var segmentId in new[] { 1, 11 })
            {
                try
                {
                    var indices = await GetIndices(
                        segmentId,
                        cancellationToken);
                    foreach (var index in indices)
                    {
                        if (!instruments.Any(item =>
                            item.SegmentId == index.SegmentId &&
                            item.ExchangeInstrumentId ==
                                index.ExchangeInstrumentId))
                            instruments.Add(index);
                    }
                }
                catch (Exception error)
                {
                    this.AddWarningLog(
                        "Wisdom Capital XTS index list for segment {0} was not loaded: {1}",
                        segmentId,
                        error.Message);
                }
            }

            _instruments = [.. instruments];
            if (_instruments.Length == 0)
            {
                throw new InvalidDataException(
                    "Wisdom Capital XTS instrument master contained no valid instruments.");
            }

            _byKey = _instruments
                .GroupBy(
                    item => WisdomCapitalExtensions.CreateInstrumentKey(
                        item.ExchangeSegment,
                        item.ExchangeInstrumentId),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            _bySymbol = _instruments
                .Where(item => !item.TradingSymbol.IsEmpty())
                .GroupBy(
                    item => SymbolKey(
                        item.ExchangeSegment,
                        item.TradingSymbol),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            return _instruments;
        }
        finally
        {
            _instrumentLock.Release();
        }
    }

    public async Task<WisdomInstrument> GetInstrument(
        string segment,
        long instrumentId,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        _byKey.TryGetValue(
            WisdomCapitalExtensions.CreateInstrumentKey(
                segment,
                instrumentId),
            out var instrument);
        return instrument;
    }

    public async Task<WisdomInstrument> FindInstrument(
        string segment,
        string symbol,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        if (_bySymbol.TryGetValue(
            SymbolKey(segment, symbol),
            out var instrument))
            return instrument;
        return _instruments.FirstOrDefault(item =>
            item.ExchangeSegment.EqualsIgnoreCase(segment) &&
            (item.TradingSymbol.EqualsIgnoreCase(symbol) ||
                item.DisplayName.EqualsIgnoreCase(symbol) ||
                item.Description.EqualsIgnoreCase(symbol) ||
                item.Name.EqualsIgnoreCase(symbol)));
    }

    public async Task<WisdomMarketUpdate[]> GetQuotes(
        WisdomInstrumentReference instrument,
        int messageCode,
        CancellationToken cancellationToken)
        => ParseQuoteResponse(await Send(
            "apimarketdata/instruments/quotes",
            HttpMethod.Post,
            CreateSubscriptionBody(instrument, messageCode, true),
            ApiChannel.MarketData,
            cancellationToken));

    public async Task<WisdomMarketUpdate[]> Subscribe(
        WisdomInstrumentReference instrument,
        int messageCode,
        CancellationToken cancellationToken)
        => ParseQuoteResponse(await Send(
            "apimarketdata/instruments/subscription",
            HttpMethod.Post,
            CreateSubscriptionBody(instrument, messageCode, false),
            ApiChannel.MarketData,
            cancellationToken));

    public Task Unsubscribe(
        WisdomInstrumentReference instrument,
        int messageCode,
        CancellationToken cancellationToken)
        => Send(
            "apimarketdata/instruments/subscription",
            HttpMethod.Put,
            CreateSubscriptionBody(instrument, messageCode, false),
            ApiChannel.MarketData,
            cancellationToken);

    public async Task<WisdomCandle[]> GetCandles(
        WisdomInstrument instrument,
        TimeSpan timeFrame,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);
        var compression = ToCompression(timeFrame);
        var candles = new List<WisdomCandle>();
        var cursor = EnsureUtc(from);
        var upperBound = EnsureUtc(to);
        var chunkSpan = TimeSpan.FromDays(6);
        if (timeFrame >= TimeSpan.FromDays(7))
            chunkSpan = TimeSpan.FromTicks(timeFrame.Ticks * 2);

        while (cursor <= upperBound)
        {
            var chunkEnd = cursor.Add(chunkSpan);
            if (chunkEnd > upperBound)
                chunkEnd = upperBound;
            var path =
                "apimarketdata/instruments/ohlc" +
                $"?exchangeSegment={Uri.EscapeDataString(instrument.ExchangeSegment)}" +
                $"&exchangeInstrumentID={instrument.ExchangeInstrumentId.ToString(CultureInfo.InvariantCulture)}" +
                $"&startTime={Uri.EscapeDataString(ToApiTime(cursor))}" +
                $"&endTime={Uri.EscapeDataString(ToApiTime(chunkEnd))}" +
                $"&compressionValue={Uri.EscapeDataString(compression)}";
            var response = await Send(
                path,
                HttpMethod.Get,
                null,
                ApiChannel.MarketData,
                cancellationToken);
            var result = UnwrapResult(response);
            var raw =
                WisdomCapitalExtensions.FindProperty(
                    result,
                    "dataReponse")?.Value<string>() ??
                WisdomCapitalExtensions.FindProperty(
                    result,
                    "dataResponse")?.Value<string>() ??
                (result.Type == JTokenType.String
                    ? result.Value<string>()
                    : null);
            if (!raw.IsEmpty())
                candles.AddRange(ParseCandles(raw));

            if (chunkEnd >= upperBound)
                break;
            cursor = chunkEnd.AddSeconds(1);
        }

        return
        [
            ..
                candles
                    .GroupBy(candle => candle.OpenTime)
                    .Select(group => group.First())
                    .OrderBy(candle => candle.OpenTime)
        ];
    }

    public async Task<string> PlaceOrder(
        JObject order,
        CancellationToken cancellationToken)
        => ParseOrderId(await Send(
            "interactive/orders",
            HttpMethod.Post,
            order ?? throw new ArgumentNullException(nameof(order)),
            ApiChannel.Interactive,
            cancellationToken));

    public async Task<string> ModifyOrder(
        JObject order,
        CancellationToken cancellationToken)
        => ParseOrderId(await Send(
            "interactive/orders",
            HttpMethod.Put,
            order ?? throw new ArgumentNullException(nameof(order)),
            ApiChannel.Interactive,
            cancellationToken));

    public Task CancelOrder(
        string orderId,
        string uniqueIdentifier,
        CancellationToken cancellationToken)
    {
        orderId.ThrowIfEmpty(nameof(orderId));
        var path =
            "interactive/orders" +
            $"?appOrderID={Uri.EscapeDataString(orderId)}" +
            $"&orderUniqueIdentifier={Uri.EscapeDataString(uniqueIdentifier.IsEmpty("StockSharp"))}";
        return Send(
            path,
            HttpMethod.Delete,
            null,
            ApiChannel.Interactive,
            cancellationToken);
    }

    public async Task<WisdomOrder[]> GetOrders(
        CancellationToken cancellationToken)
        => ToArray<WisdomOrder>(UnwrapResult(await Send(
            "interactive/orders",
            HttpMethod.Get,
            null,
            ApiChannel.Interactive,
            cancellationToken)));

    public async Task<WisdomTrade[]> GetTrades(
        CancellationToken cancellationToken)
        => ToArray<WisdomTrade>(UnwrapResult(await Send(
            "interactive/orders/trades",
            HttpMethod.Get,
            null,
            ApiChannel.Interactive,
            cancellationToken)));

    public async Task<WisdomPosition[]> GetPositions(
        CancellationToken cancellationToken)
    {
        var result = UnwrapResult(await Send(
            "interactive/portfolio/positions?dayOrNet=NetWise",
            HttpMethod.Get,
            null,
            ApiChannel.Interactive,
            cancellationToken));
        var rows =
            WisdomCapitalExtensions.FindProperty(
                result,
                "positionList") ??
            result;
        return ToArray<WisdomPosition>(rows);
    }

    public async Task<WisdomHolding[]> GetHoldings(
        CancellationToken cancellationToken)
    {
        var result = UnwrapResult(await Send(
            "interactive/portfolio/holdings",
            HttpMethod.Get,
            null,
            ApiChannel.Interactive,
            cancellationToken));
        if (WisdomCapitalExtensions.FindProperty(
                WisdomCapitalExtensions.FindProperty(
                    result,
                    "RMSHoldings"),
                "Holdings") is not JObject holdings)
            return [];

        return
        [
            ..
                holdings.Properties().Select(property =>
                {
                    var row = property.Value;
                    var nseId = WisdomCapitalExtensions.LongAt(
                        row,
                        "ExchangeNSEInstrumentId");
                    var bseId = WisdomCapitalExtensions.LongAt(
                        row,
                        "ExchangeBSEInstrumentId");
                    return new WisdomHolding
                    {
                        Isin = property.Name,
                        ExchangeSegment =
                            nseId > 0
                                ? "NSECM"
                                : bseId > 0
                                    ? "BSECM"
                                    : null,
                        ExchangeInstrumentId =
                            nseId > 0 ? nseId : bseId,
                        Quantity = WisdomCapitalExtensions.DecimalAt(
                            row,
                            "HoldingQuantity"),
                        AveragePrice = WisdomCapitalExtensions.DecimalAt(
                            row,
                            "BuyAvgPrice"),
                    };
                })
        ];
    }

    public async Task<WisdomFunds> GetFunds(
        CancellationToken cancellationToken)
    {
        var result = UnwrapResult(await Send(
            "interactive/user/balance",
            HttpMethod.Get,
            null,
            ApiChannel.Interactive,
            cancellationToken));
        var limits = (result as JContainer)?
            .Descendants()
            .OfType<JProperty>()
            .FirstOrDefault(property => property.Name.Equals(
                "RMSSubLimits",
                StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return new()
        {
            Available = WisdomCapitalExtensions.DecimalAt(
                limits,
                "netMarginAvailable"),
            Collateral = WisdomCapitalExtensions.DecimalAt(
                limits,
                "collateral"),
            Utilized = WisdomCapitalExtensions.DecimalAt(
                limits,
                "marginUtilized"),
            UnrealizedPnl = WisdomCapitalExtensions.DecimalAt(
                limits,
                "UnrealizedMTM"),
            RealizedPnl = WisdomCapitalExtensions.DecimalAt(
                limits,
                "RealizedMTM"),
        };
    }

    internal static IEnumerable<WisdomInstrument> ParseInstrumentMaster(
        string content)
    {
        if (content.IsEmpty())
            yield break;
        foreach (var line in content.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('|');
            if (fields.Length < 14)
                continue;
            var segment = NormalizeSegment(fields[0]);
            if (segment.IsEmpty() ||
                !long.TryParse(
                    fields[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var instrumentId) ||
                instrumentId <= 0)
                continue;

            var segmentId = segment.ToSegmentId();
            var derivative = segment is
                "NSEFO" or "NSECD" or "BSEFO" or "MCXFO";
            int? optionType = derivative && fields.Length > 18 &&
                int.TryParse(
                    fields[18],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedOption)
                    ? parsedOption
                    : null;
            var displayName = fields.Length > (derivative ? 19 : 14)
                ? fields[derivative ? 19 : 14].Trim()
                : null;
            var description = fields[4].Trim();
            var tradingSymbol = derivative
                ? description
                : displayName.IsEmpty(fields[6].Trim())
                    .IsEmpty(fields[3].Trim());
            yield return new()
            {
                ExchangeSegment = segment,
                SegmentId = segmentId,
                ExchangeInstrumentId = instrumentId,
                InstrumentType = fields[2].Trim(),
                Name = fields[3].Trim(),
                Description = description,
                Series = fields[5].Trim(),
                TradingSymbol = tradingSymbol,
                DisplayName = displayName,
                Isin = !derivative && fields.Length > 15
                    ? fields[15].Trim()
                    : null,
                TickSize = ParseDecimal(fields[11]),
                LotSize = ParseDecimal(fields[12], 1),
                Multiplier = ParseDecimal(fields[13], 1),
                UnderlyingInstrumentId =
                    derivative && fields.Length > 14 &&
                    long.TryParse(
                        fields[14],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var underlyingId)
                            ? underlyingId
                            : null,
                UnderlyingName =
                    derivative && fields.Length > 15
                        ? fields[15].Trim()
                        : null,
                ExpiryDate =
                    derivative && fields.Length > 16
                        ? ParseExpiry(fields[16])
                        : null,
                StrikePrice =
                    derivative && fields.Length > 17
                        ? ParseDecimal(fields[17])
                        : 0,
                OptionTypeCode = optionType,
            };
        }
    }

    internal static WisdomMarketUpdate ParseMarketUpdate(
        JToken token,
        DateTime fallback)
    {
        if (token?.Type == JTokenType.String)
        {
            var json = token.Value<string>();
            if (!json.IsEmpty())
                token = JToken.Parse(json);
        }
        if (token is not JObject)
        {
            throw new InvalidDataException(
                "Wisdom Capital XTS market update is not a JSON object.");
        }

        var touchline =
            WisdomCapitalExtensions.FindProperty(token, "Touchline") ??
            token;
        var messageCode = (int)WisdomCapitalExtensions.LongAt(
            token,
            "MessageCode");
        var segmentId = (int)WisdomCapitalExtensions.LongAt(
            token,
            "ExchangeSegment");
        var timestamp =
            WisdomCapitalExtensions.FindProperty(
                touchline,
                "LastTradedTime") ??
            WisdomCapitalExtensions.FindProperty(
                token,
                "ExchangeTimeStamp");
        return new()
        {
            MessageCode = messageCode,
            SegmentId = segmentId,
            ExchangeInstrumentId = WisdomCapitalExtensions.LongAt(
                token,
                "ExchangeInstrumentID"),
            ServerTime = timestamp.ToWisdomTime(fallback),
            LastPrice = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "LastTradedPrice"),
            LastVolume =
                WisdomCapitalExtensions.DecimalAt(
                    touchline,
                    "LastTradedQuantity") is var lastQuantity &&
                lastQuantity > 0
                    ? lastQuantity
                    : WisdomCapitalExtensions.DecimalAt(
                        touchline,
                        "LastTradedQunatity"),
            OpenPrice = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "Open"),
            HighPrice = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "High"),
            LowPrice = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "Low"),
            ClosePrice = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "Close"),
            Volume = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "TotalTradedQuantity"),
            AveragePrice =
                WisdomCapitalExtensions.DecimalAt(
                    touchline,
                    "AverageTradedPrice") is var average &&
                average > 0
                    ? average
                    : WisdomCapitalExtensions.DecimalAt(
                        touchline,
                        "AveragePrice"),
            TotalBuyVolume = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "TotalBuyQuantity"),
            TotalSellVolume = WisdomCapitalExtensions.DecimalAt(
                touchline,
                "TotalSellQuantity"),
            OpenInterest = WisdomCapitalExtensions.DecimalAt(
                token,
                "OpenInterest"),
            UpperCircuit = WisdomCapitalExtensions.DecimalAt(
                token,
                "UpperCircuitLimit"),
            LowerCircuit = WisdomCapitalExtensions.DecimalAt(
                token,
                "LowerCircuitLimit"),
            Bids = ParseDepth(
                WisdomCapitalExtensions.FindProperty(token, "Bids")),
            Asks = ParseDepth(
                WisdomCapitalExtensions.FindProperty(token, "Asks")),
        };
    }

    internal static WisdomCandle[] ParseCandles(string raw)
    {
        if (raw.IsEmpty())
            return [];
        var result = new List<WisdomCandle>();
        foreach (var row in raw.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries))
        {
            var fields = row.Split('|');
            if (fields.Length < 6 ||
                !long.TryParse(
                    fields[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var timestamp))
                continue;
            try
            {
                result.Add(new()
                {
                    OpenTime = DateTimeOffset
                        .FromUnixTimeSeconds(timestamp)
                        .UtcDateTime
                        .Subtract(TimeSpan.FromMinutes(330)),
                    Open = ParseDecimal(fields[1]),
                    High = ParseDecimal(fields[2]),
                    Low = ParseDecimal(fields[3]),
                    Close = ParseDecimal(fields[4]),
                    Volume = ParseDecimal(fields[5]),
                    OpenInterest = fields.Length > 6
                        ? ParseDecimal(fields[6])
                        : null,
                });
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        return [.. result];
    }

    internal static JToken ParseResponse(
        string path,
        string json,
        HttpStatusCode statusCode)
    {
        JToken response = null;
        if (!json.IsEmpty())
        {
            try
            {
                response = JToken.Parse(json);
            }
            catch (JsonException error)
            {
                if ((int)statusCode is >= 200 and < 300)
                {
                    throw new InvalidDataException(
                        $"Wisdom Capital XTS {path} returned invalid JSON.",
                        error);
                }
            }
        }

        var success = (int)statusCode is >= 200 and < 300;
        var type = WisdomCapitalExtensions
            .FindProperty(response, "type")
            ?.Value<string>();
        if (!type.IsEmpty() && !type.EqualsIgnoreCase("success"))
            success = false;
        if (!success)
        {
            var message =
                WisdomCapitalExtensions
                    .FindProperty(response, "description")
                    ?.Value<string>()
                    .IsEmpty(
                        WisdomCapitalExtensions
                            .FindProperty(response, "message")
                            ?.Value<string>())
                    .IsEmpty(statusCode.ToString());
            throw new InvalidOperationException(
                $"Wisdom Capital XTS {path} returned HTTP {(int)statusCode}: {message}");
        }
        return response ?? new JObject();
    }

    private async Task<JToken> Send(
        string path,
        HttpMethod method,
        object body,
        ApiChannel channel,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        var token = channel switch
        {
            ApiChannel.Interactive => _interactiveToken,
            ApiChannel.MarketData => _marketDataToken,
            _ => null,
        };
        if (channel != ApiChannel.Public)
        {
            var publicMaster =
                channel == ApiChannel.MarketData &&
                (path.StartsWith(
                        "apimarketdata/instruments/master",
                        StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(
                        "apimarketdata/instruments/indexlist",
                        StringComparison.OrdinalIgnoreCase));
            if (token.IsEmpty() && !publicMaster)
            {
                token.ThrowIfEmpty(
                    channel == ApiChannel.Interactive
                        ? "Wisdom Capital interactive token"
                        : "Wisdom Capital market-data token");
            }
            if (!token.IsEmpty())
            {
                request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    token);
            }
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
            "Wisdom Capital XTS {0} {1}.",
            method,
            path.Split('?')[0]);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);
        return ParseResponse(path.Split('?')[0], json, response.StatusCode);
    }

    private async Task<WisdomInstrument[]> GetIndices(
        int segmentId,
        CancellationToken cancellationToken)
    {
        var response = await Send(
            "apimarketdata/instruments/indexlist" +
            $"?exchangeSegment={segmentId.ToString(CultureInfo.InvariantCulture)}",
            HttpMethod.Get,
            null,
            ApiChannel.MarketData,
            cancellationToken);
        if (WisdomCapitalExtensions.FindProperty(
            UnwrapResult(response),
            "indexList") is not JArray list)
            return [];
        var segment = segmentId.ToExchangeSegment();
        return
        [
            ..
                list.Values<string>()
                    .Select(item => ParseIndex(item, segment, segmentId))
                    .Where(item => item != null)
        ];
    }

    private static WisdomInstrument ParseIndex(
        string value,
        string segment,
        int segmentId)
    {
        if (value.IsEmpty())
            return null;
        var separator = value.LastIndexOf('_');
        if (separator <= 0 ||
            !long.TryParse(
                value[(separator + 1)..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var instrumentId))
            return null;
        var name = value[..separator].Trim();
        return new()
        {
            ExchangeSegment = segment,
            SegmentId = segmentId,
            ExchangeInstrumentId = instrumentId,
            InstrumentType = "INDEX",
            Name = name,
            Description = name,
            TradingSymbol = name,
            DisplayName = name,
            TickSize = 0.05m,
            LotSize = 1,
            Multiplier = 1,
            IsIndex = true,
        };
    }

    private static object CreateSubscriptionBody(
        WisdomInstrumentReference instrument,
        int messageCode,
        bool publishFormat)
        => publishFormat
            ? new
            {
                instruments = new[]
                {
                    new
                    {
                        exchangeSegment = instrument.SegmentId,
                        exchangeInstrumentID =
                            instrument.ExchangeInstrumentId,
                    },
                },
                xtsMessageCode = messageCode,
                publishFormat = "JSON",
            }
            : new
            {
                instruments = new[]
                {
                    new
                    {
                        exchangeSegment = instrument.SegmentId,
                        exchangeInstrumentID =
                            instrument.ExchangeInstrumentId,
                    },
                },
                xtsMessageCode = messageCode,
            };

    private static WisdomMarketUpdate[] ParseQuoteResponse(JToken response)
    {
        if (WisdomCapitalExtensions.FindProperty(
            UnwrapResult(response),
            "listQuotes") is not JArray quotes)
            return [];
        var result = new List<WisdomMarketUpdate>();
        foreach (var quote in quotes)
        {
            try
            {
                result.Add(ParseMarketUpdate(quote, DateTime.UtcNow));
            }
            catch (Exception)
            {
            }
        }
        return [.. result];
    }

    private static WisdomDepthLevel[] ParseDepth(JToken token)
    {
        if (token is not JArray rows)
            return [];
        return
        [
            ..
                rows.OfType<JObject>()
                    .Select(row => new WisdomDepthLevel
                    {
                        Price = WisdomCapitalExtensions.DecimalAt(
                            row,
                            "Price"),
                        Volume = WisdomCapitalExtensions.DecimalAt(
                            row,
                            "Size"),
                        Orders = WisdomCapitalExtensions.LongAt(
                            row,
                            "TotalOrders"),
                    })
                    .Where(level => level.Price > 0)
        ];
    }

    private static T[] ToArray<T>(JToken token)
    {
        if (token is JArray array)
            return array.ToObject<T[]>() ?? [];
        if (token is JObject obj)
        {
            var rows = obj.Properties()
                .Select(property => property.Value)
                .FirstOrDefault(value => value is JArray);
            if (rows is JArray nested)
                return nested.ToObject<T[]>() ?? [];
        }
        return [];
    }

    private static string ParseOrderId(JToken response)
    {
        var result = UnwrapResult(response);
        var value =
            WisdomCapitalExtensions.FindProperty(
                result,
                "AppOrderID") ??
            WisdomCapitalExtensions.FindProperty(
                result,
                "appOrderID");
        var orderId = WisdomCapitalExtensions.TokenString(value);
        if (orderId.IsEmpty())
        {
            throw new InvalidDataException(
                "Wisdom Capital XTS order response contained no AppOrderID.");
        }
        return orderId;
    }

    private static JToken UnwrapResult(JToken response)
        => WisdomCapitalExtensions.FindProperty(response, "result") ??
            response ??
            new JObject();

    private static string NormalizeSegment(string value)
    {
        value = value?.Trim();
        if (int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var id))
        {
            try
            {
                return id.ToExchangeSegment();
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return _segments.Contains(
            value,
            StringComparer.OrdinalIgnoreCase)
                ? value.ToUpperInvariant()
                : null;
    }

    private static string SymbolKey(string segment, string symbol)
        => $"{segment}:{symbol}";

    private static decimal ParseDecimal(
        string value,
        decimal defaultValue = 0)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : defaultValue;

    private static DateTime? ParseExpiry(string value)
    {
        if (value.IsEmpty())
            return null;
        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd-MM-yyyy",
            "dd/MM/yyyy",
            "dd-MMM-yyyy",
            "MM/dd/yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
        };
        return DateTime.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed)
                ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
                : null;
    }

    private static string ToCompression(TimeSpan timeFrame)
    {
        if (timeFrame == TimeSpan.FromMinutes(1))
            return "60";
        if (timeFrame == TimeSpan.FromMinutes(2))
            return "120";
        if (timeFrame == TimeSpan.FromMinutes(3))
            return "180";
        if (timeFrame == TimeSpan.FromMinutes(4))
            return "240";
        if (timeFrame == TimeSpan.FromMinutes(5))
            return "300";
        if (timeFrame == TimeSpan.FromMinutes(7))
            return "420";
        if (timeFrame == TimeSpan.FromMinutes(10))
            return "600";
        if (timeFrame == TimeSpan.FromMinutes(15))
            return "900";
        if (timeFrame == TimeSpan.FromMinutes(30))
            return "1800";
        if (timeFrame == TimeSpan.FromHours(1))
            return "3600";
        if (timeFrame == TimeSpan.FromHours(2))
            return "7200";
        if (timeFrame == TimeSpan.FromHours(3))
            return "10800";
        if (timeFrame == TimeSpan.FromHours(4))
            return "14400";
        if (timeFrame == TimeSpan.FromDays(1))
            return "D";
        if (timeFrame == TimeSpan.FromDays(7))
            return "W";
        if (timeFrame == TimeSpan.FromDays(30))
            return "M";
        throw new NotSupportedException(
            $"Wisdom Capital XTS does not support the {timeFrame} candle interval.");
    }

    private static string ToApiTime(DateTime utc)
        => new DateTimeOffset(EnsureUtc(utc))
            .ToOffset(TimeSpan.FromMinutes(330))
            .ToString("MMM dd yyyy HHmmss", CultureInfo.InvariantCulture);

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
