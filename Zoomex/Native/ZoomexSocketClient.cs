namespace StockSharp.Zoomex.Native;

sealed class ZoomexSocketClient : BaseLogReceiver
{
    private const int _maximumMessageBytes = 4 * 1024 * 1024;
    private readonly string _endpoint;
    private readonly ZoomexCategories? _category;
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, int> _subscriptions =
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
    private bool _isAuthenticated;
    private long _requestId;
    private DateTime _nextSend;
    private TaskCompletionSource<bool> _authCompletion;

    public ZoomexSocketClient(string endpoint, ZoomexCategories? category,
        SecureString key, SecureString secret, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _category = category;
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "Zoomex API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        if (_category is null && _apiKey.IsEmpty())
            throw new ArgumentException(
                "The Zoomex private WebSocket requires API credentials.");
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => _category is ZoomexCategories category
        ? $"Zoomex_{category}_WebSocket"
        : "Zoomex_Private_WebSocket";

    public event Func<ZoomexCategories, ZoomexTicker, long,
        CancellationToken, ValueTask> TickerReceived;
    public event Func<ZoomexCategories, ZoomexOrderBook, string,
        ZoomexWsUpdateTypes?, long, CancellationToken, ValueTask> BookReceived;
    public event Func<ZoomexCategories, ZoomexWsPublicTrade[], long,
        CancellationToken, ValueTask> PublicTradesReceived;
    public event Func<ZoomexCategories, string, ZoomexWsCandle[], long,
        CancellationToken, ValueTask> CandlesReceived;
    public event Func<ZoomexOrder[], long, CancellationToken, ValueTask>
        OrdersReceived;
    public event Func<ZoomexExecution[], long, CancellationToken, ValueTask>
        ExecutionsReceived;
    public event Func<ZoomexPosition[], long, CancellationToken, ValueTask>
        PositionsReceived;
    public event Func<ZoomexWalletAccount[], long, CancellationToken,
        ValueTask> WalletsReceived;
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
                "Zoomex WebSocket is already initialized.");
        _authCompletion = _category is null
            ? CreateCompletion()
            : null;
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            if (_category is null)
                await _authCompletion.Task.WaitAsync(
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

    public ValueTask SubscribeAsync(string topic,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(topic, true, cancellationToken);

    public ValueTask UnsubscribeAsync(string topic,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(topic, false, cancellationToken);

    public ValueTask PingAsync(CancellationToken cancellationToken)
        => _client is { IsConnected: true } client
            ? SendAsync(client, new ZoomexWsCommand
            {
                RequestId = NextRequestId(),
                Operation = ZoomexWsOperations.Ping,
            }, cancellationToken)
            : default;

    private WebSocketClient CreateClient()
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            _endpoint,
            (state, token) => OnStateChangedAsync(client, state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(message, token),
            (format, args) => this.AddInfoLog(format, args),
            (format, args) => this.AddErrorLog(format, args),
            (format, args) => this.AddVerboseLog(format, args))
        {
            ReconnectAttempts = _reconnectAttempts,
            WorkingTime = _workingTime,
            DisableAutoResend = true,
            Indent = false,
            SendSettings = _jsonSettings,
        };
        client.Init += socket => socket.Options.SetRequestHeader(
            "User-Agent", "StockSharp-Zoomex-Connector/1.0");
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        _isAuthenticated = false;
        _authCompletion?.TrySetCanceled(cancellationToken);
        _authCompletion = null;
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
        if (state is ConnectionStates.Reconnecting or
            ConnectionStates.Disconnected or ConnectionStates.Failed)
            _isAuthenticated = false;

        if (state is ConnectionStates.Connected or ConnectionStates.Restored)
        {
            if (_category is null)
            {
                if (state == ConnectionStates.Restored ||
                    _authCompletion is null)
                    _authCompletion = CreateCompletion();
                await AuthenticateAsync(client, cancellationToken);
            }
            else if (state == ConnectionStates.Restored)
            {
                await RestoreSubscriptionsAsync(client, cancellationToken);
            }
        }

        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(string topic,
        bool isSubscribe, CancellationToken cancellationToken)
    {
        topic = ValidateTopic(topic);
        var shouldSend = false;
        using (_sync.EnterScope())
        {
            _subscriptions.TryGetValue(topic, out var count);
            if (isSubscribe)
            {
                _subscriptions[topic] = count + 1;
                shouldSend = count == 0;
            }
            else if (count > 1)
            {
                _subscriptions[topic] = count - 1;
            }
            else if (count == 1)
            {
                _subscriptions.Remove(topic);
                shouldSend = true;
            }
        }
        if (!shouldSend || _client?.IsConnected != true ||
            _category is null && !_isAuthenticated)
            return;
        await SendSubscriptionAsync(_client, topic, isSubscribe,
            cancellationToken);
    }

    private async ValueTask RestoreSubscriptionsAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        string[] topics;
        using (_sync.EnterScope())
            topics = [.. _subscriptions.Keys];
        foreach (var topic in topics)
            await SendSubscriptionAsync(client, topic, true,
                cancellationToken);
    }

    private ValueTask SendSubscriptionAsync(WebSocketClient client,
        string topic, bool isSubscribe,
        CancellationToken cancellationToken)
        => SendAsync(client, new ZoomexWsCommand
        {
            RequestId = NextRequestId(),
            Operation = isSubscribe
                ? ZoomexWsOperations.Subscribe
                : ZoomexWsOperations.Unsubscribe,
            Arguments = [topic],
        }, cancellationToken);

    private async ValueTask AuthenticateAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        var expires = (long)DateTime.UtcNow.AddSeconds(10).ToUnix(false);
        var signature = HMACSHA256.HashData(_apiSecret,
            Encoding.UTF8.GetBytes("GET/realtime" + expires.ToString(
                CultureInfo.InvariantCulture)));
        try
        {
            await SendAsync(client, new ZoomexWsAuthCommand
            {
                RequestId = NextRequestId(),
                Operation = ZoomexWsOperations.Auth,
                Arguments = new()
                {
                    ApiKey = _apiKey,
                    Expires = expires,
                    Signature = Convert.ToHexString(signature)
                        .ToLowerInvariant(),
                },
            }, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
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
            _nextSend = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private async ValueTask OnProcessAsync(WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var payload = message.AsString();
        if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
            return;
        if (Encoding.UTF8.GetByteCount(payload) > _maximumMessageBytes)
        {
            await RaiseErrorAsync(new InvalidDataException(
                "Zoomex WebSocket message exceeds the 4 MiB limit."),
                cancellationToken);
            return;
        }
        try
        {
            var header = Deserialize<ZoomexWsHeader>(payload);
            if (header.Operation is not null)
            {
                await ProcessOperationAsync(header, cancellationToken);
                return;
            }
            if (header.Topic.IsEmpty())
                return;
            await ProcessTopicAsync(header.Topic, payload,
                cancellationToken);
        }
        catch (Exception error)
        {
            _authCompletion?.TrySetException(error);
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private async ValueTask ProcessOperationAsync(ZoomexWsHeader header,
        CancellationToken cancellationToken)
    {
        if (header.IsSuccess == false)
            throw new InvalidOperationException(
                $"Zoomex WebSocket {header.Operation} failed: " +
                (header.Message ?? "request rejected"));
        if (header.Operation != ZoomexWsOperations.Auth)
            return;
        _isAuthenticated = true;
        _authCompletion?.TrySetResult(true);
        if (_client is not null)
            await RestoreSubscriptionsAsync(_client, cancellationToken);
    }

    private async ValueTask ProcessTopicAsync(string topic, string payload,
        CancellationToken cancellationToken)
    {
        if (_category is ZoomexCategories category)
        {
            if (topic.StartsWith("tickers.",
                StringComparison.OrdinalIgnoreCase))
            {
                var envelope = Deserialize<ZoomexWsEnvelope<ZoomexTicker>>(
                    payload);
                if (envelope.Data is not null &&
                    TickerReceived is { } tickerHandler)
                    await tickerHandler(category, envelope.Data,
                        envelope.Timestamp, cancellationToken);
                return;
            }
            if (topic.StartsWith("orderbook.",
                StringComparison.OrdinalIgnoreCase))
            {
                var envelope = Deserialize<ZoomexWsEnvelope<ZoomexOrderBook>>(
                    payload);
                if (envelope.Data is not null &&
                    BookReceived is { } bookHandler)
                    await bookHandler(category, envelope.Data, envelope.Topic,
                        envelope.Type, envelope.Timestamp, cancellationToken);
                return;
            }
            if (topic.StartsWith("publicTrade.",
                StringComparison.OrdinalIgnoreCase))
            {
                var envelope = Deserialize<ZoomexWsEnvelope<
                    ZoomexWsPublicTrade[]>>(payload);
                if (PublicTradesReceived is { } tradesHandler)
                    await tradesHandler(category, envelope.Data ?? [],
                        envelope.Timestamp, cancellationToken);
                return;
            }
            if (topic.StartsWith("kline.",
                StringComparison.OrdinalIgnoreCase))
            {
                var envelope = Deserialize<ZoomexWsEnvelope<
                    ZoomexWsCandle[]>>(payload);
                if (CandlesReceived is { } candleHandler)
                    await candleHandler(category, envelope.Topic,
                        envelope.Data ?? [], envelope.Timestamp,
                        cancellationToken);
                return;
            }
            throw new InvalidDataException(
                $"Zoomex returned unknown public topic '{topic}'.");
        }

        if (topic.StartsWith("order", StringComparison.OrdinalIgnoreCase))
        {
            var envelope = Deserialize<ZoomexWsEnvelope<ZoomexOrder[]>>(
                payload);
            if (OrdersReceived is { } orderHandler)
                await orderHandler(envelope.Data ?? [],
                    envelope.CreationTime, cancellationToken);
        }
        else if (topic.StartsWith("execution",
            StringComparison.OrdinalIgnoreCase))
        {
            var envelope = Deserialize<ZoomexWsEnvelope<ZoomexExecution[]>>(
                payload);
            if (ExecutionsReceived is { } executionHandler)
                await executionHandler(envelope.Data ?? [],
                    envelope.CreationTime, cancellationToken);
        }
        else if (topic.EqualsIgnoreCase("position"))
        {
            var envelope = Deserialize<ZoomexWsEnvelope<ZoomexPosition[]>>(
                payload);
            if (PositionsReceived is { } positionHandler)
                await positionHandler(envelope.Data ?? [],
                    envelope.CreationTime, cancellationToken);
        }
        else if (topic.EqualsIgnoreCase("wallet"))
        {
            var envelope = Deserialize<ZoomexWsEnvelope<
                ZoomexWalletAccount[]>>(payload);
            if (WalletsReceived is { } walletHandler)
                await walletHandler(envelope.Data ?? [],
                    envelope.CreationTime, cancellationToken);
        }
        else
        {
            throw new InvalidDataException(
                $"Zoomex returned unknown private topic '{topic}'.");
        }
    }

    private TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
            throw new InvalidDataException(
                "Zoomex WebSocket returned an empty JSON value.");

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler
            ? handler(error, cancellationToken)
            : default;

    private string NextRequestId()
        => Interlocked.Increment(ref _requestId).ToString(
            CultureInfo.InvariantCulture);

    private static string ValidateTopic(string topic)
    {
        topic = topic.ThrowIfEmpty(nameof(topic)).Trim();
        if (topic.Length > 256 || topic.Any(static ch =>
            !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')))
            throw new ArgumentException(
                $"Invalid Zoomex WebSocket topic '{topic}'.", nameof(topic));
        return topic;
    }

    private static TaskCompletionSource<bool> CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Zoomex WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
