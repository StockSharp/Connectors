namespace StockSharp.UnusualWhales.Native;

sealed class UnusualWhalesRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _payloadLimit = 128 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _token;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public UnusualWhalesRestClient(
        Uri address,
        string token,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _token = NormalizeToken(token);
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-UnusualWhales/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<UnusualWhalesListingsData> GetListings(
        CancellationToken cancellationToken)
        => GetData<UnusualWhalesListingsData>(
            "api/companies/listings",
            [Pair("status", "active")],
            "active listings",
            cancellationToken);

    public Task<UnusualWhalesCompanyProfile> GetCompanyProfile(
        string ticker,
        CancellationToken cancellationToken)
        => GetData<UnusualWhalesCompanyProfile>(
            $"api/companies/{EscapeTicker(ticker)}/profile",
            [],
            "company profile",
            cancellationToken);

    public Task<UnusualWhalesStockState> GetStockState(
        string ticker,
        CancellationToken cancellationToken)
        => GetData<UnusualWhalesStockState>(
            $"api/stock/{EscapeTicker(ticker)}/stock-state",
            [],
            "stock state",
            cancellationToken);

    public Task<List<UnusualWhalesCandle>> GetCandles(
        string ticker,
        string candleSize,
        DateTime from,
        DateTime to,
        int limit,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 2500)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (candleSize.IsEmpty())
            throw new ArgumentNullException(nameof(candleSize));
        return GetData<List<UnusualWhalesCandle>>(
            $"api/stock/{EscapeTicker(ticker)}/ohlc/" +
                Uri.EscapeDataString(candleSize),
            [
                Pair(
                    "timeframe",
                    UnusualWhalesExtensions.ToApiTimeframe(
                        from, to)),
                Pair("end_date", FormatDate(to)),
                Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)),
            ],
            "OHLC history",
            cancellationToken);
    }

    public Task<List<UnusualWhalesHeadline>> GetNews(
        string ticker,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return GetData<List<UnusualWhalesHeadline>>(
            "api/news/headlines",
            [
                Pair(
                    "ticker",
                    ticker.IsEmpty()
                        ? null
                        : UnusualWhalesExtensions.ValidateTicker(
                            ticker)),
                Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "page",
                    page.ToString(CultureInfo.InvariantCulture)),
            ],
            "news headlines",
            cancellationToken);
    }

    public async Task<UnusualWhalesRawResponse> GetDataset(
        UnusualWhalesDataKinds kind,
        string ticker,
        int limit,
        DateTime? from,
        DateTime? to,
        bool unusualFlowOnly,
        bool otmMarketTide,
        bool fiveMinuteMarketTide,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));
        ticker = ticker.IsEmpty()
            ? null
            : UnusualWhalesExtensions.ValidateTicker(ticker);

        var resource = kind.ToResource(ticker);
        var query = new List<KeyValuePair<string, string>>();
        switch (kind)
        {
            case UnusualWhalesDataKinds.CompanyProfile:
            case UnusualWhalesDataKinds.StockState:
            case UnusualWhalesDataKinds.RecentOptionsFlow:
            case UnusualWhalesDataKinds.MarketMovers:
                break;

            case UnusualWhalesDataKinds.OptionsFlowAlerts:
                query.Add(Pair("ticker_symbol", ticker));
                query.Add(Pair(
                    "unusual",
                    unusualFlowOnly ? "true" : "false"));
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                AddUnixRange(query, from, to);
                break;

            case UnusualWhalesDataKinds.DarkPoolTrades:
                query.Add(Pair(
                    "date",
                    FormatOptionalDate(to ?? from)));
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                break;

            case UnusualWhalesDataKinds.InterpolatedIv:
            case UnusualWhalesDataKinds.VolatilityStats:
                query.Add(Pair(
                    "date",
                    FormatOptionalDate(to ?? from)));
                break;

            case UnusualWhalesDataKinds.GreekExposure:
                query.Add(Pair(
                    "date",
                    FormatOptionalDate(to ?? from)));
                if (from is not null && to is not null)
                {
                    query.Add(Pair(
                        "timeframe",
                        UnusualWhalesExtensions.ToApiTimeframe(
                            from.Value.ToUtcSafe(),
                            to.Value.ToUtcSafe())));
                }
                break;

            case UnusualWhalesDataKinds.OptionsVolume:
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                break;

            case UnusualWhalesDataKinds.InsiderTransactions:
                query.Add(Pair("ticker_symbol", ticker));
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                query.Add(Pair("page", "1"));
                query.Add(Pair(
                    "start_date",
                    FormatOptionalDate(from)));
                query.Add(Pair(
                    "end_date",
                    FormatOptionalDate(to)));
                break;

            case UnusualWhalesDataKinds.CongressTrades:
                query.Add(Pair("ticker", ticker));
                query.Add(Pair(
                    "limit",
                    Math.Min(limit, 200).ToString(
                        CultureInfo.InvariantCulture)));
                query.Add(Pair(
                    "date",
                    FormatOptionalDate(to ?? from)));
                break;

            case UnusualWhalesDataKinds.MarketTide:
                query.Add(Pair(
                    "date",
                    FormatOptionalDate(to ?? from)));
                query.Add(Pair(
                    "otm_only",
                    otmMarketTide ? "true" : "false"));
                query.Add(Pair(
                    "interval_5m",
                    fiveMinuteMarketTide ? "true" : "false"));
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null);
        }

        var document = await SendToken(
            resource,
            query,
            kind.ToString(),
            cancellationToken);
        return new UnusualWhalesRawResponse(
            resource,
            document.ToString(Formatting.None));
    }

    private async Task<T> GetData<T>(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var document = await SendToken(
            path, query, operation, cancellationToken);
        var data = document is JObject obj &&
            obj.TryGetValue(
                "data",
                StringComparison.OrdinalIgnoreCase,
                out var value)
            ? value
            : document;
        try
        {
            return data.ToObject<T>(
                JsonSerializer.Create(_jsonSettings)) ??
                throw new InvalidOperationException(
                    $"Unusual Whales returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Unusual Whales returned an invalid {operation} schema.");
        }
    }

    private async Task<JToken> SendToken(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var requestAddress = BuildAddress(path, query ?? []);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, requestAddress);
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var payload = await ReadLimited(
                    response.Content,
                    operation,
                    cancellationToken);
                var text = Encoding.UTF8.GetString(payload);

                if (IsTransient(response.StatusCode) && attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }

                var token = TryParse(text);
                if (!response.IsSuccessStatusCode)
                {
                    throw new UnusualWhalesApiException(
                        response.StatusCode,
                        $"Unusual Whales {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(GetError(token, text)));
                }
                var semanticError = GetSemanticError(token);
                if (!semanticError.IsEmpty())
                {
                    throw new UnusualWhalesApiException(
                        response.StatusCode,
                        $"Unusual Whales {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(semanticError));
                }
                if (token is null)
                {
                    throw new InvalidOperationException(
                        $"Unusual Whales returned invalid JSON for {operation}.");
                }
                return token;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Unusual Whales {operation} transport " +
                        "request failed after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= 3)
                {
                    throw new TimeoutException(
                        $"Unusual Whales {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Unusual Whales {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IEnumerable<KeyValuePair<string, string>> query)
    {
        var resource = new Uri(_address, path);
        var values = query
            .Where(pair => !pair.Value.IsEmpty())
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}=" +
                Uri.EscapeDataString(pair.Value));
        return new UriBuilder(resource)
        {
            Query = string.Join("&", values),
        }.Uri;
    }

    private static JToken TryParse(string payload)
    {
        if (payload.IsEmpty())
            return null;
        try
        {
            return JToken.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetSemanticError(JToken token)
    {
        if (token is not JObject obj)
            return null;
        var error = GetString(obj["error"]);
        if (!error.IsEmpty())
        {
            return GetString(obj["message"])
                .IsEmpty(error);
        }
        if (obj["success"]?.Value<bool?>() == false)
        {
            return GetString(obj["detail"])
                .IsEmpty(GetString(obj["message"]))
                .IsEmpty("request failed");
        }
        return null;
    }

    private static string GetError(
        JToken token,
        string payload)
    {
        if (token is JObject obj)
        {
            return GetString(obj["detail"])
                .IsEmpty(GetString(obj["message"]))
                .IsEmpty(GetString(obj["error"]))
                .IsEmpty(payload);
        }
        return payload.IsEmpty("empty error response");
    }

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > _payloadLimit)
        {
            throw new InvalidOperationException(
                $"Unusual Whales {operation} response exceeds 128 MB.");
        }

        await using var source =
            await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > _payloadLimit)
            {
                throw new InvalidOperationException(
                    $"Unusual Whales {operation} response exceeds 128 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static void AddUnixRange(
        ICollection<KeyValuePair<string, string>> query,
        DateTime? from,
        DateTime? to)
    {
        if (from is not null)
        {
            query.Add(Pair(
                "newer_than",
                new DateTimeOffset(from.Value.ToUtcSafe())
                    .ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture)));
        }
        if (to is not null)
        {
            query.Add(Pair(
                "older_than",
                new DateTimeOffset(to.Value.ToUtcSafe())
                    .ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

    private static string EscapeTicker(string ticker)
        => Uri.EscapeDataString(
            UnusualWhalesExtensions.ValidateTicker(ticker));

    private static string FormatDate(DateTime value)
        => value.ToUtcSafe().ToString(
            "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatOptionalDate(DateTime? value)
        => value is null ? null : FormatDate(value.Value);

    private static string NormalizeToken(string token)
    {
        token = token?.Trim();
        if (token?.Equals(
            "Bearer",
            StringComparison.OrdinalIgnoreCase) == true)
        {
            token = null;
        }
        if (token?.StartsWith(
            "Bearer ",
            StringComparison.OrdinalIgnoreCase) == true)
        {
            token = token[7..].Trim();
        }
        if (token.IsEmpty())
            throw new ArgumentNullException(nameof(token));
        return token;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            statusCode == HttpStatusCode.ServiceUnavailable ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
        => response.Headers.RetryAfter?.Delta is { } delay &&
            delay > TimeSpan.Zero
                ? delay.Min(TimeSpan.FromSeconds(30))
                : TimeSpan.FromSeconds(Math.Pow(2, attempt));

    private static string GetString(JToken token)
        => token switch
        {
            null => null,
            JValue value => value.ToString(
                CultureInfo.InvariantCulture),
            _ => token.ToString(Formatting.None),
        };

    private string Sanitize(string value)
    {
        if (value.IsEmpty())
            return "unknown error";
        value = value.Replace(
            _token, "[REDACTED]",
            StringComparison.Ordinal);
        return new string(value
            .Take(2000)
            .Select(character =>
                char.IsControl(character) ? ' ' : character)
            .ToArray())
            .Trim();
    }

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Unusual Whales address must be an absolute HTTPS URI.",
                nameof(address));
        }
        return address.AbsoluteUri.EndsWith('/')
            ? address
            : new Uri(address.AbsoluteUri + "/");
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        base.DisposeManaged();
    }
}
