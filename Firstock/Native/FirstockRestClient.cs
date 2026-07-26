namespace StockSharp.Firstock.Native;

sealed class FirstockRestClient : BaseLogReceiver
{
    private static readonly string[] _segments = ["NSE", "BSE", "NFO", "BFO", "Indices"];
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly string _userId;
    private readonly Uri _symbolsAddress;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private FirstockInstrument[] _instruments;
    private IReadOnlyDictionary<string, FirstockInstrument> _instrumentsByKey;
    private IReadOnlyDictionary<string, FirstockInstrument> _instrumentsBySymbol;

    public FirstockRestClient(string userId, SecureString sessionToken, Uri address, Uri symbolsAddress)
    {
        _userId = userId.ThrowIfEmpty(nameof(userId));
        SessionToken = sessionToken?.UnSecure();
        _symbolsAddress = symbolsAddress ?? throw new ArgumentNullException(nameof(symbolsAddress));
        _httpClient = new() { BaseAddress = address ?? throw new ArgumentNullException(nameof(address)) };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Firstock/1.0");
    }

    public override string Name => nameof(Firstock) + "_" + nameof(FirstockRestClient);

    public string SessionToken { get; private set; }

    public string AccountId { get; private set; }

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<FirstockLoginResult> Authenticate(
        SecureString password,
        SecureString oneTimePassword,
        string vendorCode,
        SecureString apiKey,
        CancellationToken cancellationToken)
    {
        if (!SessionToken.IsEmpty())
        {
            AccountId = _userId;
            return new()
            {
                AccountId = AccountId,
                SessionToken = SessionToken,
            };
        }

        password.ThrowIfEmpty(nameof(password));
        vendorCode.ThrowIfEmpty(nameof(vendorCode));
        apiKey.ThrowIfEmpty(nameof(apiKey));

        var data = await SendCore("login", new JObject
        {
            ["userId"] = _userId,
            ["password"] = HashPassword(password.UnSecure()),
            ["TOTP"] = oneTimePassword?.UnSecure() ?? string.Empty,
            ["vendorCode"] = vendorCode,
            ["apiKey"] = apiKey.UnSecure(),
        }, cancellationToken);

        var result = new FirstockLoginResult
        {
            AccountId = data.GetText("actid", "accountId").IsEmpty(_userId),
            UserName = data.GetText("userName"),
            SessionToken = data.GetText("susertoken", "jKey", "token"),
            Email = data.GetText("email"),
        };
        SessionToken = result.SessionToken.ThrowIfEmpty(nameof(result.SessionToken));
        AccountId = result.AccountId;
        return result;
    }

