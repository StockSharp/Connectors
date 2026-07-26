namespace StockSharp.Mastertrust.Native;

sealed class MastertrustSocketClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly string _clientId;
    private readonly WebSocketClient _client;
    private readonly SynchronizedSet<string> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);

    public MastertrustSocketClient(
        string clientId,
        string accessToken,
        int reconnectAttempts,
        WorkingTime workingTime,
        Uri webSocketAddress)
    {
        _clientId = clientId.ThrowIfEmpty(nameof(clientId));
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
        address +=
            $"login_id={Uri.EscapeDataString(_clientId)}" +
            $"&token={Uri.EscapeDataString(accessToken)}" +
            "&device=web";

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

    public override string Name =>
        nameof(Mastertrust) + "_" + nameof(MastertrustSocketClient);

    public event Func<MastertrustMarketData, CancellationToken, ValueTask>
        MarketDataReceived;
    public event Func<MastertrustSocketUpdate, CancellationToken, ValueTask>
        UpdateReceived;
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
        => _client.SendAsync(
            "{\"a\":\"h\",\"v\":[],\"m\":\"\"}",
            cancellationToken);

    public async ValueTask SetSubscription(
        string instrumentKey,
        MastertrustStreamModes mode,
        bool isSubscribe,
        CancellationToken cancellationToken)
    {
        var subscriptionKey = CreateSubscriptionKey(instrumentKey, mode);
        if (isSubscribe)
        {
            if (_subscriptions.Contains(subscriptionKey))
                return;
            _subscriptions.Add(subscriptionKey);
            await SendSubscription(
                instrumentKey,
                mode,
                true,
                cancellationToken);
        }
        else if (_subscriptions.Remove(subscriptionKey))
        {
            await SendSubscription(
                instrumentKey,
                mode,
                false,
                cancellationToken);
        }
    }

    private async ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        await SendUpdateSubscription(true, cancellationToken);
        foreach (var subscriptionKey in _subscriptions.ToArray())
        {
            var (instrumentKey, mode) =
                ParseSubscriptionKey(subscriptionKey);
            await SendSubscription(
                instrumentKey,
                mode,
                true,
                cancellationToken);
        }
    }

    private ValueTask SendUpdateSubscription(
        bool isSubscribe,
        CancellationToken cancellationToken)
        => _client.SendAsync(
            new JObject
            {
                ["a"] = isSubscribe ? "subscribe" : "unsubscribe",
                ["v"] = new JArray(_clientId, "web"),
                ["m"] = "updates",
            }.ToString(Formatting.None),
            cancellationToken);

    private ValueTask SendSubscription(
        string instrumentKey,
        MastertrustStreamModes mode,
        bool isSubscribe,
        CancellationToken cancellationToken)
    {
        var (exchange, token) = instrumentKey.ParseInstrumentKey();
        if (!long.TryParse(
            token,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var numericToken))
        {
            throw new FormatException(
                $"Mastertrust token '{token}' is not numeric.");
        }

        var values = new JArray
        {
            new JArray(exchange.ToExchangeCode(), numericToken),
        };
        return _client.SendAsync(
            new JObject
            {
                ["a"] = isSubscribe ? "subscribe" : "unsubscribe",
                ["v"] = values,
                ["m"] = mode == MastertrustStreamModes.Depth
                    ? "full_snapquote"
                    : "marketdata",
            }.ToString(Formatting.None),
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
            ProcessControlMessage(message.AsString());
            return;
        }

        var packetCode = data.Span[0];
        if (packetCode is 1 or 2 or 4)
        {
            if (MarketDataReceived is { } marketHandler)
            {
                await marketHandler(
                    DecodeMarketData(data.Span),
                    cancellationToken);
            }
            return;
        }
        if (packetCode == 14)
        {
            // TBT/20-level packets are not requested by this connector.
            return;
        }
        if (packetCode is 11 or 12 or 50 or 51 or 58)
        {
            var update = DecodeUpdate(data.Span);
            if (update != null && UpdateReceived is { } updateHandler)
                await updateHandler(update, cancellationToken);
        }
    }

    internal static void ProcessControlMessage(string text)
    {
        if (text.IsEmpty())
            return;

        JToken root;
        try
        {
            root = JToken.Parse(text);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                "Mastertrust returned invalid WebSocket JSON.",
                ex);
        }

        var status = root.GetText("status");
        var type = root.GetText("type", "event");
        var error = root.GetValueIgnoreCase("error");
        var hasError = error switch
        {
            null => false,
            JValue { Type: JTokenType.Null } => false,
            JValue { Type: JTokenType.Boolean } value => value.Value<bool>(),
            JValue value => !value.ToString().IsEmpty(),
            JContainer container => container.HasValues,
            _ => true,
        };
        if (status.EqualsIgnoreCase("error") ||
            type.EqualsIgnoreCase("error") ||
            hasError)
        {
            throw new InvalidOperationException(
                $"Mastertrust WebSocket error: " +
                root.GetText("message", "error").IsEmpty(text));
        }
    }

    internal static MastertrustMarketData DecodeMarketData(
        ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new InvalidDataException("Mastertrust quote packet is empty.");

        return data[0] switch
        {
            1 => DecodeDetailed(data),
            2 => DecodeCompact(data),
            4 => DecodeDepth(data),
            _ => throw new InvalidDataException(
                $"Unsupported Mastertrust quote packet code {data[0]}."),
        };
    }

    internal static MastertrustSocketUpdate DecodeUpdate(
        ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            throw new InvalidDataException(
                "Mastertrust update packet is truncated.");
        }

        var packetCode = data[0];
        var start = 5;
        while (start < data.Length &&
            data[start] is not ((byte)'{' or (byte)'['))
            start++;
        if (start >= data.Length)
            return null;

        var end = data.Length;
        while (end > start && data[end - 1] == 0)
            end--;
        var text = Encoding.UTF8.GetString(data[start..end]);

        JToken root;
        try
        {
            root = JToken.Parse(text);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                "Mastertrust returned invalid update JSON.",
                ex);
        }

        var payload = UnwrapPayload(root, packetCode);
        if (payload == null)
            return null;
        var serializer = JsonSerializer.Create(_jsonSettings);
        return new()
        {
            PacketCode = packetCode,
            ClientId = root.GetText("client_id", "login_id")
                .IsEmpty(payload.GetText("client_id", "login_id")),
            Order = packetCode is 11 or 50
                ? payload.ToObject<MastertrustOrder>(serializer)
                : null,
            Trade = packetCode is 12 or 51
                ? payload.ToObject<MastertrustTrade>(serializer)
                : null,
            Position = packetCode == 58
                ? payload.ToObject<MastertrustPosition>(serializer)
                : null,
        };
    }

    private static MastertrustMarketData DecodeDetailed(
        ReadOnlySpan<byte> data)
    {
        Ensure(data, 102, "detailed quote");
        var exchangeCode = data[1];
        var exchange = exchangeCode.ToExchange();
        var divisor = exchangeCode.GetPriceDivisor();
        var token = ReadInt32(data, 2);
        var lastTradeTime = ReadInt32(data, 10);
        var exchangeTime = ReadInt32(data, 58);
        var bestBidPrice = ReadPrice(data, 22, divisor);
        var bestAskPrice = ReadPrice(data, 30, divisor);
        return new()
        {
            InstrumentKey = exchange.ToInstrumentKey(
                token.ToString(CultureInfo.InvariantCulture)),
            ServerTime = ((long)(exchangeTime > 0 ? exchangeTime : lastTradeTime))
                .FromUnixSeconds(),
            LastTradeTime = lastTradeTime > 0
                ? ((long)lastTradeTime).FromUnixSeconds()
                : null,
            LastPrice = Positive(ReadPrice(data, 6, divisor)),
            LastVolume = Positive(ReadInt32(data, 14)),
            Volume = Positive(ReadInt32(data, 18)),
            BestBidPrice = Positive(bestBidPrice),
            BestBidVolume = Positive(ReadInt32(data, 26)),
            BestAskPrice = Positive(bestAskPrice),
            BestAskVolume = Positive(ReadInt32(data, 34)),
            TotalBuyVolume = Positive(ReadInt64(data, 38)),
            TotalSellVolume = Positive(ReadInt64(data, 46)),
            AveragePrice = Positive(ReadPrice(data, 54, divisor)),
            OpenPrice = Positive(ReadPrice(data, 62, divisor)),
            HighPrice = Positive(ReadPrice(data, 66, divisor)),
            LowPrice = Positive(ReadPrice(data, 70, divisor)),
            ClosePrice = Positive(ReadPrice(data, 74, divisor)),
            YearlyHighPrice = Positive(ReadPrice(data, 78, divisor)),
            YearlyLowPrice = Positive(ReadPrice(data, 82, divisor)),
            LowerCircuit = Positive(ReadPrice(data, 86, divisor)),
            UpperCircuit = Positive(ReadPrice(data, 90, divisor)),
            OpenInterest = Positive(ReadInt32(data, 94)),
            Bids = bestBidPrice > 0
                ?
                [
                    new()
                    {
                        Price = bestBidPrice,
                        Volume = Math.Max(0, ReadInt32(data, 26)),
                    },
                ]
                : [],
            Asks = bestAskPrice > 0
                ?
                [
                    new()
                    {
                        Price = bestAskPrice,
                        Volume = Math.Max(0, ReadInt32(data, 34)),
                    },
                ]
                : [],
        };
    }

    private static MastertrustMarketData DecodeCompact(
        ReadOnlySpan<byte> data)
    {
        Ensure(data, 42, "compact quote");
        var exchangeCode = data[1];
        var exchange = exchangeCode.ToExchange();
        var divisor = exchangeCode.GetPriceDivisor();
        var token = ReadInt32(data, 2);
        var lastTradeTime = ReadInt32(data, 14);
        return new()
        {
            InstrumentKey = exchange.ToInstrumentKey(
                token.ToString(CultureInfo.InvariantCulture)),
            ServerTime = ((long)lastTradeTime).FromUnixSeconds(),
            LastTradeTime = lastTradeTime > 0
                ? ((long)lastTradeTime).FromUnixSeconds()
                : null,
            LastPrice = Positive(ReadPrice(data, 6, divisor)),
            LowerCircuit = Positive(ReadPrice(data, 18, divisor)),
            UpperCircuit = Positive(ReadPrice(data, 22, divisor)),
            OpenInterest = Positive(ReadInt32(data, 26)),
            BestBidPrice = Positive(ReadPrice(data, 34, divisor)),
            BestAskPrice = Positive(ReadPrice(data, 38, divisor)),
            LastVolume = data.Length >= 46
                ? Positive(ReadInt32(data, 42))
                : null,
        };
    }

    private static MastertrustMarketData DecodeDepth(
        ReadOnlySpan<byte> data)
    {
        Ensure(data, 166, "market depth");
        var exchangeCode = data[1];
        var exchange = exchangeCode.ToExchange();
        var divisor = exchangeCode.GetPriceDivisor();
        var token = ReadInt32(data, 2);
        var bids = new MastertrustDepthLevel[5];
        var asks = new MastertrustDepthLevel[5];
        for (var index = 0; index < 5; index++)
        {
            bids[index] = new()
            {
                OrdersCount = Math.Max(0, ReadInt32(data, 6 + index * 4)),
                Price = ReadPrice(data, 26 + index * 4, divisor),
                Volume = Math.Max(0, ReadInt32(data, 46 + index * 4)),
            };
            asks[index] = new()
            {
                OrdersCount = Math.Max(0, ReadInt32(data, 66 + index * 4)),
                Price = ReadPrice(data, 86 + index * 4, divisor),
                Volume = Math.Max(0, ReadInt32(data, 106 + index * 4)),
            };
        }
        bids = [.. bids.Where(level => level.Price > 0)
            .OrderByDescending(level => level.Price)];
        asks = [.. asks.Where(level => level.Price > 0)
            .OrderBy(level => level.Price)];

        return new()
        {
            InstrumentKey = exchange.ToInstrumentKey(
                token.ToString(CultureInfo.InvariantCulture)),
            IsDepth = true,
            ServerTime = DateTime.UtcNow,
            AveragePrice = Positive(ReadPrice(data, 126, divisor)),
            OpenPrice = Positive(ReadPrice(data, 130, divisor)),
            HighPrice = Positive(ReadPrice(data, 134, divisor)),
            LowPrice = Positive(ReadPrice(data, 138, divisor)),
            ClosePrice = Positive(ReadPrice(data, 142, divisor)),
            TotalBuyVolume = Positive(ReadInt64(data, 146)),
            TotalSellVolume = Positive(ReadInt64(data, 154)),
            Volume = Positive(ReadInt32(data, 162)),
            BestBidPrice = bids.FirstOrDefault()?.Price,
            BestBidVolume = Positive(bids.FirstOrDefault()?.Volume),
            BestAskPrice = asks.FirstOrDefault()?.Price,
            BestAskVolume = Positive(asks.FirstOrDefault()?.Volume),
            Bids = bids,
            Asks = asks,
        };
    }

    private static JToken UnwrapPayload(JToken root, int packetCode)
    {
        var current = root;
        for (var depth = 0; depth < 4; depth++)
        {
            if (current is JArray array)
                return array.FirstOrDefault();
            if (current is not JObject obj)
                return current;

            var named = packetCode switch
            {
                11 or 50 => obj.GetValueIgnoreCase("order", "orders"),
                12 or 51 => obj.GetValueIgnoreCase("trade", "trades"),
                58 => obj.GetValueIgnoreCase("position", "positions"),
                _ => null,
            };
            var next = named ??
                obj.GetValueIgnoreCase("data", "result", "payload");
            if (next == null || ReferenceEquals(next, current))
                return current;
            current = next;
        }
        return current is JArray finalArray
            ? finalArray.FirstOrDefault()
            : current;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));

    private static long ReadInt64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8));

    private static decimal ReadPrice(
        ReadOnlySpan<byte> data,
        int offset,
        decimal divisor)
        => ReadInt32(data, offset) / divisor;

    private static decimal? Positive(decimal value)
        => value > 0 ? value : null;

    private static decimal? Positive(long value)
        => value > 0 ? value : null;

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static void Ensure(
        ReadOnlySpan<byte> data,
        int requiredLength,
        string part)
    {
        if (requiredLength < 0 || data.Length < requiredLength)
        {
            throw new InvalidDataException(
                $"Mastertrust WebSocket {part} packet is truncated.");
        }
    }

    private static string CreateSubscriptionKey(
        string instrumentKey,
        MastertrustStreamModes mode)
        => $"{(int)mode}|{instrumentKey}";

    private static (string instrumentKey, MastertrustStreamModes mode)
        ParseSubscriptionKey(string value)
    {
        var separator = value.IndexOf('|');
        if (separator <= 0 ||
            !int.TryParse(
                value[..separator],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var numericMode) ||
            !Enum.IsDefined(typeof(MastertrustStreamModes), numericMode))
        {
            throw new FormatException(
                $"Invalid Mastertrust subscription key '{value}'.");
        }
        return (
            value[(separator + 1)..],
            (MastertrustStreamModes)numericMode);
    }
}
