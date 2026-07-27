namespace StockSharp.StockDataOrg.Native;

sealed class StockDataOrgRestClient :
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

    public StockDataOrgRestClient(
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
            "User-Agent", "StockSharp-StockDataOrg/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<StockDataOrgResponse<StockDataOrgEntity>>
        SearchEntities(
            string search,
            string symbols,
            string types,
            int page,
            CancellationToken cancellationToken)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));

        return Get<StockDataOrgEntity>(
            "entity/search",
            new Dictionary<string, string>
            {
                ["search"] = search,
                ["symbols"] = symbols,
                ["types"] = types,
                ["page"] = page.ToString(
                    CultureInfo.InvariantCulture),
            },
            "entity search",
            cancellationToken);
    }

    public Task<StockDataOrgResponse<StockDataOrgQuote>>
        GetQuote(
            string symbol,
            bool extendedHours,
            CancellationToken cancellationToken)
        => Get<StockDataOrgQuote>(
            "data/quote",
            new Dictionary<string, string>
            {
                ["symbols"] = NormalizeSymbol(symbol),
                ["extended_hours"] =
                    extendedHours ? "true" : "false",
            },
            "quote",
            cancellationToken);

    public Task<StockDataOrgResponse<StockDataOrgBar>>
        GetBars(
            string symbol,
            string interval,
            bool intraday,
            bool adjustedIntraday,
            bool extendedHours,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (interval.IsEmpty())
            throw new ArgumentNullException(nameof(interval));

        var query = new Dictionary<string, string>
        {
            ["symbols"] = NormalizeSymbol(symbol),
            ["interval"] = interval,
            ["sort"] = "asc",
            ["date_from"] = from.UtcDateTime.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["date_to"] = to.UtcDateTime.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (intraday)
        {
            query["extended_hours"] =
                extendedHours ? "true" : "false";
        }

        return Get<StockDataOrgBar>(
            intraday
                ? adjustedIntraday
                    ? "data/intraday/adjusted"
                    : "data/intraday"
                : "data/eod",
            query,
            intraday ? "intraday history" : "end-of-day history",
            cancellationToken);
    }

    public Task<StockDataOrgResponse<StockDataOrgArticle>>
        GetNews(
            string symbol,
            string language,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int page,
            int limit,
            CancellationToken cancellationToken)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));

        var query = new Dictionary<string, string>
        {
            ["symbols"] = symbol.IsEmpty()
                ? null
                : NormalizeSymbol(symbol),
            ["filter_entities"] = symbol.IsEmpty()
                ? null
                : "true",
            ["entity_types"] =
                "equity,index,etf,mutualfund",
            ["language"] = language?.Trim(),
            ["published_after"] = FormatNewsTime(from),
            ["published_before"] = FormatNewsTime(to),
            ["limit"] = limit.ToString(
                CultureInfo.InvariantCulture),
            ["page"] = page.ToString(
                CultureInfo.InvariantCulture),
        };

        return Get<StockDataOrgArticle>(
            "news/all",
            query,
            "news",
            cancellationToken);
    }

    private async Task<StockDataOrgResponse<T>> Get<T>(
        string path,
        IReadOnlyDictionary<string, string> query,
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

                var document = Deserialize<T>(payload, operation);
                if (!response.IsSuccessStatusCode ||
                    document.Error is not null)
                {
                    var code = document.Error?.Code;
                    var message = document.Error?.Message
                        .IsEmpty(GetErrorMessage(payload));
                    throw new StockDataOrgApiException(
                        response.StatusCode,
                        code,
                        $"StockData.org {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}" +
                        (code.IsEmpty()
                            ? string.Empty
                            : $", API {code}") +
                        $"): {Sanitize(message)}");
                }

                document.Data ??= [];
                return document;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"StockData.org {operation} transport request " +
                        "failed after four attempts.");
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
                        $"StockData.org {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"StockData.org {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var values = query
            .Where(pair => !pair.Value.IsEmpty())
            .Concat([
                new KeyValuePair<string, string>(
                    "api_token", _token),
            ]);
        var resource = new Uri(_address, path);
        return new UriBuilder(resource)
        {
            Query = string.Join(
                "&",
                values.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}=" +
                    Uri.EscapeDataString(pair.Value))),
        }.Uri;
    }

    private static StockDataOrgResponse<T> Deserialize<T>(
        byte[] payload,
        string operation)
    {
        try
        {
            return JsonConvert.DeserializeObject<
                StockDataOrgResponse<T>>(
                    Encoding.UTF8.GetString(payload),
                    _jsonSettings) ??
                throw new InvalidOperationException(
                    $"StockData.org returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"StockData.org returned invalid JSON for {operation}.");
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
                $"StockData.org {operation} response exceeds 64 MB.");
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
                    $"StockData.org {operation} response exceeds 64 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
        => response.Headers.RetryAfter?.Delta is { } delay &&
            delay > TimeSpan.Zero
                ? delay.Min(TimeSpan.FromSeconds(30))
                : TimeSpan.FromSeconds(Math.Pow(2, attempt));

    private static string FormatNewsTime(DateTimeOffset? value)
        => value?.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture);

    private static string NormalizeSymbol(string symbol)
    {
        symbol = symbol?.Trim().ToUpperInvariant();
        if (symbol.IsEmpty())
            throw new ArgumentNullException(nameof(symbol));
        if (symbol.Contains(','))
        {
            throw new ArgumentException(
                "A single StockData.org symbol is required.",
                nameof(symbol));
        }
        return symbol;
    }

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";
        var value = Encoding.UTF8.GetString(payload);
        return value.Length > 2000 ? value[..2000] : value;
    }

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
                "StockData.org address must be an absolute HTTPS URI.",
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
