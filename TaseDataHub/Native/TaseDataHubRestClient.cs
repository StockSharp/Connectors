namespace StockSharp.TaseDataHub.Native;

sealed class TaseDataHubRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _payloadLimit = 64 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _scope;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _tokenSync = new(1, 1);
    private readonly SemaphoreSlim _requestSync = new(1, 1);

    private string _accessToken;
    private DateTimeOffset _accessTokenExpires;
    private DateTimeOffset _lastRequest;

    public TaseDataHubRestClient(
        Uri address,
        string clientId,
        string clientSecret,
        string scope,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null,
        Func<DateTimeOffset> utcNow = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _clientId = clientId.ThrowIfEmpty(nameof(clientId));
        _clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret));
        _scope = scope.ThrowIfEmpty(nameof(scope));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-TaseDataHub/1.0");
        _delay = delay ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<TaseSecurity[]> GetTradedSecurities(
        DateTime date,
        CancellationToken cancellationToken)
    {
        date = date.Date;
        var envelope = await GetJson<TaseSecuritiesEnvelope>(
            string.Format(
                CultureInfo.InvariantCulture,
                "api/v1/basic-securities/trade-securities-list/{0}/{1}/{2}",
                date.Year,
                date.Month,
                date.Day),
            null,
            "traded securities list",
            cancellationToken);
        return envelope?.Securities?.Result ?? [];
    }

    public async Task<TaseSecurityType[]> GetSecurityTypes(
        CancellationToken cancellationToken)
    {
        var envelope = await GetJson<TaseSecurityTypesEnvelope>(
            "api/v1/basic-securities/securities-types",
            null,
            "security types",
            cancellationToken);
        return envelope?.SecurityTypes?.Result ?? [];
    }

    public async Task<TaseEodRecord[]> GetEodBySecurity(
        long securityId,
        CancellationToken cancellationToken)
    {
        if (securityId <= 0 || securityId > 999999999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(securityId), securityId,
                "TASE security ID must be from 1 to 999999999.");
        }

        var envelope = await GetJson<TaseEodEnvelope>(
            "api/v1/securities/trading/eod/seven-days/by-security",
            new Dictionary<string, string>
            {
                ["securityId"] = securityId.ToString(
                    CultureInfo.InvariantCulture),
            },
            "security EOD data",
            cancellationToken);
        return envelope?.Records?.Result ?? [];
    }

    public async Task<TaseEodRecord[]> GetEodByDate(
        DateTime date,
        long? securityId,
        CancellationToken cancellationToken)
    {
        if (securityId is <= 0 or > 999999999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(securityId), securityId,
                "TASE security ID must be from 1 to 999999999.");
        }

        var query = new Dictionary<string, string>
        {
            ["date"] = date.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["securityId"] = securityId?.ToString(
                CultureInfo.InvariantCulture),
        };
        var envelope = await GetJson<TaseEodEnvelope>(
            "api/v1/securities/trading/eod/seven-days/by-date",
            query,
            "dated security EOD data",
            cancellationToken);
        return envelope?.Records?.Result ?? [];
    }

    private async Task<T> GetJson<T>(
        string path,
        IReadOnlyDictionary<string, string> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var address = BuildAddress(path, query);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = await GetAccessToken(
                forceRefresh: false,
                cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, address);
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation(
                    "accept-language", "en-US");

                using var response = await Send(
                    request, cancellationToken);
                var payload = await ReadLimited(
                    response.Content,
                    operation,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized &&
                    attempt < 3)
                {
                    InvalidateToken(token);
                    continue;
                }
                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateApiException(
                        response.StatusCode,
                        payload,
                        operation);
                }

                return Deserialize<T>(payload, operation);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"TASE Data Hub {operation} transport request " +
                        "failed after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= 3)
                {
                    throw new TimeoutException(
                        $"TASE Data Hub {operation} request timed out " +
                        "after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"TASE Data Hub {operation} exhausted its retry limit.");
    }

    private async Task<string> GetAccessToken(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && CanUseToken())
            return _accessToken;

        await _tokenSync.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && CanUseToken())
                return _accessToken;

            var token = await RequestAccessToken(cancellationToken);
            _accessToken = token.AccessToken;
            _accessTokenExpires = _utcNow().AddSeconds(
                token.ExpiresIn > 0 ? token.ExpiresIn : 300);
            return _accessToken;
        }
        finally
        {
            _tokenSync.Release();
        }
    }

    private bool CanUseToken()
        => !_accessToken.IsEmpty() &&
            _accessTokenExpires > _utcNow().AddSeconds(30);

    private void InvalidateToken(string token)
    {
        if (string.Equals(
            _accessToken, token, StringComparison.Ordinal))
        {
            _accessToken = null;
            _accessTokenExpires = default;
        }
    }

    private async Task<TaseOAuthToken> RequestAccessToken(
        CancellationToken cancellationToken)
    {
        var address = new Uri(
            _address, "oauth/oauth2/token");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, address);
                var basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        $"{_clientId}:{_clientSecret}"));
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", basic);
                request.Content = new FormUrlEncodedContent(
                [
                    new("grant_type", "client_credentials"),
                    new("scope", _scope),
                ]);

                using var response = await Send(
                    request, cancellationToken);
                var payload = await ReadLimited(
                    response.Content,
                    "OAuth token",
                    cancellationToken);
                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateApiException(
                        response.StatusCode,
                        payload,
                        "OAuth token");
                }

                var token = Deserialize<TaseOAuthToken>(
                    payload, "OAuth token");
                if (token?.AccessToken.IsEmpty() != false)
                {
                    throw new InvalidOperationException(
                        "TASE Data Hub OAuth response has no access token.");
                }

                return token;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        "TASE Data Hub OAuth transport request " +
                        "failed after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= 3)
                {
                    throw new TimeoutException(
                        "TASE Data Hub OAuth request timed out " +
                        "after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            "TASE Data Hub OAuth request exhausted its retry limit.");
    }

    private async Task<HttpResponseMessage> Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _requestSync.WaitAsync(cancellationToken);
        try
        {
            var elapsed = _utcNow() - _lastRequest;
            var wait = TimeSpan.FromMilliseconds(210) - elapsed;
            if (_lastRequest != default && wait > TimeSpan.Zero)
                await _delay(wait, cancellationToken);

            var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            _lastRequest = _utcNow();
            return response;
        }
        finally
        {
            _requestSync.Release();
        }
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var resource = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));
        if (query is null || query.Count == 0)
            return resource;

        var queryString = string.Join(
            "&",
            query
                .Where(pair => !pair.Value.IsEmpty())
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}=" +
                    Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(resource)
        {
            Query = queryString,
        }.Uri;
    }

    private T Deserialize<T>(
        byte[] payload,
        string operation)
    {
        try
        {
            var value = JsonConvert.DeserializeObject<T>(
                Encoding.UTF8.GetString(payload),
                _jsonSettings);
            return value is null
                ? throw new InvalidOperationException(
                    $"TASE Data Hub returned an empty {operation} payload.")
                : value;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"TASE Data Hub returned invalid JSON for {operation}.");
        }
    }

    private TaseDataHubApiException CreateApiException(
        HttpStatusCode statusCode,
        byte[] payload,
        string operation)
    {
        var code = ((int)statusCode).ToString(
            CultureInfo.InvariantCulture);
        var message = GetErrorMessage(payload);
        try
        {
            var root = JObject.Parse(
                Encoding.UTF8.GetString(payload));
            var apiCode = root["error"]?.ToString();
            if (apiCode.IsEmpty())
                apiCode = root["code"]?.ToString();
            if (!apiCode.IsEmpty())
                code = apiCode;

            var apiMessage =
                root["error_description"]?.ToString();
            if (apiMessage.IsEmpty())
                apiMessage = root["message"]?.ToString();
            if (apiMessage.IsEmpty())
                apiMessage = root["detail"]?.ToString();
            if (!apiMessage.IsEmpty())
                message = apiMessage;
        }
        catch (JsonException)
        {
        }

        return new TaseDataHubApiException(
            statusCode,
            code,
            $"TASE Data Hub {operation} request failed " +
            $"({(int)statusCode} {statusCode}, {code}): " +
            Sanitize(message));
    }

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > _payloadLimit)
        {
            throw new InvalidOperationException(
                $"TASE Data Hub {operation} response exceeds " +
                "the 64 MB safety limit.");
        }

        await using var source =
            await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;

            total = checked(total + read);
            if (total > _payloadLimit)
            {
                throw new InvalidOperationException(
                    $"TASE Data Hub {operation} response exceeds " +
                    "the 64 MB safety limit.");
            }

            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }

        return target.ToArray();
    }

    private string Sanitize(string value)
    {
        if (value.IsEmpty())
            return value;

        foreach (var secret in new[]
        {
            _clientId,
            _clientSecret,
            _accessToken,
        })
        {
            if (!secret.IsEmpty())
            {
                value = value.Replace(
                    secret,
                    "[redacted]",
                    StringComparison.Ordinal);
            }
        }

        return value;
    }

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";

        var body = Encoding.UTF8.GetString(payload);
        if (body.Length > 2000)
            body = body[..2000];

        return body;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is null &&
            response.Headers.RetryAfter?.Date is not null)
        {
            delay = response.Headers.RetryAfter.Date.Value -
                DateTimeOffset.UtcNow;
        }

        if (delay is not null && delay.Value > TimeSpan.Zero)
        {
            return delay > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : delay.Value;
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "TASE Data Hub address must be an absolute HTTPS URI.",
                nameof(address));
        }

        var value = address.AbsoluteUri;
        return value.EndsWith('/')
            ? address
            : new Uri(value + "/");
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _tokenSync.Dispose();
        _requestSync.Dispose();
        base.DisposeManaged();
    }
}
