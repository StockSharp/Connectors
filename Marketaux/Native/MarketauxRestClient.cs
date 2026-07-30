namespace StockSharp.Marketaux.Native;

sealed class MarketauxRestClient :
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

    public MarketauxRestClient(
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
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-Marketaux/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<MarketauxEntityResponse> GetEntities(
        string search,
        string symbols,
        int page,
        string entityTypes,
        string countries,
        CancellationToken cancellationToken)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        return Get<MarketauxEntityResponse>(
            "v1/entity/search",
            [
                Pair("search", NormalizeSearch(search)),
                Pair(
                    "symbols",
                    symbols.IsEmpty()
                        ? null
                        : MarketauxExtensions.ValidateTicker(
                            symbols)),
                Pair(
                    "types",
                    MarketauxExtensions.NormalizeCsv(
                        entityTypes, "entity types")),
                Pair(
                    "countries",
                    MarketauxExtensions.NormalizeCsv(
                        countries, "countries")),
                Pair(
                    "page",
                    page.ToString(CultureInfo.InvariantCulture)),
            ],
            "entity search",
            cancellationToken);
    }

    public Task<MarketauxNewsResponse> GetNews(
        string ticker,
        int page,
        int limit,
        DateTime? from,
        DateTime? to,
        string entityTypes,
        string countries,
        string language,
        bool mustHaveEntities,
        bool groupSimilar,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var query = CreateNewsQuery(
            ticker,
            limit,
            from,
            to,
            entityTypes,
            countries,
            language,
            mustHaveEntities,
            groupSimilar);
        query.Add(Pair(
            "page",
            page.ToString(CultureInfo.InvariantCulture)));
        return Get<MarketauxNewsResponse>(
            "v1/news/all",
            query,
            "market news",
            cancellationToken);
    }

    public async Task<MarketauxRawResponse> GetDataset(
        MarketauxDataKinds kind,
        string ticker,
        MarketauxIntervals interval,
        int limit,
        DateTime? from,
        DateTime? to,
        string entityTypes,
        string countries,
        string language,
        bool mustHaveEntities,
        bool groupSimilar,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        ticker = ticker.IsEmpty()
            ? null
            : MarketauxExtensions.ValidateTicker(ticker);
        var resource = kind.ToResource();
        List<KeyValuePair<string, string>> query;
        switch (kind)
        {
            case MarketauxDataKinds.NewsAnalysis:
                query = CreateNewsQuery(
                    ticker,
                    limit,
                    from,
                    to,
                    entityTypes,
                    countries,
                    language,
                    mustHaveEntities,
                    groupSimilar);
                query.Add(Pair("page", "1"));
                break;

            case MarketauxDataKinds.SentimentTimeSeries:
                query = CreateStatsQuery(
                    ticker,
                    limit,
                    from,
                    to,
                    entityTypes,
                    countries,
                    language);
                query.Add(Pair(
                    "interval",
                    interval.ToApiValue()));
                break;

            case MarketauxDataKinds.SentimentAggregation:
            case MarketauxDataKinds.TrendingEntities:
                query = CreateStatsQuery(
                    ticker,
                    limit,
                    from,
                    to,
                    entityTypes,
                    countries,
                    language);
                break;

            case MarketauxDataKinds.EntityTypes:
            case MarketauxDataKinds.Industries:
                query = [];
                break;

            case MarketauxDataKinds.NewsSources:
                query =
                [
                    Pair("distinct_domain", "true"),
                    Pair(
                        "language",
                        MarketauxExtensions.NormalizeCsv(
                            language, "languages")),
                    Pair("page", "1"),
                ];
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
        return new MarketauxRawResponse(
            resource,
            document.ToString(Formatting.None));
    }

    private static List<KeyValuePair<string, string>>
        CreateNewsQuery(
            string ticker,
            int limit,
            DateTime? from,
            DateTime? to,
            string entityTypes,
            string countries,
            string language,
            bool mustHaveEntities,
            bool groupSimilar)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            Pair(
                "symbols",
                ticker.IsEmpty()
                    ? null
                    : MarketauxExtensions.ValidateTicker(ticker)),
            Pair(
                "entity_types",
                MarketauxExtensions.NormalizeCsv(
                    entityTypes, "entity types")),
            Pair(
                "countries",
                MarketauxExtensions.NormalizeCsv(
                    countries, "countries")),
            Pair(
                "language",
                MarketauxExtensions.NormalizeCsv(
                    language, "languages")),
            Pair("filter_entities", "true"),
            Pair(
                "must_have_entities",
                mustHaveEntities ? "true" : "false"),
            Pair(
                "group_similar",
                groupSimilar ? "true" : "false"),
            Pair(
                "limit",
                limit.ToString(CultureInfo.InvariantCulture)),
        };
        AddDateRange(query, from, to);
        return query;
    }

    private static List<KeyValuePair<string, string>>
        CreateStatsQuery(
            string ticker,
            int limit,
            DateTime? from,
            DateTime? to,
            string entityTypes,
            string countries,
            string language)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            Pair("symbols", ticker),
            Pair(
                "entity_types",
                MarketauxExtensions.NormalizeCsv(
                    entityTypes, "entity types")),
            Pair(
                "countries",
                MarketauxExtensions.NormalizeCsv(
                    countries, "countries")),
            Pair(
                "language",
                MarketauxExtensions.NormalizeCsv(
                    language, "languages")),
            Pair("group_by", "symbol"),
            Pair(
                "limit",
                limit.ToString(CultureInfo.InvariantCulture)),
        };
        AddDateRange(query, from, to);
        return query;
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
                    $"Marketaux returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Marketaux returned an invalid {operation} schema.");
        }
    }

    private async Task<JObject> GetDocument(
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

                var document = TryParse(text);
                var (apiCode, apiMessage) = GetError(document);
                if (!response.IsSuccessStatusCode ||
                    !apiCode.IsEmpty())
                {
                    throw new MarketauxApiException(
                        response.StatusCode,
                        apiCode,
                        $"Marketaux {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}" +
                        (apiCode.IsEmpty()
                            ? string.Empty
                            : $", API {apiCode}") +
                        $"): {Sanitize(
                            apiMessage.IsEmpty(text))}");
                }
                if (document is null)
                {
                    throw new InvalidOperationException(
                        $"Marketaux returned invalid JSON for {operation}.");
                }
                return document;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Marketaux {operation} transport " +
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
                        $"Marketaux {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Marketaux {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IEnumerable<KeyValuePair<string, string>> query)
    {
        var resource = new Uri(_address, path);
        var values = query
            .Append(Pair("api_token", _token))
            .Where(pair => !pair.Value.IsEmpty())
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}=" +
                Uri.EscapeDataString(pair.Value));
        return new UriBuilder(resource)
        {
            Query = string.Join("&", values),
        }.Uri;
    }

    private static JObject TryParse(string payload)
    {
        if (payload.IsEmpty())
            return null;
        try
        {
            return JObject.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Code, string Message) GetError(
        JObject document)
    {
        if (document?["error"] is JObject error)
        {
            return (
                GetString(error["code"]),
                GetString(error["message"]));
        }
        return default;
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
                $"Marketaux {operation} response exceeds 64 MB.");
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
                    $"Marketaux {operation} response exceeds 64 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static void AddDateRange(
        ICollection<KeyValuePair<string, string>> query,
        DateTime? from,
        DateTime? to)
    {
        if (from is not null)
        {
            query.Add(Pair(
                "published_after",
                FormatDate(from.Value)));
        }
        if (to is not null)
        {
            query.Add(Pair(
                "published_before",
                FormatDate(to.Value)));
        }
    }

    private static string NormalizeSearch(string search)
    {
        if (search.IsEmpty())
            return null;
        search = search.Trim();
        if (search.Length > 256 ||
            search.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Marketaux entity search text is invalid.");
        }
        return search;
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

    private static string FormatDate(DateTime value)
        => value.ToUtcSafe().ToString(
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture);

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
                "Marketaux address must be an absolute HTTPS URI.",
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
