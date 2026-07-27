namespace StockSharp.Ppi.Native;

sealed class PpiRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _tokenSync = new(1, 1);
    private readonly SemaphoreSlim _requestSync = new(1, 1);
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _authorizedClient;
    private readonly string _clientKey;
    private string _accessToken;
    private string _refreshToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MaxValue;
    private DateTimeOffset _nextRequestAt;

    public PpiRestClient(
        Uri endpoint,
        SecureString apiKey,
        SecureString apiSecret,
        string authorizedClient,
        string clientKey,
        SecureString accessToken = null,
        SecureString refreshToken = null,
        HttpMessageHandler handler = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "PPI REST endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
        _apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret)).UnSecure();
        _authorizedClient =
            authorizedClient.ThrowIfEmpty(nameof(authorizedClient)).Trim();
        _clientKey = clientKey.ThrowIfEmpty(nameof(clientKey)).Trim();
        _accessToken = accessToken?.UnSecure();
        _refreshToken = refreshToken?.UnSecure();
        _expiresAt = GetJwtExpiry(_accessToken) ?? DateTimeOffset.MaxValue;

        var address = endpoint.AbsoluteUri;
        if (!address.EndsWith('/'))
            address += "/";

        _http = handler is null ? new() : new(handler);
        _http.BaseAddress = new(address);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-PPI/1.0");
    }

    public override string Name => "PPI_REST";

    public string AccessToken => _accessToken;

    public string RefreshToken => _refreshToken;

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _tokenSync.Dispose();
        _requestSync.Dispose();
        base.DisposeManaged();
    }

    public Task Authenticate(CancellationToken cancellationToken)
        => EnsureAuthenticated(true, cancellationToken);

    public async Task<string> GetAccessToken(
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticated(false, cancellationToken);
        return _accessToken;
    }

    public async Task<PpiAccount[]> GetAccounts(
        CancellationToken cancellationToken)
        => ExtractArray<PpiAccount>(
            await Get("1.0/Account/Accounts", cancellationToken));

    public async Task<PpiAvailability[]> GetAvailableBalance(
        string accountNumber,
        CancellationToken cancellationToken)
        => ExtractArray<PpiAvailability>(
            await Get(
                Query(
                    "1.0/Account/AvailableBalance",
                    ("accountNumber", accountNumber)),
                cancellationToken));

    public Task<JToken> GetBalancesAndPositions(
        string accountNumber,
        CancellationToken cancellationToken)
        => Get(
            Query(
                "1.0/Account/BalancesAndPositions",
                ("accountNumber", accountNumber)),
            cancellationToken);

    public async Task<PpiInstrument[]> SearchInstruments(
        string ticker,
        string name,
        string market,
        string type,
        CancellationToken cancellationToken)
        => ExtractArray<PpiInstrument>(
            await Get(
                Query(
                    "1.0/MarketData/SearchInstrument",
                    ("Ticker", ticker),
                    ("Name", name),
                    ("Market", market),
                    ("Type", type)),
                cancellationToken));

    public async Task<PpiPrice[]> GetHistory(
        PpiInstrumentKey instrument,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
        => ExtractArray<PpiPrice>(
            await Get(
                Query(
                    "1.0/MarketData/Search",
                    ("Ticker", instrument.Ticker),
                    ("Type", instrument.Type),
                    ("DateFrom", ToIso(from)),
                    ("DateTo", ToIso(to)),
                    ("Settlement", instrument.Settlement)),
                cancellationToken));

    public Task<PpiPrice> GetCurrent(
        PpiInstrumentKey instrument,
        CancellationToken cancellationToken)
        => GetObject<PpiPrice>(
            Query(
                "1.0/MarketData/Current",
                ("Ticker", instrument.Ticker),
                ("Type", instrument.Type),
                ("Settlement", instrument.Settlement)),
            cancellationToken);

    public async Task<PpiPrice[]> GetIntraday(
        PpiInstrumentKey instrument,
        CancellationToken cancellationToken)
        => ExtractArray<PpiPrice>(
            await Get(
                Query(
                    "1.0/MarketData/Intraday",
                    ("Ticker", instrument.Ticker),
                    ("Type", instrument.Type),
                    ("Settlement", instrument.Settlement)),
                cancellationToken));

    public Task<PpiBook> GetBook(
        PpiInstrumentKey instrument,
        CancellationToken cancellationToken)
        => GetObject<PpiBook>(
            Query(
                "1.0/MarketData/Book",
                ("Ticker", instrument.Ticker),
                ("Type", instrument.Type),
                ("Settlement", instrument.Settlement)),
            cancellationToken);

    public async Task<PpiOrder[]> GetOrders(
        string accountNumber,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
        => ExtractArray<PpiOrder>(
            await Get(
                Query(
                    "1.0/Order/Orders",
                    ("accountNumber", accountNumber),
                    ("dateFrom", ToIso(from)),
                    ("dateTo", ToIso(to))),
                cancellationToken));

    public async Task<PpiOrder[]> GetActiveOrders(
        string accountNumber,
        CancellationToken cancellationToken)
        => ExtractArray<PpiOrder>(
            await Get(
                Query(
                    "1.0/Order/ActiveOrders",
                    ("accountNumber", accountNumber)),
                cancellationToken));

    public Task<PpiOrder> GetOrderDetail(
        string accountNumber,
        long orderId,
        string externalId,
        CancellationToken cancellationToken)
        => GetObject<PpiOrder>(
            Query(
                "1.0/Order/Detail",
                ("accountNumber", accountNumber),
                ("orderID", orderId.ToString(CultureInfo.InvariantCulture)),
                ("externalID", externalId)),
            cancellationToken);

    public Task<PpiOrder> BudgetOrder(
        JObject request,
        CancellationToken cancellationToken)
        => PostObject<PpiOrder>(
            "1.0/Order/Budget", request, cancellationToken);

    public Task<PpiOrder> ConfirmOrder(
        JObject request,
        CancellationToken cancellationToken)
        => PostObject<PpiOrder>(
            "1.0/Order/Confirm", request, cancellationToken);

    public Task<PpiOrder> CancelOrder(
        string accountNumber,
        long orderId,
        string externalId,
        CancellationToken cancellationToken)
        => PostObject<PpiOrder>(
            "1.0/Order/Cancel",
            new JObject
            {
                ["accountNumber"] = accountNumber,
                ["orderID"] = orderId,
                ["externalID"] = externalId,
            },
            cancellationToken);

    private Task<JToken> Get(
        string path,
        CancellationToken cancellationToken)
        => Send(HttpMethod.Get, path, null, cancellationToken);

    private Task<JToken> Post(
        string path,
        JToken body,
        CancellationToken cancellationToken)
        => Send(HttpMethod.Post, path, body, cancellationToken);

    private async Task<T> GetObject<T>(
        string path,
        CancellationToken cancellationToken)
        => Deserialize<T>(await Get(path, cancellationToken));

    private async Task<T> PostObject<T>(
        string path,
        JToken body,
        CancellationToken cancellationToken)
        => Deserialize<T>(await Post(path, body, cancellationToken));

    private async Task<JToken> Send(
        HttpMethod method,
        string path,
        JToken body,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticated(false, cancellationToken);
        var observedToken = _accessToken;
        try
        {
            return await SendCore(
                method,
                path,
                body,
                true,
                null,
                cancellationToken);
        }
        catch (HttpRequestException error) when (
            error.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RenewAfterUnauthorized(observedToken, cancellationToken);
            return await SendCore(
                method,
                path,
                body,
                true,
                null,
                cancellationToken);
        }
    }

    private async Task EnsureAuthenticated(
        bool forceLogin,
        CancellationToken cancellationToken)
    {
        if (!forceLogin &&
            !_accessToken.IsEmpty() &&
            _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        await _tokenSync.WaitAsync(cancellationToken);
        try
        {
            if (!forceLogin &&
                !_accessToken.IsEmpty() &&
                _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            if (!forceLogin && !_refreshToken.IsEmpty())
            {
                try
                {
                    await RefreshCore(cancellationToken);
                    return;
                }
                catch (HttpRequestException error) when (
                    error.StatusCode is HttpStatusCode.BadRequest or
                        HttpStatusCode.Unauthorized or
                        HttpStatusCode.Forbidden)
                {
                    this.AddVerboseLog(
                        "PPI refresh token was rejected; performing a full login.");
                }
            }

            await LoginCore(cancellationToken);
        }
        finally
        {
            _tokenSync.Release();
        }
    }

    private async Task RenewAfterUnauthorized(
        string observedToken,
        CancellationToken cancellationToken)
    {
        await _tokenSync.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(
                observedToken,
                _accessToken,
                StringComparison.Ordinal))
            {
                return;
            }

            if (!_refreshToken.IsEmpty())
            {
                try
                {
                    await RefreshCore(cancellationToken);
                    return;
                }
                catch (HttpRequestException error) when (
                    error.StatusCode is HttpStatusCode.BadRequest or
                        HttpStatusCode.Unauthorized or
                        HttpStatusCode.Forbidden)
                {
                    this.AddVerboseLog(
                        "PPI session refresh failed; logging in again.");
                }
            }

            await LoginCore(cancellationToken);
        }
        finally
        {
            _tokenSync.Release();
        }
    }

    private async Task LoginCore(CancellationToken cancellationToken)
    {
        var token = Deserialize<PpiToken>(
            await SendCore(
                HttpMethod.Post,
                "1.0/Account/LoginApi",
                null,
                false,
                new Dictionary<string, string>
                {
                    ["ApiKey"] = _apiKey,
                    ["ApiSecret"] = _apiSecret,
                },
                cancellationToken));
        ApplyToken(token, "login");
    }

    private async Task RefreshCore(CancellationToken cancellationToken)
    {
        var token = Deserialize<PpiToken>(
            await SendCore(
                HttpMethod.Post,
                "1.0/Account/RefreshToken",
                new JObject
                {
                    ["refreshToken"] = _refreshToken,
                },
                true,
                null,
                cancellationToken));
        ApplyToken(token, "refresh");
    }

    private void ApplyToken(PpiToken token, string operation)
    {
        if (token?.AccessToken.IsEmpty() != false)
        {
            throw new InvalidDataException(
                $"PPI {operation} response returned no access token.");
        }

        _accessToken = token.AccessToken;
        if (!token.RefreshToken.IsEmpty())
            _refreshToken = token.RefreshToken;
        _expiresAt = token.ExpirationDate != default
            ? token.ExpirationDate
            : token.Expires > 0
                ? DateTimeOffset.UtcNow.AddSeconds(token.Expires)
                : GetJwtExpiry(token.AccessToken) ??
                    DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private async Task<JToken> SendCore(
        HttpMethod method,
        string path,
        JToken body,
        bool includeBearer,
        IReadOnlyDictionary<string, string> extraHeaders,
        CancellationToken cancellationToken)
    {
        var serialized = body?.ToString(Formatting.None);
        for (var attempt = 0; ; attempt++)
        {
            await Throttle(cancellationToken);

            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation(
                "AuthorizedClient", _authorizedClient);
            request.Headers.TryAddWithoutValidation("ClientKey", _clientKey);
            request.Headers.TryAddWithoutValidation(
                "x-tracking-id",
                "platform:.NET,type:SDKStockSharp,so:cross-platform");
            if (includeBearer && !_accessToken.IsEmpty())
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);
            }
            if (extraHeaders != null)
            {
                foreach (var header in extraHeaders)
                {
                    request.Headers.TryAddWithoutValidation(
                        header.Key, header.Value);
                }
            }
            if (serialized is not null)
            {
                request.Content = new StringContent(
                    serialized, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await DelayRetry(null, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                    return ParseResponse(text);

                if (IsTransient(response.StatusCode) && attempt < 2)
                {
                    await DelayRetry(response, attempt, cancellationToken);
                    continue;
                }
                throw CreateException(response.StatusCode, text);
            }
        }
    }

    private async Task Throttle(CancellationToken cancellationToken)
    {
        await _requestSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextRequestAt = DateTimeOffset.UtcNow.AddMilliseconds(100);
        }
        finally
        {
            _requestSync.Release();
        }
    }

    private static async Task DelayRetry(
        HttpResponseMessage response,
        int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta ??
            TimeSpan.FromMilliseconds(250 * (attempt + 1));
        if (delay > TimeSpan.FromSeconds(2))
            delay = TimeSpan.FromSeconds(2);
        await Task.Delay(delay, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests ||
            (int)statusCode >= 500;

    private static JToken ParseResponse(string text)
    {
        if (text.IsEmpty())
            return null;
        try
        {
            return JToken.Parse(text);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "PPI returned invalid JSON.", error);
        }
    }

    private static HttpRequestException CreateException(
        HttpStatusCode statusCode,
        string text)
    {
        string code = null;
        string message = null;
        try
        {
            var token = JToken.Parse(text);
            if (token is JObject error)
            {
                code = error.GetValue(
                    "code", StringComparison.OrdinalIgnoreCase)?.ToString();
                message = error.GetValue(
                    "message", StringComparison.OrdinalIgnoreCase)?.ToString();
                message ??= error.GetValue(
                    "error", StringComparison.OrdinalIgnoreCase)?.ToString();
            }
            else
            {
                message = token.Type == JTokenType.String
                    ? token.Value<string>()
                    : token.ToString(Formatting.None);
            }
        }
        catch (JsonException)
        {
            message = text;
        }

        var details = new[] { code, message }
            .Where(value => !value.IsEmpty())
            .Join(": ");
        if (details.IsEmpty())
            details = $"HTTP {(int)statusCode} ({statusCode}).";
        return new HttpRequestException(
            $"PPI API: {details}", null, statusCode);
    }

    private static T Deserialize<T>(JToken token)
    {
        if (token is JArray array && array.Count == 1 &&
            typeof(T) != typeof(JArray) && !typeof(T).IsArray)
        {
            token = array[0];
        }
        return token is null || token.Type == JTokenType.Null
            ? default
            : token.ToObject<T>(JsonSerializer.Create(_jsonSettings));
    }

    private static T[] ExtractArray<T>(JToken token)
    {
        if (token is JArray direct)
            return Deserialize<T[]>(direct) ?? [];
        if (token is not JObject root)
            return [];

        foreach (var name in new[] { "data", "items", "results", "value" })
        {
            if (root.GetValue(
                name,
                StringComparison.OrdinalIgnoreCase) is JArray array)
            {
                return Deserialize<T[]>(array) ?? [];
            }
        }
        return [];
    }

    internal static string Query(
        string path,
        params (string name, string value)[] parameters)
    {
        var query = parameters
            .Where(parameter => !parameter.value.IsEmpty())
            .Select(parameter =>
                $"{Uri.EscapeDataString(parameter.name)}=" +
                Uri.EscapeDataString(parameter.value))
            .Join("&");
        return query.IsEmpty() ? path : $"{path}?{query}";
    }

    private static string ToIso(DateTime? value)
        => value is null ? null : ToIso(value.Value);

    private static string ToIso(DateTime value)
        => (value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime())
            .ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? GetJwtExpiry(string token)
    {
        if (token.IsEmpty())
            return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;
            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(
                payload.Length + (4 - payload.Length % 4) % 4,
                '=');
            var json = Encoding.UTF8.GetString(
                Convert.FromBase64String(payload));
            var expiry = JObject.Parse(json).Value<long?>("exp");
            return expiry is > 0
                ? DateTimeOffset.FromUnixTimeSeconds(expiry.Value)
                : null;
        }
        catch (Exception error) when (
            error is FormatException or JsonException or
                ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
