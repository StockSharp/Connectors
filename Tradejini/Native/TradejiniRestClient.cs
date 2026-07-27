namespace StockSharp.Tradejini.Native;

sealed class TradejiniRestClient : BaseLogReceiver
{
    private static readonly string[] _masterGroups =
    [
        "CommodityOptions",
        "FutureContracts",
        "CommodityFuture",
        "NSEOptions",
        "CurrencyOptions",
        "BSEOptions",
        "CurrencyFuture",
        "Securities",
        "Spot",
        "Index",
    ];

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TradejiniInstrument[] _instruments;
    private IReadOnlyDictionary<string, TradejiniInstrument>
        _instrumentsById;
    private DateTime _lastRequestAt;

    public TradejiniRestClient(
        SecureString apiKey,
        SecureString accessToken,
        Uri address,
        HttpMessageHandler handler = null)
    {
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
        AccessToken = accessToken?.UnSecure();
        _httpClient = handler == null
            ? new HttpClient()
            : new HttpClient(handler, true);
        _httpClient.BaseAddress =
            address ?? throw new ArgumentNullException(nameof(address));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Tradejini/1.0");
    }

    public override string Name =>
        nameof(Tradejini) + "_" + nameof(TradejiniRestClient);

    public string AccessToken { get; private set; }

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        _requestLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<TradejiniLoginResult> Authenticate(
        SecureString password,
        SecureString twoFactorCode,
        TradejiniTwoFactorTypes twoFactorType,
        CancellationToken cancellationToken)
    {
        if (!AccessToken.IsEmpty())
        {
            return new()
            {
                AccessToken = AccessToken,
                ExpiresIn = 86400,
            };
        }

        var data = await Send(
            HttpMethod.Post,
            "api-gw/oauth/individual-token-v2",
            new Dictionary<string, string>
            {
                ["password"] =
                    password.ThrowIfEmpty(nameof(password)).UnSecure(),
                ["twoFa"] =
                    twoFactorCode
                        .ThrowIfEmpty(nameof(twoFactorCode))
                        .UnSecure(),
                ["twoFaTyp"] = twoFactorType.ToNative(),
            },
            AuthorizationModes.ApiKey,
            cancellationToken);
        AccessToken = data.GetText("access_token")
            .ThrowIfEmpty(nameof(TradejiniLoginResult.AccessToken));
        return new()
        {
            AccessToken = AccessToken,
            ExpiresIn = data.GetText("expires_in").To<int?>() ?? 86400,
        };
    }

    public async Task<TradejiniProfile> GetProfile(
        CancellationToken cancellationToken)
        => ToObject<TradejiniProfile>(
            await SendAuthenticated(
                HttpMethod.Get,
                "api/account/details",
                null,
                cancellationToken));

    public async Task<TradejiniInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            var result = new List<TradejiniInstrument>(100_000);
            foreach (var group in _masterGroups)
            {
                result.AddRange(
                    await DownloadInstrumentGroup(
                        group,
                        cancellationToken));
            }
            if (result.Count == 0)
            {
                throw new InvalidDataException(
                    "The Tradejini public symbol store did not contain any supported instruments.");
            }

