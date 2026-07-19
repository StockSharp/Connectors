namespace StockSharp.Coinhako.Native;

sealed class CoinhakoApiException(HttpStatusCode statusCode, string message)
    : InvalidOperationException(
        $"Coinhako API error {(int)statusCode} ({statusCode}): {message}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class CoinhakoRestClient : BaseLogReceiver
{
    private const string _algorithm = "ecdsa-secp256k1-sha256";
    private const int _maximumReadAttempts = 4;
    private const int _maximumPayloadLength = 8 * 1024 * 1024;

    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ECDsa _signer;
    private readonly Lock _signSync = new();
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };

    public CoinhakoRestClient(string endpoint, SecureString key,
        SecureString secret)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var privateKey = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != privateKey.IsEmpty())
            throw new ArgumentException(
                "Coinhako public and private API keys must be configured together.");
        if (!_apiKey.IsEmpty())
        {
            if (_apiKey.Length % 2 != 0 ||
                !_apiKey.All(static character => Uri.IsHexDigit(character)))
                throw new ArgumentException(
                    "Coinhako X-API-KEY must be hexadecimal.", nameof(key));
            _signer = ECDsa.Create();
            try
            {
                _signer.ImportFromPem(privateKey);
                var curve = _signer.ExportParameters(false).Curve.Oid;
                if (curve.Value != "1.3.132.0.10" &&
                    !curve.FriendlyName.EqualsIgnoreCase("secp256k1"))
                    throw new CryptographicException(
                        "Coinhako requires an ECDSA secp256k1 private key.");
            }
            catch
            {
                _signer.Dispose();
                throw;
            }
        }

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
            "StockSharp-Coinhako-Connector/1.0");
    }

    public override string Name => "Coinhako_Rest";

    public bool IsCredentialsAvailable
        => !_apiKey.IsEmpty() && _signer is not null;

    protected override void DisposeManaged()
    {
        _signer?.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<CoinhakoSpotPrice[]> GetSpotsAsync(string baseCurrency,
        string counterCurrency, CancellationToken cancellationToken)
        => SendGetAsync<CoinhakoSpotPrice[]>("/public_api/v1/price/spots",
            new CoinhakoSpotsQuery
            {
                BaseCurrency = baseCurrency,
                CounterCurrency = counterCurrency,
            }, false, cancellationToken);

    public ValueTask<CoinhakoBalance[]> GetBalancesAsync(string currency,
        CancellationToken cancellationToken)
        => SendGetAsync<CoinhakoBalance[]>(
            "/public_api/v1/accounts/balances",
            new CoinhakoBalanceQuery { Currency = currency }, true,
            cancellationToken);

    public ValueTask<CoinhakoOrder[]> GetOrdersAsync(
        CoinhakoOrdersQuery parameters,
        CancellationToken cancellationToken)
        => SendGetAsync<CoinhakoOrder[]>("/public_api/v1/orders", parameters,
            true, cancellationToken);

    public ValueTask<CoinhakoOrder> GetOrderAsync(long orderId,
        CancellationToken cancellationToken)
        => SendGetAsync<CoinhakoOrder>(
            $"/public_api/v1/orders/{orderId.ToString(CultureInfo.InvariantCulture)}",
            null, true, cancellationToken);

    public ValueTask<CoinhakoOrderQuote> CreateQuoteAsync(
        CoinhakoOrderQuoteRequest request,
        CancellationToken cancellationToken)
        => SendPostAsync<CoinhakoOrderQuoteRequest, CoinhakoOrderQuote>(
            "/public_api/v1/order_quotes", request, false,
            cancellationToken);

    public ValueTask<CoinhakoOrder> CreateOrderAsync(
        CoinhakoOrderRequest request, CancellationToken cancellationToken)
        => SendPostAsync<CoinhakoOrderRequest, CoinhakoOrder>(
            "/public_api/v1/orders", request, false, cancellationToken);

    public ValueTask<CoinhakoOrder> CancelOrderAsync(long orderId,
        CancellationToken cancellationToken)
        => SendDeleteAsync<CoinhakoOrder>(
            $"/public_api/v1/orders/{orderId.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);

    private async ValueTask<TResponse> SendGetAsync<TResponse>(string path,
        CoinhakoQueryParameters parameters, bool isAuthenticated,
        CancellationToken cancellationToken)
    {
        var query = new CoinhakoQueryWriter();
        parameters?.Append(query);
        return await SendAsync<TResponse>(HttpMethod.Get,
            CreateUri(path, query.ToString()), null, isAuthenticated, true,
            cancellationToken);
    }

    private ValueTask<TResponse> SendPostAsync<TRequest, TResponse>(string path,
        TRequest payload, bool isRetryable,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        return SendAsync<TResponse>(HttpMethod.Post, CreateUri(path, null),
            JsonConvert.SerializeObject(payload, _jsonSettings), true,
            isRetryable, cancellationToken);
    }

    private ValueTask<TResponse> SendDeleteAsync<TResponse>(string path,
        CancellationToken cancellationToken)
        => SendAsync<TResponse>(HttpMethod.Delete, CreateUri(path, null), null,
            true, false, cancellationToken);

    private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
        Uri uri, string body, bool isAuthenticated, bool isRetryable,
        CancellationToken cancellationToken)
    {
        if (isAuthenticated)
            EnsureCredentials();

        Exception lastError = null;
        var attempts = isRetryable ? _maximumReadAttempts : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, uri);
                if (body is not null)
                    request.Content = new StringContent(body, Encoding.UTF8,
                        "application/json");
                if (isAuthenticated)
                    Sign(request, body ?? string.Empty);
                return await SendRawAsync<TResponse>(request,
                    cancellationToken);
            }
            catch (Exception error) when (attempt < attempts &&
                !cancellationToken.IsCancellationRequested &&
                IsRetryableRead(error))
            {
                lastError = error;
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException(
            "Coinhako API request failed.");
    }

    private async ValueTask<TResponse> SendRawAsync<TResponse>(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.Content.Headers.ContentLength > _maximumPayloadLength)
            throw new InvalidDataException(
                "Coinhako API response exceeds the size limit.");
        var payload = await response.Content.ReadAsStringAsync(
            cancellationToken);
        if (payload.Length > _maximumPayloadLength)
            throw new InvalidDataException(
                "Coinhako API response exceeds the size limit.");
        if (!response.IsSuccessStatusCode)
            throw CreateApiError(response.StatusCode, payload,
                response.ReasonPhrase);
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(payload,
                _jsonSettings);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Coinhako API returned invalid JSON.", error);
        }
    }

    private void Sign(HttpRequestMessage request, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var contentDigest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(body)));
        var signatureInput =
            $"method={request.Method.Method.ToUpperInvariant()}&target-uri={request.RequestUri.AbsoluteUri}&content-digest={contentDigest}&created={timestamp.ToString(CultureInfo.InvariantCulture)}&alg={_algorithm}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signatureInput));
        byte[] signature;
        using (_signSync.EnterScope())
            signature = _signer.SignHash(hash,
                DSASignatureFormat.Rfc3279DerSequence);
        request.Headers.TryAddWithoutValidation("X-API-KEY", _apiKey);
        request.Headers.TryAddWithoutValidation("X-API-Signature",
            Convert.ToHexString(signature));
        request.Headers.TryAddWithoutValidation("X-API-Timestamp",
            timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-API-Algorithm",
            _algorithm);
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Coinhako API public key and secp256k1 private key are required for account and trading operations.");
    }

    private CoinhakoApiException CreateApiError(HttpStatusCode statusCode,
        string payload, string reasonPhrase)
    {
        CoinhakoErrorEnvelope envelope = null;
        try
        {
            envelope = JsonConvert.DeserializeObject<CoinhakoErrorEnvelope>(
                payload, _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var message = string.Join("; ", (envelope?.Errors ?? [])
            .Where(static error => error?.Message.IsEmpty() == false)
            .Select(static error => error.Message));
        return new(statusCode, message.IsEmpty()
            ? reasonPhrase.IsEmpty() ? Truncate(payload, 512) : reasonPhrase
            : message);
    }

    private static bool IsRetryableRead(Exception error)
        => error switch
        {
            CoinhakoApiException api =>
                api.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)api.StatusCode >= 500,
            HttpRequestException => true,
            TaskCanceledException => true,
            _ => false,
        };

    private static TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(5000, 250 * (1 << attempt)));

    private static string Truncate(string value, int length)
        => value.IsEmpty() || value.Length <= length ? value : value[..length];

    private Uri CreateUri(string path, string query)
    {
        var builder = new UriBuilder(new Uri(_endpoint, path.TrimStart('/')));
        if (!query.IsEmpty())
            builder.Query = query;
        return builder.Uri;
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!value.EndsWith('/'))
            value += "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("https"))
            throw new ArgumentException(
                "Coinhako endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }
}
