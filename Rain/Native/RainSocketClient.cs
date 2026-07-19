namespace StockSharp.Rain.Native;

sealed class RainSocketClient : BaseLogReceiver
{
    private readonly record struct SubscriptionKey(RainSocketChannels Channel,
        string Selector);

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

    public RainSocketClient(string endpoint, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "Rain_WebSocket";

    public event Func<RainSocketBook, CancellationToken, ValueTask>
        BookReceived;
    public event Func<RainSocketTrades, CancellationToken, ValueTask>
        TradesReceived;
    public event Func<RainSocketCandle, CancellationToken, ValueTask>
        CandleReceived;
    public event Func<RainSocketProductSummary, CancellationToken, ValueTask>
        ProductSummaryReceived;
    public event Func<RainSocketMarketSummary, CancellationToken, ValueTask>
        MarketSummaryReceived;
    public event Func<RainSocketAccounts, CancellationToken, ValueTask>
        AccountsReceived;
    public event Func<RainSocketOrders, CancellationToken, ValueTask>
        OrdersReceived;
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
                "Rain WebSocket is already initialized.");
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

    public ValueTask SubscribeAsync(RainSocketChannels channel,
        string selector, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(channel, selector, true,
            cancellationToken);

    public ValueTask UnsubscribeAsync(RainSocketChannels channel,
        string selector, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(channel, selector, false,
            cancellationToken);

    public async ValueTask RefreshAsync(RainSocketChannels channel,
        string selector, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client?.IsConnected != true)
            return;
        selector = NormalizeSelector(channel, selector);
        await SendAsync(client, channel, selector, false, cancellationToken);
        await SendAsync(client, channel, selector, true, cancellationToken);
    }

    public async ValueTask PingAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client?.IsConnected != true)
            return;
        await SendCommandAsync(client, new()
        {
            Name = "ping",
            Data = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        }, cancellationToken);
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
                value.Selector, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static value => value.Channel)];
        foreach (var subscription in subscriptions)
            await SendAsync(client, subscription.Channel,
                subscription.Selector, true, cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(
        RainSocketChannels channel, string selector, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        var key = new SubscriptionKey(channel,
            NormalizeSelector(channel, selector));
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
            await SendAsync(client, channel, key.Selector, isSubscribe,
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

    private ValueTask SendAsync(WebSocketClient client,
        RainSocketChannels channel, string selector, bool isSubscribe,
        CancellationToken cancellationToken)
        => SendCommandAsync(client, new()
        {
            Name = channel.ToWire() +
                (isSubscribe ? " subscribe" : " unsubscribe"),
            Data = selector,
        }, cancellationToken);

    private async ValueTask SendCommandAsync(WebSocketClient client,
        RainSocketCommand command, CancellationToken cancellationToken)
    {
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
            var header = Deserialize<RainSocketNameEnvelope>(payload);
            var name = header.Name?.Trim();
            if (name.IsEmpty() || name.EqualsIgnoreCase("pong") ||
                name.EndsWith(" subscribe", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(" unsubscribe", StringComparison.OrdinalIgnoreCase))
                return;
            if (name.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                var error = Deserialize<RainSocketEnvelope<RainSocketError>>(
                    payload).Data;
                throw new InvalidOperationException(
                    "Rain WebSocket rejected a request: " +
                    (error?.Message ?? error?.Reason ?? "unknown error"));
            }

            if (name.EqualsIgnoreCase("orderBook"))
                await RaiseAsync(BookReceived,
                    Deserialize<RainSocketEnvelope<RainSocketBook>>(payload)
                        .Data, cancellationToken);
            else if (name.EqualsIgnoreCase("trades"))
                await RaiseAsync(TradesReceived,
                    Deserialize<RainSocketEnvelope<RainSocketTrades>>(payload)
                        .Data, cancellationToken);
            else if (name.EqualsIgnoreCase("candles"))
                await RaiseAsync(CandleReceived,
                    Deserialize<RainSocketEnvelope<RainSocketCandle>>(payload)
                        .Data, cancellationToken);
            else if (name.EqualsIgnoreCase("productSummary"))
                await RaiseAsync(ProductSummaryReceived,
                    Deserialize<RainSocketEnvelope<RainSocketProductSummary>>(
                        payload).Data, cancellationToken);
            else if (name.EqualsIgnoreCase("marketSummary"))
                await RaiseAsync(MarketSummaryReceived,
                    Deserialize<RainSocketEnvelope<RainSocketMarketSummary>>(
                        payload).Data, cancellationToken);
            else if (name.EqualsIgnoreCase("accountBalance"))
                await RaiseAsync(AccountsReceived,
                    Deserialize<RainSocketEnvelope<RainSocketAccounts>>(payload)
                        .Data, cancellationToken);
            else if (name.EqualsIgnoreCase("orders"))
                await RaiseAsync(OrdersReceived,
                    Deserialize<RainSocketEnvelope<RainSocketOrders>>(payload)
                        .Data, cancellationToken);
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private static async ValueTask RaiseAsync<TPayload>(
        Func<TPayload, CancellationToken, ValueTask> handler,
        TPayload payload, CancellationToken cancellationToken)
        where TPayload : class
    {
        if (handler is not null && payload is not null)
            await handler(payload, cancellationToken);
    }

    private TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
            throw new InvalidDataException(
                "Rain WebSocket returned an empty JSON value.");

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private static string NormalizeSelector(RainSocketChannels channel,
        string selector)
    {
        selector = selector.ThrowIfEmpty(nameof(selector)).Trim();
        if (channel is RainSocketChannels.AccountBalance or
            RainSocketChannels.Orders)
            return selector;
        if (channel == RainSocketChannels.Candles)
        {
            var separator = selector.IndexOf(';');
            if (separator <= 0 || separator == selector.Length - 1)
                throw new ArgumentException(
                    "Rain candle selector must contain symbol and interval.",
                    nameof(selector));
            return selector[..separator].NormalizeSymbol() +
                selector[separator..];
        }
        return selector.NormalizeSymbol();
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Rain WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
