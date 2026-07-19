namespace StockSharp.Coinone.Native;

sealed class CoinoneRestClient : BaseLogReceiver
{
    private const int _maximumReadAttempts = 4;
    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly string _accessToken;
    private readonly byte[] _secret;
    private readonly SemaphoreSlim _publicSync = new(1, 1);
    private readonly SemaphoreSlim _privateSync = new(1, 1);
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

    public CoinoneRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = CreateEndpoint(endpoint);
        _accessToken = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_accessToken.IsEmpty() != secretValue.IsEmpty())
            throw new ArgumentException(
                "Coinone access token and secret must be configured together.");
        _secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);

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
            "StockSharp-Coinone-Connector/1.0");
    }

    public override string Name => "Coinone_Rest";

    public bool IsCredentialsAvailable
        => !_accessToken.IsEmpty() && _secret is not null;

    protected override void DisposeManaged()
    {
        if (_secret is not null)
            CryptographicOperations.ZeroMemory(_secret);
        _publicSync.Dispose();
        _privateSync.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<CoinoneMarketsResponse> GetMarketsAsync(string quoteCurrency,
        CancellationToken cancellationToken)
        => SendPublicAsync<CoinoneMarketsResponse>(
            $"/public/v2/markets/{Escape(quoteCurrency)}", null,
            cancellationToken);

    public ValueTask<CoinoneTickerResponse> GetTickerAsync(
        CoinoneMarketRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPublicAsync<CoinoneTickerResponse>(
            $"/public/v2/ticker_new/{Escape(request.QuoteCurrency)}/{Escape(request.TargetCurrency)}",
            new CoinoneQueryWriter().Add("additional_data", "true").ToString(),
            cancellationToken);
    }

    public ValueTask<CoinoneBookResponse> GetOrderBookAsync(
        CoinoneOrderBookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new CoinoneQueryWriter()
            .Add("size", request.Size)
            .Add("order_book_unit", request.OrderBookUnit)
            .ToString();
        return SendPublicAsync<CoinoneBookResponse>(
            $"/public/v2/orderbook/{Escape(request.QuoteCurrency)}/{Escape(request.TargetCurrency)}",
            query, cancellationToken);
    }

    public ValueTask<CoinoneTradesResponse> GetTradesAsync(
        CoinoneTradesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new CoinoneQueryWriter().Add("size", request.Size).ToString();
        return SendPublicAsync<CoinoneTradesResponse>(
            $"/public/v2/trades/{Escape(request.QuoteCurrency)}/{Escape(request.TargetCurrency)}",
            query, cancellationToken);
    }

    public ValueTask<CoinoneChartResponse> GetChartAsync(
        CoinoneChartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new CoinoneQueryWriter()
            .Add("interval", request.Interval)
            .Add("timestamp", request.Timestamp)
            .Add("size", request.Size)
            .ToString();
        return SendPublicAsync<CoinoneChartResponse>(
            $"/public/v2/chart/{Escape(request.QuoteCurrency)}/{Escape(request.TargetCurrency)}",
            query, cancellationToken);
    }

    public ValueTask<CoinoneBalancesResponse> GetBalancesAsync(
        CancellationToken cancellationToken)
        => SendPrivateAsync<CoinoneBalanceRequest, CoinoneBalancesResponse>(
            "/v2.1/account/balance/all", new(), true, false, cancellationToken);

    public ValueTask<CoinonePlaceOrderResponse> PlaceOrderAsync(
        CoinonePlaceOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPrivateAsync<CoinonePlaceOrderRequest,
            CoinonePlaceOrderResponse>("/v2.1/order", request, false, true,
            cancellationToken);
    }

    public ValueTask<CoinoneCancelOrderResponse> CancelOrderAsync(
        CoinoneCancelOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPrivateAsync<CoinoneCancelOrderRequest,
            CoinoneCancelOrderResponse>("/v2.1/order/cancel", request, false, true,
            cancellationToken);
    }

    public ValueTask<CoinoneCancelAllResponse> CancelAllAsync(
        CoinoneCancelAllRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPrivateAsync<CoinoneCancelAllRequest,
            CoinoneCancelAllResponse>("/v2.1/order/cancel/all", request, false,
            true, cancellationToken);
    }

    public ValueTask<CoinoneActiveOrdersResponse> GetActiveOrdersAsync(
        CoinoneActiveOrdersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPrivateAsync<CoinoneActiveOrdersRequest,
            CoinoneActiveOrdersResponse>("/v2.1/order/active_orders", request,
            true, false, cancellationToken);
    }

    public ValueTask<CoinoneOrderDetailResponse> GetOrderAsync(
        CoinoneOrderDetailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPrivateAsync<CoinoneOrderDetailRequest,
            CoinoneOrderDetailResponse>("/v2.1/order/detail", request, true,
            false, cancellationToken);
    }

    public ValueTask<CoinoneCompletedOrdersResponse> GetCompletedOrdersAsync(
        CoinoneCompletedOrdersRequest request, bool allMarkets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendPrivateAsync<CoinoneCompletedOrdersRequest,
            CoinoneCompletedOrdersResponse>(allMarkets
                ? "/v2.1/order/completed_orders/all"
                : "/v2.1/order/completed_orders", request, true, false,
                cancellationToken);
    }

    public CoinoneAuthentication CreateWebSocketAuthentication()
    {
        EnsureCredentials();
        var request = new CoinoneWebSocketAuthRequest
        {
            AccessToken = _accessToken,
            Nonce = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        var body = Serialize(request);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        return new()
        {
            EncodedPayload = encoded,
            Signature = Sign(encoded),
        };
    }

    private async ValueTask<TResponse> SendPublicAsync<TResponse>(string path,
        string query, CancellationToken cancellationToken)
        where TResponse : CoinoneResponse
    {
        for (var attempt = 0; ; attempt++)
        {
            await WaitPublicAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                CreateUri(path, query));
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return DeserializeResponse<TResponse>(body);
            if (attempt + 1 >= _maximumReadAttempts ||
                !IsTransient(response.StatusCode))
                throw CreateHttpError(response.StatusCode, body);
            await DelayRetryAsync(response, attempt, cancellationToken);
        }
    }

    private async ValueTask<TResponse> SendPrivateAsync<TRequest, TResponse>(
        string path, TRequest payload, bool isRetrySafe, bool isOrder,
        CancellationToken cancellationToken)
        where TRequest : CoinonePrivateRequest
        where TResponse : CoinoneResponse
    {
        EnsureCredentials();
        for (var attempt = 0; ; attempt++)
        {
            payload.AccessToken = _accessToken;
            payload.Nonce = Guid.NewGuid().ToString();
            var body = Serialize(payload);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
            await WaitPrivateAsync(isOrder, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                CreateUri(path, null))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-COINONE-PAYLOAD", encoded);
            request.Headers.TryAddWithoutValidation("X-COINONE-SIGNATURE",
                Sign(encoded));
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (response.IsSuccessStatusCode)
                return DeserializeResponse<TResponse>(responseBody);
            if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
                !IsTransient(response.StatusCode))
                throw CreateHttpError(response.StatusCode, responseBody);
            await DelayRetryAsync(response, attempt, cancellationToken);
        }
    }

    private string Serialize<TValue>(TValue value)
        => JsonConvert.SerializeObject(value, _jsonSettings);

    private TResponse DeserializeResponse<TResponse>(string body)
        where TResponse : CoinoneResponse
    {
        if (body.IsEmpty())
            throw new InvalidDataException("Coinone returned an empty response.");
        TResponse response;
        try
        {
            response = JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
                throw new InvalidDataException(
                    "Coinone returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Coinone returned an unexpected response shape.", error);
        }
        if (!response.IsSuccess)
            throw new CoinoneApiException(response.ErrorCode,
                $"Coinone error {response.ErrorCode}: {response.ErrorMessage}".Trim());
        return response;
    }

    private Exception CreateHttpError(HttpStatusCode statusCode, string body)
    {
        try
        {
            var error = JsonConvert.DeserializeObject<CoinoneResponse>(
                body ?? string.Empty, _jsonSettings);
            if (error is not null && !error.ErrorCode.IsEmpty())
                return new CoinoneApiException(error.ErrorCode,
                    $"Coinone HTTP {(int)statusCode}, error {error.ErrorCode}: " +
                    error.ErrorMessage);
        }
        catch (JsonException)
        {
        }
        var details = body?.Trim();
        if (details?.Length > 512)
            details = details[..512];
        return new HttpRequestException(
            $"Coinone HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
            null, statusCode);
    }

    private string Sign(string encodedPayload)
    {
        using var hmac = new HMACSHA512(_secret);
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(encodedPayload))).ToLowerInvariant();
    }

    private async ValueTask WaitPublicAsync(
        CancellationToken cancellationToken)
    {
        await _publicSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextPublicRequest - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextPublicRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(55);
        }
        finally
        {
            _publicSync.Release();
        }
    }

    private async ValueTask WaitPrivateAsync(bool isOrder,
        CancellationToken cancellationToken)
    {
        await _privateSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextPrivateRequest - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextPrivateRequest = DateTime.UtcNow +
                TimeSpan.FromMilliseconds(isOrder ? 30 : 15);
        }
        finally
        {
            _privateSync.Release();
        }
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Coinone access token and secret are required for private operations.");
    }

    private static Uri CreateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Coinone REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private Uri CreateUri(string path, string query)
    {
        var uri = new Uri(_endpoint,
            path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
        return query.IsEmpty() ? uri : new UriBuilder(uri) { Query = query }.Uri;
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value.NormalizeCurrency());

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
