namespace StockSharp.OSL.Native;

sealed class OSLSocketClient : BaseLogReceiver
{
    private const int _maximumMessageBytes = 1024 * 1024;
    private readonly string _endpoint;
    private readonly OSLSocketKinds _kind;
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly string _passphrase;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<OSLSubscriptionKey> _subscriptions = [];
    private readonly HashSet<OSLSubscriptionKey> _sentSubscriptions = [];
    private readonly HashSet<string> _candleSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private WebSocketClient _client;
    private bool _isLoggedIn;
    private long _requestId;
    private DateTime _nextSend;
    private TaskCompletionSource<bool> _loginCompletion;

    public OSLSocketClient(string endpoint, OSLSocketKinds kind,
        SecureString key, SecureString secret, SecureString passphrase,
        WorkingTime workingTime, int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _kind = kind;
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        _passphrase = passphrase.IsEmpty()
            ? string.Empty
            : passphrase.UnSecure();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "OSL API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => $"OSL_{_kind}_WebSocket";

    public bool IsCredentialsAvailable => !_apiKey.IsEmpty();

    public event Func<OSLTicker, CancellationToken, ValueTask> TickerReceived;
    public event Func<string, OSLOrderBook, CancellationToken, ValueTask>
        BookReceived;
    public event Func<string, OSLPublicTrade, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<OSLLegacyCandle, CancellationToken, ValueTask>
        CandleReceived;
    public event Func<OSLOrder, CancellationToken, ValueTask> OrderReceived;
    public event Func<OSLFill, CancellationToken, ValueTask> FillReceived;
    public event Func<OSLAsset[], CancellationToken, ValueTask> AssetsReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

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
                "OSL WebSocket is already initialized.");
        if (_kind == OSLSocketKinds.Private && !IsCredentialsAvailable)
            throw new InvalidOperationException(
                "OSL API key and secret are required for private WebSocket access.");

