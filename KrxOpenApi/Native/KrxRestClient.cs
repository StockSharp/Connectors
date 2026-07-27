namespace StockSharp.KrxOpenApi.Native;

sealed class KrxApiException : InvalidOperationException
{
    public KrxApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

sealed class KrxRestClient : BaseLogReceiver, IDisposable
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _authKey;
    private readonly HttpClient _http;

    public KrxRestClient(
        Uri address,
        string authKey,
        HttpMessageHandler handler = null)
    {
        _address = EnsureTrailingSlash(
            address ?? throw new ArgumentNullException(nameof(address)));
        _authKey = authKey.ThrowIfEmpty(nameof(authKey));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-KrxOpenApi/1.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "AUTH_KEY", _authKey);
    }

    public async Task<T[]> Get<T>(
        string path,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var address = new Uri(
            _address,
            $"{path.ThrowIfEmpty(nameof(path))}?basDd={date:yyyyMMdd}");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, address);
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (IsTransient(response.StatusCode) && attempt < 3)
            {
                await Task.Delay(
                    GetRetryDelay(response, attempt),
                    cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw CreateApiError(response.StatusCode, body, address);
            if (response.StatusCode == HttpStatusCode.NoContent ||
                body.IsEmpty())
            {
                return [];
            }

            var result = JsonConvert.DeserializeObject<KrxResponse<T>>(
                    body, _jsonSettings)
                ?? throw new InvalidOperationException(
                    $"KRX returned an empty response for '{address}'.");
            return result.Items ?? [];
        }

        throw new InvalidOperationException(
            $"KRX request '{address}' exhausted its retry limit.");
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is null && response.Headers.RetryAfter?.Date is not null)
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

    private static KrxApiException CreateApiError(
        HttpStatusCode statusCode,
        string body,
        Uri address)
    {
        string details = null;
        try
        {
            var json = JObject.Parse(body);
            details = (string)json["message"] ??
                (string)json["error"] ??
                (string)json["resultMsg"];
        }
        catch (JsonException)
        {
        }

        if (details.IsEmpty())
        {
            details = body?.Length > 2000
                ? body[..2000]
                : body;
        }

        return new KrxApiException(
            statusCode,
            $"KRX request '{address}' failed ({(int)statusCode} {statusCode}): {details}");
    }

    private static Uri EnsureTrailingSlash(Uri address)
    {
        if (!address.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "KRX API address must be absolute.", nameof(address));
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
