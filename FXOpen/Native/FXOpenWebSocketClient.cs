namespace StockSharp.FXOpen.Native;

readonly record struct FXOpenFeedChannel(string Symbol, int Depth);
readonly record struct FXOpenBarChannel(string Symbol, string Periodicity,
    TickTraderPriceTypes PriceType);

sealed class FXOpenWebSocketClient : BaseLogReceiver
{
    private readonly string _feedEndpoint;
    private readonly string _tradeEndpoint;
    private readonly string _webApiId;
    private readonly string _key;
    private readonly string _secret;
    private readonly string _oneTimePassword;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<FXOpenFeedChannel> _feedChannels = [];
    private readonly HashSet<FXOpenBarChannel> _barChannels = [];
    private readonly SemaphoreSlim _connectionSync = new(1, 1);
    private readonly SemaphoreSlim _feedSendSync = new(1, 1);
    private readonly SemaphoreSlim _tradeSendSync = new(1, 1);
    private DateTime _nextFeedSendTime;
    private DateTime _nextTradeSendTime;
    private WebSocketClient _feed;
    private WebSocketClient _trade;
    private TaskCompletionSource<bool> _feedLogin;
    private TaskCompletionSource<bool> _tradeLogin;
    private TaskCompletionSource<bool> _feedTwoFactor;
    private TaskCompletionSource<bool> _tradeTwoFactor;
    private bool _isReady;
    private bool _feedAvailable;
    private bool _tradeAvailable;
    private bool _failureReported;

