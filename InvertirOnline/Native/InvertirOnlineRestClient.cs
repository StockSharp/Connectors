namespace StockSharp.InvertirOnline.Native;

sealed class InvertirOnlineRestClient : BaseLogReceiver
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
    private readonly string _login;
    private readonly string _password;
    private readonly int _maxRetries;
    private string _accessToken;
    private string _refreshToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MaxValue;
    private DateTimeOffset _nextRequestAt;

    public InvertirOnlineRestClient(
        Uri endpoint,
        string login,
        SecureString password,
        SecureString accessToken,
        SecureString refreshToken,
        int maxRetries,
        HttpMessageHandler handler = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "IOL endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        _login = login?.Trim();
        _password = password?.UnSecure();
        _accessToken = accessToken?.UnSecure();
        _refreshToken = refreshToken?.UnSecure();
        _maxRetries = Math.Max(0, maxRetries);

        if (_accessToken.IsEmpty() &&
            (_login.IsEmpty() || _password.IsEmpty()))
        {
            throw new InvalidOperationException(
                "IOL login and password are required when no access token is supplied.");
        }

        var address = endpoint.AbsoluteUri;
        if (!address.EndsWith('/'))
            address += "/";

        _http = handler is null ? new() : new(handler);
        _http.BaseAddress = new(address);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-InvertirOnline/1.0");
    }

    public override string Name => "IOL_REST";

    public string AccessToken => _accessToken;

    public string RefreshToken => _refreshToken;

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _tokenSync.Dispose();
        _requestSync.Dispose();
        base.DisposeManaged();
    }

    public async Task<string> GetAccessToken(
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticated(false, cancellationToken);
        return _accessToken;
    }

    public async Task<IolInstrumentGroup[]> GetInstrumentGroups(
        string country,
        CancellationToken cancellationToken)
        => ExtractArray<IolInstrumentGroup>(
            await Get(
                $"api/v2/{Escape(country)}/Titulos/Cotizacion/Instrumentos",
                cancellationToken));

    public async Task<IolInstrument[]> GetInstruments(
        string instrumentType,
        string country,
        CancellationToken cancellationToken)
    {
        var value = await GetObject<IolInstrumentList>(
            $"api/v2/Cotizaciones/{Escape(instrumentType)}/" +
            $"{Escape(country)}/Todos",
            cancellationToken);
        return value?.Instruments ?? [];
    }

    public Task<IolTitle> GetTitle(
        string market,
        string symbol,
        CancellationToken cancellationToken)
        => GetObject<IolTitle>(
            $"api/v2/{Escape(market)}/Titulos/{Escape(symbol)}",
            cancellationToken);

    public Task<IolQuote> GetQuote(
        IolSecurityKey security,
        CancellationToken cancellationToken)
        => GetObject<IolQuote>(
            Query(
                $"api/v2/{Escape(security.Market)}/Titulos/" +
                $"{Escape(security.Symbol)}/Cotizacion",
                ("model.simbolo", security.Symbol),
                ("model.mercado", security.Market),
                ("model.plazo", security.Settlement)),
            cancellationToken);

    public async Task<IolQuote[]> GetHistory(
        IolSecurityKey security,
        DateTime from,
        DateTime to,
        bool adjusted,
        CancellationToken cancellationToken)
        => ExtractArray<IolQuote>(
            await Get(
                $"api/v2/{Escape(security.Market)}/Titulos/" +
                $"{Escape(security.Symbol)}/Cotizacion/seriehistorica/" +
                $"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}/" +
                (adjusted ? "ajustada" : "sinAjustar"),
                cancellationToken));

    public Task<IolAccountState> GetAccountState(
        CancellationToken cancellationToken)
        => GetObject<IolAccountState>(
            "api/v2/estadocuenta", cancellationToken);

    public Task<IolPortfolio> GetPortfolio(
        string country,
        CancellationToken cancellationToken)
        => GetObject<IolPortfolio>(
            $"api/v2/portafolio/{Escape(country)}",
            cancellationToken);

    public async Task<IolOperation[]> GetOperations(
        long? number,
        string state,
        DateTime? from,
        DateTime? to,
        string country,
        CancellationToken cancellationToken)
        => ExtractArray<IolOperation>(
            await Get(
                Query(
                    "api/v2/operaciones",
                    ("filtro.numero", ToText(number)),
                    ("filtro.estado", state),
                    ("filtro.fechaDesde", ToIso(from)),
                    ("filtro.fechaHasta", ToIso(to)),
                    ("filtro.pais", country)),
                cancellationToken));

    public Task<IolOperationDetail> GetOperation(
        long number,
        CancellationToken cancellationToken)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number), number, null);

        return GetObject<IolOperationDetail>(
            $"api/v2/operaciones/{number.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);
    }

    public async Task<IolPlacementResult> PlaceOrder(
        Sides side,
        JObject request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (status, payload) = await Send(
            HttpMethod.Post,
            side == Sides.Sell
                ? "api/v2/operar/Vender"
                : "api/v2/operar/Comprar",
            request,
            cancellationToken);

        var root = payload as JObject;
        var operationNumber =
            root?["numeroOperacion"]?.Value<long?>() ?? 0;
        var response = root?.ToObject<IolApiResponse>(
            JsonSerializer.Create(_jsonSettings));
        var messages = response?.Messages ??
            (payload as JArray)?.ToObject<IolApiMessage[]>(
                JsonSerializer.Create(_jsonSettings));
        var message = FormatMessages(messages);
        if (operationNumber <= 0)
        {
            operationNumber = FindOperationNumber(messages);
        }

        if (operationNumber > 0)
        {
            return new(
                operationNumber,
                true,
                message);
        }

        throw new InvalidDataException(
            message.IsEmpty()
                ? $"IOL accepted the order with HTTP {(int)status}, but returned no operation number."
                : $"IOL did not create the order: {message}");
    }

    public async Task CancelOrder(
        long number,
        CancellationToken cancellationToken)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number), number, null);

        var (_, payload) = await Send(
            HttpMethod.Delete,
            $"api/v2/operaciones/{number.ToString(CultureInfo.InvariantCulture)}",
            null,
            cancellationToken);
        var response = (payload as JObject)?.ToObject<IolApiResponse>(
            JsonSerializer.Create(_jsonSettings));
        if (response != null && !response.Ok)
        {
            throw new InvalidOperationException(
                FormatMessages(response.Messages)
                    .IsEmpty("IOL rejected the cancellation request."));
        }
    }

    internal static string Query(
        string path,
        params (string name, string value)[] values)
    {
        var query = values
            .Where(item => !item.value.IsEmpty())
            .Select(item =>
                $"{Uri.EscapeDataString(item.name)}=" +
                Uri.EscapeDataString(item.value));
        var suffix = string.Join("&", query);
        return suffix.IsEmpty() ? path : $"{path}?{suffix}";
    }

    private async Task<JToken> Get(
        string path,
        CancellationToken cancellationToken)
    {
        var result = await Send(
            HttpMethod.Get, path, null, cancellationToken);
        return result.payload;
    }

    private async Task<T> GetObject<T>(
        string path,
        CancellationToken cancellationToken)
        where T : class
    {
        var value = await Get(path, cancellationToken);
        if (value is null or JValue { Type: JTokenType.Null })
            return null;
        if (value is JObject root)
        {
            var data = root.GetValue(
                "data", StringComparison.OrdinalIgnoreCase);
            if (data is JObject)
                value = data;
        }
        return value.ToObject<T>(JsonSerializer.Create(_jsonSettings));
    }

    private async Task<(HttpStatusCode status, JToken payload)> Send(
        HttpMethod method,
        string path,
        JToken body,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticated(false, cancellationToken);
        var bodyText = body?.ToString(Formatting.None);

        for (var attempt = 0; ; attempt++)
        {
            await WaitForRequestSlot(cancellationToken);
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
            if (bodyText != null)
            {
                request.Content = new StringContent(
                    bodyText, Encoding.UTF8, "application/json");
            }

            using var response =
                await _http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                attempt == 0)
            {
                _expiresAt = DateTimeOffset.MinValue;
                await EnsureAuthenticated(true, cancellationToken);
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500) &&
                attempt < _maxRetries)
            {
                var delay = GetRetryDelay(response, attempt);
                this.AddWarningLog(
                    "IOL {0} {1} returned HTTP {2}; retrying in {3}.",
                    method,
                    path,
                    (int)response.StatusCode,
                    delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    BuildError(
                        method, path, response.StatusCode, text),
                    null,
                    response.StatusCode);
            }

            var payload = ParsePayload(text);
            return (response.StatusCode, payload);
        }
    }

    private async Task EnsureAuthenticated(
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force &&
            !_accessToken.IsEmpty() &&
            _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        await _tokenSync.WaitAsync(cancellationToken);
        try
        {
            if (!force &&
                !_accessToken.IsEmpty() &&
                _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            Exception refreshError = null;
            if (!_refreshToken.IsEmpty())
            {
                try
                {
                    await RequestToken(
                        new()
                        {
                            ["refresh_token"] = _refreshToken,
                            ["grant_type"] = "refresh_token",
                        },
                        cancellationToken);
                    return;
                }
                catch (Exception error) when (
                    error is HttpRequestException or InvalidDataException)
                {
                    refreshError = error;
                    this.AddWarningLog(
                        "IOL refresh token was rejected: {0}",
                        error.Message);
                }
            }

            if (_login.IsEmpty() || _password.IsEmpty())
            {
                throw new InvalidOperationException(
                    "IOL access token expired and no login credentials are available.",
                    refreshError);
            }

            await RequestToken(
                new()
                {
                    ["username"] = _login,
                    ["password"] = _password,
                    ["grant_type"] = "password",
                },
                cancellationToken);
        }
        finally
        {
            _tokenSync.Release();
        }
    }

    private async Task RequestToken(
        Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await WaitForRequestSlot(cancellationToken);
            using var content = new FormUrlEncodedContent(values);
            using var response =
                await _http.PostAsync("token", content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500) &&
                attempt < _maxRetries)
            {
                await Task.Delay(
                    GetRetryDelay(response, attempt),
                    cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    BuildError(
                        HttpMethod.Post,
                        "token",
                        response.StatusCode,
                        text),
                    null,
                    response.StatusCode);
            }

            var token = JsonConvert.DeserializeObject<IolToken>(
                text, _jsonSettings);
            if (token?.AccessToken.IsEmpty() != false)
            {
                throw new InvalidDataException(
                    "IOL token response contains no access_token.");
            }

            _accessToken = token.AccessToken;
            if (!token.RefreshToken.IsEmpty())
                _refreshToken = token.RefreshToken;

            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(60, token.ExpiresIn));
            if (DateTimeOffset.TryParse(
                token.Expires,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var explicitExpiry))
            {
                _expiresAt = explicitExpiry;
            }
            return;
        }
    }

    private async Task WaitForRequestSlot(
        CancellationToken cancellationToken)
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

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : delta;
        if (retry?.Date is { } date)
        {
            var fromHeader = date - DateTimeOffset.UtcNow;
            if (fromHeader > TimeSpan.Zero)
            {
                return fromHeader > TimeSpan.FromSeconds(30)
                    ? TimeSpan.FromSeconds(30)
                    : fromHeader;
            }
        }
        return TimeSpan.FromSeconds(Math.Min(8, 1 << attempt));
    }

    private static string BuildError(
        HttpMethod method,
        string path,
        HttpStatusCode status,
        string text)
    {
        var detail = ExtractError(text);
        return $"IOL {method} {path} returned HTTP {(int)status}" +
            (detail.IsEmpty() ? "." : $": {detail}");
    }

    private static string ExtractError(string text)
    {
        if (text.IsEmpty())
            return null;
        try
        {
            var value = JToken.Parse(text);
            var messages = value is JArray array
                ? FormatMessages(array.ToObject<IolApiMessage[]>(
                    JsonSerializer.Create(_jsonSettings)))
                : FormatMessages(value.ToObject<IolApiResponse>(
                    JsonSerializer.Create(_jsonSettings))?.Messages);
            if (!messages.IsEmpty())
                return messages;
            return value["error_description"]?.Value<string>() ??
                value["message"]?.Value<string>() ??
                value["detail"]?.Value<string>() ??
                value.ToString(Formatting.None);
        }
        catch (JsonException)
        {
            var normalized = text.Trim();
            return normalized.Length <= 1000
                ? normalized
                : normalized[..1000];
        }
    }

    private static JToken ParsePayload(string text)
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
                "IOL returned a non-JSON response.", error);
        }
    }

    private static T[] ExtractArray<T>(JToken value)
    {
        if (value is null or JValue { Type: JTokenType.Null })
            return [];
        if (value is JObject root)
        {
            value = root.GetValue(
                "data", StringComparison.OrdinalIgnoreCase) ??
                root.GetValue(
                    "items", StringComparison.OrdinalIgnoreCase) ??
                value;
        }
        return value is JArray
            ? value.ToObject<T[]>(
                JsonSerializer.Create(_jsonSettings)) ?? []
            : [];
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

    private static string ToText(long? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    private static string ToIso(DateTime? value)
        => value?.ToUniversalTime().ToString(
            "O", CultureInfo.InvariantCulture);

    private static string FormatMessages(
        IEnumerable<IolApiMessage> messages)
        => string.Join(
            "; ",
            (messages ?? [])
                .Select(item => string.Join(
                    ": ",
                    new[] { item?.Title, item?.Description }
                        .Where(value => !value.IsEmpty())))
                .Where(value => !value.IsEmpty()));

    private static long FindOperationNumber(
        IEnumerable<IolApiMessage> messages)
    {
        foreach (var message in messages ?? [])
        {
            var title = message?.Title;
            if (!title.ContainsIgnoreCase("transaction") &&
                !title.ContainsIgnoreCase("operaci") &&
                !title.ContainsIgnoreCase("operation"))
            {
                continue;
            }
            if (long.TryParse(
                message.Description,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number))
            {
                return number;
            }
        }
        return 0;
    }
}
