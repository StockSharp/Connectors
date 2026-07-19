namespace StockSharp.MercadoBitcoin.Native;

sealed class MercadoBitcoinRestClient : BaseLogReceiver
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
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly SemaphoreSlim _tokenSync = new(1, 1);
    private readonly Lock _tokenStateSync = new();
    private readonly RateGate _publicGate = new(TimeSpan.FromMilliseconds(1050));
    private readonly RateGate _privateReadGate = new(TimeSpan.FromMilliseconds(350));
    private readonly RateGate _singleReadGate = new(TimeSpan.FromMilliseconds(1050));
    private readonly RateGate _privateWriteGate = new(TimeSpan.FromMilliseconds(350));
    private readonly RateGate _cancelAllGate = new(TimeSpan.FromSeconds(61));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private string _accessToken;
    private DateTime _accessTokenExpiresAt;

    public MercadoBitcoinRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = CreateEndpoint(endpoint);
        _clientId = key.IsEmpty() ? null : key.UnSecure().Trim();
        _clientSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_clientId.IsEmpty() != _clientSecret.IsEmpty())
            throw new ArgumentException(
                "Mercado Bitcoin client ID and secret must be configured together.");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-MercadoBitcoin-Connector/1.0");
    }

    public override string Name => "MercadoBitcoin_REST";

    public bool IsCredentialsAvailable
        => !_clientId.IsEmpty() && !_clientSecret.IsEmpty();

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _tokenSync.Dispose();
        _publicGate.Dispose();
        _privateReadGate.Dispose();
        _singleReadGate.Dispose();
        _privateWriteGate.Dispose();
        _cancelAllGate.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<MercadoBitcoinSymbols> GetSymbolsAsync(
        MercadoBitcoinSymbolsRequest request,
        CancellationToken cancellationToken)
        => GetPublicAsync<MercadoBitcoinSymbols>("symbols",
            MercadoBitcoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<MercadoBitcoinTicker[]> GetTickersAsync(
        MercadoBitcoinTickersRequest request,
        CancellationToken cancellationToken)
        => GetPublicAsync<MercadoBitcoinTicker[]>("tickers",
            MercadoBitcoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<MercadoBitcoinOrderBook> GetOrderBookAsync(string symbol,
        MercadoBitcoinOrderBookRequest request,
        CancellationToken cancellationToken)
        => GetPublicAsync<MercadoBitcoinOrderBook>(
            $"{EscapePath(symbol.NormalizeSymbol())}/orderbook",
            MercadoBitcoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<MercadoBitcoinTrade[]> GetTradesAsync(string symbol,
        MercadoBitcoinTradesRequest request,
        CancellationToken cancellationToken)
        => GetPublicAsync<MercadoBitcoinTrade[]>(
            $"{EscapePath(symbol.NormalizeSymbol())}/trades",
            MercadoBitcoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<MercadoBitcoinCandles> GetCandlesAsync(
        MercadoBitcoinCandlesRequest request,
        CancellationToken cancellationToken)
        => GetPublicAsync<MercadoBitcoinCandles>("candles",
            MercadoBitcoinQueryWriter.Create(request), cancellationToken);

    public ValueTask<MercadoBitcoinAccount[]> GetAccountsAsync(
        CancellationToken cancellationToken)
        => GetPrivateAsync<MercadoBitcoinAccount[]>("accounts", null,
            _privateReadGate, cancellationToken);

    public ValueTask<MercadoBitcoinBalance[]> GetBalancesAsync(string accountId,
        CancellationToken cancellationToken)
        => GetPrivateAsync<MercadoBitcoinBalance[]>(
            $"accounts/{EscapePath(accountId)}/balances", null,
            _privateReadGate, cancellationToken);

    public ValueTask<MercadoBitcoinOrder[]> GetOrdersAsync(string accountId,
        string symbol, MercadoBitcoinListOrdersRequest request,
        CancellationToken cancellationToken)
        => GetPrivateAsync<MercadoBitcoinOrder[]>(
            $"accounts/{EscapePath(accountId)}/{EscapePath(symbol.NormalizeSymbol())}/orders",
            MercadoBitcoinQueryWriter.Create(request), _privateReadGate,
            cancellationToken);

    public ValueTask<MercadoBitcoinOrder> GetOrderAsync(string accountId,
        string symbol, string orderId, CancellationToken cancellationToken)
        => GetPrivateAsync<MercadoBitcoinOrder>(
            $"accounts/{EscapePath(accountId)}/{EscapePath(symbol.NormalizeSymbol())}/orders/{EscapePath(orderId)}",
            null, _singleReadGate, cancellationToken);

    public ValueTask<MercadoBitcoinOrdersPage> GetAllOrdersAsync(string accountId,
        MercadoBitcoinListAllOrdersRequest request,
        CancellationToken cancellationToken)
        => GetPrivateAsync<MercadoBitcoinOrdersPage>(
            $"accounts/{EscapePath(accountId)}/orders",
            MercadoBitcoinQueryWriter.Create(request), _privateReadGate,
            cancellationToken);

    public ValueTask<MercadoBitcoinPlaceOrderResponse> PlaceOrderAsync(
        string accountId, string symbol, MercadoBitcoinPlaceOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<MercadoBitcoinPlaceOrderResponse,
            MercadoBitcoinPlaceOrderRequest>(HttpMethod.Post,
            $"accounts/{EscapePath(accountId)}/{EscapePath(symbol.NormalizeSymbol())}/orders",
            null, request, _privateWriteGate, cancellationToken);

    public ValueTask<MercadoBitcoinCancelOrderResponse> CancelOrderAsync(
        string accountId, string symbol, string orderId,
        MercadoBitcoinCancelOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateWithoutBodyAsync<MercadoBitcoinCancelOrderResponse>(
            HttpMethod.Delete,
            $"accounts/{EscapePath(accountId)}/{EscapePath(symbol.NormalizeSymbol())}/orders/{EscapePath(orderId)}",
            MercadoBitcoinQueryWriter.Create(request), _privateWriteGate,
            cancellationToken);

    public ValueTask<MercadoBitcoinCancelAllResponse> CancelAllAsync(
        string accountId, MercadoBitcoinCancelAllRequest request,
        CancellationToken cancellationToken)
        => SendPrivateWithoutBodyAsync<MercadoBitcoinCancelAllResponse>(
            HttpMethod.Delete,
            $"accounts/{EscapePath(accountId)}/cancel_all_open_orders",
            MercadoBitcoinQueryWriter.Create(request), _cancelAllGate,
            cancellationToken);

    private ValueTask<TResponse> GetPublicAsync<TResponse>(string path,
        string query, CancellationToken cancellationToken)
        => SendAsync<TResponse>(false, HttpMethod.Get, path, query, null,
            _publicGate, true, cancellationToken);

    private ValueTask<TResponse> GetPrivateAsync<TResponse>(string path,
        string query, RateGate gate, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendAsync<TResponse>(true, HttpMethod.Get, path, query, null,
            gate, true, cancellationToken);
    }

    private ValueTask<TResponse> SendPrivateAsync<TResponse, TRequest>(
        HttpMethod method, string path, string query, TRequest request,
        RateGate gate, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendAsync<TResponse>(true, method, path, query,
            Serialize(request), gate, false, cancellationToken);
    }

    private ValueTask<TResponse> SendPrivateWithoutBodyAsync<TResponse>(
        HttpMethod method, string path, string query, RateGate gate,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return SendAsync<TResponse>(true, method, path, query, null, gate,
            false, cancellationToken);
    }

    private async ValueTask<TResponse> SendAsync<TResponse>(bool isPrivate,
        HttpMethod method, string path, string query, string body, RateGate gate,
        bool canRetry, CancellationToken cancellationToken)
    {
        for (var authAttempt = 0; authAttempt < (isPrivate ? 2 : 1);
            authAttempt++)
        {
            var token = isPrivate
                ? await GetAccessTokenAsync(cancellationToken)
                : null;
            for (var attempt = 0; ; attempt++)
            {
                await gate.WaitAsync(cancellationToken);
                using var request = new HttpRequestMessage(method,
                    CreateUri(path, query));
                if (body is not null)
                    request.Content = new StringContent(body, Encoding.UTF8,
                        "application/json");
                if (!token.IsEmpty())
                    request.Headers.TryAddWithoutValidation("Authorization",
                        $"Bearer {token}");

                using var response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                if (isPrivate && response.StatusCode == HttpStatusCode.Unauthorized &&
                    authAttempt == 0)
                {
                    InvalidateAccessToken(token);
                    break;
                }
                if (canRetry && attempt < _maximumReadAttempts - 1 &&
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
        throw new InvalidOperationException(
            "Mercado Bitcoin authentication could not be renewed.");
    }

    private async ValueTask<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        using (_tokenStateSync.EnterScope())
            if (!_accessToken.IsEmpty() &&
                _accessTokenExpiresAt > DateTime.UtcNow.AddMinutes(1))
                return _accessToken;

        await _tokenSync.WaitAsync(cancellationToken);
        try
        {
            using (_tokenStateSync.EnterScope())
                if (!_accessToken.IsEmpty() &&
                    _accessTokenExpiresAt > DateTime.UtcNow.AddMinutes(1))
                    return _accessToken;

            var request = new MercadoBitcoinTokenRequest
            {
                GrantType = "client_credentials",
                Scope = "global",
                ClientId = _clientId,
                ClientSecret = _clientSecret,
            };
            var form = SerializeForm(request);
            using var message = new HttpRequestMessage(HttpMethod.Post,
                CreateUri("oauth2/token", null))
            {
                Content = new StringContent(form, Encoding.UTF8,
                    "application/x-www-form-urlencoded"),
            };
            using var response = await _http.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreateApiError(response.StatusCode, body);
            var token = Deserialize<MercadoBitcoinTokenResponse>(body);
            if (token.AccessToken.IsEmpty())
                throw new InvalidDataException(
                    "Mercado Bitcoin returned an empty access token.");
            using (_tokenStateSync.EnterScope())
            {
                _accessToken = token.AccessToken;
                _accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(
                    token.ExpiresIn > 0 ? token.ExpiresIn : 300);
                return _accessToken;
            }
        }
        finally
        {
            _tokenSync.Release();
        }
    }

    private void InvalidateAccessToken(string token)
    {
        using (_tokenStateSync.EnterScope())
            if (_accessToken == token)
            {
                _accessToken = null;
                _accessTokenExpiresAt = default;
            }
    }

    private string Serialize<TValue>(TValue value)
        => JsonConvert.SerializeObject(value, _jsonSettings);

    private TResponse Deserialize<TResponse>(string body)
    {
        if (body.IsEmpty())
            throw new InvalidDataException(
                "Mercado Bitcoin returned an empty response.");
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings) ??
                throw new InvalidDataException(
                    "Mercado Bitcoin returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Mercado Bitcoin returned an unexpected response shape.", error);
        }
    }

    private Exception CreateApiError(HttpStatusCode statusCode, string body)
    {
        MercadoBitcoinErrorResponse error = null;
        try
        {
            error = JsonConvert.DeserializeObject<MercadoBitcoinErrorResponse>(
                body, _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var code = error?.Code;
        var message = error?.Message;
        if (message.IsEmpty())
            message = error?.Data?.Description ?? error?.Data?.Error;
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new MercadoBitcoinApiException(statusCode, code,
            $"Mercado Bitcoin HTTP {(int)statusCode}" +
            (code.IsEmpty() ? string.Empty : $" ({code})") + $": {message}");
    }

    private static string SerializeForm(MercadoBitcoinTokenRequest request)
        => $"grant_type={EscapeForm(request.GrantType)}" +
            $"&scope={EscapeForm(request.Scope)}" +
            $"&client_id={EscapeForm(request.ClientId)}" +
            $"&client_secret={EscapeForm(request.ClientSecret)}";

    private static string EscapeForm(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    private static string EscapePath(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

    private static Uri CreateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Mercado Bitcoin REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private Uri CreateUri(string path, string query)
    {
        var uri = new Uri(_endpoint,
            path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
        return query.IsEmpty() ? uri : new UriBuilder(uri) { Query = query }.Uri;
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Mercado Bitcoin client ID and secret are required for private operations.");
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
