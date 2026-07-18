namespace StockSharp.WhiteBit.Native;

sealed class WhiteBitWsClient : BaseLogReceiver
{
    private readonly WebSocketClient _client;
    private readonly bool _isPrivate;
    private readonly Func<CancellationToken, ValueTask<string>> _tokenProvider;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _restoreSync = new(1, 1);
    private readonly HashSet<string> _markets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _trades = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _depths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Symbol, long Interval)> _candles = [];
    private readonly HashSet<string> _privateSymbols = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<WhiteBitWsStatusEnvelope> _authentication;
    private long _authorizationId;
    private long _nextRequestId;
    private bool _isReady;

    public WhiteBitWsClient(string endpoint, bool isPrivate,
        Func<CancellationToken, ValueTask<string>> tokenProvider, WorkingTime workingTime)
    {
        _isPrivate = isPrivate;
        _tokenProvider = tokenProvider;
        if (isPrivate && tokenProvider is null)
            throw new ArgumentNullException(nameof(tokenProvider));

        _client = new WebSocketClient(
            NormalizeEndpoint(endpoint),
            OnStateChangedAsync,
            (error, token) => RaiseErrorAsync(error, token),
            OnProcessAsync,
            (s, a) => this.AddInfoLog(s, a),
            (s, a) => this.AddErrorLog(s, a),
            (s, a) => this.AddVerboseLog(s, a))
        {
            ReconnectAttempts = 5,
            WorkingTime = workingTime,
            SendSettings = new()
            {
                NullValueHandling = NullValueHandling.Ignore,
            },
        };
    }

    public override string Name => nameof(WhiteBit) + "_" + (_isPrivate ? "UserWs" : "MarketWs");

    public event Func<WhiteBitMarketUpdateParams, CancellationToken, ValueTask> MarketReceived;
    public event Func<WhiteBitDepthUpdateParams, CancellationToken, ValueTask> DepthReceived;
    public event Func<WhiteBitTradesUpdateParams, CancellationToken, ValueTask> TradesReceived;
    public event Func<WhiteBitCandleUpdateParams, CancellationToken, ValueTask> CandleReceived;
    public event Func<WhiteBitSpotBalance[], CancellationToken, ValueTask> SpotBalanceReceived;
    public event Func<WhiteBitMarginBalanceUpdate[], CancellationToken, ValueTask> MarginBalanceReceived;
    public event Func<int, WhiteBitOrder, CancellationToken, ValueTask> PendingOrderReceived;
    public event Func<WhiteBitOrder, CancellationToken, ValueTask> ExecutedOrderReceived;
    public event Func<WhiteBitUserTrade, CancellationToken, ValueTask> UserTradeReceived;
    public event Func<WhiteBitPosition[], CancellationToken, ValueTask> PositionReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _restoreSync.Dispose();
        _client.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken);
        await RestoreSessionAsync(cancellationToken);
        _isReady = true;
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        _isReady = false;
        await _client.DisconnectAsync(cancellationToken);
    }

    public ValueTask PingAsync(CancellationToken cancellationToken)
        => SendAsync("ping", WhiteBitEmptyWsParams.Instance, cancellationToken);

    public void SetPrivateSymbols(IEnumerable<string> symbols)
    {
        if (!_isPrivate)
            throw new InvalidOperationException("Private symbols can only be configured for the user stream.");
        using (_sync.EnterScope())
        {
            _privateSymbols.Clear();
            foreach (var symbol in symbols.Where(static value => !value.IsEmpty()))
                _privateSymbols.Add(symbol);
        }
    }

    public ValueTask SetMarketsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        string[] current;
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var symbol in symbols.Where(static value => !value.IsEmpty()))
                _markets.Add(symbol);
            current = [.. _markets];
        }
        return SendReplacementAsync("market", current, cancellationToken);
    }

    public ValueTask SetTradesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        string[] current;
        using (_sync.EnterScope())
        {
            _trades.Clear();
            foreach (var symbol in symbols.Where(static value => !value.IsEmpty()))
                _trades.Add(symbol);
            current = [.. _trades];
        }
        return SendReplacementAsync("trades", current, cancellationToken);
    }

    public ValueTask SetDepthAsync(string symbol, int depth, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
        {
            if (isSubscribe)
                _depths[symbol] = depth;
            else
                _depths.Remove(symbol);
        }

        return isSubscribe
            ? SendAsync("depth_subscribe", new WhiteBitDepthWsParams
            {
                Symbol = symbol,
                Limit = depth,
            }, cancellationToken)
            : SendAsync("depth_unsubscribe", new WhiteBitStringWsParams
            {
                Values = [symbol],
            }, cancellationToken);
    }

    public async ValueTask SetCandlesAsync(IEnumerable<(string Symbol, TimeSpan TimeFrame)> subscriptions,
        CancellationToken cancellationToken)
    {
        (string Symbol, long Interval)[] current;
        using (_sync.EnterScope())
        {
            _candles.Clear();
            foreach (var item in subscriptions)
                _candles.Add((item.Symbol, item.TimeFrame.ToNativeSeconds()));
            current = [.. _candles];
        }

        await SendAsync("candles_unsubscribe", WhiteBitEmptyWsParams.Instance, cancellationToken);
        foreach (var item in current)
            await SendCandleSubscriptionAsync(item.Symbol, item.Interval, cancellationToken);
    }

    private async ValueTask OnStateChangedAsync(ConnectionStates state, CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Connected && _isReady)
        {
            try
            {
                await RestoreSessionAsync(cancellationToken);
            }
            catch (Exception error)
            {
                await RaiseErrorAsync(error, cancellationToken);
            }
        }

        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask RestoreSessionAsync(CancellationToken cancellationToken)
    {
        await _restoreSync.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            if (_isPrivate)
            {
                await AuthenticateAsync(cancellationToken);
                await SubscribePrivateAsync(cancellationToken);
                return;
            }

            string[] markets;
            string[] trades;
            KeyValuePair<string, int>[] depths;
            (string Symbol, long Interval)[] candles;
            using (_sync.EnterScope())
            {
                markets = [.. _markets];
                trades = [.. _trades];
                depths = [.. _depths];
                candles = [.. _candles];
            }

            if (markets.Length > 0)
                await SendAsync("market_subscribe", new WhiteBitStringWsParams { Values = markets }, cancellationToken);
            if (trades.Length > 0)
                await SendAsync("trades_subscribe", new WhiteBitStringWsParams { Values = trades }, cancellationToken);
            foreach (var depth in depths)
            {
                await SendAsync("depth_subscribe", new WhiteBitDepthWsParams
                {
                    Symbol = depth.Key,
                    Limit = depth.Value,
                }, cancellationToken);
            }
            foreach (var candle in candles)
                await SendCandleSubscriptionAsync(candle.Symbol, candle.Interval, cancellationToken);
        }
        finally
        {
            _restoreSync.Release();
        }
    }

    private async ValueTask AuthenticateAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider(cancellationToken);
        if (token.IsEmpty())
            throw new InvalidDataException("WhiteBIT returned an empty WebSocket token.");

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<WhiteBitWsStatusEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (_sync.EnterScope())
        {
            _authorizationId = id;
            _authentication = completion;
        }

        try
        {
            await _client.SendAsync(new WhiteBitWsRequest<WhiteBitAuthorizeWsParams>
            {
                Id = id,
                Method = "authorize",
                Parameters = new() { Token = token },
            }, cancellationToken, id);
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            using (_sync.EnterScope())
            {
                if (ReferenceEquals(_authentication, completion))
                    _authentication = null;
            }
        }
    }

    private async ValueTask SubscribePrivateAsync(CancellationToken cancellationToken)
    {
        string[] symbols;
        using (_sync.EnterScope())
            symbols = [.. _privateSymbols];

        await SendAsync("balanceSpot_subscribe", WhiteBitEmptyWsParams.Instance, cancellationToken);
        await SendAsync("balanceMargin_subscribe", WhiteBitEmptyWsParams.Instance, cancellationToken);
        if (symbols.Length > 0)
            await SendAsync("ordersPending_subscribe", new WhiteBitStringWsParams { Values = symbols }, cancellationToken);
        await SendAsync("ordersExecuted_subscribe", new WhiteBitExecutedSubscribeWsParams { Symbols = symbols }, cancellationToken);
        await SendAsync("deals_subscribe", new WhiteBitDealsSubscribeWsParams(), cancellationToken);
        await SendAsync("positionsMargin_subscribe", WhiteBitEmptyWsParams.Instance, cancellationToken);
    }

    private ValueTask SendReplacementAsync(string channel, string[] symbols,
        CancellationToken cancellationToken)
        => symbols.Length == 0
            ? SendAsync(channel + "_unsubscribe", WhiteBitEmptyWsParams.Instance, cancellationToken)
            : SendAsync(channel + "_subscribe", new WhiteBitStringWsParams { Values = symbols }, cancellationToken);

    private ValueTask SendCandleSubscriptionAsync(string symbol, long interval,
        CancellationToken cancellationToken)
        => SendAsync("candles_subscribe", new WhiteBitCandleWsParams
        {
            Symbol = symbol,
            Interval = interval,
        }, cancellationToken);

    private ValueTask SendAsync<TParams>(string method, TParams parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        return _client.SendAsync(new WhiteBitWsRequest<TParams>
        {
            Id = id,
            Method = method,
            Parameters = parameters,
        }, cancellationToken, id);
    }

    private async ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
    {
        var payload = message.AsString();
        if (payload.IsEmpty())
            return;

        try
        {
            var header = Deserialize<WhiteBitWsHeader>(payload);
            TaskCompletionSource<WhiteBitWsStatusEnvelope> authentication;
            long authorizationId;
            using (_sync.EnterScope())
            {
                authentication = _authentication;
                authorizationId = _authorizationId;
            }

            if (header.Error is { } error)
            {
                var exception = new InvalidOperationException($"WhiteBIT WebSocket error {error.Code}: {error.Message}");
                if (authentication is not null && header.Id == authorizationId)
                {
                    authentication.TrySetException(exception);
                    return;
                }
                throw exception;
            }

            if (authentication is not null && header.Id == authorizationId)
            {
                var response = Deserialize<WhiteBitWsStatusEnvelope>(payload);
                if (response.Result?.Status.EqualsIgnoreCase("success") == true)
                    authentication.TrySetResult(response);
                else
                    authentication.TrySetException(new InvalidOperationException("WhiteBIT WebSocket authorization failed."));
                return;
            }

            switch (header.Method?.ToLowerInvariant())
            {
                case "market_update":
                    if (MarketReceived is { } marketHandler)
                        await marketHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitMarketUpdateParams>>(payload).Parameters, cancellationToken);
                    break;

                case "depth_update":
                    if (DepthReceived is { } depthHandler)
                        await depthHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitDepthUpdateParams>>(payload).Parameters, cancellationToken);
                    break;

                case "trades_update":
                    if (TradesReceived is { } tradesHandler)
                        await tradesHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitTradesUpdateParams>>(payload).Parameters, cancellationToken);
                    break;

                case "candles_update":
                    if (CandleReceived is { } candleHandler)
                        await candleHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitCandleUpdateParams>>(payload).Parameters, cancellationToken);
                    break;

                case "balancespot_update":
                    if (SpotBalanceReceived is { } spotBalanceHandler)
                        await spotBalanceHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitSpotBalanceUpdateParams>>(payload).Parameters.Balances, cancellationToken);
                    break;

                case "balancemargin_update":
                    if (MarginBalanceReceived is { } marginBalanceHandler)
                        await marginBalanceHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitMarginBalanceUpdate[]>>(payload).Parameters ?? [], cancellationToken);
                    break;

                case "orderspending_update":
                    if (PendingOrderReceived is { } pendingOrderHandler)
                    {
                        var update = Deserialize<WhiteBitWsEnvelope<WhiteBitPendingOrderUpdateParams>>(payload).Parameters;
                        await pendingOrderHandler(update.EventId, update.Order, cancellationToken);
                    }
                    break;

                case "ordersexecuted_update":
                    if (ExecutedOrderReceived is { } executedOrderHandler)
                    {
                        foreach (var order in Deserialize<WhiteBitWsEnvelope<WhiteBitOrder[]>>(payload).Parameters ?? [])
                            await executedOrderHandler(order, cancellationToken);
                    }
                    break;

                case "deals_update":
                    if (UserTradeReceived is { } userTradeHandler)
                        await userTradeHandler(Deserialize<WhiteBitWsEnvelope<WhiteBitDealUpdateParams>>(payload).Parameters.Trade, cancellationToken);
                    break;

                case "positionsmargin_update":
                    if (PositionReceived is { } positionHandler)
                    {
                        var positions = Deserialize<WhiteBitWsEnvelope<WhiteBitPositionCollection>>(payload).Parameters;
                        await positionHandler(positions?.Records ?? [], cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private static T Deserialize<T>(string payload)
        where T : class
        => JsonConvert.DeserializeObject<T>(payload)
            ?? throw new InvalidDataException("WhiteBIT WebSocket returned an empty JSON value.");

    private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
    {
        this.AddErrorLog(error);
        if (Error is { } handler)
            await handler(error, cancellationToken);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"wss://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }
}
