namespace StockSharp.Bigul.Native;

sealed class BigulRestClient : BaseLogReceiver
{
    private static readonly IReadOnlyDictionary<string, string> _masterFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nse_cm.csv"] = "nse_cm",
            ["bse_cm.csv"] = "bse_cm",
            ["nse_fo.csv"] = "nse_fo",
            ["bse_fo.csv"] = "bse_fo",
            ["cde_fo.csv"] = "cde_fo",
            ["mcx.csv"] = "mcx_fo",
        };

    private static readonly IReadOnlyDictionary<string, int> _masterColumnCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["nse_cm"] = 48,
            ["bse_cm"] = 48,
            ["nse_fo"] = 33,
            ["bse_fo"] = 33,
            ["cde_fo"] = 34,
            ["mcx_fo"] = 52,
        };

    private static readonly Regex _expiryRegex = new(
        @"\b(?<day>\d{1,2})(?<month>JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)(?<year>\d{2}|\d{4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly string _clientCode;
    private readonly string _source;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly Uri _masterAddress;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private BigulInstrument[] _instruments;
    private IReadOnlyDictionary<string, BigulInstrument> _instrumentsByKey;
    private IReadOnlyDictionary<string, BigulInstrument> _instrumentsBySymbol;

    public BigulRestClient(
        string clientCode,
        SecureString apiKey,
        SecureString apiSecret,
        SecureString accessToken,
        string source,
        Uri address,
        Uri masterAddress)
    {
        _clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
        _apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret)).UnSecure();
        _source = source.ThrowIfEmpty(nameof(source));
        AccessToken = accessToken?.UnSecure();
        _masterAddress = masterAddress ?? throw new ArgumentNullException(nameof(masterAddress));
        _httpClient = new()
        {
            BaseAddress = address ?? throw new ArgumentNullException(nameof(address)),
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bigul/1.0");
    }

    public override string Name => nameof(Bigul) + "_" + nameof(BigulRestClient);

    public string AccessToken { get; private set; }

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<BigulLoginResult> Authenticate(
        SecureString oneTimePassword,
        CancellationToken cancellationToken)
    {
        if (!AccessToken.IsEmpty())
        {
            return new()
            {
                ClientCode = _clientCode,
                AccessToken = AccessToken,
            };
        }

        oneTimePassword.ThrowIfEmpty(nameof(oneTimePassword));
        var data = await SendCore(
            "auth/connect/login",
            new JObject
            {
                ["source"] = _source,
                ["clientCode"] = _clientCode,
                ["totp"] = oneTimePassword.UnSecure(),
            },
            false,
            cancellationToken);
        AccessToken = data.GetText("token")
            .ThrowIfEmpty(nameof(BigulLoginResult.AccessToken));
        return new()
        {
            ClientCode = _clientCode,
            AccessToken = AccessToken,
        };
    }

    public async Task<BigulInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            _instruments = await DownloadInstruments(cancellationToken);
            _instrumentsByKey = _instruments
                .GroupBy(
                    instrument => instrument.Segment.ToInstrumentKey(instrument.Token),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            _instrumentsBySymbol = _instruments
                .Where(instrument => !instrument.TradingSymbol.IsEmpty())
                .GroupBy(
                    instrument => ToSymbolKey(instrument.Segment, instrument.TradingSymbol),
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

    public async Task<BigulInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument)
            ? instrument
            : null;
    }

    public async Task<BigulInstrument> FindInstrument(
        string segment,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsBySymbol.TryGetValue(
            ToSymbolKey(segment, tradingSymbol),
            out var instrument)
                ? instrument
                : null;
    }

    public async Task<string> PlaceOrder(
        BigulPlaceOrderRequest order,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            "order/vr-place",
            JObject.FromObject(order, JsonSerializer.Create(_jsonSettings)),
            cancellationToken);
        return data.GetText("nOrdNo")
            .ThrowIfEmpty(nameof(BigulOrder.OrderId));
    }

    public async Task<string> ModifyOrder(
        BigulModifyOrderRequest order,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            "order/vr-modify",
            JObject.FromObject(order, JsonSerializer.Create(_jsonSettings)),
            cancellationToken);
        return data.GetText("nOrdNo").IsEmpty(order.OrderId);
    }

    public async Task<string> CancelOrder(
        string orderId,
        bool afterMarket,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            "order/cancel",
            new JObject
            {
                ["am"] = afterMarket ? "YES" : "NO",
                ["on"] = orderId.ThrowIfEmpty(nameof(orderId)),
                ["ts"] = afterMarket
                    ? tradingSymbol.ThrowIfEmpty(nameof(tradingSymbol))
                    : tradingSymbol,
            },
            cancellationToken);
        return data.GetText("result").IsEmpty(orderId);
    }

    public async Task<BigulOrder[]> GetOrders(CancellationToken cancellationToken)
        => ParseArray<BigulOrder>(
            await SendAuthenticated("order/order-book", new(), cancellationToken));

    public async Task<BigulTrade[]> GetTrades(CancellationToken cancellationToken)
        => ParseArray<BigulTrade>(
            await SendAuthenticated("order/trade-book", new(), cancellationToken));

    public async Task<BigulPosition[]> GetPositions(CancellationToken cancellationToken)
        => ParseArray<BigulPosition>(
            await SendAuthenticated("order/get-position", new(), cancellationToken));

    public async Task<BigulHolding[]> GetHoldings(CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            "order/get-holdings",
            new JObject { ["prod"] = "CNC" },
            cancellationToken);
        if (data is JArray)
            return ParseArray<BigulHolding>(data);
        return data.ToObject<BigulHoldingEnvelope>(
            JsonSerializer.Create(_jsonSettings))?.Holdings ?? [];
    }

    public async Task<BigulLimits> GetLimits(CancellationToken cancellationToken)
        => (await SendAuthenticated(
                "order/user-limits",
                new JObject
                {
                    ["seg"] = "ALL",
                    ["exch"] = "ALL",
                    ["prod"] = "ALL",
                },
                cancellationToken))
            .ToObject<BigulLimits>(JsonSerializer.Create(_jsonSettings)) ?? new();

    internal static JToken ParseResponse(string content, string operation)
    {
        if (content.IsEmpty())
            throw new InvalidOperationException(
                $"Bigul returned an empty response for {operation}.");

        JObject root;
        try
        {
            root = JObject.Parse(content);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                $"Bigul returned invalid JSON for {operation}.",
                ex);
        }

        var statusToken = root.GetValueIgnoreCase("status");
        var success = statusToken?.Type == JTokenType.Boolean
            ? statusToken.Value<bool>()
            : statusToken?.ToString().EqualsIgnoreCase("true") == true ||
                statusToken?.ToString().EqualsIgnoreCase("success") == true ||
                statusToken?.ToString().EqualsIgnoreCase("ok") == true;
        if (success)
            return root.GetValueIgnoreCase("data") ?? root;

        var error = root.GetValueIgnoreCase("error");
        var message = error?.GetText("message", "msg", "error")
            .IsEmpty(error is JValue ? error.ToString() : null)
            .IsEmpty(root.GetText("message"))
            .IsEmpty(statusToken?.ToString())
            .IsEmpty("Unknown API error.");
        throw new InvalidOperationException($"Bigul {operation} error: {message}");
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

    internal static BigulInstrument ParseInstrument(
        string segment,
        IReadOnlyDictionary<string, string> values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        string get(string name)
            => values.TryGetValue(name, out var value) ? value?.Trim() : null;

        var token = get("ScripCode");
        var tradingSymbol = get("TradingSymbol");
        if (token.IsEmpty() || tradingSymbol.IsEmpty())
            return null;

        var rawTick = get(segment.EqualsIgnoreCase("mcx_fo")
            ? "McxTickSize"
            : "TickSize").ToDecimal();
        if (rawTick <= 0)
            rawTick = get("TickSize").ToDecimal();

        var series = get("SERIES");
        var optionType = get("OPTION_TYPE");
        var instrument = new BigulInstrument
        {
            Segment = segment,
            Token = token,
            Symbol = get("Name").IsEmpty(tradingSymbol),
            Description = get("Desc").IsEmpty(get("Name")),
            TradingSymbol = tradingSymbol,
            Series = series,
            Isin = get("ISIN"),
            TickSize = NormalizeTickSize(segment, rawTick),
            LotSize = PositiveOrDefault(
                get("LOTSIZE").ToDecimal(),
                PositiveOrDefault(
                    get("MinimumLotQuantity").ToDecimal(),
                    PositiveOrDefault(get("MinimumLotQty").ToDecimal(), 1m))),
            Expiry = ParseExpiry(
                get("Desc"),
                get("EXPIRY_DATE"),
                get("ExpiryDate")),
            StrikePrice = get("StrikePrice").ToDecimal(),
            OptionType = optionType,
            IsFuture = ParseBoolean(get("IsFuture")) ||
                series?.Contains("FUT", StringComparison.OrdinalIgnoreCase) == true,
            IsOption = ParseBoolean(get("IsOption")) ||
                optionType?.ToUpperInvariant() is "CE" or "PE" ||
                series?.Contains("OPT", StringComparison.OrdinalIgnoreCase) == true,
        };
        instrument.Segment.ToBoardCode();
        return instrument;
    }

    private async Task<JToken> SendAuthenticated(
        string path,
        JObject body,
        CancellationToken cancellationToken)
    {
        AccessToken.ThrowIfEmpty(nameof(AccessToken));
        body ??= new();
        body["source"] = _source;
        body["clientCode"] = _clientCode;
        return await SendCore(path, body, true, cancellationToken);
    }

    private async Task<JToken> SendCore(
        string path,
        JObject body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        request.Headers.TryAddWithoutValidation("x-api-secret", _apiSecret);
        if (authenticated)
            request.Headers.Authorization = new("Bearer", AccessToken);

        this.AddVerboseLog("Bigul POST {0}.", path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return ParseResponse(content, path);
        }
        catch (Exception ex) when (
            !response.IsSuccessStatusCode &&
            ex is InvalidDataException or InvalidOperationException)
        {
            throw new HttpRequestException(
                $"Bigul {path} returned HTTP {(int)response.StatusCode}: {ex.Message}",
                ex,
                response.StatusCode);
        }
    }

    private async Task<BigulInstrument[]> DownloadInstruments(
        CancellationToken cancellationToken)
    {
        this.AddVerboseLog("Bigul GET {0}.", _masterAddress);
        using var response = await _httpClient.GetAsync(
            _masterAddress,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var stream = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
        var instruments = new List<BigulInstrument>();

        foreach (var entry in archive.Entries)
        {
            if (!_masterFiles.TryGetValue(
                Path.GetFileName(entry.FullName),
                out var segment))
                continue;
            await using var entryStream = entry.Open();
            instruments.AddRange(
                await ParseInstrumentCsv(segment, entryStream, cancellationToken));
        }

        if (instruments.Count == 0)
            throw new InvalidDataException(
                "The Bigul master archive did not contain any supported CSV files.");
        return [.. instruments];
    }

    private static async Task<BigulInstrument[]> ParseInstrumentCsv(
        string segment,
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16, true);
        var csv = new FastCsvReader(reader, StringHelper.N)
        {
            ColumnSeparator = ',',
        };
        if (!await csv.NextLineAsync(cancellationToken))
            return [];

        var columnCount = _masterColumnCounts[segment];
        var headers = new string[columnCount];
        for (var index = 0; index < headers.Length; index++)
            headers[index] = csv.ReadString()?.Trim().TrimStart('\uFEFF');

        var result = new List<BigulInstrument>();
        while (await csv.NextLineAsync(cancellationToken))
        {
            var values = new Dictionary<string, string>(
                headers.Length,
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
                values[headers[index]] = csv.ReadString()?.Trim();
            var instrument = ParseInstrument(segment, values);
            if (instrument != null)
                result.Add(instrument);
        }
        return [.. result];
    }

    private static decimal NormalizeTickSize(string segment, decimal rawTick)
    {
        if (rawTick <= 0)
            return 0m;
        if (rawTick < 1m)
            return rawTick;
        return segment.EqualsIgnoreCase("cde_fo")
            ? rawTick / 10_000_000m
            : rawTick / 100m;
    }

    private static DateTime? ParseExpiry(
        string description,
        params string[] values)
    {
        var match = _expiryRegex.Match(description ?? string.Empty);
        if (match.Success)
        {
            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            if (year < 100)
                year += 2000;
            if (DateTime.TryParseExact(
                $"{match.Groups["day"].Value}{match.Groups["month"].Value}{year}",
                "dMMMyyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var contractDate))
                return contractDate.ToUtcFromIndia();
        }

        foreach (var value in values)
        {
            if (value.IsEmpty())
                continue;
            if (DateTime.TryParseExact(
                value.Trim(),
                ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd-MMM-yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var date))
                return date.ToUtcFromIndia();
            if (decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var seconds) &&
                seconds > 100_000_000 &&
                seconds <= long.MaxValue)
                return decimal.ToInt64(seconds).FromUnixSeconds();
        }
        return null;
    }

    private static bool ParseBoolean(string value)
        => value.EqualsIgnoreCase("1") ||
            value.EqualsIgnoreCase("Y") ||
            value.EqualsIgnoreCase("YES") ||
            value.EqualsIgnoreCase("TRUE");

    private static decimal PositiveOrDefault(decimal value, decimal defaultValue)
        => value > 0 ? value : defaultValue;

    private static string ToSymbolKey(string segment, string tradingSymbol)
        => $"{segment?.ToLowerInvariant()}|{tradingSymbol?.ToUpperInvariant()}";
}