        _loginCompletion = _kind == OSLSocketKinds.Private
            ? CreateLoginCompletion()
            : null;
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            if (_kind == OSLSocketKinds.Private)
                await _loginCompletion.Task.WaitAsync(
                    TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeAsync(OSLWsChannels channel, string selector,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(channel, NormalizeSelector(channel,
            selector)), true, cancellationToken);

    public ValueTask UnsubscribeAsync(OSLWsChannels channel, string selector,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(channel, NormalizeSelector(channel,
            selector)), false, cancellationToken);

    public ValueTask SubscribeCandleAsync(string symbol, TimeSpan timeFrame,
        CancellationToken cancellationToken)
        => ChangeCandleSubscriptionAsync(symbol.ToCandleSelector(timeFrame),
            true, cancellationToken);

    public ValueTask UnsubscribeCandleAsync(string symbol, TimeSpan timeFrame,
        CancellationToken cancellationToken)
        => ChangeCandleSubscriptionAsync(symbol.ToCandleSelector(timeFrame),
            false, cancellationToken);

    public ValueTask PingAsync(CancellationToken cancellationToken)
        => _client is { IsConnected: true } client
            ? SendAsync(client, "ping", cancellationToken)
            : default;

    private WebSocketClient CreateClient()
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            _endpoint,
            (state, token) => OnStateChangedAsync(client, state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(socket, message,
                token),
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
        client.Init += socket => socket.Options.SetRequestHeader(
            "User-Agent", "StockSharp-OSL-Connector/1.0");
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        _isLoggedIn = false;
        _loginCompletion?.TrySetCanceled(cancellationToken);
        _loginCompletion = null;
        using (_sync.EnterScope())
            _sentSubscriptions.Clear();
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
        if (state is ConnectionStates.Disconnected or ConnectionStates.Failed)
        {
            _isLoggedIn = false;
            using (_sync.EnterScope())
                _sentSubscriptions.Clear();
        }

        if (state is ConnectionStates.Connected or ConnectionStates.Restored)
        {
            if (_kind == OSLSocketKinds.Private)
            {
                _isLoggedIn = false;
                using (_sync.EnterScope())
                    _sentSubscriptions.Clear();
                if (state == ConnectionStates.Restored ||
                    _loginCompletion is null)
                    _loginCompletion = CreateLoginCompletion();
                await SendLoginAsync(client, cancellationToken);
            }
            else if (state == ConnectionStates.Restored)
                await RestoreSubscriptionsAsync(client, cancellationToken);
        }

        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask RestoreSubscriptionsAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        if (_kind == OSLSocketKinds.Candles)
        {
            string[] candleSubscriptions;
            using (_sync.EnterScope())
                candleSubscriptions = [.. _candleSubscriptions];
            foreach (var selector in candleSubscriptions)
                await SendLegacySubscriptionAsync(client, selector, true,
                    cancellationToken);
            return;
        }

        OSLSubscriptionKey[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _subscriptions];
        foreach (var subscription in subscriptions)
            await SendSubscriptionAsync(client, subscription, true,
                cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(
        OSLSubscriptionKey subscription, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (_kind == OSLSocketKinds.Candles)
            throw new InvalidOperationException(
                "Use the OSL candle subscription command on the legacy stream.");
        if (_kind == OSLSocketKinds.Public && subscription.Channel is
            OSLWsChannels.Fill or OSLWsChannels.Orders or
            OSLWsChannels.SpotAssets)
            throw new InvalidOperationException(
                "Private OSL channels require the private WebSocket.");
        if (_kind == OSLSocketKinds.Private && subscription.Channel is
            OSLWsChannels.Ticker or OSLWsChannels.Books5 or
            OSLWsChannels.Books15 or OSLWsChannels.Trade)
            throw new InvalidOperationException(
                "Public OSL channels require the public WebSocket.");

        bool shouldSend;
        using (_sync.EnterScope())
            shouldSend = isSubscribe
                ? _subscriptions.Add(subscription)
                : _subscriptions.Remove(subscription);
        if (!shouldSend || _client?.IsConnected != true ||
            _kind == OSLSocketKinds.Private && !_isLoggedIn)
            return;
        await SendSubscriptionAsync(_client, subscription, isSubscribe,
            cancellationToken);
        if (_kind == OSLSocketKinds.Private)
        {
            using (_sync.EnterScope())
            {
                if (isSubscribe)
                    _sentSubscriptions.Add(subscription);
                else
                    _sentSubscriptions.Remove(subscription);
            }
        }
    }

    private async ValueTask ChangeCandleSubscriptionAsync(string selector,
        bool isSubscribe, CancellationToken cancellationToken)
    {
        if (_kind != OSLSocketKinds.Candles)
            throw new InvalidOperationException(
                "OSL candle subscriptions require the legacy public stream.");
        bool shouldSend;
        using (_sync.EnterScope())
            shouldSend = isSubscribe
                ? _candleSubscriptions.Add(selector)
                : _candleSubscriptions.Remove(selector);
        if (shouldSend && _client?.IsConnected == true)
            await SendLegacySubscriptionAsync(_client, selector, isSubscribe,
                cancellationToken);
    }

    private ValueTask SendSubscriptionAsync(WebSocketClient client,
        OSLSubscriptionKey subscription, bool isSubscribe,
        CancellationToken cancellationToken)
        => SendAsync(client, new OSLWsCommand<OSLWsArgument>
        {
            Operation = isSubscribe
                ? OSLWsOperations.Subscribe
                : OSLWsOperations.Unsubscribe,
            Arguments = [ToArgument(subscription)],
        }, cancellationToken);

    private static OSLWsArgument ToArgument(
        OSLSubscriptionKey subscription)
        => subscription.Channel == OSLWsChannels.SpotAssets
            ? new()
            {
                Channel = subscription.Channel,
                Coin = subscription.Selector,
            }
            : new()
            {
                Channel = subscription.Channel,
                InstrumentId = subscription.Selector,
            };

    private ValueTask SendLegacySubscriptionAsync(WebSocketClient client,
        string selector, bool isSubscribe,
        CancellationToken cancellationToken)
        => SendAsync(client, new OSLLegacyCommand
        {
            Method = isSubscribe
                ? OSLLegacyMethods.Subscribe
                : OSLLegacyMethods.Unsubscribe,
            Parameters = [selector],
            IsBinary = false,
            Id = Interlocked.Increment(ref _requestId).ToString(
                CultureInfo.InvariantCulture),
        }, cancellationToken);

    private async ValueTask SendLoginAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        var signatureBytes = HMACSHA256.HashData(_apiSecret,
            Encoding.UTF8.GetBytes(timestamp + "GET/user/verify"));
        try
        {
            await SendAsync(client, new OSLWsCommand<OSLWsLogin>
            {
                Operation = OSLWsOperations.Login,
                Arguments =
                [
                    new()
                    {
                        ApiKey = _apiKey,
                        Passphrase = _passphrase,
                        Timestamp = timestamp,
                        Signature = Convert.ToBase64String(signatureBytes),
                    },
                ],
            }, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }

    private async ValueTask SynchronizePrivateSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        if (_kind != OSLSocketKinds.Private || !_isLoggedIn ||
            _client?.IsConnected != true)
            return;

        OSLSubscriptionKey[] subscribe;
        OSLSubscriptionKey[] unsubscribe;
        using (_sync.EnterScope())
        {
            subscribe = [.. _subscriptions.Except(_sentSubscriptions)];
            unsubscribe = [.. _sentSubscriptions.Except(_subscriptions)];
        }
        foreach (var item in subscribe)
        {
            await SendSubscriptionAsync(_client, item, true,
                cancellationToken);
            using (_sync.EnterScope())
                _sentSubscriptions.Add(item);
        }
        foreach (var item in unsubscribe)
        {
            await SendSubscriptionAsync(_client, item, false,
                cancellationToken);
            using (_sync.EnterScope())
                _sentSubscriptions.Remove(item);
        }
    }

    private async ValueTask SendAsync<TPayload>(WebSocketClient client,
        TPayload payload, CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextSend - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            await client.SendAsync(payload, cancellationToken);
            _nextSend = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
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
        if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
            return;
        if (Encoding.UTF8.GetByteCount(payload) > _maximumMessageBytes)
        {
            await RaiseErrorAsync(new InvalidDataException(
                "OSL WebSocket message exceeds the 1 MiB limit."),
                cancellationToken);
            return;
        }
        try
        {
            if (_kind == OSLSocketKinds.Candles)
                await ProcessLegacyAsync(payload, cancellationToken);
            else
                await ProcessV2Async(payload, cancellationToken);
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private async ValueTask ProcessV2Async(string payload,
        CancellationToken cancellationToken)
    {
        var header = Deserialize<OSLWsHeader>(payload);
        if (header.Event is OSLWsEvents wsEvent)
        {
            await ProcessEventAsync(wsEvent, header, cancellationToken);
            return;
        }
        if (header.Argument is null || header.Action is null)
            return;
        switch (header.Argument.Channel)
        {
            case OSLWsChannels.Ticker:
                await RaiseItemsAsync(TickerReceived,
                    Deserialize<OSLWsEnvelope<OSLTicker>>(payload).Data,
                    cancellationToken);
                break;
            case OSLWsChannels.Books5:
            case OSLWsChannels.Books15:
                if (BookReceived is { } bookHandler)
                    foreach (var book in Deserialize<OSLWsEnvelope<
                        OSLOrderBook>>(payload).Data ?? [])
                        if (book is not null)
                            await bookHandler(header.Argument.InstrumentId,
                                book, cancellationToken);
                break;
            case OSLWsChannels.Trade:
                if (TradeReceived is { } tradeHandler)
                    foreach (var trade in Deserialize<OSLWsEnvelope<
                        OSLPublicTrade>>(payload).Data ?? [])
                        if (trade is not null)
                            await tradeHandler(header.Argument.InstrumentId,
                                trade, cancellationToken);
                break;
            case OSLWsChannels.Orders:
                await RaiseItemsAsync(OrderReceived,
                    Deserialize<OSLWsEnvelope<OSLOrder>>(payload).Data,
                    cancellationToken);
                break;
            case OSLWsChannels.Fill:
                await RaiseItemsAsync(FillReceived,
                    Deserialize<OSLWsEnvelope<OSLFill>>(payload).Data,
                    cancellationToken);
                break;
            case OSLWsChannels.SpotAssets:
                if (AssetsReceived is { } assetHandler)
                    await assetHandler(Deserialize<OSLWsEnvelope<OSLAsset>>(
                        payload).Data ?? [], cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(header.Argument.Channel), header.Argument.Channel,
                    null);
        }
    }

    private async ValueTask ProcessEventAsync(OSLWsEvents wsEvent,
        OSLWsHeader header, CancellationToken cancellationToken)
    {
        if (wsEvent == OSLWsEvents.Error)
        {
            var error = new InvalidOperationException(
                $"OSL WebSocket error {header.Code ?? "unknown"}: " +
                (header.Message ?? "request rejected"));
            _loginCompletion?.TrySetException(error);
            throw error;
        }
        if (wsEvent != OSLWsEvents.Login)
            return;
        if (header.Code.IsEmpty() || header.Code is "0" or "00000")
        {
            _isLoggedIn = true;
            _loginCompletion?.TrySetResult(true);
            await SynchronizePrivateSubscriptionsAsync(cancellationToken);
            return;
        }
        var loginError = new InvalidOperationException(
            $"OSL WebSocket login failed ({header.Code}): " +
            (header.Message ?? "unknown error"));
        _loginCompletion?.TrySetException(loginError);
        throw loginError;
    }

    private async ValueTask ProcessLegacyAsync(string payload,
        CancellationToken cancellationToken)
    {
        var envelope = Deserialize<OSLLegacyEnvelope>(payload);
        if (!envelope.Code.IsEmpty())
            throw new InvalidOperationException(
                $"OSL candle WebSocket error {envelope.Code}: " +
                (envelope.Message ?? envelope.ShortMessage ??
                    "request rejected"));
        if (!envelope.EventType.EqualsIgnoreCase("kline") ||
            CandleReceived is not { } handler)
            return;
        foreach (var candle in envelope.Data ?? [])
            if (candle is not null)
                await handler(candle, cancellationToken);
    }

    private TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
            throw new InvalidDataException(
                "OSL WebSocket returned an empty JSON value.");

    private static async ValueTask RaiseItemsAsync<TItem>(
        Func<TItem, CancellationToken, ValueTask> handler, TItem[] items,
        CancellationToken cancellationToken)
        where TItem : class
    {
        if (handler is null)
            return;
        foreach (var item in items ?? [])
            if (item is not null)
                await handler(item, cancellationToken);
    }

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private static string NormalizeSelector(OSLWsChannels channel,
        string selector)
        => channel is OSLWsChannels.Fill or OSLWsChannels.SpotAssets
            ? "default"
            : selector.NormalizeSymbol();

    private static TaskCompletionSource<bool> CreateLoginCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "OSL WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
