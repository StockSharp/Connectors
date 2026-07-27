namespace StockSharp.QuiverQuant.Native;

sealed class QuiverQuantRestClient :
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

    public QuiverQuantRestClient(
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
            "User-Agent", "StockSharp-QuiverQuant/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<List<QuiverQuantCompany>> GetCompanies(
        CancellationToken cancellationToken)
        => Get<List<QuiverQuantCompany>>(
            "beta/companies",
            [],
            "company list",
            cancellationToken);

    public Task<List<QuiverQuantNews>> GetNews(
        string ticker,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        return Get<List<QuiverQuantNews>>(
            "beta/live/quivernews",
            [
                Pair(
                    "page",
                    page.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "page_size",
                    pageSize.ToString(CultureInfo.InvariantCulture)),
                Pair(
                    "ticker",
                    ticker.IsEmpty()
                        ? null
                        : QuiverQuantExtensions.ValidateTicker(ticker)),
            ],
            "Quiver News",
            cancellationToken);
    }

    public async Task<QuiverQuantRawResponse> GetDataset(
        QuiverQuantDataKinds kind,
        string ticker,
        int limit,
        DateTime? from,
        DateTime? to,
        bool limitInsiderCodes,
        bool mostRecentInstitutional,
        bool includeNewFunds,
        string donorCycle,
        CancellationToken cancellationToken)
    {
        ticker = QuiverQuantExtensions.ValidateTicker(ticker);
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));
        donorCycle =
            QuiverQuantExtensions.NormalizeCycle(donorCycle);

        var resource = kind.ToResource(ticker);
        var query = new List<KeyValuePair<string, string>>();
        switch (kind)
        {
            case QuiverQuantDataKinds.CongressTrades:
            case QuiverQuantDataKinds.OffExchange:
            case QuiverQuantDataKinds.GovernmentContracts:
            case QuiverQuantDataKinds.TopShareholders:
            case QuiverQuantDataKinds.EarningsDistortionScores:
            case QuiverQuantDataKinds.EventsBeta:
                break;

            case QuiverQuantDataKinds.InsiderTrades:
                AddTickerPage(query, ticker, limit);
                query.Add(Pair(
                    "limit_codes",
                    Bool(limitInsiderCodes)));
                break;

            case QuiverQuantDataKinds.InstitutionalHoldings:
                AddTickerPage(query, ticker, limit);
                break;

            case QuiverQuantDataKinds.InstitutionalChanges:
                AddTickerPage(query, ticker, limit);
                query.Add(Pair(
                    "most_recent",
                    Bool(mostRecentInstitutional)));
                query.Add(Pair(
                    "show_new_funds",
                    Bool(includeNewFunds)));
                break;

            case QuiverQuantDataKinds.Lobbying:
            case QuiverQuantDataKinds.ExecutiveCompensation:
                AddPage(query, limit);
                break;

            case QuiverQuantDataKinds.CorporateDonors:
                AddPage(query, limit);
                query.Add(Pair("cycle", donorCycle));
                break;

            case QuiverQuantDataKinds.Patents:
                AddCompactRange(query, from, to);
                break;

            case QuiverQuantDataKinds.CnbcTrades:
                query.Add(Pair("ticker", ticker));
                if (from is not null &&
                    to is not null &&
                    from.Value.Date == to.Value.Date)
                {
                    query.Add(Pair(
                        "date",
                        QuiverQuantExtensions.FormatCompactDate(
                            from.Value)));
                }
                break;

            case QuiverQuantDataKinds.PatentDrift:
            case QuiverQuantDataKinds.PatentMomentum:
                query.Add(Pair("ticker", ticker));
                AddCompactRange(query, from, to);
                if (from is null && to is null)
                    query.Add(Pair("latest", "true"));
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null);
        }

        var token = await SendToken(
            resource,
            query,
            kind.ToString(),
            cancellationToken);
        return new QuiverQuantRawResponse(
            resource,
            token.ToString(Formatting.None));
    }

    private async Task<T> Get<T>(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var token = await SendToken(
            path, query, operation, cancellationToken);
        try
        {
            return token.ToObject<T>(
                JsonSerializer.Create(_jsonSettings)) ??
                throw new InvalidOperationException(
                    $"Quiver Quantitative returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Quiver Quantitative returned an invalid {operation} schema.");
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
                    throw new QuiverQuantApiException(
                        response.StatusCode,
                        $"Quiver Quantitative {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(GetError(token, text)));
                }
                var semanticError = GetSemanticError(token);
                if (!semanticError.IsEmpty())
                {
                    throw new QuiverQuantApiException(
                        response.StatusCode,
                        $"Quiver Quantitative {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(semanticError));
                }
                if (token is null)
                {
                    throw new InvalidOperationException(
                        $"Quiver Quantitative returned invalid JSON for {operation}.");
                }
                return token;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Quiver Quantitative {operation} transport " +
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
                        $"Quiver Quantitative {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Quiver Quantitative {operation} exhausted its retry limit.");
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
                $"Quiver Quantitative {operation} response exceeds 128 MB.");
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
                    $"Quiver Quantitative {operation} response exceeds 128 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static void AddTickerPage(
        ICollection<KeyValuePair<string, string>> query,
        string ticker,
        int pageSize)
    {
        query.Add(Pair("ticker", ticker));
        AddPage(query, pageSize);
    }

    private static void AddPage(
        ICollection<KeyValuePair<string, string>> query,
        int pageSize)
    {
        ValidatePage(1, pageSize);
        query.Add(Pair("page", "1"));
        query.Add(Pair(
            "page_size",
            pageSize.ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddCompactRange(
        ICollection<KeyValuePair<string, string>> query,
        DateTime? from,
        DateTime? to)
    {
        if (from is not null)
        {
            query.Add(Pair(
                "date_from",
                QuiverQuantExtensions.FormatCompactDate(
                    from.Value)));
        }
        if (to is not null)
        {
            query.Add(Pair(
                "date_to",
                QuiverQuantExtensions.FormatCompactDate(
                    to.Value)));
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

    private static string Bool(bool value)
        => value ? "true" : "false";

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
                "Quiver Quantitative address must be an absolute HTTPS URI.",
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
