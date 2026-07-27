namespace StockSharp.Dnse.Native;

sealed class DnseRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requestSync = new(1, 1);
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _apiVersion;
    private readonly string _dateHeaderName;
    private DateTimeOffset _nextRequestAt;

    public DnseRestClient(
        Uri endpoint,
        SecureString apiKey,
        SecureString apiSecret,
        string apiVersion,
        string dateHeaderName,
        HttpMessageHandler handler = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "DNSE REST endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
        _apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret)).UnSecure();
        _apiVersion = apiVersion.ThrowIfEmpty(nameof(apiVersion)).Trim();
        _dateHeaderName =
            dateHeaderName.ThrowIfEmpty(nameof(dateHeaderName)).Trim();

        var address = endpoint.AbsoluteUri;
        if (!address.EndsWith('/'))
            address += "/";

        _http = handler is null ? new() : new(handler);
        _http.BaseAddress = new(address);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-DNSE/1.0");
    }

    public override string Name => "DNSE_REST";

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _requestSync.Dispose();
        base.DisposeManaged();
    }

    public async Task<DnseAccount[]> GetAccounts(
        CancellationToken cancellationToken)
        => ExtractArray<DnseAccount>(
            await Get("/accounts", cancellationToken),
            "accounts");

    public async Task<DnseBalance> GetBalances(
        string accountNo,
        CancellationToken cancellationToken)
        => Deserialize<DnseBalance>(
            await Get(
                $"/accounts/{EscapeSegment(accountNo)}/balances",
                cancellationToken));

    public async Task<DnseInstrumentPage> GetInstruments(
        string symbols,
        string marketId,
        string securityGroupId,
        int limit,
        int page,
        CancellationToken cancellationToken)
    {
        var token = await Get(
            Query(
                "/instruments",
                ("symbol", symbols),
                ("marketId", marketId),
                ("securityGroupId", securityGroupId),
                ("limit", Math.Clamp(limit, 1, 1000).ToString(
                    CultureInfo.InvariantCulture)),
                ("page", Math.Max(1, page).ToString(
                    CultureInfo.InvariantCulture))),
            cancellationToken);

        if (token is JArray array)
        {
            return new()
            {
                Data = array.ToObject<DnseInstrument[]>() ?? [],
                Total = array.Count,
                Page = page,
                PageSize = array.Count,
            };
        }

        return Deserialize<DnseInstrumentPage>(token) ??
            new() { Data = [] };
    }

    public async Task<DnseSecurityDefinition> GetSecurityDefinition(
        string symbol,
        string boardId,
        CancellationToken cancellationToken)
        => Deserialize<DnseSecurityDefinition>(
            await Get(
                Query(
                    $"/price/{EscapeSegment(symbol)}/secdef",
                    ("boardId", boardId)),
                cancellationToken));

    public async Task<DnseTrade[]> GetTrades(
        string symbol,
        string boardId,
        DateTime? from,
        DateTime? to,
        int? limit,
        bool latest,
        CancellationToken cancellationToken)
    {
        var path = $"/price/{EscapeSegment(symbol)}/trades" +
            (latest ? "/latest" : string.Empty);
        var token = await Get(
            Query(
                path,
                ("boardId", boardId),
                ("from", ToUnixSeconds(from)),
                ("to", ToUnixSeconds(to)),
                ("limit", limit is > 0
                    ? Math.Min(limit.Value, 1000).ToString(
                        CultureInfo.InvariantCulture)
                    : null),
                ("order", latest ? null : "ASC")),
            cancellationToken);
        return ExtractArray<DnseTrade>(token, "trades");
    }

    public async Task<DnseQuote[]> GetQuotes(
        string symbol,
        string boardId,
        DateTime? from,
        DateTime? to,
        int? limit,
        bool latest,
        CancellationToken cancellationToken)
    {
        var path = $"/price/{EscapeSegment(symbol)}/quotes" +
            (latest ? "/latest" : string.Empty);
        var token = await Get(
            Query(
                path,
                ("boardId", boardId),
                ("from", ToUnixSeconds(from)),
                ("to", ToUnixSeconds(to)),
                ("limit", limit is > 0
                    ? Math.Min(limit.Value, 1000).ToString(
                        CultureInfo.InvariantCulture)
                    : null),
                ("order", latest ? null : "ASC")),
            cancellationToken);
        return ExtractArray<DnseQuote>(token, "quotes");
    }

    public async Task<DnseCandlePage> GetCandles(
        string symbol,
        string resolution,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
        => Deserialize<DnseCandlePage>(
            await Get(
                Query(
                    "/price/ohlc",
                    ("symbol", symbol),
                    ("type", "STOCK"),
                    ("resolution", resolution),
                    ("from", ToUnixSeconds(from)),
                    ("to", ToUnixSeconds(to))),
                cancellationToken));

    public async Task<DnseOrder[]> GetOrders(
        string accountNo,
        CancellationToken cancellationToken)
        => ExtractArray<DnseOrder>(
            await Get(
                Query(
                    $"/accounts/{EscapeSegment(accountNo)}/orders",
                    ("marketType", "STOCK"),
                    ("orderCategory", "NORMAL")),
                cancellationToken),
            "orders");

    public async Task<DnseOrder> GetOrder(
        string accountNo,
        long orderId,
        CancellationToken cancellationToken)
        => Deserialize<DnseOrder>(
            await Get(
                Query(
                    $"/accounts/{EscapeSegment(accountNo)}/orders/{orderId}",
                    ("marketType", "STOCK"),
                    ("orderCategory", "NORMAL")),
                cancellationToken));

    public async Task<DnseOrder> GetExecutions(
        string accountNo,
        long orderId,
        CancellationToken cancellationToken)
        => Deserialize<DnseOrder>(
            await Get(
                Query(
                    $"/accounts/{EscapeSegment(accountNo)}/executions/{orderId}",
                    ("marketType", "STOCK"),
                    ("orderCategory", "NORMAL")),
                cancellationToken));

    public async Task<DnsePosition[]> GetPositions(
        string accountNo,
        CancellationToken cancellationToken)
        => ExtractArray<DnsePosition>(
            await Get(
                Query(
                    $"/accounts/{EscapeSegment(accountNo)}/positions",
                    ("marketType", "STOCK"),
                    ("pageSize", "1000")),
                cancellationToken),
            "positions");

    public async Task<DnseOrder> CreateOrder(
        object payload,
        SecureString tradingToken,
        CancellationToken cancellationToken)
        => Deserialize<DnseOrder>(
            await Send(
                HttpMethod.Post,
                Query(
                    "/accounts/orders",
                    ("marketType", "STOCK"),
                    ("orderCategory", "NORMAL")),
                payload,
                tradingToken,
                cancellationToken));

    public async Task<DnseOrder> ReplaceOrder(
        string accountNo,
        long orderId,
        object payload,
        SecureString tradingToken,
        CancellationToken cancellationToken)
        => Deserialize<DnseOrder>(
            await Send(
                HttpMethod.Put,
                Query(
                    $"/accounts/{EscapeSegment(accountNo)}/orders/{orderId}",
                    ("marketType", "STOCK"),
                    ("orderCategory", "NORMAL")),
                payload,
                tradingToken,
                cancellationToken));

    public async Task<DnseOrder> CancelOrder(
        string accountNo,
        long orderId,
        SecureString tradingToken,
        CancellationToken cancellationToken)
        => Deserialize<DnseOrder>(
            await Send(
                HttpMethod.Delete,
                Query(
                    $"/accounts/{EscapeSegment(accountNo)}/orders/{orderId}",
                    ("marketType", "STOCK"),
                    ("orderCategory", "NORMAL")),
                null,
                tradingToken,
                cancellationToken));

    public async Task<string> CreateTradingToken(
        DnseOtpTypes otpType,
        SecureString passcode,
        CancellationToken cancellationToken)
    {
        var token = await Send(
            HttpMethod.Post,
            "/registration/trading-token",
            new
            {
                otpType = otpType == DnseOtpTypes.Email
                    ? "email_otp"
                    : "smart_otp",
                passcode = passcode.ThrowIfEmpty(nameof(passcode)).UnSecure(),
            },
            null,
            cancellationToken);
        var tradingToken = token?.Value<string>("tradingToken");
        if (tradingToken.IsEmpty())
        {
            throw new InvalidDataException(
                "DNSE did not return a trading token after OTP verification.");
        }
        return tradingToken;
    }

    public async Task SendEmailOtp(CancellationToken cancellationToken)
        => _ = await Send(
            HttpMethod.Post,
            "/registration/send-email-otp",
            null,
            null,
            cancellationToken);

    internal static string CreateSigningString(
        HttpMethod method,
        string path,
        string dateValue,
        string nonce,
        string dateHeaderName = "Date")
    {
        ArgumentNullException.ThrowIfNull(method);
        path.ThrowIfEmpty(nameof(path));
        dateValue.ThrowIfEmpty(nameof(dateValue));
        dateHeaderName.ThrowIfEmpty(nameof(dateHeaderName));

        var header = dateHeaderName.ToLowerInvariant();
        var value =
            $"(request-target): {method.Method.ToLowerInvariant()} {path}\n" +
            $"{header}: {dateValue}";
        if (!nonce.IsEmpty())
            value += $"\nnonce: {nonce}";
        return value;
    }

    internal static string CreateSignatureHeader(
        string apiKey,
        string apiSecret,
        HttpMethod method,
        string path,
        string dateValue,
        string nonce,
        string dateHeaderName = "Date")
    {
        apiKey.ThrowIfEmpty(nameof(apiKey));
        apiSecret.ThrowIfEmpty(nameof(apiSecret));

        var signingString = CreateSigningString(
            method, path, dateValue, nonce, dateHeaderName);
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(apiSecret),
            Encoding.UTF8.GetBytes(signingString));
        var signature = Uri.EscapeDataString(
            Convert.ToBase64String(hash));
        var headers =
            $"(request-target) {dateHeaderName.ToLowerInvariant()}";
        var result =
            $"Signature keyId=\"{apiKey}\",algorithm=\"hmac-sha256\"," +
            $"headers=\"{headers}\",signature=\"{signature}\"";
        if (!nonce.IsEmpty())
            result += $",nonce=\"{nonce}\"";
        return result;
    }

    private Task<JToken> Get(
        string path,
        CancellationToken cancellationToken)
        => Send(
            HttpMethod.Get,
            path,
            null,
            null,
            cancellationToken);

    private async Task<JToken> Send(
        HttpMethod method,
        string pathAndQuery,
        object body,
        SecureString tradingToken,
        CancellationToken cancellationToken)
    {
        var separator = pathAndQuery.IndexOf('?');
        var path = separator < 0
            ? pathAndQuery
            : pathAndQuery[..separator];
        if (!path.StartsWith('/'))
            path = "/" + path;
        var relative = pathAndQuery.TrimStart('/');
        var serialized = body is null
            ? null
            : JsonConvert.SerializeObject(body, _jsonSettings);

        for (var attempt = 0; ; attempt++)
        {
            await Throttle(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var dateValue = _dateHeaderName.EqualsIgnoreCase("Date")
                ? now.ToString("r", CultureInfo.InvariantCulture)
                : now.ToString(
                    "ddd, dd MMM yyyy HH:mm:ss +0000",
                    CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            using var request = new HttpRequestMessage(method, relative);
            request.Headers.TryAddWithoutValidation(
                _dateHeaderName, dateValue);
            request.Headers.TryAddWithoutValidation(
                "X-Signature",
                CreateSignatureHeader(
                    _apiKey,
                    _apiSecret,
                    method,
                    path,
                    dateValue,
                    nonce,
                    _dateHeaderName));
            request.Headers.TryAddWithoutValidation(
                "x-api-key", _apiKey);
            request.Headers.TryAddWithoutValidation(
                "version", _apiVersion);
            if (!tradingToken.IsEmpty())
            {
                request.Headers.TryAddWithoutValidation(
                    "trading-token", tradingToken.UnSecure());
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
                            "DNSE returned invalid JSON.", error);
                    }
                }

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
            _nextRequestAt = DateTimeOffset.UtcNow.AddMilliseconds(110);
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

    private static HttpRequestException CreateException(
        HttpStatusCode statusCode,
        string text)
    {
        string code = null;
        string message = null;
        try
        {
            var error = JObject.Parse(text);
            code = error.Value<string>("code");
            message = error.Value<string>("message");
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
            $"DNSE OpenAPI: {details}",
            null,
            statusCode);
    }

    private static T Deserialize<T>(JToken token)
        => token is null || token.Type == JTokenType.Null
            ? default
            : token.ToObject<T>(
                JsonSerializer.Create(_jsonSettings));

    private static T[] ExtractArray<T>(
        JToken token,
        string propertyName)
    {
        if (token is JArray direct)
            return Deserialize<T[]>(direct) ?? [];
        if (token is not JObject root)
            return [];

        var value = root.GetValue(
            propertyName, StringComparison.OrdinalIgnoreCase);
        if (value is JArray array)
            return Deserialize<T[]>(array) ?? [];
        if (value is JObject nested &&
            nested.GetValue(
                propertyName,
                StringComparison.OrdinalIgnoreCase) is JArray nestedArray)
        {
            return Deserialize<T[]>(nestedArray) ?? [];
        }
        if (root.GetValue(
            "data",
            StringComparison.OrdinalIgnoreCase) is JArray data)
        {
            return Deserialize<T[]>(data) ?? [];
        }
        return [];
    }

    private static string Query(
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

    private static string EscapeSegment(string value)
        => Uri.EscapeDataString(
            value.ThrowIfEmpty(nameof(value)).Trim());

    private static string ToUnixSeconds(DateTime? value)
        => value is null
            ? null
            : new DateTimeOffset(
                NormalizeUtc(value.Value))
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
