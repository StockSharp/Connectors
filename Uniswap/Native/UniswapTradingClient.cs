namespace StockSharp.Uniswap.Native;

sealed class UniswapTradingClient : BaseLogReceiver
{
    private const int _maximumResponseBytes = 16 * 1024 * 1024;
    private const int _maximumAttempts = 4;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly string _routerVersion;
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private DateTime _nextSend;

    public UniswapTradingClient(string endpoint, SecureString apiKey,
        UniswapRouterVersions routerVersion)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim()
            .TrimEnd('/') + "/";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
            !_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Uniswap Trading API endpoint must be an absolute HTTPS URI.",
                nameof(endpoint));
        _apiKey = apiKey.IsEmpty()
            ? throw new ArgumentException(
                "A Uniswap Developer Platform API key is required.",
                nameof(apiKey))
            : apiKey.UnSecure().Trim();
        _routerVersion = routerVersion.ToWire();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Uniswap-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "Uniswap_TradingAPI";

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<UniswapQuoteResponse> GetQuoteAsync(
        UniswapQuoteRequest request, CancellationToken cancellationToken)
        => SendAsync<UniswapQuoteRequest, UniswapQuoteResponse>("quote",
            request, cancellationToken);

    public ValueTask<UniswapApprovalResponse> CheckApprovalAsync(
        UniswapApprovalRequest request,
        CancellationToken cancellationToken)
        => SendAsync<UniswapApprovalRequest, UniswapApprovalResponse>(
            "check_approval", request, cancellationToken);

    public ValueTask<UniswapCreateSwapResponse> CreateSwapAsync(
        UniswapCreateSwapRequest request,
        CancellationToken cancellationToken)
        => SendAsync<UniswapCreateSwapRequest, UniswapCreateSwapResponse>(
            "swap", request, cancellationToken);

    private async ValueTask<TResponse> SendAsync<TRequest, TResponse>(
        string path, TRequest payload, CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        var body = JsonConvert.SerializeObject(payload, _jsonSettings);
        for (var attempt = 0; ; attempt++)
        {
            await WaitForSendAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(_endpoint, path));
            request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
            request.Headers.TryAddWithoutValidation(
                "x-universal-router-version", _routerVersion);
            request.Headers.TryAddWithoutValidation(
                "x-permit2-disabled", "true");
            request.Content = new StringContent(body, Encoding.UTF8,
                "application/json");
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await ReadBodyAsync(response.Content,
                cancellationToken);
            if (attempt < _maximumAttempts - 1 &&
                IsTransient(response.StatusCode))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw CreateError(response.StatusCode, responseBody);
            try
            {
                return JsonConvert.DeserializeObject<TResponse>(responseBody,
                    _jsonSettings) ?? throw new InvalidDataException(
                    "Uniswap Trading API returned an empty response.");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    "Uniswap Trading API returned an unexpected response " +
                    "shape.", error);
            }
        }
    }

    private async ValueTask WaitForSendAsync(
        CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextSend - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextSend = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private static UniswapApiException CreateError(HttpStatusCode status,
        string body)
    {
        UniswapApiError error = null;
        try
        {
            if (!body.IsEmpty())
                error = JsonConvert.DeserializeObject<UniswapApiError>(body);
        }
        catch (JsonException)
        {
        }
        var message = error?.Message;
        if (message.IsEmpty())
            message = error?.Error;
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new(status,
            $"Uniswap Trading API HTTP {(int)status}: {message}");
    }

    private static bool IsTransient(HttpStatusCode status)
        => status == (HttpStatusCode)429 || (int)status >= 500;

    private static async ValueTask DelayRetryAsync(
        HttpResponseMessage response, int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date?.UtcDateTime -
                DateTime.UtcNow) ??
            TimeSpan.FromMilliseconds(500 * (1 << attempt));
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromSeconds(10))
            delay = TimeSpan.FromSeconds(10);
        await Task.Delay(delay, cancellationToken);
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "Uniswap response exceeds the 16 MiB safety limit.");
        await using var stream = await content.ReadAsStreamAsync(
            cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(block, cancellationToken);
            if (read == 0)
                break;
            if (buffer.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "Uniswap response exceeds the 16 MiB safety limit.");
            buffer.Write(block, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
