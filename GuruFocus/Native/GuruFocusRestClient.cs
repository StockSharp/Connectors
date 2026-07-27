namespace StockSharp.GuruFocus.Native;

sealed class GuruFocusRestClient :
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

    public GuruFocusRestClient(
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
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization", _token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-GuruFocus/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<GuruFocusPage<GuruFocusSecurity>> GetStocks(
        string regionCode,
        int page,
        int perPage,
        CancellationToken cancellationToken)
        => Get<GuruFocusPage<GuruFocusSecurity>>(
            $"stocks/{GuruFocusExtensions.NormalizeRegionCode(regionCode)}",
            PageQuery(page, perPage),
            "stock list",
            cancellationToken);

    public Task<GuruFocusPage<GuruFocusEtfSecurity>> GetEtfs(
        int page,
        int perPage,
        CancellationToken cancellationToken)
        => Get<GuruFocusPage<GuruFocusEtfSecurity>>(
            "etf/list",
            PageQuery(page, perPage),
            "ETF list",
            cancellationToken);

    public Task<GuruFocusProfile> GetProfile(
        string ticker,
        CancellationToken cancellationToken)
        => Get<GuruFocusProfile>(
            GuruFocusDataKinds.Profile.ToResource(ticker),
            [],
            "company profile",
            cancellationToken);

    public Task<GuruFocusEtfData> GetEtfData(
        string ticker,
        CancellationToken cancellationToken)
        => Get<GuruFocusEtfData>(
            GuruFocusDataKinds.EtfData.ToResource(ticker),
            [],
            "ETF data",
            cancellationToken);

    public async Task<GuruFocusSnapshotResult> GetSnapshot(
        string ticker,
        CancellationToken cancellationToken)
    {
        ticker = GuruFocusExtensions.ValidateTicker(ticker);
        try
        {
            var profile = await GetProfile(
                ticker, cancellationToken);
            if (profile?.Identity is not null)
            {
                return new GuruFocusSnapshotResult(
                    profile.Identity,
                    profile.Price,
                    SecurityTypes.Stock);
            }
        }
        catch (GuruFocusApiException ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }

        var etf = await GetEtfData(ticker, cancellationToken);
        return new GuruFocusSnapshotResult(
            etf?.BasicInformation,
            etf?.KeyStatistics,
            SecurityTypes.Etf);
    }

    public Task<List<GuruFocusPrice>> GetPrices(
        string ticker,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        return Get<List<GuruFocusPrice>>(
            $"stocks/{EscapeTicker(ticker)}/price",
            [
                Pair(
                    "start_date",
                    GuruFocusExtensions.FormatDate(from)),
                Pair(
                    "end_date",
                    GuruFocusExtensions.FormatDate(to)),
            ],
            "daily prices",
            cancellationToken);
    }

    public Task<GuruFocusNewsPage> GetNews(
        string ticker,
        int page,
        int perPage,
        CancellationToken cancellationToken)
        => Get<GuruFocusNewsPage>(
            $"stocks/{EscapeTicker(ticker)}/news",
            PageQuery(page, perPage),
            "stock news",
            cancellationToken);

    public Task<GuruFocusHeadlinesPage> GetHeadlines(
        int page,
        int perPage,
        CancellationToken cancellationToken)
        => Get<GuruFocusHeadlinesPage>(
            "headlines",
            PageQuery(page, perPage, 200),
            "market headlines",
            cancellationToken);

    public async Task<GuruFocusRawResponse> GetDataset(
        GuruFocusDataKinds kind,
        string ticker,
        int limit,
        DateTime? from,
        DateTime? to,
        string filingFormType,
        string guruActions,
        CancellationToken cancellationToken)
    {
        ticker = GuruFocusExtensions.ValidateTicker(ticker);
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var resource = kind.ToResource(ticker);
        var query = new List<KeyValuePair<string, string>>();
        switch (kind)
        {
            case GuruFocusDataKinds.Profile:
            case GuruFocusDataKinds.Fundamentals:
            case GuruFocusDataKinds.Valuations:
            case GuruFocusDataKinds.Rankings:
            case GuruFocusDataKinds.EtfData:
            case GuruFocusDataKinds.GuruHoldings:
                break;

            case GuruFocusDataKinds.SecFilings:
                query.Add(Pair(
                    "form_type",
                    filingFormType?.Trim()));
                AddDateRange(query, from, to);
                break;

            case GuruFocusDataKinds.InsiderTrades:
                query.AddRange(PageQuery(1, limit));
                break;

            case GuruFocusDataKinds.GuruTrades:
                query.Add(Pair("action", guruActions));
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
        return new GuruFocusRawResponse(
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
                    $"GuruFocus returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"GuruFocus returned an invalid {operation} schema.");
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
                var (apiCode, error) = FindError(token);
                if (!response.IsSuccessStatusCode ||
                    !error.IsEmpty())
                {
                    var detail = error
                        .IsEmpty(text)
                        .IsEmpty("empty error response");
                    throw new GuruFocusApiException(
                        response.StatusCode,
                        apiCode,
                        $"GuruFocus {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}" +
                        (apiCode.IsEmpty()
                            ? string.Empty
                            : $", API {apiCode}") +
                        $"): {Sanitize(detail)}");
                }
                if (token is null)
                {
                    throw new InvalidOperationException(
                        $"GuruFocus returned invalid JSON for {operation}.");
                }
                return token;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"GuruFocus {operation} transport request " +
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
                        $"GuruFocus {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"GuruFocus {operation} exhausted its retry limit.");
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

    private static (string Code, string Error) FindError(
        JToken token)
    {
        if (token is not JObject obj)
            return default;

        var code = GetString(obj["code"]);
        var error = GetString(obj["error"]);
        var message = GetString(obj["message"]);
        if (!error.IsEmpty())
            return (code, message.IsEmpty(error));
        if (obj["success"]?.Value<bool?>() == false ||
            (!code.IsEmpty() &&
                long.TryParse(
                    code,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var numericCode) &&
                numericCode >= 400))
        {
            return (code, message.IsEmpty("request failed"));
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
                $"GuruFocus {operation} response exceeds 128 MB.");
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
                    $"GuruFocus {operation} response exceeds 128 MB.");
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
                "start_date",
                GuruFocusExtensions.FormatDate(from.Value)));
        }
        if (to is not null)
        {
            query.Add(Pair(
                "end_date",
                GuruFocusExtensions.FormatDate(to.Value)));
        }
    }

    private static IEnumerable<KeyValuePair<string, string>>
        PageQuery(
            int page,
            int perPage,
            int maximum = 100)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (perPage < 1 || perPage > maximum)
            throw new ArgumentOutOfRangeException(nameof(perPage));
        return
        [
            Pair(
                "page",
                page.ToString(CultureInfo.InvariantCulture)),
            Pair(
                "per_page",
                perPage.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

    private static string EscapeTicker(string ticker)
        => Uri.EscapeDataString(
            GuruFocusExtensions.ValidateTicker(ticker));

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
                "GuruFocus address must be an absolute HTTPS URI.",
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
