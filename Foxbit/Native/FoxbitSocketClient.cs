namespace StockSharp.Foxbit.Native;

sealed class FoxbitSocketClient : BaseLogReceiver
{
    private readonly record struct SubscriptionKey(
        FoxbitSocketChannels Channel, string MarketSymbol);

    private readonly string _endpoint;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<SubscriptionKey> _subscriptions = [];
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private WebSocketClient _client;

    public FoxbitSocketClient(string endpoint, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "Foxbit_WebSocket";

    public event Func<string, FoxbitSocketTicker, CancellationToken, ValueTask>
        TickerReceived;
    public event Func<string, FoxbitSocketTrade[], CancellationToken, ValueTask>
        TradesReceived;
    public event Func<string, FoxbitSocketBookSnapshot, CancellationToken,
        ValueTask> BookSnapshotReceived;
    public event Func<string, FoxbitSocketBookUpdate, CancellationToken,
        ValueTask> BookUpdateReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _client?.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "Foxbit WebSocket is already initialized.");
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            await RestoreAsync(client, cancellationToken);
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeAsync(FoxbitSocketChannels channel,
        string marketSymbol, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(channel, marketSymbol, true,
            cancellationToken);

    public ValueTask UnsubscribeAsync(FoxbitSocketChannels channel,
        string marketSymbol, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(channel, marketSymbol, false,
            cancellationToken);

    public async ValueTask RefreshOrderBookAsync(string marketSymbol,
        CancellationToken cancellationToken)
    {
        var client = _client;
        if (client?.IsConnected != true)
            return;
        await SendAsync(client, FoxbitSocketMessageTypes.Subscribe,
            FoxbitSocketChannels.OrderBook100, marketSymbol.NormalizeMarket(),
            true, cancellationToken);
    }

    public async ValueTask PingAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client?.IsConnected != true)
            return;
        await SendAsync(client, FoxbitSocketMessageTypes.Message,
            FoxbitSocketChannels.Ping, null, null, cancellationToken);
    }

    private WebSocketClient CreateClient()
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            _endpoint,
            (state, token) => OnStateChangedAsync(client, state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(socket, message, token),
            (s, a) => this.AddInfoLog(s, a),
            (s, a) => this.AddErrorLog(s, a),
            (s, a) => this.AddVerboseLog(s, a))
        {
            ReconnectAttempts = _reconnectAttempts,
            WorkingTime = _workingTime,
            DisableAutoResend = true,
            Indent = false,
            SendSettings = _jsonSettings,
        };
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        if (client is null)
            return;
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(cancellationToken);
        }
        finally
        {
            client.Dispose();
        }
    }

    private async ValueTask OnStateChangedAsync(WebSocketClient client,
        ConnectionStates state, CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Restored)
            await RestoreAsync(client, cancellationToken);
        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask RestoreAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        SubscriptionKey[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _subscriptions.OrderBy(static value =>
                value.MarketSymbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static value => value.Channel)];
        foreach (var subscription in subscriptions)
            await SendAsync(client, FoxbitSocketMessageTypes.Subscribe,
                subscription.Channel, subscription.MarketSymbol,
                subscription.Channel == FoxbitSocketChannels.OrderBook100,
                cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(
        FoxbitSocketChannels channel, string marketSymbol, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (channel is FoxbitSocketChannels.Ping or
            FoxbitSocketChannels.Candles60)
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "The channel is not a supported market-data subscription.");
        var key = new SubscriptionKey(channel, marketSymbol.NormalizeMarket());
        using (_sync.EnterScope())
        {
            var changed = isSubscribe
                ? _subscriptions.Add(key)
                : _subscriptions.Remove(key);
            if (!changed)
                return;
        }
        var client = _client;
        if (client?.IsConnected != true)
            return;
        try
        {
            await SendAsync(client, isSubscribe
                ? FoxbitSocketMessageTypes.Subscribe
                : FoxbitSocketMessageTypes.Unsubscribe, channel,
                key.MarketSymbol,
                isSubscribe && channel == FoxbitSocketChannels.OrderBook100,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                if (isSubscribe)
                    _subscriptions.Remove(key);
                else
                    _subscriptions.Add(key);
            throw;
        }
    }

    private async ValueTask SendAsync(WebSocketClient client,
        FoxbitSocketMessageTypes type, FoxbitSocketChannels channel,
        string marketSymbol, bool? isSnapshot,
        CancellationToken cancellationToken)
    {
        var command = new FoxbitSocketCommand
        {
            Type = type,
            Parameters =
            [
                new()
                {
                    Channel = channel,
                    MarketSymbol = marketSymbol,
                    IsSnapshot = isSnapshot,
                },
            ],
        };
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            await client.SendAsync(command, cancellationToken);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private async ValueTask OnProcessAsync(WebSocketClient client,
        WebSocketMessage message, CancellationToken cancellationToken)
    {
        _ = client;
        var payload = message.AsString();
        if (payload.IsEmpty())
            return;
        try
        {
            var header = Deserialize<FoxbitSocketHeader>(payload);
            if (header.Event == FoxbitSocketEvents.Error)
                throw new InvalidOperationException(
                    "Foxbit WebSocket rejected a request" +
                    (header.Parameters is null
                        ? string.Empty
                        : $" for {header.Parameters.Channel}") +
                    $": {header.Message ?? "unknown error"}");
            if (header.Event is FoxbitSocketEvents.Success or null ||
                header.Parameters is null)
                return;

            var marketSymbol = header.Parameters.MarketSymbol;
            switch (header.Parameters.Channel)
            {
                case FoxbitSocketChannels.Trades
                    when header.Event == FoxbitSocketEvents.Update:
                    var trades = Deserialize<FoxbitSocketEnvelope<
                        FoxbitSocketTrade[]>>(payload);
                    if (TradesReceived is { } tradesHandler)
                        await tradesHandler(marketSymbol, trades.Data ?? [],
                            cancellationToken);
                    break;
                case FoxbitSocketChannels.Ticker
                    when header.Event == FoxbitSocketEvents.Update:
                    var ticker = Deserialize<FoxbitSocketEnvelope<
                        FoxbitSocketTicker>>(payload);
                    if (TickerReceived is { } tickerHandler &&
                        ticker.Data is not null)
                        await tickerHandler(marketSymbol, ticker.Data,
                            cancellationToken);
                    break;
                case FoxbitSocketChannels.OrderBook100
                    when header.Event == FoxbitSocketEvents.Snapshot:
                    var snapshot = Deserialize<FoxbitSocketEnvelope<
                        FoxbitSocketBookSnapshot>>(payload);
                    if (BookSnapshotReceived is { } snapshotHandler &&
                        snapshot.Data is not null)
                        await snapshotHandler(marketSymbol, snapshot.Data,
                            cancellationToken);
                    break;
                case FoxbitSocketChannels.OrderBook100
                    when header.Event == FoxbitSocketEvents.Update:
                    var update = Deserialize<FoxbitSocketEnvelope<
                        FoxbitSocketBookUpdate>>(payload);
                    if (BookUpdateReceived is { } updateHandler &&
                        update.Data is not null)
                        await updateHandler(marketSymbol, update.Data,
                            cancellationToken);
                    break;
                case FoxbitSocketChannels.Ping:
                    _ = Deserialize<FoxbitSocketEnvelope<FoxbitSocketPong>>(
                        payload);
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
                "Foxbit WebSocket returned an empty JSON value.");

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Foxbit WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
