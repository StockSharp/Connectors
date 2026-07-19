namespace StockSharp.BYDFi.Native;

sealed class BYDFiRestClient : BaseLogReceiver
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
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly RateGate _rateGate = new(TimeSpan.FromMilliseconds(60));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };

    public BYDFiRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "BYDFi API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-BYDFi-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "BYDFi_REST";

    public bool IsPrivateAvailable => !_apiKey.IsEmpty();

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _rateGate.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public ValueTask<BYDFiProduct[]> GetProductsAsync(
        CancellationToken cancellationToken)
        => SendPublicAsync<BYDFiProduct[]>(HttpMethod.Get,
            "/v1/fapi/market/exchange_info", null, cancellationToken);

    public ValueTask<BYDFiTicker[]> GetTickersAsync(string symbol,
        CancellationToken cancellationToken)
        => SendPublicAsync<BYDFiTicker[]>(HttpMethod.Get,
            "/v1/fapi/market/ticker/24hr",
            new BYDFiQueryBuilder()
                .Add("symbol", symbol?.NormalizeSymbol()).ToString(),
            cancellationToken);

    public ValueTask<BYDFiMarkPrice> GetMarkPriceAsync(string symbol,
        CancellationToken cancellationToken)
        => SendPublicAsync<BYDFiMarkPrice>(HttpMethod.Get,
            "/v1/fapi/market/mark_price",
            new BYDFiQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol()).ToString(),
            cancellationToken);

    public ValueTask<BYDFiOrderBook> GetOrderBookAsync(string symbol,
        int depth, CancellationToken cancellationToken)
        => SendPublicAsync<BYDFiOrderBook>(HttpMethod.Get,
            "/v1/fapi/market/depth",
            new BYDFiQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("limit", NormalizeRestDepth(depth)).ToString(),
            cancellationToken);

    public ValueTask<BYDFiPublicTrade[]> GetPublicTradesAsync(string symbol,
        int limit, CancellationToken cancellationToken)
        => SendPublicAsync<BYDFiPublicTrade[]>(HttpMethod.Get,
            "/v1/fapi/market/trades",
            new BYDFiQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("limit", limit.Min(1000).Max(1)).ToString(),
            cancellationToken);

    public async ValueTask<BYDFiKline[]> GetCandlesAsync(string symbol,
        TimeSpan timeFrame, DateTime? from, DateTime? to, int limit,
        CancellationToken cancellationToken)
    {
        limit = limit.Min(1500).Max(1);
        var end = (to ?? DateTime.UtcNow).ToUniversalTime();
        var start = (from ?? end - TimeSpan.FromTicks(
            timeFrame.Ticks * limit)).ToUniversalTime();
        if (start > end)
            (start, end) = (end, start);
        var response = await SendPublicAsync<BYDFiKline[]>(HttpMethod.Get,
            "/v1/fapi/market/klines",
            new BYDFiQueryBuilder()
                .Add("symbol", symbol.NormalizeSymbol())
                .Add("interval", timeFrame.ToInterval())
                .Add("startTime", new DateTimeOffset(start)
                    .ToUnixTimeMilliseconds())
                .Add("endTime", new DateTimeOffset(end)
                    .ToUnixTimeMilliseconds())
                .Add("limit", limit).ToString(), cancellationToken);
        return (response ?? [])
            .Where(item => item?.OpenTime.ToUtcTime() is DateTime time &&
                time >= start && time <= end)
            .OrderBy(static item => item.OpenTime.ToLong())
            .TakeLast(limit)
            .ToArray();
    }

    public ValueTask<BYDFiBalance[]> GetBalancesAsync(string wallet,
        CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiBalance[]>(HttpMethod.Get,
            "/v1/fapi/account/balance",
            new BYDFiQueryBuilder().Add("wallet", wallet).ToString(), null,
            cancellationToken);

    public ValueTask<BYDFiPosition[]> GetPositionsAsync(string wallet,
        string symbol, CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiPosition[]>(HttpMethod.Get,
            "/v2/fapi/trade/positions",
            new BYDFiQueryBuilder()
                .Add("contractType", "FUTURE")
                .Add("wallet", wallet)
                .Add("symbol", symbol?.NormalizeSymbol()).ToString(), null,
            cancellationToken);

    public ValueTask<BYDFiOrder> PlaceOrderAsync(
        BYDFiPlaceOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiOrder>(HttpMethod.Post,
            "/v2/fapi/trade/place_order", null, Serialize(request),
            cancellationToken);

    public ValueTask<BYDFiOrder> EditOrderAsync(
        BYDFiEditOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiOrder>(HttpMethod.Post,
            "/v2/fapi/trade/edit_order", null, Serialize(request),
            cancellationToken);

    public ValueTask<BYDFiOrder> CancelOrderAsync(
        BYDFiCancelOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiOrder>(HttpMethod.Post,
            "/v2/fapi/trade/cancel_order", null, Serialize(request),
            cancellationToken);

    public ValueTask<BYDFiOrder[]> CancelAllOrdersAsync(
        BYDFiOrderScopeRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiOrder[]>(HttpMethod.Post,
            "/v2/fapi/trade/cancel_all_order", null, Serialize(request),
            cancellationToken);

    public ValueTask<BYDFiOrder[]> GetOpenOrdersAsync(string wallet,
        string symbol, string orderId, string clientOrderId,
        CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiOrder[]>(HttpMethod.Get,
            "/v2/fapi/trade/open_order",
            new BYDFiQueryBuilder()
                .Add("wallet", wallet)
                .Add("symbol", symbol?.NormalizeSymbol())
                .Add("orderId", orderId)
                .Add("clientOrderId", clientOrderId).ToString(), null,
            cancellationToken);

    public ValueTask<BYDFiOrder[]> GetHistoryOrdersAsync(string wallet,
        string symbol, DateTime? from, DateTime? to, int limit,
        CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiOrder[]>(HttpMethod.Get,
            "/v2/fapi/trade/history_order",
            CreateHistoryQuery(wallet, symbol, from, to, limit), null,
            cancellationToken);

    public ValueTask<BYDFiUserTrade[]> GetUserTradesAsync(string wallet,
        string symbol, DateTime? from, DateTime? to, int limit,
        CancellationToken cancellationToken)
        => SendPrivateAsync<BYDFiUserTrade[]>(HttpMethod.Get,
            "/v2/fapi/trade/history_trade",
            CreateHistoryQuery(wallet, symbol, from, to, limit), null,
            cancellationToken);

    private static string CreateHistoryQuery(string wallet, string symbol,
        DateTime? from, DateTime? to, int limit)
        => new BYDFiQueryBuilder()
            .Add("contractType", "FUTURE")
            .Add("symbol", symbol?.NormalizeSymbol())
            .Add("wallet", wallet)
            .Add("startTime", ToTimestamp(from))
            .Add("endTime", ToTimestamp(to))
            .Add("limit", limit.Min(1000).Max(1)).ToString();

    private static long? ToTimestamp(DateTime? value)
        => value is DateTime time
            ? new DateTimeOffset(time.ToUniversalTime())
                .ToUnixTimeMilliseconds()
            : null;

    private static int NormalizeRestDepth(int value)
    {
        var supported = new[] { 5, 10, 20, 50, 100, 500, 1000 };
        value = value.Max(1);
        return supported.FirstOrDefault(item => item >= value, 1000);
    }

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
            return DeserializeResponse<TResponse>(response.StatusCode,
                responseBody);
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string query,
        string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        var prehash = _apiKey + timestamp + (query ?? string.Empty) +
            (body ?? string.Empty);
        var signature = HMACSHA256.HashData(_apiSecret,
            Encoding.UTF8.GetBytes(prehash));
        try
        {
            request.Headers.TryAddWithoutValidation("X-API-KEY", _apiKey);
            request.Headers.TryAddWithoutValidation("X-API-TIMESTAMP",
                timestamp);
            request.Headers.TryAddWithoutValidation("X-API-SIGNATURE",
                Convert.ToHexString(signature).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private TResponse DeserializeResponse<TResponse>(HttpStatusCode status,
        string body)
    {
        BYDFiResponse<TResponse> response = null;
        try
        {
            if (!body.IsEmpty())
                response = JsonConvert.DeserializeObject<
                    BYDFiResponse<TResponse>>(body, _jsonSettings);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "BYDFi returned an unexpected response shape.", error);
        }
        if ((int)status is < 200 or >= 300 || response?.Code != 200)
            throw CreateApiError(status, response?.Code, response?.Message,
                body);
        return response.Data;
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
        return new BYDFiApiException(status, code,
            $"BYDFi HTTP {(int)status} ({code?.ToString() ?? "unknown"}): " +
            message);
    }

    private void EnsurePrivateAvailable()
    {
        if (!IsPrivateAvailable)
            throw new InvalidOperationException(
                "BYDFi API key and secret are required for private operations.");
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "BYDFi REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is (HttpStatusCode)429 or (HttpStatusCode)510 ||
            (int)status >= 500;

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
                "BYDFi response exceeds the 16 MiB safety limit.");
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
                    "BYDFi response exceeds the 16 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