            _instruments = [.. result];
            _instrumentsById = _instruments
                .GroupBy(
                    instrument => instrument.Id,
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

    public async Task<TradejiniInstrument> GetInstrument(
        string symbolId,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsById.TryGetValue(
            symbolId.ThrowIfEmpty(nameof(symbolId)),
            out var instrument)
                ? instrument
                : null;
    }

    public async Task<string> PlaceOrder(
        string symbolId,
        decimal quantity,
        Sides side,
        OrderTypes orderType,
        TradejiniProducts product,
        decimal limitPrice,
        decimal triggerPrice,
        TradejiniValidities validity,
        decimal disclosedQuantity,
        bool isAfterMarket,
        decimal marketProtection,
        string remarks,
        CancellationToken cancellationToken)
    {
        var form = CreateOrderForm(
            symbolId,
            quantity,
            side,
            orderType,
            product,
            limitPrice,
            triggerPrice,
            validity,
            disclosedQuantity,
            isAfterMarket,
            marketProtection,
            remarks);
        var data = await SendAuthenticated(
            HttpMethod.Post,
            "api/oms/place-order",
            form,
            cancellationToken);
        return data.GetText("orderId")
            .ThrowIfEmpty(nameof(TradejiniOrder.OrderId));
    }

    public async Task<string> ModifyOrder(
        string symbolId,
        string orderId,
        decimal quantity,
        Sides side,
        OrderTypes orderType,
        decimal limitPrice,
        decimal triggerPrice,
        TradejiniValidities validity,
        decimal disclosedQuantity,
        decimal marketProtection,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["symId"] = symbolId.ThrowIfEmpty(nameof(symbolId)),
            ["orderId"] = orderId.ThrowIfEmpty(nameof(orderId)),
            ["qty"] = Format(quantity),
            ["side"] = side.ToNative(),
            ["type"] = orderType.ToNative(limitPrice),
            ["validity"] = validity.ToNative(),
        };
        AddOrderOptionals(
            form,
            orderType,
            limitPrice,
            triggerPrice,
            disclosedQuantity,
            false,
            marketProtection,
            null);
        var data = await SendAuthenticated(
            HttpMethod.Put,
            "api/oms/modify-order",
            form,
            cancellationToken);
        return data.GetText("orderId").IsEmpty(orderId);
    }

    public async Task<string> CancelOrder(
        string orderId,
        CancellationToken cancellationToken)
    {
        orderId.ThrowIfEmpty(nameof(orderId));
        var path =
            $"api/oms/cancel-order?orderId={Uri.EscapeDataString(orderId)}";
        var data = await SendAuthenticated(
            HttpMethod.Delete,
            path,
            null,
            cancellationToken);
        return data.GetText("orderId").IsEmpty(orderId);
    }

    public async Task<TradejiniOrder[]> GetOrders(
        CancellationToken cancellationToken)
        => ParseArray<TradejiniOrder>(
            await SendAuthenticated(
                HttpMethod.Get,
                "api/oms/orders?symDetails=false",
                null,
                cancellationToken));

    public async Task<TradejiniTrade[]> GetTrades(
        CancellationToken cancellationToken)
        => ParseArray<TradejiniTrade>(
            await SendAuthenticated(
                HttpMethod.Get,
                "api/oms/trades?symDetails=false",
                null,
                cancellationToken));

    public async Task<TradejiniPosition[]> GetPositions(
        CancellationToken cancellationToken)
        => ParseArray<TradejiniPosition>(
            await SendAuthenticated(
                HttpMethod.Get,
                "api/oms/positions?symDetails=false",
                null,
                cancellationToken));

