namespace StockSharp.Jainam.Native;

sealed class JainamRestClient : BaseLogReceiver
{
    private static readonly string[] _segments =
        ["nse", "nfo", "cds", "bse", "bfo", "bcd", "mcx", "indices"];

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static readonly TimeSpan _limitedRequestInterval =
        TimeSpan.FromMilliseconds(510);

    private readonly HttpClient _httpClient;
    private readonly string _masterAddress;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private readonly SemaphoreSlim _rateLock = new(1, 1);
    private JainamInstrument[] _instruments;
    private IReadOnlyDictionary<string, JainamInstrument> _instrumentsByKey;
    private IReadOnlyDictionary<string, JainamInstrument> _instrumentsBySymbol;
    private DateTime _lastLimitedRequest;
    private string _token;

    public JainamRestClient(
        Uri restAddress,
        string instrumentAddress,
        HttpMessageHandler handler = null)
    {
        _httpClient = handler == null ? new() : new(handler);
        _httpClient.BaseAddress =
            restAddress ?? throw new ArgumentNullException(nameof(restAddress));
        _masterAddress = instrumentAddress.ThrowIfEmpty(nameof(instrumentAddress)).TrimEnd('/') + "/";
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Jainam/1.0");
    }

    public override string Name => nameof(Jainam) + "_" + nameof(JainamRestClient);

    public string SessionToken => _token;

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        _rateLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<(string token, string userId)> Authenticate(
        string userId,
        SecureString sessionToken,
        SecureString authCode,
        SecureString apiSecret,
        CancellationToken cancellationToken)
    {
        var suppliedToken = sessionToken.IsEmpty()
            ? null
            : NormalizeToken(sessionToken.UnSecure());

        if (!suppliedToken.IsEmpty())
        {
            _token = suppliedToken;
            return (_token, userId);
        }

        var resolvedUserId = userId.ThrowIfEmpty(nameof(userId));
        var code = authCode.ThrowIfEmpty(nameof(authCode)).UnSecure();
        var secret = apiSecret.ThrowIfEmpty(nameof(apiSecret)).UnSecure();
        var checksum = $"{resolvedUserId}{code}{secret}".Sha256();
        var response = await SendResponse(
            "omt/auth/sso/vendor/getUserDetails",
            HttpMethod.Post,
            new { checkSum = checksum },
            false,
            false,
            false,
            cancellationToken);

        var result = response.Result ??
            throw new InvalidOperationException("Jainam SSO returned no session result.");
        _token = FindString(
            result,
            "userSession",
            "sessionId",
            "session",
            "accessToken",
            "token",
            "jwtToken")
            .ThrowIfEmpty("userSession");
        _token = NormalizeToken(_token);
        resolvedUserId = FindString(result, "userId", "clientId", "clientCode")
            .IsEmpty(resolvedUserId);
        return (_token, resolvedUserId);
    }

    public async Task<JainamProfile> GetProfile(CancellationToken cancellationToken)
    {
        var response = await SendResponse(
            "omt/api-order-rest/v1/profile/",
            HttpMethod.Get,
            null,
            true,
            true,
            false,
            cancellationToken);
        return ToSingle<JainamProfile>(response.Result, "profile");
    }

    public Task CreateSocketSession(string userId, CancellationToken cancellationToken)
        => ChangeSocketSession(
            "api/client-rest/profile/createWsSess",
            userId,
            cancellationToken);

    public Task InvalidateSocketSession(string userId, CancellationToken cancellationToken)
        => ChangeSocketSession(
            "api/client-rest/profile/invalidateWsSess",
            userId,
            cancellationToken);

    private async Task ChangeSocketSession(
        string path,
        string userId,
        CancellationToken cancellationToken)
    {
        var response = await SendResponse(
            path,
            HttpMethod.Post,
            new JainamSocketSessionRequest
            {
                UserId = userId.ThrowIfEmpty(nameof(userId)),
                Token = BareToken(_token),
            },
            true,
            true,
            false,
            cancellationToken);

        var status = FindString(response.Result, "Status", "status");
        if (!status.IsEmpty() && !status.EqualsIgnoreCase("OK"))
            throw new InvalidOperationException($"Jainam WebSocket session error: {status}.");
    }

    public async Task<JainamInstrument[]> GetInstruments(CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            var pages = await Task.WhenAll(
                _segments.Select(segment => DownloadInstruments(segment, cancellationToken)));
            _instruments =
            [
                ..
                pages
                    .SelectMany(page => page)
                    .Where(instrument =>
                        instrument != null &&
                        !instrument.Exchange.IsEmpty() &&
                        !instrument.Token.IsEmpty())
                    .GroupBy(
                        instrument => instrument.Exchange.ToInstrumentKey(instrument.Token),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last()),
            ];
            _instrumentsByKey = _instruments.ToDictionary(
                instrument => instrument.Exchange.ToInstrumentKey(instrument.Token),
                StringComparer.OrdinalIgnoreCase);
            _instrumentsBySymbol = _instruments
                .Where(instrument => !instrument.TradingSymbol.IsEmpty())
                .GroupBy(
                    instrument => ToSymbolKey(instrument.Exchange, instrument.TradingSymbol),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            return _instruments;
        }
        finally
        {
            _instrumentLock.Release();
        }
    }

