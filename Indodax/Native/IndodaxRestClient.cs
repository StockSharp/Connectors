namespace StockSharp.Indodax.Native;

sealed class IndodaxApiException(HttpStatusCode statusCode, string code,
    string message)
    : InvalidOperationException(
        $"Indodax API error {(int)statusCode} ({statusCode}), code {code}: {message}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

sealed class IndodaxRestClient : BaseLogReceiver
{
    private const int _maximumReadAttempts = 4;
    private const int _receiveWindow = 5000;

    private sealed class ErrorPayload
    {
        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("error_code")]
        public string ErrorCode { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    private readonly Uri _publicEndpoint;
    private readonly Uri _historyEndpoint;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly byte[] _secret;
    private readonly IndodaxRateGate _publicRate = new(180,
        TimeSpan.FromMinutes(1));
    private readonly IndodaxRateGate _privateRate = new(20,
        TimeSpan.FromSeconds(1));
    private readonly IndodaxRateGate _cancelRate = new(30,
        TimeSpan.FromSeconds(1));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private long _serverTimeOffset;

    public IndodaxRestClient(string publicEndpoint, string historyEndpoint,
        SecureString key, SecureString secret)
    {
        _publicEndpoint = ValidateEndpoint(publicEndpoint, "public");
        _historyEndpoint = ValidateEndpoint(historyEndpoint, "history");
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretValue.IsEmpty())
            throw new ArgumentException(
                "Indodax TAPI key and secret must be configured together.");
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
            "StockSharp-Indodax-Connector/1.0");
    }

    public override string Name => "Indodax_Rest";

    public bool IsCredentialsAvailable
        => !_apiKey.IsEmpty() && _secret is not null;

    protected override void DisposeManaged()
    {
        if (_secret is not null)
            CryptographicOperations.ZeroMemory(_secret);
        _publicRate.Dispose();
        _privateRate.Dispose();
        _cancelRate.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask SynchronizeTimeAsync(
        CancellationToken cancellationToken)
    {
        var time = await SendPublicAsync<IndodaxServerTime>(
            "/api/server_time", null, cancellationToken);
        if (time?.ServerTime > 0)
            Interlocked.Exchange(ref _serverTimeOffset,
                time.ServerTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public ValueTask<IndodaxPair[]> GetPairsAsync(
        CancellationToken cancellationToken)
        => SendPublicAsync<IndodaxPair[]>("/api/pairs", null,
            cancellationToken);

    public ValueTask<IndodaxTickerEnvelope> GetTickerAsync(string pairId,
        CancellationToken cancellationToken)
        => SendPublicAsync<IndodaxTickerEnvelope>(
            $"/api/ticker/{pairId.NormalizePairId()}", null,
            cancellationToken);

    public ValueTask<IndodaxPublicTrade[]> GetTradesAsync(string pairId,
        CancellationToken cancellationToken)
        => SendPublicAsync<IndodaxPublicTrade[]>(
            $"/api/trades/{pairId.NormalizePairId()}", null,
            cancellationToken);

    public ValueTask<IndodaxDepth> GetDepthAsync(string pairId,
        CancellationToken cancellationToken)
        => SendPublicAsync<IndodaxDepth>(
            $"/api/depth/{pairId.NormalizePairId()}", null,
            cancellationToken);

    public ValueTask<IndodaxCandle[]> GetCandlesAsync(string symbol,
        string timeFrame, long from, long to,
        CancellationToken cancellationToken)
    {
        var query = new IndodaxFormWriter()
            .Add("from", from)
            .Add("symbol", symbol)
            .Add("tf", timeFrame)
            .Add("to", to)
            .ToString();
        return SendPublicAsync<IndodaxCandle[]>(
            "/tradingview/history_v2", query, cancellationToken);
    }

    public ValueTask<IndodaxAccountData> GetAccountAsync(
        CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxEmptyParameters, IndodaxAccountData>(
            "getInfo", IndodaxEmptyParameters.Instance, true, false,
            cancellationToken);

    public ValueTask<IndodaxOrdersData> GetOpenOrdersAsync(string pair,
        CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxOpenOrdersParameters, IndodaxOrdersData>(
            "openOrders", new() { Pair = pair }, true, false,
            cancellationToken);

    public ValueTask<IndodaxOrderData> GetOrderAsync(string pair,
        string orderId, CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxOrderParameters, IndodaxOrderData>(
            "getOrder", new() { Pair = pair, OrderId = orderId }, true, false,
            cancellationToken);

    public ValueTask<IndodaxOrderData> GetOrderByClientIdAsync(
        string clientOrderId, CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxClientOrderParameters, IndodaxOrderData>(
            "getOrderByClientOrderId", new()
            {
                ClientOrderId = clientOrderId,
            }, true, false, cancellationToken);

    public ValueTask<IndodaxPlaceOrderData> PlaceOrderAsync(
        IndodaxTradeParameters parameters,
        CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxTradeParameters, IndodaxPlaceOrderData>(
            "trade", parameters, false, false, cancellationToken);

    public ValueTask<IndodaxCancelOrderData> CancelOrderAsync(
        IndodaxCancelOrderParameters parameters,
        CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxCancelOrderParameters,
            IndodaxCancelOrderData>("cancelOrder", parameters, false, true,
            cancellationToken);

    public ValueTask<IndodaxCancelOrderData> CancelByClientIdAsync(
        string clientOrderId, CancellationToken cancellationToken)
        => SendTapiAsync<IndodaxClientOrderParameters,
            IndodaxCancelOrderData>("cancelByClientOrderId", new()
            {
                ClientOrderId = clientOrderId,
            }, false, true, cancellationToken);

    public ValueTask<IndodaxV2Order[]> GetOrderHistoryAsync(
        IndodaxHistoryParameters parameters,
        CancellationToken cancellationToken)
        => SendV2Async<IndodaxHistoryParameters, IndodaxV2Order[]>(
            "/api/v2/order/histories", parameters, cancellationToken);

    public ValueTask<IndodaxV2Trade[]> GetTradeHistoryAsync(
        IndodaxHistoryParameters parameters,
        CancellationToken cancellationToken)
        => SendV2Async<IndodaxHistoryParameters, IndodaxV2Trade[]>(
            "/api/v2/myTrades", parameters, cancellationToken);

    public ValueTask<IndodaxTokenData> GeneratePrivateTokenAsync(
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendTokenAsync(new() { ApiKey = _apiKey }, cancellationToken);
    }

    public long GetTimestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            Interlocked.Read(ref _serverTimeOffset);

    private async ValueTask<TData> SendPublicAsync<TData>(string path,
        string query, CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1; attempt <= _maximumReadAttempts; attempt++)
        {
            try
            {
                await _publicRate.WaitAsync(cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    CreateUri(_publicEndpoint, path, query));
                return await SendRawAsync<TData>(request, cancellationToken);
            }
            catch (Exception error) when (attempt < _maximumReadAttempts &&
                IsRetryableRead(error))
            {
                lastError = error;
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            "Indodax public request failed.");
    }

    private async ValueTask<TData> SendTapiAsync<TParameters, TData>(
        string method, TParameters parameters, bool isSafeRead, bool isCancel,
        CancellationToken cancellationToken)
        where TParameters : IndodaxParameters
    {
        ArgumentNullException.ThrowIfNull(parameters);
        EnsureCredentials();
        Exception lastError = null;
        var attempts = isSafeRead ? _maximumReadAttempts : 2;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await (isCancel ? _cancelRate : _privateRate)
                    .WaitAsync(cancellationToken);
                var body = new IndodaxFormWriter()
                    .Add("method", method)
                    .Add("timestamp", GetTimestamp())
                    .Add("recvWindow", _receiveWindow);
                parameters.Append(body);
                var payload = body.ToString();
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    CreateUri(_publicEndpoint, "/tapi", null))
                {
                    Content = new StringContent(payload, Encoding.UTF8,
                        "application/x-www-form-urlencoded"),
                };
                request.Headers.TryAddWithoutValidation("Key", _apiKey);
                request.Headers.TryAddWithoutValidation("Sign", Sign(payload));
                return await SendTapiResponseAsync<TData>(request,
                    cancellationToken);
            }
            catch (Exception error) when (attempt < attempts &&
                (isSafeRead ? IsRetryableRead(error) :
                    IsDefiniteRejection(error)))
            {
                lastError = error;
                if (error is IndodaxApiException api &&
                    api.Code.EqualsIgnoreCase("invalid_timestamp"))
                    await SynchronizeTimeAsync(cancellationToken);
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            $"Indodax {method} request failed.");
    }

    private async ValueTask<TData> SendV2Async<TParameters, TData>(
        string path, TParameters parameters,
        CancellationToken cancellationToken)
        where TParameters : IndodaxParameters
    {
        ArgumentNullException.ThrowIfNull(parameters);
        EnsureCredentials();
        Exception lastError = null;
        for (var attempt = 1; attempt <= _maximumReadAttempts; attempt++)
        {
            try
            {
                await _privateRate.WaitAsync(cancellationToken);
                var query = new IndodaxFormWriter();
                parameters.Append(query);
                query.Add("timestamp", GetTimestamp());
                query.Add("recvWindow", _receiveWindow);
                var payload = query.ToString();
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    CreateUri(_historyEndpoint, path, payload));
                request.Headers.TryAddWithoutValidation("X-APIKEY", _apiKey);
                request.Headers.TryAddWithoutValidation("Sign", Sign(payload));
                return await SendV2ResponseAsync<TData>(request,
                    cancellationToken);
            }
            catch (Exception error) when (attempt < _maximumReadAttempts &&
                IsRetryableRead(error))
            {
                lastError = error;
                if (error is IndodaxApiException api && api.Code == "1002")
                    await SynchronizeTimeAsync(cancellationToken);
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            "Indodax Trade API v2 request failed.");
    }

    private async ValueTask<IndodaxTokenData> SendTokenAsync(
        IndodaxPrivateTokenParameters parameters,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1; attempt <= _maximumReadAttempts; attempt++)
        {
            try
            {
                await _privateRate.WaitAsync(cancellationToken);
                var writer = new IndodaxFormWriter();
                parameters.Append(writer);
                var payload = writer.ToString();
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    CreateUri(_publicEndpoint,
                        "/api/private_ws/v1/generate_token", null))
                {
                    Content = new StringContent(payload, Encoding.UTF8,
                        "application/x-www-form-urlencoded"),
                };
                request.Headers.TryAddWithoutValidation("Sign", Sign(payload));
                return await SendTapiResponseAsync<IndodaxTokenData>(request,
                    cancellationToken);
            }
            catch (Exception error) when (attempt < _maximumReadAttempts &&
                IsRetryableRead(error))
            {
                lastError = error;
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            "Indodax private-token request failed.");
    }

    private async ValueTask<TData> SendRawAsync<TData>(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (response, payload) = await SendHttpAsync(request,
            cancellationToken);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw CreateApiError(response.StatusCode, payload,
                    response.ReasonPhrase);
            try
            {
                return JsonConvert.DeserializeObject<TData>(payload,
                    _jsonSettings);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    "Indodax returned invalid JSON.", error);
            }
        }
    }

    private async ValueTask<TData> SendTapiResponseAsync<TData>(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (response, payload) = await SendHttpAsync(request,
            cancellationToken);
        using (response)
        {
            IndodaxResponse<TData> envelope = null;
            try
            {
                envelope = JsonConvert.DeserializeObject<
                    IndodaxResponse<TData>>(payload, _jsonSettings);
            }
            catch (JsonException error) when (response.IsSuccessStatusCode)
            {
                throw new InvalidDataException(
                    "Indodax TAPI returned invalid JSON.", error);
            }
            catch (JsonException)
            {
            }

            if (!response.IsSuccessStatusCode || envelope?.Success != 1)
                throw new IndodaxApiException(response.StatusCode,
                    envelope?.ErrorCode ??
                        ((int)response.StatusCode).ToString(
                            CultureInfo.InvariantCulture),
                    envelope?.Error ?? envelope?.Message ??
                        Truncate(payload, 512));
            return envelope.Data;
        }
    }

    private async ValueTask<TData> SendV2ResponseAsync<TData>(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (response, payload) = await SendHttpAsync(request,
            cancellationToken);
        using (response)
        {
            IndodaxV2Response<TData> envelope = null;
            try
            {
                envelope = JsonConvert.DeserializeObject<
                    IndodaxV2Response<TData>>(payload, _jsonSettings);
            }
            catch (JsonException error) when (response.IsSuccessStatusCode)
            {
                throw new InvalidDataException(
                    "Indodax Trade API v2 returned invalid JSON.", error);
            }
            catch (JsonException)
            {
            }

            if (!response.IsSuccessStatusCode || envelope is null ||
                !envelope.Error.IsEmpty())
                throw new IndodaxApiException(response.StatusCode,
                    (envelope?.Code ?? (int)response.StatusCode).ToString(
                        CultureInfo.InvariantCulture),
                    envelope?.Error ?? Truncate(payload, 512));
            return envelope.Data;
        }
    }

    private async ValueTask<(HttpResponseMessage Response, string Payload)>
        SendHttpAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        var ended = DateTime.UtcNow;
        UpdateServerTime(response, started, ended);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response, payload);
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA512(_secret);
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Indodax TAPI key and secret are required for private operations.");
    }

    private void UpdateServerTime(HttpResponseMessage response,
        DateTime started, DateTime ended)
    {
        if (response.Headers.Date is not { } serverDate)
            return;
        var midpoint = started + TimeSpan.FromTicks((ended - started).Ticks / 2);
        Interlocked.Exchange(ref _serverTimeOffset,
            (long)(serverDate.UtcDateTime - midpoint).TotalMilliseconds);
    }

    private IndodaxApiException CreateApiError(HttpStatusCode statusCode,
        string payload, string reasonPhrase)
    {
        ErrorPayload error = null;
        try
        {
            error = JsonConvert.DeserializeObject<ErrorPayload>(payload,
                _jsonSettings);
        }
        catch (JsonException)
        {
        }
        return new(statusCode,
            error?.ErrorCode ?? error?.Code?.ToString(
                CultureInfo.InvariantCulture) ??
                ((int)statusCode).ToString(CultureInfo.InvariantCulture),
            error?.Error ?? error?.Message ?? reasonPhrase ??
                Truncate(payload, 512));
    }

    private static bool IsRetryableRead(Exception error)
        => error switch
        {
            IndodaxApiException api =>
                api.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)api.StatusCode >= 500 ||
                api.Code is "1000" or "1002" or "invalid_timestamp" or
                    "too_many_requests",
            HttpRequestException => true,
            TaskCanceledException => true,
            _ => false,
        };

    private static bool IsDefiniteRejection(Exception error)
        => error is IndodaxApiException api &&
            (api.StatusCode == HttpStatusCode.TooManyRequests ||
            api.Code is "1002" or "invalid_timestamp" or
                "too_many_requests");

    private static TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(5000, 250 * (1 << attempt)));

    private static string Truncate(string value, int length)
        => value.IsEmpty() || value.Length <= length ? value : value[..length];

    private static Uri CreateUri(Uri endpoint, string path, string query)
    {
        var builder = new UriBuilder(new Uri(endpoint, path.TrimStart('/')));
        if (!query.IsEmpty())
            builder.Query = query;
        return builder.Uri;
    }

    private static Uri ValidateEndpoint(string value, string name)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!value.EndsWith('/'))
            value += "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("https"))
            throw new ArgumentException(
                $"Indodax {name} endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }
}

sealed class IndodaxRateGate(int limit, TimeSpan interval) : IDisposable
{
    private readonly int _limit = limit > 0
        ? limit
        : throw new ArgumentOutOfRangeException(nameof(limit));
    private readonly TimeSpan _interval = interval > TimeSpan.Zero
        ? interval
        : throw new ArgumentOutOfRangeException(nameof(interval));
    private readonly Queue<DateTime> _requests = new();
    private readonly SemaphoreSlim _sync = new(1, 1);

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            await _sync.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                while (_requests.Count > 0 &&
                    now - _requests.Peek() >= _interval)
                    _requests.Dequeue();
                if (_requests.Count < _limit)
                {
                    _requests.Enqueue(now);
                    return;
                }
                delay = _interval - (now - _requests.Peek());
            }
            finally
            {
                _sync.Release();
            }
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }
    }

    public void Dispose() => _sync.Dispose();
}