    public FXOpenWebSocketClient(string feedEndpoint, string tradeEndpoint, string webApiId,
        SecureString key, SecureString secret, SecureString oneTimePassword,
        WorkingTime workingTime, int reconnectAttempts)
    {
        _feedEndpoint = NormalizeEndpoint(feedEndpoint, "/feed");
        _tradeEndpoint = NormalizeEndpoint(tradeEndpoint, "/trade");
        _webApiId = webApiId.ThrowIfEmpty(nameof(webApiId));
        _key = key?.UnSecure().ThrowIfEmpty(nameof(key));
        _secret = secret?.UnSecure().ThrowIfEmpty(nameof(secret));
        _oneTimePassword = oneTimePassword.IsEmpty() ? null : oneTimePassword.UnSecure();
        WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    private WorkingTime WorkingTime { get; }

    public override string Name => nameof(FXOpen) + "_WebSocket";

    public event Func<TickTraderFeedTick, CancellationToken, ValueTask> FeedReceived;
    public event Func<TickTraderBarUpdateResult, CancellationToken, ValueTask> BarReceived;
    public event Func<TickTraderAccount, CancellationToken, ValueTask> AccountReceived;
    public event Func<TickTraderExecutionReport, CancellationToken, ValueTask> ExecutionReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _isReady = false;
        _feed?.Dispose();
        _trade?.Dispose();
        _connectionSync.Dispose();
        _feedSendSync.Dispose();
        _tradeSendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        await _connectionSync.WaitAsync(cancellationToken);
        try
        {
            if (_feed is not null || _trade is not null)
                throw new InvalidOperationException("FXOpen WebSocket is already initialized.");

            this.AddInfoLog("Connecting FXOpen feed and trade WebSockets.");
            _feed = CreateClient(_feedEndpoint, true);
            _trade = CreateClient(_tradeEndpoint, false);
            await _feed.ConnectAsync(cancellationToken);
            await AuthenticateAsync(_feed, true, cancellationToken);
            using (_sync.EnterScope())
                _feedAvailable = true;
            await _trade.ConnectAsync(cancellationToken);
            await AuthenticateAsync(_trade, false, cancellationToken);
            using (_sync.EnterScope())
            {
                _tradeAvailable = true;
                _failureReported = false;
                _isReady = true;
            }
            this.AddInfoLog("FXOpen feed and trade WebSockets are authenticated.");
        }
        catch
        {
            try
            {
                await DisconnectCoreAsync(CancellationToken.None);
            }
            catch (Exception cleanupError)
            {
                this.AddWarningLog("FXOpen WebSocket cleanup failed: {0}", cleanupError.Message);
            }
            throw;
        }
        finally
        {
            _connectionSync.Release();
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
        {
            _isReady = false;
            _feedAvailable = false;
            _tradeAvailable = false;
            _failureReported = false;
        }
        await DisconnectCoreAsync(cancellationToken);
        this.AddInfoLog("FXOpen WebSockets disconnected.");
    }

    public async ValueTask SubscribeFeedAsync(string symbol, int depth,
        CancellationToken cancellationToken)
    {
        var channel = new FXOpenFeedChannel(symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
            depth.Max(1).Min(100));
        FXOpenFeedChannel? previous = null;
        using (_sync.EnterScope())
        {
            foreach (var item in _feedChannels)
            {
                if (!item.Symbol.EqualsIgnoreCase(channel.Symbol))
                    continue;
                previous = item;
                break;
            }
        }
        if (previous == channel)
            return;
        if (_feed is { IsConnected: true } client)
            await SendFeedSubscriptionAsync(client, channel, true, cancellationToken);
        using (_sync.EnterScope())
        {
            _feedChannels.RemoveWhere(item => item.Symbol.EqualsIgnoreCase(channel.Symbol));
            _feedChannels.Add(channel);
        }
        this.AddDebugLog("FXOpen feed subscribed: {0}, depth {1}.", channel.Symbol,
            channel.Depth);
    }

    public async ValueTask UnsubscribeFeedAsync(string symbol,
        CancellationToken cancellationToken)
    {
        symbol = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
        if (_feed is { IsConnected: true } client)
            await SendFeedUnsubscribeAsync(client, symbol, cancellationToken);
        using (_sync.EnterScope())
            _feedChannels.RemoveWhere(item => item.Symbol.EqualsIgnoreCase(symbol));
        this.AddDebugLog("FXOpen feed unsubscribed: {0}.", symbol);
    }

    public async ValueTask SubscribeBarAsync(string symbol, string periodicity,
        TickTraderPriceTypes priceType, CancellationToken cancellationToken)
    {
        var channel = new FXOpenBarChannel(symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
            periodicity.ThrowIfEmpty(nameof(periodicity)), priceType);
        bool exists;
        using (_sync.EnterScope())
            exists = _barChannels.Contains(channel);
        if (exists)
            return;
        if (_feed is { IsConnected: true } client)
            await SendBarSubscriptionAsync(client, channel, true, cancellationToken);
        using (_sync.EnterScope())
            _barChannels.Add(channel);
        this.AddDebugLog("FXOpen bars subscribed: {0} {1} {2}.", channel.Symbol,
            channel.Periodicity, channel.PriceType);
    }

    public async ValueTask UnsubscribeBarsAsync(string symbol,
        CancellationToken cancellationToken)
    {
        symbol = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
        if (_feed is { IsConnected: true } client)
            await SendBarUnsubscribeAsync(client, symbol, cancellationToken);
        using (_sync.EnterScope())
            _barChannels.RemoveWhere(item => item.Symbol.EqualsIgnoreCase(symbol));
        this.AddDebugLog("FXOpen bars unsubscribed: {0}.", symbol);
    }

    private WebSocketClient CreateClient(string endpoint, bool isFeed)
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            endpoint,
            (state, token) => OnStateChangedAsync(client, isFeed, state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(socket, isFeed, message, token),
            (s, a) => this.AddInfoLog(s, a),
            (s, a) => this.AddErrorLog(s, a),
            (s, a) => this.AddVerboseLog(s, a))
        {
            ReconnectAttempts = _reconnectAttempts,
            WorkingTime = WorkingTime,
            DisableAutoResend = true,
            SendSettings = new()
            {
                NullValueHandling = NullValueHandling.Ignore,
            },
        };
        client.Init += static socket =>
            socket.Options.SetRequestHeader("User-Agent", "StockSharp-FXOpen-Connector/1.0");
        return client;
    }

