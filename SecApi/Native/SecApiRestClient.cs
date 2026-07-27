namespace StockSharp.SecApi.Native;

sealed class SecApiRestClient :
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

    public SecApiRestClient(
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
            "User-Agent", "StockSharp-SecApi/1.0");
        _delay = delay ?? Task.Delay;
    }

    public async Task<IReadOnlyList<SecApiMapping>> GetMapping(
        string kind,
        string value,
        CancellationToken cancellationToken)
    {
        kind = kind?.Trim().ToLowerInvariant();
        if (kind is not (
            "cik" or "ticker" or "cusip" or "name" or
            "exchange" or "sector" or "industry"))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        value = value?.Trim().ThrowIfEmpty(nameof(value));
        var token = await SendToken(
            HttpMethod.Get,
            $"mapping/{kind}/{EscapePath(value)}",
            null,
            null,
            $"mapping by {kind}",
            cancellationToken);
        if (token is not JArray array)
        {
            throw new InvalidOperationException(
                "SEC-API.io returned an invalid mapping schema.");
        }
        try
        {
            return array.ToObject<List<SecApiMapping>>(
                JsonSerializer.Create(_jsonSettings)) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "SEC-API.io returned an invalid mapping schema.");
        }
    }

    public async Task<SecApiFilingResponse> SearchFilings(
        string query,
        int offset,
        int size,
        CancellationToken cancellationToken)
    {
        var token = await PostSearch(
            string.Empty,
            query,
            offset,
            size,
            "filing search",
            cancellationToken);
        try
        {
            return token.ToObject<SecApiFilingResponse>(
                JsonSerializer.Create(_jsonSettings)) ??
                throw new InvalidOperationException(
                    "SEC-API.io returned an empty filing search payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "SEC-API.io returned an invalid filing search schema.");
        }
    }

    public async Task<SecApiRawResponse> SearchRaw(
        string path,
        string query,
        int offset,
        int size,
        string operation,
        CancellationToken cancellationToken)
    {
        var token = await PostSearch(
            path,
            query,
            offset,
            size,
            operation,
            cancellationToken);
        return new SecApiRawResponse(
            path.IsEmpty("/") ,
            token.ToString(Formatting.None));
    }

    public async Task<SecApiRawResponse> GetXbrl(
        string accessionNumber,
        CancellationToken cancellationToken)
    {
        accessionNumber = accessionNumber?
            .Trim()
            .ThrowIfEmpty(nameof(accessionNumber));
        if (!SecApiExtensions.IsAccessionNumber(accessionNumber))
        {
            throw new ArgumentException(
                "SEC accession number is invalid.",
                nameof(accessionNumber));
        }
        const string path = "xbrl-to-json";
        var token = await SendToken(
            HttpMethod.Get,
            path,
            [Pair("accession-no", accessionNumber)],
            null,
            "XBRL conversion",
            cancellationToken);
        return new SecApiRawResponse(
            path,
            token.ToString(Formatting.None));
    }

    private Task<JToken> PostSearch(
        string path,
        string query,
        int offset,
        int size,
        string operation,
        CancellationToken cancellationToken)
    {
        query = query?.Trim().ThrowIfEmpty(nameof(query));
        if (query.Length > 3500)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (offset is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (size is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(size));

        var body = new JObject
        {
            ["query"] = query,
            ["from"] = offset,
            ["size"] = size,
            ["sort"] = new JArray
            {
                new JObject
                {
                    ["filedAt"] = new JObject
                    {
                        ["order"] = "desc",
                    },
                },
            },
        };
        return SendToken(
            HttpMethod.Post,
            path,
            null,
            body,
            operation,
            cancellationToken);
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
                    throw new SecApiApiException(
                        response.StatusCode,
                        $"SEC-API.io {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): {Sanitize(GetError(text))}");
                }

                var token = Parse(text, operation);
                var error = FindError(token);
                if (!error.IsEmpty())
                {
                    throw new SecApiApiException(
                        response.StatusCode,
                        $"SEC-API.io {operation} request failed " +
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
                        $"SEC-API.io {operation} transport request " +
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
                        $"SEC-API.io {operation} timed out after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"SEC-API.io {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IEnumerable<KeyValuePair<string, string>> query)
    {
        var resource = path.IsEmpty()
            ? _address
            : new Uri(_address, path);
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
            return new JObject();
        try
        {
            return JToken.Parse(payload);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"SEC-API.io returned invalid JSON for {operation}.");
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
                $"SEC-API.io {operation} response exceeds 128 MB.");
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
                    $"SEC-API.io {operation} response exceeds 128 MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static string FindError(JToken token)
    {
        if (token is not JObject obj)
            return null;
        var error = GetString(obj["error"]);
        if (!error.IsEmpty())
        {
            return GetString(obj["message"])
                .IsEmpty(error);
        }
        if (obj["status"]?.Value<int?>() is >= 400)
        {
            return GetString(obj["message"])
                .IsEmpty("request failed");
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

    private static string EscapePath(string value)
        => Uri.EscapeDataString(value);

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
                "SEC-API.io address must be an absolute HTTPS URI.",
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
