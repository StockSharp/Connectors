namespace StockSharp.BTCMarkets.Native;

sealed class BTCMarketsRestClient : BaseLogReceiver
{
    private sealed class RateGate : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private readonly TimeSpan _interval;
        private DateTime _nextRequest;

        public RateGate(TimeSpan interval) => _interval = interval;

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
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly RateGate _readGate = new(TimeSpan.FromMilliseconds(75));
    private readonly RateGate _writeGate = new(TimeSpan.FromMilliseconds(210));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private long _clockOffsetMilliseconds;

    public BTCMarketsRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "BTC Markets API key and secret must be configured together.");
        if (!secretText.IsEmpty())
        {
            try
            {
                _apiSecret = Convert.FromBase64String(secretText);
            }
            catch (FormatException error)
            {
                throw new ArgumentException(
                    "BTC Markets API secret must be Base64 encoded.",
                    nameof(secret), error);
            }
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-BTCMarkets-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "BTCMarkets_REST";

    public bool IsCredentialsAvailable => !_apiKey.IsEmpty();

    public long GetTimestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            Interlocked.Read(ref _clockOffsetMilliseconds);

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _readGate.Dispose();
        _writeGate.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public async ValueTask SynchronizeClockAsync(
        CancellationToken cancellationToken)
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var response = await GetPublicAsync<BTCMarketsServerTime>("/v3/time", null,
            cancellationToken);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var local = before + (after - before) / 2;
        _clockOffsetMilliseconds = new DateTimeOffset(
            response.Timestamp.ToUtcTime()).ToUnixTimeMilliseconds() - local;
    }

    public ValueTask<BTCMarketsMarket[]> GetMarketsAsync(
        CancellationToken cancellationToken)
        => GetPublicAsync<BTCMarketsMarket[]>("/v3/markets", null,
            cancellationToken);

    public ValueTask<BTCMarketsTicker> GetTickerAsync(string marketId,
        CancellationToken cancellationToken)
        => GetPublicAsync<BTCMarketsTicker>(
            $"/v3/markets/{marketId.EscapePath()}/ticker", null,
            cancellationToken);

    public ValueTask<BTCMarketsOrderBook> GetOrderBookAsync(string marketId,
        int level, CancellationToken cancellationToken)
        => GetPublicAsync<BTCMarketsOrderBook>(
            $"/v3/markets/{marketId.EscapePath()}/orderbook", $"level={level}",
            cancellationToken);

    public ValueTask<BTCMarketsPage<BTCMarketsPublicTrade>> GetMarketTradesAsync(
        string marketId, BTCMarketsTradesRequest request,
        CancellationToken cancellationToken)
        => GetPublicPageAsync<BTCMarketsPublicTrade>(
            $"/v3/markets/{marketId.EscapePath()}/trades",
            BTCMarketsQueryWriter.Create(request), cancellationToken);

    public ValueTask<BTCMarketsCandle[]> GetCandlesAsync(string marketId,
        BTCMarketsCandlesRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<BTCMarketsCandle[]>(
            $"/v3/markets/{marketId.EscapePath()}/candles",
            BTCMarketsQueryWriter.Create(request), cancellationToken);

    public ValueTask<BTCMarketsBalance[]> GetBalancesAsync(
        CancellationToken cancellationToken)
        => GetPrivateAsync<BTCMarketsBalance[]>(
            "/v3/accounts/me/balances", null, cancellationToken);

    public ValueTask<BTCMarketsPage<BTCMarketsOrder>> GetOrdersAsync(
        BTCMarketsOrdersRequest request, CancellationToken cancellationToken)
        => GetPrivatePageAsync<BTCMarketsOrder>("/v3/orders",
            BTCMarketsQueryWriter.Create(request), cancellationToken);

    public ValueTask<BTCMarketsOrder> GetOrderAsync(string orderId,
        CancellationToken cancellationToken)
        => GetPrivateAsync<BTCMarketsOrder>(
            $"/v3/orders/{orderId.EscapePath()}", null, cancellationToken);

    public ValueTask<BTCMarketsPage<BTCMarketsUserTrade>> GetUserTradesAsync(
        BTCMarketsUserTradesRequest request, CancellationToken cancellationToken)
        => GetPrivatePageAsync<BTCMarketsUserTrade>("/v3/trades",
            BTCMarketsQueryWriter.Create(request), cancellationToken);

    public ValueTask<BTCMarketsOrder> PlaceOrderAsync(
        BTCMarketsPlaceOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BTCMarketsOrder, BTCMarketsPlaceOrderRequest>(
            HttpMethod.Post, "/v3/orders", null, request, cancellationToken);

    public ValueTask<BTCMarketsOrder> ReplaceOrderAsync(string orderId,
        BTCMarketsReplaceOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BTCMarketsOrder, BTCMarketsReplaceOrderRequest>(
            HttpMethod.Put, $"/v3/orders/{orderId.EscapePath()}", null, request,
            cancellationToken);

    public ValueTask<BTCMarketsOrderReference> CancelOrderAsync(string orderId,
        CancellationToken cancellationToken)
        => SendPrivateAsync<BTCMarketsOrderReference>(HttpMethod.Delete,
            $"/v3/orders/{orderId.EscapePath()}", null, cancellationToken);

    public ValueTask<BTCMarketsOrderReference[]> CancelOrdersAsync(
        BTCMarketsCancelOrdersRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<BTCMarketsOrderReference[]>(HttpMethod.Delete,
            "/v3/orders", BTCMarketsQueryWriter.Create(request), cancellationToken);

    private async ValueTask<TResponse> GetPublicAsync<TResponse>(string path,
        string query, CancellationToken cancellationToken)
        => (await SendAsync<TResponse>(HttpMethod.Get, path, query, null, false,
            cancellationToken)).Response;

    private async ValueTask<BTCMarketsPage<TItem>> GetPublicPageAsync<TItem>(
        string path, string query, CancellationToken cancellationToken)
    {
        var result = await SendAsync<TItem[]>(HttpMethod.Get, path, query, null,
            false, cancellationToken);
        return new()
        {
            Items = result.Response,
            Before = result.Before,
            After = result.After,
        };
    }

    private async ValueTask<TResponse> GetPrivateAsync<TResponse>(string path,
        string query, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return (await SendAsync<TResponse>(HttpMethod.Get, path, query, null, true,
            cancellationToken)).Response;
    }

    private async ValueTask<BTCMarketsPage<TItem>> GetPrivatePageAsync<TItem>(
        string path, string query, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var result = await SendAsync<TItem[]>(HttpMethod.Get, path, query, null,
            true, cancellationToken);
        return new()
        {
            Items = result.Response,
            Before = result.Before,
            After = result.After,
        };
    }

    private async ValueTask<TResponse> SendPrivateAsync<TResponse>(
        HttpMethod method, string path, string query,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return (await SendAsync<TResponse>(method, path, query, null, true,
            cancellationToken)).Response;
    }

    private async ValueTask<TResponse> SendPrivateAsync<TResponse, TRequest>(
        HttpMethod method, string path, string query, TRequest payload,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        EnsureCredentials();
        ArgumentNullException.ThrowIfNull(payload);
        var body = JsonConvert.SerializeObject(payload, _jsonSettings);
        return (await SendAsync<TResponse>(method, path, query, body, true,
            cancellationToken)).Response;
    }

    private async ValueTask<(TResponse Response, string Before, string After)>
        SendAsync<TResponse>(
        HttpMethod method, string path, string query, string body,
        bool isPrivate, CancellationToken cancellationToken)
    {
        var requestPath = query.IsEmpty() ? path : $"{path}?{query}";
        var isRead = method == HttpMethod.Get;
        for (var attempt = 0; ; attempt++)
        {
            await (isRead ? _readGate : _writeGate).WaitAsync(cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(_endpoint, requestPath.TrimStart('/')));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                AddAuthentication(request, method.Method, path, body);
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (isRead && attempt < _maximumReadAttempts - 1 &&
                IsTransient(response.StatusCode))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw CreateApiError(response.StatusCode, responseBody);
            return (Deserialize<TResponse>(responseBody),
                GetHeader(response.Headers, "BM-BEFORE"),
                GetHeader(response.Headers, "BM-AFTER"));
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string method,
        string path, string body)
    {
        var timestamp = GetTimestamp();
        var message = method + path + timestamp.ToString(CultureInfo.InvariantCulture) +
            (body ?? string.Empty);
        using var hmac = new HMACSHA512(_apiSecret);
        var signature = Convert.ToBase64String(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(message)));
        request.Headers.TryAddWithoutValidation("BM-AUTH-APIKEY", _apiKey);
        request.Headers.TryAddWithoutValidation("BM-AUTH-TIMESTAMP",
            timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("BM-AUTH-SIGNATURE", signature);
    }

    private TResponse Deserialize<TResponse>(string body)
    {
        if (body.IsEmpty())
            throw new InvalidDataException("BTC Markets returned an empty response.");
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
                throw new InvalidDataException(
                    "BTC Markets returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "BTC Markets returned an unexpected response shape.", error);
        }
    }

    private Exception CreateApiError(HttpStatusCode statusCode, string body)
    {
        BTCMarketsErrorResponse error = null;
        try
        {
            error = JsonConvert.DeserializeObject<BTCMarketsErrorResponse>(body,
                _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var message = error?.Message;
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new BTCMarketsApiException(statusCode, error?.Code,
            $"BTC Markets HTTP {(int)statusCode}" +
            (error?.Code.IsEmpty() == false ? $" ({error.Code})" : string.Empty) +
            $": {message}");
    }

    private static string GetHeader(
        System.Net.Http.Headers.HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "BTC Markets API key and secret are required for this operation.");
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "BTC Markets REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
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