    private async ValueTask OnStateChangedAsync(WebSocketClient source, bool isFeed,
        ConnectionStates state, CancellationToken cancellationToken)
    {
        bool isReady;
        using (_sync.EnterScope())
            isReady = _isReady;
        if (!isReady || !ReferenceEquals(source, isFeed ? _feed : _trade))
            return;

        if (state == ConnectionStates.Failed)
        {
            bool notify;
            using (_sync.EnterScope())
            {
                if (isFeed)
                    _feedAvailable = false;
                else
                    _tradeAvailable = false;
                notify = !_failureReported;
                _failureReported = true;
            }
            this.AddWarningLog("FXOpen {0} WebSocket connection failed.",
                isFeed ? "feed" : "trade");
            if (notify && StateChanged is { } failedHandler)
                await failedHandler(ConnectionStates.Failed, cancellationToken);
            return;
        }

        if (state != ConnectionStates.Restored)
        {
            using (_sync.EnterScope())
            {
                if (isFeed)
                    _feedAvailable = false;
                else
                    _tradeAvailable = false;
            }
            return;
        }

        try
        {
            await AuthenticateAsync(source, isFeed, cancellationToken);
            if (isFeed)
                await RestoreFeedSubscriptionsAsync(source, cancellationToken);
            bool notify;
            using (_sync.EnterScope())
            {
                var wasAvailable = isFeed ? _feedAvailable : _tradeAvailable;
                if (isFeed)
                    _feedAvailable = true;
                else
                    _tradeAvailable = true;
                notify = _feedAvailable && _tradeAvailable &&
                    (_failureReported || !wasAvailable);
                if (notify)
                    _failureReported = false;
            }
            this.AddInfoLog("FXOpen {0} WebSocket restored and authenticated.",
                isFeed ? "feed" : "trade");
            if (notify && StateChanged is { } restoredHandler)
                await restoredHandler(ConnectionStates.Restored, cancellationToken);
        }
        catch (Exception error)
        {
            bool notify;
            using (_sync.EnterScope())
            {
                if (isFeed)
                    _feedAvailable = false;
                else
                    _tradeAvailable = false;
                notify = !_failureReported;
                _failureReported = true;
            }
            await RaiseErrorAsync(error, cancellationToken);
            if (notify && StateChanged is { } failedHandler)
                await failedHandler(ConnectionStates.Failed, cancellationToken);
        }
    }

    private async ValueTask AuthenticateAsync(WebSocketClient client, bool isFeed,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (_sync.EnterScope())
        {
            if (isFeed)
                _feedLogin = completion;
            else
                _tradeLogin = completion;
        }

        try
        {
            this.AddDebugLog("Authenticating FXOpen {0} WebSocket.",
                isFeed ? "feed" : "trade");
            var timestamp = checked((long)DateTime.UtcNow.ToUnix(false));
            await SendAsync(client, isFeed, new TickTraderWsRequest<TickTraderLoginParameters>
            {
                Id = Guid.NewGuid().ToString("D"),
                Request = "Login",
                Params = new()
                {
                    WebApiId = _webApiId,
                    WebApiKey = _key,
                    Timestamp = timestamp,
                    Signature = CreateSignature(timestamp),
                    AppSessionId = Guid.NewGuid().ToString("D"),
                },
            }, cancellationToken);

            var isTwoFactor = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20),
                cancellationToken);
            if (!isTwoFactor)
                return;
            if (_oneTimePassword.IsEmpty())
                throw new InvalidOperationException(
                    "FXOpen requires a one-time password for this Web API session.");

