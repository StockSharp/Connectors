namespace StockSharp.PaytmMoney.Native;

sealed class PaytmMoneyWebSocketClient : BaseLogReceiver
{
    private readonly WebSocketClient _client;
    private readonly SynchronizedDictionary<string, string>
        _subscriptions =
            new(StringComparer.OrdinalIgnoreCase);

    public PaytmMoneyWebSocketClient(
        Uri address,
        string publicAccessToken,
        int reconnectAttempts,
        WorkingTime workingTime)
    {
        ArgumentNullException.ThrowIfNull(address);
        var separator = address.Query.IsEmpty() ? "?" : "&";
        var url = address.AbsoluteUri +
            separator + "x_jwt_token=" +
            Uri.EscapeDataString(
                publicAccessToken.ThrowIfEmpty(
                    nameof(publicAccessToken)));

        _client = new(
            url,
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

    public override string Name => "PaytmMoney_MarketData";

    public event Func<
        PaytmMoneyTick,
        CancellationToken,
        ValueTask> TickReceived;

    public event Func<
        Exception,
        CancellationToken,
        ValueTask> Error;

    public event Func<
        ConnectionStates,
        CancellationToken,
        ValueTask> StateChanged;

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
        string mode,
        CancellationToken cancellationToken)
    {
        if (_subscriptions.TryGetValue(
            instrumentKey, out var currentMode))
        {
            if (currentMode.EqualsIgnoreCase(mode))
                return;
            _subscriptions.Remove(instrumentKey);
            await Send(
                false, currentMode, [instrumentKey],
                cancellationToken);
        }

        if (mode.IsEmpty())
            return;
        mode = NormalizeMode(mode);
        _subscriptions[instrumentKey] = mode;
        await Send(
            true, mode, [instrumentKey], cancellationToken);
    }

    private async ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        foreach (var group in _subscriptions
            .ToArray()
            .GroupBy(pair => pair.Value))
        {
            var keys = group
                .Select(pair => pair.Key)
                .ToArray();
            for (var index = 0; index < keys.Length; index += 100)
            {
                await Send(
                    true,
                    group.Key,
                    keys.Skip(index).Take(100).ToArray(),
                    cancellationToken);
            }
        }
    }

    private ValueTask Send(
        bool subscribe,
        string mode,
        string[] instrumentKeys,
        CancellationToken cancellationToken)
    {
        var preferences = instrumentKeys.Select(key =>
        {
            var (
                exchange,
                _,
                securityId,
                scripType,
                _) = key.ParseInstrumentKey();
            return new
            {
                actionType = subscribe ? "ADD" : "REMOVE",
                modeType = NormalizeMode(mode),
                scripType,
                exchangeType = exchange,
                scripId = securityId,
            };
        }).ToArray();
        return _client.SendAsync(
            JsonConvert.SerializeObject(preferences),
            cancellationToken);
    }

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var data = message.Memory;
        if (data.IsEmpty)
            return;

        if (data.Span[0] is (byte)'{' or (byte)'[')
        {
            throw new InvalidOperationException(
                $"Paytm Money market-feed error: {message.AsString()}");
        }

