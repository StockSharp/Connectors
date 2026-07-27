namespace StockSharp.Definedge.Native;

sealed class DefinedgeRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings =
        new()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

    private readonly Uri _historyAddress;
    private readonly Uri _instrumentMasterAddress;
    private readonly string _apiSessionKey;
    private readonly int _attempts;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private DefinedgeInstrument[] _instruments;
    private IReadOnlyDictionary<string, DefinedgeInstrument>
        _instrumentsByKey;
    private IReadOnlyDictionary<string, DefinedgeInstrument>
        _instrumentsBySymbol;

    public DefinedgeRestClient(
        Uri address,
        Uri historyAddress,
        Uri instrumentMasterAddress,
        string apiSessionKey,
        int attempts)
    {
        _historyAddress = EnsureTrailingSlash(
            historyAddress ??
            throw new ArgumentNullException(nameof(historyAddress)));
        _instrumentMasterAddress =
            instrumentMasterAddress ??
            throw new ArgumentNullException(
                nameof(instrumentMasterAddress));
        _apiSessionKey =
            apiSessionKey.ThrowIfEmpty(nameof(apiSessionKey));
        _attempts = Math.Max(1, attempts);
        _httpClient = new()
        {
            BaseAddress = EnsureTrailingSlash(
                address ??
                throw new ArgumentNullException(nameof(address))),
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Definedge/1.0");
    }

    public override string Name =>
        nameof(Definedge) + "_" +
        nameof(DefinedgeRestClient);

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        base.DisposeManaged();
    }

    public static async Task<DefinedgeSession> Login(
        Uri loginAddress,
        string apiToken,
        string apiSecret,
        string oneTimePassword,
        CancellationToken cancellationToken)
    {
        apiToken.ThrowIfEmpty(nameof(apiToken));
        apiSecret.ThrowIfEmpty(nameof(apiSecret));
        oneTimePassword.ThrowIfEmpty(nameof(oneTimePassword));

        using var client = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(
                loginAddress ??
                throw new ArgumentNullException(
                    nameof(loginAddress))),
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Definedge/1.0");

        using var challengeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"login/{Uri.EscapeDataString(apiToken)}");
        challengeRequest.Headers.TryAddWithoutValidation(
            "api_secret", apiSecret);
        using var challengeResponse = await client.SendAsync(
            challengeRequest,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var challengeContent =
            await challengeResponse.Content.ReadAsStringAsync(
                cancellationToken);
        EnsureHttpSuccess(
            challengeResponse,
            challengeContent,
            "login challenge");
        var challenge =
            JsonConvert.DeserializeObject<DefinedgeLoginChallenge>(
                challengeContent, _jsonSettings) ??
            throw new InvalidOperationException(
                "Definedge returned an empty login challenge.");
        challenge.OtpToken.ThrowIfEmpty(
            nameof(challenge.OtpToken));

        var payload = new
        {
            otp_token = challenge.OtpToken,
            otp = oneTimePassword,
            ac = CreateAuthCode(
                challenge.OtpToken,
                oneTimePassword,
                apiSecret),
        };
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Post, "token")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(
                    payload, Formatting.None, _jsonSettings),
                Encoding.UTF8,
                "application/json"),
        };
        using var tokenResponse = await client.SendAsync(
            tokenRequest,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var tokenContent =
            await tokenResponse.Content.ReadAsStringAsync(
                cancellationToken);
        EnsureHttpSuccess(
            tokenResponse, tokenContent, "token");
        var session =
            JsonConvert.DeserializeObject<DefinedgeSession>(
                tokenContent, _jsonSettings) ??
            throw new InvalidOperationException(
                "Definedge returned an empty session.");
        if (!session.Status.IsEmpty() &&
            !session.Status.EqualsIgnoreCase("OK"))
        {
            throw new InvalidOperationException(
                $"Definedge login failed: {session.Message.IsEmpty(session.Status)}");
        }
        session.UserId.ThrowIfEmpty(nameof(session.UserId));
        session.AccountId.ThrowIfEmpty(nameof(session.AccountId));
        session.ApiSessionKey.ThrowIfEmpty(
            nameof(session.ApiSessionKey));
        session.WebSocketToken.ThrowIfEmpty(
            nameof(session.WebSocketToken));
        return session;
    }

    internal static string CreateAuthCode(
        string otpToken,
        string oneTimePassword,
        string apiSecret)
    {
        var bytes = Encoding.UTF8.GetBytes(
            otpToken.ThrowIfEmpty(nameof(otpToken)) +
            oneTimePassword.ThrowIfEmpty(
                nameof(oneTimePassword)) +
            apiSecret.ThrowIfEmpty(nameof(apiSecret)));
        return Convert.ToHexString(
            SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public async Task<DefinedgeInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            var bytes = await DownloadBytes(
                _instrumentMasterAddress,
                "instrument master",
                cancellationToken);
            using var archive = new ZipArchive(
                new MemoryStream(bytes),
                ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(
                item =>
                    item.Name.EndsWith(
                        ".csv",
                        StringComparison.OrdinalIgnoreCase)) ??
                archive.Entries.FirstOrDefault(
                    item => !item.Name.IsEmpty()) ??
                throw new InvalidDataException(
                    "Definedge instrument archive is empty.");
            using var reader = new StreamReader(
                entry.Open(), Encoding.UTF8, true, 1 << 16);
            _instruments = await ParseInstruments(
                reader, cancellationToken);
            _instrumentsByKey = _instruments
                .GroupBy(
                    item => item.Exchange.ToInstrumentKey(
                        item.Token),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            _instrumentsBySymbol = _instruments
                .Where(item => !item.TradingSymbol.IsEmpty())
                .GroupBy(
                    item => ToSymbolKey(
                        item.Exchange,
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

    public async Task<DefinedgeInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(
            instrumentKey, out var instrument)
                ? instrument
                : null;
    }

    public async Task<DefinedgeInstrument> FindInstrument(
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
        DefinedgeOrderRequest order,
        CancellationToken cancellationToken)
    {
        var root = await SendJson(
            HttpMethod.Post,
            "placeorder",
            order,
            cancellationToken);
        return root.GetText("order_id", "norenordno")
            .ThrowIfEmpty("order_id");
    }

    public async Task ModifyOrder(
        DefinedgeOrderRequest order,
        CancellationToken cancellationToken)
        => _ = await SendJson(
            HttpMethod.Post,
            "modify",
            order,
            cancellationToken);

    public async Task CancelOrder(
        string orderId,
        CancellationToken cancellationToken)
        => _ = await SendJson(
            HttpMethod.Get,
            $"cancel/{Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId)))}",
            null,
            cancellationToken);

    public Task<DefinedgeOrder[]> GetOrders(
        CancellationToken cancellationToken)
        => GetArray<DefinedgeOrder>(
            "orders", "orders", cancellationToken);

    public async Task<DefinedgeOrder> GetOrder(
        string orderId,
        CancellationToken cancellationToken)
    {
        var root = await SendJson(
            HttpMethod.Get,
            $"order/{Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId)))}",
            null,
            cancellationToken);
        return (root["order"] ??
            root["data"] ??
            root).ToObject<DefinedgeOrder>();
    }

    public Task<DefinedgeOrder[]> GetTrades(
        CancellationToken cancellationToken)
        => GetArray<DefinedgeOrder>(
            "trades", "trades", cancellationToken);

    public Task<DefinedgePosition[]> GetPositions(
        CancellationToken cancellationToken)
        => GetArray<DefinedgePosition>(
            "positions", "positions", cancellationToken);

    public Task<DefinedgeHolding[]> GetHoldings(
        CancellationToken cancellationToken)
        => GetArray<DefinedgeHolding>(
            "holdings", "data", cancellationToken);

    public async Task<DefinedgeLimits> GetLimits(
        CancellationToken cancellationToken)
        => (await SendJson(
            HttpMethod.Get,
            "limits",
            null,
            cancellationToken))
            .ToObject<DefinedgeLimits>();

    public Task<JObject> GetQuote(
        DefinedgeInstrument instrument,
        CancellationToken cancellationToken)
        => SendJson(
            HttpMethod.Get,
            $"quotes/{Uri.EscapeDataString(instrument.Exchange)}/{Uri.EscapeDataString(instrument.Token)}",
            null,
            cancellationToken);

    public async Task<DefinedgeHistoryRow[]> GetHistory(
        DefinedgeInstrument instrument,
        string interval,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        if (interval is not ("day" or "minute" or "tick"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval), interval,
                "Definedge history interval must be day, minute, or tick.");
        }

        var end = NormalizeUtc(to ?? DateTime.UtcNow);
        var defaultDays = interval switch
        {
            "day" => 3650,
            "minute" => 180,
            _ => 2,
        };
        var start = NormalizeUtc(
            from ?? end.AddDays(-defaultDays));
        if (start > end)
            return [];

        var path = string.Join(
            "/",
            Uri.EscapeDataString(instrument.Exchange),
            Uri.EscapeDataString(instrument.Token),
            interval,
            FormatHistoryTime(start),
            FormatHistoryTime(end));
        var content = await SendRaw(
            HttpMethod.Get,
            new Uri(_historyAddress, path),
            null,
            $"history {interval}",
            cancellationToken);
        return ParseHistory(content, interval == "tick");
    }

    private async Task<T[]> GetArray<T>(
        string path,
        string property,
        CancellationToken cancellationToken)
    {
        var root = await SendJson(
            HttpMethod.Get, path, null, cancellationToken);
        var token = root.GetValue(
            property, StringComparison.OrdinalIgnoreCase) ??
            root["data"];
        if (token == null || token.Type == JTokenType.Null)
            return [];
        if (token is not JArray array)
        {
            throw new InvalidDataException(
                $"Definedge {path} response does not contain an array.");
        }
        return array.ToObject<T[]>() ?? [];
    }

    private async Task<JObject> SendJson(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var content = await SendRaw(
            method,
            new Uri(path, UriKind.Relative),
            body,
            path,
            cancellationToken);
        JObject root;
        try
        {
            root = JObject.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Definedge {path} returned invalid JSON.",
                ex);
        }
        EnsureSuccess(root, path);
        return root;
    }

    private async Task<string> SendRaw(
        HttpMethod method,
        Uri requestUri,
        object body,
        string operation,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1; attempt <= _attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    method, requestUri);
                request.Headers.TryAddWithoutValidation(
                    "Authorization", _apiSessionKey);
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
                    "Definedge {0} {1}.",
                    method,
                    requestUri);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
                var content =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (content.IsEmpty())
                    {
                        throw new InvalidOperationException(
                            $"Definedge returned an empty response for {operation}.");
                    }
                    return content;
                }

                if (attempt < _attempts &&
                    (response.StatusCode ==
                        HttpStatusCode.TooManyRequests ||
                     (int)response.StatusCode >= 500))
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(250 * attempt),
                        cancellationToken);
                    continue;
                }

                throw new HttpRequestException(
                    $"Definedge {operation} returned HTTP {(int)response.StatusCode}: {Truncate(content, 300).IsEmpty(response.ReasonPhrase)}");
            }
            catch (Exception ex)
                when (attempt < _attempts &&
                    ex is HttpRequestException or IOException)
            {
                lastError = ex;
                await Task.Delay(
                    TimeSpan.FromMilliseconds(250 * attempt),
                    cancellationToken);
            }
        }

        throw lastError ??
            new HttpRequestException(
                $"Definedge {operation} failed.");
    }

    private async Task<byte[]> DownloadBytes(
        Uri address,
        string operation,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1; attempt <= _attempts; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    address,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content
                        .ReadAsByteArrayAsync(cancellationToken);
                }
                if (attempt == _attempts ||
                    response.StatusCode !=
                        HttpStatusCode.TooManyRequests &&
                    (int)response.StatusCode < 500)
                {
                    throw new HttpRequestException(
                        $"Definedge {operation} returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
                when (attempt < _attempts &&
                    ex is HttpRequestException or IOException)
            {
                lastError = ex;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(250 * attempt),
                cancellationToken);
        }
        throw lastError ??
            new HttpRequestException(
                $"Definedge {operation} failed.");
    }

    internal static async Task<DefinedgeInstrument[]>
        ParseInstruments(string content)
    {
        using var reader = new StringReader(content);
        return await ParseInstruments(
            reader, CancellationToken.None);
    }

    private static async Task<DefinedgeInstrument[]>
        ParseInstruments(
            TextReader reader,
            CancellationToken cancellationToken)
    {
        var csv = new FastCsvReader(
            reader, StringHelper.N)
        {
            ColumnSeparator = ',',
        };
        var instruments = new List<DefinedgeInstrument>();
        while (await csv.NextLineAsync(cancellationToken))
        {
            var values = new string[14];
            for (var index = 0;
                index < values.Length;
                index++)
            {
                values[index] = csv.ReadString()?.Trim();
            }

            if (values[0].IsEmpty() ||
                values[1].IsEmpty() ||
                values[3].IsEmpty() ||
                values[1].EqualsIgnoreCase("token"))
            {
                continue;
            }

            var multiplier = ParseDecimal(values[11]);
            if (multiplier <= 0)
                multiplier = 1;
            var precision = ParseInt(values[10]);
            var strikeDivisor =
                multiplier *
                Convert.ToDecimal(Math.Pow(10, precision));
            var rawStrike = ParseDecimal(values[9]);
            var priceScale =
                Convert.ToDecimal(Math.Pow(10, precision));
            instruments.Add(new()
            {
                Exchange = values[0].ToUpperInvariant(),
                Token = values[1],
                Symbol = values[2],
                TradingSymbol = values[3],
                InstrumentType = values[4],
                Expiry = ParseExpiry(values[5]),
                TickSize = priceScale > 0
                    ? ParseDecimal(values[6]) / priceScale
                    : ParseDecimal(values[6]),
                LotSize = Math.Max(
                    1, ParseDecimal(values[7])),
                OptionType = values[8],
                StrikePrice = strikeDivisor > 0
                    ? rawStrike / strikeDivisor
                    : rawStrike,
                PricePrecision = precision,
                Multiplier = multiplier,
                Isin = values[12],
                PriceFactor = ParseDecimal(values[13]),
            });
        }
        return [.. instruments];
    }

    internal static DefinedgeHistoryRow[] ParseHistory(
        string content, bool ticks)
    {
        if (content.IsEmpty())
            return [];
        if (content.TrimStart().StartsWith(
            "{", StringComparison.Ordinal))
        {
            var root = JObject.Parse(content);
            EnsureSuccess(root, "history");
            return [];
        }

        var rows = new List<DefinedgeHistoryRow>();
        using var reader = new StringReader(content);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var values = line
                .Split(',')
                .Select(value => value.Trim())
                .ToArray();
            if (ticks)
            {
                if (values.Length < 3)
                    continue;
                var timeText = values[0];
                var priceText = values[1];
                var volumeText = values[2];
                var oiText = values.ElementAtOrDefault(3);
                if (!long.TryParse(
                    timeText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var seconds) ||
                    seconds <= 0)
                {
                    continue;
                }
                rows.Add(new()
                {
                    Time = DateTimeOffset
                        .FromUnixTimeSeconds(seconds)
                        .UtcDateTime,
                    LastPrice = ParseDecimal(priceText),
                    LastVolume = ParseDecimal(volumeText),
                    OpenInterest = ParseNullableDecimal(oiText),
                });
                continue;
            }

            if (values.Length < 6)
                continue;
            var time = values[0].ToDefinedgeTime();
            var open = values[1];
            var high = values[2];
            var low = values[3];
            var close = values[4];
            var volume = values[5];
            var oi = values.ElementAtOrDefault(6);
            if (time == null ||
                open.EqualsIgnoreCase("open"))
                continue;
            rows.Add(new()
            {
                Time = time.Value,
                Open = ParseDecimal(open),
                High = ParseDecimal(high),
                Low = ParseDecimal(low),
                Close = ParseDecimal(close),
                Volume = ParseDecimal(volume),
                OpenInterest = ParseNullableDecimal(oi),
            });
        }
        return [.. rows];
    }

    private static void EnsureSuccess(
        JObject root, string operation)
    {
        var status = root.GetText("status", "stat");
        if (status.IsEmpty() ||
            status.EqualsIgnoreCase("SUCCESS") ||
            status.EqualsIgnoreCase("OK"))
        {
            return;
        }
        throw new InvalidOperationException(
            $"Definedge {operation} error: {root.GetText("message", "emsg", "error").IsEmpty(status)}");
    }

    private static void EnsureHttpSuccess(
        HttpResponseMessage response,
        string content,
        string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Definedge {operation} returned HTTP {(int)response.StatusCode}: {Truncate(content, 300).IsEmpty(response.ReasonPhrase)}");
        }
        if (content.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Definedge returned an empty response for {operation}.");
        }
    }

    private static string FormatHistoryTime(DateTime utc)
        => utc.ToUniversalTime()
            .AddMinutes(330)
            .ToString(
                "ddMMyyyyHHmm",
                CultureInfo.InvariantCulture);

    private static DateTime? ParseExpiry(string value)
    {
        if (value.IsEmpty() || value.Trim() is "0" or "-")
            return null;
        if (!DateTime.TryParseExact(
            value.Trim(),
            [
                "ddMMyyyy",
                "dd-MM-yyyy",
                "dd-MMM-yyyy",
                "yyyy-MM-dd",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var expiry))
        {
            return null;
        }
        return expiry.ToUtcFromIndia();
    }

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0;

    private static decimal? ParseNullableDecimal(string value)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;

    private static int ParseInt(string value)
        => int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? Math.Max(0, result)
                : 0;

    private static string ToSymbolKey(
        string exchange, string tradingSymbol)
        => $"{exchange?.ToUpperInvariant()}|{tradingSymbol?.ToUpperInvariant()}";

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

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
