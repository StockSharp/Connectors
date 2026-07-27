namespace StockSharp.Bavest.Native;

sealed class BavestRestClient :
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

    public BavestRestClient(
        Uri address,
        string token,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _token = token?.Trim().ThrowIfEmpty(nameof(token));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "x-api-key", _token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-Bavest/2.1");
        _delay = delay ?? Task.Delay;
    }

    public Task<BavestSecuritiesResponse> SearchSecurities(
        string query,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        query = NormalizeSearch(query);
        ValidatePage(limit, offset);
        return Get<BavestSecuritiesResponse>(
            "v2/reference/search",
            [
                Pair("q", query),
                Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "offset",
                    offset.ToString(CultureInfo.InvariantCulture)),
            ],
            "security search",
            cancellationToken);
    }

    public Task<BavestSecuritiesResponse> GetSecurities(
        bool etfs,
        int limit,
        int offset,
        string exchangeCode,
        CancellationToken cancellationToken)
    {
        ValidatePage(limit, offset);
        return Get<BavestSecuritiesResponse>(
            etfs
                ? "v2/etfs/list"
                : "v2/securities/list",
            [
                Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "offset",
                    offset.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "exchangeCode",
                    BavestExtensions.NormalizeOptionalCode(
                        exchangeCode, "exchange code")),
            ],
            etfs ? "ETF list" : "security list",
            cancellationToken);
    }

    public Task<BavestQuote> GetQuote(
        string ticker,
        string currency,
        string exchange,
        CancellationToken cancellationToken)
        => GetData<BavestQuote>(
            "v2/timeseries/quote",
            [
                Pair(
                    "symbol",
                    BavestExtensions.ValidateTicker(ticker)),
                Pair(
                    "currency",
                    BavestExtensions.NormalizeOptionalCode(
                        currency, "currency")),
                Pair(
                    "exchange",
                    BavestExtensions.NormalizeOptionalCode(
                        exchange, "exchange")),
            ],
            "real-time quote",
            cancellationToken);

    public Task<BavestCandlesData> GetCandles(
        string ticker,
        string resolution,
        DateTime from,
        DateTime to,
        string currency,
        string exchange,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (resolution.IsEmpty())
            throw new ArgumentNullException(nameof(resolution));
        return GetData<BavestCandlesData>(
            "v2/timeseries/candles",
            [
                Pair(
                    "symbol",
                    BavestExtensions.ValidateTicker(ticker)),
                Pair("resolution", resolution),
                Pair("from", FormatDate(from)),
                Pair("to", FormatDate(to)),
                Pair(
                    "currency",
                    BavestExtensions.NormalizeOptionalCode(
                        currency, "currency")),
                Pair(
                    "exchange",
                    BavestExtensions.NormalizeOptionalCode(
                        exchange, "exchange")),
            ],
            "OHLCV candles",
            cancellationToken);
    }

    public Task<BavestNewsResponse> GetNews(
        string ticker,
        int limit,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return Get<BavestNewsResponse>(
            ticker.IsEmpty()
                ? "v2/news/market"
                : "v2/news/stock",
            [
                Pair(
                    "symbol",
                    ticker.IsEmpty()
                        ? null
                        : BavestExtensions.ValidateTicker(ticker)),
                Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "from",
                    FormatOptionalDate(from)),
                Pair(
                    "to",
                    FormatOptionalDate(to)),
            ],
            ticker.IsEmpty()
                ? "market news"
                : "stock news",
            cancellationToken);
    }

    public async Task<BavestRawResponse> GetDataset(
        BavestDataKinds kind,
        string ticker,
        BavestFinancialFrequencies frequency,
        int limit,
        string currency,
        bool traceEtfMetrics,
        string screenerQuery,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        ticker = ticker.IsEmpty()
            ? null
            : BavestExtensions.ValidateTicker(ticker);
        if (kind.RequiresTicker() && ticker.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Bavest {kind} requires a security symbol.");
        }

        var resource = kind.ToResource();
        HttpMethod method;
        List<KeyValuePair<string, string>> query;
        JToken body = null;
        switch (kind)
        {
            case BavestDataKinds.CompanyProfile:
            case BavestDataKinds.FinancialsTtm:
            case BavestDataKinds.AnalystConsensus:
            case BavestDataKinds.AnalystRecommendations:
            case BavestDataKinds.PriceTarget:
            case BavestDataKinds.EtfProfile:
                method = HttpMethod.Get;
                query = [Pair("symbol", ticker)];
                break;

            case BavestDataKinds.EquityMetrics:
                method = HttpMethod.Get;
                query =
                [
                    Pair("symbol", ticker),
                    Pair(
                        "currency",
                        BavestExtensions.NormalizeOptionalCode(
                            currency, "currency")),
                    Pair(
                        "limit",
                        Math.Min(limit, 200).ToString(
                            CultureInfo.InvariantCulture)),
                ];
                break;

            case BavestDataKinds.IncomeStatements:
            case BavestDataKinds.BalanceSheets:
            case BavestDataKinds.CashFlows:
                method = HttpMethod.Get;
                query =
                [
                    Pair("symbol", ticker),
                    Pair("freq", frequency.ToApiValue()),
                ];
                break;

            case BavestDataKinds.UpgradesDowngrades:
            case BavestDataKinds.DividendHistory:
                method = HttpMethod.Get;
                query =
                [
                    Pair("symbol", ticker),
                    Pair(
                        "limit",
                        limit.ToString(CultureInfo.InvariantCulture)),
                ];
                break;

            case BavestDataKinds.EtfMetrics:
                method = HttpMethod.Get;
                query =
                [
                    Pair("symbol", ticker),
                    Pair(
                        "limit",
                        limit.ToString(CultureInfo.InvariantCulture)),
                    Pair(
                        "trace",
                        traceEtfMetrics ? "true" : "false"),
                ];
                break;

            case BavestDataKinds.Screener:
                method = HttpMethod.Post;
                query = [];
                body = BuildScreenerBody(
                    screenerQuery, limit, currency);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null);
        }

        var document = await SendToken(
            method,
            resource,
            query,
            body,
            kind.ToString(),
            cancellationToken);
        return new BavestRawResponse(
            resource,
            document.ToString(Formatting.None));
    }

    private async Task<T> Get<T>(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var document = await SendToken(
            HttpMethod.Get,
            path,
            query,
            null,
            operation,
            cancellationToken);
        return Deserialize<T>(document, operation);
    }

    private async Task<T> GetData<T>(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var document = await SendToken(
            HttpMethod.Get,
            path,
            query,
            null,
            operation,
            cancellationToken);
        var data = document is JObject obj &&
            obj.TryGetValue(
                "data",
                StringComparison.OrdinalIgnoreCase,
                out var value)
            ? value
            : document;
        return Deserialize<T>(data, operation);
    }

    private static T Deserialize<T>(
        JToken token,
        string operation)
    {
        try
        {
            return token.ToObject<T>(
                JsonSerializer.Create(_jsonSettings)) ??
                throw new InvalidOperationException(
                    $"Bavest returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Bavest returned an invalid {operation} schema.");
        }
    }

    private async Task<JToken> SendToken(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        JToken body,
        string operation,
        CancellationToken cancellationToken)
    {
        var requestAddress = BuildAddress(path, query ?? []);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    method, requestAddress);
                if (body is not null)
                {
                    request.Content = new StringContent(
                        body.ToString(Formatting.None),
                        Encoding.UTF8,
                        "application/json");
                }
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

                var document = TryParse(text);
                var error = GetError(document, text);
                if (!response.IsSuccessStatusCode ||
                    IsSemanticError(document))
                {
                    var requestId = GetRequestId(response);
                    throw new BavestApiException(
                        response.StatusCode,
                        requestId,
                        $"Bavest {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(error));
                }
                if (document is null)
                {
                    throw new InvalidOperationException(
                        $"Bavest returned invalid JSON for {operation}.");
                }
                return document;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Bavest {operation} transport " +
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
                        $"Bavest {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Bavest {operation} exhausted its retry limit.");
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

    private static bool IsSemanticError(JToken token)
        => token is JObject obj &&
            (GetString(obj["status"])?.Equals(
                "ERROR",
                StringComparison.OrdinalIgnoreCase) == true ||
            obj["error"] is not null);

    private static string GetError(
        JToken token,
        string payload)
    {
        if (token is JObject obj)
        {
            var error = obj["error"];
            if (error is JObject errorObj)
            {
                return GetString(errorObj["message"])
                    .IsEmpty(GetString(errorObj["code"]))
                    .IsEmpty(error.ToString(Formatting.None));
            }
            return GetString(obj["message"])
                .IsEmpty(GetString(error))
                .IsEmpty(GetString(obj["status"]))
                .IsEmpty(payload);
        }
        return payload.IsEmpty("empty error response");
    }

    private static JObject BuildScreenerBody(
        string query,
        int limit,
        string currency)
    {
        JArray filters;
        try
        {
            filters = query.IsEmpty()
                ? []
                : JArray.Parse(query);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "Bavest screener query must be a JSON array.");
        }
        var result = new JObject
        {
            ["query"] = filters,
            ["offset"] = 0,
            ["limit"] = limit,
        };
        var normalizedCurrency =
            BavestExtensions.NormalizeOptionalCode(
                currency, "currency");
        if (!normalizedCurrency.IsEmpty())
            result["currency"] = normalizedCurrency;
        return result;
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
                $"Bavest {operation} response exceeds 128 MB.");
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
                    $"Bavest {operation} response exceeds 128 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
    }

    private static string NormalizeSearch(string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            throw new ArgumentNullException(nameof(value));
        if (value.Length > 256 ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Bavest security search text is invalid.");
        }
        return value;
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

    private static string FormatDate(DateTime value)
        => value.ToUtcSafe().ToString(
            "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatOptionalDate(DateTime? value)
        => value is null ? null : FormatDate(value.Value);

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            statusCode == HttpStatusCode.BadGateway ||
            statusCode == HttpStatusCode.ServiceUnavailable ||
            statusCode == HttpStatusCode.GatewayTimeout ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
        => response.Headers.RetryAfter?.Delta is { } delay &&
            delay > TimeSpan.Zero
                ? delay.Min(TimeSpan.FromSeconds(30))
                : TimeSpan.FromSeconds(Math.Pow(2, attempt));

    private static string GetRequestId(
        HttpResponseMessage response)
    {
        foreach (var name in new[]
        {
            "X-Request-Id",
            "x-amzn-RequestId",
            "x-amz-apigw-id",
        })
        {
            if (response.Headers.TryGetValues(
                name, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

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
                "Bavest address must be an absolute HTTPS URI.",
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
