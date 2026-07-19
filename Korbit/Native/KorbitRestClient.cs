namespace StockSharp.Korbit.Native;

sealed class KorbitApiException(HttpStatusCode statusCode, int code,
    string errorCode, string details) : InvalidOperationException($"Korbit API error {(int)statusCode} ({statusCode}), " +
            $"code {code}: {errorCode}{details}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public int Code { get; } = code;
    public string ErrorCode { get; } = errorCode;
}

sealed class KorbitRestClient : BaseLogReceiver
{
    private enum RateBuckets
    {
        Public,
        Private,
        PlaceOrder,
        CancelOrder,
    }

    private const int _maximumReadAttempts = 4;
    private const int _receiveWindow = 10_000;

    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly byte[] _secret;
    private readonly SemaphoreSlim _publicSync = new(1, 1);
    private readonly SemaphoreSlim _privateSync = new(1, 1);
    private readonly SemaphoreSlim _placeOrderSync = new(1, 1);
    private readonly SemaphoreSlim _cancelOrderSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private DateTime _nextPublicRequest;
    private DateTime _nextPrivateRequest;
    private DateTime _nextPlaceOrderRequest;
    private DateTime _nextCancelOrderRequest;
    private long _serverTimeOffset;

    public KorbitRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = CreateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretValue.IsEmpty())
            throw new ArgumentException(
                "Korbit API key and HMAC secret must be configured together.");
        _secret = secretValue.IsEmpty()
            ? null
            : Encoding.UTF8.GetBytes(secretValue);

        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Korbit-Connector/1.0");
    }

    public override string Name => "Korbit_Rest";

    public bool IsCredentialsAvailable
        => !_apiKey.IsEmpty() && _secret is not null;

    protected override void DisposeManaged()
    {
        if (_secret is not null)
            CryptographicOperations.ZeroMemory(_secret);
        _publicSync.Dispose();
        _privateSync.Dispose();
        _placeOrderSync.Dispose();
        _cancelOrderSync.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<KorbitTradingPair[]> GetTradingPairsAsync(
        CancellationToken cancellationToken)
        => SendPublicAsync<KorbitTradingPair[]>("/v2/currencyPairs", null,
            cancellationToken);

    public ValueTask<KorbitTicker[]> GetTickersAsync(KorbitTickerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("symbol", request.Symbols)
            .ToString();
        return SendPublicAsync<KorbitTicker[]>("/v2/tickers", query,
            cancellationToken);
    }

    public ValueTask<KorbitOrderBook> GetOrderBookAsync(
        KorbitOrderBookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("level", request.Level)
            .ToString();
        return SendPublicAsync<KorbitOrderBook>("/v2/orderbook", query,
            cancellationToken);
    }

    public ValueTask<KorbitPublicTrade[]> GetTradesAsync(
        KorbitTradesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("limit", request.Limit)
            .ToString();
        return SendPublicAsync<KorbitPublicTrade[]>("/v2/trades", query,
            cancellationToken);
    }

    public ValueTask<KorbitCandle[]> GetCandlesAsync(
        KorbitCandlesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("interval", request.Interval)
            .Add("start", request.Start)
            .Add("end", request.End)
            .Add("limit", request.Limit)
            .ToString();
        return SendPublicAsync<KorbitCandle[]>("/v2/candles", query,
            cancellationToken);
    }

    public ValueTask<KorbitTickSizePolicy[]> GetTickSizePolicyAsync(
        KorbitTickSizeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .ToString();
        return SendPublicAsync<KorbitTickSizePolicy[]>("/v2/tickSizePolicy",
            query, cancellationToken);
    }

    public ValueTask<KorbitBalance[]> GetBalancesAsync(KorbitBalanceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("accountSeq", request.AccountSequence)
            .Add("currencies", request.Currencies)
            .ToString();
        return SendPrivateAsync<KorbitBalance[]>(HttpMethod.Get, "/v2/balance",
            query, RateBuckets.Private, true, cancellationToken);
    }

    public ValueTask<KorbitOrder> GetOrderAsync(KorbitOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = CreateOrderQuery(request);
        return SendPrivateAsync<KorbitOrder>(HttpMethod.Get, "/v2/orders", query,
            RateBuckets.Private, true, cancellationToken);
    }

    public ValueTask<KorbitOrder[]> GetOpenOrdersAsync(KorbitOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = CreateOrdersQuery(request, false);
        return SendPrivateAsync<KorbitOrder[]>(HttpMethod.Get, "/v2/openOrders",
            query, RateBuckets.Private, true, cancellationToken);
    }

    public ValueTask<KorbitOrder[]> GetAllOrdersAsync(KorbitOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = CreateOrdersQuery(request, true);
        return SendPrivateAsync<KorbitOrder[]>(HttpMethod.Get, "/v2/allOrders",
            query, RateBuckets.Private, true, cancellationToken);
    }

    public ValueTask<KorbitAccountTrade[]> GetAccountTradesAsync(
        KorbitAccountTradesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("accountSeq", request.AccountSequence)
            .Add("startTime", request.StartTime)
            .Add("endTime", request.EndTime)
            .Add("limit", request.Limit)
            .ToString();
        return SendPrivateAsync<KorbitAccountTrade[]>(HttpMethod.Get,
            "/v2/myTrades", query, RateBuckets.Private, true,
            cancellationToken);
    }

    public ValueTask<KorbitPlaceOrderResult> PlaceOrderAsync(
        KorbitOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("accountSeq", request.AccountSequence)
            .Add("side", request.Side.ToWire())
            .Add("price", request.Price)
            .Add("qty", request.Quantity)
            .Add("amt", request.Amount)
            .Add("orderType", request.OrderType.ToWire())
            .Add("bestNth", request.BestLevel)
            .Add("timeInForce", request.TimeInForce.ToWire())
            .Add("clientOrderId", request.ClientOrderId)
            .Add("pp", request.IsPriceProtection ? true : null)
            .Add("ppPercent", request.PriceProtectionPercent)
            .ToString();
        return SendPrivateAsync<KorbitPlaceOrderResult>(HttpMethod.Post,
            "/v2/orders", body, RateBuckets.PlaceOrder, false,
            cancellationToken);
    }

    public async ValueTask CancelOrderAsync(KorbitOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = CreateOrderQuery(request);
        _ = await SendPrivateAsync<KorbitOperationResult>(HttpMethod.Delete,
            "/v2/orders", query, RateBuckets.CancelOrder, false,
            cancellationToken);
    }

    public async ValueTask SynchronizeTimeAsync(
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var server = await SendPublicAsync<KorbitTime>("/v2/time", null,
            cancellationToken);
        var finished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (server is null || server.Time <= 0)
            throw new InvalidDataException(
                "Korbit returned an invalid server timestamp.");
        Interlocked.Exchange(ref _serverTimeOffset,
            server.Time - (started + finished) / 2);
    }

    public KorbitWebSocketAuthentication CreateWebSocketAuthentication()
    {
        EnsureCredentials();
        var timestamp = GetTimestamp();
        var parameters = new KorbitQueryWriter()
            .Add("timestamp", timestamp)
            .Add("recvWindow", _receiveWindow)
            .ToString();
        return new(_apiKey,
            new KorbitQueryWriter()
                .Add("timestamp", timestamp)
                .Add("recvWindow", _receiveWindow)
                .Add("signature", Sign(parameters))
                .ToString());
    }

    private async ValueTask<TData> SendPublicAsync<TData>(string path,
        string query, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await WaitAsync(RateBuckets.Public, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                CreateUri(path, query));
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsTransient(response.StatusCode) &&
                attempt + 1 < _maximumReadAttempts)
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            return Parse<TData>(response.StatusCode, body);
        }
    }

    private async ValueTask<TData> SendPrivateAsync<TData>(HttpMethod method,
        string path, string parameters, RateBuckets bucket, bool isIdempotent,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var isClockRetried = false;
        for (var attempt = 0; ; attempt++)
        {
            await WaitAsync(bucket, cancellationToken);
            var signed = Append(parameters, "timestamp", GetTimestamp());
            signed = Append(signed, "recvWindow", _receiveWindow);
            signed = Append(signed, "signature", Sign(signed));

            using var request = new HttpRequestMessage(method,
                method == HttpMethod.Post
                    ? CreateUri(path, null)
                    : CreateUri(path, signed));
            request.Headers.TryAddWithoutValidation("X-KAPI-KEY", _apiKey);
            if (method == HttpMethod.Post)
                request.Content = new StringContent(signed, Encoding.UTF8,
                    "application/x-www-form-urlencoded");

            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                if (IsTransient(response.StatusCode) && isIdempotent &&
                    attempt + 1 < _maximumReadAttempts)
                {
                    await DelayRetryAsync(response, attempt, cancellationToken);
                    continue;
                }
                return Parse<TData>(response.StatusCode, body);
            }
            catch (KorbitApiException error) when (!isClockRetried &&
                error.ErrorCode.EqualsIgnoreCase("EXCEED_TIME_WINDOW"))
            {
                isClockRetried = true;
                await SynchronizeTimeAsync(cancellationToken);
            }
        }
    }

    private TData Parse<TData>(HttpStatusCode statusCode, string body)
    {
        KorbitResponse<TData> envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<KorbitResponse<TData>>(
                body, _jsonSettings);
        }
        catch (JsonException error) when ((int)statusCode >= 400)
        {
            throw CreateHttpError(statusCode, body, error);
        }

        if (envelope?.Success == true && (int)statusCode < 400)
            return envelope.Data;

        if (envelope?.Error is { } apiError)
            throw new KorbitApiException(statusCode, apiError.Code,
                apiError.Message.IsEmpty() ? "UNKNOWN" : apiError.Message,
                string.Empty);
        if ((int)statusCode >= 400)
            throw CreateHttpError(statusCode, body, null);
        throw new InvalidDataException(
            "Korbit returned an empty or unsuccessful response envelope.");
    }

    private async ValueTask WaitAsync(RateBuckets bucket,
        CancellationToken cancellationToken)
    {
        var sync = bucket switch
        {
            RateBuckets.Public => _publicSync,
            RateBuckets.PlaceOrder => _placeOrderSync,
            RateBuckets.CancelOrder => _cancelOrderSync,
            _ => _privateSync,
        };
        await sync.WaitAsync(cancellationToken);
        try
        {
            var next = bucket switch
            {
                RateBuckets.Public => _nextPublicRequest,
                RateBuckets.PlaceOrder => _nextPlaceOrderRequest,
                RateBuckets.CancelOrder => _nextCancelOrderRequest,
                _ => _nextPrivateRequest,
            };
            var delay = next - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            var value = DateTime.UtcNow + TimeSpan.FromMilliseconds(
                bucket is RateBuckets.PlaceOrder or RateBuckets.CancelOrder
                    ? 35
                    : 21);
            switch (bucket)
            {
                case RateBuckets.Public:
                    _nextPublicRequest = value;
                    break;
                case RateBuckets.PlaceOrder:
                    _nextPlaceOrderRequest = value;
                    break;
                case RateBuckets.CancelOrder:
                    _nextCancelOrderRequest = value;
                    break;
                default:
                    _nextPrivateRequest = value;
                    break;
            }
        }
        finally
        {
            sync.Release();
        }
    }

    private static string CreateOrderQuery(KorbitOrderQuery request)
        => new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("accountSeq", request.AccountSequence)
            .Add("orderId", request.OrderId)
            .Add("clientOrderId", request.ClientOrderId)
            .ToString();

    private static string CreateOrdersQuery(KorbitOrdersQuery request,
        bool includeRange)
    {
        var writer = new KorbitQueryWriter()
            .Add("symbol", request.Symbol.NormalizeSymbol())
            .Add("accountSeq", request.AccountSequence);
        if (includeRange)
            writer.Add("startTime", request.StartTime)
                .Add("endTime", request.EndTime);
        return writer.Add("limit", request.Limit).ToString();
    }

    private long GetTimestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            Interlocked.Read(ref _serverTimeOffset);

    private string Sign(string parameters)
    {
        using var hmac = new HMACSHA256(_secret);
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(parameters))).ToLowerInvariant();
    }

    private static string Append(string source, string name, long value)
        => Append(source, name, value.ToString(CultureInfo.InvariantCulture));

    private static string Append(string source, string name, string value)
    {
        var item = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
        return source.IsEmpty() ? item : $"{source}&{item}";
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Korbit API key and HMAC secret are required for private operations.");
    }

    private static Uri CreateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Korbit REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private Uri CreateUri(string path, string query)
    {
        var uri = new Uri(_endpoint,
            path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
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

    private static HttpRequestException CreateHttpError(HttpStatusCode statusCode,
        string body, Exception inner)
    {
        var details = body?.Trim();
        if (details?.Length > 512)
            details = details[..512];
        return new HttpRequestException(
            $"Korbit HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
            inner, statusCode);
    }
}