            var twoFactor = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (_sync.EnterScope())
            {
                if (isFeed)
                    _feedTwoFactor = twoFactor;
                else
                    _tradeTwoFactor = twoFactor;
            }
            await SendAsync(client, isFeed,
                new TickTraderWsRequest<TickTraderTwoFactorParameters>
                {
                    Id = Guid.NewGuid().ToString("D"),
                    Request = "TwoFactor",
                    Params = new() { OneTimePassword = _oneTimePassword },
                }, cancellationToken);
            await twoFactor.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            this.AddDebugLog("FXOpen {0} WebSocket two-factor authentication completed.",
                isFeed ? "feed" : "trade");
        }
        finally
        {
            using (_sync.EnterScope())
            {
                if (isFeed)
                {
                    if (ReferenceEquals(_feedLogin, completion)) _feedLogin = null;
                    _feedTwoFactor = null;
                }
                else
                {
                    if (ReferenceEquals(_tradeLogin, completion)) _tradeLogin = null;
                    _tradeTwoFactor = null;
                }
            }
        }
    }

    private async ValueTask RestoreFeedSubscriptionsAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        FXOpenFeedChannel[] feeds;
        FXOpenBarChannel[] bars;
        using (_sync.EnterScope())
        {
            feeds = [.. _feedChannels];
            bars = [.. _barChannels];
        }
        this.AddInfoLog("Restoring FXOpen subscriptions: {0} feed, {1} bars.",
            feeds.Length, bars.Length);
        foreach (var channel in feeds)
            await SendFeedSubscriptionAsync(client, channel, true, cancellationToken);
        foreach (var channel in bars)
            await SendBarSubscriptionAsync(client, channel, true, cancellationToken);
    }

    private ValueTask SendFeedSubscriptionAsync(WebSocketClient client, FXOpenFeedChannel channel,
        bool isSubscribe, CancellationToken cancellationToken)
        => isSubscribe
            ? SendAsync(client, true, new TickTraderWsRequest<TickTraderFeedSubscribeParameters>
            {
                Id = Guid.NewGuid().ToString("D"),
                Request = "FeedSubscribe",
                Params = new()
                {
                    Subscribe = [new() { Symbol = channel.Symbol, BookDepth = channel.Depth }],
                },
            }, cancellationToken)
            : SendFeedUnsubscribeAsync(client, channel.Symbol, cancellationToken);

    private ValueTask SendFeedUnsubscribeAsync(WebSocketClient client, string symbol,
        CancellationToken cancellationToken)
        => SendAsync(client, true, new TickTraderWsRequest<TickTraderFeedSubscribeParameters>
        {
            Id = Guid.NewGuid().ToString("D"),
            Request = "FeedSubscribe",
            Params = new() { Unsubscribe = [symbol] },
        }, cancellationToken);

    private ValueTask SendBarSubscriptionAsync(WebSocketClient client, FXOpenBarChannel channel,
        bool isSubscribe, CancellationToken cancellationToken)
        => isSubscribe
            ? SendAsync(client, true, new TickTraderWsRequest<TickTraderBarSubscribeParameters>
            {
                Id = Guid.NewGuid().ToString("D"),
                Request = "BarFeedSubscribe",
                Params = new()
                {
                    Subscribe = [new()
                    {
                        Symbol = channel.Symbol,
                        BarParams = [new()
                        {
                            Periodicity = channel.Periodicity,
                            PriceType = channel.PriceType,
                        }],
                    }],
                },
            }, cancellationToken)
            : SendBarUnsubscribeAsync(client, channel.Symbol, cancellationToken);

    private ValueTask SendBarUnsubscribeAsync(WebSocketClient client, string symbol,
        CancellationToken cancellationToken)
        => SendAsync(client, true, new TickTraderWsRequest<TickTraderBarSubscribeParameters>
        {
            Id = Guid.NewGuid().ToString("D"),
            Request = "BarFeedSubscribe",
            Params = new() { Unsubscribe = [symbol] },
        }, cancellationToken);

    private async ValueTask SendAsync<TParams>(WebSocketClient client, bool isFeed,
        TickTraderWsRequest<TParams> request, CancellationToken cancellationToken)
        where TParams : class
    {
        var semaphore = isFeed ? _feedSendSync : _tradeSendSync;
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var next = isFeed ? _nextFeedSendTime : _nextTradeSendTime;
            var delay = next - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            await client.SendAsync(request, cancellationToken);
            if (isFeed)
                _nextFeedSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(75);
            else
                _nextTradeSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(75);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async ValueTask OnProcessAsync(WebSocketClient source, bool isFeed,
        WebSocketMessage message, CancellationToken cancellationToken)
    {
        _ = source;
        var payload = message.AsString();
        if (payload.IsEmpty())
            return;

        try
        {
            var header = Deserialize<TickTraderWsHeader>(payload);
            if (header.Response.EqualsIgnoreCase("Error"))
            {
                var error = new InvalidOperationException(
                    $"FXOpen WebSocket error: {header.Error.IsEmpty("Unknown error")}.");
                TaskCompletionSource<bool> login;
                TaskCompletionSource<bool> twoFactor;
                using (_sync.EnterScope())
                {
                    login = isFeed ? _feedLogin : _tradeLogin;
                    twoFactor = isFeed ? _feedTwoFactor : _tradeTwoFactor;
                }
                if (twoFactor?.TrySetException(error) == true || login?.TrySetException(error) == true)
                    return;
                throw error;
            }

            switch (header.Response?.ToLowerInvariant())
            {
                case "login":
                    {
                        var result = Deserialize<TickTraderWsResponse<TickTraderLoginResult>>(payload).Result
                            ?? throw new InvalidDataException("FXOpen returned no Login result.");
                        TaskCompletionSource<bool> completion;
                        using (_sync.EnterScope())
                            completion = isFeed ? _feedLogin : _tradeLogin;
                        if (result.Info.EqualsIgnoreCase("ok"))
                            completion?.TrySetResult(result.TwoFactorFlag);
                        else
                            completion?.TrySetException(new InvalidOperationException(
                                $"FXOpen authentication failed: {result.Info}."));
                        break;
                    }
                case "twofactor":
                    {
                        var result = Deserialize<TickTraderWsResponse<TickTraderTwoFactorResult>>(payload).Result
                            ?? throw new InvalidDataException("FXOpen returned no TwoFactor result.");
                        TaskCompletionSource<bool> completion;
                        using (_sync.EnterScope())
                            completion = isFeed ? _feedTwoFactor : _tradeTwoFactor;
                        if (result.Info.EqualsIgnoreCase("Success"))
                            completion?.TrySetResult(true);
                        else if (result.Info?.ContainsIgnoreCase("required") == true)
                            this.AddDebugLog("FXOpen {0} WebSocket requested two-factor authentication.",
                                isFeed ? "feed" : "trade");
                        else
                            completion?.TrySetException(new InvalidOperationException(
                                $"FXOpen two-factor authentication failed: {result.Info}."));
                        break;
                    }
                case "feedsubscribe":
                    {
                        var result = Deserialize<TickTraderWsResponse<TickTraderFeedSnapshot>>(payload).Result;
                        if (FeedReceived is { } handler)
                        {
                            foreach (var tick in result?.Snapshot ?? [])
                                await handler(tick, cancellationToken);
                        }
                        break;
                    }
                case "feedtick":
                    if (FeedReceived is { } feedHandler)
                        await feedHandler(Deserialize<TickTraderWsResponse<TickTraderFeedTick>>(payload).Result,
                            cancellationToken);
                    break;
                case "feedbarupdate":
                    if (BarReceived is { } barHandler)
                        await barHandler(Deserialize<TickTraderWsResponse<TickTraderBarUpdateResult>>(payload).Result,
                            cancellationToken);
                    break;
                case "account":
                    if (AccountReceived is { } accountHandler)
                        await accountHandler(Deserialize<TickTraderWsResponse<TickTraderAccount>>(payload).Result,
                            cancellationToken);
                    break;
                case "executionreport":
                    if (ExecutionReceived is { } executionHandler)
                        await executionHandler(
                            Deserialize<TickTraderWsResponse<TickTraderExecutionReport>>(payload).Result,
                            cancellationToken);
                    break;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or
            InvalidOperationException or TimeoutException)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private static T Deserialize<T>(string payload)
        where T : class
        => JsonConvert.DeserializeObject<T>(payload)
            ?? throw new InvalidDataException("FXOpen WebSocket returned an empty JSON value.");

    private string CreateSignature(long timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(_secret));
        var source = timestamp.ToString(CultureInfo.InvariantCulture) + _webApiId + _key;
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(source)));
    }

    private async ValueTask DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        var feed = _feed;
        var trade = _trade;
        _feed = null;
        _trade = null;
        Exception failure = null;
        foreach (var client in new[] { feed, trade })
        {
            if (client is null)
                continue;
            try
            {
                if (client.IsConnected)
                    await client.DisconnectAsync(cancellationToken);
            }
            catch (Exception error)
            {
                failure ??= error;
            }
            finally
            {
                client.Dispose();
            }
        }
        if (failure is not null)
            throw failure;
    }

    private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
    {
        this.AddErrorLog(error);
        if (Error is { } handler)
            await handler(error, cancellationToken);
    }

    private static string NormalizeEndpoint(string endpoint, string path)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"wss://{endpoint.TrimStart('/')}";
        var uri = new Uri(endpoint, UriKind.Absolute);
        if (uri.AbsolutePath.IsEmpty() || uri.AbsolutePath == "/")
            endpoint = endpoint.TrimEnd('/') + path;
        return endpoint;
    }
}
