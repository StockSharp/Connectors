namespace StockSharp.WhiteBit.Native;

sealed class WhiteBitRestClient : BaseLogReceiver
{
    private const int _maxAttempts = 4;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new();
    private readonly string _apiKey;
    private readonly HMACSHA512 _hasher;
    private readonly Lock _signSync = new();
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };
    private long _lastNonce;

    public WhiteBitRestClient(string endpoint, SecureString key, SecureString secret)
    {
        _endpoint = new Uri(NormalizeEndpoint(endpoint), UriKind.Absolute);
        _apiKey = key.IsEmpty() ? null : key.UnSecure();
        _hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().UTF8());
    }

    public override string Name => nameof(WhiteBit) + "_Rest";

    protected override void DisposeManaged()
    {
        _hasher?.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<WhiteBitMarket[]> GetMarketsAsync(CancellationToken cancellationToken)
        => SendPublicAsync<WhiteBitMarket[]>("/api/v4/public/markets", cancellationToken);

    public async ValueTask<WhiteBitTicker[]> GetTickersAsync(CancellationToken cancellationToken)
        => (await SendPublicAsync<WhiteBitTickerCollection>("/api/v4/public/ticker", cancellationToken)).Items;

    public ValueTask<WhiteBitOrderBook> GetOrderBookAsync(string symbol, int depth,
        CancellationToken cancellationToken)
        => SendPublicAsync<WhiteBitOrderBook>($"/api/v4/public/orderbook/{Escape(symbol)}?limit={depth}&level=0",
            cancellationToken);

    public ValueTask<WhiteBitPublicTrade[]> GetTradesAsync(string symbol,
        CancellationToken cancellationToken)
        => SendPublicAsync<WhiteBitPublicTrade[]>($"/api/v4/public/trades/{Escape(symbol)}", cancellationToken);

    public async ValueTask<WhiteBitCandle[]> GetCandlesAsync(string symbol, TimeSpan timeFrame,
        DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
    {
        var end = (to ?? DateTime.UtcNow).ToUnix();
        var start = (from ?? (to ?? DateTime.UtcNow) - TimeSpan.FromTicks(timeFrame.Ticks * limit)).ToUnix();
        var path = $"/api/v1/public/kline?market={Escape(symbol)}&start={start}&end={end}" +
            $"&interval={Escape(timeFrame.ToNative())}&limit={limit.Min(1440).Max(1)}";
        var response = await SendPublicAsync<WhiteBitKlineResponse>(path, cancellationToken);
        if (!response.IsSuccess)
            throw new InvalidOperationException($"WhiteBIT kline failed: {response.Message}");
        return response.Result ?? [];
    }

    public ValueTask<WhiteBitWebSocketToken> GetWebSocketTokenAsync(CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitWebSocketToken>("/api/v4/profile/websocket_token",
            new WhiteBitEmptyPrivateRequest(), true, cancellationToken);

    public ValueTask<WhiteBitOrder> RegisterLimitOrderAsync(WhiteBitLimitOrderRequest request,
        bool isCollateral, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder>(isCollateral
            ? "/api/v4/order/collateral/limit"
            : "/api/v4/order/new", request, false, cancellationToken);

    public ValueTask<WhiteBitOrder> RegisterMarketOrderAsync(WhiteBitMarketOrderRequest request,
        bool isCollateral, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder>(isCollateral
            ? "/api/v4/order/collateral/market"
            : "/api/v4/order/market", request, false, cancellationToken);

    public ValueTask<WhiteBitOrder> RegisterStopLimitOrderAsync(WhiteBitStopLimitOrderRequest request,
        bool isCollateral, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder>(isCollateral
            ? "/api/v4/order/collateral/stop-limit"
            : "/api/v4/order/stop_limit", request, false, cancellationToken);

    public ValueTask<WhiteBitOrder> RegisterStopMarketOrderAsync(WhiteBitStopMarketOrderRequest request,
        bool isCollateral, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder>(isCollateral
            ? "/api/v4/order/collateral/trigger-market"
            : "/api/v4/order/stop_market", request, false, cancellationToken);

    public ValueTask<WhiteBitOrder> ModifyOrderAsync(WhiteBitModifyOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder>("/api/v4/order/modify", request, false, cancellationToken);

    public ValueTask<WhiteBitOrder> CancelOrderAsync(WhiteBitCancelOrderRequest request,
        bool isConditionalCollateral, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder>(isConditionalCollateral
            ? "/api/v4/order/conditional-cancel"
            : "/api/v4/order/cancel", request, false, cancellationToken);

    public ValueTask<WhiteBitEmptyResult> CancelAllOrdersAsync(WhiteBitCancelAllOrdersRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitEmptyResult>("/api/v4/order/cancel/all", request, false, cancellationToken);

    public ValueTask<WhiteBitOrder[]> GetOpenOrdersAsync(string symbol, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitOrder[]>("/api/v4/orders", new WhiteBitOrdersRequest
        {
            Market = symbol,
            Limit = 100,
        }, true, cancellationToken);

    public async ValueTask<WhiteBitOrder[]> GetOrderHistoryAsync(string symbol, int limit,
        CancellationToken cancellationToken)
    {
        var result = (await SendPrivateAsync<WhiteBitOrderHistoryCollection>("/api/v4/trade-account/order/history",
            new WhiteBitOrderHistoryRequest
            {
                Market = symbol,
                Limit = limit.Min(500).Max(1),
            }, true, cancellationToken)).Items;
        if (!symbol.IsEmpty())
        {
            foreach (var order in result)
                order.Market = order.Market.IsEmpty(symbol);
        }
        foreach (var order in result)
            order.IsHistory = true;
        return result;
    }

    public async ValueTask<WhiteBitOrder[]> GetConditionalOrdersAsync(string symbol,
        CancellationToken cancellationToken)
        => (await SendPrivateAsync<WhiteBitOrderCollection>("/api/v4/conditional-orders",
            new WhiteBitOrdersRequest
            {
                Market = symbol,
                Limit = 100,
            }, true, cancellationToken)).Records ?? [];

    public async ValueTask<WhiteBitUserTrade[]> GetExecutedHistoryAsync(string symbol, int limit,
        CancellationToken cancellationToken)
    {
        var result = (await SendPrivateAsync<WhiteBitUserTradeCollection>("/api/v4/trade-account/executed-history",
            new WhiteBitExecutedHistoryRequest
            {
                Market = symbol,
                Limit = limit.Min(500).Max(1),
            }, true, cancellationToken)).Items;
        if (!symbol.IsEmpty())
        {
            foreach (var trade in result)
                trade.Market = trade.Market.IsEmpty(symbol);
        }
        return result;
    }

    public async ValueTask<WhiteBitSpotBalance[]> GetSpotBalancesAsync(CancellationToken cancellationToken)
        => (await SendPrivateAsync<WhiteBitSpotBalanceCollection>("/api/v4/trade-account/balance",
            new WhiteBitBalanceRequest(), true, cancellationToken)).Items;

    public ValueTask<WhiteBitMarginBalance[]> GetMarginBalancesAsync(CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitMarginBalance[]>("/api/v4/collateral-account/balance-summary",
            new WhiteBitBalanceRequest(), true, cancellationToken);

    public ValueTask<WhiteBitPosition[]> GetPositionsAsync(string symbol, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitPosition[]>("/api/v4/collateral-account/positions",
            new WhiteBitPositionsRequest { Market = symbol }, true, cancellationToken);

    public ValueTask<WhiteBitEmptyResult> ClosePositionAsync(string symbol,
        WhiteBitPositionSides? positionSide, CancellationToken cancellationToken)
        => SendPrivateAsync<WhiteBitEmptyResult>("/api/v4/collateral-account/position/close",
            new WhiteBitClosePositionRequest
            {
                Market = symbol,
                PositionSide = positionSide,
            }, false, cancellationToken);

    private ValueTask<TResult> SendPublicAsync<TResult>(string path, CancellationToken cancellationToken)
        => SendAsync<TResult>(HttpMethod.Get, path, null, true, cancellationToken);

    private ValueTask<TResult> SendPrivateAsync<TResult>(string path, WhiteBitPrivateRequest request,
        bool safe, CancellationToken cancellationToken)
    {
        if (_apiKey.IsEmpty())
            throw new InvalidOperationException("WhiteBIT API key is not specified.");
        if (_hasher is null)
            throw new InvalidOperationException("WhiteBIT API secret is not specified.");

        request.Request = path;
        request.Nonce = NextNonce();
        var body = JsonConvert.SerializeObject(request, _jsonSettings);
        var payload = Convert.ToBase64String(body.UTF8());
        string signature;
        using (_signSync.EnterScope())
            signature = _hasher.ComputeHash(payload.UTF8()).Digest().ToLowerInvariant();

        return SendAsync<TResult>(HttpMethod.Post, path,
            new WhiteBitPrivatePayload(body, payload, signature), safe, cancellationToken);
    }

    private async ValueTask<TResult> SendAsync<TResult>(HttpMethod method, string path,
        WhiteBitPrivatePayload privatePayload, bool safe, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(_endpoint, path));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (privatePayload is not null)
            {
                request.Headers.TryAddWithoutValidation("X-TXC-APIKEY", _apiKey);
                request.Headers.TryAddWithoutValidation("X-TXC-PAYLOAD", privatePayload.Payload);
                request.Headers.TryAddWithoutValidation("X-TXC-SIGNATURE", privatePayload.Signature);
                request.Content = new StringContent(privatePayload.Body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException error) when (safe && attempt < _maxAttempts)
            {
                this.AddWarningLog("WhiteBIT {0} transport error. Retrying safe request: {1}", path, error.Message);
                await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
                continue;
            }
            catch (HttpRequestException error)
            {
                var outcome = safe ? string.Empty : " Trading operation outcome is unknown; it was not retried.";
                throw new InvalidOperationException($"WhiteBIT {path} failed: {error.Message}.{outcome}", error);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (safe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500) && attempt < _maxAttempts)
                {
                    await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw CreateError(response.StatusCode, path, body, safe);

                WhiteBitApiStatus status;
                try
                {
                    status = JsonConvert.DeserializeObject<WhiteBitApiStatus>(body, _jsonSettings);
                }
                catch (JsonException)
                {
                    status = null;
                }

                if (status?.IsSuccess == false || status?.Code is not null and not 0)
                    throw CreateError(response.StatusCode, path, body, safe);

                try
                {
                    return JsonConvert.DeserializeObject<TResult>(body, _jsonSettings)
                        ?? throw new InvalidDataException($"WhiteBIT {path} returned no JSON value.");
                }
                catch (JsonException error)
                {
                    throw new InvalidDataException($"WhiteBIT {path} returned invalid JSON.", error);
                }
            }
        }
    }

    private long NextNonce()
    {
        while (true)
        {
            var current = Volatile.Read(ref _lastNonce);
            var next = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().Max(current + 1);
            if (Interlocked.CompareExchange(ref _lastNonce, next, current) == current)
                return next;
        }
    }

    private static Exception CreateError(HttpStatusCode statusCode, string path, string body, bool safe)
    {
        var outcome = safe || (int)statusCode < 500
            ? string.Empty
            : " Trading operation outcome is unknown; do not submit it again without checking order state.";
        return new InvalidOperationException(
            $"WhiteBIT {path} failed ({(int)statusCode}): {body.IsEmpty("empty response")}.{outcome}");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
            return retryAfter.Min(TimeSpan.FromSeconds(60));
        return TimeSpan.FromSeconds(1 << attempt.Min(5));
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"https://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/') + "/";
    }

    private sealed class WhiteBitPrivatePayload
    {
        public WhiteBitPrivatePayload(string body, string payload, string signature)
        {
            Body = body;
            Payload = payload;
            Signature = signature;
        }

        public string Body { get; }
        public string Payload { get; }
        public string Signature { get; }
    }
}
