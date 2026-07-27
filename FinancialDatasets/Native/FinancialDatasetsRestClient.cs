namespace StockSharp.FinancialDatasets.Native;

sealed class FinancialDatasetsRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _payloadLimit = 64 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _token;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public FinancialDatasetsRestClient(
        Uri address,
        string token,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _token = token.ThrowIfEmpty(nameof(token));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-API-KEY", _token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-FinancialDatasets/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<FinancialDatasetsTickersResponse> GetTickers(
        bool activeOnly,
        CancellationToken cancellationToken)
        => Get<FinancialDatasetsTickersResponse>(
            activeOnly
                ? "prices/snapshot/tickers"
                : "company/facts/tickers",
            [],
            "ticker lookup",
            cancellationToken);

    public Task<FinancialDatasetsFactsResponse> GetFacts(
        string identifier,
        bool isCik,
        CancellationToken cancellationToken)
        => Get<FinancialDatasetsFactsResponse>(
            "company/facts",
            Query(isCik ? "cik" : "ticker", identifier),
            "company facts",
            cancellationToken);

    public Task<FinancialDatasetsSnapshotResponse> GetSnapshot(
        string ticker,
        CancellationToken cancellationToken)
        => Get<FinancialDatasetsSnapshotResponse>(
            "prices/snapshot",
            Query("ticker", NormalizeTicker(ticker)),
            "price snapshot",
            cancellationToken);

    public Task<FinancialDatasetsPricesResponse> GetPrices(
        string ticker,
        string interval,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (interval.IsEmpty())
            throw new ArgumentNullException(nameof(interval));

        return Get<FinancialDatasetsPricesResponse>(
            "prices",
            [
                Pair("ticker", NormalizeTicker(ticker)),
                Pair("interval", interval),
                Pair("start_date", FormatDate(from)),
                Pair("end_date", FormatDate(to)),
            ],
            "historical prices",
            cancellationToken);
    }

    public Task<FinancialDatasetsNewsResponse> GetNews(
        string ticker,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return Get<FinancialDatasetsNewsResponse>(
            "news",
            [
                Pair(
                    "ticker",
                    ticker.IsEmpty()
                        ? null
                        : NormalizeTicker(ticker)),
                Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)),
            ],
            "news",
            cancellationToken);
    }

    public async Task<FinancialDatasetsRawResponse> GetDataset(
        FinancialDatasetsDataKinds kind,
        string ticker,
        FinancialDatasetsPeriods period,
        int limit,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        ticker = NormalizeTicker(ticker);
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var resource = kind.ToResource();
        var query = new List<KeyValuePair<string, string>>
        {
            Pair("ticker", ticker),
        };
        switch (kind)
        {
            case FinancialDatasetsDataKinds.CompanyFacts:
                break;

            case FinancialDatasetsDataKinds.FinancialStatements:
            case FinancialDatasetsDataKinds.FinancialMetrics:
                query.Add(Pair("period", period.ToApiValue()));
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                AddDateRange(
                    query, "report_period", from, to);
                break;

            case FinancialDatasetsDataKinds.Earnings:
                query.Add(Pair(
                    "limit",
                    Math.Min(limit, 40).ToString(
                        CultureInfo.InvariantCulture)));
                break;

            case FinancialDatasetsDataKinds.InstitutionalHoldings:
                query.Add(Pair(
                    "limit",
                    Math.Min(limit, 200).ToString(
                        CultureInfo.InvariantCulture)));
                AddDateRange(
                    query, "report_period", from, to);
                break;

            case FinancialDatasetsDataKinds.InsiderTrades:
            case FinancialDatasetsDataKinds.InsiderOwnership:
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                AddDateRange(
                    query, "filing_date", from, to);
                break;

            case FinancialDatasetsDataKinds.BeneficialOwnership:
            case FinancialDatasetsDataKinds.ActivistOwnership:
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                if (from is not null || to is not null)
                    query.Add(Pair("history", "true"));
                AddDateRange(
                    query, "filing_date", from, to);
                break;

            case FinancialDatasetsDataKinds.SecFilings:
                query.Add(Pair(
                    "limit",
                    limit.ToString(CultureInfo.InvariantCulture)));
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null);
        }

        var document = await GetDocument(
            resource,
            query,
            kind.ToString(),
            cancellationToken);
        return new FinancialDatasetsRawResponse(
            resource,
            document.ToString(Formatting.None));
    }

    private async Task<T> Get<T>(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var document = await GetDocument(
            path, query, operation, cancellationToken);
        try
        {
            return document.ToObject<T>(
                JsonSerializer.Create(_jsonSettings)) ??
                throw new InvalidOperationException(
                    $"Financial Datasets returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Financial Datasets returned an invalid {operation} schema.");
        }
    }

    private async Task<JObject> GetDocument(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var requestAddress = BuildAddress(path, query);
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

                if (IsTransient(response.StatusCode) && attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }

                var document = Parse(payload, operation);
                var error = GetString(document["error"]);
                var detail = GetString(document["message"])
                    .IsEmpty(GetString(document["detail"]));
                if (!response.IsSuccessStatusCode ||
                    !error.IsEmpty())
                {
                    throw new FinancialDatasetsApiException(
                        response.StatusCode,
                        error,
                        $"Financial Datasets {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}" +
                        (error.IsEmpty()
                            ? string.Empty
                            : $", API {error}") +
                        $"): {Sanitize(detail.IsEmpty(error))}");
                }

                return document;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Financial Datasets {operation} transport " +
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
                        $"Financial Datasets {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Financial Datasets {operation} exhausted its retry limit.");
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

    private static JObject Parse(
        byte[] payload,
        string operation)
    {
        try
        {
            return JObject.Parse(Encoding.UTF8.GetString(payload));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Financial Datasets returned invalid JSON for {operation}.");
        }
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
                $"Financial Datasets {operation} response exceeds 64 MB.");
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
                    $"Financial Datasets {operation} response exceeds 64 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static void AddDateRange(
        ICollection<KeyValuePair<string, string>> query,
        string prefix,
        DateTime? from,
        DateTime? to)
    {
        if (from is not null)
            query.Add(Pair($"{prefix}_gte", FormatDate(from.Value)));
        if (to is not null)
            query.Add(Pair($"{prefix}_lte", FormatDate(to.Value)));
    }

    private static IEnumerable<KeyValuePair<string, string>> Query(
        string key,
        string value)
        => [Pair(key, value)];

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

    private static string NormalizeTicker(string ticker)
    {
        ticker = ticker?.Trim().ToUpperInvariant();
        if (ticker.IsEmpty())
            throw new ArgumentNullException(nameof(ticker));
        if (ticker.Contains(','))
        {
            throw new ArgumentException(
                "A single Financial Datasets ticker is required.",
                nameof(ticker));
        }
        return ticker;
    }

    private static string FormatDate(DateTime value)
        => FinancialDatasetsExtensions.ToUtcSafe(value).ToString(
            "yyyy-MM-dd", CultureInfo.InvariantCulture);

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
                "Financial Datasets address must be an absolute HTTPS URI.",
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
