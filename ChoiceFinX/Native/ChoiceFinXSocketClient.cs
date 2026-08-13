namespace StockSharp.ChoiceFinX.Native;

sealed class ChoiceFinXSocketClient : BaseLogReceiver
{
    private readonly WebSocketClient _client;

    public ChoiceFinXSocketClient(
        Uri address,
        string token,
        int reconnectAttempts,
        WorkingTime workingTime)
    {
        var socketAddress = AddToken(
            address ??
                throw new ArgumentNullException(
                    nameof(address)),
            token.ThrowIfEmpty(nameof(token)));

        _client = new(
            socketAddress.AbsoluteUri,
            (state, cancellationToken) =>
                StateChanged is { } stateHandler
                    ? stateHandler.InvokeAsync(state, cancellationToken)
                    : default,
            (error, cancellationToken) =>
                Error is { } errorHandler
                    ? errorHandler.InvokeAsync(error, cancellationToken)
                    : default,
            Process,
            (message, args) =>
                this.AddInfoLog(message, args),
            (message, args) =>
                this.AddErrorLog(message, args),
            (message, args) =>
                this.AddVerboseLog(message, args))
        {
            ReconnectAttempts =
                Math.Max(0, reconnectAttempts),
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
    }

    public override string Name =>
        nameof(ChoiceFinX) + "_" +
        nameof(ChoiceFinXSocketClient);

    public event Func<JObject, CancellationToken, ValueTask>
        OrderReceived;
    public event Func<JObject, CancellationToken, ValueTask>
        TradeReceived;
    public event Func<JObject, CancellationToken, ValueTask>
        MarketStatusReceived;
    public event Func<Exception, CancellationToken, ValueTask>
        Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _client.Dispose();
        base.DisposeManaged();
    }

    public ValueTask Connect(
        CancellationToken cancellationToken)
        => _client.ConnectAsync(cancellationToken);

    public ValueTask Disconnect(
        CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public ValueTask SendHeartbeat(
        CancellationToken cancellationToken)
        => _client.SendAsync("2", cancellationToken);

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var text = message.AsString()?.Trim();
        if (text.IsEmpty() || text == "3")
            return;

        JObject root;
        try
        {
            root = JObject.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Choice FinX returned an invalid WebSocket message.",
                ex);
        }

        var type = root.GetText(
            "MessageType", "messageType",
            "Type", "type")?.ToUpperInvariant();
        switch (type)
        {
            case "ORD_NRML":
                if (OrderReceived is { } orderHandler)
                    await orderHandler.InvokeAsync(root, cancellationToken);
                break;

            case "TRD_MSG":
                if (TradeReceived is { } tradeHandler)
                    await tradeHandler.InvokeAsync(root, cancellationToken);
                break;

            case "MKT_STAT":
                if (MarketStatusReceived is { } marketHandler)
                {
                    await marketHandler.InvokeAsync(
                        root, cancellationToken);
                }
                break;

            default:
                var error = root.GetText(
                    "Error", "error",
                    "Reason", "reason");
                if (!error.IsEmpty())
                {
                    throw new InvalidOperationException(
                        $"Choice FinX WebSocket error: {error}");
                }
                this.AddVerboseLog(
                    "Ignored Choice FinX WebSocket message type {0}.",
                    type);
                break;
        }
    }

    internal static JObject GetPayload(JObject root)
    {
        if (root?.GetToken("Data", "data") is JObject data)
        {
            var result = (JObject)data.DeepClone();
            foreach (var property in root.Properties())
            {
                if (property.Name.Equals(
                    "Data",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (result.GetValue(
                    property.Name,
                    StringComparison.OrdinalIgnoreCase) == null)
                {
                    result[property.Name] =
                        property.Value.DeepClone();
                }
            }
            return result;
        }
        return root;
    }

    private static Uri AddToken(
        Uri address, string token)
    {
        var separator = address.Query.IsEmpty()
            ? "?"
            : "&";
        return new(
            address.AbsoluteUri +
            separator +
            "token=" +
            Uri.EscapeDataString(token));
    }
}
