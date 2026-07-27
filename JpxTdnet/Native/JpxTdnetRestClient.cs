namespace StockSharp.JpxTdnet.Native;

sealed class JpxTdnetApiException : InvalidOperationException
{
    public JpxTdnetApiException(string status, string message)
        : base(message)
    {
        Status = status;
    }

    public string Status { get; }
}

sealed class JpxTdnetRestClient : BaseLogReceiver, IDisposable
{
    private const int _indexLimit = 32 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Include,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _apiKey;
    private readonly int _documentLimit;
    private readonly int _encodedDocumentLimit;
    private readonly TimeSpan _requestInterval;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private DateTimeOffset _lastApiRequest;

    public JpxTdnetRestClient(
        Uri address,
        string apiKey,
        int maxDocumentSizeMb,
        TimeSpan requestInterval,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null,
        Func<DateTimeOffset> utcNow = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
        if (maxDocumentSizeMb is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDocumentSizeMb));
        }
        if (requestInterval < TimeSpan.Zero ||
            requestInterval > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestInterval));
        }

        _documentLimit =
            checked(maxDocumentSizeMb * 1024 * 1024);
        _encodedDocumentLimit = checked(
            (int)Math.Min(
                int.MaxValue,
                (long)_documentLimit * 4 / 3 +
                1024 * 1024));
        _requestInterval = requestInterval;
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-JpxTdnet/1.0");
        _delay = delay ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<JpxTdnetIndexResponse> GetIndex(
        string code,
        DateTime? from,
        DateTime? to,
        JpxTdnetIndexModes mode,
        CancellationToken cancellationToken)
    {
        code = code?.Trim().ToUpperInvariant();
        if (!code.IsEmpty() && !code.IsTdnetCode())
        {
            throw new ArgumentException(
                "JPX TDnet stock code must contain four or five alphanumeric characters.",
                nameof(code));
        }
        if (from.HasValue != to.HasValue)
        {
            throw new ArgumentException(
                "JPX TDnet index dates must be supplied together.");
        }
        if (code.IsEmpty() && from is null)
        {
            throw new ArgumentException(
                "JPX TDnet index request requires a stock code or date range.");
        }
        if (from is DateTime start && to is DateTime end)
        {
            if (start.Date > end.Date)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(from), start,
                    "JPX TDnet start date is after its end date.");
            }
            if (end.Date > start.Date.AddMonths(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(to), end,
                    "JPX TDnet index range cannot exceed one month.");
            }
        }

        var request = new JpxTdnetIndexRequest
        {
            AccessKey = _apiKey,
            Code = code,
            DateFrom = from?.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTo = to?.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            EditDeleteFlag = mode.ToApiCode(),
        };
        var result = await PostJson<JpxTdnetIndexResponse>(
            "tdlist",
            request,
            _indexLimit,
            "index",
            cancellationToken);
        EnsureStatus(
            result,
            "index",
            allowPartial: true);
        result.Items ??= [];
        return result;
    }

    public async Task<byte[]> DownloadDocument(
        string disclosureNumber,
        JpxTdnetDocumentFormats format,
        CancellationToken cancellationToken)
    {
        ValidateDisclosureNumber(disclosureNumber);
        var fileType = format.ToApiCode();
        var operation =
            $"document '{disclosureNumber}' ({fileType})";
        var result =
            await PostJson<JpxTdnetDocumentResponse>(
                "tdfile",
                new JpxTdnetDocumentRequest
                {
                    AccessKey = _apiKey,
                    DisclosureNumber = disclosureNumber,
                    FileTypeFlag = fileType,
                },
                _encodedDocumentLimit,
                operation,
                cancellationToken);
        EnsureStatus(result, operation, allowPartial: false);

        byte[] document;
        if (result.ResponseType == "1")
        {
            document = DecodeBase64(
                result.FileData, operation);
        }
        else if (result.ResponseType == "2")
        {
            var fileAddress = ValidateFileAddress(
                result.FileUrl);
            var payload = await GetExternal(
                fileAddress,
                _encodedDocumentLimit,
                operation,
                cancellationToken);
            document = IsExpectedDocument(payload, format)
                ? payload
                : DecodeBase64(
                    Encoding.UTF8.GetString(payload)
                        .Trim()
                        .TrimStart('\uFEFF')
                        .Trim('"'),
                    operation);
        }
        else
        {
            throw new InvalidOperationException(
                $"JPX TDnet returned unsupported response type '{result.ResponseType}' for {operation}.");
        }

        if (!IsExpectedDocument(document, format))
        {
            throw new InvalidOperationException(
                $"JPX TDnet returned an invalid {(format == JpxTdnetDocumentFormats.Xbrl ? "ZIP" : "PDF")} payload for {operation}.");
        }

        return document;
    }

    private async Task<T> PostJson<T>(
        string path,
        object body,
        int maxBytes,
        string operation,
        CancellationToken cancellationToken)
        where T : JpxTdnetResponse
    {
        var address = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));
        var json = JsonConvert.SerializeObject(
            body, _jsonSettings);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await WaitForApiSlot(cancellationToken);

                using var request = new HttpRequestMessage(
                    HttpMethod.Post, address);
                request.Headers.TryAddWithoutValidation(
                    "x-api-key", _apiKey);
                request.Content = new StringContent(
                    json, Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }

                var payload = await ReadLimited(
                    response.Content,
                    maxBytes,
                    operation,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var apiError = ParseApiError(
                        payload, operation);
                    if (apiError is not null)
                        throw apiError;

                    throw new HttpRequestException(
                        $"JPX TDnet {operation} request failed " +
                        $"({(int)response.StatusCode} {response.StatusCode}): " +
                        Sanitize(GetErrorMessage(payload)),
                        null,
                        response.StatusCode);
                }

                T result;
                try
                {
                    result = JsonConvert.DeserializeObject<T>(
                        Encoding.UTF8.GetString(payload),
                        _jsonSettings);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException(
                        $"JPX TDnet returned invalid JSON for {operation}.");
                }

                return result ??
                    throw new InvalidOperationException(
                        $"JPX TDnet returned an empty response for {operation}.");
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"JPX TDnet {operation} transport request failed after four attempts.");
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
                        $"JPX TDnet {operation} request timed out after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"JPX TDnet {operation} request exhausted its retry limit.");
    }

    private async Task<byte[]> GetExternal(
        Uri address,
        int maxBytes,
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

                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }

                var payload = await ReadLimited(
                    response.Content,
                    maxBytes,
                    operation,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"JPX TDnet document-file request failed " +
                        $"({(int)response.StatusCode} {response.StatusCode}).",
                        null,
                        response.StatusCode);
                }

                return payload;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        "JPX TDnet document-file transport request failed after four attempts.");
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
                        "JPX TDnet document-file request timed out after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            "JPX TDnet document-file request exhausted its retry limit.");
    }

    private async Task WaitForApiSlot(
        CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var now = _utcNow();
            var remaining =
                _requestInterval - (now - _lastApiRequest);
            if (_lastApiRequest != default &&
                remaining > TimeSpan.Zero)
            {
                await _delay(remaining, cancellationToken);
            }

            _lastApiRequest = _utcNow();
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private byte[] DecodeBase64(
        string value,
        string operation)
    {
        if (value.IsEmpty())
        {
            throw new InvalidOperationException(
                $"JPX TDnet returned empty base64 data for {operation}.");
        }

        var characters = value.Count(
            character => !char.IsWhiteSpace(character));
        var estimatedSize = (long)characters / 4 * 3 + 3;
        if (estimatedSize > _documentLimit)
        {
            throw new InvalidOperationException(
                $"JPX TDnet {operation} exceeds the configured " +
                $"{_documentLimit / (1024 * 1024)} MB limit.");
        }

        byte[] result;
        try
        {
            result = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"JPX TDnet returned invalid base64 data for {operation}.",
                ex);
        }

        if (result.Length > _documentLimit)
        {
            throw new InvalidOperationException(
                $"JPX TDnet {operation} exceeds the configured " +
                $"{_documentLimit / (1024 * 1024)} MB limit.");
        }

        return result;
    }

    private void EnsureStatus(
        JpxTdnetResponse response,
        string operation,
        bool allowPartial)
    {
        if (response.StatusCode == "200" ||
            (allowPartial && response.StatusCode == "206"))
        {
            return;
        }

        throw new JpxTdnetApiException(
            response.StatusCode,
            $"JPX TDnet {operation} request failed " +
            $"({response.StatusCode.IsEmpty("unknown")}): " +
            Sanitize(
                response.Message.IsEmpty(
                    "No error description.")));
    }

    private JpxTdnetApiException ParseApiError(
        byte[] payload,
        string operation)
    {
        if (payload is null || payload.Length == 0)
            return null;

        try
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(payload));
            var status = json["statusCode"]?.ToString();
            var message = json["message"]?.ToString();
            return status.IsEmpty() && message.IsEmpty()
                ? null
                : new JpxTdnetApiException(
                    status,
                    $"JPX TDnet {operation} request failed " +
                    $"({status.IsEmpty("unknown")}): " +
                    Sanitize(
                        message.IsEmpty(
                            "No error description.")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        int maxBytes,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > maxBytes)
        {
            throw new InvalidOperationException(
                $"JPX TDnet {operation} response exceeds its safety limit.");
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
            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"JPX TDnet {operation} response exceeds its safety limit.");
            }

            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }

        return target.ToArray();
    }

    private static bool IsExpectedDocument(
        byte[] payload,
        JpxTdnetDocumentFormats format)
        => format == JpxTdnetDocumentFormats.Xbrl
            ? payload is { Length: >= 2 } &&
                payload[0] == (byte)'P' &&
                payload[1] == (byte)'K'
            : HasPrefix(payload, "%PDF");

    private static bool HasPrefix(
        byte[] payload,
        string value)
        => payload is not null &&
            payload.Length >= value.Length &&
            Encoding.ASCII.GetString(
                payload, 0, value.Length) == value;

    private static Uri ValidateFileAddress(string value)
    {
        if (!Uri.TryCreate(
                value, UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps ||
            !(address.Host.Equals(
                    "amazonaws.com",
                    StringComparison.OrdinalIgnoreCase) ||
              address.Host.EndsWith(
                    ".amazonaws.com",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "JPX TDnet returned an invalid Amazon S3 document URL.");
        }

        return address;
    }

    private static void ValidateDisclosureNumber(string value)
    {
        if (value?.Length != 14 ||
            !value.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "JPX TDnet disclosure number must contain 14 digits.",
                nameof(value));
        }
    }

    private string Sanitize(string value)
        => value.IsEmpty()
            ? value
            : value.Replace(
                _apiKey,
                "[redacted]",
                StringComparison.Ordinal);

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";

        var body = Encoding.UTF8.GetString(payload);
        if (body.Length > 2000)
            body = body[..2000];

        try
        {
            var json = JObject.Parse(body);
            return json["message"]?.ToString().IsEmpty(body);
        }
        catch (JsonException)
        {
            return body;
        }
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

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "JPX TDnet API address must be an absolute HTTPS URI.",
                nameof(address));
        }

        var value = address.AbsoluteUri;
        return value.EndsWith('/')
            ? address
            : new Uri(value + "/");
    }

    protected override void DisposeManaged()
    {
        _requestLock.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }
}