    public async Task<FirstockInstrument[]> GetInstruments(CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            var pages = await Task.WhenAll(
                _segments.Select(segment => DownloadInstruments(segment, cancellationToken)));
            _instruments = [.. pages.SelectMany(page => page)];
            _instrumentsByKey = _instruments
                .GroupBy(i => i.Exchange.ToInstrumentKey(i.Token), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            _instrumentsBySymbol = _instruments
                .Where(i => !i.TradingSymbol.IsEmpty())
                .GroupBy(i => ToSymbolKey(i.Exchange, i.TradingSymbol), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            return _instruments;
        }
        finally
        {
            _instrumentLock.Release();
        }
    }

    public async Task<FirstockInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument)
            ? instrument
            : null;
    }

    public async Task<FirstockInstrument> FindInstrument(
        string exchange,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsBySymbol.TryGetValue(
            ToSymbolKey(exchange, tradingSymbol), out var instrument)
            ? instrument
            : null;
    }

    public async Task<string> PlaceOrder(
        FirstockPlaceOrderRequest order,
        bool afterMarket,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            afterMarket ? "placeAMO" : "placeOrder",
            JObject.FromObject(order, JsonSerializer.Create(_jsonSettings)),
            cancellationToken);
        var item = data is JArray array ? array.FirstOrDefault() : data;
        return (item?.GetText("orderNumber", "norenordno"))
            .ThrowIfEmpty(nameof(FirstockOrder.OrderId));
    }

    public async Task ModifyOrder(
        FirstockModifyOrderRequest order,
        bool afterMarket,
        CancellationToken cancellationToken)
    {
        var operation = afterMarket ? "modifyAMO" : "modifyOrder";
        var data = await SendAuthenticated(
            operation,
            JObject.FromObject(order, JsonSerializer.Create(_jsonSettings)),
            cancellationToken);
        EnsureOrderActionAccepted(data, operation);
    }

    public async Task CancelOrder(string orderId, CancellationToken cancellationToken)
    {
        const string operation = "cancelOrder";
        var data = await SendAuthenticated(operation, new JObject
        {
            ["orderNumber"] = orderId.ThrowIfEmpty(nameof(orderId)),
        }, cancellationToken);
        EnsureOrderActionAccepted(data, operation);
    }

    public async Task<FirstockOrder[]> GetOrders(CancellationToken cancellationToken)
        => ParseArray<FirstockOrder>(
            await SendAuthenticated("orderBook", new(), cancellationToken));

    public async Task<FirstockTrade[]> GetTrades(CancellationToken cancellationToken)
        => ParseArray<FirstockTrade>(
            await SendAuthenticated("tradeBook", new(), cancellationToken));

    public async Task<FirstockPosition[]> GetPositions(CancellationToken cancellationToken)
        => ParseArray<FirstockPosition>(
            await SendAuthenticated("positionBook", new(), cancellationToken));

    public async Task<FirstockHolding[]> GetHoldings(CancellationToken cancellationToken)
        => ParseArray<FirstockHolding>(
            await SendAuthenticated("holdingsDetails", new(), cancellationToken));

    public async Task<FirstockLimits> GetLimits(CancellationToken cancellationToken)
        => (await SendAuthenticated("limit", new(), cancellationToken))
            .ToObject<FirstockLimits>(JsonSerializer.Create(_jsonSettings)) ?? new();

    public async Task<FirstockCandle[]> GetCandles(
        FirstockInstrument instrument,
        TimeSpan timeFrame,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        var interval = ToInterval(timeFrame);
        var end = NormalizeUtc(to ?? DateTime.UtcNow);
        var start = NormalizeUtc(from ??
            end.AddDays(timeFrame == TimeSpan.FromDays(1) ? -3650 : -30));
        if (start > end)
            return [];

        var data = await SendAuthenticated("timePriceSeries", new JObject
        {
            ["exchange"] = instrument.Exchange,
            ["tradingSymbol"] = instrument.TradingSymbol,
            ["startTime"] = FormatApiTime(start),
            ["endTime"] = FormatApiTime(end),
            ["interval"] = interval,
        }, cancellationToken);
        return ParseArray<FirstockCandle>(data);
    }

    internal static string HashPassword(string password)
    {
        password.ThrowIfEmpty(nameof(password));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(password)))
            .ToLowerInvariant();
    }

    internal static JToken ParseResponse(string content, string operation)
    {
        if (content.IsEmpty())
            throw new InvalidOperationException(
                $"Firstock returned an empty response for {operation}.");

        JToken root;
        try
        {
            root = JToken.Parse(content);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                $"Firstock returned invalid JSON for {operation}.", ex);
        }

        if (root is not JObject obj)
            throw new InvalidDataException(
                $"Firstock returned an unexpected response for {operation}.");

        var status = obj.GetText("status");
        if (status.EqualsIgnoreCase("success") || status.EqualsIgnoreCase("ok"))
            return obj.GetValueIgnoreCase("data") ?? obj;

        var error = obj.GetValueIgnoreCase("error");
        var message = (error?.GetText("message"))
            .IsEmpty(obj.GetText("message"))
            .IsEmpty(obj.GetText("name"))
            .IsEmpty(status)
            .IsEmpty("Unknown API error.");
        throw new InvalidOperationException($"Firstock {operation} error: {message}");
    }

    internal static FirstockInstrument ParseInstrument(string segment, string[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        FirstockInstrument instrument;
        switch (segment?.ToUpperInvariant())
        {
            case "NSE":
            case "BSE":
                if (values.Length < 8)
                    throw new InvalidDataException($"Firstock {segment} symbol row has fewer than 8 fields.");
                instrument = new()
                {
                    Exchange = values[0],
                    Token = values[1],
                    LotSize = values[2].ToDecimal(),
                    TradingSymbol = values[3],
                    Symbol = values[3],
                    CompanyName = values[4],
                    Isin = values[5],
                    TickSize = values[6].ToDecimal(),
                    FreezeQuantity = values[7].ToDecimal(),
                    Instrument = "EQT",
                };
                break;

            case "NFO":
            case "BFO":
                if (values.Length < 12)
                    throw new InvalidDataException($"Firstock {segment} symbol row has fewer than 12 fields.");
                instrument = new()
                {
                    Exchange = values[0],
                    Token = values[1],
                    LotSize = values[2].ToDecimal(),
                    Symbol = values[3],
                    TradingSymbol = values[4],
                    CompanyName = values[5],
                    Expiry = ParseExpiry(values[6]),
                    Instrument = values[7],
                    OptionType = values[8],
                    StrikePrice = values[9].ToDecimal(),
                    TickSize = values[10].ToDecimal(),
                    FreezeQuantity = values[11].ToDecimal(),
                };
                break;

            case "INDICES":
                if (values.Length < 10)
                    throw new InvalidDataException("Firstock Indices symbol row has fewer than 10 fields.");
                instrument = new()
                {
                    Exchange = values[0],
                    Token = values[1],
                    LotSize = values[2].ToDecimal(),
                    Symbol = values[3],
                    TradingSymbol = values[4],
                    Expiry = ParseExpiry(values[5]),
                    Instrument = values[6].IsEmpty("INDEX"),
                    OptionType = values[7],
                    StrikePrice = values[8].ToDecimal(),
                    TickSize = values[9].ToDecimal(),
                };
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(segment), segment, null);
        }

        if (instrument.Exchange.IsEmpty() ||
            instrument.Token.IsEmpty() ||
            instrument.TradingSymbol.IsEmpty())
            return null;
        instrument.Exchange = instrument.Exchange.ToBoardCode();
        instrument.LotSize = instrument.LotSize > 0 ? instrument.LotSize : 1m;
        return instrument;
    }

    internal static T[] ParseArray<T>(JToken data)
    {
        if (data == null || data.Type is JTokenType.Null or JTokenType.Undefined)
            return [];
        var serializer = JsonSerializer.Create(_jsonSettings);
        if (data is JArray array)
            return array.ToObject<T[]>(serializer) ?? [];
        return data.Type == JTokenType.Object
            ? [data.ToObject<T>(serializer)]
            : [];
    }

    internal static void EnsureOrderActionAccepted(JToken data, string operation)
    {
        var item = data is JArray array ? array.FirstOrDefault() : data;
        var rejection = item?.GetText("rejreason", "rejectReason");
        if (!rejection.IsEmpty() && !string.IsNullOrWhiteSpace(rejection) &&
            rejection.Trim() != "-")
            throw new InvalidOperationException($"Firstock {operation} rejected: {rejection.Trim()}");
    }

    private async Task<JToken> SendAuthenticated(
        string path,
        JObject body,
        CancellationToken cancellationToken)
    {
        SessionToken.ThrowIfEmpty(nameof(SessionToken));
        body ??= new();
        body["userId"] = _userId;
        body["jKey"] = SessionToken;
        return await SendCore(path, body, cancellationToken);
    }

    private async Task<JToken> SendCore(
        string path,
        JObject body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json"),
        };
        this.AddVerboseLog("Firstock POST {0}.", path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return ParseResponse(content, path);
        }
        catch (Exception ex) when (!response.IsSuccessStatusCode &&
            ex is InvalidDataException or InvalidOperationException)
        {
            throw new HttpRequestException(
                $"Firstock {path} returned HTTP {(int)response.StatusCode}: {ex.Message}",
                ex,
                response.StatusCode);
        }
    }

    private async Task<FirstockInstrument[]> DownloadInstruments(
        string segment,
        CancellationToken cancellationToken)
    {
        var address = new Uri(_symbolsAddress, segment);
        this.AddVerboseLog("Firstock GET {0}.", address);
        using var response = await _httpClient.GetAsync(
            address,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);
        var csv = new FastCsvReader(reader, StringHelper.N) { ColumnSeparator = ',' };
        if (!await csv.NextLineAsync(cancellationToken))
            return [];

        var columnCount = segment.ToUpperInvariant() switch
        {
            "NSE" or "BSE" => 8,
            "NFO" or "BFO" => 12,
            "INDICES" => 10,
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };
        var instruments = new List<FirstockInstrument>();
        while (await csv.NextLineAsync(cancellationToken))
        {
            var values = new string[columnCount];
            for (var i = 0; i < values.Length; i++)
                values[i] = csv.ReadString()?.Trim();
            var instrument = ParseInstrument(segment, values);
            if (instrument != null)
                instruments.Add(instrument);
        }
        return [.. instruments];
    }

    private static string ToInterval(TimeSpan timeFrame)
    {
        if (timeFrame == TimeSpan.FromDays(1))
            return "1d";
        if (timeFrame <= TimeSpan.Zero ||
            timeFrame.TotalMinutes != Math.Truncate(timeFrame.TotalMinutes))
            throw new ArgumentOutOfRangeException(
                nameof(timeFrame), timeFrame, "Firstock candle intervals must be whole minutes or one day.");
        var minutes = Convert.ToInt32(timeFrame.TotalMinutes);
        if (minutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(
                nameof(timeFrame), timeFrame, "Unsupported Firstock candle interval.");
        return $"{minutes.ToString(CultureInfo.InvariantCulture)}mi";
    }

    private static string FormatApiTime(DateTime value)
        => value.ToIndiaLocal().ToString("HH:mm:ss dd-MM-yyyy", CultureInfo.InvariantCulture);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static DateTime? ParseExpiry(string value)
        => DateTime.TryParseExact(
            value,
            "dd-MMM-yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var expiry)
            ? expiry.ToUtcFromIndia()
            : null;

    private static string ToSymbolKey(string exchange, string tradingSymbol)
        => $"{exchange?.ToUpperInvariant()}|{tradingSymbol?.ToUpperInvariant()}";
}
