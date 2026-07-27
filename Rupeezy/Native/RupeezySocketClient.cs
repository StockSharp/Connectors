namespace StockSharp.Rupeezy.Native;

sealed class RupeezySocketClient : BaseLogReceiver
{
    private const int _subscriptionLimit = 1000;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly WebSocketClient _client;
    private readonly SynchronizedSet<string> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);

    public RupeezySocketClient(
        string accessToken,
        int reconnectAttempts,
        WorkingTime workingTime,
        Uri webSocketAddress)
    {
        accessToken.ThrowIfEmpty(nameof(accessToken));
        if (reconnectAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectAttempts),
                reconnectAttempts,
                "Reconnect attempts cannot be negative.");
        }

        var address = webSocketAddress?.ToString()
            ?? throw new ArgumentNullException(nameof(webSocketAddress));
        address += address.Contains('?', StringComparison.Ordinal)
            ? "&"
            : "?";
        address += "auth_token=" + Uri.EscapeDataString(accessToken);

        _client = new(
            address,
            (state, cancellationToken) =>
                StateChanged is { } stateHandler
                    ? stateHandler(state, cancellationToken)
                    : default,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler(error, cancellationToken)
                    : default,
            Process,
            (message, args) => this.AddInfoLog(message, args),
            (message, args) => this.AddErrorLog(message, args),
            (message, args) => this.AddVerboseLog(message, args))
        {
            ReconnectAttempts = reconnectAttempts,
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
        _client.PostConnect += OnPostConnect;
    }

    public override string Name => nameof(Rupeezy) + "_" + nameof(RupeezySocketClient);

    public event Func<RupeezyMarketTick, CancellationToken, ValueTask> TickReceived;
    public event Func<RupeezySocketUpdate, CancellationToken, ValueTask> OrderReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _client.PostConnect -= OnPostConnect;
        _client.Dispose();
        base.DisposeManaged();
    }

    public ValueTask Connect(CancellationToken cancellationToken)
        => _client.ConnectAsync(cancellationToken);

    public ValueTask Disconnect(CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public async ValueTask SetSubscription(
        string instrumentKey,
        bool isSubscribe,
        CancellationToken cancellationToken)
    {
        var (exchange, token) = instrumentKey.ParseInstrumentKey();
        if (isSubscribe)
        {
            if (_subscriptions.Contains(instrumentKey))
                return;
            if (_subscriptions.Count >= _subscriptionLimit)
            {
                throw new InvalidOperationException(
                    $"Rupeezy allows at most {_subscriptionLimit} instruments per WebSocket connection.");
            }
            _subscriptions.Add(instrumentKey);
            await SendSubscription(exchange, token, true, cancellationToken);
        }
        else if (_subscriptions.Remove(instrumentKey))
            await SendSubscription(exchange, token, false, cancellationToken);
    }

    private async ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        foreach (var instrumentKey in _subscriptions.ToArray())
        {
            var (exchange, token) = instrumentKey.ParseInstrumentKey();
            await SendSubscription(exchange, token, true, cancellationToken);
        }
    }

    private ValueTask SendSubscription(
        string exchange,
        string token,
        bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
            token,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var numericToken))
        {
            throw new FormatException(
                $"Rupeezy token '{token}' is not numeric.");
        }

        var payload = new JObject
        {
            ["exchange"] = exchange,
            ["token"] = numericToken,
            ["message_type"] = isSubscribe ? "subscribe" : "unsubscribe",
        };
        if (isSubscribe)
            payload["mode"] = "full";
        return _client.SendAsync(
            payload.ToString(Formatting.None),
            cancellationToken);
    }

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var data = message.Memory;
        if (data.IsEmpty)
            return;
        if (data.Span[0] is not ((byte)'{' or (byte)'['))
        {
            if (TickReceived is not { } tickHandler)
                return;
            foreach (var tick in Decode(data.Span))
                await tickHandler(tick, cancellationToken);
            return;
        }

        var text = message.AsString()?.Trim();
        if (text.IsEmpty() ||
            text.EqualsIgnoreCase("ping") ||
            text.EqualsIgnoreCase("pong"))
            return;

        var update = ParseText(text);
        if (update != null && OrderReceived is { } orderHandler)
            await orderHandler(update, cancellationToken);
    }

    internal static RupeezySocketUpdate ParseText(string text)
    {
        JObject root;
        try
        {
            root = JObject.Parse(text.ThrowIfEmpty(nameof(text)));
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                "Rupeezy returned invalid WebSocket JSON.",
                ex);
        }

        var type = root.GetText("type", "message_type");
        var payload = root.GetValueIgnoreCase("data") as JObject;
        if (type.IsEmpty() || payload == null)
            return null;
        if (type.EqualsIgnoreCase("error"))
        {
            throw new InvalidOperationException(
                $"Rupeezy WebSocket error: {root.GetText("message").IsEmpty(text)}");
        }
        if (type.ToLowerInvariant() is not (
            "order" or
            "trade" or
            "sl_trigger" or
            "gtt_order" or
            "position_conversion"))
            return null;

        var serializer = JsonSerializer.Create(_jsonSettings);
        return new()
        {
            Type = type,
            Order = payload.ToObject<RupeezyOrder>(serializer),
            Trade = type.EqualsIgnoreCase("trade")
                ? payload.ToObject<RupeezyTrade>(serializer)
                : null,
            ClientCode = root.GetText("client_code"),
        };
    }

    internal static RupeezyMarketTick[] Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return [];

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var offset = 2;
        var result = new List<RupeezyMarketTick>(count);
        for (var index = 0; index < count; index++)
        {
            Ensure(data, offset + 2, "packet length");
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;
            Ensure(data, offset + length, "packet");
            var packet = data.Slice(offset, length);
            offset += length;

            if (length is not (22 or 62 or 266))
            {
                throw new InvalidDataException(
                    $"Rupeezy returned unsupported quote length {length}.");
            }
            result.Add(DecodePacket(packet));
        }
        return [.. result];
    }

    private static RupeezyMarketTick DecodePacket(ReadOnlySpan<byte> packet)
    {
        var exchange = Encoding.ASCII.GetString(packet[..10]).TrimEnd('\0', ' ');
        var token = ReadInt32(packet, 10).ToString(CultureInfo.InvariantCulture);
        var lastPrice = ReadDouble(packet, 14).To<decimal>();
        var tick = new RupeezyMarketTick
        {
            InstrumentKey = exchange.ToInstrumentKey(token),
            LastPrice = lastPrice,
            ServerTime = DateTime.UtcNow,
        };
        if (packet.Length == 22)
            return tick;

        var lastTradeTime = ReadInt32(packet, 22);
        tick.LastTradeTime = lastTradeTime > 0
            ? ((long)lastTradeTime).FromUnixSeconds()
            : null;
        tick.OpenPrice = ReadDouble(packet, 26).To<decimal>();
        tick.HighPrice = ReadDouble(packet, 34).To<decimal>();
        tick.LowPrice = ReadDouble(packet, 42).To<decimal>();
        tick.ClosePrice = ReadDouble(packet, 50).To<decimal>();
        tick.Volume = ReadInt32(packet, 58);
        tick.ServerTime = tick.LastTradeTime ?? tick.ServerTime;
        if (packet.Length == 62)
            return tick;

        var lastUpdateTime = ReadInt32(packet, 62);
        if (lastUpdateTime > 0)
            tick.ServerTime = ((long)lastUpdateTime).FromUnixSeconds();
        tick.LastVolume = ReadInt32(packet, 66);
        tick.AveragePrice = ReadDouble(packet, 70).To<decimal>();
        tick.TotalBuyVolume = ReadInt64(packet, 78);
        tick.TotalSellVolume = ReadInt64(packet, 86);
        tick.OpenInterest = ReadInt32(packet, 94);

        var bids = new List<RupeezyDepthLevel>(5);
        var asks = new List<RupeezyDepthLevel>(5);
        var depthOffset = 98;
        for (var index = 0; index < 5; index++)
        {
            var level = ReadDepth(packet, depthOffset);
            depthOffset += 16;
            if (level.Price > 0)
                bids.Add(level);
        }
        for (var index = 0; index < 5; index++)
        {
            var level = ReadDepth(packet, depthOffset);
            depthOffset += 16;
            if (level.Price > 0)
                asks.Add(level);
        }
        tick.Bids = [.. bids.OrderByDescending(level => level.Price)];
        tick.Asks = [.. asks.OrderBy(level => level.Price)];

        var upperCircuit = ReadInt32(packet, 258);
        var lowerCircuit = ReadInt32(packet, 262);
        tick.UpperCircuit = upperCircuit > 0 ? upperCircuit / 100m : null;
        tick.LowerCircuit = lowerCircuit > 0 ? lowerCircuit / 100m : null;
        return tick;
    }

    private static RupeezyDepthLevel ReadDepth(
        ReadOnlySpan<byte> data,
        int offset)
        => new()
        {
            Price = ReadDouble(data, offset).To<decimal>(),
            Volume = ReadInt32(data, offset + 8),
            OrdersCount = ReadInt32(data, offset + 12),
        };

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));

    private static long ReadInt64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));

    private static double ReadDouble(ReadOnlySpan<byte> data, int offset)
        => BitConverter.Int64BitsToDouble(ReadInt64(data, offset));

    private static void Ensure(
        ReadOnlySpan<byte> data,
        int requiredLength,
        string part)
    {
        if (requiredLength < 0 || data.Length < requiredLength)
        {
            throw new InvalidDataException(
                $"Rupeezy WebSocket {part} is truncated.");
        }
    }
}
