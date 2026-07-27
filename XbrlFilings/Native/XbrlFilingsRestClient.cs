namespace StockSharp.XbrlFilings.Native;

sealed class XbrlFilingsRestClient :
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
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public XbrlFilingsRestClient(
        Uri address,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.api+json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-XbrlFilings/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<XbrlJsonApiDocument<XbrlEntity>> SearchEntities(
        string value,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(pageNumber, pageSize);
        var query = PageQuery(pageNumber, pageSize);
        value = value?.Trim();
        if (!value.IsEmpty())
        {
            query[value.IsEntityIdentifier()
                ? "filter[identifier]"
                : "filter[name]"] = value;
        }

        return GetJson<XbrlJsonApiDocument<XbrlEntity>>(
            BuildAddress("entities", query),
            "entity lookup",
            cancellationToken);
    }

    public Task<XbrlJsonApiDocument<XbrlFiling>> GetFilings(
        string entityIdentifier,
        string country,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(pageNumber, pageSize);
        entityIdentifier = entityIdentifier?.Trim();
        if (!entityIdentifier.IsEmpty() &&
            !entityIdentifier.IsEntityIdentifier())
        {
            throw new ArgumentException(
                "filings.xbrl.org entity identifier is invalid.",
                nameof(entityIdentifier));
        }

        country = country?.Trim().ToUpperInvariant();
        if (!country.IsEmpty() &&
            (country.Length != 2 ||
                country.Any(character =>
                    !char.IsAsciiLetterUpper(character))))
        {
            throw new ArgumentException(
                "filings.xbrl.org country must be an ISO 3166-1 alpha-2 code.",
                nameof(country));
        }

        var query = PageQuery(pageNumber, pageSize);
        query["include"] = "entity";
        query["sort"] = "-processed";
        if (!country.IsEmpty())
            query["filter[country]"] = country;

        var path = entityIdentifier.IsEmpty()
            ? "filings"
            : $"entities/{Uri.EscapeDataString(entityIdentifier)}/filings";
        return GetJson<XbrlJsonApiDocument<XbrlFiling>>(
            BuildAddress(path, query),
            "filing lookup",
            cancellationToken);
    }

    private async Task<T> GetJson<T>(
        Uri address,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, address);
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var payload = await ReadLimited(
                    response.Content,
                    operation,
                    cancellationToken);

                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateApiException(
                        response.StatusCode,
                        payload,
                        operation);
                }

                var result = Deserialize<T>(payload, operation);
                if (result is XbrlJsonApiDocument<XbrlEntity>
                    entityDocument)
                {
                    ThrowJsonApiErrors(
                        entityDocument.Errors, operation);
                    entityDocument.Data ??= [];
                    entityDocument.Included ??= [];
                }
                else if (result is
                    XbrlJsonApiDocument<XbrlFiling>
                    filingDocument)
                {
                    ThrowJsonApiErrors(
                        filingDocument.Errors, operation);
                    filingDocument.Data ??= [];
                    filingDocument.Included ??= [];
                }

                return result;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"filings.xbrl.org {operation} transport " +
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
                        $"filings.xbrl.org {operation} request timed " +
                        "out after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"filings.xbrl.org {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var resource = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));
        var queryString = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}=" +
                Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(resource)
        {
            Query = queryString,
        }.Uri;
    }

    private static Dictionary<string, string> PageQuery(
        int pageNumber,
        int pageSize)
        => new()
        {
            ["page[number]"] = pageNumber.ToString(
                CultureInfo.InvariantCulture),
            ["page[size]"] = pageSize.ToString(
                CultureInfo.InvariantCulture),
        };

    private static void ValidatePage(
        int pageNumber,
        int pageSize)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber), pageNumber,
                "JSON:API page number must be positive.");
        }
        if (pageSize is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize), pageSize,
                "JSON:API page size must be from 1 to 200.");
        }
    }

    private static T Deserialize<T>(
        byte[] payload,
        string operation)
    {
        try
        {
            var value = JsonConvert.DeserializeObject<T>(
                Encoding.UTF8.GetString(payload),
                _jsonSettings);
            return value is null
                ? throw new InvalidOperationException(
                    $"filings.xbrl.org returned an empty {operation} payload.")
                : value;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"filings.xbrl.org returned invalid JSON for {operation}.");
        }
    }

    private static void ThrowJsonApiErrors(
        XbrlJsonApiError[] errors,
        string operation)
    {
        var error = errors?.FirstOrDefault();
        if (error is null)
            return;

        var message = error.Detail
            .IsEmpty(error.Title)
            .IsEmpty("unknown JSON:API error");
        throw new XbrlFilingsApiException(
            null,
            error.Code.IsEmpty(error.Status),
            $"filings.xbrl.org {operation} failed" +
            FormatCode(error.Code.IsEmpty(error.Status)) +
            $": {Sanitize(message)}");
    }

    private static XbrlFilingsApiException CreateApiException(
        HttpStatusCode statusCode,
        byte[] payload,
        string operation)
    {
        XbrlJsonApiError error = null;
        try
        {
            error = JsonConvert
                .DeserializeObject<
                    XbrlJsonApiDocument<XbrlEntity>>(
                    Encoding.UTF8.GetString(payload),
                    _jsonSettings)
                ?.Errors
                ?.FirstOrDefault();
        }
        catch (JsonException)
        {
        }

        var code = error?.Code
            .IsEmpty(error?.Status);
        var message = error?.Detail
            .IsEmpty(error?.Title)
            .IsEmpty(GetErrorMessage(payload));
        return new XbrlFilingsApiException(
            statusCode,
            code,
            $"filings.xbrl.org {operation} request failed " +
            $"({(int)statusCode} {statusCode}" +
            FormatCode(code) +
            $"): {Sanitize(message)}");
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
                $"filings.xbrl.org {operation} response exceeds " +
                "the 64 MB safety limit.");
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
                    $"filings.xbrl.org {operation} response exceeds " +
                    "the 64 MB safety limit.");
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
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is null &&
            response.Headers.RetryAfter?.Date is not null)
        {
            delay = response.Headers.RetryAfter.Date.Value -
                DateTimeOffset.UtcNow;
        }

        if (delay is not null && delay.Value > TimeSpan.Zero)
        {
            return delay > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : delay.Value;
        }
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";
        var body = Encoding.UTF8.GetString(payload);
        return body.Length > 2000 ? body[..2000] : body;
    }

    private static string Sanitize(string value)
    {
        if (value.IsEmpty())
            return "unknown error";
        return new string(value
            .Take(2000)
            .Select(character =>
                char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t'
                    ? ' '
                    : character)
            .ToArray())
            .Trim();
    }

    private static string FormatCode(string code)
        => code.IsEmpty() ? string.Empty : $", API {code}";

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "filings.xbrl.org address must be an absolute HTTPS URI.",
                nameof(address));
        }

        var value = address.AbsoluteUri;
        return value.EndsWith('/')
            ? address
            : new Uri(value + "/");
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        base.DisposeManaged();
    }
}
