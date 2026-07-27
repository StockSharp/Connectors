namespace StockSharp.Primary.Native;

sealed class PrimaryRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _authenticationSync = new(1, 1);
    private readonly string _username;
    private readonly string _password;
    private string _token;

    public PrimaryRestClient(
        Uri endpoint,
        string username,
        SecureString password,
        SecureString token,
        HttpMessageHandler handler = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Primary REST endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        _username = username?.Trim();
        _password = password?.UnSecure();
        _token = token?.UnSecure();
        if (_token.IsEmpty() &&
            (_username.IsEmpty() || _password.IsEmpty()))
        {
            throw new ArgumentException(
                "Primary requires either an active token or a username and password.");
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
            "StockSharp-Primary/1.0");
    }

    public override string Name => "Primary_REST";

    public string Token => _token;

    public async Task Authenticate(CancellationToken cancellationToken)
    {
        await EnsureAuthenticated(_token.IsEmpty(), cancellationToken);
        await GetSegments(cancellationToken);
    }

    public async Task<JArray> GetSegments(
        CancellationToken cancellationToken)
        => (await Get("rest/segment/all", cancellationToken))
            ["segments"] as JArray ?? [];

    public async Task<PrimaryInstrument[]> GetInstruments(
        bool detailed,
        CancellationToken cancellationToken)
        => ExtractArray<PrimaryInstrument>(
            await Get(
                detailed
                    ? "rest/instruments/details"
                    : "rest/instruments/all",
                cancellationToken),
            "instruments");

    public async Task<PrimaryInstrument> GetInstrument(
        PrimarySecurityKey security,
        CancellationToken cancellationToken)
        => ExtractObject<PrimaryInstrument>(
            await Get(
                Query(
                    "rest/instruments/detail",
                    ("symbol", security.Symbol),
                    ("marketId", security.Market)),
                cancellationToken),
            "instrument");

    public async Task<PrimaryMarketUpdate> GetMarketData(
        PrimarySecurityKey security,
        IEnumerable<string> entries,
        int depth,
        CancellationToken cancellationToken)
    {
        var root = await Get(
            Query(
                "rest/marketdata/get",
                ("marketId", security.Market),
                ("symbol", security.Symbol),
                ("entries", string.Join(',', entries)),
                ("depth", Math.Max(1, depth).ToString(
                    CultureInfo.InvariantCulture))),
            cancellationToken);
        return new()
        {
            InstrumentId = new()
            {
                MarketId = security.Market,
                Symbol = security.Symbol,
            },
            MarketData = root["marketData"] as JObject,
            Depth = root.Value<int?>("depth") ?? depth,
            Aggregated = root.Value<bool?>("aggregated") ?? true,
        };
    }

    public async Task<PrimaryTrade[]> GetTrades(
        PrimarySecurityKey security,
        DateTime from,
        DateTime to,
        bool isDemo,
        CancellationToken cancellationToken)
        => ExtractArray<PrimaryTrade>(
            await Get(
                Query(
                    "rest/data/getTrades",
                    ("marketId", security.Market),
                    ("symbol", security.Symbol),
                    ("dateFrom", from.ToString(
                        "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("dateTo", to.ToString(
                        "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("external", (
                        security.BoardCode.EqualsIgnoreCase("BYMA"))
                            .ToString().ToLowerInvariant()),
                    ("environment", isDemo ? "REMARKETS" : null)),
                cancellationToken),
            "trades");

    public async Task<PrimaryOrderReference> NewOrder(
        PrimarySecurityKey security,
        string account,
        Sides side,
        OrderTypes? orderType,
        decimal quantity,
        decimal price,
        TimeInForce? timeInForce,
        DateTime? tillDate,
        bool cancelPrevious,
        bool iceberg,
        decimal? displayQuantity,
        CancellationToken cancellationToken)
    {
        var type = orderType.ToNative();
        return ExtractObject<PrimaryOrderReference>(
            await Get(
                Query(
                    "rest/order/newSingleOrder",
                    ("marketId", security.Market),
                    ("symbol", security.Symbol),
                    ("account", account),
                    ("side", side.ToNative()),
                    ("orderQty", ToString(quantity)),
                    ("ordType", type),
                    ("timeInForce", timeInForce.ToNative(tillDate)),
                    ("price", type == "LIMIT"
                        ? ToString(price)
                        : null),
                    ("cancelPrevious",
                        cancelPrevious.ToString().ToLowerInvariant()),
                    ("iceberg", iceberg.ToString().ToLowerInvariant()),
                    ("displayQty", iceberg
                        ? ToString(displayQuantity ?? 0)
                        : null),
                    ("expireDate", tillDate?.ToString(
                        "yyyyMMdd", CultureInfo.InvariantCulture))),
                cancellationToken),
            "order");
    }

    public async Task<PrimaryOrderReference> ReplaceOrder(
        string clientOrderId,
        string proprietary,
        decimal quantity,
        decimal price,
        CancellationToken cancellationToken)
        => ExtractObject<PrimaryOrderReference>(
            await Get(
                Query(
                    "rest/order/replaceById",
                    ("clOrdId", clientOrderId),
                    ("proprietary", proprietary),
                    ("orderQty", ToString(quantity)),
                    ("price", ToString(price))),
                cancellationToken),
            "order");

    public async Task<PrimaryOrderReference> CancelOrder(
        string clientOrderId,
        string proprietary,
        CancellationToken cancellationToken)
        => ExtractObject<PrimaryOrderReference>(
            await Get(
                Query(
                    "rest/order/cancelById",
                    ("clOrdId", clientOrderId),
                    ("proprietary", proprietary)),
                cancellationToken),
            "order");

    public async Task<PrimaryOrder> GetOrder(
        string clientOrderId,
        string proprietary,
        CancellationToken cancellationToken)
        => ExtractObject<PrimaryOrder>(
            await Get(
                Query(
                    "rest/order/id",
                    ("clOrdId", clientOrderId),
                    ("proprietary", proprietary)),
                cancellationToken),
            "order");

    public async Task<PrimaryOrder[]> GetOrders(
        string account,
        CancellationToken cancellationToken)
        => ExtractArray<PrimaryOrder>(
            await Get(
                Query(
                    "rest/order/all",
                    ("accountId", account)),
                cancellationToken),
            "orders");

    public async Task<PrimaryPosition[]> GetPositions(
        string account,
        CancellationToken cancellationToken)
        => ExtractArray<PrimaryPosition>(
            await Get(
                "rest/risk/position/getPositions/" +
                    Uri.EscapeDataString(account),
                cancellationToken),
            "positions");

    public async Task<PrimaryAccountReport> GetAccountReport(
        string account,
        CancellationToken cancellationToken)
        => ExtractObject<PrimaryAccountReport>(
            await Get(
                "rest/risk/accountReport/" +
                    Uri.EscapeDataString(account),
                cancellationToken),
            "accountData");

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _authenticationSync.Dispose();
        base.DisposeManaged();
    }

    internal static string Query(
        string path,
        params (string key, string value)[] parameters)
    {
        var values = parameters
            .Where(pair => !pair.value.IsEmpty())
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.key)}=" +
                Uri.EscapeDataString(pair.value));
        var query = string.Join("&", values);
        return query.IsEmpty() ? path : $"{path}?{query}";
    }

    private async Task<JObject> Get(
        string relativeUri,
        CancellationToken cancellationToken)
        => await Send(
            HttpMethod.Get, relativeUri, false, cancellationToken);

    private async Task<JObject> Send(
        HttpMethod method,
        string relativeUri,
        bool retried,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticated(false, cancellationToken);
        using var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.TryAddWithoutValidation(
            "X-Auth-Token", _token);
        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            !retried &&
            !_username.IsEmpty() &&
            !_password.IsEmpty())
        {
            await EnsureAuthenticated(true, cancellationToken);
            return await Send(
                method, relativeUri, true, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Primary API returned {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}): {GetError(content)}",
                null,
                response.StatusCode);
        }
        if (content.IsEmpty())
            return [];

        JObject root;
        try
        {
            root = JObject.Parse(content);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Primary API returned invalid JSON.", error);
        }

        if (root.Value<string>("status")
            .EqualsIgnoreCase("ERROR"))
        {
            throw new InvalidOperationException(
                "Primary API: " +
                root.Value<string>("description")
                    .IsEmpty(root.Value<string>("message"))
                    .IsEmpty("Unknown API error."));
        }
        return root;
    }

    private async Task EnsureAuthenticated(
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && !_token.IsEmpty())
            return;

        await _authenticationSync.WaitAsync(cancellationToken);
        try
        {
            if (!force && !_token.IsEmpty())
                return;
            if (_username.IsEmpty() || _password.IsEmpty())
            {
                throw new InvalidOperationException(
                    "Primary access token is missing or expired and no username/password credentials are configured.");
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post, "auth/getToken");
            request.Headers.TryAddWithoutValidation(
                "X-Username", _username);
            request.Headers.TryAddWithoutValidation(
                "X-Password", _password);
            request.Content = new StringContent(
                string.Empty, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var content = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Primary authentication returned " +
                    $"{(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}): {GetError(content)}",
                    null,
                    response.StatusCode);
            }
            if (!response.Headers.TryGetValues(
                "X-Auth-Token", out var values))
            {
                throw new InvalidDataException(
                    "Primary authentication did not return the X-Auth-Token header.");
            }

            _token = values.FirstOrDefault()?.Trim();
            if (_token.IsEmpty())
            {
                throw new InvalidDataException(
                    "Primary authentication returned an empty token.");
            }
        }
        finally
        {
            _authenticationSync.Release();
        }
    }

    private static T[] ExtractArray<T>(
        JObject root,
        string property)
        => root?[property] is JArray array
            ? array.ToObject<T[]>(
                JsonSerializer.Create(_jsonSettings)) ?? []
            : [];

    private static T ExtractObject<T>(
        JObject root,
        string property)
        where T : class
        => root?[property]?.ToObject<T>(
            JsonSerializer.Create(_jsonSettings));

    private static string ToString(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string GetError(string content)
    {
        if (content.IsEmpty())
            return "empty response";
        try
        {
            var root = JObject.Parse(content);
            return root.Value<string>("description")
                .IsEmpty(root.Value<string>("message"))
                .IsEmpty(content);
        }
        catch (JsonException)
        {
            return content;
        }
    }
}
