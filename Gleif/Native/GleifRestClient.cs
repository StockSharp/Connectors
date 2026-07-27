namespace StockSharp.Gleif.Native;

sealed class GleifRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _payloadLimit = 64 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.DateTimeOffset,
    };

    private readonly Uri _address;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GleifRestClient(
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
            "User-Agent", "StockSharp-Gleif/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<GleifDocument<GleifLeiRecord>> Search(
        string value,
        bool activeOnly,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(pageNumber, pageSize);
        value = value?.Trim();
        if (value.IsEmpty())
            throw new ArgumentNullException(nameof(value));

        var query = new Dictionary<string, string>
        {
            [value.IsLei()
                ? "filter[lei]"
                : value.IsIsin()
                    ? "filter[isin]"
                    : "filter[fulltext]"] = value,
            ["page[number]"] = pageNumber.ToString(
                CultureInfo.InvariantCulture),
            ["page[size]"] = pageSize.ToString(
                CultureInfo.InvariantCulture),
        };
        if (activeOnly)
            query["filter[entity.status]"] = "ACTIVE";

        return Get<GleifLeiRecord>(
            "lei-records",
            query,
            "LEI lookup",
            cancellationToken);
    }

    public Task<GleifDocument<GleifIsin>> GetIsins(
        string lei,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(pageNumber, pageSize);
        if (!lei.IsLei())
        {
            throw new ArgumentException(
                "GLEIF ISIN lookup requires a valid LEI.",
                nameof(lei));
        }

        return Get<GleifIsin>(
            $"lei-records/{lei.Trim().ToUpperInvariant()}/isins",
            new Dictionary<string, string>
            {
                ["page[number]"] = pageNumber.ToString(
                    CultureInfo.InvariantCulture),
                ["page[size]"] = pageSize.ToString(
                    CultureInfo.InvariantCulture),
            },
            "ISIN mapping",
            cancellationToken);
    }

    private async Task<GleifDocument<T>> Get<T>(
        string path,
        IReadOnlyDictionary<string, string> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var address = BuildAddress(path, query);
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
                if (IsTransient(response.StatusCode) && attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }

                var document = Deserialize<T>(payload, operation);
                var error = document.Errors?.FirstOrDefault();
                if (!response.IsSuccessStatusCode || error is not null)
                {
                    var status = error?.Status;
                    var message = error?.Detail
                        .IsEmpty(error?.Title)
                        .IsEmpty(GetErrorMessage(payload));
                    throw new GleifApiException(
                        response.StatusCode,
                        status,
                        $"GLEIF {operation} request failed " +
                        $"({(int)response.StatusCode} {response.StatusCode}" +
                        (status.IsEmpty()
                            ? string.Empty
                            : $", API {status}") +
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
                        $"GLEIF {operation} transport request " +
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
                    throw new TimeoutException(
                        $"GLEIF {operation} timed out after four attempts.");
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"GLEIF {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var resource = new Uri(_address, path);
        return new UriBuilder(resource)
        {
            Query = string.Join(
                "&",
                query.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}=" +
                    Uri.EscapeDataString(pair.Value))),
        }.Uri;
    }

    private static GleifDocument<T> Deserialize<T>(
        byte[] payload,
        string operation)
    {
        try
        {
            return JsonConvert.DeserializeObject<GleifDocument<T>>(
                Encoding.UTF8.GetString(payload),
                _jsonSettings) ??
                throw new InvalidOperationException(
                    $"GLEIF returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"GLEIF returned invalid JSON for {operation}.");
        }
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
    }

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > _payloadLimit)
            throw new InvalidOperationException(
                $"GLEIF {operation} response exceeds 64 MB.");

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
                throw new InvalidOperationException(
                    $"GLEIF {operation} response exceeds 64 MB.");
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

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";
        var value = Encoding.UTF8.GetString(payload);
        return value.Length > 2000 ? value[..2000] : value;
    }

    private static string Sanitize(string value)
        => value.IsEmpty()
            ? "unknown error"
            : new string(value
                .Take(2000)
                .Select(character =>
                    char.IsControl(character) ? ' ' : character)
                .ToArray())
                .Trim();

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException(
                "GLEIF address must be an absolute HTTPS URI.",
                nameof(address));
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