    public async Task<TradejiniHolding[]> GetHoldings(
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            "api/oms/holdings?symDetails=false",
            null,
            cancellationToken);
        return ParseArray<TradejiniHolding>(
            data.GetValueIgnoreCase("holdings") ?? data);
    }

    public async Task<TradejiniFund[]> GetFunds(
        CancellationToken cancellationToken)
        => ParseArray<TradejiniFund>(
            await SendAuthenticated(
                HttpMethod.Get,
                "api/oms/limits",
                null,
                cancellationToken));

    public async Task<TradejiniCandle[]> GetCandles(
        string symbolId,
        TimeSpan timeFrame,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        if (!TradejiniExtensions.TimeFrames.TryGetValue(
            timeFrame,
            out var interval))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeFrame),
                timeFrame,
                "Tradejini REST chart data supports one-minute candles.");
        }

        var end = NormalizeUtc(to ?? DateTime.UtcNow);
        var start = NormalizeUtc(from ?? end - TimeSpan.FromDays(7));
        if (start >= end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "The candle start time must be earlier than the end time.");
        }

        var path =
            "api/mkt-data/chart/interval-data" +
            $"?from={new DateTimeOffset(start).ToUnixTimeSeconds()}" +
            $"&to={new DateTimeOffset(end).ToUnixTimeSeconds()}" +
            $"&interval={Uri.EscapeDataString(interval)}" +
            $"&id={Uri.EscapeDataString(symbolId.ThrowIfEmpty(nameof(symbolId)))}";
        return ParseCandles(
            await SendAuthenticated(
                HttpMethod.Get,
                path,
                null,
                cancellationToken));
    }

    internal static JToken ParseResponse(
        string content,
        string operation)
    {
        if (content.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Tradejini returned an empty response for {operation}.");
        }

        JToken root;
        try
        {
            root = JToken.Parse(content);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                $"Tradejini returned invalid JSON for {operation}.",
                ex);
        }

        if (root is not JObject obj)
            return root;

        var status = obj.GetText("s", "status");
        if (status.IsEmpty())
            return obj;
        if (status.EqualsIgnoreCase("ok") ||
            status.EqualsIgnoreCase("success"))
        {
            return obj.GetValueIgnoreCase("d", "data") ??
                JValue.CreateNull();
        }
        if (status.EqualsIgnoreCase("no-data") ||
            status.EqualsIgnoreCase("no_data"))
            return JValue.CreateNull();

        var message = obj.GetText("msg", "message", "error")
            .IsEmpty(status)
            .IsEmpty("Unknown API error.");
        throw new InvalidOperationException(
            $"Tradejini {operation} error: {message}");
    }

    internal static T[] ParseArray<T>(JToken data)
    {
        if (data == null ||
            data.Type is JTokenType.Null or JTokenType.Undefined)
            return [];
        var serializer = JsonSerializer.Create(_jsonSettings);
        if (data is JArray array)
            return array.ToObject<T[]>(serializer) ?? [];
        return data.Type == JTokenType.Object
            ? [data.ToObject<T>(serializer)]
            : [];
    }

    internal static TradejiniCandle[] ParseCandles(JToken data)
    {
        var bars = data?.GetValueIgnoreCase("bars") ?? data;
        return ParseArray<TradejiniCandle>(bars)
            .Where(candle => candle.UnixTime > 0)
            .OrderBy(candle => candle.UnixTime)
            .ToArray();
    }

    internal static TradejiniInstrument ParseInstrument(
        string group,
        IReadOnlyDictionary<string, string> values)
    {
        group.ThrowIfEmpty(nameof(group));
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        string get(string name)
            => values.TryGetValue(name, out var value)
                ? value?.Trim()
                : null;

        var id = get("id");
        var exchangeToken = get("excToken");
        if (id.IsEmpty() || exchangeToken.IsEmpty())
            return null;

        var parts = id.Split('_');
        if (parts.Length < 3)
            return null;

        string instrument;
        string symbol;
        string series = null;
        string exchange;
        string expiry = null;
        string strike = null;
        string optionType = null;
        switch (group)
        {
            case "Securities":
                if (parts.Length < 4)
                    return null;
                instrument = parts[0];
                symbol = Join(parts, 1, parts.Length - 3);
                series = parts[^2];
                exchange = parts[^1];
                break;

            case "FutureContracts":
            case "CommodityFuture":
            case "CurrencyFuture":
                if (parts.Length < 4)
                    return null;
                instrument = parts[0];
                symbol = Join(parts, 1, parts.Length - 3);
                exchange = parts[^2];
                expiry = parts[^1];
                break;

            case "CommodityOptions":
            case "NSEOptions":
            case "CurrencyOptions":
            case "BSEOptions":
                if (parts.Length < 6)
                    return null;
                instrument = parts[0];
                symbol = Join(parts, 1, parts.Length - 5);
                exchange = parts[^4];
                expiry = parts[^3];
                strike = parts[^2];
                optionType = parts[^1];
                break;

            case "Spot":
                instrument = parts[0];
                symbol = get("symbol").IsEmpty(
                    Join(parts, 1, parts.Length - 2));
                exchange = parts[^1];
                break;

            case "Index":
                instrument = parts[0];
                symbol = get("symbol").IsEmpty(get("dispName"));
                exchange = parts[^1];
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(group),
                    group,
                    "Unknown Tradejini symbol-store group.");
        }

        exchange.ToBoardCode();
        var lotSize = get("lot").ToDecimal();
        return new()
        {
            Id = id,
            Isin = get("isin"),
            DisplayName = get("dispName").IsEmpty(symbol).IsEmpty(id),
            Description = get("desc").IsEmpty(get("dispName")).IsEmpty(symbol),
            ExchangeToken = exchangeToken,
            LotSize = lotSize > 0 ? lotSize : 1,
            TickSize = get("tick").ToDecimal(),
            Expiry = expiry.ToExpiryDate(),
            Strike = strike.ToDecimal(),
            OptionType = optionType,
            IsWeekly = get("weekly").ToBoolean(),
            Asset = get("asset"),
            Instrument = instrument,
            Symbol = symbol,
            Series = series,
            Exchange = exchange.ToUpperInvariant(),
            FreezeQuantity = get("freezeQty").ToDecimal(),
            UnderlyingId = get("undId"),
            TradingUnit = get("trdUnit"),
            AvailabilityFlag = get("availFlag"),
            LotMultiplier = get("lotMulti").ToDecimal(),
        };
    }

    internal static string BuildAuthorization(
        string apiKey,
        string accessToken)
        => accessToken.IsEmpty()
            ? apiKey.ThrowIfEmpty(nameof(apiKey))
            : $"{apiKey.ThrowIfEmpty(nameof(apiKey))}:{accessToken}";

    internal static IReadOnlyDictionary<string, string> CreateOrderForm(
        string symbolId,
        decimal quantity,
        Sides side,
        OrderTypes orderType,
        TradejiniProducts product,
        decimal limitPrice,
        decimal triggerPrice,
        TradejiniValidities validity,
        decimal disclosedQuantity,
        bool isAfterMarket,
        decimal marketProtection,
        string remarks)
    {
        var form = new Dictionary<string, string>
        {
            ["symId"] = symbolId.ThrowIfEmpty(nameof(symbolId)),
            ["qty"] = Format(quantity),
            ["side"] = side.ToNative(),
            ["type"] = orderType.ToNative(limitPrice),
            ["product"] = product.ToNative(),
            ["validity"] = validity.ToNative(),
        };
        AddOrderOptionals(
            form,
            orderType,
            limitPrice,
            triggerPrice,
            disclosedQuantity,
            isAfterMarket,
            marketProtection,
            remarks);
        return form;
    }

    private async Task<JToken> SendAuthenticated(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        AccessToken.ThrowIfEmpty(nameof(AccessToken));
        return await Send(
            method,
            path,
            form,
            AuthorizationModes.ApiKeyAndToken,
            cancellationToken);
    }

    private async Task<JToken> Send(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> form,
        AuthorizationModes authorization,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await WaitRateLimit(cancellationToken);
            using var request = new HttpRequestMessage(method, path);
            if (form != null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }
            if (authorization != AuthorizationModes.None)
            {
                var value = BuildAuthorization(
                    _apiKey,
                    authorization == AuthorizationModes.ApiKeyAndToken
                        ? AccessToken
                        : null);
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", value);
            }

            this.AddVerboseLog(
                "Tradejini {0} {1}.",
                method,
                SafePath(path));
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException error) when (attempt < 3)
            {
                lastError = error;
                await DelayRetry(null, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                    return ParseResponse(content, SafePath(path));

                if (attempt < 3 && IsTransient(response.StatusCode))
                {
                    lastError = new HttpRequestException(
                        $"Tradejini {SafePath(path)} returned HTTP {(int)response.StatusCode}.",
                        null,
                        response.StatusCode);
                    await DelayRetry(
                        response,
                        attempt,
                        cancellationToken);
                    continue;
                }

                try
                {
                    _ = ParseResponse(content, SafePath(path));
                }
                catch (Exception error)
                {
                    throw new HttpRequestException(
                        $"Tradejini {SafePath(path)} returned HTTP {(int)response.StatusCode}: {error.Message}",
                        error,
                        response.StatusCode);
                }
                throw new HttpRequestException(
                    $"Tradejini {SafePath(path)} returned HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
            }
        }

        throw lastError ??
            new HttpRequestException(
                $"Tradejini {SafePath(path)} request failed.");
    }

    private async Task<TradejiniInstrument[]> DownloadInstrumentGroup(
        string group,
        CancellationToken cancellationToken)
    {
        await WaitRateLimit(cancellationToken);
        var path =
            $"api/mkt-data/scrips/symbol-store/{Uri.EscapeDataString(group)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain"));
        this.AddVerboseLog(
            "Tradejini GET public symbol group {0}.",
            group);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        return await ParseInstrumentCsv(
            group,
            stream,
            cancellationToken);
    }

    internal static async Task<TradejiniInstrument[]> ParseInstrumentCsv(
        string group,
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            true,
            1 << 16,
            true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine.IsEmpty())
            return [];
        var headers = headerLine
            .Split(',')
            .Select(header => header.Trim().TrimStart('\uFEFF'))
            .ToArray();
        if (headers.Length == 0 || headers.Any(header => header.IsEmpty()))
        {
            throw new InvalidDataException(
                $"Tradejini {group} symbol-store header is invalid.");
        }

        var csv = new FastCsvReader(reader, StringHelper.N)
        {
            ColumnSeparator = ',',
        };
        var result = new List<TradejiniInstrument>();
        while (await csv.NextLineAsync(cancellationToken))
        {
            var values = new Dictionary<string, string>(
                headers.Length,
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
                values[headers[index]] = csv.ReadString()?.Trim();
            try
            {
                var instrument = ParseInstrument(group, values);
                if (instrument != null)
                    result.Add(instrument);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A new exchange may appear in the public master before
                // StockSharp has a corresponding board.
            }
        }
        return [.. result];
    }

    private static T ToObject<T>(JToken data)
    {
        if (data == null ||
            data.Type is JTokenType.Null or JTokenType.Undefined)
            return default;
        return data.ToObject<T>(JsonSerializer.Create(_jsonSettings));
    }

    private static void AddOrderOptionals(
        IDictionary<string, string> form,
        OrderTypes orderType,
        decimal limitPrice,
        decimal triggerPrice,
        decimal disclosedQuantity,
        bool isAfterMarket,
        decimal marketProtection,
        string remarks)
    {
        if (orderType == OrderTypes.Limit ||
            orderType == OrderTypes.Conditional && limitPrice > 0)
            form["limitPrice"] = Format(limitPrice);
        if (orderType == OrderTypes.Conditional)
            form["trigPrice"] = Format(triggerPrice);
        if (disclosedQuantity > 0)
            form["discQty"] = Format(disclosedQuantity);
        if (isAfterMarket)
            form["amo"] = "true";
        if (marketProtection > 0)
        {
            if (orderType != OrderTypes.Market)
            {
                throw new ArgumentException(
                    "Tradejini market protection is available only for market orders.",
                    nameof(marketProtection));
            }
            form["mktProt"] = Format(marketProtection);
        }
        if (!remarks.IsEmpty())
            form["remarks"] = remarks;
    }

    private async Task WaitRateLimit(
        CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var delay = TimeSpan.FromMilliseconds(100) -
                (DateTime.UtcNow - _lastRequestAt);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _lastRequestAt = DateTime.UtcNow;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static async Task DelayRetry(
        HttpResponseMessage response,
        int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta;
        if (delay == null &&
            response?.Headers.RetryAfter?.Date is DateTimeOffset date)
            delay = date - DateTimeOffset.UtcNow;
        if (delay == null || delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromMilliseconds(
                Math.Min(5000, 250 * (1 << Math.Min(attempt - 1, 4))));
        }
        await Task.Delay(delay.Value, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500;

    private static string Join(
        string[] parts,
        int start,
        int count)
        => count <= 0
            ? null
            : string.Join("_", parts, start, count);

    private static string Format(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string SafePath(string path)
        => path?.Split('?')[0];

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private enum AuthorizationModes
    {
        None,
        ApiKey,
        ApiKeyAndToken,
    }
}
