namespace StockSharp.BTCMarkets.Native;

sealed class BTCMarketsSocketClient : BaseLogReceiver
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly Func<long> _timestampFactory;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly Dictionary<BTCMarketsSocketChannels, HashSet<string>>
        _publicSubscriptions = [];
    private readonly HashSet<BTCMarketsSocketChannels> _privateSubscriptions = [];
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

    public BTCMarketsSocketClient(string endpoint, SecureString key,
        SecureString secret, Func<long> timestampFactory, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "BTC Markets WebSocket key and secret must be configured together.");
        if (!secretText.IsEmpty())
        {
            try
            {
                _apiSecret = Convert.FromBase64String(secretText);
            }
            catch (FormatException error)
            {
                throw new ArgumentException(
                    "BTC Markets API secret must be Base64 encoded.",
                    nameof(secret), error);
            }
        }
        _timestampFactory = timestampFactory ?? throw new ArgumentNullException(
            nameof(timestampFactory));
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "BTCMarkets_WebSocket";

    public event Func<BTCMarketsSocketTick, CancellationToken, ValueTask>
        TickReceived;
    public event Func<BTCMarketsSocketTrade, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<BTCMarketsSocketOrderBook, CancellationToken, ValueTask>
        OrderBookReceived;
    public event Func<BTCMarketsSocketOrderChange, CancellationToken, ValueTask>
        OrderChanged;
    public event Func<BTCMarketsSocketFundChange, CancellationToken, ValueTask>
        FundChanged;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _client?.Dispose();
        _sendSync.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "BTC Markets WebSocket is already initialized.");
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

    public ValueTask SubscribeAsync(BTCMarketsSocketChannels channel,
        string marketId, CancellationToken cancellationToken)
        => ChangePublicSubscriptionAsync(channel, marketId, true,
            cancellationToken);

    public ValueTask UnsubscribeAsync(BTCMarketsSocketChannels channel,
        string marketId, CancellationToken cancellationToken)
        => ChangePublicSubscriptionAsync(channel, marketId, false,
            cancellationToken);

    public ValueTask SubscribePrivateAsync(BTCMarketsSocketChannels channel,
        CancellationToken cancellationToken)
        => ChangePrivateSubscriptionAsync(channel, true, cancellationToken);

    public ValueTask UnsubscribePrivateAsync(BTCMarketsSocketChannels channel,
        CancellationToken cancellationToken)
        => ChangePrivateSubscriptionAsync(channel, false, cancellationToken);

    public async ValueTask RefreshOrderBookAsync(string marketId,
        CancellationToken cancellationToken)
    {
        marketId = marketId.NormalizeMarket();
        var client = _client;
        if (client?.IsConnected != true)
            return;
        await SendCommandAsync(client,
            BTCMarketsSocketMessageTypes.RemoveSubscription,
            [BTCMarketsSocketChannels.OrderBookUpdate], [marketId],
            cancellationToken);
        await SendCommandAsync(client,
            BTCMarketsSocketMessageTypes.AddSubscription,
            [BTCMarketsSocketChannels.OrderBookUpdate], [marketId],
            cancellationToken);
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
        (BTCMarketsSocketChannels Channel, string[] Markets)[] publicSubscriptions;
        BTCMarketsSocketChannels[] privateSubscriptions;
        using (_sync.EnterScope())
        {
            publicSubscriptions = [.. _publicSubscriptions.Select(static pair =>
                (pair.Key, pair.Value.OrderBy(static value => value,
                    StringComparer.OrdinalIgnoreCase).ToArray()))];
            privateSubscriptions = [.. _privateSubscriptions];
        }
        await SendCommandAsync(client, BTCMarketsSocketMessageTypes.Subscribe,
            [BTCMarketsSocketChannels.Heartbeat], [], cancellationToken);
        foreach (var subscription in publicSubscriptions)
            await SendCommandAsync(client,
                BTCMarketsSocketMessageTypes.AddSubscription,
                [subscription.Channel], subscription.Markets, cancellationToken);
        foreach (var channel in privateSubscriptions)
            await SendCommandAsync(client,
                BTCMarketsSocketMessageTypes.AddSubscription, [channel], [],
                cancellationToken);
    }

    private async ValueTask ChangePublicSubscriptionAsync(
        BTCMarketsSocketChannels channel, string marketId, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (channel is BTCMarketsSocketChannels.OrderChange or
            BTCMarketsSocketChannels.FundChange or
            BTCMarketsSocketChannels.Heartbeat)
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "The channel is not a market-specific public channel.");
        marketId = marketId.NormalizeMarket();
        using (_sync.EnterScope())
        {
            if (!_publicSubscriptions.TryGetValue(channel, out var markets))
                _publicSubscriptions.Add(channel, markets = new(
                    StringComparer.OrdinalIgnoreCase));
            var changed = isSubscribe
                ? markets.Add(marketId)
                : markets.Remove(marketId);
            if (!changed)
                return;
            if (markets.Count == 0)
                _publicSubscriptions.Remove(channel);
        }
        var client = _client;
        if (client?.IsConnected != true)
            return;
        try
        {
            await SendCommandAsync(client, isSubscribe
                ? BTCMarketsSocketMessageTypes.AddSubscription
                : BTCMarketsSocketMessageTypes.RemoveSubscription,
                [channel], [marketId], cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
            {
                if (!_publicSubscriptions.TryGetValue(channel, out var markets))
                    _publicSubscriptions.Add(channel, markets = new(
                        StringComparer.OrdinalIgnoreCase));
                if (isSubscribe)
                    markets.Remove(marketId);
                else
                    markets.Add(marketId);
                if (markets.Count == 0)
                    _publicSubscriptions.Remove(channel);
            }
            throw;
        }
    }

    private async ValueTask ChangePrivateSubscriptionAsync(
        BTCMarketsSocketChannels channel, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (channel is not (BTCMarketsSocketChannels.OrderChange or
            BTCMarketsSocketChannels.FundChange))
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "The channel is not a private channel.");
        if (_apiKey.IsEmpty())
            throw new InvalidOperationException(
                "BTC Markets credentials are required for private WebSocket events.");
        using (_sync.EnterScope())
        {
            var changed = isSubscribe
                ? _privateSubscriptions.Add(channel)
                : _privateSubscriptions.Remove(channel);
            if (!changed)
                return;
        }
        var client = _client;
        if (client?.IsConnected != true)
            return;
        try
        {
            await SendCommandAsync(client, isSubscribe
                ? BTCMarketsSocketMessageTypes.AddSubscription
                : BTCMarketsSocketMessageTypes.RemoveSubscription,
                [channel], [], cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                if (isSubscribe)
                    _privateSubscriptions.Remove(channel);
                else
                    _privateSubscriptions.Add(channel);
            throw;
        }
    }

    private async ValueTask SendCommandAsync(WebSocketClient client,
        BTCMarketsSocketMessageTypes messageType,
        BTCMarketsSocketChannels[] channels, string[] markets,
        CancellationToken cancellationToken)
    {
        var timestamp = _apiKey.IsEmpty()
            ? (long?)null
            : _timestampFactory();
        string signature = null;
        if (timestamp is long value)
        {
            using var hmac = new HMACSHA512(_apiSecret);
            signature = Convert.ToBase64String(hmac.ComputeHash(
                Encoding.UTF8.GetBytes("GET\n" + value.ToString(
                    CultureInfo.InvariantCulture))));
        }
        var command = new BTCMarketsSocketCommand
        {
            MarketIds = markets is { Length: > 0 } ? markets : null,
            Channels = channels,
            MessageType = messageType,
            Timestamp = timestamp,
            Key = timestamp is null ? null : _apiKey,
            Signature = signature,
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
            var header = Deserialize<BTCMarketsSocketHeader>(payload);
            switch (header.MessageType)
            {
                case BTCMarketsSocketMessageTypes.Tick:
                    await RaiseAsync(Deserialize<BTCMarketsSocketTick>(payload),
                        TickReceived, cancellationToken);
                    break;
                case BTCMarketsSocketMessageTypes.Trade:
                    await RaiseAsync(Deserialize<BTCMarketsSocketTrade>(payload),
                        TradeReceived, cancellationToken);
                    break;
                case BTCMarketsSocketMessageTypes.OrderBook:
                case BTCMarketsSocketMessageTypes.OrderBookUpdate:
                    await RaiseAsync(Deserialize<BTCMarketsSocketOrderBook>(payload),
                        OrderBookReceived, cancellationToken);
                    break;
                case BTCMarketsSocketMessageTypes.OrderChange:
                    await RaiseAsync(Deserialize<BTCMarketsSocketOrderChange>(payload),
                        OrderChanged, cancellationToken);
                    break;
                case BTCMarketsSocketMessageTypes.FundChange:
                    await RaiseAsync(Deserialize<BTCMarketsSocketFundChange>(payload),
                        FundChanged, cancellationToken);
                    break;
                case BTCMarketsSocketMessageTypes.Heartbeat:
                    _ = Deserialize<BTCMarketsSocketHeartbeat>(payload);
                    break;
                case BTCMarketsSocketMessageTypes.Error:
                    var error = Deserialize<BTCMarketsSocketError>(payload);
                    throw new InvalidOperationException(
                        $"BTC Markets WebSocket error {error.Code}: " +
                        (error.Message.IsEmpty() ? "unknown error" : error.Message));
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
                "BTC Markets WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "BTC Markets WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
