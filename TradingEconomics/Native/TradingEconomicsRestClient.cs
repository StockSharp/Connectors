namespace StockSharp.TradingEconomics.Native;

sealed class TradingEconomicsRestClient :
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

    public TradingEconomicsRestClient(
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
            "Authorization", _token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-TradingEconomics/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<IReadOnlyList<TradingEconomicsMarket>> Search(
        string term,
        CancellationToken cancellationToken)
    {
        term = term?.Trim().ThrowIfEmpty(nameof(term));
        return GetArray<TradingEconomicsMarket>(
            $"markets/search/{EscapePath(term)}",
            [Pair("category", "index,markets")],
            "market search",
            cancellationToken);
    }

    public Task<IReadOnlyList<TradingEconomicsMarket>> GetQuote(
        string symbol,
        CancellationToken cancellationToken)
        => GetArray<TradingEconomicsMarket>(
            $"markets/symbol/{EscapePath(NormalizeSymbol(symbol))}",
            [],
            "market quote",
            cancellationToken);

    public Task<IReadOnlyList<TradingEconomicsBar>> GetBars(
        string symbol,
        TimeSpan timeFrame,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        var (interval, isDaily) =
            timeFrame.ToTradingEconomicsInterval();
        var path = isDaily
            ? $"markets/historical/{EscapePath(NormalizeSymbol(symbol))}"
            : $"markets/intraday/{EscapePath(NormalizeSymbol(symbol))}";
        var query = new List<KeyValuePair<string, string>>
        {
            Pair("d1", FormatDate(from, !isDaily)),
            Pair("d2", FormatDate(to, !isDaily)),
        };
        if (!isDaily)
            query.Add(Pair("agr", interval));
        return GetArray<TradingEconomicsBar>(
            path,
            query,
            isDaily
                ? "daily market history"
                : "intraday market history",
            cancellationToken);
    }

    public Task<IReadOnlyList<TradingEconomicsArticle>> GetNews(
        string symbol,
        CancellationToken cancellationToken)
        => GetArray<TradingEconomicsArticle>(
            symbol.IsEmpty()
                ? "news"
                : $"news/ticker/{EscapePath(NormalizeSymbol(symbol))}",
            [],
            symbol.IsEmpty()
                ? "latest news"
                : "ticker news",
            cancellationToken);

    public async Task<TradingEconomicsRawResponse> GetDataset(
        TradingEconomicsDataKinds kind,
        string symbol,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        symbol = NormalizeSymbol(symbol);
        string resource;
        var query = new List<KeyValuePair<string, string>>();
        switch (kind)
        {
            case TradingEconomicsDataKinds.Financials:
                resource =
                    $"financials/symbol/{EscapePath(symbol)}";
                break;

            case TradingEconomicsDataKinds.Earnings:
                resource =
                    $"earnings-revenues/symbol/{EscapePath(symbol)}";
                if (from is not null)
                    query.Add(Pair("d1", FormatDate(from.Value, false)));
                if (to is not null)
                    query.Add(Pair("d2", FormatDate(to.Value, false)));
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null);
        }

        var token = await GetToken(
            resource,
            query,
            kind.ToString(),
            cancellationToken);
        return new TradingEconomicsRawResponse(
            resource,
            token.ToString(Formatting.None));
    }

    private async Task<IReadOnlyList<T>> GetArray<T>(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var token = await GetToken(
            path, query, operation, cancellationToken);
        if (token is not JArray array)
        {
            throw new InvalidOperationException(
                $"Trading Economics returned an invalid {operation} schema.");
        }
        try
        {
            return array.ToObject<List<T>>(
                JsonSerializer.Create(_jsonSettings)) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Trading Economics returned an invalid {operation} schema.");
        }
    }

    private async Task<JToken> GetToken(
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

                var text = Encoding.UTF8.GetString(payload);
                if (!response.IsSuccessStatusCode)
                {
                    throw new TradingEconomicsApiException(
                        response.StatusCode,
                        $"Trading Economics {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): {Sanitize(GetError(text))}");
                }

                var token = Parse(text, operation);
                var error = FindError(token);
                if (!error.IsEmpty())
                {
                    throw new TradingEconomicsApiException(
                        response.StatusCode,
                        $"Trading Economics {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): {Sanitize(error)}");
                }
                return token;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Trading Economics {operation} transport " +
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
                        $"Trading Economics {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Trading Economics {operation} exhausted its retry limit.");
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

    private static JToken Parse(
        string payload,
        string operation)
    {
        if (payload.IsEmpty())
            return new JArray();
        try
        {
            return JToken.Parse(payload);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Trading Economics returned invalid JSON for {operation}.");
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
                $"Trading Economics {operation} response exceeds 64 MB.");
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
                    $"Trading Economics {operation} response exceeds 64 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static string FindError(JToken token)
    {
        if (token is JObject obj)
        {
            return GetString(obj["error"])
                .IsEmpty(GetString(obj["Error"]))
                .IsEmpty(GetString(obj["message"]))
                .IsEmpty(GetString(obj["Message"]));
        }
        if (token is JArray { Count: 1 } array &&
            array[0] is JObject item)
        {
            return GetString(item["error"])
                .IsEmpty(GetString(item["Error"]));
        }
        return null;
    }

    private static string GetError(string payload)
    {
        if (payload.IsEmpty())
            return "empty error response";
        try
        {
            return FindError(JToken.Parse(payload))
                .IsEmpty(payload);
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static string GetString(JToken token)
        => token switch
        {
            null => null,
            JValue value => value.ToString(
                CultureInfo.InvariantCulture),
            _ => token.ToString(Formatting.None),
        };

    private static string NormalizeSymbol(string symbol)
        => TradingEconomicsExtensions.NormalizeSymbol(symbol);

    private static string EscapePath(string value)
        => Uri.EscapeDataString(value);

    private static string FormatDate(
        DateTime value,
        bool includeTime)
        => TradingEconomicsExtensions.ToUtcSafe(value).ToString(
            includeTime
                ? "yyyy-MM-dd HH:mm"
                : "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);

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
                "Trading Economics address must be an absolute HTTPS URI.",
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
