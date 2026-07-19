namespace StockSharp.BYDFi.Native;

sealed class BYDFiWebSocketClient : BaseLogReceiver
{
    private const int _maximumMessageBytes = 2 * 1024 * 1024;
    private readonly string _endpoint;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly Lock _sync = new();
    private readonly HashSet<string> _streams =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private WebSocketClient _client;

    public BYDFiWebSocketClient(string endpoint, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "BYDFi_WebSocket";

    public event Func<BYDFiWsTicker, CancellationToken, ValueTask>
        TickerReceived;
    public event Func<BYDFiWsRealTicker, CancellationToken, ValueTask>
        RealTickerReceived;
    public event Func<BYDFiWsDepth, CancellationToken, ValueTask>
        DepthReceived;
    public event Func<BYDFiWsKline, CancellationToken, ValueTask>
        KlineReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _client?.Dispose();
        _client = null;
        _transition.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask SetStreamsAsync(IEnumerable<string> streams,
        CancellationToken cancellationToken)
    {
        var desired = (streams ?? [])
            .Where(static value => !value.IsEmpty())
            .Select(ValidateStream)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await _transition.WaitAsync(cancellationToken);
        try
        {
            using (_sync.EnterScope())
            {
                if (_streams.SetEquals(desired))
                    return;
                _streams.Clear();
                _streams.AddRange(desired);
            }
            await DisposeClientAsync(cancellationToken);
            if (desired.Length == 0)
                return;
            var client = _client = CreateClient(BuildEndpoint(desired));
            try
            {
                await client.ConnectAsync(cancellationToken);
            }
            catch
            {
                await DisposeClientAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _transition.Release();
        }
    }

    public async ValueTask DisconnectAsync(
        CancellationToken cancellationToken)
    {
        await _transition.WaitAsync(cancellationToken);
        try
        {
            using (_sync.EnterScope())
                _streams.Clear();
            await DisposeClientAsync(cancellationToken);
        }
        finally
        {
            _transition.Release();
        }
    }

    private WebSocketClient CreateClient(string endpoint)
    {
        WebSocketClient client = null;
        client = new WebSocketClient(endpoint,
            (state, token) => OnStateChangedAsync(state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(message, token),
            (format, args) => this.AddInfoLog(format, args),
            (format, args) => this.AddErrorLog(format, args),
            (format, args) => this.AddVerboseLog(format, args))
        {
            ReconnectAttempts = _reconnectAttempts,
            WorkingTime = _workingTime,
            DisableAutoResend = true,
            Indent = false,
            SendSettings = _jsonSettings,
        };
        client.Init += socket => socket.Options.SetRequestHeader(
            "User-Agent", "StockSharp-BYDFi-Connector/1.0");
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        if (client is null)
            return;
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(cancellationToken);
        }
        finally
        {
            client.Dispose();
        }
    }

    private async ValueTask OnStateChangedAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask OnProcessAsync(WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var payload = message.AsString();
        if (payload.IsEmpty())
            return;
        if (Encoding.UTF8.GetByteCount(payload) > _maximumMessageBytes)
        {
            await RaiseErrorAsync(new InvalidDataException(
                "BYDFi WebSocket message exceeds the 2 MiB limit."),
                cancellationToken);
            return;
        }
        try
        {
            var header = Deserialize<BYDFiWsHeader>(payload);
            switch (header?.Event)
            {
                case "24hrTicker":
                    if (TickerReceived is { } tickerHandler)
                        await tickerHandler(Deserialize<BYDFiWsTicker>(
                            payload), cancellationToken);
                    break;
                case "tradePriceUpdate":
                    if (RealTickerReceived is { } priceHandler)
                        await priceHandler(Deserialize<BYDFiWsRealTicker>(
                            payload), cancellationToken);
                    break;
                case "depthUpdate":
                    if (DepthReceived is { } depthHandler)
                        await depthHandler(Deserialize<BYDFiWsDepth>(payload),
                            cancellationToken);
                    break;
                case "kline":
                    if (KlineReceived is { } candleHandler)
                        await candleHandler(Deserialize<BYDFiWsKline>(
                            payload), cancellationToken);
                    break;
            }
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private TMessage Deserialize<TMessage>(string payload)
        where TMessage : class
        => JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
            ?? throw new InvalidDataException(
                "BYDFi WebSocket returned an empty payload.");

    private async ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
    {
        if (Error is { } handler)
            await handler(error, cancellationToken);
        else
            this.AddErrorLog(error);
    }

    private string BuildEndpoint(IEnumerable<string> streams)
        => _endpoint + "/" + string.Join('/', streams);

    private static string ValidateStream(string value)
    {
        value = value.Trim();
        if (value.Length is < 3 or > 128 || value.Any(static ch =>
            !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '@' or '!' or
                '.')))
            throw new ArgumentException(
                $"Invalid BYDFi WebSocket stream '{value}'.", nameof(value));
        return value;
    }

    private static string ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "BYDFi WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint.ToString().TrimEnd('/');
    }
}
