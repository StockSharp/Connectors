namespace StockSharp.GmoCoin.Native;

sealed class GmoCoinRestClient : BaseLogReceiver
{
    private sealed class RateGate : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private readonly TimeSpan _interval;
        private DateTime _nextRequest;

        public RateGate(TimeSpan interval)
        {
            _interval = interval;
        }

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
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
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new();
    private readonly string _apiKey;
    private readonly byte[] _secret;
    private readonly RateGate _publicGate = new(TimeSpan.FromMilliseconds(50));
    private readonly RateGate _privateReadGate = new(TimeSpan.FromMilliseconds(55));
    private readonly RateGate _privateWriteGate = new(TimeSpan.FromMilliseconds(55));
    private readonly Lock _timestampSync = new();
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private long _lastTimestamp;

    public GmoCoinRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = CreateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretValue.IsEmpty())
            throw new ArgumentException(
                "GMO Coin API key and secret must be configured together.");
        _secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-GmoCoin-Connector/1.0");
    }

    public override string Name => "GmoCoin_REST";

    public bool IsCredentialsAvailable
        => !_apiKey.IsEmpty() && _secret is { Length: > 0 };

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _publicGate.Dispose();
        _privateReadGate.Dispose();
        _privateWriteGate.Dispose();
        if (_secret is not null)
            CryptographicOperations.ZeroMemory(_secret);
        base.DisposeManaged();
    }

    public ValueTask<GmoCoinStatus> GetStatusAsync(
        CancellationToken cancellationToken)
        => GetPublicAsync<GmoCoinStatus>("/v1/status", null,
            cancellationToken);

    public ValueTask<GmoCoinSymbol[]> GetSymbolsAsync(
        CancellationToken cancellationToken)
        => GetPublicAsync<GmoCoinSymbol[]>("/v1/symbols", null,
            cancellationToken);

    public ValueTask<GmoCoinTicker[]> GetTickerAsync(
        GmoCoinTickerRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<GmoCoinTicker[]>("/v1/ticker",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinOrderBook> GetOrderBookAsync(
        GmoCoinOrderBookRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<GmoCoinOrderBook>("/v1/orderbooks",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinPage<GmoCoinPublicTrade>> GetTradesAsync(
        GmoCoinTradesRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<GmoCoinPage<GmoCoinPublicTrade>>("/v1/trades",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinCandle[]> GetKlinesAsync(
        GmoCoinKlinesRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<GmoCoinCandle[]>("/v1/klines",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinAsset[]> GetAssetsAsync(
        CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinAsset[]>("/v1/account/assets", null,
            cancellationToken);

    public ValueTask<GmoCoinList<GmoCoinOrder>> GetOrdersAsync(
        GmoCoinOrdersRequest request, CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinList<GmoCoinOrder>>("/v1/orders",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinPage<GmoCoinOrder>> GetActiveOrdersAsync(
        GmoCoinActiveOrdersRequest request, CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinPage<GmoCoinOrder>>("/v1/activeOrders",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinList<GmoCoinExecution>> GetExecutionsAsync(
        GmoCoinExecutionsRequest request, CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinList<GmoCoinExecution>>("/v1/executions",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinPage<GmoCoinExecution>> GetLatestExecutionsAsync(
        GmoCoinLatestExecutionsRequest request,
        CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinPage<GmoCoinExecution>>(
            "/v1/latestExecutions", GmoCoinQueryWriter.Create(request),
            cancellationToken);

    public ValueTask<GmoCoinPage<GmoCoinPosition>> GetOpenPositionsAsync(
        GmoCoinOpenPositionsRequest request,
        CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinPage<GmoCoinPosition>>("/v1/openPositions",
            GmoCoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<GmoCoinList<GmoCoinPositionSummary>> GetPositionSummaryAsync(
        GmoCoinPositionSummaryRequest request,
        CancellationToken cancellationToken)
        => GetPrivateAsync<GmoCoinList<GmoCoinPositionSummary>>(
            "/v1/positionSummary", GmoCoinQueryWriter.Create(request),
            cancellationToken);

    public ValueTask<string> PlaceOrderAsync(GmoCoinPlaceOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<string, GmoCoinPlaceOrderRequest>(HttpMethod.Post,
            "/v1/order", request, true, cancellationToken);

    public ValueTask<string> CloseOrderAsync(GmoCoinCloseOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<string, GmoCoinCloseOrderRequest>(HttpMethod.Post,
            "/v1/closeOrder", request, true, cancellationToken);

    public ValueTask<string> CloseBulkOrderAsync(
        GmoCoinCloseBulkOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<string, GmoCoinCloseBulkOrderRequest>(HttpMethod.Post,
            "/v1/closeBulkOrder", request, true, cancellationToken);

    public ValueTask ChangeOrderAsync(GmoCoinChangeOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateWithoutDataAsync(HttpMethod.Post, "/v1/changeOrder",
            request, true, cancellationToken);

    public ValueTask CancelOrderAsync(GmoCoinCancelOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateWithoutDataAsync(HttpMethod.Post, "/v1/cancelOrder",
            request, true, cancellationToken);

    public ValueTask<GmoCoinCancelOrdersResult> CancelOrdersAsync(
        GmoCoinCancelOrdersRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<GmoCoinCancelOrdersResult, GmoCoinCancelOrdersRequest>(
            HttpMethod.Post, "/v1/cancelOrders", request, true,
            cancellationToken);

    public ValueTask<long[]> CancelBulkOrderAsync(
        GmoCoinCancelBulkOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<long[], GmoCoinCancelBulkOrderRequest>(HttpMethod.Post,
            "/v1/cancelBulkOrder", request, true, cancellationToken);

    public ValueTask<string> CreateWebSocketTokenAsync(
        CancellationToken cancellationToken)
        => SendPrivateAsync<string, GmoCoinEmptyRequest>(HttpMethod.Post,
            "/v1/ws-auth", new(), true, cancellationToken);

    public ValueTask ExtendWebSocketTokenAsync(string token,
        CancellationToken cancellationToken)
        => SendPrivateWithoutDataAsync(HttpMethod.Put, "/v1/ws-auth",
            new GmoCoinWebSocketTokenRequest { Token = token }, false,
            cancellationToken);

    public ValueTask DeleteWebSocketTokenAsync(string token,
        CancellationToken cancellationToken)
        => SendPrivateWithoutDataAsync(HttpMethod.Delete, "/v1/ws-auth",
            new GmoCoinWebSocketTokenRequest { Token = token }, false,
            cancellationToken);

    private ValueTask<TData> GetPublicAsync<TData>(string path, string query,
        CancellationToken cancellationToken)
        => SendAsync<TData>(false, HttpMethod.Get, path, query, null, false,
            _publicGate, true, cancellationToken);

    private ValueTask<TData> GetPrivateAsync<TData>(string path, string query,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendAsync<TData>(true, HttpMethod.Get, path, query, null, false,
            _privateReadGate, true, cancellationToken);
    }

    private ValueTask<TData> SendPrivateAsync<TData, TRequest>(HttpMethod method,
        string path, TRequest request, bool isBodySigned,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var body = Serialize(request);
        return SendAsync<TData>(true, method, path, null, body, isBodySigned,
            _privateWriteGate, false, cancellationToken);
    }

    private async ValueTask SendPrivateWithoutDataAsync<TRequest>(
        HttpMethod method, string path, TRequest request, bool isBodySigned,
        CancellationToken cancellationToken)
        => _ = await SendPrivateAsync<GmoCoinEmptyData, TRequest>(method, path,
            request, isBodySigned, cancellationToken);

    private async ValueTask<TData> SendAsync<TData>(bool isPrivate,
        HttpMethod method, string path, string query, string body,
        bool isBodySigned, RateGate rateGate, bool canRetry,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await rateGate.WaitAsync(cancellationToken);
            var uri = CreateUri(isPrivate, path, query);
            using var request = new HttpRequestMessage(method, uri);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                AddAuthentication(request, path, isBodySigned ? body : null);

            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (canRetry && attempt < _maximumReadAttempts - 1 &&
                IsTransient(response.StatusCode))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw CreateHttpError(response.StatusCode, responseBody);

            var envelope = Deserialize<GmoCoinResponse<TData>>(responseBody);
            if (!envelope.IsSuccess)
                throw CreateApiError(envelope.Status, envelope.Messages);
            return envelope.Data;
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string path,
        string body)
    {
        var timestamp = CreateTimestamp().ToString(CultureInfo.InvariantCulture);
        var signature = Sign(timestamp + request.Method.Method + path +
            (body ?? string.Empty));
        request.Headers.TryAddWithoutValidation("API-KEY", _apiKey);
        request.Headers.TryAddWithoutValidation("API-TIMESTAMP", timestamp);
        request.Headers.TryAddWithoutValidation("API-SIGN", signature);
    }

    private string Serialize<TValue>(TValue value)
        => JsonConvert.SerializeObject(value, _jsonSettings);

    private TResponse Deserialize<TResponse>(string body)
    {
        if (body.IsEmpty())
            throw new InvalidDataException(
                "GMO Coin returned an empty response.");
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
                throw new InvalidDataException(
                    "GMO Coin returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "GMO Coin returned an unexpected response shape.", error);
        }
    }

    private Exception CreateHttpError(HttpStatusCode statusCode, string body)
    {
        try
        {
            var error = Deserialize<GmoCoinResponse<GmoCoinEmptyData>>(body);
            if (!error.IsSuccess)
                return CreateApiError(error.Status, error.Messages,
                    $"HTTP {(int)statusCode}");
        }
        catch (InvalidDataException)
        {
        }

        var details = body?.Trim();
        if (details?.Length > 512)
            details = details[..512];
        return new HttpRequestException(
            $"GMO Coin HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
            null, statusCode);
    }

    private static Exception CreateApiError(int status,
        IEnumerable<GmoCoinApiMessage> messages, string prefix = null)
    {
        var values = (messages ?? []).Where(static item => item is not null)
            .ToArray();
        var code = string.Join(", ", values.Select(static item => item.Code)
            .Where(static value => !value.IsEmpty()));
        var message = string.Join("; ", values.Select(static item => item.Message)
            .Where(static value => !value.IsEmpty()));
        if (message.IsEmpty())
            message = "The API rejected the request.";
        var label = prefix.IsEmpty() ? "GMO Coin" : $"GMO Coin {prefix}";
        return new GmoCoinApiException(status, code,
            $"{label} error {status}{(code.IsEmpty() ? string.Empty : $" ({code})")}: {message}");
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_secret);
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private long CreateTimestamp()
    {
        using (_timestampSync.EnterScope())
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (timestamp <= _lastTimestamp)
                timestamp = _lastTimestamp + 1;
            _lastTimestamp = timestamp;
            return timestamp;
        }
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "GMO Coin API key and secret are required for private operations.");
    }

    private static Uri CreateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "GMO Coin REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private Uri CreateUri(bool isPrivate, string path, string query)
    {
        var prefix = isPrivate ? "private" : "public";
        var uri = new Uri(_endpoint,
            $"{prefix}/{path.ThrowIfEmpty(nameof(path)).TrimStart('/')}");
        return query.IsEmpty() ? uri : new UriBuilder(uri) { Query = query }.Uri;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is (HttpStatusCode)429 || (int)statusCode >= 500;

    private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
        int attempt, CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
            TimeSpan.FromMilliseconds(500 * (1 << attempt));
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        await Task.Delay(delay, cancellationToken);
    }
}
