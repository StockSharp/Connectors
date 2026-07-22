namespace StockSharp.FXOpen.Native;

readonly record struct FXOpenParameter(string Name, string Value);

sealed class FXOpenRestClient : BaseLogReceiver
{
    private const int _maxAttempts = 4;
    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly string _webApiId;
    private readonly string _key;
    private readonly string _secret;
    private readonly SemaphoreSlim _rateSync = new(1, 1);
    private DateTime _nextRequestTime;
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    public FXOpenRestClient(string endpoint, string webApiId, SecureString key, SecureString secret)
    {
        _endpoint = new Uri(NormalizeEndpoint(endpoint), UriKind.Absolute);
        _webApiId = webApiId;
        _key = key.IsEmpty() ? null : key.UnSecure();
        _secret = secret.IsEmpty() ? null : secret.UnSecure();
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-FXOpen-Connector/1.0");
    }

    public override string Name => nameof(FXOpen) + "_Rest";

    public bool IsCredentialsAvailable
        => !_webApiId.IsEmpty() && !_key.IsEmpty() && !_secret.IsEmpty();

    protected override void DisposeManaged()
    {
        _rateSync.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<TickTraderSymbol[]> GetSymbolsAsync(CancellationToken cancellationToken)
        => SendAsync<TickTraderSymbol[]>(HttpMethod.Get, "/api/v2/public/symbol", null,
            false, true, cancellationToken);

    public ValueTask<TickTraderFeedTick[]> GetTicksAsync(string symbol,
        CancellationToken cancellationToken)
        => SendAsync<TickTraderFeedTick[]>(HttpMethod.Get,
            $"/api/v2/public/tick/{Escape(symbol)}", null, false, true, cancellationToken);

    public ValueTask<TickTraderFeedTick[]> GetLevel2Async(string symbol,
        CancellationToken cancellationToken)
        => SendAsync<TickTraderFeedTick[]>(HttpMethod.Get,
            $"/api/v2/public/level2/{Escape(symbol)}", null, false, true, cancellationToken);

    public ValueTask<TickTraderTicks> GetTickHistoryAsync(string symbol, bool isLevel2,
        DateTime timestamp, int count, CancellationToken cancellationToken)
        => SendAsync<TickTraderTicks>(HttpMethod.Get,
            $"/api/v2/public/quotehistory/{Escape(symbol)}/" +
            $"{(isLevel2 ? "level2" : "ticks")}" +
            $"?timestamp={checked((long)timestamp.EnsureUtc().ToUnix(false))}&count={count}",
            null, false, true, cancellationToken);

    public ValueTask<TickTraderBars> GetBarsAsync(string symbol, string periodicity,
        TickTraderPriceTypes priceType, DateTime timestamp, int count,
        CancellationToken cancellationToken)
        => SendAsync<TickTraderBars>(HttpMethod.Get,
            $"/api/v2/public/quotehistory/{Escape(symbol)}/{Escape(periodicity)}/bars/" +
            $"{priceType.ToString().ToLowerInvariant()}" +
            $"?timestamp={checked((long)timestamp.EnsureUtc().ToUnix(false))}&count={count}",
            null, false, true,
            cancellationToken);

    public ValueTask<TickTraderAccount> GetAccountAsync(CancellationToken cancellationToken)
        => SendAsync<TickTraderAccount>(HttpMethod.Get, "/api/v2/account", null, true, true,
            cancellationToken);

    public ValueTask<TickTraderAsset[]> GetAssetsAsync(CancellationToken cancellationToken)
        => SendAsync<TickTraderAsset[]>(HttpMethod.Get, "/api/v2/asset", null, true, true,
            cancellationToken);

    public ValueTask<TickTraderPosition[]> GetPositionsAsync(CancellationToken cancellationToken)
        => SendAsync<TickTraderPosition[]>(HttpMethod.Get, "/api/v2/position", null, true, true,
            cancellationToken);

    public ValueTask<TickTraderTrade[]> GetTradesAsync(CancellationToken cancellationToken)
        => SendAsync<TickTraderTrade[]>(HttpMethod.Get, "/api/v2/trade", null, true, true,
            cancellationToken);

    public ValueTask<TickTraderTrade> GetTradeAsync(long tradeId,
        CancellationToken cancellationToken)
        => SendAsync<TickTraderTrade>(HttpMethod.Get, $"/api/v2/trade/{tradeId}", null,
            true, true, cancellationToken);

    public ValueTask<TickTraderTrade> CreateTradeAsync(TickTraderTradeCreate request,
        CancellationToken cancellationToken)
        => SendAsync<TickTraderTrade, TickTraderTradeCreate>(HttpMethod.Post,
            "/api/v2/trade", request,
            true, false, cancellationToken);

    public ValueTask<TickTraderTrade> ModifyTradeAsync(TickTraderTradeModify request,
        CancellationToken cancellationToken)
        => SendAsync<TickTraderTrade, TickTraderTradeModify>(HttpMethod.Put,
            "/api/v2/trade", request,
            true, false, cancellationToken);

    public ValueTask<TickTraderTradeDelete> DeleteTradeAsync(long tradeId,
        TickTraderDeleteTypes type, decimal? amount, long? byTradeId,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v2/trade?type={type}&id={tradeId}";
        if (amount is not null)
            path += "&amount=" + amount.Value.ToString(CultureInfo.InvariantCulture);
        if (byTradeId is not null)
            path += "&byId=" + byTradeId.Value.ToString(CultureInfo.InvariantCulture);
        return SendAsync<TickTraderTradeDelete>(HttpMethod.Delete, path, null,
            true, false, cancellationToken, true);
    }

    public ValueTask<TickTraderHistoryReport> GetTradeHistoryAsync(
        TickTraderHistoryRequest request, CancellationToken cancellationToken)
        => SendAsync<TickTraderHistoryReport, TickTraderHistoryRequest>(HttpMethod.Post,
            "/api/v2/tradehistory", request,
            true, true, cancellationToken);

    private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method, string path,
        TResponse requestBody, bool isPrivate, bool isSafe, CancellationToken cancellationToken,
        bool allowEmptyResponse = false)
        where TResponse : class
    {
        return await SendCoreAsync<TResponse, TResponse>(method, path, requestBody, isPrivate,
            isSafe, cancellationToken, allowEmptyResponse);
    }

