namespace StockSharp.Firstock.Native;

sealed class FirstockSocketClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly WebSocketClient _client;
    private readonly SynchronizedSet<string> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<bool> _authenticationCompletion;

    public FirstockSocketClient(
        string userId,
        string sessionToken,
        int reconnectAttempts,
        WorkingTime workingTime,
        Uri webSocketAddress)
    {
        userId.ThrowIfEmpty(nameof(userId));
        sessionToken.ThrowIfEmpty(nameof(sessionToken));
        if (reconnectAttempts < 0)
            throw new ArgumentOutOfRangeException(
                nameof(reconnectAttempts), reconnectAttempts, "Reconnect attempts cannot be negative.");

        var address = BuildAddress(webSocketAddress, userId, sessionToken);
        _client = new(
            address,
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
            ReconnectAttempts = reconnectAttempts,
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
        _client.InitAsync += OnInit;
        _client.PostConnect += OnPostConnect;
    }

    public override string Name => nameof(Firstock) + "_" + nameof(FirstockSocketClient);

    public event Func<FirstockMarketUpdate, CancellationToken, ValueTask> MarketDataReceived;
    public event Func<FirstockOrder, CancellationToken, ValueTask> OrderReceived;
    public event Func<FirstockPosition, CancellationToken, ValueTask> PositionReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _client.InitAsync -= OnInit;
        _client.PostConnect -= OnPostConnect;
        _client.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask Connect(CancellationToken cancellationToken)
    {
        _authenticationCompletion = CreateCompletion();
        await _client.ConnectAsync(cancellationToken);
        await _authenticationCompletion.Task.WaitAsync(cancellationToken);
    }

    public ValueTask Disconnect(CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public async ValueTask Subscribe(string instrumentKey, CancellationToken cancellationToken)
    {
        instrumentKey.ParseInstrumentKey();
        if (!_subscriptions.TryAdd(instrumentKey))
            return;
        await SendSubscription("subscribe", instrumentKey, cancellationToken);
    }

    public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
    {
        if (!_subscriptions.Remove(instrumentKey))
            return;
        await SendSubscription("unsubscribe", instrumentKey, cancellationToken);
    }

    private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
    {
        if (reconnect || _authenticationCompletion == null ||
            _authenticationCompletion.Task.IsCompleted)
            _authenticationCompletion = CreateCompletion();
        return default;
    }

    private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
    {
        var text = message.AsString()?.Trim();
        if (text.IsEmpty())
            return;

        JObject root;
        try
        {
            root = JObject.Parse(text);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException("Firstock returned invalid WebSocket JSON.", ex);
        }

        var status = root.GetText("status");
        if (!status.IsEmpty())
        {
            var responseMessage = root.GetText("message");
            if (!status.EqualsIgnoreCase("success"))
            {
                var error = new InvalidOperationException(
                    $"Firstock WebSocket authentication failed: {responseMessage.IsEmpty(status)}");
                _authenticationCompletion?.TrySetException(error);
                throw error;
            }

            if (responseMessage?.Contains(
                "Authentication successful",
                StringComparison.OrdinalIgnoreCase) == true)
            {
                foreach (var instrumentKey in _subscriptions.ToArray())
                    await SendSubscription("subscribe", instrumentKey, cancellationToken);
                _authenticationCompletion?.TrySetResult(true);
            }
            else
                this.AddVerboseLog("Firstock WebSocket acknowledgement: {0}.", responseMessage);
            return;
        }

        if (root.GetValueIgnoreCase("norenordno") != null)
        {
            var order = ParseOrder(root);
            if (!order.OrderId.IsEmpty() && OrderReceived is { } orderHandler)
                await orderHandler.InvokeAsync(order, cancellationToken);
            return;
        }

        if (root.GetValueIgnoreCase("brkname") != null)
        {
            var position = ParsePosition(root);
            if (PositionReceived is { } positionHandler)
                await positionHandler.InvokeAsync(position, cancellationToken);
            return;
        }

        var updates = ParseMarketFeeds(root);
        if (updates.Length > 0)
        {
            if (MarketDataReceived is { } marketHandler)
            {
                foreach (var update in updates)
                    await marketHandler.InvokeAsync(update, cancellationToken);
            }
            return;
        }

        if (root.GetValueIgnoreCase("message") is { } errorToken)
            throw new InvalidOperationException($"Firstock WebSocket error: {errorToken}");

        this.AddVerboseLog("Ignored Firstock WebSocket payload.");
    }

    internal static FirstockMarketUpdate[] ParseMarketFeeds(JObject root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        var result = new List<FirstockMarketUpdate>();
        foreach (var property in root.Properties())
        {
            if (property.Value is not JObject data || !property.Name.Contains(':'))
                continue;

            string keyExchange = null;
            string keyToken = null;
            try
            {
                (keyExchange, keyToken) = property.Name.ParseInstrumentKey();
            }
            catch (FormatException)
            {
                continue;
            }

            var exchange = data.GetText("c_exch_seg").IsEmpty(keyExchange);
            var token = data.GetText("c_symbol").IsEmpty(keyToken);
            if (exchange.IsEmpty() || token.IsEmpty())
                continue;

            var feedSeconds = data.GetLong("i_feed_time");
            var feedText = data.GetText("c_exch_feed_time");
            var lastTradeSeconds = data.GetLong("i_last_trade_time");
            result.Add(new()
            {
                Exchange = exchange.ToBoardCode(),
                Token = token,
                TradingSymbol = data.GetText("tradingSymbol", "tsym"),
                ServerTime = feedSeconds is > 0
                    ? feedSeconds.Value.FromUnixSeconds()
                    : feedText.ToFirstockTime() ?? DateTime.UtcNow,
                LastTradeTime = lastTradeSeconds is > 0
                    ? lastTradeSeconds.Value.FromUnixSeconds()
                    : null,
                LastPrice = data.GetDecimal("i_last_traded_price"),
                LastQuantity = data.GetDecimal("i_last_trade_quantity"),
                Volume = data.GetDecimal("i_volume_traded_today"),
                AveragePrice = data.GetDecimal("i_average_trade_price"),
                Open = data.GetDecimal("i_open_price"),
                High = data.GetDecimal("i_high_price"),
                Low = data.GetDecimal("i_low_price"),
                Close = data.GetDecimal("i_closing_price", "close_price"),
                OpenInterest = data.GetDecimal("i_open_interest", "i_total_open_interest"),
                TotalBuyQuantity = data.GetDecimal("i_total_buy_quantity"),
                TotalSellQuantity = data.GetDecimal("i_total_sell_quantity"),
                LowerCircuit = data.GetDecimal("i_lower_circuit_limit"),
                UpperCircuit = data.GetDecimal("i_upper_circuit_limit"),
                YearHigh = data.GetDecimal("i_yearly_high_price"),
                YearLow = data.GetDecimal("i_yearly_low_price"),
                Bids = ParseDepth(data.GetValueIgnoreCase("best_buy")),
                Asks = ParseDepth(data.GetValueIgnoreCase("best_sell")),
            });
        }
        return [.. result];
    }

    internal static FirstockOrder ParseOrder(JObject root)
        => root?.ToObject<FirstockOrder>(JsonSerializer.Create(_jsonSettings))
            ?? throw new ArgumentNullException(nameof(root));

    internal static FirstockPosition ParsePosition(JObject root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        var child = root.GetValueIgnoreCase("child_orders") as JArray;
        var item = child?.OfType<JObject>().FirstOrDefault();
        return new()
        {
            Exchange = (item?.GetText("exch")).IsEmpty(root.GetText("exch")),
            Token = (item?.GetText("token")).IsEmpty(root.GetText("token")),
            TradingSymbol = (item?.GetText("tsym")).IsEmpty(root.GetText("tsym")),
            Product = root.GetText("pcode"),
            NetQuantity = root.GetText("netqty"),
            NetAveragePrice = root.GetText("buyavgprc", "totbuyavgprc"),
            RealizedPnL = root.GetText("rpnl"),
        };
    }

    private static FirstockDepthLevel[] ParseDepth(JToken token)
    {
        if (token is not JArray array)
            return [];
        return
        [
            .. array
                .OfType<JObject>()
                .Select(item => new FirstockDepthLevel
                {
                    Price = item.GetDecimal("price") ?? 0m,
                    Quantity = item.GetDecimal("quantity") ?? 0m,
                    Orders = Convert.ToInt32(item.GetLong("orders") ?? 0),
                })
                .Where(level => level.Price > 0),
        ];
    }

    private ValueTask SendSubscription(
        string action,
        string instrumentKey,
        CancellationToken cancellationToken)
        => Send(new JObject
        {
            ["action"] = action,
            ["tokens"] = instrumentKey,
        }, cancellationToken);

    private ValueTask Send(JObject request, CancellationToken cancellationToken)
        => _client.SendAsync(request.ToString(Formatting.None), cancellationToken);

    private ValueTask OnInit(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        socket.Options.SetRequestHeader("Origin", "https://firstock.in");
        socket.Options.SetRequestHeader("User-Agent", "StockSharp-Firstock/1.0");
        return default;
    }

    private static string BuildAddress(Uri address, string userId, string sessionToken)
    {
        if (address == null)
            throw new ArgumentNullException(nameof(address));
        var builder = new UriBuilder(address);
        var authentication =
            $"userId={Uri.EscapeDataString(userId)}&jKey={Uri.EscapeDataString(sessionToken)}&source=developer-api";
        builder.Query = builder.Query.IsEmpty()
            ? authentication
            : $"{builder.Query.TrimStart('?')}&{authentication}";
        return builder.Uri.ToString();
    }

    private static TaskCompletionSource<bool> CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
