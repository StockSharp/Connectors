namespace StockSharp.OSL.Native;

sealed class OSLRestClient : BaseLogReceiver
{
    private sealed class RateGate : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private readonly TimeSpan _interval;
        private DateTime _nextRequest;

        public RateGate(TimeSpan interval) => _interval = interval;

        public async ValueTask WaitAsync(
            CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                var delay = _nextRequest - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                _nextRequest = DateTime.UtcNow + _interval;
            }
            finally
            {
                _sync.Release();
            }
        }

        public void Dispose() => _sync.Dispose();
    }

    private const int _maximumReadAttempts = 4;
    private const int _maximumResponseBytes = 8 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly string _passphrase;
    private readonly RateGate _rateGate = new(TimeSpan.FromMilliseconds(100));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };

    public OSLRestClient(string endpoint, SecureString key,
        SecureString secret, SecureString passphrase)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        _passphrase = passphrase.IsEmpty()
            ? string.Empty
            : passphrase.UnSecure();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "OSL API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-OSL-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "OSL_REST";

    public bool IsPrivateAvailable => !_apiKey.IsEmpty();

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _rateGate.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public ValueTask<OSLSymbol[]> GetSymbolsAsync(string symbol,
        CancellationToken cancellationToken)
        => SendAsync<OSLSymbol[]>(HttpMethod.Get,
            "/openapi/v1/spot/public/symbols",
            new OSLQueryBuilder().Add("symbol", symbol).ToString(), null,
            false, cancellationToken);

    public ValueTask<OSLTicker[]> GetTickersAsync(string symbol,
        CancellationToken cancellationToken)
        => SendAsync<OSLTicker[]>(HttpMethod.Get,
            "/openapi/v1/spot/market/tickers",
            new OSLQueryBuilder().Add("symbol", symbol).ToString(), null,
            false, cancellationToken);

    public ValueTask<OSLOrderBook> GetOrderBookAsync(string symbol, int limit,
        CancellationToken cancellationToken)
        => SendAsync<OSLOrderBook>(HttpMethod.Get,
            "/openapi/v1/spot/market/orderbook",
            new OSLQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("limit", limit.Min(150).Max(1))
                .ToString(), null, false, cancellationToken);

    public ValueTask<OSLPublicTrade[]> GetPublicTradesAsync(string symbol,
        int limit, CancellationToken cancellationToken)
        => SendAsync<OSLPublicTrade[]>(HttpMethod.Get,
            "/openapi/v1/spot/market/fills",
            new OSLQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("limit", limit.Min(500).Max(1))
                .ToString(), null, false, cancellationToken);

    public ValueTask<OSLCandle[]> GetCandlesAsync(string symbol,
        TimeSpan timeFrame, DateTime from, DateTime to, int limit,
        CancellationToken cancellationToken)
        => SendAsync<OSLCandle[]>(HttpMethod.Get,
            "/openapi/v1/spot/market/candles",
            new OSLQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("granularity", timeFrame.ToRestInterval())
                .Add("startTime", new DateTimeOffset(
                    from.ToUniversalTime()).ToUnixTimeMilliseconds())
                .Add("endTime", new DateTimeOffset(
                    to.ToUniversalTime()).ToUnixTimeMilliseconds())
                .Add("limit", limit.Min(1000).Max(1))
                .ToString(), null, false, cancellationToken);

    public ValueTask<OSLAsset[]> GetAssetsAsync(string coin,
        CancellationToken cancellationToken)
        => SendPrivateAsync<OSLAsset[]>(HttpMethod.Get,
            "/openapi/v1/asset/tradingAssets",
            new OSLQueryBuilder()
                .Add("coin", coin)
                .Add("assetType", "all")
                .ToString(), null, cancellationToken);

    public ValueTask<OSLOrder[]> GetOpenOrdersAsync(string symbol,
        string orderId, DateTime? from, DateTime? to, string idLessThan,
        int limit, CancellationToken cancellationToken)
        => SendPrivateAsync<OSLOrder[]>(HttpMethod.Get,
            "/openapi/v1/spot/trade/unfilled-orders",
            CreateOrderQuery(symbol, orderId, from, to, idLessThan, limit),
            null, cancellationToken);

    public ValueTask<OSLOrder[]> GetHistoryOrdersAsync(string symbol,
        string orderId, DateTime? from, DateTime? to, string idLessThan,
        int limit, CancellationToken cancellationToken)
        => SendPrivateAsync<OSLOrder[]>(HttpMethod.Get,
            "/openapi/v1/spot/trade/history-orders",
            CreateOrderQuery(symbol, orderId, from, to, idLessThan, limit),
            null, cancellationToken);

    public ValueTask<OSLFill[]> GetFillsAsync(string symbol, string orderId,
        DateTime? from, DateTime? to, string idLessThan, int limit,
        CancellationToken cancellationToken)
        => SendPrivateAsync<OSLFill[]>(HttpMethod.Get,
            "/openapi/v1/spot/trade/fills",
            new OSLQueryBuilder()
                .Add("symbol", symbol?.NormalizeSymbol())
                .Add("orderId", orderId)
                .Add("startTime", ToTimestamp(from))
                .Add("endTime", ToTimestamp(to))
                .Add("idLessThan", idLessThan)
                .Add("limit", limit.Min(100).Max(1))
                .ToString(), null, cancellationToken);

    public ValueTask<OSLOrder> PlaceOrderAsync(
        OSLPlaceOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<OSLOrder>(HttpMethod.Post,
            "/openapi/v1/order", null, SerializeRequest(request),
            cancellationToken);

    public ValueTask<OSLOrder> CancelOrderAsync(
        OSLCancelOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<OSLOrder>(HttpMethod.Delete,
            "/openapi/v1/order", null, SerializeRequest(request),
            cancellationToken);

    public ValueTask<OSLOrder[]> CancelAllOrdersAsync(
        OSLCancelAllOrdersRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<OSLOrder[]>(HttpMethod.Delete,
            "/openapi/v1/allOpenOrders", null, SerializeRequest(request),
            cancellationToken);

    private static string CreateOrderQuery(string symbol, string orderId,
        DateTime? from, DateTime? to, string idLessThan, int limit)
        => new OSLQueryBuilder()
            .Add("symbol", symbol?.NormalizeSymbol())
            .Add("startTime", ToTimestamp(from))
            .Add("endTime", ToTimestamp(to))
            .Add("idLessThan", idLessThan)
            .Add("limit", limit.Min(100).Max(1))
            .Add("orderId", orderId)
            .ToString();

    private static string ToTimestamp(DateTime? value)
        => value is DateTime time
            ? new DateTimeOffset(time.ToUniversalTime())
                .ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : null;

    private string SerializeRequest<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonConvert.SerializeObject(request, _jsonSettings);
    }

    private ValueTask<TResponse> SendPrivateAsync<TResponse>(
        HttpMethod method, string path, string query, string body,
        CancellationToken cancellationToken)
    {
        EnsurePrivateAvailable();
        return SendAsync<TResponse>(method, path, query, body, true,
            cancellationToken);
    }

    private async ValueTask<TResponse> SendAsync<TResponse>(
        HttpMethod method, string path, string query, string body,
        bool isPrivate, CancellationToken cancellationToken)
    {
        var queryString = query.IsEmpty() ? string.Empty : $"?{query}";
        var requestPath = path + queryString;
        var isRead = method == HttpMethod.Get;
        for (var attempt = 0; ; attempt++)
        {
            await _rateGate.WaitAsync(cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(_endpoint, requestPath.TrimStart('/')));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                AddAuthentication(request, method.Method, path,
                    queryString, body);
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await ReadBodyAsync(response.Content,
                cancellationToken);
            if (isRead && attempt < _maximumReadAttempts - 1 &&
                IsTransient(response.StatusCode))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            return DeserializeResponse<TResponse>(response.StatusCode,
                responseBody);
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string method,
        string path, string query, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        var prehash = timestamp + method.ToUpperInvariant() + path +
            (query ?? string.Empty) + (body ?? string.Empty);
        var signatureBytes = HMACSHA256.HashData(_apiSecret,
            Encoding.UTF8.GetBytes(prehash));
        try
        {
            request.Headers.TryAddWithoutValidation("ACCESS-KEY", _apiKey);
            request.Headers.TryAddWithoutValidation("ACCESS-SIGN",
                Convert.ToBase64String(signatureBytes));
            request.Headers.TryAddWithoutValidation("ACCESS-TIMESTAMP",
                timestamp);
            request.Headers.TryAddWithoutValidation("ACCESS-PASSPHRASE",
                _passphrase);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }

    private TResponse DeserializeResponse<TResponse>(HttpStatusCode statusCode,
        string body)
    {
        OSLResponse<TResponse> response = null;
        try
        {
            if (!body.IsEmpty())
                response = JsonConvert.DeserializeObject<OSLResponse<TResponse>>(
                    body, _jsonSettings);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "OSL returned an unexpected response shape.", error);
        }
        if ((int)statusCode is < 200 or >= 300 ||
            response?.Code is not ("00000" or "0"))
            throw CreateApiError(statusCode, body, response?.Code,
                response?.Message);
        return response.Data;
    }

    private static Exception CreateApiError(HttpStatusCode statusCode,
        string body, string code, string message)
    {
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new OSLApiException(statusCode, code,
            $"OSL HTTP {(int)statusCode} ({code ?? "unknown"}): {message}");
    }

    private void EnsurePrivateAvailable()
    {
        if (!IsPrivateAvailable)
            throw new InvalidOperationException(
                "OSL API key and secret are required for private operations.");
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "OSL REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is (HttpStatusCode)429 || (int)statusCode >= 500;

    private static async ValueTask DelayRetryAsync(
        HttpResponseMessage response, int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
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
                "OSL response exceeds the 8 MiB safety limit.");
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "OSL response exceeds the 8 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
