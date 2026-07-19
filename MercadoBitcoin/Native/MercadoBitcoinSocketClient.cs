namespace StockSharp.MercadoBitcoin.Native;

sealed class MercadoBitcoinSocketClient : BaseLogReceiver
{
    private readonly record struct SubscriptionKey(
        MercadoBitcoinSocketTopics Topic, string Id);

    private readonly string _endpoint;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<SubscriptionKey> _subscriptions = [];
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private WebSocketClient _client;
    private CancellationTokenSource _heartbeatCancellation;
    private Task _heartbeatTask;

    public MercadoBitcoinSocketClient(string endpoint, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "MercadoBitcoin_WebSocket";

    public event Func<MercadoBitcoinSocketTicker, CancellationToken, ValueTask>
        TickerReceived;
    public event Func<MercadoBitcoinSocketOrderBook, CancellationToken, ValueTask>
        OrderBookReceived;
    public event Func<MercadoBitcoinSocketTrade, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _heartbeatCancellation?.Cancel();
        _heartbeatCancellation?.Dispose();
        _client?.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "Mercado Bitcoin WebSocket is already initialized.");
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            await SendPingAsync(client, cancellationToken);
            StartHeartbeat();
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeTickerAsync(string id,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(MercadoBitcoinSocketTopics.Ticker, id, true,
            cancellationToken);

    public ValueTask UnsubscribeTickerAsync(string id,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(MercadoBitcoinSocketTopics.Ticker, id, false,
            cancellationToken);

    public ValueTask SubscribeOrderBookAsync(string id,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(MercadoBitcoinSocketTopics.OrderBook, id, true,
            cancellationToken);

    public ValueTask UnsubscribeOrderBookAsync(string id,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(MercadoBitcoinSocketTopics.OrderBook, id, false,
            cancellationToken);

    public ValueTask SubscribeTradesAsync(string id,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(MercadoBitcoinSocketTopics.Trade, id, true,
            cancellationToken);

    public ValueTask UnsubscribeTradesAsync(string id,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(MercadoBitcoinSocketTopics.Trade, id, false,
            cancellationToken);

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
        client.Init += socket =>
        {
            socket.Options.SetRequestHeader("User-Agent",
                "StockSharp-MercadoBitcoin-Connector/1.0");
            socket.Options.SetRequestHeader("Origin",
                "https://www.mercadobitcoin.com.br");
        };
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        await StopHeartbeatAsync();
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
        {
            await SendPingAsync(client, cancellationToken);
            SubscriptionKey[] subscriptions;
            using (_sync.EnterScope())
                subscriptions = [.. _subscriptions];
            foreach (var subscription in subscriptions)
                await SendSubscriptionAsync(client, subscription, true,
                    cancellationToken);
        }

        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(
        MercadoBitcoinSocketTopics topic, string id, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        var key = new SubscriptionKey(topic,
            id.ThrowIfEmpty(nameof(id)).Trim().ToUpperInvariant());
        using (_sync.EnterScope())
            if (isSubscribe ? !_subscriptions.Add(key) :
                !_subscriptions.Remove(key))
                return;

        if (_client?.IsConnected != true)
            return;
        try
        {
            await SendSubscriptionAsync(_client, key, isSubscribe,
                cancellationToken);
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

    private ValueTask SendSubscriptionAsync(WebSocketClient client,
        SubscriptionKey subscription, bool isSubscribe,
        CancellationToken cancellationToken)
        => SendAsync(client, new MercadoBitcoinSocketSubscriptionRequest
        {
            Type = isSubscribe
                ? MercadoBitcoinSocketCommands.Subscribe
                : MercadoBitcoinSocketCommands.Unsubscribe,
            Subscription = new MercadoBitcoinSocketSubscription
            {
                Name = subscription.Topic,
                Id = subscription.Id,
                Limit = subscription.Topic == MercadoBitcoinSocketTopics.OrderBook
                    ? 200
                    : null,
            },
        }, cancellationToken);

    private ValueTask SendPingAsync(WebSocketClient client,
        CancellationToken cancellationToken)
        => SendAsync(client, new MercadoBitcoinSocketPingRequest(),
            cancellationToken);

    private async ValueTask SendAsync<TPayload>(WebSocketClient client,
        TPayload payload, CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            await client.SendAsync(payload, cancellationToken);
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
            var header = Deserialize<MercadoBitcoinSocketHeader>(payload);
            if (header.Type == MercadoBitcoinSocketMessageTypes.Error)
                throw new InvalidOperationException(
                    $"Mercado Bitcoin WebSocket error: {header.Message}");
            if (header.Type is null or MercadoBitcoinSocketMessageTypes.Pong)
                return;

            switch (header.Type.Value)
            {
                case MercadoBitcoinSocketMessageTypes.Ticker:
                    await RaiseAsync(Deserialize<MercadoBitcoinSocketTicker>(payload),
                        TickerReceived, cancellationToken);
                    break;
                case MercadoBitcoinSocketMessageTypes.OrderBook:
                    await RaiseAsync(
                        Deserialize<MercadoBitcoinSocketOrderBook>(payload),
                        OrderBookReceived, cancellationToken);
                    break;
                case MercadoBitcoinSocketMessageTypes.Trade:
                    await RaiseAsync(Deserialize<MercadoBitcoinSocketTrade>(payload),
                        TradeReceived, cancellationToken);
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
                "Mercado Bitcoin WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private void StartHeartbeat()
    {
        _heartbeatCancellation = new();
        _heartbeatTask = RunHeartbeatAsync(_heartbeatCancellation.Token);
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                var client = _client;
                if (client?.IsConnected == true)
                    await SendPingAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, CancellationToken.None);
        }
    }

    private async ValueTask StopHeartbeatAsync()
    {
        var cancellation = _heartbeatCancellation;
        _heartbeatCancellation = null;
        var task = _heartbeatTask;
        _heartbeatTask = null;
        if (cancellation is null)
            return;
        cancellation.Cancel();
        try
        {
            if (task is not null)
                await task;
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static string ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Mercado Bitcoin WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint.ToString().TrimEnd('/');
    }
}
