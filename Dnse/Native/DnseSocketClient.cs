namespace StockSharp.Dnse.Native;

readonly record struct DnseSocketSubscription(
    string Channel,
    string Symbol);

sealed class DnseSocketClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly WebSocketClient _client;
    private readonly SynchronizedSet<DnseSocketSubscription>
        _subscriptions = [];
    private TaskCompletionSource<string> _authentication;
    private bool _isAuthenticated;

    public DnseSocketClient(
        Uri endpoint,
        SecureString apiKey,
        SecureString apiSecret,
        int reconnectAttempts,
        WorkingTime workingTime)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException(
                "DNSE WebSocket endpoint must be an absolute WS or WSS URI.",
                nameof(endpoint));
        }

        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
        _apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret)).UnSecure();
        _client = new(
            endpoint.AbsoluteUri,
            (state, cancellationToken) =>
                StateChanged is { } stateHandler
                    ? stateHandler.InvokeAsync(state, cancellationToken)
                    : default,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler.InvokeAsync(error, cancellationToken)
                    : default,
            Process,
            (s, a) => this.AddInfoLog(s, a),
            (s, a) => this.AddErrorLog(s, a),
            (s, a) => this.AddVerboseLog(s, a))
        {
            ReconnectAttempts = Math.Max(1, reconnectAttempts),
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
        _client.InitAsync += OnInit;
        _client.PostConnect += OnPostConnect;
    }

    public override string Name => "DNSE_WebSocket";

    public event Func<DnseSecurityDefinition, CancellationToken, ValueTask>
        SecurityDefinitionReceived;
    public event Func<DnseTrade, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<DnseQuote, CancellationToken, ValueTask>
        QuoteReceived;
    public event Func<DnseCandle, bool, CancellationToken, ValueTask>
        CandleReceived;
    public event Func<DnseOrder, CancellationToken, ValueTask>
        OrderReceived;
    public event Func<DnsePosition, CancellationToken, ValueTask>
        PositionReceived;
    public event Func<DnseAccountUpdate, CancellationToken, ValueTask>
        AccountReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    public async ValueTask<string> Connect(
        CancellationToken cancellationToken)
    {
        _authentication = CreateCompletion();
        await _client.ConnectAsync(cancellationToken);
        return await _authentication.Task.WaitAsync(cancellationToken);
    }

    public ValueTask Disconnect(CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public async ValueTask Subscribe(
        string channel,
        string symbol,
        CancellationToken cancellationToken)
    {
        var subscription = Normalize(channel, symbol);
        if (!_subscriptions.TryAdd(subscription))
            return;
        try
        {
            if (_isAuthenticated)
                await SendSubscription(subscription, true, cancellationToken);
        }
        catch
        {
            _subscriptions.Remove(subscription);
            throw;
        }
    }

    public async ValueTask Unsubscribe(
        string channel,
        string symbol,
        CancellationToken cancellationToken)
    {
        var subscription = Normalize(channel, symbol);
        if (!_subscriptions.Remove(subscription))
            return;
        if (_isAuthenticated)
            await SendSubscription(subscription, false, cancellationToken);
    }

    public ValueTask Ping(CancellationToken cancellationToken)
        => Send(
            new JObject { ["action"] = "ping" },
            cancellationToken);

    protected override void DisposeManaged()
    {
        _client.InitAsync -= OnInit;
        _client.PostConnect -= OnPostConnect;
        _client.Dispose();
        _authentication?.TrySetCanceled();
        base.DisposeManaged();
    }

    internal static string CreateAuthenticationSignature(
        string apiKey,
        string apiSecret,
        long timestamp,
        string nonce)
    {
        apiKey.ThrowIfEmpty(nameof(apiKey));
        apiSecret.ThrowIfEmpty(nameof(apiSecret));
        nonce.ThrowIfEmpty(nameof(nonce));

        var message = $"{apiKey}:{timestamp}:{nonce}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(apiSecret),
            Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private ValueTask OnInit(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        socket.Options.SetRequestHeader(
            "User-Agent", "StockSharp-DNSE/1.0");
        return default;
    }

    private ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        _isAuthenticated = false;
        if (reconnect ||
            _authentication is null ||
            _authentication.Task.IsCompleted)
        {
            _authentication = CreateCompletion();
        }
        return default;
    }

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var text = message.AsString()?.Trim();
        if (text.IsEmpty())
            return;

        JObject root;
        try
        {
            root = JObject.Parse(text);
        }
        catch (JsonException error)
        {
            await RaiseError(
                new InvalidDataException(
                    "DNSE returned invalid WebSocket JSON.", error),
                cancellationToken);
            return;
        }

        var action = root.Value<string>("action") ??
            root.Value<string>("a");
        if (action.EqualsIgnoreCase("ping"))
        {
            await Send(
                new JObject { ["action"] = "pong" },
                cancellationToken);
            return;
        }
        if (action.EqualsIgnoreCase("pong") ||
            action.EqualsIgnoreCase("subscribed") ||
            action.EqualsIgnoreCase("unsubscribed"))
        {
            return;
        }
        if (action.EqualsIgnoreCase("auth_success"))
        {
            _isAuthenticated = true;

            foreach (var subscription in _subscriptions.ToArray())
            {
                await SendSubscription(
                    subscription, true, cancellationToken);
            }

            var sessionId =
                root.Value<string>("session_id") ??
                root.Value<string>("sid") ??
                "authenticated";
            _authentication?.TrySetResult(sessionId);
            return;
        }
        if (action.EqualsIgnoreCase("auth_error") ||
            action.EqualsIgnoreCase("error"))
        {
            var description =
                root.Value<string>("message") ??
                root.Value<string>("msg") ??
                "Unknown WebSocket error.";
            var error = new InvalidOperationException(
                $"DNSE WebSocket: {description}");
            if (!_isAuthenticated)
                _authentication?.TrySetException(error);
            await RaiseError(error, cancellationToken);
            return;
        }

        var session =
            root.Value<string>("session_id") ??
            root.Value<string>("sid");
        if (!_isAuthenticated &&
            (!session.IsEmpty() || action.EqualsIgnoreCase("welcome")))
        {
            await Authenticate(cancellationToken);
            return;
        }

        var type = root.Value<string>("T");
        switch (type)
        {
            case "sd":
                await Raise(
                    SecurityDefinitionReceived,
                    Deserialize<DnseSecurityDefinition>(root),
                    cancellationToken);
                break;
            case "t":
                await Raise(
                    TradeReceived,
                    Deserialize<DnseTrade>(root),
                    cancellationToken);
                break;
            case "q":
                await Raise(
                    QuoteReceived,
                    Deserialize<DnseQuote>(root),
                    cancellationToken);
                break;
            case "b":
            case "bc":
                var candle = Deserialize<DnseCandle>(root);
                if (candle is not null &&
                    CandleReceived is { } candleHandler)
                {
                    await candleHandler.InvokeAsync(
                        candle,
                        type == "bc",
                        cancellationToken);
                }
                break;
            case "do":
            case "eo":
                await Raise(
                    OrderReceived,
                    Deserialize<DnseOrder>(
                        root["order"] as JObject ?? root),
                    cancellationToken);
                break;
            case "dp":
            case "ep":
                await Raise(
                    PositionReceived,
                    Deserialize<DnsePosition>(
                        root["position"] as JObject ?? root),
                    cancellationToken);
                break;
            case "a":
                await Raise(
                    AccountReceived,
                    Deserialize<DnseAccountUpdate>(root),
                    cancellationToken);
                break;
            default:
                this.AddVerboseLog(
                    "DNSE ignored WebSocket message type '{0}'.",
                    type.IsEmpty(action));
                break;
        }
    }

    private ValueTask Authenticate(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds();
        var nonce = checked(
            now.ToUnixTimeMilliseconds() * 1000)
            .ToString(CultureInfo.InvariantCulture);
        return Send(
            new JObject
            {
                ["action"] = "auth",
                ["api_key"] = _apiKey,
                ["signature"] = CreateAuthenticationSignature(
                    _apiKey, _apiSecret, timestamp, nonce),
                ["timestamp"] = timestamp,
                ["nonce"] = nonce,
            },
            cancellationToken);
    }

    private ValueTask SendSubscription(
        DnseSocketSubscription subscription,
        bool subscribe,
        CancellationToken cancellationToken)
    {
        var channel = new JObject
        {
            ["name"] = subscription.Channel,
            ["symbols"] = subscription.Symbol.IsEmpty()
                ? new JArray()
                : new JArray(subscription.Symbol),
        };
        return Send(
            new JObject
            {
                ["action"] = subscribe
                    ? "subscribe"
                    : "unsubscribe",
                ["channels"] = new JArray(channel),
            },
            cancellationToken);
    }

    private ValueTask Send(
        JObject request,
        CancellationToken cancellationToken)
        => _client.SendAsync(
            request.ToString(Formatting.None),
            cancellationToken);

    private ValueTask RaiseError(
        Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler
            ? handler.InvokeAsync(error, cancellationToken)
            : default;

    private static ValueTask Raise<T>(
        Func<T, CancellationToken, ValueTask> handler,
        T value,
        CancellationToken cancellationToken)
        where T : class
        => value is not null && handler is not null
            ? handler.InvokeAsync(value, cancellationToken)
            : default;

    private static T Deserialize<T>(JObject value)
    {
        if (value is null)
            return default;
        return value.ToObject<T>(
            JsonSerializer.Create(_jsonSettings));
    }

    private static DnseSocketSubscription Normalize(
        string channel,
        string symbol)
        => new(
            channel.ThrowIfEmpty(nameof(channel)).Trim(),
            symbol?.Trim().ToUpperInvariant());

    private static TaskCompletionSource<string> CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
