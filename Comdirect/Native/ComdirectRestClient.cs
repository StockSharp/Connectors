namespace StockSharp.Comdirect.Native;

sealed class ComdirectRestClient : BaseLogReceiver
{
    private sealed class ApiResponse<T>
    {
        public T Value { get; init; }
        public ComdirectAuthenticationInfo AuthenticationInfo { get; init; }
    }

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _authSync = new(1, 1);
    private readonly SemaphoreSlim _rateSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _login;
    private readonly string _pin;
    private readonly ComdirectTanTypes _tanType;
    private readonly Func<ComdirectTanChallenge, CancellationToken,
        ValueTask<string>> _tanProvider;
    private readonly int _maxAttempts;
    private readonly string _clientSessionId = Guid.NewGuid().ToString("N");
    private string _accessToken;
    private string _refreshToken;
    private DateTime _accessExpiresAt;
    private DateTime _lastRequestAt;
    private int _requestId = 100000000;

    public ComdirectRestClient(Uri address, SecureString clientId,
        SecureString clientSecret, string login, SecureString pin,
        ComdirectTanTypes tanType,
        Func<ComdirectTanChallenge, CancellationToken, ValueTask<string>>
            tanProvider,
        int maxAttempts)
    {
        if (address is null || !address.IsAbsoluteUri ||
            address.Scheme is not ("http" or "https"))
            throw new ArgumentException(
                "A valid comdirect API address is required.", nameof(address));

        _clientId = clientId?.UnSecure().ThrowIfEmpty(nameof(clientId));
        _clientSecret = clientSecret?.UnSecure()
            .ThrowIfEmpty(nameof(clientSecret));
        _login = login.ThrowIfEmpty(nameof(login));
        _pin = pin?.UnSecure().ThrowIfEmpty(nameof(pin));
        _tanType = tanType;
        _tanProvider = tanProvider;
        _maxAttempts = Math.Max(1, maxAttempts);

        _http = new(new HttpClientHandler
        {
            UseCookies = true,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            BaseAddress = new Uri(
                address.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-comdirect/1.0");
    }

    public override string Name => "comdirect_REST";

    public async Task Authenticate(CancellationToken cancellationToken)
    {
        await _authSync.WaitAsync(cancellationToken);
        try
        {
            if (!_accessToken.IsEmpty() &&
                _accessExpiresAt > DateTime.UtcNow.AddSeconds(30))
                return;

            var initial = await RequestToken(
                [
                    new("client_id", _clientId),
                    new("client_secret", _clientSecret),
                    new("grant_type", "password"),
                    new("username", _login),
                    new("password", _pin),
                ],
                null, cancellationToken);
            SetToken(initial);

            var session = await GetSession(cancellationToken);
            if (session is null || session.Identifier.IsEmpty())
                throw new InvalidDataException(
                    "comdirect returned no session identifier.");

            if (!session.SessionTanActive || !session.Activated2FA)
            {
                session.SessionTanActive = true;
                session.Activated2FA = true;

                var validation = await Send<ComdirectSession>(
                    HttpMethod.Post,
                    $"api/session/clients/user/v1/sessions/" +
                    $"{Escape(session.Identifier)}/validate",
                    SerializeBody(session), true,
                    CreateTanTypeHeader(_tanType), null, false,
                    cancellationToken);
                var info = validation.AuthenticationInfo ??
                    throw new InvalidDataException(
                        "comdirect returned no TAN challenge.");
                var challenge = new ComdirectTanChallenge
                {
                    Id = info.Id,
                    Type = info.Typ,
                    Challenge = info.Challenge,
                    AvailableTypes = info.AvailableTypes ?? [],
                };

                var isPush =
                    info.Typ.EqualsIgnoreCase("P_TAN_PUSH");
                string tan = null;
                if (_tanProvider is not null)
                    tan = await _tanProvider(challenge, cancellationToken);

                if (!isPush)
                {
                    if (_tanProvider is null)
                        throw new InvalidOperationException(
                            $"comdirect generated a {info.Typ} challenge. " +
                            "Configure TanProvider to return its TAN.");
                    if (tan.IsEmpty())
                        throw new InvalidOperationException(
                            "TanProvider returned an empty TAN.");
                }

                await Send<ComdirectSession>(
                    HttpMethod.Patch,
                    $"api/session/clients/user/v1/sessions/" +
                    Escape(session.Identifier),
                    SerializeBody(session), true,
                    SerializeOnceInfo(info.Id), tan, false,
                    cancellationToken);
            }

            var secondary = await RequestToken(
                [
                    new("client_id", _clientId),
                    new("client_secret", _clientSecret),
                    new("grant_type", "cd_secondary"),
                    new("token", _accessToken),
                ],
                _accessToken, cancellationToken);
            SetToken(secondary);
        }
        catch
        {
            ClearToken();
            throw;
        }
        finally
        {
            _authSync.Release();
        }
    }

    public async Task Revoke(CancellationToken cancellationToken)
    {
        if (_accessToken.IsEmpty())
            return;

        try
        {
            await Send<JToken>(HttpMethod.Delete, "oauth/revoke", null,
                true, null, null, false, cancellationToken);
        }
        finally
        {
            ClearToken();
        }
    }

    public async Task<ComdirectDepot[]> GetDepots(
        CancellationToken cancellationToken)
        => (await Send<ComdirectPage<ComdirectDepot>>(
            HttpMethod.Get,
            "api/brokerage/clients/user/v3/depots",
            null, true, null, null, true, cancellationToken))
            .Value?.Values ?? [];

    public async Task<ComdirectAccountBalance[]> GetAccountBalances(
        CancellationToken cancellationToken)
        => (await Send<ComdirectPage<ComdirectAccountBalance>>(
            HttpMethod.Get,
            "api/banking/clients/user/v2/accounts/balances",
            null, true, null, null, true, cancellationToken))
            .Value?.Values ?? [];

    public async Task<ComdirectPosition[]> GetPositions(string depotId,
        CancellationToken cancellationToken)
        => (await Send<ComdirectPositionPage>(
            HttpMethod.Get,
            $"api/brokerage/v3/depots/{Escape(depotId)}/positions" +
            "?with-attr=instrument&without-attr=depot",
            null, true, null, null, true, cancellationToken))
            .Value?.Values ?? [];

    public async Task<ComdirectInstrument> GetInstrument(string instrumentId,
        CancellationToken cancellationToken)
        => (await Send<ComdirectPage<ComdirectInstrument>>(
            HttpMethod.Get,
            $"api/brokerage/v1/instruments/{Escape(instrumentId)}" +
            "?with-attr=orderDimensions&with-attr=derivativeData",
            null, true, null, null, true, cancellationToken))
            .Value?.Values?.FirstOrDefault();

    public async Task<ComdirectOrder[]> GetOrders(string depotId,
        CancellationToken cancellationToken)
        => (await Send<ComdirectPage<ComdirectOrder>>(
            HttpMethod.Get,
            $"api/brokerage/depots/{Escape(depotId)}/v3/orders" +
            "?with-attr=instrument",
            null, true, null, null, true, cancellationToken))
            .Value?.Values ?? [];

    public async Task<ComdirectOrder> GetOrder(string orderId,
        CancellationToken cancellationToken)
        => (await Send<ComdirectOrder>(
            HttpMethod.Get,
            $"api/brokerage/v3/orders/{Escape(orderId)}",
            null, true, null, null, true, cancellationToken)).Value;

    public async Task<ComdirectOrder> CreateOrder(ComdirectOrder order,
        CancellationToken cancellationToken)
    {
        var body = SerializeBody(order);
        var validation = await Send<ComdirectOrder>(
            HttpMethod.Post, "api/brokerage/v3/orders/validation",
            body, true, null, null, true, cancellationToken);
        var challengeId = RequireChallenge(validation.AuthenticationInfo);

        return (await Send<ComdirectOrder>(
            HttpMethod.Post, "api/brokerage/v3/orders",
            body, true, SerializeOnceInfo(challengeId), null, true,
            cancellationToken)).Value;
    }

    public async Task<ComdirectOrder> UpdateOrder(string orderId,
        ComdirectOrder changes, CancellationToken cancellationToken)
    {
        var body = SerializeBody(changes);
        var path = $"api/brokerage/v3/orders/{Escape(orderId)}";
        var validation = await Send<ComdirectOrder>(
            HttpMethod.Post, path + "/validation",
            body, true, null, null, true, cancellationToken);
        var challengeId = RequireChallenge(validation.AuthenticationInfo);

        return (await Send<ComdirectOrder>(
            HttpMethod.Patch, path, body, true,
            SerializeOnceInfo(challengeId), null, true,
            cancellationToken)).Value;
    }

    public async Task DeleteOrder(string orderId,
        CancellationToken cancellationToken)
    {
        var path = $"api/brokerage/v3/orders/{Escape(orderId)}";
        var validation = await Send<JToken>(
            HttpMethod.Post, path + "/validation", "{}",
            true, null, null, true, cancellationToken);
        var challengeId = RequireChallenge(validation.AuthenticationInfo);

        await Send<JToken>(HttpMethod.Delete, path, null,
            true, SerializeOnceInfo(challengeId), null, true,
            cancellationToken);
    }

    internal static string SerializeBody<T>(T value)
        => JsonConvert.SerializeObject(value, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
        });

    internal static string SerializeRequestInfo(string sessionId,
        int requestId)
        => JsonConvert.SerializeObject(new
        {
            clientRequestId = new
            {
                sessionId,
                requestId = requestId.ToString(
                    "000000000", CultureInfo.InvariantCulture),
            },
        }, Formatting.None);

    private async Task<ComdirectSession> GetSession(
        CancellationToken cancellationToken)
    {
        var response = await Send<JToken>(
            HttpMethod.Get,
            "api/session/clients/user/v1/sessions",
            null, true, null, null, false, cancellationToken);
        var token = response.Value;
        if (token is JArray array)
            token = array.FirstOrDefault();
        else if (token is JObject obj &&
            obj.TryGetValue("values", StringComparison.OrdinalIgnoreCase,
                out var values) && values is JArray valuesArray)
            token = valuesArray.FirstOrDefault();
        return token?.ToObject<ComdirectSession>(
            JsonSerializer.Create(_jsonSettings));
    }

    private async Task EnsureToken(CancellationToken cancellationToken)
    {
        if (!_accessToken.IsEmpty() &&
            _accessExpiresAt > DateTime.UtcNow.AddSeconds(30))
            return;

        await _authSync.WaitAsync(cancellationToken);
        try
        {
            if (!_accessToken.IsEmpty() &&
                _accessExpiresAt > DateTime.UtcNow.AddSeconds(30))
                return;
            if (_refreshToken.IsEmpty())
                throw new InvalidOperationException(
                    "comdirect refresh token is not available.");

            var token = await RequestToken(
                [
                    new("client_id", _clientId),
                    new("client_secret", _clientSecret),
                    new("grant_type", "refresh_token"),
                    new("refresh_token", _refreshToken),
                ],
                null, cancellationToken);
            SetToken(token);
        }
        finally
        {
            _authSync.Release();
        }
    }

    private async Task<ComdirectToken> RequestToken(
        IEnumerable<KeyValuePair<string, string>> values,
        string bearerToken, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await WaitRateLimit(cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "oauth/token")
            {
                Content = new FormUrlEncodedContent(values),
            };
            if (!bearerToken.IsEmpty())
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException error) when (attempt < _maxAttempts)
            {
                this.AddWarningLog(
                    "comdirect OAuth retry {0}: {1}",
                    attempt, error.Message);
                await DelayRetry(null, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var token = Deserialize<ComdirectToken>(payload);
                    if (token?.AccessToken.IsEmpty() != false)
                        throw new InvalidDataException(
                            "comdirect returned no access token.");
                    return token;
                }

                if (attempt < _maxAttempts &&
                    IsTransient(response.StatusCode))
                {
                    await DelayRetry(response, attempt, cancellationToken);
                    continue;
                }

                throw CreateError(response.StatusCode, payload);
            }
        }
    }

