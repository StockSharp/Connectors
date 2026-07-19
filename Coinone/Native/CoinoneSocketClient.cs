namespace StockSharp.Coinone.Native;

sealed class CoinoneSocketClient : BaseLogReceiver
{
    private readonly record struct SubscriptionKey(CoinoneSocketChannels Channel,
        string QuoteCurrency, string TargetCurrency, string Interval);

    private readonly string _endpoint;
    private readonly CoinoneRestClient _restClient;
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
    private CancellationTokenSource _pingCancellation;
    private Task _pingTask;

    public CoinoneSocketClient(string endpoint, CoinoneRestClient restClient,
        bool isPrivate, WorkingTime workingTime, int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        _isPrivate = isPrivate;
        _workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
        if (isPrivate && !restClient.IsCredentialsAvailable)
            throw new ArgumentException(
                "Private Coinone WebSocket requires credentials.", nameof(restClient));
    }

    public override string Name
        => _isPrivate ? "Coinone_PrivateWebSocket" : "Coinone_PublicWebSocket";

    public event Func<CoinoneSocketBook, CancellationToken, ValueTask>
        BookReceived;
    public event Func<CoinoneSocketTicker, CancellationToken, ValueTask>
        TickerReceived;
    public event Func<CoinoneSocketTrade, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<CoinoneSocketCandle, CancellationToken, ValueTask>
        CandleReceived;
    public event Func<CoinoneMyOrderUpdate, CancellationToken, ValueTask>
        MyOrderReceived;
    public event Func<CoinoneMyAssetUpdate, CancellationToken, ValueTask>
        MyAssetReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _pingCancellation?.Cancel();
        _pingCancellation?.Dispose();
        _client?.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "Coinone WebSocket is already initialized.");
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            if (_isPrivate)
                await SubscribePrivateChannelsAsync(client, cancellationToken);
            StartPing(client);
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeOrderBookAsync(string quoteCurrency,
        string targetCurrency, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.OrderBook, quoteCurrency,
            targetCurrency, null, true, cancellationToken);

    public ValueTask UnsubscribeOrderBookAsync(string quoteCurrency,
        string targetCurrency, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.OrderBook, quoteCurrency,
            targetCurrency, null, false, cancellationToken);

    public ValueTask SubscribeTickerAsync(string quoteCurrency,
        string targetCurrency, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.Ticker, quoteCurrency,
            targetCurrency, null, true, cancellationToken);

    public ValueTask UnsubscribeTickerAsync(string quoteCurrency,
        string targetCurrency, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.Ticker, quoteCurrency,
            targetCurrency, null, false, cancellationToken);

    public ValueTask SubscribeTradesAsync(string quoteCurrency,
        string targetCurrency, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.Trade, quoteCurrency,
            targetCurrency, null, true, cancellationToken);

    public ValueTask UnsubscribeTradesAsync(string quoteCurrency,
        string targetCurrency, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.Trade, quoteCurrency,
            targetCurrency, null, false, cancellationToken);

    public ValueTask SubscribeCandlesAsync(string quoteCurrency,
        string targetCurrency, string interval,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.Chart, quoteCurrency,
            targetCurrency, interval, true, cancellationToken);

    public ValueTask UnsubscribeCandlesAsync(string quoteCurrency,
        string targetCurrency, string interval,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(CoinoneSocketChannels.Chart, quoteCurrency,
            targetCurrency, interval, false, cancellationToken);

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
                "StockSharp-Coinone-Connector/1.0");
            if (!_isPrivate)
                return;
            var authentication = _restClient.CreateWebSocketAuthentication();
            socket.Options.SetRequestHeader("X-COINONE-PAYLOAD",
                authentication.EncodedPayload);
            socket.Options.SetRequestHeader("X-COINONE-SIGNATURE",
                authentication.Signature);
        };
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        await StopPingAsync();
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
        CoinoneSocketChannels channel, string quoteCurrency,
        string targetCurrency, string interval, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (_isPrivate)
            throw new InvalidOperationException(
                "Public subscriptions cannot use the private Coinone socket.");
        var key = new SubscriptionKey(channel,
            quoteCurrency.NormalizeCurrency(), targetCurrency.NormalizeCurrency(),
            interval);
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
        => SendAsync(client, new CoinoneSocketSubscriptionRequest
        {
            RequestType = isSubscribe
                ? CoinoneSocketRequestTypes.Subscribe
                : CoinoneSocketRequestTypes.Unsubscribe,
            Channel = subscription.Channel,
            Topic = new()
            {
                QuoteCurrency = subscription.QuoteCurrency,
                TargetCurrency = subscription.TargetCurrency,
                Interval = subscription.Interval,
            },
        }, cancellationToken);

    private async ValueTask SubscribePrivateChannelsAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        await SendAsync(client, new CoinoneSocketChannelRequest
        {
            RequestType = CoinoneSocketRequestTypes.Subscribe,
            Channel = CoinoneSocketChannels.MyOrder,
        }, cancellationToken);
        await SendAsync(client, new CoinoneSocketChannelRequest
        {
            RequestType = CoinoneSocketRequestTypes.Subscribe,
            Channel = CoinoneSocketChannels.MyAsset,
        }, cancellationToken);
    }

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
            var header = Deserialize<CoinoneSocketHeader>(payload);
            if (header.ResponseType == CoinoneSocketResponseTypes.Error)
                throw new InvalidOperationException(
                    $"Coinone WebSocket error {header.ErrorCode}: {header.Message}");
            if (header.ResponseType != CoinoneSocketResponseTypes.Data ||
                header.Channel is null)
                return;

            switch (header.Channel.Value)
            {
                case CoinoneSocketChannels.OrderBook:
                    await RaiseAsync(Deserialize<CoinoneSocketEnvelope<
                        CoinoneSocketBook>>(payload).Data, BookReceived,
                        cancellationToken);
                    break;
                case CoinoneSocketChannels.Ticker:
                    await RaiseAsync(Deserialize<CoinoneSocketEnvelope<
                        CoinoneSocketTicker>>(payload).Data, TickerReceived,
                        cancellationToken);
                    break;
                case CoinoneSocketChannels.Trade:
                    await RaiseAsync(Deserialize<CoinoneSocketEnvelope<
                        CoinoneSocketTrade>>(payload).Data, TradeReceived,
                        cancellationToken);
                    break;
                case CoinoneSocketChannels.Chart:
                    await RaiseAsync(Deserialize<CoinoneSocketEnvelope<
                        CoinoneSocketCandle>>(payload).Data, CandleReceived,
                        cancellationToken);
                    break;
                case CoinoneSocketChannels.MyOrder:
                    await RaiseAsync(Deserialize<CoinoneSocketEnvelope<
                        CoinoneMyOrderUpdate>>(payload).Data, MyOrderReceived,
                        cancellationToken);
                    break;
                case CoinoneSocketChannels.MyAsset:
                    await RaiseAsync(Deserialize<CoinoneSocketEnvelope<
                        CoinoneMyAssetUpdate>>(payload).Data, MyAssetReceived,
                        cancellationToken);
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
                "Coinone WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private void StartPing(WebSocketClient client)
    {
        _pingCancellation = new();
        _pingTask = RunPingAsync(client, _pingCancellation.Token);
    }

    private async Task RunPingAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
                if (client.IsConnected)
                    await SendAsync(client, new CoinoneSocketPingRequest(),
                        cancellationToken);
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

    private async ValueTask StopPingAsync()
    {
        var cancellation = _pingCancellation;
        _pingCancellation = null;
        var task = _pingTask;
        _pingTask = null;
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
                "Coinone WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint.ToString().TrimEnd('/');
    }
}
