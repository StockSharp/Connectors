namespace StockSharp.Tapbit.Native;

sealed class TapbitSocketClient : BaseLogReceiver
{
    private const int _maximumMessageBytes = 4 * 1024 * 1024;
    private const int _maximumTopicsPerCommand = 50;
    private readonly string _endpoint;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<string> _topics = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private WebSocketClient _client;
    private DateTime _nextSend;

    public TapbitSocketClient(string endpoint, WorkingTime workingTime,
        int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "Tapbit_WebSocket";

    public event Func<TapbitProductTypes, string, TapbitWsActions?,
        TapbitWsBook, CancellationToken, ValueTask> BookReceived;
    public event Func<TapbitProductTypes, TapbitWsTicker,
        CancellationToken, ValueTask> TickerReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _client?.Dispose();
        _client = null;
        _transition.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "Tapbit WebSocket is already initialized.");
        var client = _client = CreateClient();
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

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public async ValueTask SetTopicsAsync(IEnumerable<string> topics,
        CancellationToken cancellationToken)
    {
        var desired = (topics ?? []).Where(static topic => !topic.IsEmpty())
            .Select(ValidateTopic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static topic => topic,
                StringComparer.OrdinalIgnoreCase).ToArray();
        await _transition.WaitAsync(cancellationToken);
        try
        {
            string[] added;
            string[] removed;
            using (_sync.EnterScope())
            {
                added = [.. desired.Except(_topics,
                    StringComparer.OrdinalIgnoreCase)];
                removed = [.. _topics.Except(desired,
                    StringComparer.OrdinalIgnoreCase)];
                _topics.Clear();
                _topics.AddRange(desired);
            }
            if (_client?.IsConnected != true)
                return;
            await SendTopicsAsync(_client, removed, false,
                cancellationToken);
            await SendTopicsAsync(_client, added, true, cancellationToken);
        }
        finally
        {
            _transition.Release();
        }
    }

    private WebSocketClient CreateClient()
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            _endpoint,
            (state, token) => OnStateChangedAsync(client, state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(client, message,
                token),
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
            "User-Agent", "StockSharp-Tapbit-Connector/1.0");
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

    private async ValueTask OnStateChangedAsync(WebSocketClient client,
        ConnectionStates state, CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Restored)
        {
            string[] topics;
            using (_sync.EnterScope())
                topics = [.. _topics];
            await SendTopicsAsync(client, topics, true, cancellationToken);
        }
        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask SendTopicsAsync(WebSocketClient client,
        string[] topics, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < topics.Length;
            index += _maximumTopicsPerCommand)
        {
            await SendAsync(client, new TapbitWsCommand
            {
                Operation = isSubscribe
                    ? TapbitWsOperations.Subscribe
                    : TapbitWsOperations.Unsubscribe,
                Arguments = [.. topics.Skip(index).Take(
                    _maximumTopicsPerCommand)],
            }, cancellationToken);
        }
    }

    private async ValueTask SendAsync<TPayload>(WebSocketClient client,
        TPayload payload, CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextSend - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            await client.SendAsync(payload, cancellationToken);
            _nextSend = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private async ValueTask OnProcessAsync(WebSocketClient client,
        WebSocketMessage message, CancellationToken cancellationToken)
    {
        var payload = message.AsString();
        if (payload.IsEmpty())
            return;
        if (Encoding.UTF8.GetByteCount(payload) > _maximumMessageBytes)
        {
            await RaiseErrorAsync(new InvalidDataException(
                "Tapbit WebSocket message exceeds the 4 MiB limit."),
                cancellationToken);
            return;
        }
        if (payload.Trim().Trim('"').EqualsIgnoreCase("ping"))
        {
            await SendAsync(client, "pong", cancellationToken);
            return;
        }
        if (payload.Trim().Trim('"').EqualsIgnoreCase("pong"))
            return;
        try
        {
            var header = Deserialize<TapbitWsHeader>(payload);
            if (header.Operation is not null)
            {
                if (header.Code is not (null or 0 or 200))
                    throw new InvalidOperationException(
                        $"Tapbit WebSocket {header.Operation} failed " +
                        $"({header.Code}): " +
                        (header.Message ?? "request rejected"));
                return;
            }
            if (header.Topic.IsEmpty())
                return;
            var productType = header.Topic.StartsWith("spot/",
                StringComparison.OrdinalIgnoreCase)
                ? TapbitProductTypes.Spot
                : header.Topic.StartsWith("usdt/",
                    StringComparison.OrdinalIgnoreCase)
                    ? TapbitProductTypes.Futures
                    : throw new InvalidDataException(
                        $"Tapbit returned unknown topic '{header.Topic}'.");
            if (header.Topic.Contains("/orderBook.",
                StringComparison.OrdinalIgnoreCase))
            {
                var envelope = Deserialize<TapbitWsEnvelope<TapbitWsBook>>(
                    payload);
                if (BookReceived is { } bookHandler)
                    foreach (var book in envelope.Data ?? [])
                        if (book is not null)
                            await bookHandler(productType, envelope.Topic,
                                envelope.Action, book, cancellationToken);
                return;
            }
            if (header.Topic.Contains("/ticker.",
                StringComparison.OrdinalIgnoreCase))
            {
                var envelope = Deserialize<TapbitWsEnvelope<TapbitWsTicker>>(
                    payload);
                if (TickerReceived is { } tickerHandler)
                    foreach (var ticker in envelope.Data ?? [])
                        if (ticker is not null)
                            await tickerHandler(productType, ticker,
                                cancellationToken);
                return;
            }
            throw new InvalidDataException(
                $"Tapbit returned unsupported topic '{header.Topic}'.");
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
                "Tapbit WebSocket returned an empty payload.");

    private async ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
    {
        if (Error is { } handler)
            await handler(error, cancellationToken);
        else
            this.AddErrorLog(error);
    }

    private static string ValidateTopic(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (value.Length is < 5 or > 160 || value.Any(static ch =>
            !(char.IsLetterOrDigit(ch) || ch is '/' or '.' or '-' or '_')))
            throw new ArgumentException(
                $"Invalid Tapbit WebSocket topic '{value}'.", nameof(value));
        return value;
    }

    private static string ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Tapbit WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint.ToString();
    }
}
