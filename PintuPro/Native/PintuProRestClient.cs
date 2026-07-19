namespace StockSharp.PintuPro.Native;

sealed class PintuProApiException(HttpStatusCode statusCode, string code,
    string message, string reason)
    : InvalidOperationException(
        $"Pintu Pro API error {(int)statusCode} ({statusCode}), code {code}: " +
        $"{message}{(reason.IsEmpty() ? string.Empty : $" ({reason})")}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

sealed class PintuProRestClient : BaseLogReceiver
{
    private const int _maximumReadAttempts = 4;
    private const int _tokensPerSecond = 50_000;

    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly byte[] _secret;
    private readonly SemaphoreSlim _rateSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private DateTime _rateWindow = DateTime.UtcNow;
    private int _usedTokens;
    private long _serverTimeOffset;

    public PintuProRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretValue.IsEmpty())
            throw new ArgumentException(
                "Pintu Pro API key and HMAC secret must be configured together.");
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
            "StockSharp-PintuPro-Connector/1.0");
    }

    public override string Name => "PintuPro_Rest";

    public bool IsCredentialsAvailable
        => !_apiKey.IsEmpty() && _secret is not null;

    protected override void DisposeManaged()
    {
        if (_secret is not null)
            CryptographicOperations.ZeroMemory(_secret);
        _rateSync.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<PintuProSymbolsData> GetSymbolsAsync(
        CancellationToken cancellationToken)
        => SendPublicAsync<PintuProSymbolsData>(
            "/v1/public/get-symbols-reference", null, 100,
            cancellationToken);

    public ValueTask<PintuProBookData> GetBookAsync(string symbol, int depth,
        CancellationToken cancellationToken)
    {
        if (depth is not 1 and not 10)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "Pintu Pro order-book depth must be 1 or 10.");
        var query = new PintuProQueryWriter()
            .Add("symbol", symbol.NormalizeSymbol())
            .Add("depth", depth)
            .ToString();
        return SendPublicAsync<PintuProBookData>("/v1/public/get-book",
            query, 500, cancellationToken);
    }

    public ValueTask<PintuProPublicTradesData> GetTradesAsync(string symbol,
        CancellationToken cancellationToken)
    {
        var query = new PintuProQueryWriter()
            .Add("symbol", symbol.NormalizeSymbol())
            .ToString();
        return SendPublicAsync<PintuProPublicTradesData>(
            "/v1/public/get-trades", query, 100, cancellationToken);
    }

    public ValueTask<PintuProPlaceOrderData> PlaceOrderAsync(
        PintuProPlaceOrderParams parameters,
        CancellationToken cancellationToken)
        => SendPrivateAsync<PintuProPlaceOrderParams, PintuProPlaceOrderData>(
            "private/place-order", parameters, 1000, false,
            cancellationToken);

    public async ValueTask CancelOrderAsync(
        PintuProCancelOrderParams parameters,
        CancellationToken cancellationToken)
        => _ = await SendPrivateAsync<PintuProCancelOrderParams,
            PintuProOperationResult>("private/cancel-order", parameters, 1000,
            false, cancellationToken);

    public async ValueTask CancelAllOrdersAsync(
        PintuProSymbolParams parameters,
        CancellationToken cancellationToken)
        => _ = await SendPrivateAsync<PintuProSymbolParams,
            PintuProOperationResult>("private/cancel-all-orders", parameters,
            1250, false, cancellationToken);

    public ValueTask<PintuProAccountData> GetAccountAsync(
        CancellationToken cancellationToken)
        => SendPrivateAsync<PintuProEmptyParams, PintuProAccountData>(
            "private/get-account-information", PintuProEmptyParams.Instance,
            100, true, cancellationToken);

    public ValueTask<PintuProOrdersData> GetOpenOrdersAsync(
        PintuProOpenOrdersParams parameters,
        CancellationToken cancellationToken)
        => SendPrivateAsync<PintuProOpenOrdersParams, PintuProOrdersData>(
            "private/get-open-orders", parameters, 100, true,
            cancellationToken);

    public ValueTask<PintuProOrdersData> GetOrderHistoryAsync(
        PintuProHistoryParams parameters,
        CancellationToken cancellationToken)
        => SendPrivateAsync<PintuProHistoryParams, PintuProOrdersData>(
            "private/get-order-history", parameters, 1000, true,
            cancellationToken);

    public ValueTask<PintuProAccountTradesData> GetTradeHistoryAsync(
        PintuProHistoryParams parameters,
        CancellationToken cancellationToken)
        => SendPrivateAsync<PintuProHistoryParams, PintuProAccountTradesData>(
            "private/get-trade-history", parameters, 1000, true,
            cancellationToken);

    public ValueTask<PintuProOrderDetailsData> GetOrderDetailsAsync(
        PintuProOrderDetailsParams parameters,
        CancellationToken cancellationToken)
        => SendPrivateAsync<PintuProOrderDetailsParams,
            PintuProOrderDetailsData>("private/get-order-details", parameters,
            300, true, cancellationToken);

    public PintuProSocketAuthentication CreateSocketAuthentication()
    {
        EnsureCredentials();
        var requestId = Guid.NewGuid().ToString();
        var timestamp = GetTimestamp();
        const string method = "public/auth";
        return new(requestId, timestamp, _apiKey,
            Sign(requestId, timestamp, method, PintuProEmptyParams.Instance));
    }

    public long GetTimestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            Interlocked.Read(ref _serverTimeOffset);

    private async ValueTask<TData> SendPublicAsync<TData>(string path,
        string query, int cost, CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 1; attempt <= _maximumReadAttempts; attempt++)
        {
            try
            {
                await WaitAsync(cost, cancellationToken);
                var uri = CreateUri(path, query);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                return await SendAsync<TData>(request, cancellationToken);
            }
            catch (Exception error) when (attempt < _maximumReadAttempts &&
                IsRetryable(error))
            {
                lastError = error;
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            "Pintu Pro public request failed.");
    }

    private async ValueTask<TData> SendPrivateAsync<TParams, TData>(
        string method, TParams parameters, int cost, bool isSafeRead,
        CancellationToken cancellationToken)
        where TParams : PintuProParameters
    {
        ArgumentNullException.ThrowIfNull(parameters);
        EnsureCredentials();
        var attempts = _maximumReadAttempts;
        Exception lastError = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await WaitAsync(cost, cancellationToken);
                var requestId = Guid.NewGuid().ToString();
                var timestamp = GetTimestamp();
                var payload = new PintuProRequest<TParams>
                {
                    RequestId = requestId,
                    Timestamp = timestamp,
                    Method = method,
                    ApiKey = _apiKey,
                    Parameters = parameters,
                    Signature = Sign(requestId, timestamp, method, parameters),
                };
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    CreateUri($"/v1/{method}", null))
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload,
                        _jsonSettings), Encoding.UTF8, "application/json"),
                };
                return await SendAsync<TData>(request, cancellationToken);
            }
            catch (Exception error) when (attempt < attempts &&
                (isSafeRead ? IsRetryable(error) :
                    IsDefiniteRejection(error)))
            {
                lastError = error;
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            "Pintu Pro private request failed.");
    }

    private async ValueTask<TData> SendAsync<TData>(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        var ended = DateTime.UtcNow;
        UpdateServerTime(response, started, ended);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        PintuProResponse<TData> envelope = null;
        if (!payload.IsEmpty())
        {
            try
            {
                envelope = JsonConvert.DeserializeObject<
                    PintuProResponse<TData>>(payload, _jsonSettings);
            }
            catch (JsonException error) when (response.IsSuccessStatusCode)
            {
                throw new InvalidDataException(
                    "Pintu Pro returned invalid JSON.", error);
            }
            catch (JsonException)
            {
            }
        }

        if (!response.IsSuccessStatusCode || envelope is null ||
            !IsSuccessCode(envelope.Code))
        {
            var code = envelope?.Code ?? ((int)response.StatusCode).ToString(
                CultureInfo.InvariantCulture);
            throw new PintuProApiException(response.StatusCode, code,
                envelope?.Message ?? response.ReasonPhrase,
                envelope?.Reason ?? Truncate(payload, 512));
        }
        if (envelope.Data is IPintuProServerTimestamp timestamped)
            timestamped.ServerTimestamp = envelope.Timestamp;
        return envelope.Data;
    }

    private async ValueTask WaitAsync(int cost,
        CancellationToken cancellationToken)
    {
        if (cost <= 0 || cost > _tokensPerSecond)
            throw new ArgumentOutOfRangeException(nameof(cost), cost, null);
        while (true)
        {
            TimeSpan delay;
            await _rateSync.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _rateWindow;
                if (elapsed >= TimeSpan.FromSeconds(1))
                {
                    _rateWindow = now;
                    _usedTokens = 0;
                }
                if (_usedTokens + cost <= _tokensPerSecond)
                {
                    _usedTokens += cost;
                    return;
                }
                delay = TimeSpan.FromSeconds(1) - elapsed;
            }
            finally
            {
                _rateSync.Release();
            }
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }
    }

    private string Sign<TParams>(string requestId, long timestamp,
        string method, TParams parameters)
        where TParams : PintuProParameters
    {
        var payload = new StringBuilder(requestId)
            .Append(timestamp.ToString(CultureInfo.InvariantCulture))
            .Append(method)
            .Append(_apiKey);
        parameters.AppendSignature(payload);
        using var hmac = new HMACSHA256(_secret);
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(payload.ToString()))).ToLowerInvariant();
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Pintu Pro API key and HMAC secret are required for private operations.");
    }

    private Uri CreateUri(string path, string query)
    {
        var builder = new UriBuilder(new Uri(_endpoint,
            path.TrimStart('/')));
        if (!query.IsEmpty())
            builder.Query = query;
        return builder.Uri;
    }

    private void UpdateServerTime(HttpResponseMessage response,
        DateTime started, DateTime ended)
    {
        if (response.Headers.Date is not { } serverDate)
            return;
        var midpoint = started + TimeSpan.FromTicks((ended - started).Ticks / 2);
        var offset = (long)(serverDate.UtcDateTime - midpoint).TotalMilliseconds;
        Interlocked.Exchange(ref _serverTimeOffset, offset);
    }

    private static bool IsSuccessCode(string code)
        => code.IsEmpty() || code.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);

    private static bool IsRetryable(Exception error)
        => error switch
        {
            PintuProApiException api => api.StatusCode ==
                HttpStatusCode.TooManyRequests ||
                (int)api.StatusCode >= 500 || api.Code is "11" or "17",
            HttpRequestException => true,
            TaskCanceledException => true,
            _ => false,
        };

    private static bool IsDefiniteRejection(Exception error)
        => error is PintuProApiException api &&
            (api.StatusCode == HttpStatusCode.TooManyRequests ||
            api.Code is "11" or "17");

    private static TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(5000, 250 * (1 << attempt)));

    private static string Truncate(string value, int length)
        => value.IsEmpty() || value.Length <= length ? value : value[..length];

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!value.EndsWith('/'))
            value += "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("https"))
            throw new ArgumentException(
                "Pintu Pro REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }
}
