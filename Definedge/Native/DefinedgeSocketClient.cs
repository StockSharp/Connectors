namespace StockSharp.Definedge.Native;

sealed class DefinedgeSocketClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings =
        new()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

    private readonly WebSocketClient _client;
    private readonly string _userId;
    private readonly string _accountId;
    private readonly string _webSocketToken;
    private readonly bool _subscribeOrders;
    private readonly SynchronizedDictionary<string, bool>
        _subscriptions =
            new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<bool> _loginCompletion;

    public DefinedgeSocketClient(
        Uri address,
        string userId,
        string accountId,
        string webSocketToken,
        bool subscribeOrders,
        int reconnectAttempts,
        WorkingTime workingTime)
    {
        _userId = userId.ThrowIfEmpty(nameof(userId));
        _accountId =
            accountId.ThrowIfEmpty(nameof(accountId));
        _webSocketToken =
            webSocketToken.ThrowIfEmpty(nameof(webSocketToken));
        _subscribeOrders = subscribeOrders;

        _client = new(
            (address ??
                throw new ArgumentNullException(nameof(address)))
                .AbsoluteUri,
            (state, cancellationToken) =>
                StateChanged is { } stateHandler
                    ? stateHandler(state, cancellationToken)
                    : default,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler(error, cancellationToken)
                    : default,
            Process,
            (message, args) =>
                this.AddInfoLog(message, args),
            (message, args) =>
                this.AddErrorLog(message, args),
            (message, args) =>
                this.AddVerboseLog(message, args))
        {
            ReconnectAttempts = Math.Max(0, reconnectAttempts),
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
        _client.PostConnect += OnPostConnect;
    }

    public override string Name =>
        nameof(Definedge) + "_" +
        nameof(DefinedgeSocketClient);

    public event Func<JObject, CancellationToken, ValueTask>
        MarketDataReceived;
    public event Func<DefinedgeOrder, CancellationToken, ValueTask>
        OrderReceived;
    public event Func<Exception, CancellationToken, ValueTask>
        Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _client.PostConnect -= OnPostConnect;
        _client.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask Connect(
        CancellationToken cancellationToken)
    {
        _loginCompletion = CreateCompletion();
        await _client.ConnectAsync(cancellationToken);
        await _loginCompletion.Task.WaitAsync(cancellationToken);
    }

    public ValueTask Disconnect(
        CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public ValueTask SendHeartbeat(
        CancellationToken cancellationToken)
        => Send(
            new DefinedgeSocketHeartbeat(),
            cancellationToken);

    public async ValueTask Subscribe(
        string instrumentKey,
        bool isDepth,
        CancellationToken cancellationToken)
    {
        instrumentKey.ParseInstrumentKey();
        if (_subscriptions.TryGetValue(
            instrumentKey, out var previous))
        {
            if (previous == isDepth)
                return;
            await SendSubscription(
                instrumentKey,
                previous,
                false,
                cancellationToken);
        }
        else if (_subscriptions.Count >= 500)
        {
            throw new InvalidOperationException(
                "Definedge allows at most 500 instrument tokens per WebSocket connection.");
        }

        _subscriptions[instrumentKey] = isDepth;
        await SendSubscription(
            instrumentKey,
            isDepth,
            true,
            cancellationToken);
    }

    public async ValueTask Unsubscribe(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (!_subscriptions.TryGetAndRemove(
            instrumentKey, out var isDepth))
        {
            return;
        }
        await SendSubscription(
            instrumentKey,
            isDepth,
            false,
            cancellationToken);
    }

    private async ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        if (reconnect ||
            _loginCompletion == null ||
            _loginCompletion.Task.IsCompleted)
        {
            _loginCompletion = CreateCompletion();
        }

        await Send(new DefinedgeSocketLoginRequest
        {
            UserId = _userId,
            AccountId = _accountId,
            WebSocketToken = _webSocketToken,
        }, cancellationToken);
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
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Definedge returned an invalid WebSocket message.",
                ex);
        }

        var type = root.GetText("t")?.ToLowerInvariant();
        switch (type)
        {
            case "ck":
                if (!root.GetText("s").EqualsIgnoreCase("OK"))
                {
                    var error = new InvalidOperationException(
                        $"Definedge WebSocket login failed: {root.GetText("emsg", "message").IsEmpty(root.GetText("s"))}");
                    _loginCompletion?.TrySetException(error);
                    throw error;
                }

                if (_subscribeOrders)
                {
                    await Send(
                        new DefinedgeSocketOrderRequest
                        {
                            Type = "o",
                            AccountId = _accountId,
                        },
                        cancellationToken);
                }
                foreach (var subscription in
                    _subscriptions.ToArray())
                {
                    await SendSubscription(
                        subscription.Key,
                        subscription.Value,
                        true,
                        cancellationToken);
                }
                _loginCompletion?.TrySetResult(true);
                break;

            case "tk":
            case "tf":
            case "dk":
            case "df":
                if (!root.GetText("e").IsEmpty() &&
                    !root.GetText("tk").IsEmpty() &&
                    MarketDataReceived is { } marketHandler)
                {
                    await marketHandler(root, cancellationToken);
                }
                break;

            case "om":
                var order = NormalizeOrderUpdate(root);
                if (!order.OrderId.IsEmpty() &&
                    OrderReceived is { } orderHandler)
                {
                    await orderHandler(order, cancellationToken);
                }
                break;

            case "ok":
            case "h":
                break;

            default:
                var errorMessage =
                    root.GetText("emsg", "error");
                if (!errorMessage.IsEmpty())
                {
                    throw new InvalidOperationException(
                        $"Definedge WebSocket error: {errorMessage}");
                }
                this.AddVerboseLog(
                    "Ignored Definedge WebSocket message type {0}.",
                    type);
                break;
        }
    }

    internal static DefinedgeOrder NormalizeOrderUpdate(
        JObject root)
    {
        var normalized = (JObject)root.DeepClone();
        CopyAlias(normalized, "order_id", "norenordno");
        CopyAlias(normalized, "exchange", "exch");
        CopyAlias(normalized, "tradingsymbol", "tsym");
        CopyAlias(normalized, "quantity", "qty");
        CopyAlias(normalized, "price", "prc");
        CopyAlias(normalized, "product_type", "prd");
        CopyAlias(normalized, "order_status", "status");
        CopyAlias(normalized, "order_type", "trantype");
        CopyAlias(normalized, "price_type", "prctyp");
        CopyAlias(normalized, "validity", "ret");
        CopyAlias(normalized, "filled_qty", "fillshares");
        CopyAlias(
            normalized,
            "average_traded_price",
            "avgprc");
        CopyAlias(normalized, "fill_time", "fltm");
        CopyAlias(normalized, "fill_id", "flid");
        CopyAlias(normalized, "last_fill_qty", "flqty");
        CopyAlias(normalized, "fill_price", "flprc");
        CopyAlias(
            normalized,
            "rejection_reason",
            "rejreason");
        CopyAlias(
            normalized,
            "exchange_orderid",
            "exchordid");
        CopyAlias(
            normalized,
            "cancel_qty",
            "cancelqty");
        CopyAlias(
            normalized,
            "disclosed_quantity",
            "dscqty");
        CopyAlias(normalized, "trigger_price", "trgprc");
        CopyAlias(normalized, "exchange_time", "exch_tm");
        CopyAlias(normalized, "account_id", "actid");
        return normalized.ToObject<DefinedgeOrder>() ?? new();
    }

    private ValueTask SendSubscription(
        string instrumentKey,
        bool isDepth,
        bool subscribe,
        CancellationToken cancellationToken)
        => Send(new DefinedgeSocketSubscriptionRequest
        {
            Type = subscribe
                ? isDepth ? "d" : "t"
                : isDepth ? "ud" : "u",
            Instruments = instrumentKey,
        }, cancellationToken);

    private ValueTask Send<T>(
        T request,
        CancellationToken cancellationToken)
        where T : class
        => _client.SendAsync(
            JsonConvert.SerializeObject(
                request,
                Formatting.None,
                _jsonSettings),
            cancellationToken);

    private static void CopyAlias(
        JObject value,
        string destination,
        string source)
    {
        if (value.GetValue(
            destination,
            StringComparison.OrdinalIgnoreCase) == null &&
            value.GetValue(
                source,
                StringComparison.OrdinalIgnoreCase) is { } token)
        {
            value[destination] = token.DeepClone();
        }
    }

    private static TaskCompletionSource<bool>
        CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
