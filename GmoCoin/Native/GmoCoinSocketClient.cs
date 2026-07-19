namespace StockSharp.GmoCoin.Native;

sealed class GmoCoinSocketClient : BaseLogReceiver
{
    private readonly record struct SubscriptionKey(
        GmoCoinSocketChannels Channel, string Symbol);

    private readonly string _endpoint;
    private readonly GmoCoinRestClient _restClient;
    private readonly string _token;
    private readonly bool _isPrivate;
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
    private DateTime _nextCommand;
    private CancellationTokenSource _renewalCancellation;
    private Task _renewalTask;

    public GmoCoinSocketClient(string endpoint, GmoCoinRestClient restClient,
        string token, bool isPrivate, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        _token = token;
        _isPrivate = isPrivate;
        _workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
        if (isPrivate && (token.IsEmpty() || !restClient.IsCredentialsAvailable))
            throw new ArgumentException(
                "Private GMO Coin WebSocket requires credentials and an access token.");
    }

    public override string Name
        => _isPrivate ? "GmoCoin_PrivateWebSocket" : "GmoCoin_PublicWebSocket";

    public event Func<GmoCoinSocketTicker, CancellationToken, ValueTask>
        TickerReceived;
    public event Func<GmoCoinSocketOrderBook, CancellationToken, ValueTask>
        OrderBookReceived;
    public event Func<GmoCoinSocketTrade, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<GmoCoinExecutionEvent, CancellationToken, ValueTask>
        ExecutionReceived;
    public event Func<GmoCoinOrderEvent, CancellationToken, ValueTask>
        OrderReceived;
    public event Func<GmoCoinPositionEvent, CancellationToken, ValueTask>
        PositionReceived;
    public event Func<GmoCoinPositionSummaryEvent, CancellationToken, ValueTask>
        PositionSummaryReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _renewalCancellation?.Cancel();
        _renewalCancellation?.Dispose();
        _client?.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "GMO Coin WebSocket is already initialized.");
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            if (_isPrivate)
            {
                await SubscribePrivateChannelsAsync(client, cancellationToken);
                StartTokenRenewal();
            }
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeTickerAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(GmoCoinSocketChannels.Ticker, symbol, true,
            cancellationToken);

    public ValueTask UnsubscribeTickerAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(GmoCoinSocketChannels.Ticker, symbol, false,
            cancellationToken);

    public ValueTask SubscribeOrderBookAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(GmoCoinSocketChannels.OrderBooks, symbol, true,
            cancellationToken);

    public ValueTask UnsubscribeOrderBookAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(GmoCoinSocketChannels.OrderBooks, symbol, false,
            cancellationToken);

    public ValueTask SubscribeTradesAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(GmoCoinSocketChannels.Trades, symbol, true,
            cancellationToken);

    public ValueTask UnsubscribeTradesAsync(string symbol,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(GmoCoinSocketChannels.Trades, symbol, false,
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
        client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
            "StockSharp-GmoCoin-Connector/1.0");
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        await StopTokenRenewalAsync();
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
            if (_isPrivate)
                await SubscribePrivateChannelsAsync(client, cancellationToken);
            else
            {
                SubscriptionKey[] subscriptions;
                using (_sync.EnterScope())
                    subscriptions = [.. _subscriptions];
                foreach (var subscription in subscriptions)
                    await SendSubscriptionAsync(client, subscription, true,
                        cancellationToken);
            }
        }

        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(
        GmoCoinSocketChannels channel, string symbol, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (_isPrivate)
            throw new InvalidOperationException(
                "Public subscriptions cannot use the private GMO Coin socket.");
        var key = new SubscriptionKey(channel, symbol.NormalizeSymbol());
        using (_sync.EnterScope())
            if (isSubscribe ? !_subscriptions.Add(key) : !_subscriptions.Remove(key))
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
        => SendAsync(client, new GmoCoinSocketSubscriptionRequest
        {
            Command = isSubscribe
                ? GmoCoinSocketCommands.Subscribe
                : GmoCoinSocketCommands.Unsubscribe,
            Channel = subscription.Channel,
            Symbol = subscription.Symbol,
        }, cancellationToken);

    private async ValueTask SubscribePrivateChannelsAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        await SendPrivateSubscriptionAsync(client,
            GmoCoinSocketChannels.ExecutionEvents, null, cancellationToken);
        await SendPrivateSubscriptionAsync(client,
            GmoCoinSocketChannels.OrderEvents, null, cancellationToken);
        await SendPrivateSubscriptionAsync(client,
            GmoCoinSocketChannels.PositionEvents, null, cancellationToken);
        await SendPrivateSubscriptionAsync(client,
            GmoCoinSocketChannels.PositionSummaryEvents,
            GmoCoinSocketOptions.Periodic, cancellationToken);
    }

    private ValueTask SendPrivateSubscriptionAsync(WebSocketClient client,
        GmoCoinSocketChannels channel, GmoCoinSocketOptions? option,
        CancellationToken cancellationToken)
        => SendAsync(client, new GmoCoinSocketSubscriptionRequest
        {
            Command = GmoCoinSocketCommands.Subscribe,
            Channel = channel,
            Option = option,
        }, cancellationToken);

    private async ValueTask SendAsync<TPayload>(WebSocketClient client,
        TPayload payload, CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextCommand - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            await client.SendAsync(payload, cancellationToken);
            _nextCommand = DateTime.UtcNow + TimeSpan.FromMilliseconds(1050);
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
            var header = Deserialize<GmoCoinSocketHeader>(payload);
            if (header.Status is > 0)
                throw new InvalidOperationException(
                    $"GMO Coin WebSocket error {header.Status}: {header.Message}");
            if (header.Channel is null)
                return;

            switch (header.Channel.Value)
            {
                case GmoCoinSocketChannels.Ticker:
                    await RaiseAsync(Deserialize<GmoCoinSocketTicker>(payload),
                        TickerReceived, cancellationToken);
                    break;
                case GmoCoinSocketChannels.OrderBooks:
                    await RaiseAsync(Deserialize<GmoCoinSocketOrderBook>(payload),
                        OrderBookReceived, cancellationToken);
                    break;
                case GmoCoinSocketChannels.Trades:
                    await RaiseAsync(Deserialize<GmoCoinSocketTrade>(payload),
                        TradeReceived, cancellationToken);
                    break;
                case GmoCoinSocketChannels.ExecutionEvents:
                    await RaiseAsync(Deserialize<GmoCoinExecutionEvent>(payload),
                        ExecutionReceived, cancellationToken);
                    break;
                case GmoCoinSocketChannels.OrderEvents:
                    await RaiseAsync(Deserialize<GmoCoinOrderEvent>(payload),
                        OrderReceived, cancellationToken);
                    break;
                case GmoCoinSocketChannels.PositionEvents:
                    await RaiseAsync(Deserialize<GmoCoinPositionEvent>(payload),
                        PositionReceived, cancellationToken);
                    break;
                case GmoCoinSocketChannels.PositionSummaryEvents:
                    await RaiseAsync(
                        Deserialize<GmoCoinPositionSummaryEvent>(payload),
                        PositionSummaryReceived, cancellationToken);
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
                "GMO Coin WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private void StartTokenRenewal()
    {
        _renewalCancellation = new();
        _renewalTask = RunTokenRenewalAsync(_renewalCancellation.Token);
    }

    private async Task RunTokenRenewalAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(45), cancellationToken);
                while (true)
                {
                    try
                    {
                        await _restClient.ExtendWebSocketTokenAsync(_token,
                            cancellationToken);
                        break;
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        await RaiseErrorAsync(error, cancellationToken);
                        await Task.Delay(TimeSpan.FromMinutes(1),
                            cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, CancellationToken.None);
        }
    }

    private async ValueTask StopTokenRenewalAsync()
    {
        var cancellation = _renewalCancellation;
        _renewalCancellation = null;
        var task = _renewalTask;
        _renewalTask = null;
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
                "GMO Coin WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint.ToString().TrimEnd('/');
    }
}
