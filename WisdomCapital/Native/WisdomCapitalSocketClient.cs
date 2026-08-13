namespace StockSharp.WisdomCapital.Native;

sealed class WisdomCapitalSocketClient : BaseLogReceiver
{
    private readonly WebSocketClient _client;
    private readonly string _token;
    private readonly bool _marketData;

    public WisdomCapitalSocketClient(
        Uri rootAddress,
        string token,
        string userId,
        bool marketData,
        int engineIoVersion,
        int reconnectAttempts,
        WorkingTime workingTime)
    {
        _token = token.ThrowIfEmpty(nameof(token));
        _marketData = marketData;
        var address = CreateSocketAddress(
            rootAddress,
            token,
            userId,
            marketData,
            engineIoVersion);
        _client = new(
            address.AbsoluteUri,
            (state, cancellationToken) =>
                StateChanged is { } stateHandler
                    ? stateHandler.InvokeAsync(state, cancellationToken)
                    : default,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler.InvokeAsync(error, cancellationToken)
                    : default,
            Process,
            (s, a) => AddSafeLog(LogLevels.Info, s, a),
            (s, a) => AddSafeLog(LogLevels.Error, s, a),
            (s, a) => AddSafeLog(LogLevels.Verbose, s, a))
        {
            ReconnectAttempts = reconnectAttempts,
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
        _client.PostConnect += OnPostConnect;
    }

    public override string Name =>
        nameof(WisdomCapital) + "_" +
        (_marketData ? "MarketSocket" : "InteractiveSocket");

    public event Func<
        WisdomMarketUpdate,
        CancellationToken,
        ValueTask> MarketDataReceived;

    public event Func<
        string,
        JToken,
        CancellationToken,
        ValueTask> InteractiveEventReceived;

    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;
    public event Func<CancellationToken, ValueTask> Ready;

    public ValueTask Connect(CancellationToken cancellationToken)
        => _client.ConnectAsync(cancellationToken);

    public ValueTask Disconnect(CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public static ValueTask SendHeartbeat(CancellationToken cancellationToken)
        => default;

    protected override void DisposeManaged()
    {
        _client.PostConnect -= OnPostConnect;
        _client.Dispose();
        base.DisposeManaged();
    }

    internal static Uri CreateSocketAddress(
        Uri rootAddress,
        string token,
        string userId,
        bool marketData,
        int engineIoVersion)
    {
        ArgumentNullException.ThrowIfNull(rootAddress);
        token.ThrowIfEmpty(nameof(token));
        userId.ThrowIfEmpty(nameof(userId));
        if (engineIoVersion is < 3 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(engineIoVersion),
                engineIoVersion,
                "Wisdom Capital supports Engine.IO 3 or 4.");
        }
        var builder = new UriBuilder(rootAddress)
        {
            Scheme = rootAddress.Scheme.EqualsIgnoreCase("http")
                ? "ws"
                : "wss",
            Path = marketData
                ? "/apimarketdata/socket.io/"
                : "/interactive/socket.io/",
            Query =
                $"token={Uri.EscapeDataString(token)}" +
                $"&userID={Uri.EscapeDataString(userId)}" +
                (marketData
                    ? "&publishFormat=JSON&broadcastMode=Full"
                    : "&apiType=INTERACTIVE") +
                $"&EIO={engineIoVersion.ToString(CultureInfo.InvariantCulture)}" +
                "&transport=websocket",
        };
        return builder.Uri;
    }

    internal static JArray ParseEventPacket(string packet)
    {
        if (packet.IsEmpty() ||
            !packet.StartsWith("42", StringComparison.Ordinal))
            return null;
        var arrayIndex = packet.IndexOf('[');
        if (arrayIndex < 0)
            return null;
        try
        {
            return JArray.Parse(packet[arrayIndex..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ValueTask OnPostConnect(
        bool reconnect,
        CancellationToken cancellationToken)
    {
        return default;
    }

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var text = message.AsString();
        if (text.IsEmpty())
            return;
        foreach (var packet in text.Split(
            '\u001e',
            StringSplitOptions.RemoveEmptyEntries))
        {
            await ProcessPacket(packet, cancellationToken);
        }
    }

    private async ValueTask ProcessPacket(
        string packet,
        CancellationToken cancellationToken)
    {
        if (packet == "2" ||
            packet.StartsWith("2probe", StringComparison.Ordinal))
        {
            await _client.SendAsync(
                packet == "2" ? "3" : "3probe",
                cancellationToken);
            return;
        }
        if (packet.StartsWith('0'))
        {
            await _client.SendAsync("40", cancellationToken);
            return;
        }
        if (packet.StartsWith("40", StringComparison.Ordinal))
        {
            if (Ready is { } readyHandler)
                await readyHandler.InvokeAsync(cancellationToken);
            return;
        }
        if (packet.StartsWith("41", StringComparison.Ordinal))
        {
            return;
        }
        if (packet.StartsWith("44", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wisdom Capital Socket.IO authentication failed: {packet[2..]}");
        }
        if (!packet.StartsWith("42", StringComparison.Ordinal))
            return;

        var eventPacket = ParseEventPacket(packet);
        if (eventPacket == null ||
            eventPacket.Count == 0 ||
            eventPacket[0]?.Value<string>() is not { } eventName)
            return;
        var payload = eventPacket.Count > 1
            ? eventPacket[1]
            : null;
        if (payload?.Type == JTokenType.String)
        {
            var value = payload.Value<string>();
            if (!value.IsEmpty() &&
                (value[0] == '{' || value[0] == '['))
            {
                try
                {
                    payload = JToken.Parse(value);
                }
                catch (JsonException)
                {
                }
            }
        }

        if (_marketData)
        {
            if (!int.TryParse(
                eventName.Split('-')[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var messageCode))
                return;
            if (payload is JObject obj &&
                WisdomCapitalExtensions.FindProperty(
                    obj,
                    "MessageCode") == null)
                obj["MessageCode"] = messageCode;
            var update = WisdomCapitalRestClient.ParseMarketUpdate(
                payload,
                DateTime.UtcNow);
            if (MarketDataReceived is { } marketHandler)
                await marketHandler.InvokeAsync(update, cancellationToken);
            return;
        }

        if (eventName.EqualsIgnoreCase("error"))
        {
            throw new InvalidOperationException(
                $"Wisdom Capital interactive Socket.IO error: {payload}");
        }
        if (InteractiveEventReceived is { } interactiveHandler)
        {
            await interactiveHandler.InvokeAsync(
                eventName,
                payload ?? JValue.CreateNull(),
                cancellationToken);
        }
    }

    private void AddSafeLog(
        LogLevels level,
        string format,
        params object[] args)
    {
        if (format.IsEmpty())
            return;
        format = format.Replace(
            _token,
            "<redacted>",
            StringComparison.Ordinal);
        var safeArgs = args?.Select(arg =>
            arg is string value
                ? value.Replace(
                    _token,
                    "<redacted>",
                    StringComparison.Ordinal)
                : arg).ToArray();
        switch (level)
        {
            case LogLevels.Error:
                this.AddErrorLog(format, safeArgs ?? []);
                break;
            case LogLevels.Verbose:
                this.AddVerboseLog(format, safeArgs ?? []);
                break;
            default:
                this.AddInfoLog(format, safeArgs ?? []);
                break;
        }
    }
}
