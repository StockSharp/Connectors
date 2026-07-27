namespace StockSharp.Primary.Native;

readonly record struct PrimarySocketSubscription(
    string Market,
    string Symbol,
    string Entries,
    int Depth);

sealed class PrimarySocketClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly PrimaryRestClient _rest;
    private readonly int _marketDataLevel;
    private readonly WebSocketClient _client;
    private readonly SynchronizedSet<PrimarySocketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedSet<string> _orderSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _isConnected;

    public PrimarySocketClient(
        Uri endpoint,
        PrimaryRestClient rest,
        int marketDataLevel,
        int reconnectAttempts,
        WorkingTime workingTime)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException(
                "Primary WebSocket endpoint must be an absolute WS or WSS URI.",
                nameof(endpoint));
        }

        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
        if (marketDataLevel is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(marketDataLevel),
                marketDataLevel,
                "Primary market-data level must be between 1 and 5.");
        }
        _marketDataLevel = marketDataLevel;
        _client = new(
            endpoint.AbsoluteUri,
            OnStateChanged,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler(error, cancellationToken)
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

    public override string Name => "Primary_WebSocket";

    public event Func<PrimaryMarketUpdate, CancellationToken, ValueTask>
        MarketDataReceived;
    public event Func<PrimaryOrder, CancellationToken, ValueTask>
        OrderReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    public ValueTask Connect(CancellationToken cancellationToken)
        => _client.ConnectAsync(cancellationToken);

    public ValueTask Disconnect(CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public async ValueTask SubscribeMarket(
        PrimarySecurityKey security,
        IEnumerable<string> entries,
        int depth,
        CancellationToken cancellationToken)
    {
        var subscription = Normalize(security, entries, depth);
        if (!_marketSubscriptions.TryAdd(subscription))
            return;
        try
        {
            if (_isConnected)
            {
                await SendMarketSubscription(
                    subscription, cancellationToken);
            }
        }
        catch
        {
            _marketSubscriptions.Remove(subscription);
            throw;
        }
    }

    public ValueTask UnsubscribeMarket(
        PrimarySecurityKey security,
        IEnumerable<string> entries,
        int depth)
    {
        _marketSubscriptions.Remove(
            Normalize(security, entries, depth));
        return default;
    }

    public async ValueTask SubscribeOrders(
        string account,
        CancellationToken cancellationToken)
    {
        account.ThrowIfEmpty(nameof(account));
        account = account.Trim();
        if (!_orderSubscriptions.TryAdd(account))
            return;
        try
        {
            if (_isConnected)
            {
                await SendOrderSubscription(
                    account, cancellationToken);
            }
        }
        catch
        {
            _orderSubscriptions.Remove(account);
            throw;
        }
    }

    protected override void DisposeManaged()
    {
        _client.InitAsync -= OnInit;
        _client.PostConnect -= OnPostConnect;
        _client.Dispose();
        base.DisposeManaged();
    }

    private ValueTask OnInit(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var token = _rest.Token.ThrowIfEmpty(nameof(_rest.Token));
        socket.Options.SetRequestHeader("X-Auth-Token", token);
        socket.Options.SetRequestHeader(
            "User-Agent", "StockSharp-Primary/1.0");
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
        return default;
    }

    private async ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        _isConnected = true;
        if (!reconnect)
            return;

        foreach (var subscription in _marketSubscriptions.ToArray())
        {
            await SendMarketSubscription(
                subscription, cancellationToken);
        }
        foreach (var account in _orderSubscriptions.ToArray())
        {
            await SendOrderSubscription(account, cancellationToken);
        }
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
                    "Primary returned invalid WebSocket JSON.", error),
                cancellationToken);
            return;
        }

        if (root.Value<string>("status").EqualsIgnoreCase("ERROR"))
        {
            await RaiseError(
                new InvalidOperationException(
                    "Primary WebSocket: " +
                    root.Value<string>("description")
                        .IsEmpty(root.Value<string>("message"))
                        .IsEmpty("Unknown API error.")),
                cancellationToken);
            return;
        }

        switch (root.Value<string>("type")?.Trim().ToUpperInvariant())
        {
            case "MD":
                if (MarketDataReceived is { } marketHandler)
                {
                    var update = root.ToObject<PrimaryMarketUpdate>(
                        JsonSerializer.Create(_jsonSettings));
                    if (update is not null)
                    {
                        await marketHandler(update, cancellationToken);
                    }
                }
                break;

            case "OR":
                if (OrderReceived is { } orderHandler)
                {
                    var update = root.ToObject<PrimaryOrderUpdate>(
                        JsonSerializer.Create(_jsonSettings));
                    if (update?.Order is not null)
                    {
                        await orderHandler(
                            update.Order, cancellationToken);
                    }
                }
                break;

            default:
                this.AddVerboseLog(
                    "Primary ignored WebSocket message: {0}", text);
                break;
        }
    }

    private ValueTask SendMarketSubscription(
        PrimarySocketSubscription subscription,
        CancellationToken cancellationToken)
        => Send(
            CreateMarketSubscriptionRequest(
                subscription, _marketDataLevel),
            cancellationToken);

    private ValueTask SendOrderSubscription(
        string account,
        CancellationToken cancellationToken)
        => Send(
            CreateOrderSubscriptionRequest(account),
            cancellationToken);

    private ValueTask Send(
        JObject request,
        CancellationToken cancellationToken)
        => _client.SendAsync(
            request.ToString(Formatting.None), cancellationToken);

    private ValueTask RaiseError(
        Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler
            ? handler(error, cancellationToken)
            : default;

    private ValueTask OnStateChanged(
        ConnectionStates state,
        CancellationToken cancellationToken)
    {
        _isConnected = state == ConnectionStates.Connected;
        return StateChanged is { } handler
            ? handler(state, cancellationToken)
            : default;
    }

    private static PrimarySocketSubscription Normalize(
        PrimarySecurityKey security,
        IEnumerable<string> entries,
        int depth)
    {
        var normalizedEntries = (entries ?? [])
            .Where(entry => !entry.IsEmpty())
            .Select(entry => entry.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();
        if (normalizedEntries.Length == 0)
        {
            throw new ArgumentException(
                "At least one Primary market-data entry is required.",
                nameof(entries));
        }
        return new(
            security.Market.IsEmpty("ROFX")
                .Trim().ToUpperInvariant(),
            security.Symbol.ThrowIfEmpty(nameof(security.Symbol)).Trim(),
            string.Join(',', normalizedEntries),
            Math.Max(1, depth));
    }

    internal static JObject CreateMarketSubscriptionRequest(
        PrimarySocketSubscription subscription,
        int level)
    {
        if (level is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level), level,
                "Primary market-data level must be between 1 and 5.");
        }
        return new()
        {
            ["type"] = "smd",
            ["level"] = level,
            ["depth"] = subscription.Depth,
            ["entries"] = new JArray(
                subscription.Entries.Split(',')),
            ["products"] = new JArray(
                new JObject
                {
                    ["symbol"] = subscription.Symbol,
                    ["marketId"] = subscription.Market,
                }),
        };
    }

    internal static JObject CreateOrderSubscriptionRequest(string account)
        => new()
        {
            ["type"] = "os",
            ["account"] = new JObject
            {
                ["id"] = account.ThrowIfEmpty(nameof(account)),
            },
            ["snapshotOnlyActive"] = true,
        };
}
