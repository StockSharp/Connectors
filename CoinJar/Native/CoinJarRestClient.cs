namespace StockSharp.CoinJar.Native;

sealed class CoinJarRestClient : BaseLogReceiver
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
    private readonly Uri _tradingEndpoint;
    private readonly Uri _dataEndpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _token;
    private readonly RateGate _publicGate = new(TimeSpan.FromMilliseconds(125));
    private readonly RateGate _privateGate = new(TimeSpan.FromSeconds(1));
    private readonly RateGate _orderGate = new(TimeSpan.FromMilliseconds(20));
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

    public CoinJarRestClient(string tradingEndpoint, string dataEndpoint,
        SecureString token)
    {
        _tradingEndpoint = ValidateEndpoint(tradingEndpoint, "Trading");
        _dataEndpoint = ValidateEndpoint(dataEndpoint, "Market Data");
        _token = token.IsEmpty() ? null : token.UnSecure().Trim();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-CoinJar-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "CoinJar_REST";

    public bool IsCredentialsAvailable => !_token.IsEmpty();

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _publicGate.Dispose();
        _privateGate.Dispose();
        _orderGate.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<CoinJarProduct[]> GetProductsAsync(
        CancellationToken cancellationToken)
        => GetPublicAsync<CoinJarProduct[]>(_tradingEndpoint, "/products", null,
            cancellationToken);

    public ValueTask<CoinJarTicker> GetTickerAsync(string productId,
        CancellationToken cancellationToken)
        => GetPublicAsync<CoinJarTicker>(_dataEndpoint,
            $"/products/{productId.EscapePath()}/ticker", null,
            cancellationToken);

    public ValueTask<CoinJarOrderBook> GetOrderBookAsync(string productId,
        int level, CancellationToken cancellationToken)
        => GetPublicAsync<CoinJarOrderBook>(_dataEndpoint,
            $"/products/{productId.EscapePath()}/book", $"level={level}",
            cancellationToken);

    public ValueTask<CoinJarTrade[]> GetTradesAsync(string productId,
        CoinJarTradesRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<CoinJarTrade[]>(_dataEndpoint,
            $"/products/{productId.EscapePath()}/trades",
            CoinJarQueryWriter.Create(request), cancellationToken);

    public ValueTask<CoinJarCandle[]> GetCandlesAsync(string productId,
        CoinJarCandlesRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<CoinJarCandle[]>(_dataEndpoint,
            $"/products/{productId.EscapePath()}/candles",
            CoinJarQueryWriter.Create(request), cancellationToken);

    public ValueTask<CoinJarAccount[]> GetAccountsAsync(
        CancellationToken cancellationToken)
        => GetPrivateAsync<CoinJarAccount[]>("/accounts", null,
            cancellationToken);

    public ValueTask<CoinJarPage<CoinJarOrder>> GetOrdersAsync(bool includeClosed,
        string cursor, CancellationToken cancellationToken)
        => GetPrivatePageAsync<CoinJarOrder>(includeClosed ? "/orders/all" :
            "/orders", CoinJarQueryWriter.Create(new CoinJarCursorRequest
            {
                Cursor = cursor,
            }),
            cancellationToken);

    public ValueTask<CoinJarOrder> GetOrderAsync(long orderId,
        CancellationToken cancellationToken)
        => GetPrivateAsync<CoinJarOrder>(
            $"/orders/{orderId.ToString(CultureInfo.InvariantCulture)}", null,
            cancellationToken);

    public ValueTask<CoinJarPage<CoinJarFill>> GetFillsAsync(string cursor,
        CancellationToken cancellationToken)
        => GetPrivatePageAsync<CoinJarFill>("/fills",
            CoinJarQueryWriter.Create(new CoinJarCursorRequest
            {
                Cursor = cursor,
            }),
            cancellationToken);

    public ValueTask<CoinJarFill> GetFillAsync(long tradeId,
        CancellationToken cancellationToken)
        => GetPrivateAsync<CoinJarFill>(
            $"/fills/{tradeId.ToString(CultureInfo.InvariantCulture)}", null,
            cancellationToken);

    public ValueTask<CoinJarOrder> PlaceOrderAsync(
        CoinJarPlaceOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<CoinJarOrder, CoinJarPlaceOrderRequest>(
            HttpMethod.Post, "/orders", request, _orderGate, cancellationToken);

    public ValueTask<CoinJarOrder> CancelOrderAsync(long orderId,
        CancellationToken cancellationToken)
        => SendPrivateAsync<CoinJarOrder>(HttpMethod.Delete,
            $"/orders/{orderId.ToString(CultureInfo.InvariantCulture)}",
            _orderGate, cancellationToken);

    public ValueTask<CoinJarCancelSummary> CancelAllOrdersAsync(
        CancellationToken cancellationToken)
        => SendPrivateAsync<CoinJarCancelSummary>(HttpMethod.Post,
            "/orders/cancel_all", _orderGate, cancellationToken);

    private async ValueTask<TResponse> GetPublicAsync<TResponse>(Uri endpoint,
        string path, string query, CancellationToken cancellationToken)
        => (await SendAsync<TResponse>(endpoint, HttpMethod.Get, path, query, null,
            false, _publicGate, cancellationToken)).Response;

    private async ValueTask<TResponse> GetPrivateAsync<TResponse>(string path,
        string query, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return (await SendAsync<TResponse>(_tradingEndpoint, HttpMethod.Get, path,
            query, null, true, _privateGate, cancellationToken)).Response;
    }

    private async ValueTask<CoinJarPage<TItem>> GetPrivatePageAsync<TItem>(
        string path, string query, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var result = await SendAsync<TItem[]>(_tradingEndpoint, HttpMethod.Get,
            path, query, null, true, _privateGate, cancellationToken);
        return new()
        {
            Items = result.Response,
            Cursor = result.Cursor,
        };
    }

    private async ValueTask<TResponse> SendPrivateAsync<TResponse>(
        HttpMethod method, string path, RateGate gate,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return (await SendAsync<TResponse>(_tradingEndpoint, method, path, null,
            null, true, gate, cancellationToken)).Response;
    }

    private async ValueTask<TResponse> SendPrivateAsync<TResponse, TRequest>(
        HttpMethod method, string path, TRequest payload, RateGate gate,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        EnsureCredentials();
        ArgumentNullException.ThrowIfNull(payload);
        var body = JsonConvert.SerializeObject(payload, _jsonSettings);
        return (await SendAsync<TResponse>(_tradingEndpoint, method, path, null,
            body, true, gate, cancellationToken)).Response;
    }

    private async ValueTask<(TResponse Response, string Cursor)>
        SendAsync<TResponse>(Uri endpoint, HttpMethod method, string path,
            string query, string body, bool isPrivate, RateGate gate,
            CancellationToken cancellationToken)
    {
        var requestPath = query.IsEmpty() ? path : $"{path}?{query}";
        var canRetry = method == HttpMethod.Get;
        for (var attempt = 0; ; attempt++)
        {
            await gate.WaitAsync(cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(endpoint, requestPath.TrimStart('/')));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", _token);

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
                throw CreateApiError(response.StatusCode, responseBody);
            return (Deserialize<TResponse>(responseBody),
                GetHeader(response, "X-CJX-Cursor"));
        }
    }

    private TResponse Deserialize<TResponse>(string body)
    {
        if (body.IsEmpty())
            throw new InvalidDataException("CoinJar returned an empty response.");
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
                throw new InvalidDataException(
                    "CoinJar returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "CoinJar returned an unexpected response shape.", error);
        }
    }

    private Exception CreateApiError(HttpStatusCode statusCode, string body)
    {
        CoinJarErrorResponse error = null;
        try
        {
            error = JsonConvert.DeserializeObject<CoinJarErrorResponse>(body,
                _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var message = error?.ErrorMessages?.Where(static value =>
            !value.IsEmpty()).Join("; ");
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new CoinJarApiException(statusCode, error?.ErrorType,
            $"CoinJar HTTP {(int)statusCode}" +
            (error?.ErrorType is { } type ? $" ({type})" : string.Empty) +
            $": {message}");
    }

    private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
        int attempt, CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            TimeSpan.FromMilliseconds(250 * (1 << attempt));
        if (delay > TimeSpan.FromSeconds(10))
            delay = TimeSpan.FromSeconds(10);
        await Task.Delay(delay, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 || (int)statusCode >= 500;

    private static string GetHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static Uri ValidateEndpoint(string value, string name)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                $"CoinJar {name} endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "A CoinJar Exchange API token is required for private operations.");
    }
}
