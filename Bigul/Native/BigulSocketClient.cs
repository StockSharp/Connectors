namespace StockSharp.Bigul.Native;

sealed class BigulSocketClient : BaseLogReceiver
{
    private const string _socketSource = "StockSharp-Bigul-1.0";
    private const int _channel = 1;
    private const int _symbolLimit = 800;
    private const int _batchSize = 100;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly WebSocketClient _client;
    private readonly string _accessToken;
    private readonly SynchronizedDictionary<string, BigulFeedSubscriptions> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, BigulInstrument> _instruments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string> _topicInstruments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<ushort, BigulFeedState> _states = [];
    private int _acknowledgementFrequency;
    private int _updatesSinceAcknowledgement;

    public BigulSocketClient(
        string accessToken,
        int reconnectAttempts,
        WorkingTime workingTime,
        Uri webSocketAddress)
    {
        _accessToken = accessToken.ThrowIfEmpty(nameof(accessToken));
        if (reconnectAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectAttempts),
                reconnectAttempts,
                "Reconnect attempts cannot be negative.");
        }

        _client = new(
            webSocketAddress?.ToString()
                ?? throw new ArgumentNullException(nameof(webSocketAddress)),
            (state, cancellationToken) =>
                StateChanged is { } stateHandler
                    ? stateHandler.InvokeAsync(state, cancellationToken)
                    : default,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler.InvokeAsync(error, cancellationToken)
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

    public override string Name => nameof(Bigul) + "_" + nameof(BigulSocketClient);

    public event Func<BigulMarketTick, CancellationToken, ValueTask> TickReceived;
    public event Func<BigulOrder, CancellationToken, ValueTask> OrderReceived;
    public event Func<BigulTrade, CancellationToken, ValueTask> TradeReceived;
    public event Func<BigulPosition, CancellationToken, ValueTask> PositionReceived;
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

    public ValueTask SendHeartbeat(CancellationToken cancellationToken)
        => _client.SendAsync("Ping", cancellationToken);

    public async ValueTask SetSubscription(
        BigulInstrument instrument,
        BigulFeedSubscriptions subscriptions,
        CancellationToken cancellationToken)
    {
        if (instrument == null)
            throw new ArgumentNullException(nameof(instrument));

        var instrumentKey = instrument.Segment.ToInstrumentKey(instrument.Token);
        _subscriptions.TryGetValue(instrumentKey, out var current);
        if (current == subscriptions)
            return;

        var count = _subscriptions.Values.Sum(CountFlags) -
            CountFlags(current) +
            CountFlags(subscriptions);
        if (count > _symbolLimit)
        {
            throw new InvalidOperationException(
                $"Bigul allows at most {_symbolLimit} streaming subscriptions per connection.");
        }

        _instruments[instrumentKey] = instrument;
        var removed = current & ~subscriptions;
        var added = subscriptions & ~current;
        if (removed.HasFlag(BigulFeedSubscriptions.Symbol))
            await SendSubscription(false, [RegisterTopic(instrument, false)], cancellationToken);
        if (removed.HasFlag(BigulFeedSubscriptions.Depth))
            await SendSubscription(false, [RegisterTopic(instrument, true)], cancellationToken);

        if (subscriptions == BigulFeedSubscriptions.None)
        {
            _subscriptions.Remove(instrumentKey);
            _instruments.Remove(instrumentKey);
        }
        else
            _subscriptions[instrumentKey] = subscriptions;

        if (added.HasFlag(BigulFeedSubscriptions.Symbol))
            await SendSubscription(true, [RegisterTopic(instrument, false)], cancellationToken);
        if (added.HasFlag(BigulFeedSubscriptions.Depth))
            await SendSubscription(true, [RegisterTopic(instrument, true)], cancellationToken);
    }

    private async ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        _states.Clear();
        _acknowledgementFrequency = 0;
        _updatesSinceAcknowledgement = 0;
        await _client.SendAsync(
            CreateAuthentication(_accessToken, _socketSource),
            WebSocketMessageType.Binary,
            cancellationToken);
        await _client.SendAsync(
            CreateFullMode(),
            WebSocketMessageType.Binary,
            cancellationToken);

        var topics = new List<string>();
        foreach (var pair in _subscriptions.ToArray())
        {
            if (!_instruments.TryGetValue(pair.Key, out var instrument))
                continue;
            if (pair.Value.HasFlag(BigulFeedSubscriptions.Symbol))
                topics.Add(RegisterTopic(instrument, false));
            if (pair.Value.HasFlag(BigulFeedSubscriptions.Depth))
                topics.Add(RegisterTopic(instrument, true));
        }

        for (var index = 0; index < topics.Count; index += _batchSize)
        {
            await SendSubscription(
                true,
                [.. topics.Skip(index).Take(_batchSize)],
                cancellationToken);
        }
    }

    private string RegisterTopic(BigulInstrument instrument, bool isDepth)
    {
        var instrumentKey = instrument.Segment.ToInstrumentKey(instrument.Token);
        var prefix = isDepth
            ? "dp"
            : instrument.ToSecurityType() == SecurityTypes.Index
                ? "if"
                : "sf";
        var topic = $"{prefix}|{instrument.Segment.ToLowerInvariant()}|{instrument.Token}";
        _topicInstruments[topic] = instrumentKey;
        return topic;
    }

    private ValueTask SendSubscription(
        bool isSubscribe,
        string[] topics,
        CancellationToken cancellationToken)
        => topics.Length == 0
            ? default
            : _client.SendAsync(
                CreateSubscription(
                    isSubscribe,
                    topics,
                    _accessToken.Length,
                    _socketSource.Length),
                WebSocketMessageType.Binary,
                cancellationToken);

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var data = message.Memory.ToArray();
        if (data.Length == 0)
            return;

        if (data[0] is (byte)'{' or (byte)'[')
        {
            await ProcessText(message.AsString(), cancellationToken);
            return;
        }
        if (data[0] is (byte)'P' or (byte)'p')
        {
            var text = message.AsString();
            if (text.EqualsIgnoreCase("Ping") || text.EqualsIgnoreCase("Pong"))
                return;
        }

        var result = Decode(data);
        if (result.Acknowledgement != null)
        {
            await _client.SendAsync(
                result.Acknowledgement,
                WebSocketMessageType.Binary,
                cancellationToken);
        }
        if (TickReceived is not { } handler)
            return;
        foreach (var tick in result.Ticks)
            await handler.InvokeAsync(tick, cancellationToken);
    }

    private async ValueTask ProcessText(
        string text,
        CancellationToken cancellationToken)
    {
        text = text?.Trim();
        if (text.IsEmpty() ||
            text.EqualsIgnoreCase("Ping") ||
            text.EqualsIgnoreCase("Pong"))
            return;

        JObject root;
        try
        {
            root = JObject.Parse(text);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                "Bigul returned invalid WebSocket JSON.",
                ex);
        }

        var type = root.GetText("type");
        var payload = root.GetValueIgnoreCase("data") as JObject ?? root;
        switch (type?.ToLowerInvariant())
        {
            case "order":
                if (OrderReceived is { } orderHandler)
                {
                    var order = payload.ToObject<BigulOrder>(
                        JsonSerializer.Create(_jsonSettings));
                    if (order != null)
                        await orderHandler.InvokeAsync(order, cancellationToken);
                }
                return;

            case "trade":
                if (TradeReceived is { } tradeHandler)
                {
                    var trade = payload.ToObject<BigulTrade>(
                        JsonSerializer.Create(_jsonSettings));
                    if (trade != null)
                        await tradeHandler.InvokeAsync(trade, cancellationToken);
                }
                return;

            case "position":
                if (PositionReceived is { } positionHandler)
                {
                    var position = payload.ToObject<BigulPosition>(
                        JsonSerializer.Create(_jsonSettings));
                    if (position != null)
                        await positionHandler.InvokeAsync(position, cancellationToken);
                }
                return;

            case "error":
                throw new InvalidOperationException(
                    $"Bigul WebSocket error: {root.GetText("message").IsEmpty(text)}");

            case "cn":
            case "hb":
            case "ping":
            case "pong":
                if (root.GetText("ak").EqualsIgnoreCase("nk"))
                {
                    throw new InvalidOperationException(
                        $"Bigul WebSocket authentication failed: {root.GetText("msg")}");
                }
                return;
        }

        var marketTick = ParseJsonMarketData(root);
        if (marketTick != null && TickReceived is { } tickHandler)
            await tickHandler.InvokeAsync(marketTick, cancellationToken);
        else
            this.AddVerboseLog("Ignored Bigul WebSocket payload.");
    }

    internal static BigulMarketTick ParseJsonMarketData(JObject root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        var segment = root.GetText("e", "exchange");
        var token = root.GetText("tk", "token");
        var feed = root.GetText("name");
        if (segment.IsEmpty() || token.IsEmpty() ||
            feed is not "sf" and not "dp" and not "if")
            return null;

        var tick = new BigulMarketTick
        {
            InstrumentKey = segment.ToInstrumentKey(token),
            IsDepth = feed == "dp",
            ServerTime = DateTime.UtcNow,
            LastPrice = root.GetDecimal("ltp", "iv"),
            Volume = root.GetDecimal("v"),
            BidPrice = root.GetDecimal("bp"),
            AskPrice = root.GetDecimal("sp"),
        };
        if (tick.BidPrice is > 0)
        {
            tick.Bids =
            [
                new()
                {
                    Price = tick.BidPrice.Value,
                    Position = 1,
                },
            ];
        }
        if (tick.AskPrice is > 0)
        {
            tick.Asks =
            [
                new()
                {
                    Price = tick.AskPrice.Value,
                    Position = 1,
                },
            ];
        }
        return tick;
    }

    internal BigulFeedDecodeResult Decode(ReadOnlySpan<byte> data)
    {
        Ensure(data, 3, "header");
        var responseType = data[2];
        if (responseType == 1)
        {
            DecodeAuthentication(data);
            return new();
        }
        if (responseType != 6)
            return new();

        Ensure(data, 9, "data-feed header");
        var messageNumber = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(3, 4));
        byte[] acknowledgement = null;
        if (_acknowledgementFrequency > 0 &&
            ++_updatesSinceAcknowledgement >= _acknowledgementFrequency)
        {
            acknowledgement = CreateAcknowledgement(messageNumber);
            _updatesSinceAcknowledgement = 0;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(7, 2));
        var offset = 9;
        var ticks = new List<BigulMarketTick>(count);
        for (var index = 0; index < count; index++)
        {
            Ensure(data, offset + 1, "data-feed item");
            var dataType = data[offset++];
            BigulFeedState state;
            switch (dataType)
            {
                case 83:
                    state = DecodeSnapshot(data, ref offset);
                    break;
                case 85:
                    state = DecodeUpdate(data, ref offset);
                    break;
                case 76:
                    state = DecodeLiteUpdate(data, ref offset);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Bigul HSM returned unsupported data type {dataType}.");
            }

            if (state != null)
                ticks.Add(ToTick(state));
        }

        return new()
        {
            Acknowledgement = acknowledgement,
            Ticks = [.. ticks],
        };
    }

    private void DecodeAuthentication(ReadOnlySpan<byte> data)
    {
        var offset = 4;
        ReadField(data, ref offset, out _, out var status);
        if (status.Length != 1 || status[0] != (byte)'K')
            throw new InvalidOperationException("Bigul HSM authentication failed.");
        ReadField(data, ref offset, out _, out var acknowledgement);
        if (acknowledgement.Length >= 4)
        {
            _acknowledgementFrequency = checked(
                (int)BinaryPrimitives.ReadUInt32BigEndian(acknowledgement[..4]));
        }
    }

    private BigulFeedState DecodeSnapshot(
        ReadOnlySpan<byte> data,
        ref int offset)
    {
        Ensure(data, offset + 3, "snapshot topic");
        var topicId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        var topic = ReadString(data, ref offset);
        Ensure(data, offset + 1, "snapshot field count");
        var fieldCount = data[offset++];

        var kind = topic.StartsWith("dp|", StringComparison.Ordinal)
            ? BigulFeedKinds.Depth
            : topic.StartsWith("if|", StringComparison.Ordinal)
                ? BigulFeedKinds.Index
                : BigulFeedKinds.Symbol;
        var state = new BigulFeedState
        {
            Kind = kind,
            Topic = topic,
            InstrumentKey = _topicInstruments.TryGetValue(topic, out var instrumentKey)
                ? instrumentKey
                : ParseTopicInstrumentKey(topic),
        };
        ReadValues(data, ref offset, state, fieldCount);

        Ensure(data, offset + 5, "snapshot scale");
        offset += 2;
        state.Multiplier = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        state.Precision = data[offset++];
        ReadString(data, ref offset);
        ReadString(data, ref offset);
        ReadString(data, ref offset);

        _states[topicId] = state;
        return state;
    }

    private BigulFeedState DecodeUpdate(
        ReadOnlySpan<byte> data,
        ref int offset)
    {
        Ensure(data, offset + 3, "update topic");
        var topicId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        var fieldCount = data[offset++];
        if (!_states.TryGetValue(topicId, out var state))
        {
            Ensure(data, offset + fieldCount * 4, "unknown update fields");
            offset += fieldCount * 4;
            return null;
        }

        ReadValues(data, ref offset, state, fieldCount);
        return state;
    }

    private BigulFeedState DecodeLiteUpdate(
        ReadOnlySpan<byte> data,
        ref int offset)
    {
        Ensure(data, offset + 6, "lite update");
        var topicId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
        offset += 4;
        if (!_states.TryGetValue(topicId, out var state))
            return null;
        if (value != int.MinValue)
        {
            state.Values[0] = value;
            state.HasValues[0] = true;
        }
        return state;
    }

    private static void ReadValues(
        ReadOnlySpan<byte> data,
        ref int offset,
        BigulFeedState state,
        int count)
    {
        Ensure(data, offset + count * 4, "data-feed fields");
        for (var index = 0; index < count; index++)
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
            offset += 4;
            if (index >= state.Values.Length || value == int.MinValue)
                continue;
            state.Values[index] = value;
            state.HasValues[index] = true;
        }
    }

    private static BigulMarketTick ToTick(BigulFeedState state)
    {
        decimal scale = state.Multiplier <= 0 ? 1 : state.Multiplier;
        for (var index = 0; index < state.Precision; index++)
            scale *= 10;

        decimal? value(int index, bool isPrice = false)
            => index >= 0 &&
                index < state.Values.Length &&
                state.HasValues[index]
                    ? isPrice
                        ? state.Values[index] / scale
                        : state.Values[index]
                    : null;

        DateTime serverTime(int index)
        {
            var seconds = value(index);
            return seconds is > 0
                ? decimal.ToInt64(seconds.Value).FromUnixSeconds()
                : DateTime.UtcNow;
        }

        if (state.Kind == BigulFeedKinds.Depth)
        {
            var bids = new List<BigulDepthLevel>(5);
            var asks = new List<BigulDepthLevel>(5);
            for (var index = 0; index < 5; index++)
            {
                var bidPrice = value(index, true);
                var askPrice = value(index + 5, true);
                if (bidPrice is > 0)
                {
                    bids.Add(new()
                    {
                        Price = bidPrice.Value,
                        Volume = value(index + 10) ?? 0,
                        OrdersCount = value(index + 20)?.To<int>(),
                        Position = index + 1,
                    });
                }
                if (askPrice is > 0)
                {
                    asks.Add(new()
                    {
                        Price = askPrice.Value,
                        Volume = value(index + 15) ?? 0,
                        OrdersCount = value(index + 25)?.To<int>(),
                        Position = index + 1,
                    });
                }
            }
            return new()
            {
                InstrumentKey = state.InstrumentKey,
                IsDepth = true,
                ServerTime = DateTime.UtcNow,
                Bids = [.. bids.OrderByDescending(level => level.Price)],
                Asks = [.. asks.OrderBy(level => level.Price)],
            };
        }

        if (state.Kind == BigulFeedKinds.Index)
        {
            return new()
            {
                InstrumentKey = state.InstrumentKey,
                ServerTime = serverTime(2),
                LastPrice = value(0, true),
                ClosePrice = value(1, true),
                HighPrice = value(3, true),
                LowPrice = value(4, true),
                OpenPrice = value(5, true),
            };
        }

        var bestBid = value(6, true);
        var bestAsk = value(7, true);
        return new()
        {
            InstrumentKey = state.InstrumentKey,
            ServerTime = serverTime(3),
            LastTradeTime = value(2) is > 0
                ? decimal.ToInt64(value(2).Value).FromUnixSeconds()
                : null,
            LastPrice = value(0, true),
            LastVolume = value(8),
            Volume = value(1),
            BidPrice = bestBid,
            BidVolume = value(4),
            AskPrice = bestAsk,
            AskVolume = value(5),
            TotalBuyVolume = value(9),
            TotalSellVolume = value(10),
            AveragePrice = value(11, true),
            OpenInterest = value(12),
            LowPrice = value(13, true),
            HighPrice = value(14, true),
            LowerCircuit = value(17, true),
            UpperCircuit = value(18, true),
            OpenPrice = value(19, true),
            ClosePrice = value(20, true),
            Bids = bestBid is > 0
                ? [new() { Price = bestBid.Value, Volume = value(4) ?? 0, Position = 1 }]
                : [],
            Asks = bestAsk is > 0
                ? [new() { Price = bestAsk.Value, Volume = value(5) ?? 0, Position = 1 }]
                : [],
        };
    }

    internal static byte[] CreateAuthentication(string accessToken, string source)
    {
        var token = Encoding.UTF8.GetBytes(
            accessToken.ThrowIfEmpty(nameof(accessToken)));
        var sourceBytes = Encoding.UTF8.GetBytes(source.ThrowIfEmpty(nameof(source)));
        var data = new byte[18 + token.Length + sourceBytes.Length];
        var offset = 0;
        WriteUInt16(data, ref offset, data.Length - 2);
        data[offset++] = 1;
        data[offset++] = 4;
        WriteField(data, ref offset, 1, token);
        WriteField(data, ref offset, 2, [(byte)'N']);
        WriteField(data, ref offset, 3, [1]);
        WriteField(data, ref offset, 4, sourceBytes);
        return data;
    }

    internal static byte[] CreateFullMode()
    {
        var data = new byte[19];
        var offset = 2;
        data[offset++] = 12;
        data[offset++] = 2;
        data[offset++] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), 8);
        offset += 2;
        BinaryPrimitives.WriteUInt64BigEndian(
            data.AsSpan(offset, 8),
            1UL << _channel);
        offset += 8;
        data[offset++] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), 1);
        offset += 2;
        data[offset] = (byte)'F';
        return data;
    }

    internal static byte[] CreateSubscription(
        bool isSubscribe,
        string[] topics,
        int accessTokenLength,
        int sourceLength)
    {
        if (topics == null)
            throw new ArgumentNullException(nameof(topics));
        if (topics.Length > _batchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topics),
                topics.Length,
                $"Bigul accepts at most {_batchSize} topics per request.");
        }

        var topicBytes = topics.Select(Encoding.ASCII.GetBytes).ToArray();
        if (topicBytes.Any(bytes => bytes.Length > byte.MaxValue))
            throw new InvalidOperationException("A Bigul HSM topic is too long.");
        var topicsLength = 2 + topicBytes.Sum(bytes => 1 + bytes.Length);
        var data = new byte[11 + topicsLength];
        var offset = 0;
        var officialLength =
            18 + topicsLength + accessTokenLength + sourceLength;
        WriteUInt16(data, ref offset, officialLength);
        data[offset++] = isSubscribe ? (byte)4 : (byte)5;
        data[offset++] = 2;
        data[offset++] = 1;
        WriteUInt16(data, ref offset, topicsLength);
        WriteUInt16(data, ref offset, topics.Length);
        foreach (var topic in topicBytes)
        {
            data[offset++] = (byte)topic.Length;
            topic.CopyTo(data, offset);
            offset += topic.Length;
        }
        data[offset++] = 2;
        WriteUInt16(data, ref offset, 1);
        data[offset] = _channel;
        return data;
    }

    private static byte[] CreateAcknowledgement(uint messageNumber)
    {
        var data = new byte[11];
        var offset = 0;
        WriteUInt16(data, ref offset, 9);
        data[offset++] = 3;
        data[offset++] = 1;
        data[offset++] = 1;
        WriteUInt16(data, ref offset, 4);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), messageNumber);
        return data;
    }

    private static void WriteField(
        byte[] data,
        ref int offset,
        byte id,
        byte[] value)
    {
        data[offset++] = id;
        WriteUInt16(data, ref offset, value.Length);
        value.CopyTo(data, offset);
        offset += value.Length;
    }

    private static void WriteUInt16(byte[] data, ref int offset, int value)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Bigul HSM field length {value} is out of range.");
        }
        BinaryPrimitives.WriteUInt16BigEndian(
            data.AsSpan(offset, 2),
            (ushort)value);
        offset += 2;
    }

    private static void ReadField(
        ReadOnlySpan<byte> data,
        ref int offset,
        out byte id,
        out ReadOnlySpan<byte> value)
    {
        Ensure(data, offset + 3, "response field");
        id = data[offset++];
        var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        Ensure(data, offset + length, "response field value");
        value = data.Slice(offset, length);
        offset += length;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        Ensure(data, offset + 1, "string length");
        var length = data[offset++];
        Ensure(data, offset + length, "string value");
        var value = Encoding.UTF8.GetString(data.Slice(offset, length));
        offset += length;
        return value;
    }

    private static string ParseTopicInstrumentKey(string topic)
    {
        var parts = topic?.Split('|');
        return parts?.Length == 3
            ? parts[1].ToInstrumentKey(parts[2])
            : throw new InvalidDataException($"Invalid Bigul HSM topic '{topic}'.");
    }

    private static int CountFlags(BigulFeedSubscriptions subscriptions)
        => (subscriptions.HasFlag(BigulFeedSubscriptions.Symbol) ? 1 : 0) +
            (subscriptions.HasFlag(BigulFeedSubscriptions.Depth) ? 1 : 0);

    private static void Ensure(
        ReadOnlySpan<byte> data,
        int requiredLength,
        string part)
    {
        if (requiredLength < 0 || data.Length < requiredLength)
            throw new InvalidDataException($"Bigul HSM {part} is truncated.");
    }
}