        foreach (var tick in Decode(data.ToArray()))
        {
            if (TickReceived is { } handler)
                await handler(tick, cancellationToken);
        }
    }

    internal static PaytmMoneyTick[] Decode(byte[] data)
    {
        if (data == null || data.Length == 0)
            return [];

        var ticks = new List<PaytmMoneyTick>();
        var position = 0;
        while (position < data.Length)
        {
            var packetType = data[position++];
            var payloadLength = packetType switch
            {
                61 or 64 => 22,
                62 => 66,
                63 => 174,
                65 => 42,
                66 => 38,
                _ => throw new InvalidDataException(
                    $"Unknown Paytm Money packet type {packetType}."),
            };
            if (position + payloadLength > data.Length)
            {
                throw new InvalidDataException(
                    $"Paytm Money packet {packetType} is incomplete: " +
                    $"{data.Length - position} of {payloadLength} payload bytes.");
            }

            var payload = data.AsSpan(position, payloadLength);
            ticks.Add(packetType switch
            {
                61 => DecodeLtp(payload, false),
                62 => DecodeQuote(payload),
                63 => DecodeFull(payload),
                64 => DecodeLtp(payload, true),
                65 => DecodeIndexQuote(payload),
                66 => DecodeIndexFull(payload),
                _ => throw new InvalidDataException(),
            });
            position += payloadLength;
        }
        return [.. ticks];
    }

    private static PaytmMoneyTick DecodeLtp(
        ReadOnlySpan<byte> data, bool index)
    {
        var timestamp = ReadUInt32(data, 4);
        var time = PaytmMoneyExtensions
            .FromPaytmEpoch(timestamp);
        return new()
        {
            LastPrice = ReadSingle(data, 0),
            LastTradeTime = index ? null : time,
            ServerTime = time ?? DateTime.UtcNow,
            SecurityId = ReadUInt32(data, 8)
                .ToString(CultureInfo.InvariantCulture),
        };
    }

    private static PaytmMoneyTick DecodeQuote(
        ReadOnlySpan<byte> data)
    {
        var time = PaytmMoneyExtensions.FromPaytmEpoch(
            ReadUInt32(data, 4));
        return new()
        {
            LastPrice = ReadSingle(data, 0),
            LastTradeTime = time,
            ServerTime = time ?? DateTime.UtcNow,
            SecurityId = ReadUInt32(data, 8)
                .ToString(CultureInfo.InvariantCulture),
            LastQuantity = ReadUInt32(data, 14),
            AveragePrice = ReadSingle(data, 18),
            Volume = ReadUInt32(data, 22),
            TotalBuyQuantity = ReadUInt32(data, 26),
            TotalSellQuantity = ReadUInt32(data, 30),
            Open = ReadSingle(data, 34),
            Close = ReadSingle(data, 38),
            High = ReadSingle(data, 42),
            Low = ReadSingle(data, 46),
        };
    }

    private static PaytmMoneyTick DecodeFull(
        ReadOnlySpan<byte> data)
    {
        var tick = DecodeQuote(data[100..]);
        var bids = new List<PaytmMoneyDepthLevel>(5);
        var asks = new List<PaytmMoneyDepthLevel>(5);
        for (var index = 0; index < 5; index++)
        {
            var offset = index * 20;
            var bidPrice = ReadSingle(data, offset + 12);
            var askPrice = ReadSingle(data, offset + 16);
            if (bidPrice > 0)
            {
                bids.Add(new()
                {
                    Quantity = ReadUInt32(data, offset),
                    Orders = ReadUInt16(data, offset + 8),
                    Price = bidPrice,
                });
            }
            if (askPrice > 0)
            {
                asks.Add(new()
                {
                    Quantity = ReadUInt32(data, offset + 4),
                    Orders = ReadUInt16(data, offset + 10),
                    Price = askPrice,
                });
            }
        }
        tick.Bids = [.. bids.OrderByDescending(level => level.Price)];
        tick.Asks = [.. asks.OrderBy(level => level.Price)];
        tick.OpenInterest = ReadUInt32(data, 166);
        tick.OpenInterestChange =
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(170, 4));
        return tick;
    }

    private static PaytmMoneyTick DecodeIndexQuote(
        ReadOnlySpan<byte> data)
        => new()
        {
            LastPrice = ReadSingle(data, 0),
            SecurityId = ReadUInt32(data, 4)
                .ToString(CultureInfo.InvariantCulture),
            ServerTime = DateTime.UtcNow,
            Open = ReadSingle(data, 10),
            Close = ReadSingle(data, 14),
            High = ReadSingle(data, 18),
            Low = ReadSingle(data, 22),
        };

    private static PaytmMoneyTick DecodeIndexFull(
        ReadOnlySpan<byte> data)
    {
        var time = PaytmMoneyExtensions.FromPaytmEpoch(
            ReadUInt32(data, 34));
        return new()
        {
            LastPrice = ReadSingle(data, 0),
            SecurityId = ReadUInt32(data, 4)
                .ToString(CultureInfo.InvariantCulture),
            ServerTime = time ?? DateTime.UtcNow,
            Open = ReadSingle(data, 10),
            Close = ReadSingle(data, 14),
            High = ReadSingle(data, 18),
            Low = ReadSingle(data, 22),
        };
    }

    private static decimal ReadSingle(
        ReadOnlySpan<byte> data, int offset)
        => Convert.ToDecimal(
            BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    data.Slice(offset, 4))),
            CultureInfo.InvariantCulture);

    private static uint ReadUInt32(
        ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, 4));

    private static ushort ReadUInt16(
        ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(offset, 2));

    private static string NormalizeMode(string mode)
    {
        mode = mode?.ToUpperInvariant();
        return mode is "LTP" or "QUOTE" or "FULL"
            ? mode
            : throw new ArgumentOutOfRangeException(
                nameof(mode), mode,
                "Paytm Money feed mode must be LTP, QUOTE, or FULL.");
    }
}
