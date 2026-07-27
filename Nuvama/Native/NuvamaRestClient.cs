namespace StockSharp.Nuvama.Native;

sealed class NuvamaRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _instrumentAddress;
    private readonly Uri _ipAddressService;
    private readonly TimeSpan _minimumRequestInterval;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private readonly SemaphoreSlim _rateLock = new(1, 1);
    private NuvamaInstrument[] _instruments;
    private IReadOnlyDictionary<string, NuvamaInstrument> _instrumentsByKey;
    private IReadOnlyDictionary<string, NuvamaInstrument> _instrumentsBySymbol;
    private DateTime _lastRequest;
    private string _source;
    private string _vendorToken;
    private string _authorization;
    private string _appIdKey;
    private string _publicIpAddress;
    private string _accountId;
    private string _userId;
    private string _accountType;
    private string _employeeOrDependent;

    public NuvamaRestClient(
        Uri restAddress,
        Uri instrumentAddress,
        Uri ipAddressService,
        HttpMessageHandler handler = null,
        TimeSpan? minimumRequestInterval = null)
    {
        _httpClient = handler == null ? new() : new(handler);
        _httpClient.BaseAddress =
            restAddress ?? throw new ArgumentNullException(nameof(restAddress));
        _instrumentAddress = instrumentAddress ??
            throw new ArgumentNullException(nameof(instrumentAddress));
        _ipAddressService = ipAddressService ??
            throw new ArgumentNullException(nameof(ipAddressService));
        _minimumRequestInterval =
            minimumRequestInterval ?? TimeSpan.FromMilliseconds(105);
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Nuvama/1.0");
    }

    public override string Name => nameof(Nuvama) + "_" +
        nameof(NuvamaRestClient);

    public string AppIdKey => _appIdKey;

    public string Authorization => _authorization;

    public event Action<string> AppIdKeyChanged;

    public event Action<string> AuthorizationChanged;

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        _rateLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<NuvamaLoginResult> Authenticate(
        SecureString key,
        SecureString secret,
        SecureString requestId,
        SecureString vendorToken,
        SecureString authorization,
        SecureString appIdKey,
        string publicIpAddress,
        string accountId,
        string userId,
        string accountType,
        string employeeOrDependent,
        CancellationToken cancellationToken)
    {
        _source = key.ThrowIfEmpty(nameof(key)).UnSecure();
        _appIdKey = appIdKey.ThrowIfEmpty(nameof(appIdKey)).UnSecure();
        _vendorToken = vendorToken.IsEmpty()
            ? null
            : vendorToken.UnSecure();
        _authorization = authorization.IsEmpty()
            ? null
            : authorization.UnSecure();
        _accountId = accountId;
        _userId = userId;
        _accountType = accountType.IsEmpty("EQ").ToUpperInvariant();
        _employeeOrDependent = employeeOrDependent;
        _publicIpAddress = publicIpAddress;

        if (_publicIpAddress.IsEmpty())
            _publicIpAddress = await DiscoverPublicIp(cancellationToken);
        if (!IPAddress.TryParse(_publicIpAddress, out _))
        {
            throw new InvalidOperationException(
                $"Nuvama requires a valid registered static public IP, but '{_publicIpAddress}' is not an IP address.");
        }

        if (_vendorToken.IsEmpty())
        {
            var apiSecret = secret.ThrowIfEmpty(nameof(secret)).UnSecure();
            var login = await Send(
                $"edelmw-login/login/accounts/loginvendor/{Escape(_source)}",
                HttpMethod.Post,
                new { pwd = apiSecret },
                true,
                false,
                cancellationToken);
            _vendorToken = NuvamaExtensions.FindString(login, "msg")
                .ThrowIfEmpty("Vendor session");
        }

        if (_authorization.IsEmpty())
        {
            var id = requestId.ThrowIfEmpty(nameof(requestId)).UnSecure();
            var login = await Send(
                "edelmw-login/login/accounts/logindata",
                HttpMethod.Post,
                new { reqId = id },
                false,
                false,
                cancellationToken);
            var data = GetData(login);
            _authorization = NuvamaExtensions.FindString(data, "auth")
                .ThrowIfEmpty("Authorization");
            var loginData =
                NuvamaExtensions.FindToken(data, "lgnData") ??
                throw new InvalidDataException(
                    "Nuvama logindata returned no lgnData object.");
            var accounts = NuvamaExtensions.FindToken(loginData, "accs");
            _accountId = NuvamaExtensions.FindString(
                    accounts,
                    "eqAccID",
                    "coAccID")
                .IsEmpty(_accountId);
            _userId = NuvamaExtensions.FindString(accounts, "uid")
                .IsEmpty(_userId)
                .IsEmpty(_accountId);
            _accountType = NuvamaExtensions.FindString(loginData, "accTyp")
                .IsEmpty(_accountType)
                .IsEmpty("EQ")
                .ToUpperInvariant();
            _employeeOrDependent = NuvamaExtensions.FindString(
                    accounts,
                    "empOrDependent")
                .IsEmpty(_employeeOrDependent);
        }

        _accountId.ThrowIfEmpty(nameof(accountId));
        _userId = _userId.IsEmpty(_accountId);

        return new()
        {
            VendorToken = _vendorToken,
            Authorization = _authorization,
            AppIdKey = _appIdKey,
            AccountId = _accountId,
            UserId = _userId,
            AccountType = _accountType,
            PublicIpAddress = _publicIpAddress,
            EmployeeOrDependent = _employeeOrDependent,
        };
    }

    public async Task<NuvamaInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                _instrumentAddress);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(
                cancellationToken);
            _instruments = ParseInstrumentArchive(bytes);
            _instrumentsByKey = _instruments.ToDictionary(
                instrument => instrument.Exchange.ToInstrumentKey(
                    instrument.ExchangeToken),
                StringComparer.OrdinalIgnoreCase);
            _instrumentsBySymbol = _instruments
                .Where(instrument =>
                    !instrument.Exchange.IsEmpty() &&
                    !instrument.TradingSymbol.IsEmpty())
                .GroupBy(
                    instrument => ToSymbolKey(
                        instrument.Exchange,
                        instrument.TradingSymbol),
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

    public async Task<NuvamaInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(
            instrumentKey,
            out var instrument)
            ? instrument
            : null;
    }

    public async Task<NuvamaInstrument> FindInstrument(
        string exchange,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsBySymbol.TryGetValue(
            ToSymbolKey(exchange, tradingSymbol),
            out var instrument)
            ? instrument
            : null;
    }

    public async Task<string> PlaceOrder(
        NuvamaOrderRequest order,
        CancellationToken cancellationToken)
    {
        var response = await Send(
            $"edelmw-eq/eq/trade/placetrade/v1/{Escape(_accountId)}",
            HttpMethod.Post,
            order ?? throw new ArgumentNullException(nameof(order)),
            true,
            false,
            cancellationToken);
        var data = GetData(response);
        var orderId = NuvamaExtensions.FindString(
            data,
            "oid",
            "ordID",
            "nstOID");

        if (orderId.IsEmpty() &&
            NuvamaExtensions.FindToken(data, "ord") is JArray orders)
        {
            orderId = orders
                .Select(item => NuvamaExtensions.FindString(
                    item,
                    "oid",
                    "ordID",
                    "nstOID"))
                .FirstOrDefault(id => !id.IsEmpty());
        }

        return orderId.ThrowIfEmpty("Order ID");
    }

    public async Task ModifyOrder(
        NuvamaModifyOrderRequest order,
        CancellationToken cancellationToken)
        => await Send(
            $"edelmw-eq/eq/trade/modifytrade/v1/{Escape(_accountId)}",
            HttpMethod.Put,
            order ?? throw new ArgumentNullException(nameof(order)),
            true,
            false,
            cancellationToken);

    public async Task CancelOrder(
        NuvamaCancelOrderRequest order,
        CancellationToken cancellationToken)
        => await Send(
            $"edelmw-eq/eq/trade/canceltrade/v1/{Escape(_accountId)}",
            HttpMethod.Put,
            order ?? throw new ArgumentNullException(nameof(order)),
            true,
            false,
            cancellationToken);

    public async Task<NuvamaOrder[]> GetOrders(
        CancellationToken cancellationToken)
        => ToArray<NuvamaOrder>(
            await Send(
                $"edelmw-eq/eq/order/book/{Escape(_accountId)}/v1",
                HttpMethod.Get,
                null,
                true,
                true,
                cancellationToken),
            "ord");

    public async Task<NuvamaTrade[]> GetTrades(
        CancellationToken cancellationToken)
        => ToArray<NuvamaTrade>(
            await Send(
                $"edelmw-eq/eq/tradebook/v1/{Escape(_accountId)}",
                HttpMethod.Get,
                null,
                true,
                true,
                cancellationToken),
            "trade");

    public async Task<NuvamaPosition[]> GetPositions(
        CancellationToken cancellationToken)
        => ToArray<NuvamaPosition>(
            await Send(
                $"edelmw-eq/eq/positions/net/{Escape(_accountId)}",
                HttpMethod.Get,
                null,
                true,
                true,
                cancellationToken),
            "pos");

    public async Task<NuvamaHolding[]> GetHoldings(
        CancellationToken cancellationToken)
        => ToArray<NuvamaHolding>(
            await Send(
                $"edelmw-eq/eq/holdings/v1/rmsholdings/{Escape(_accountId)}",
                HttpMethod.Get,
                null,
                true,
                true,
                cancellationToken),
            "rmsHdg");

    public async Task<NuvamaLimits> GetLimits(
        CancellationToken cancellationToken)
    {
        var response = await Send(
            $"edelmw-eq/eq/limits/rmssublimits/{Escape(_accountId)}",
            HttpMethod.Get,
            null,
            true,
            false,
            cancellationToken);
        return GetData(response).ToObject<NuvamaLimits>() ?? new();
    }

    public async Task<NuvamaDepth> GetMarketDepth(
        string streamingSymbol,
        CancellationToken cancellationToken)
    {
        var response = await Send(
            $"edelmw-content/content/quote/scrip/{Escape(streamingSymbol)}",
            HttpMethod.Get,
            null,
            false,
            true,
            cancellationToken);
        if (response == null)
            return null;
        var data = GetData(response);
        data = NuvamaExtensions.FindToken(data, "mkd") ?? data;
        return data.ToObject<NuvamaDepth>();
    }

    public async Task<NuvamaCandle[]> GetCandles(
        NuvamaInstrument instrument,
        TimeSpan timeFrame,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        var interval = timeFrame.ToChartInterval();
        var till = (to ?? DateTime.UtcNow).ToUniversalTime()
            .AddHours(5.5)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        object body;

        if (timeFrame == TimeSpan.FromDays(1) &&
            from != null &&
            to != null)
        {
            body = new
            {
                conti = false,
                chTyp = "Interval",
                frmDt = from.Value.ToUniversalTime()
                    .AddHours(5.5)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                toDt = till,
                prdTyp = "CUST",
            };
        }
        else
        {
            body = new
            {
                conti = false,
                chTyp = "Interval",
                ltt = till,
            };
        }

        var response = await Send(
            $"edelmw-content/content/charts/v2/main/{interval}/" +
            $"{Escape(instrument.Exchange)}/{Escape(instrument.AssetType)}/" +
            Escape(instrument.ExchangeToken),
            HttpMethod.Post,
            body,
            true,
            true,
            cancellationToken);
        return ParseCandles(response);
    }

    internal static NuvamaInstrument[] ParseInstrumentArchive(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidDataException("Nuvama instrument archive is empty.");

        using var stream = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            false);
        var entry = archive.Entries.FirstOrDefault(
            item => item.Name.Equals(
                "instruments.csv",
                StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException(
                "Nuvama instrument archive does not contain instruments.csv.");
        if (entry.Length <= 0 || entry.Length > 128 * 1024 * 1024)
        {
            throw new InvalidDataException(
                $"Nuvama instruments.csv has invalid length {entry.Length}.");
        }

        using var reader = new StreamReader(
            entry.Open(),
            Encoding.UTF8,
            true);
        return ParseInstrumentCsv(reader.ReadToEnd());
    }

    internal static NuvamaInstrument[] ParseInstrumentCsv(string csv)
    {
        var records = ParseCsvRecords(csv).ToArray();
        if (records.Length == 0)
            throw new InvalidDataException("Nuvama instrument CSV is empty.");

        var headers = records[0]
            .Select((name, index) => (name, index))
            .ToDictionary(
                item => item.name.Trim(),
                item => item.index,
                StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[]
        {
            "exchangetoken",
            "tradingsymbol",
            "assettype",
            "exchange",
        })
        {
            if (!headers.ContainsKey(required))
            {
                throw new InvalidDataException(
                    $"Nuvama instrument CSV is missing '{required}'.");
            }
        }

        string Get(string[] row, string name)
            => headers.TryGetValue(name, out var index) && index < row.Length
                ? row[index].Trim()
                : null;

        return
        [
            ..
            records
                .Skip(1)
                .Where(row => row.Length > 1)
                .Select(row => new NuvamaInstrument
                {
                    ExchangeToken = Get(row, "exchangetoken"),
                    TradingSymbol = Get(row, "tradingsymbol"),
                    SymbolName = Get(row, "symbolname"),
                    Description = Get(row, "description"),
                    Expiry = Get(row, "expiry"),
                    StrikePrice = Get(row, "strikeprice"),
                    TickSize = Get(row, "ticksize"),
                    LotSize = Get(row, "lotsize"),
                    OptionType = Get(row, "optiontype"),
                    Series = Get(row, "series"),
                    AssetType = Get(row, "assettype"),
                    Exchange = Get(row, "exchange")?.ToUpperInvariant(),
                    Isin = Get(row, "isin"),
                    QuantityUnits = Get(row, "qtyunits"),
                    PriceUnits = Get(row, "prcunits"),
                    PriceQuotation = Get(row, "prcqtn"),
                    Multiplier = Get(row, "multiplier"),
                    AsmGsmFlag = Get(row, "asmgsmflag"),
                    AsmGsmMessage = Get(row, "asmgsmmsg"),
                })
                .Where(instrument =>
                    !instrument.ExchangeToken.IsEmpty() &&
                    !instrument.Exchange.IsEmpty() &&
                    !instrument.AssetType.IsEmpty())
                .GroupBy(
                    instrument => instrument.Exchange.ToInstrumentKey(
                        instrument.ExchangeToken),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
        ];
    }

    internal static IEnumerable<string[]> ParseCsvRecords(string csv)
    {
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));

        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (quoted &&
                    index + 1 < csv.Length &&
                    csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }

            if (!quoted && character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!quoted && character is '\r' or '\n')
            {
                if (character == '\r' &&
                    index + 1 < csv.Length &&
                    csv[index + 1] == '\n')
                    index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => !value.IsEmpty()))
                    yield return [.. row];
                row.Clear();
                continue;
            }

            field.Append(character);
        }

        if (quoted)
            throw new InvalidDataException(
                "Nuvama instrument CSV contains an unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(value => !value.IsEmpty()))
                yield return [.. row];
        }
    }

    internal static NuvamaCandle[] ParseCandles(JToken response)
    {
        if (response == null)
            return [];

        var data = GetData(response);
        if (NuvamaExtensions.FindToken(data, "pltPnts") is JObject points)
        {
            var times = NuvamaExtensions.FindToken(points, "ltt") as JArray;
            var opens = NuvamaExtensions.FindToken(points, "open") as JArray;
            var highs = NuvamaExtensions.FindToken(points, "high") as JArray;
            var lows = NuvamaExtensions.FindToken(points, "low") as JArray;
            var closes = NuvamaExtensions.FindToken(points, "close") as JArray;
            var volumes = NuvamaExtensions.FindToken(points, "vol") as JArray;
            var length = new[]
            {
                times?.Count ?? 0,
                opens?.Count ?? 0,
                highs?.Count ?? 0,
                lows?.Count ?? 0,
                closes?.Count ?? 0,
                volumes?.Count ?? 0,
            }.Min();
            var result = new List<NuvamaCandle>(length);
            for (var index = 0; index < length; index++)
            {
                if (times[index].Value<string>().ToNuvamaTime()
                    is not DateTime time)
                    continue;
                result.Add(new()
                {
                    Time = time,
                    Open = opens[index].Value<string>().ToDecimal(),
                    High = highs[index].Value<string>().ToDecimal(),
                    Low = lows[index].Value<string>().ToDecimal(),
                    Close = closes[index].Value<string>().ToDecimal(),
                    Volume = volumes[index].Value<string>().ToDecimal(),
                });
            }
            return [.. result];
        }

        if (data is not JArray rows)
            data = NuvamaExtensions.FindToken(data, "data");
        if (data is not JArray values)
            return [];

        return
        [
            ..
            values
                .OfType<JArray>()
                .Where(row => row.Count >= 6)
                .Select(row => new NuvamaCandle
                {
                    Time = row[0].Value<string>().ToNuvamaTime() ?? default,
                    Open = row[1].Value<string>().ToDecimal(),
                    High = row[2].Value<string>().ToDecimal(),
                    Low = row[3].Value<string>().ToDecimal(),
                    Close = row[4].Value<string>().ToDecimal(),
                    Volume = row[5].Value<string>().ToDecimal(),
                })
                .Where(candle => candle.Time != default)
        ];
    }

    internal static JToken ParseResponse(
        string operation,
        string json,
        bool allowNoData = false)
    {
        if (json.IsEmpty())
            return new JObject();

        JToken root;
        try
        {
            root = JToken.Parse(json);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Nuvama {operation} returned invalid JSON.",
                error);
        }

        if (NuvamaExtensions.FindToken(root, "resp") is JToken response)
            root = response;

        if (NuvamaExtensions.FindToken(root, "error") is JToken apiError)
        {
            var message = NuvamaExtensions.FindString(
                    apiError,
                    "errMsg",
                    "message",
                    "msg")
                .IsEmpty($"Nuvama {operation} failed.");
            if (allowNoData &&
                (message.Contains(
                    "no order",
                    StringComparison.OrdinalIgnoreCase) ||
                 message.Contains(
                    "no trade",
                    StringComparison.OrdinalIgnoreCase) ||
                 message.Contains(
                    "no position",
                    StringComparison.OrdinalIgnoreCase) ||
                 message.Contains(
                    "no holding",
                    StringComparison.OrdinalIgnoreCase) ||
                 message.Contains(
                    "no data",
                    StringComparison.OrdinalIgnoreCase)))
                return null;

            throw new InvalidOperationException(message);
        }

        return root;
    }

    private async Task<string> DiscoverPublicIp(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            _ipAddressService);
        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = (await response.Content.ReadAsStringAsync(
            cancellationToken)).Trim();

        try
        {
            return NuvamaExtensions.FindString(
                JToken.Parse(content),
                "ip");
        }
        catch (JsonException)
        {
            return content.Trim('"');
        }
    }

    private async Task<JToken> Send(
        string path,
        HttpMethod method,
        object body,
        bool sendSource,
        bool allowNoData,
        CancellationToken cancellationToken)
    {
        await RateLimit(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        AddHeader(request, "Authorization", _authorization);
        AddHeader(request, "SourceToken", _vendorToken);
        AddHeader(request, "AppIdKey", _appIdKey);
        AddHeader(request, "X-Forwarded-For", _publicIpAddress);
        if (sendSource)
            AddHeader(request, "Source", _source);
        if (body != null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(body, _jsonSettings),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);
        UpdateRotatingHeaders(response);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        JToken parsed = null;
        Exception parseError = null;
        try
        {
            parsed = ParseResponse(path, json, allowNoData);
        }
        catch (Exception error) when (
            error is InvalidDataException or InvalidOperationException)
        {
            parseError = error;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Nuvama {path} returned HTTP {(int)response.StatusCode} " +
                $"({response.ReasonPhrase})." +
                (parseError == null ? string.Empty : $" {parseError.Message}"),
                parseError);
        }

        if (parseError != null)
            throw parseError;
        return parsed;
    }

    private void UpdateRotatingHeaders(HttpResponseMessage response)
    {
        if (TryGetHeader(response, "appidkey") is string appIdKey &&
            !appIdKey.IsEmpty() &&
            !appIdKey.Equals(_appIdKey, StringComparison.Ordinal))
        {
            _appIdKey = appIdKey;
            AppIdKeyChanged?.Invoke(appIdKey);
        }

        if (TryGetHeader(response, "Authorization")
            is string authorization &&
            !authorization.IsEmpty() &&
            !authorization.Equals(
                _authorization,
                StringComparison.Ordinal))
        {
            _authorization = authorization;
            AuthorizationChanged?.Invoke(authorization);
        }
    }

    private static string TryGetHeader(
        HttpResponseMessage response,
        string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return values.FirstOrDefault();
        if (response.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();
        return null;
    }

    private static void AddHeader(
        HttpRequestMessage request,
        string name,
        string value)
    {
        if (!value.IsEmpty())
            request.Headers.TryAddWithoutValidation(name, value);
    }

    private async Task RateLimit(CancellationToken cancellationToken)
    {
        if (_minimumRequestInterval <= TimeSpan.Zero)
            return;

        await _rateLock.WaitAsync(cancellationToken);
        try
        {
            var delay = _lastRequest + _minimumRequestInterval -
                DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateLock.Release();
        }
    }

    private static JToken GetData(JToken response)
        => response == null
            ? null
            : NuvamaExtensions.FindToken(response, "data") ?? response;

    private static T[] ToArray<T>(
        JToken response,
        params string[] propertyNames)
    {
        if (response == null)
            return [];

        var data = GetData(response);
        var token = NuvamaExtensions.FindToken(data, propertyNames) ?? data;
        if (token is JArray array)
            return array.ToObject<T[]>() ?? [];
        if (token is JObject)
            return [token.ToObject<T>()];
        return [];
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    private static string ToSymbolKey(
        string exchange,
        string tradingSymbol)
        => $"{exchange?.ToUpperInvariant()}|{tradingSymbol?.ToUpperInvariant()}";
}