    private async ValueTask<TResponse> SendAsync<TResponse, TRequest>(HttpMethod method,
        string path, TRequest requestBody, bool isPrivate, bool isSafe,
        CancellationToken cancellationToken, bool allowEmptyResponse = false)
        where TResponse : class
        where TRequest : class
    {
        return await SendCoreAsync<TResponse, TRequest>(method, path, requestBody, isPrivate,
            isSafe, cancellationToken, allowEmptyResponse);
    }

    private async ValueTask<TResponse> SendCoreAsync<TResponse, TRequest>(HttpMethod method,
        string path, TRequest requestBody, bool isPrivate, bool isSafe,
        CancellationToken cancellationToken, bool allowEmptyResponse)
        where TResponse : class
        where TRequest : class
    {
        if (isPrivate)
            EnsureCredentials();

        var body = requestBody is null
            ? string.Empty
            : JsonConvert.SerializeObject(requestBody, _jsonSettings);

        for (var attempt = 1; ; attempt++)
        {
            await WaitForRateLimitAsync(cancellationToken);
            var uri = new Uri(_endpoint, path);
            this.AddDebugLog("FXOpen REST {0} {1}, attempt {2}.", method.Method,
                uri.PathAndQuery, attempt);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (isPrivate)
                request.Headers.Authorization = new AuthenticationHeaderValue("HMAC",
                    CreateAuthorization(method, uri, body));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException error) when (isSafe && attempt < _maxAttempts)
            {
                this.AddWarningLog("FXOpen {0} transport error. Retrying read request: {1}",
                    path, error.Message);
                await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
                continue;
            }
            catch (HttpRequestException error)
            {
                throw new InvalidOperationException(CreateTransportError(path, isSafe, error.Message),
                    error);
            }

            using (response)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (isSafe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500) && attempt < _maxAttempts)
                {
                    var retryDelay = GetRetryDelay(response, attempt);
                    this.AddWarningLog("FXOpen REST {0} returned HTTP {1}. Retrying in {2}.",
                        path, (int)response.StatusCode, retryDelay);
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"FXOpen {path} returned HTTP " +
                        $"{(int)response.StatusCode}: {responseBody}. " +
                        (isSafe ? "The read request failed." :
                        "The write was not retried; inspect broker state before retrying."));
                if (responseBody.IsEmpty() && allowEmptyResponse)
                {
                    this.AddDebugLog("FXOpen REST {0} {1} returned HTTP {2} without a body.",
                        method.Method, uri.PathAndQuery, (int)response.StatusCode);
                    return null;
                }
                if (responseBody.IsEmpty())
                    throw new InvalidDataException($"FXOpen {path} returned an empty response.");

                try
                {
                    var result = JsonConvert.DeserializeObject<TResponse>(responseBody, _jsonSettings)
                        ?? throw new InvalidDataException(
                            $"FXOpen {path} returned no JSON value.");
                    this.AddDebugLog("FXOpen REST {0} {1} completed with HTTP {2}.",
                        method.Method, uri.PathAndQuery, (int)response.StatusCode);
                    return result;
                }
                catch (JsonException error)
                {
                    throw new InvalidDataException($"FXOpen {path} returned invalid JSON.", error);
                }
            }
        }
    }

    private string CreateAuthorization(HttpMethod method, Uri uri, string body)
    {
        var timestamp = checked((long)DateTime.UtcNow.ToUnix(false));
        var source = timestamp.ToString(CultureInfo.InvariantCulture) + _webApiId + _key +
            method.Method + uri + body;
        using var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(_secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(source)));
        return $"{_webApiId}:{_key}:{timestamp}:{signature}";
    }

    private async ValueTask WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        await _rateSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequestTime - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(75);
        }
        finally
        {
            _rateSync.Release();
        }
    }

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "FXOpen Web API ID, key, and secret are required for private requests.");
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is TimeSpan delay && delay > TimeSpan.Zero)
            return delay.Min(TimeSpan.FromSeconds(30));
        if (response?.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
        {
            var dateDelay = retryAt - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
                return dateDelay.Min(TimeSpan.FromSeconds(30));
        }
        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private static string CreateTransportError(string path, bool isSafe, string message)
        => $"FXOpen {path} transport error: {message}. " +
            (isSafe ? "The read request failed." :
            "The write may have reached FXOpen; inspect broker state before retrying.");

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"https://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }
}