    public async Task<JainamInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument)
            ? instrument
            : null;
    }

    public async Task<JainamInstrument> FindInstrument(
        string exchange,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsBySymbol.TryGetValue(
            ToSymbolKey(exchange, tradingSymbol),
            out var instrument)
            ? instrument
            : null;
    }

    public async Task<string> PlaceOrder(
        JainamOrderRequest order,
        CancellationToken cancellationToken)
    {
        var response = await SendResponse(
            "omt/api-order-rest/v1/orders/placeorder",
            HttpMethod.Post,
            new[] { order ?? throw new ArgumentNullException(nameof(order)) },
            true,
            false,
            false,
            cancellationToken);
        return ToArray<JainamOrderResult>(response.Result)
            .FirstOrDefault()?
            .OrderId
            .ThrowIfEmpty(nameof(JainamOrderResult.OrderId));
    }

    public async Task ModifyOrder(
        JainamModifyOrderRequest order,
        CancellationToken cancellationToken)
        => await SendResponse(
            "omt/api-order-rest/v1/orders/modify",
            HttpMethod.Post,
            order ?? throw new ArgumentNullException(nameof(order)),
            true,
            false,
            false,
            cancellationToken);

    public async Task CancelOrder(string orderId, CancellationToken cancellationToken)
        => await SendResponse(
            "omt/api-order-rest/v1/orders/cancel",
            HttpMethod.Post,
            new JainamCancelOrderRequest
            {
                OrderId = orderId.ThrowIfEmpty(nameof(orderId)),
            },
            true,
            false,
            false,
            cancellationToken);

    public Task<JainamOrder[]> GetOrders(CancellationToken cancellationToken)
        => GetArray<JainamOrder>(
            "omt/api-order-rest/v1/orders/book",
            true,
            cancellationToken);

    public Task<JainamTrade[]> GetTrades(CancellationToken cancellationToken)
        => GetArray<JainamTrade>(
            "omt/api-order-rest/v1/orders/trades",
            true,
            cancellationToken);

    public Task<JainamPosition[]> GetPositions(CancellationToken cancellationToken)
        => GetArray<JainamPosition>(
            "omt/api-order-rest/v1/positions",
            true,
            cancellationToken);

    public async Task<JainamHolding[]> GetHoldings(CancellationToken cancellationToken)
    {
        var result = new List<JainamHolding>();

        foreach (var product in new[] { "cnc", "mtf", "mis" })
        {
            result.AddRange(await GetArray<JainamHolding>(
                $"omt/api-order-rest/v1/holdings/{product}",
                true,
                cancellationToken));
        }

        return [.. result];
    }

    public async Task<JainamLimits> GetLimits(CancellationToken cancellationToken)
    {
        var response = await SendResponse(
            "omt/api-order-rest/v1/limits/",
            HttpMethod.Get,
            null,
            true,
            true,
            false,
            cancellationToken);
        return ToSingle<JainamLimits>(response.Result, "limits");
    }

    private async Task<T[]> GetArray<T>(
        string path,
        bool allowNoData,
        CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendResponse(
            path,
            HttpMethod.Get,
            null,
            true,
            true,
            allowNoData,
            cancellationToken);
        return response == null ? [] : ToArray<T>(response.Result);
    }

    private async Task<JainamInstrument[]> DownloadInstruments(
        string segment,
        CancellationToken cancellationToken)
    {
        this.AddVerboseLog("Jainam GET public contract master {0}.", segment);
        var content = await _httpClient.GetStringAsync(
            _masterAddress + segment,
            cancellationToken);
        return ParseInstrumentMaster(segment, content);
    }

    internal static JainamInstrument[] ParseInstrumentMaster(string segment, string content)
    {
        var root = JObject.Parse(content.ThrowIfEmpty(nameof(content)));
        if (segment.EqualsIgnoreCase("indices"))
        {
            var result = new List<JainamInstrument>();
            var groups = root.GetValue("INDICES", StringComparison.OrdinalIgnoreCase) as JArray ??
                throw new InvalidDataException("Jainam INDICES contract master has an invalid envelope.");

            foreach (var group in groups.OfType<JObject>())
            {
                foreach (var property in group.Properties())
                {
                    foreach (var item in property.Value.OfType<JObject>())
                    {
                        var instrument = item.ToObject<JainamInstrument>();
                        if (instrument == null)
                            continue;
                        instrument.Exchange = instrument.Exchange.IsEmpty(property.Name);
                        instrument.TradingSymbol = instrument.Symbol;
                        instrument.FormattedName = instrument.Symbol;
                        instrument.LotSize = "1";
                        instrument.IsIndex = true;
                        result.Add(instrument);
                    }
                }
            }

            return [.. result];
        }

        var key = segment.ToUpperInvariant();
        var array = root.GetValue(key, StringComparison.OrdinalIgnoreCase) as JArray ??
            throw new InvalidDataException($"Jainam {key} contract master has an invalid envelope.");
        return array.ToObject<JainamInstrument[]>() ?? [];
    }

    internal static JainamResponse ParseResponse(
        string operation,
        string content,
        bool allowNoData = false)
    {
        JainamResponse response;
        try
        {
            response = JsonConvert.DeserializeObject<JainamResponse>(content, _jsonSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Jainam returned invalid JSON for {operation}.",
                ex);
        }

        if (response == null)
            throw new InvalidDataException($"Jainam returned an empty response for {operation}.");
        if (response.Status.EqualsIgnoreCase("Ok"))
            return response;
        if (allowNoData && IsNoData(response))
            return null;

        var detail = FindString(
            response.Result,
            "errorMessage",
            "message",
            "error",
            "code");
        throw new InvalidOperationException(
            $"Jainam {operation} error: {detail.IsEmpty(response.InfoMessage).IsEmpty(response.Message).IsEmpty(response.Status)}");
    }

    private async Task<JainamResponse> SendResponse(
        string path,
        HttpMethod method,
        object body,
        bool useAuthentication,
        bool limited,
        bool allowNoData,
        CancellationToken cancellationToken)
    {
        var content = await SendRaw(
            path,
            method,
            body,
            useAuthentication,
            limited,
            cancellationToken);
        return ParseResponse(path, content, allowNoData);
    }

    private async Task<string> SendRaw(
        string path,
        HttpMethod method,
        object body,
        bool useAuthentication,
        bool limited,
        CancellationToken cancellationToken)
    {
        if (limited)
            await WaitForRateLimit(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        if (useAuthentication)
        {
            var token = _token.ThrowIfEmpty(nameof(SessionToken));
            request.Headers.TryAddWithoutValidation("Authorization", token);
        }
        if (body != null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings),
                Encoding.UTF8,
                "application/json");
        }

        this.AddVerboseLog("Jainam {0} {1}.", method.Method, path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TryGetErrorMessage(content);
            throw new HttpRequestException(
                $"Jainam {path} returned HTTP {(int)response.StatusCode}: {detail.IsEmpty(response.ReasonPhrase)}");
        }
        if (content.IsEmpty())
            throw new InvalidDataException($"Jainam returned an empty response for {path}.");
        return content;
    }

    private async Task WaitForRateLimit(CancellationToken cancellationToken)
    {
        await _rateLock.WaitAsync(cancellationToken);
        try
        {
            var delay = _limitedRequestInterval - (DateTime.UtcNow - _lastLimitedRequest);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _lastLimitedRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateLock.Release();
        }
    }

    private static T ToSingle<T>(JToken result, string operation)
        where T : class
    {
        var token = result is JArray array ? array.FirstOrDefault() : result;
        return token?.ToObject<T>() ??
            throw new InvalidDataException($"Jainam returned no data for {operation}.");
    }

    private static T[] ToArray<T>(JToken result)
        where T : class
    {
        if (result == null || result.Type == JTokenType.Null)
            return [];
        if (result is JArray array)
            return array.ToObject<T[]>() ?? [];
        var item = result.ToObject<T>();
        return item == null ? [] : [item];
    }

    private static bool IsNoData(JainamResponse response)
    {
        var message = response.Message.IsEmpty(response.InfoMessage);
        return message?.Contains("no data", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("no record", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("no holding", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("no position", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("no order", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("no trade", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string FindString(JToken token, params string[] names)
    {
        if (token == null)
            return null;

        IEnumerable<JToken> items = token is JContainer container
            ? container.DescendantsAndSelf()
            : [token];

        foreach (var item in items)
        {
            if (item is not JProperty property ||
                !names.Any(name => property.Name.EqualsIgnoreCase(name)))
                continue;
            var value = property.Value.Type == JTokenType.String
                ? property.Value.Value<string>()
                : property.Value.Type is JTokenType.Integer or JTokenType.Float
                    ? Convert.ToString(
                        property.Value.Value<object>(),
                        CultureInfo.InvariantCulture)
                    : null;
            if (!value.IsEmpty())
                return value;
        }

        return null;
    }

    private static string TryGetErrorMessage(string content)
    {
        if (content.IsEmpty())
            return null;
        try
        {
            var token = JToken.Parse(content);
            return FindString(token, "errorMessage", "message", "error", "detail");
        }
        catch (JsonException)
        {
            return content.Length <= 200 ? content : null;
        }
    }

    private static string ToSymbolKey(string exchange, string tradingSymbol)
        => $"{exchange?.ToUpperInvariant()}|{tradingSymbol?.ToUpperInvariant()}";

    private static string NormalizeToken(string token)
        => token.ThrowIfEmpty(nameof(token)).Trim().Trim('"');

    private static string BareToken(string token)
        => token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token[7..].Trim()
            : token;
}
