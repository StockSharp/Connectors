namespace StockSharp.Korbit.Native;

sealed class KorbitSocketClient : BaseLogReceiver
{
    private const int _maximumMessageSize = 8 * 1024 * 1024;

    private readonly record struct SubscriptionKey(
        KorbitSocketChannels Channel, string Symbol, string Level);

    private readonly Uri _endpoint;
    private readonly KorbitRestClient _restClient;
    private readonly bool _isPrivate;
    private readonly string[] _privateSymbols;
    private readonly int _accountSequence;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<SubscriptionKey> _subscriptions = [];
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _initialConnection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private ClientWebSocket _socket;
    private Task _runTask;
    private int _requestId;
    private bool _isDisconnecting;

    public KorbitSocketClient(string endpoint, KorbitRestClient restClient,
        bool isPrivate, IEnumerable<string> privateSymbols,
        int accountSequence, int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _restClient = restClient ?? throw new ArgumentNullException(
            nameof(restClient));
        _isPrivate = isPrivate;
        _privateSymbols = [.. (privateSymbols ?? []).Where(static value =>
            !value.IsEmpty()).Select(static value => value.NormalizeSymbol())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        _accountSequence = accountSequence.Max(1);
        _reconnectAttempts = reconnectAttempts < 0
            ? int.MaxValue
            : reconnectAttempts.Max(1);
        if (isPrivate && !restClient.IsCredentialsAvailable)
            throw new ArgumentException(
                "Private Korbit WebSocket requires credentials.",
                nameof(restClient));
        if (isPrivate && _privateSymbols.Length == 0)
            throw new ArgumentException(
                "Private Korbit WebSocket requires trading-pair symbols.",
                nameof(privateSymbols));
    }

    public override string Name
        => _isPrivate ? "Korbit_PrivateWebSocket" : "Korbit_PublicWebSocket";

    public event Func<KorbitSocketBookMessage, CancellationToken, ValueTask>
        BookReceived;
    public event Func<KorbitSocketTickerMessage, CancellationToken, ValueTask>
        TickerReceived;
    public event Func<KorbitSocketTradeMessage, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<KorbitSocketOrderMessage, CancellationToken, ValueTask>
        OrderReceived;
    public event Func<KorbitSocketAccountTradeMessage, CancellationToken,
        ValueTask> AccountTradeReceived;
    public event Func<KorbitSocketAssetMessage, CancellationToken, ValueTask>
        AssetReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _isDisconnecting = true;
        _lifetime.Cancel();
        _socket?.Dispose();
        _sendSync.Dispose();
        _lifetime.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
            throw new InvalidOperationException(
                "Korbit WebSocket is already initialized.");
        _runTask = RunAsync(_lifetime.Token);
        await _initialConnection.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        _isDisconnecting = true;
        _lifetime.Cancel();
        var socket = _socket;
        if (socket?.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure, "Client disconnect",
                    cancellationToken);
            }
            catch (WebSocketException)
            {
            }
        }
        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (
                _lifetime.IsCancellationRequested)
            {
            }
        }
    }

    public ValueTask SubscribeTickerAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(KorbitSocketChannels.Ticker, symbol, null,
            true, cancellationToken);

    public ValueTask UnsubscribeTickerAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(KorbitSocketChannels.Ticker, symbol, null,
            false, cancellationToken);

    public ValueTask SubscribeOrderBookAsync(string symbol, string level,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(KorbitSocketChannels.OrderBook, symbol,
            level, true, cancellationToken);

    public ValueTask UnsubscribeOrderBookAsync(string symbol, string level,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(KorbitSocketChannels.OrderBook, symbol,
            level, false, cancellationToken);

    public ValueTask SubscribeTradesAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(KorbitSocketChannels.Trade, symbol, null,
            true, cancellationToken);

    public ValueTask UnsubscribeTradesAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(KorbitSocketChannels.Trade, symbol, null,
            false, cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        var wasConnected = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RaiseStateAsync(ConnectionStates.Connecting,
                        cancellationToken);
                    using var socket = CreateSocket(out var uri);
                    _socket = socket;
                    await socket.ConnectAsync(uri, cancellationToken);
                    await RestoreSubscriptionsAsync(socket, cancellationToken);
                    failures = 0;
                    var restored = wasConnected;
                    wasConnected = true;
                    _initialConnection.TrySetResult();
                    await RaiseStateAsync(restored
                        ? ConnectionStates.Restored
                        : ConnectionStates.Connected, cancellationToken);
                    await ReceiveLoopAsync(socket, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                        throw new WebSocketException(
                            "Korbit WebSocket closed unexpectedly.");
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    failures++;
                    await RaiseErrorAsync(error, cancellationToken);
                    await RaiseStateAsync(ConnectionStates.Disconnected,
                        cancellationToken);
                    if (_isPrivate)
                    {
                        try
                        {
                            await _restClient.SynchronizeTimeAsync(
                                cancellationToken);
                        }
                        catch (Exception synchronizationError)
                        {
                            await RaiseErrorAsync(synchronizationError,
                                cancellationToken);
                        }
                    }
                    if (failures > _reconnectAttempts)
                    {
                        _initialConnection.TrySetException(error);
                        await RaiseStateAsync(ConnectionStates.Failed,
                            cancellationToken);
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(
                        Math.Min(30, 1 << Math.Min(failures, 5))),
                        cancellationToken);
                }
                finally
                {
                    _socket = null;
                }
            }
        }
        finally
        {
            if (!_initialConnection.Task.IsCompleted)
            {
                if (_isDisconnecting)
                    _initialConnection.TrySetCanceled(cancellationToken);
                else
                    _initialConnection.TrySetException(
                        new WebSocketException(
                            "Korbit WebSocket could not connect."));
            }
            await RaiseStateAsync(ConnectionStates.Disconnected,
                CancellationToken.None);
        }
    }

    private ClientWebSocket CreateSocket(out Uri uri)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(5);
        socket.Options.SetRequestHeader("User-Agent",
            "StockSharp-Korbit-Connector/1.0");
        if (!_isPrivate)
        {
            uri = _endpoint;
            return socket;
        }

        var authentication = _restClient.CreateWebSocketAuthentication();
        socket.Options.SetRequestHeader("X-KAPI-KEY", authentication.ApiKey);
        uri = new UriBuilder(_endpoint) { Query = authentication.Query }.Uri;
        return socket;
    }

    private async ValueTask RestoreSubscriptionsAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        if (_isPrivate)
        {
            var accountSequences = new[] { _accountSequence };
            await SendAsync(socket,
            [
                CreateSubscription(KorbitSocketChannels.MyOrder,
                    KorbitSocketMethods.Subscribe, _privateSymbols, null,
                    accountSequences),
                CreateSubscription(KorbitSocketChannels.MyTrade,
                    KorbitSocketMethods.Subscribe, _privateSymbols, null,
                    accountSequences),
                CreateSubscription(KorbitSocketChannels.MyAsset,
                    KorbitSocketMethods.Subscribe, null, null,
                    accountSequences),
            ], cancellationToken);
            return;
        }

        SubscriptionKey[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _subscriptions];
        if (subscriptions.Length == 0)
            return;
        await SendAsync(socket, [.. subscriptions.Select(subscription =>
            CreateSubscription(subscription.Channel,
                KorbitSocketMethods.Subscribe, [subscription.Symbol],
                subscription.Level, null))], cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(
        KorbitSocketChannels channel, string symbol, string level,
        bool isSubscribe, CancellationToken cancellationToken)
    {
        if (_isPrivate)
            throw new InvalidOperationException(
                "Public subscriptions cannot use the private Korbit socket.");
        var key = new SubscriptionKey(channel, symbol.NormalizeSymbol(), level);
        using (_sync.EnterScope())
            if (isSubscribe
                ? !_subscriptions.Add(key)
                : !_subscriptions.Remove(key))
                return;

        var socket = _socket;
        if (socket?.State != WebSocketState.Open)
            return;
        try
        {
            await SendAsync(socket,
            [
                CreateSubscription(channel, isSubscribe
                    ? KorbitSocketMethods.Subscribe
                    : KorbitSocketMethods.Unsubscribe, [key.Symbol], level,
                    null),
            ], cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
            {
                if (isSubscribe)
                    _subscriptions.Remove(key);
                else
                    _subscriptions.Add(key);
            }
            throw;
        }
    }

    private KorbitSocketSubscriptionRequest CreateSubscription(
        KorbitSocketChannels channel, KorbitSocketMethods method,
        string[] symbols, string level, int[] accountSequences)
        => new()
        {
            RequestId = Interlocked.Increment(ref _requestId),
            Method = method,
            Channel = channel,
            Symbols = symbols,
            Level = level,
            AccountSequences = accountSequences,
        };

    private async ValueTask SendAsync(ClientWebSocket socket,
        KorbitSocketSubscriptionRequest[] payload,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(payload, _jsonSettings));
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true,
                cancellationToken);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private async ValueTask ReceiveLoopAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open &&
            !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (message.Length + result.Count > _maximumMessageSize)
                    throw new InvalidDataException(
                        "Korbit WebSocket message exceeds the size limit.");
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            await ProcessAsync(Encoding.UTF8.GetString(message.GetBuffer(), 0,
                (int)message.Length), cancellationToken);
        }
    }

    private async ValueTask ProcessAsync(string payload,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty())
            return;
        try
        {
            var header = Deserialize<KorbitSocketHeader>(payload);
            if (!header.Status.IsEmpty())
            {
                if (!header.Status.EqualsIgnoreCase("success"))
                    throw new InvalidOperationException(
                        $"Korbit WebSocket error {header.Code}: {header.Message}");
                return;
            }

            var channel = header.ChannelType ?? header.Type;
            switch (channel)
            {
                case KorbitSocketChannels.Ticker:
                    await RaiseAsync(Deserialize<KorbitSocketTickerMessage>(
                        payload), TickerReceived, cancellationToken);
                    break;
                case KorbitSocketChannels.OrderBook:
                    await RaiseAsync(Deserialize<KorbitSocketBookMessage>(
                        payload), BookReceived, cancellationToken);
                    break;
                case KorbitSocketChannels.Trade:
                    await RaiseAsync(Deserialize<KorbitSocketTradeMessage>(
                        payload), TradeReceived, cancellationToken);
                    break;
                case KorbitSocketChannels.MyOrder:
                    await RaiseAsync(Deserialize<KorbitSocketOrderMessage>(
                        payload), OrderReceived, cancellationToken);
                    break;
                case KorbitSocketChannels.MyTrade:
                    await RaiseAsync(
                        Deserialize<KorbitSocketAccountTradeMessage>(payload),
                        AccountTradeReceived, cancellationToken);
                    break;
                case KorbitSocketChannels.MyAsset:
                    await RaiseAsync(Deserialize<KorbitSocketAssetMessage>(
                        payload), AssetReceived, cancellationToken);
                    break;
            }
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
            throw new InvalidDataException(
                "Korbit WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private ValueTask RaiseStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
        => StateChanged is { } handler
            ? handler(state, cancellationToken)
            : default;

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Korbit WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
