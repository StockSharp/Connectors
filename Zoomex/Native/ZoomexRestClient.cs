namespace StockSharp.Zoomex.Native;

sealed class ZoomexRestClient : BaseLogReceiver
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
    private const int _maximumResponseBytes = 16 * 1024 * 1024;
    private const int _receiveWindow = 5000;
    private const string _apiPrefix = "/cloud/trade/v3";
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly RateGate _rateGate = new(TimeSpan.FromMilliseconds(50));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };

    public ZoomexRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "Zoomex API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Zoomex-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "Zoomex_REST";

    public bool IsPrivateAvailable => !_apiKey.IsEmpty();

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _rateGate.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public ValueTask<ZoomexListResult<ZoomexProduct>> GetProductsAsync(
        ZoomexCategories category, string cursor, int limit,
        CancellationToken cancellationToken)
        => SendPublicAsync<ZoomexListResult<ZoomexProduct>>(HttpMethod.Get,
            _apiPrefix + "/market/instruments-info",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("limit", limit.Min(1000).Max(1))
                .Add("cursor", cursor).ToString(), cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexTicker>> GetTickersAsync(
        ZoomexCategories category, string symbol,
        CancellationToken cancellationToken)
        => SendPublicAsync<ZoomexListResult<ZoomexTicker>>(HttpMethod.Get,
            _apiPrefix + "/market/tickers",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol?.NormalizeSymbol()).ToString(),
            cancellationToken);

    public ValueTask<ZoomexOrderBook> GetOrderBookAsync(
        ZoomexCategories category, string symbol, int depth,
        CancellationToken cancellationToken)
        => SendPublicAsync<ZoomexOrderBook>(HttpMethod.Get,
            _apiPrefix + "/market/orderbook",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("limit", NormalizeRestDepth(category, depth)).ToString(),
            cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexPublicTrade>>
        GetPublicTradesAsync(ZoomexCategories category, string symbol,
            int limit, CancellationToken cancellationToken)
        => SendPublicAsync<ZoomexListResult<ZoomexPublicTrade>>(
            HttpMethod.Get, _apiPrefix + "/market/recent-trade",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("limit", limit.Min(1000).Max(1)).ToString(),
            cancellationToken);

    public ValueTask<ZoomexCandleResult> GetCandlesAsync(
        ZoomexCategories category, string symbol, TimeSpan timeFrame,
        DateTime? from, DateTime? to, int limit,
        CancellationToken cancellationToken)
        => SendPublicAsync<ZoomexCandleResult>(HttpMethod.Get,
            _apiPrefix + "/market/kline",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("interval", timeFrame.ToInterval())
                .Add("start", ToTimestamp(from))
                .Add("end", ToTimestamp(to))
                .Add("limit", limit.Min(1000).Max(1)).ToString(),
            cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexWalletAccount>>
        GetWalletBalanceAsync(ZoomexNativeAccountTypes accountType,
            CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexListResult<ZoomexWalletAccount>>(
            HttpMethod.Get, _apiPrefix + "/account/wallet-balance",
            new ZoomexQueryBuilder().Add("accountType", accountType)
                .ToString(), null, cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexPosition>> GetPositionsAsync(
        ZoomexCategories category, string symbol, string settleCoin,
        string cursor, int limit, CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexListResult<ZoomexPosition>>(
            HttpMethod.Get, _apiPrefix + "/position/list",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol?.NormalizeSymbol())
                .Add("settleCoin", settleCoin?.ToUpperInvariant())
                .Add("limit", limit.Min(200).Max(1))
                .Add("cursor", cursor).ToString(), null,
            cancellationToken);

    public ValueTask<ZoomexOrderAcknowledgement> PlaceOrderAsync(
        ZoomexPlaceOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexOrderAcknowledgement>(HttpMethod.Post,
            _apiPrefix + "/order/create", null, Serialize(request),
            cancellationToken);

    public ValueTask<ZoomexOrderAcknowledgement> AmendOrderAsync(
        ZoomexAmendOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexOrderAcknowledgement>(HttpMethod.Post,
            _apiPrefix + "/order/amend", null, Serialize(request),
            cancellationToken);

    public ValueTask<ZoomexOrderAcknowledgement> CancelOrderAsync(
        ZoomexCancelOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexOrderAcknowledgement>(HttpMethod.Post,
            _apiPrefix + "/order/cancel", null, Serialize(request),
            cancellationToken);

    public ValueTask<ZoomexCancelAllResult> CancelAllOrdersAsync(
        ZoomexCancelAllRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexCancelAllResult>(HttpMethod.Post,
            _apiPrefix + "/order/cancel-all", null, Serialize(request),
            cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexRealtimeOrder>>
        GetOpenOrdersAsync(ZoomexCategories category, string symbol,
            string settleCoin, string orderId, string orderLinkId,
            string cursor, int limit, CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexListResult<ZoomexRealtimeOrder>>(
            HttpMethod.Get, _apiPrefix + "/order/realtime",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol?.NormalizeSymbol())
                .Add("settleCoin", settleCoin?.ToUpperInvariant())
                .Add("orderId", orderId)
                .Add("orderLinkId", orderLinkId)
                .Add("openOnly", (int?)0)
                .Add("limit", limit.Min(50).Max(1))
                .Add("cursor", cursor).ToString(), null,
            cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexOrder>> GetOrderHistoryAsync(
        ZoomexCategories category, string symbol, string orderId,
        string orderLinkId, DateTime? from, DateTime? to, string cursor,
        int limit, CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexListResult<ZoomexOrder>>(
            HttpMethod.Get, _apiPrefix + "/order/history",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol?.NormalizeSymbol())
                .Add("orderId", orderId)
                .Add("orderLinkId", orderLinkId)
                .Add("startTime", ToTimestamp(from))
                .Add("endTime", ToTimestamp(to))
                .Add("limit", limit.Min(50).Max(1))
                .Add("cursor", cursor).ToString(), null,
            cancellationToken);

    public ValueTask<ZoomexListResult<ZoomexExecution>> GetExecutionsAsync(
        ZoomexCategories category, string symbol, string orderId,
        DateTime? from, DateTime? to, string cursor, int limit,
        CancellationToken cancellationToken)
        => SendPrivateAsync<ZoomexListResult<ZoomexExecution>>(
            HttpMethod.Get, _apiPrefix + "/execution/list",
            new ZoomexQueryBuilder()
                .Add("category", category)
                .Add("symbol", symbol?.NormalizeSymbol())
                .Add("orderId", orderId)
                .Add("startTime", ToTimestamp(from))
                .Add("endTime", ToTimestamp(to))
                .Add("limit", limit.Min(100).Max(1))
                .Add("cursor", cursor).ToString(), null,
            cancellationToken);

    private string Serialize<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonConvert.SerializeObject(request, _jsonSettings);
    }

    private ValueTask<TResponse> SendPublicAsync<TResponse>(
        HttpMethod method, string path, string query,
        CancellationToken cancellationToken)
        => SendAsync<TResponse>(method, path, query, null, false,
            cancellationToken);

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
        var isRead = method == HttpMethod.Get;
        for (var attempt = 0; ; attempt++)
        {
            await _rateGate.WaitAsync(cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(_endpoint, (path + queryString).TrimStart('/')));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                AddAuthentication(request, query, body);
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
            try
            {
                return DeserializeResponse<TResponse>(response.StatusCode,
                    responseBody);
            }
            catch (ZoomexApiException error) when (isRead &&
                attempt < _maximumReadAttempts - 1 &&
                IsTransient(error.Code))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
            }
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string query,
        string body)
    {
        var timestamp = ((long)DateTime.UtcNow.ToUnix(false)).ToString(
            CultureInfo.InvariantCulture);
        var payload = timestamp + _apiKey +
            _receiveWindow.ToString(CultureInfo.InvariantCulture) +
            (query ?? body ?? string.Empty);
        var signature = HMACSHA256.HashData(_apiSecret,
            Encoding.UTF8.GetBytes(payload));
        try
        {
            request.Headers.TryAddWithoutValidation("X-BAPI-API-KEY",
                _apiKey);
            request.Headers.TryAddWithoutValidation("X-BAPI-SIGN",
                Convert.ToHexString(signature).ToLowerInvariant());
            request.Headers.TryAddWithoutValidation("X-BAPI-SIGN-TYPE",
                "2");
            request.Headers.TryAddWithoutValidation("X-BAPI-TIMESTAMP",
                timestamp);
            request.Headers.TryAddWithoutValidation("X-BAPI-RECV-WINDOW",
                _receiveWindow.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private TResponse DeserializeResponse<TResponse>(HttpStatusCode status,
        string body)
    {
        ZoomexResponse<TResponse> response = null;
        try
        {
            if (!body.IsEmpty())
                response = JsonConvert.DeserializeObject<
                    ZoomexResponse<TResponse>>(body, _jsonSettings);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Zoomex returned an unexpected response shape.", error);
        }
        if ((int)status is < 200 or >= 300 || response?.Code != 0)
            throw CreateApiError(status, response?.Code, response?.Message,
                body);
        return response.Result;
    }

    private static Exception CreateApiError(HttpStatusCode status, int? code,
        string message, string body)
    {
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new ZoomexApiException(status, code,
            $"Zoomex HTTP {(int)status} ({code?.ToString() ?? "unknown"}): " +
            message);
    }

    private void EnsurePrivateAvailable()
    {
        if (!IsPrivateAvailable)
            throw new InvalidOperationException(
                "Zoomex API key and secret are required for private operations.");
    }

    private static int NormalizeRestDepth(ZoomexCategories category, int depth)
    {
        var maximum = category == ZoomexCategories.Spot ? 200 : 500;
        return depth.Min(maximum).Max(1);
    }

    private static long? ToTimestamp(DateTime? value)
        => value is DateTime time
            ? (long)time.ToUniversalTime().ToUnix(false)
            : null;

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Zoomex REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is (HttpStatusCode)429 or HttpStatusCode.Forbidden ||
            (int)status >= 500;

    private static bool IsTransient(int? code)
        => code is 10000 or 10006 or 10016 or 10018 or 3400214;

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
                "Zoomex response exceeds the 16 MiB safety limit.");
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
                    "Zoomex response exceeds the 16 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