    private async Task<ApiResponse<T>> Send<T>(HttpMethod method,
        string path, string body, bool authorized,
        string onceAuthenticationInfo, string tan,
        bool ensureToken, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (authorized && ensureToken)
                await EnsureToken(cancellationToken);
            await WaitRateLimit(cancellationToken);

            using var request = new HttpRequestMessage(method, path);
            if (authorized)
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);
                request.Headers.TryAddWithoutValidation(
                    "x-http-request-info", NextRequestInfo());
            }
            if (!onceAuthenticationInfo.IsEmpty())
            {
                request.Headers.TryAddWithoutValidation(
                    "x-once-authentication-info",
                    onceAuthenticationInfo);
            }
            if (!tan.IsEmpty())
            {
                request.Headers.TryAddWithoutValidation(
                    "x-once-authentication", tan);
            }
            if (body is not null)
            {
                request.Content = new StringContent(
                    body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException error) when (attempt < _maxAttempts)
            {
                this.AddWarningLog(
                    "comdirect {0} retry {1}: {2}",
                    method, attempt, error.Message);
                await DelayRetry(null, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return new()
                    {
                        Value = payload.IsEmpty()
                            ? default : Deserialize<T>(payload),
                        AuthenticationInfo =
                            ReadAuthenticationInfo(response),
                    };
                }

                if (authorized && ensureToken &&
                    response.StatusCode == HttpStatusCode.Unauthorized &&
                    attempt < _maxAttempts)
                {
                    _accessToken = null;
                    _accessExpiresAt = default;
                    continue;
                }

                if (attempt < _maxAttempts &&
                    IsTransient(response.StatusCode))
                {
                    this.AddWarningLog(
                        "comdirect {0} {1} retry {2} after HTTP {3}.",
                        method, SafePath(path), attempt,
                        (int)response.StatusCode);
                    await DelayRetry(response, attempt, cancellationToken);
                    continue;
                }

                throw CreateError(response.StatusCode, payload);
            }
        }
    }

    private void SetToken(ComdirectToken token)
    {
        _accessToken = token.AccessToken;
        _refreshToken = token.RefreshToken.IsEmpty(_refreshToken);
        _accessExpiresAt = DateTime.UtcNow.AddSeconds(
            Math.Max(30, token.ExpiresIn - 30));
    }

    private void ClearToken()
    {
        _accessToken = null;
        _refreshToken = null;
        _accessExpiresAt = default;
    }

    private string NextRequestInfo()
    {
        var requestId = Interlocked.Increment(ref _requestId);
        if (requestId > 999999999)
        {
            Interlocked.Exchange(ref _requestId, 100000000);
            requestId = Interlocked.Increment(ref _requestId);
        }
        return SerializeRequestInfo(_clientSessionId, requestId);
    }

    private async Task WaitRateLimit(CancellationToken cancellationToken)
    {
        await _rateSync.WaitAsync(cancellationToken);
        try
        {
            var delay = TimeSpan.FromMilliseconds(100) -
                (DateTime.UtcNow - _lastRequestAt);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _lastRequestAt = DateTime.UtcNow;
        }
        finally
        {
            _rateSync.Release();
        }
    }

    private static async Task DelayRetry(HttpResponseMessage response,
        int attempt, CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta;
        if (delay is null && response?.Headers.RetryAfter?.Date is DateTimeOffset date)
            delay = date - DateTimeOffset.UtcNow;
        if (delay is null || delay <= TimeSpan.Zero)
            delay = TimeSpan.FromMilliseconds(
                Math.Min(5000, 250 * (1 << Math.Min(attempt - 1, 4))));
        await Task.Delay(delay.Value, cancellationToken);
    }

    private T Deserialize<T>(string payload)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(payload, _jsonSettings);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Invalid comdirect response: {Limit(payload)}", error);
        }
    }

    private static ComdirectAuthenticationInfo ReadAuthenticationInfo(
        HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(
            "x-once-authentication-info", out var values))
            return null;
        var value = values.FirstOrDefault();
        return value.IsEmpty()
            ? null
            : JsonConvert.DeserializeObject<ComdirectAuthenticationInfo>(
                value, new JsonSerializerSettings
                {
                    ContractResolver =
                        new CamelCasePropertyNamesContractResolver(),
                });
    }

    private static Exception CreateError(HttpStatusCode statusCode,
        string payload)
    {
        var message = payload;
        try
        {
            var error = JsonConvert.DeserializeObject<
                ComdirectErrorEnvelope>(payload);
            message = error?.Messages?
                .Where(m => !m?.Message.IsEmpty() == true)
                .Select(m => m.Message)
                .Join("; ")
                .IsEmpty(error?.ErrorDescription)
                .IsEmpty(error?.Error)
                .IsEmpty(payload);
        }
        catch (JsonException)
        {
            // Preserve the raw payload below.
        }

        return new HttpRequestException(
            $"comdirect HTTP {(int)statusCode} ({statusCode}): " +
            Limit(message), null, statusCode);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 || (int)statusCode >= 500;

    private static string CreateTanTypeHeader(ComdirectTanTypes type)
    {
        var value = type.ToNative();
        return value.IsEmpty()
            ? null
            : JsonConvert.SerializeObject(new { typ = value });
    }

    private static string SerializeOnceInfo(string id)
        => JsonConvert.SerializeObject(new { id });

    private static string RequireChallenge(
        ComdirectAuthenticationInfo info)
        => info?.Id.ThrowIfEmpty(
            "comdirect transaction challenge identifier");

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    private static string SafePath(string path)
        => path?.Split('?')[0];

    private static string Limit(string value)
    {
        value = value.IsEmpty("(empty response)");
        return value.Length <= 1000 ? value : value[..1000] + "...";
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _authSync.Dispose();
        _rateSync.Dispose();
        base.DisposeManaged();
    }
}
