namespace StockSharp.TossSecurities.Native;

sealed class TossRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private DateTimeOffset _nextRequestAt;
    private string _clientId;
    private string _clientSecret;
    private string _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public TossRestClient(Uri restAddress, HttpMessageHandler handler = null)
    {
        ArgumentNullException.ThrowIfNull(restAddress);

        var address = restAddress.AbsoluteUri;
        if (!address.EndsWith('/'))
            address += "/";

        _httpClient = handler is null ? new() : new(handler);
        _httpClient.BaseAddress = new(address);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-TossSecurities/1.0");
    }

    public override string Name =>
        nameof(TossSecurities) + "_" + nameof(TossRestClient);

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _authLock.Dispose();
        _requestLock.Dispose();
        base.DisposeManaged();
    }

    public async Task Authenticate(
        SecureString clientId,
        SecureString clientSecret,
        CancellationToken cancellationToken)
    {
        _clientId = clientId.ThrowIfEmpty(nameof(clientId)).UnSecure();
        _clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret))
            .UnSecure();
        await AuthenticateCore(true, cancellationToken);
    }

    private async Task AuthenticateCore(
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && !_accessToken.IsEmpty() &&
            _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return;

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (!force && !_accessToken.IsEmpty() &&
                _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return;

            using var request = new HttpRequestMessage(
                HttpMethod.Post, "oauth2/token")
            {
                Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                }),
            };
            using var response = await _httpClient.SendAsync(
                request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreateException(response.StatusCode, text);

            var token = JObject.Parse(text);
            _accessToken = token.Value<string>("access_token");
            if (_accessToken.IsEmpty())
            {
                throw new InvalidDataException(
                    "Toss Securities OAuth response contained no access token.");
            }

            var lifetime = token.Value<int?>("expires_in") ?? 3600;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(60, lifetime));
        }
        finally
        {
            _authLock.Release();
        }
    }

    public Task<TossStock[]> GetStocks(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
        => GetArray<TossStock>(
            Query("api/v1/stocks",
                ("symbols", JoinSymbols(symbols, 200))),
            null,
            cancellationToken);

    public Task<TossPrice[]> GetPrices(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
        => GetArray<TossPrice>(
            Query("api/v1/prices",
                ("symbols", JoinSymbols(symbols, 200))),
            null,
            cancellationToken);

    public Task<TossPrice[]> GetIndicatorPrices(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
        => GetArray<TossPrice>(
            Query("api/v1/market-indicators/prices",
                ("symbols", JoinSymbols(symbols, 200))),
            null,
            cancellationToken);

    public Task<TossOrderBook> GetOrderBook(
        string symbol,
        CancellationToken cancellationToken)
        => Get<TossOrderBook>(
            Query("api/v1/orderbook", ("symbol", symbol)),
            null,
            cancellationToken);

    public Task<TossPublicTrade[]> GetTrades(
        string symbol,
        int count,
        CancellationToken cancellationToken)
        => GetArray<TossPublicTrade>(
            Query(
                "api/v1/trades",
                ("symbol", symbol),
                ("count", Math.Clamp(count, 1, 50).ToString(
                    CultureInfo.InvariantCulture))),
            null,
            cancellationToken);

    public Task<TossPriceLimits> GetPriceLimits(
        string symbol,
        CancellationToken cancellationToken)
        => Get<TossPriceLimits>(
            Query("api/v1/price-limits", ("symbol", symbol)),
            null,
            cancellationToken);

    public Task<TossCandlePage> GetCandles(
        string symbol,
        bool indicator,
        string interval,
        int count,
        DateTimeOffset? before,
        bool adjusted,
        CancellationToken cancellationToken)
    {
        var path = indicator
            ? $"api/v1/market-indicators/{Uri.EscapeDataString(symbol)}/candles"
            : "api/v1/candles";
        var parameters = new List<(string, string)>();
        if (!indicator)
            parameters.Add(("symbol", symbol));
        parameters.Add(("interval", interval));
        parameters.Add(("count", Math.Clamp(count, 1, 200).ToString(
            CultureInfo.InvariantCulture)));
        if (before is not null)
            parameters.Add(("before", before.Value.ToString(
                "O", CultureInfo.InvariantCulture)));
        if (!indicator)
            parameters.Add(("adjusted", adjusted ? "true" : "false"));

        return Get<TossCandlePage>(
            Query(path, [.. parameters]),
            null,
            cancellationToken);
    }

    public Task<TossAccount[]> GetAccounts(
        CancellationToken cancellationToken)
        => GetArray<TossAccount>(
            "api/v1/accounts", null, cancellationToken);

    public Task<TossHoldings> GetHoldings(
        long accountSequence,
        string symbol,
        CancellationToken cancellationToken)
        => Get<TossHoldings>(
            Query("api/v1/holdings", ("symbol", symbol)),
            accountSequence,
            cancellationToken);

    public Task<TossBuyingPower> GetBuyingPower(
        long accountSequence,
        string currency,
        CancellationToken cancellationToken)
        => Get<TossBuyingPower>(
            Query("api/v1/buying-power", ("currency", currency)),
            accountSequence,
            cancellationToken);

    public Task<TossOrderPage> GetOrders(
        long accountSequence,
        string status,
        string symbol,
        DateTime? from,
        DateTime? to,
        string cursor,
        int limit,
        CancellationToken cancellationToken)
        => Get<TossOrderPage>(
            Query(
                "api/v1/orders",
                ("status", status),
                ("symbol", symbol),
                ("from", FormatDate(from)),
                ("to", FormatDate(to)),
                ("cursor", cursor),
                ("limit", Math.Clamp(limit, 1, 100).ToString(
                    CultureInfo.InvariantCulture))),
            accountSequence,
            cancellationToken);

    public Task<TossOrder> GetOrder(
        long accountSequence,
        string orderId,
        CancellationToken cancellationToken)
        => Get<TossOrder>(
            $"api/v1/orders/{Uri.EscapeDataString(orderId)}",
            accountSequence,
            cancellationToken);

    public Task<TossOrderResult> CreateOrder(
        long accountSequence,
        object body,
        CancellationToken cancellationToken)
        => Send<TossOrderResult>(
            "api/v1/orders",
            HttpMethod.Post,
            body,
            accountSequence,
            cancellationToken);

    public Task<TossOrderResult> ModifyOrder(
        long accountSequence,
        string orderId,
        object body,
        CancellationToken cancellationToken)
        => Send<TossOrderResult>(
            $"api/v1/orders/{Uri.EscapeDataString(orderId)}/modify",
            HttpMethod.Post,
            body,
            accountSequence,
            cancellationToken);

    public Task<TossOrderResult> CancelOrder(
        long accountSequence,
        string orderId,
        CancellationToken cancellationToken)
        => Send<TossOrderResult>(
            $"api/v1/orders/{Uri.EscapeDataString(orderId)}/cancel",
            HttpMethod.Post,
            null,
            accountSequence,
            cancellationToken);

    public Task<TossConditionalOrderPage> GetConditionalOrders(
        long accountSequence,
        string status,
        string symbol,
        string cursor,
        int limit,
        CancellationToken cancellationToken)
        => Get<TossConditionalOrderPage>(
            Query(
                "api/v1/conditional-orders",
                ("status", status),
                ("symbol", symbol),
                ("cursor", cursor),
                ("limit", Math.Clamp(limit, 1, 100).ToString(
                    CultureInfo.InvariantCulture))),
            accountSequence,
            cancellationToken);

    public Task<TossConditionalOrder> GetConditionalOrder(
        long accountSequence,
        string orderId,
        CancellationToken cancellationToken)
        => Get<TossConditionalOrder>(
            $"api/v1/conditional-orders/{Uri.EscapeDataString(orderId)}",
            accountSequence,
            cancellationToken);

    public Task<TossOrderResult> CreateConditionalOrder(
        long accountSequence,
        object body,
        CancellationToken cancellationToken)
        => Send<TossOrderResult>(
            "api/v1/conditional-orders",
            HttpMethod.Post,
            body,
            accountSequence,
            cancellationToken);

    public Task<TossOrderResult> ModifyConditionalOrder(
        long accountSequence,
        string orderId,
        object body,
        CancellationToken cancellationToken)
        => Send<TossOrderResult>(
            $"api/v1/conditional-orders/{Uri.EscapeDataString(orderId)}/modify",
            HttpMethod.Post,
            body,
            accountSequence,
            cancellationToken);

    public Task<TossConditionalOrder> CancelConditionalOrder(
        long accountSequence,
        string orderId,
        CancellationToken cancellationToken)
        => Send<TossConditionalOrder>(
            $"api/v1/conditional-orders/{Uri.EscapeDataString(orderId)}",
            HttpMethod.Delete,
            null,
            accountSequence,
            cancellationToken);

    private async Task<T[]> GetArray<T>(
        string path,
        long? accountSequence,
        CancellationToken cancellationToken)
        => await Get<T[]>(path, accountSequence, cancellationToken) ?? [];

    private Task<T> Get<T>(
        string path,
        long? accountSequence,
        CancellationToken cancellationToken)
        => Send<T>(
            path,
            HttpMethod.Get,
            null,
            accountSequence,
            cancellationToken);

    private async Task<T> Send<T>(
        string path,
        HttpMethod method,
        object body,
        long? accountSequence,
        CancellationToken cancellationToken)
    {
        var token = await Send(
            path,
            method,
            body,
            accountSequence,
            cancellationToken);
        return token is null || token.Type == JTokenType.Null
            ? default
            : token.ToObject<T>();
    }

    private async Task<JToken> Send(
        string path,
        HttpMethod method,
        object body,
        long? accountSequence,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await AuthenticateCore(false, cancellationToken);
            await Throttle(cancellationToken);
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
            if (accountSequence is > 0)
            {
                request.Headers.TryAddWithoutValidation(
                    "X-Tossinvest-Account",
                    accountSequence.Value.ToString(
                        CultureInfo.InvariantCulture));
            }
            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body, _jsonSettings),
                    Encoding.UTF8,
                    "application/json");
            }

            using var response = await _httpClient.SendAsync(
                request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                attempt == 0)
            {
                await AuthenticateCore(true, cancellationToken);
                continue;
            }

            if (((int)response.StatusCode == 429 ||
                (int)response.StatusCode >= 500) &&
                attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ??
                    TimeSpan.FromMilliseconds(250 * (attempt + 1));
                if (delay > TimeSpan.FromSeconds(2))
                    delay = TimeSpan.FromSeconds(2);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw CreateException(response.StatusCode, text);
            if (text.IsEmpty())
                return null;

            var envelope = JToken.Parse(text);
            return envelope["result"];
        }
    }

    private async Task Throttle(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            // The endpoints used by this adapter have limits from five to
            // ten requests per second. A shared conservative gate also keeps
            // mixed polling groups below their documented limits.
            _nextRequestAt =
                DateTimeOffset.UtcNow.AddMilliseconds(210);
        }
        finally
        {
            _requestLock.Release();
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
            var error = JObject.Parse(text)["error"];
            code = error?.Value<string>("code");
            message = error?.Value<string>("message");
        }
        catch (JsonException)
        {
            message = text;
        }

        var description = new[] { code, message }
            .Where(value => !value.IsEmpty())
            .Join(": ");
        if (description.IsEmpty())
            description = $"HTTP {(int)statusCode} ({statusCode}).";

        return new HttpRequestException(
            $"Toss Securities Open API: {description}",
            null,
            statusCode);
    }

    private static string JoinSymbols(
        IEnumerable<string> symbols,
        int maximum)
    {
        var values = symbols?
            .Where(symbol => !symbol.IsEmpty())
            .Select(symbol => symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximum)
            .ToArray() ?? [];
        if (values.Length == 0)
            throw new ArgumentException(
                "At least one Toss Securities symbol is required.",
                nameof(symbols));
        return values.Join(",");
    }

    private static string FormatDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Query(
        string path,
        params (string name, string value)[] parameters)
    {
        var query = parameters
            .Where(pair => !pair.value.IsEmpty())
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.name)}=" +
                Uri.EscapeDataString(pair.value))
            .Join("&");
        return query.IsEmpty() ? path : $"{path}?{query}";
    }
}
