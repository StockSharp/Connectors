namespace StockSharp.Foxbit.Native;

sealed class FoxbitRestClient : BaseLogReceiver
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
    private const int _maximumResponseBytes = 8 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly RateGate _readGate = new(TimeSpan.FromMilliseconds(200));
    private readonly RateGate _writeGate = new(TimeSpan.FromMilliseconds(75));
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

    public FoxbitRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "Foxbit API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Foxbit-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "Foxbit_REST";

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
        var response = await GetPublicAsync<FoxbitServerTime>(
            "/rest/v3/system/time", null, cancellationToken);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (response.Timestamp <= 0)
            throw new InvalidDataException(
                "Foxbit returned an invalid server timestamp.");
        var local = before + (after - before) / 2;
        Interlocked.Exchange(ref _clockOffsetMilliseconds,
            response.Timestamp - local);
    }

    public async ValueTask<FoxbitMarket[]> GetMarketsAsync(
        CancellationToken cancellationToken)
        => (await GetPublicAsync<FoxbitEnvelope<FoxbitMarket[]>>(
            "/rest/v3/markets", null, cancellationToken)).Data ?? [];

    public async ValueTask<FoxbitTicker> GetTickerAsync(string marketSymbol,
        CancellationToken cancellationToken)
    {
        marketSymbol = marketSymbol.NormalizeMarket();
        var response = await GetPublicAsync<FoxbitEnvelope<FoxbitTicker[]>>(
            $"/rest/v3/markets/{marketSymbol.EscapePath()}/ticker/24hr", null,
            cancellationToken);
        return (response.Data ?? []).FirstOrDefault(ticker =>
            ticker?.MarketSymbol.EqualsIgnoreCase(marketSymbol) == true) ??
            response.Data?.FirstOrDefault() ?? throw new InvalidDataException(
                $"Foxbit returned no ticker for '{marketSymbol}'.");
    }

    public ValueTask<FoxbitOrderBook> GetOrderBookAsync(string marketSymbol,
        int depth, CancellationToken cancellationToken)
        => GetPublicAsync<FoxbitOrderBook>(
            $"/rest/v3/markets/{marketSymbol.NormalizeMarket().EscapePath()}" +
            "/orderbook", FoxbitQueryWriter.Depth(depth.Min(300).Max(1)),
            cancellationToken);

    public async ValueTask<FoxbitPublicTrade[]> GetPublicTradesAsync(
        string marketSymbol, FoxbitPublicTradesRequest request,
        CancellationToken cancellationToken)
        => (await GetPublicAsync<FoxbitEnvelope<FoxbitPublicTrade[]>>(
            $"/rest/v3/markets/{marketSymbol.NormalizeMarket().EscapePath()}" +
            "/trades/history", FoxbitQueryWriter.Create(request),
            cancellationToken)).Data ?? [];

    public ValueTask<FoxbitCandle[]> GetCandlesAsync(string marketSymbol,
        FoxbitCandlesRequest request, CancellationToken cancellationToken)
        => GetPublicAsync<FoxbitCandle[]>(
            $"/rest/v3/markets/{marketSymbol.NormalizeMarket().EscapePath()}" +
            "/candlesticks", FoxbitQueryWriter.Create(request),
            cancellationToken);

    public async ValueTask<FoxbitAccount[]> GetAccountsAsync(
        CancellationToken cancellationToken)
        => (await GetPrivateAsync<FoxbitEnvelope<FoxbitAccount[]>>(
            "/rest/v3/accounts", null, cancellationToken)).Data ?? [];

    public async ValueTask<FoxbitOrder[]> GetOrdersAsync(
        FoxbitOrdersRequest request, CancellationToken cancellationToken)
        => (await GetPrivateAsync<FoxbitEnvelope<FoxbitOrder[]>>(
            "/rest/v3/orders", FoxbitQueryWriter.Create(request),
            cancellationToken)).Data ?? [];

    public ValueTask<FoxbitOrder> GetOrderAsync(string orderId,
        CancellationToken cancellationToken)
        => GetPrivateAsync<FoxbitOrder>(
            $"/rest/v3/orders/by-order-id/{orderId.EscapePath()}", null,
            cancellationToken);

    public ValueTask<FoxbitOrder> GetOrderByClientIdAsync(string clientOrderId,
        CancellationToken cancellationToken)
        => GetPrivateAsync<FoxbitOrder>(
            "/rest/v3/orders/by-client-order-id/" +
            clientOrderId.EscapePath(), null, cancellationToken);

    public async ValueTask<FoxbitTrade[]> GetTradesAsync(
        FoxbitTradesRequest request, CancellationToken cancellationToken)
        => (await GetPrivateAsync<FoxbitEnvelope<FoxbitTrade[]>>(
            "/rest/v3/trades", FoxbitQueryWriter.Create(request),
            cancellationToken)).Data ?? [];

    public ValueTask<FoxbitOrderCreated> PlaceOrderAsync(
        FoxbitPlaceOrderRequest request, CancellationToken cancellationToken)
        => SendPrivateAsync<FoxbitOrderCreated, FoxbitPlaceOrderRequest>(
            HttpMethod.Post, "/rest/v3/orders", null, request,
            cancellationToken);

    public async ValueTask<FoxbitCanceledOrder[]> CancelOrdersAsync(
        FoxbitCancelRequest request, CancellationToken cancellationToken)
        => (await SendPrivateAsync<FoxbitEnvelope<FoxbitCanceledOrder[]>,
            FoxbitCancelRequest>(HttpMethod.Put, "/rest/v3/orders/cancel",
            null, request, cancellationToken)).Data ?? [];

    private async ValueTask<TResponse> GetPublicAsync<TResponse>(string path,
        FoxbitQuery? query, CancellationToken cancellationToken)
        => await SendAsync<TResponse>(HttpMethod.Get, path, query, null, false,
            cancellationToken);

    private async ValueTask<TResponse> GetPrivateAsync<TResponse>(string path,
        FoxbitQuery? query, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return await SendAsync<TResponse>(HttpMethod.Get, path, query, null,
            true, cancellationToken);
    }

    private async ValueTask<TResponse> SendPrivateAsync<TResponse, TRequest>(
        HttpMethod method, string path, FoxbitQuery? query, TRequest payload,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        EnsureCredentials();
        ArgumentNullException.ThrowIfNull(payload);
        var body = JsonConvert.SerializeObject(payload, _jsonSettings);
        return await SendAsync<TResponse>(method, path, query, body, true,
            cancellationToken);
    }

    private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
        string path, FoxbitQuery? query, string body, bool isPrivate,
        CancellationToken cancellationToken)
    {
        var encodedQuery = query?.Encoded;
        var requestPath = encodedQuery.IsEmpty()
            ? path
            : $"{path}?{encodedQuery}";
        var isRead = method == HttpMethod.Get;
        for (var attempt = 0; ; attempt++)
        {
            await (isRead ? _readGate : _writeGate).WaitAsync(
                cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(_endpoint, requestPath.TrimStart('/')));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                AddAuthentication(request, method.Method, path,
                    query?.Signing, body);
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
            if (!response.IsSuccessStatusCode)
                throw CreateApiError(response.StatusCode, responseBody);
            return Deserialize<TResponse>(responseBody);
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string method,
        string path, string signingQuery, string body)
    {
        var timestamp = GetTimestamp().ToString(CultureInfo.InvariantCulture);
        var prehash = timestamp + method.ToUpperInvariant() + path +
            (signingQuery ?? string.Empty) + (body ?? string.Empty);
        using var hmac = new HMACSHA256(_apiSecret);
        var signature = Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(prehash))).ToLowerInvariant();
        request.Headers.TryAddWithoutValidation("X-FB-ACCESS-KEY", _apiKey);
        request.Headers.TryAddWithoutValidation("X-FB-ACCESS-TIMESTAMP",
            timestamp);
        request.Headers.TryAddWithoutValidation("X-FB-ACCESS-SIGNATURE",
            signature);
    }

    private TResponse Deserialize<TResponse>(string body)
    {
        if (body.IsEmpty())
            throw new InvalidDataException("Foxbit returned an empty response.");
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(body,
                _jsonSettings) ?? throw new InvalidDataException(
                    "Foxbit returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Foxbit returned an unexpected response shape.", error);
        }
    }

    private Exception CreateApiError(HttpStatusCode statusCode, string body)
    {
        FoxbitErrorResponse response = null;
        try
        {
            response = JsonConvert.DeserializeObject<FoxbitErrorResponse>(body,
                _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var details = response?.Error ?? new FoxbitErrorDetails
        {
            Message = response?.Message,
            Code = response?.Code,
            Details = response?.Details,
        };
        var message = details.Message;
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        if (details.Details is { Length: > 0 })
            message += " " + string.Join("; ", details.Details.Where(
                static value => !value.IsEmpty()));
        return new FoxbitApiException(statusCode, details.Code,
            $"Foxbit HTTP {(int)statusCode}" +
            (details.Code is long code ? $" ({code})" : string.Empty) +
            $": {message}");
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "Foxbit response exceeds the 8 MiB safety limit.");
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
                    "Foxbit response exceeds the 8 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Foxbit API key and secret are required for this operation.");
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Foxbit REST endpoint must be an absolute HTTPS URI.",
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
        if (delay > TimeSpan.FromSeconds(10))
            delay = TimeSpan.FromSeconds(10);
        await Task.Delay(delay, cancellationToken);
    }
}
