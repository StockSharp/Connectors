namespace StockSharp.Rain.Native;

sealed class RainRestClient : BaseLogReceiver
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
    private readonly string _accessToken;
    private readonly RateGate _readGate = new(TimeSpan.FromMilliseconds(200));
    private readonly RateGate _writeGate = new(TimeSpan.FromMilliseconds(100));
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

    public RainRestClient(string endpoint, SecureString key,
        SecureString secret, SecureString accessToken)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        _accessToken = accessToken.IsEmpty()
            ? null
            : accessToken.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "Rain API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Rain-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
    }

    public override string Name => "Rain_REST";

    public bool IsPrivateAvailable => !_apiKey.IsEmpty() &&
        !_accessToken.IsEmpty();

    public string AccessToken => _accessToken;

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _readGate.Dispose();
        _writeGate.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public ValueTask<RainProduct[]> GetProductsAsync(
        CancellationToken cancellationToken)
        => SendAsync<RainProduct[]>(HttpMethod.Get,
            "/api/1/pro/products", null, null, false, cancellationToken);

    public ValueTask<RainCandle[]> GetCandlesAsync(string symbol,
        TimeSpan timeFrame, DateTime from, DateTime to, int count,
        CancellationToken cancellationToken)
    {
        var path = $"/api/1/pro/candles/" +
            $"{symbol.NormalizeSymbol().EscapePath()}/" +
            timeFrame.ToWire().EscapePath();
        var query = "from=" + Uri.EscapeDataString(from.ToUtcTime().ToString(
            "O", CultureInfo.InvariantCulture)) + "&to=" +
            Uri.EscapeDataString(to.ToUtcTime().ToString("O",
                CultureInfo.InvariantCulture)) + "&count=" +
            count.Min(500).Max(1).ToString(CultureInfo.InvariantCulture);
        return SendAsync<RainCandle[]>(HttpMethod.Get, path, query, null,
            false, cancellationToken);
    }

    public async ValueTask<RainAccount[]> GetAccountsAsync(
        CancellationToken cancellationToken)
        => (await SendPrivateAsync<RainAccounts>(HttpMethod.Get,
            "/api/3/accounts", null, null, cancellationToken)).Accounts ?? [];

    public async ValueTask<RainOrder[]> GetClosedOrdersAsync(int limit,
        int offset, CancellationToken cancellationToken)
        => (await SendPrivateAsync<RainClosedOrders>(HttpMethod.Get,
            $"/api/1/pro/orders/closed/{limit.Min(100).Max(1)}/" +
            offset.Max(0).ToString(CultureInfo.InvariantCulture), null, null,
            cancellationToken)).Orders ?? [];

    public ValueTask<RainOrder> GetOrderAsync(string clientOrderId,
        CancellationToken cancellationToken)
        => SendPrivateAsync<RainOrder>(HttpMethod.Get,
            "/api/1/pro/orders/" + clientOrderId.EscapePath(), null, null,
            cancellationToken);

    public async ValueTask<RainOrder> PlaceOrderAsync(
        RainPlaceOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonConvert.SerializeObject(request, _jsonSettings);
        var response = await SendPrivateAsync<RainPlaceOrderResponse>(
            HttpMethod.Post, "/api/1/pro/orders", null, body,
            cancellationToken);
        return response.Order ?? response;
    }

    public ValueTask CancelOrderAsync(string clientOrderId,
        CancellationToken cancellationToken)
        => SendPrivateEmptyAsync(HttpMethod.Delete,
            "/api/1/pro/orders/" + clientOrderId.EscapePath(),
            cancellationToken);

    private async ValueTask<TResponse> SendPrivateAsync<TResponse>(
        HttpMethod method, string path, string query, string body,
        CancellationToken cancellationToken)
    {
        EnsurePrivateAvailable();
        return await SendAsync<TResponse>(method, path, query, body, true,
            cancellationToken);
    }

    private async ValueTask SendPrivateEmptyAsync(HttpMethod method,
        string path, CancellationToken cancellationToken)
    {
        EnsurePrivateAvailable();
        _ = await SendAsync<RainEmptyResponse>(method, path, null, null, true,
            cancellationToken);
    }

    private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
        string path, string query, string body, bool isPrivate,
        CancellationToken cancellationToken)
    {
        var requestPath = query.IsEmpty() ? path : $"{path}?{query}";
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
                AddAuthentication(request, method.Method, path, body);
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
        string path, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(
            CultureInfo.InvariantCulture);
        var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        var contentHashBytes = SHA512.HashData(bodyBytes);
        var contentHash = Convert.ToHexString(contentHashBytes)
            .ToLowerInvariant();
        var suffix = Encoding.UTF8.GetBytes(timestamp + contentHash +
            method.ToUpperInvariant() + path);
        var signing = new byte[_apiSecret.Length + suffix.Length];
        Buffer.BlockCopy(_apiSecret, 0, signing, 0, _apiSecret.Length);
        Buffer.BlockCopy(suffix, 0, signing, _apiSecret.Length,
            suffix.Length);
        try
        {
            using var hmac = new HMACSHA512(_apiSecret);
            var signature = Convert.ToHexString(hmac.ComputeHash(signing))
                .ToLowerInvariant();
            request.Headers.Authorization = new("Bearer", _accessToken);
            request.Headers.TryAddWithoutValidation("api-key", _apiKey);
            request.Headers.TryAddWithoutValidation("api-content-hash",
                contentHash);
            request.Headers.TryAddWithoutValidation("api-timestamp",
                timestamp);
            request.Headers.TryAddWithoutValidation("api-signature",
                signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signing);
            CryptographicOperations.ZeroMemory(bodyBytes);
            CryptographicOperations.ZeroMemory(contentHashBytes);
            CryptographicOperations.ZeroMemory(suffix);
        }
    }

    private TResponse Deserialize<TResponse>(string body)
    {
        if (body.IsEmpty())
        {
            if (typeof(TResponse) == typeof(RainEmptyResponse))
                return (TResponse)(object)new RainEmptyResponse();
            if (typeof(TResponse) == typeof(RainPlaceOrderResponse))
                return (TResponse)(object)new RainPlaceOrderResponse();
            throw new InvalidDataException("Rain returned an empty response.");
        }
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(body,
                _jsonSettings) ?? throw new InvalidDataException(
                "Rain returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Rain returned an unexpected response shape.", error);
        }
    }

    private Exception CreateApiError(HttpStatusCode statusCode, string body)
    {
        RainErrorResponse response = null;
        try
        {
            response = JsonConvert.DeserializeObject<RainErrorResponse>(body,
                _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var message = response?.Message ?? response?.Error;
        if (response?.Errors is { Length: > 0 })
        {
            var details = string.Join("; ", response.Errors.Where(
                static value => value is not null).Select(value =>
                value.Code.IsEmpty() ? value.Message :
                    $"{value.Code}: {value.Message}"));
            if (!details.IsEmpty())
                message = message.IsEmpty() ? details : $"{message} {details}";
        }
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new RainApiException(statusCode,
            $"Rain HTTP {(int)statusCode}: {message}");
    }

    private void EnsurePrivateAvailable()
    {
        if (!IsPrivateAvailable)
            throw new InvalidOperationException(
                "Rain API key, secret, and access token are required for private operations.");
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "Rain REST endpoint must be an absolute HTTPS URI.",
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

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "Rain response exceeds the 8 MiB safety limit.");
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
                    "Rain response exceeds the 8 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
