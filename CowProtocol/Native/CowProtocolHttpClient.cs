namespace StockSharp.CowProtocol.Native;

sealed class CowProtocolHttpClient : BaseLogReceiver
{
    private const int _maximumResponseBytes = 8 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private DateTime _nextRequest;

    public CowProtocolHttpClient(string endpoint)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
            "/";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
            !(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
                _endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
            throw new ArgumentException(
                "CoW Protocol API endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-CoW-Protocol-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "CoW_Protocol_REST";

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _requestGate.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask VerifyAsync(CancellationToken cancellationToken)
    {
        var version = await SendTextAsync(HttpMethod.Get, "api/v1/version",
            null, true, cancellationToken);
        if (version.IsEmpty())
            throw new InvalidDataException(
                "CoW Protocol API returned an empty version.");
    }

    public ValueTask<CowProtocolQuoteResponse> GetQuoteAsync(
        CowProtocolQuoteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendJsonAsync<CowProtocolQuoteRequest,
            CowProtocolQuoteResponse>(HttpMethod.Post, "api/v1/quote", request,
            true, cancellationToken);
    }

    public async ValueTask<string> CreateOrderAsync(
        CowProtocolOrderCreation request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uid = await SendJsonAsync<CowProtocolOrderCreation, string>(
            HttpMethod.Post, "api/v1/orders", request, false,
            cancellationToken);
        return uid.NormalizeOrderUid();
    }

    public async ValueTask<CowProtocolOrder> GetOrderAsync(string uid,
        CancellationToken cancellationToken)
    {
        uid = uid.NormalizeOrderUid();
        try
        {
            return await SendJsonAsync<CowProtocolOrder>(HttpMethod.Get,
                $"api/v1/orders/{Uri.EscapeDataString(uid)}", true,
                cancellationToken);
        }
        catch (CowProtocolApiException error) when (
            error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public ValueTask<CowProtocolOrder[]> GetOrdersAsync(string owner,
        int offset, int limit, CancellationToken cancellationToken)
    {
        owner = owner.NormalizeAddress();
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return SendJsonAsync<CowProtocolOrder[]>(HttpMethod.Get,
            $"api/v1/account/{Uri.EscapeDataString(owner)}/orders" +
            $"?offset={offset}&limit={limit}", true, cancellationToken);
    }

    public ValueTask<CowProtocolApiTrade[]> GetTradesAsync(string uid,
        CancellationToken cancellationToken)
    {
        uid = uid.NormalizeOrderUid();
        return SendJsonAsync<CowProtocolApiTrade[]>(HttpMethod.Get,
            "api/v2/trades?orderUid=" + Uri.EscapeDataString(uid) +
            "&offset=0&limit=1000", true, cancellationToken);
    }

    public async ValueTask CancelOrderAsync(string uid, string signature,
        CancellationToken cancellationToken)
    {
        uid = uid.NormalizeOrderUid();
        signature = signature.NormalizeSignature();
        var payload = JsonConvert.SerializeObject(
            new CowProtocolCancellationRequest
            {
                Signature = signature,
                SigningScheme = CowProtocolSigningSchemes.Eip712,
            }, _jsonSettings);
        _ = await SendTextAsync(HttpMethod.Delete,
            $"api/v1/orders/{Uri.EscapeDataString(uid)}", payload, false,
            cancellationToken);
    }

    private async ValueTask<TResult> SendJsonAsync<TResult>(HttpMethod method,
        string path, bool isRead, CancellationToken cancellationToken)
    {
        var body = await SendTextAsync(method, path, null, isRead,
            cancellationToken);
        return Deserialize<TResult>(body);
    }

    private async ValueTask<TResult> SendJsonAsync<TRequest, TResult>(
        HttpMethod method, string path, TRequest request, bool isRead,
        CancellationToken cancellationToken)
    {
        var payload = JsonConvert.SerializeObject(request, _jsonSettings);
        var body = await SendTextAsync(method, path, payload, isRead,
            cancellationToken);
        return Deserialize<TResult>(body);
    }

    private TResult Deserialize<TResult>(string body)
    {
        try
        {
            var result = JsonConvert.DeserializeObject<TResult>(body,
                _jsonSettings);
            return result is null
                ? throw new InvalidDataException(
                    "CoW Protocol API returned an empty JSON value.")
                : result;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "CoW Protocol API returned an unexpected response shape.",
                error);
        }
    }

    private async ValueTask<string> SendTextAsync(HttpMethod method,
        string path, string payload, bool isRead,
        CancellationToken cancellationToken)
    {
        path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
        for (var attempt = 0; ; attempt++)
        {
            await WaitForRequestAsync(cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(_endpoint, path));
            if (payload is not null)
                request.Content = new StringContent(payload, Encoding.UTF8,
                    "application/json");
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await ReadBodyAsync(response.Content,
                cancellationToken);
            if (isRead && attempt < 3 && (response.StatusCode ==
                    (HttpStatusCode)429 || (int)response.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(
                    250 * (1 << attempt)), cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw CreateApiException(response.StatusCode, body);
            return body;
        }
    }

    private CowProtocolApiException CreateApiException(
        HttpStatusCode statusCode, string body)
    {
        CowProtocolApiError error = null;
        try
        {
            error = JsonConvert.DeserializeObject<CowProtocolApiError>(body,
                _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var detail = error?.Description;
        if (error?.ErrorType.IsEmpty() == false)
            detail = $"{error.ErrorType}: {detail}";
        if (detail.IsEmpty())
            detail = body?.Trim().Truncate(512, string.Empty);
        if (detail.IsEmpty())
            detail = "request rejected";
        return new(statusCode,
            $"CoW Protocol HTTP {(int)statusCode}: {detail}");
    }

    private async ValueTask WaitForRequestAsync(
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequest - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "CoW Protocol response exceeds the 8 MiB safety limit.");
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var target = new MemoryStream();
        var block = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(block, cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "CoW Protocol response exceeds the 8 MiB safety limit.");
            target.Write(block, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
