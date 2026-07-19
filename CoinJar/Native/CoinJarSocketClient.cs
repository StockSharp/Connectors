namespace StockSharp.CoinJar.Native;

sealed class CoinJarSocketClient : BaseLogReceiver
{
    private readonly string _endpoint;
    private readonly string _token;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly HashSet<string> _topics =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long,
        TaskCompletionSource<CoinJarSocketReplyPayload>> _pendingReplies = [];
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private WebSocketClient _client;
    private CancellationTokenSource _heartbeatCancellation;
    private Task _heartbeatTask;
    private long _reference;

    public CoinJarSocketClient(string endpoint, SecureString token,
        WorkingTime workingTime, int reconnectAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _token = token.IsEmpty() ? null : token.UnSecure().Trim();
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => "CoinJar_WebSocket";

    public bool IsCredentialsAvailable => !_token.IsEmpty();

    public event Func<string, CoinJarTicker, CoinJarSocketEvents,
        CancellationToken, ValueTask> TickerReceived;
    public event Func<string, CoinJarOrderBook, CoinJarSocketEvents,
        CancellationToken, ValueTask> BookReceived;
    public event Func<string, CoinJarTrade[], CoinJarSocketEvents,
        CancellationToken, ValueTask> TradesReceived;
    public event Func<CoinJarOrder, CancellationToken, ValueTask> OrderReceived;
    public event Func<CoinJarFill, CancellationToken, ValueTask> FillReceived;
    public event Func<CoinJarAccount, CancellationToken, ValueTask>
        AccountReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

    protected override void DisposeManaged()
    {
        _heartbeatCancellation?.Cancel();
        _heartbeatCancellation?.Dispose();
        FailPending(new ObjectDisposedException(nameof(CoinJarSocketClient)));
        _client?.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "CoinJar WebSocket is already initialized.");
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            StartHeartbeat();
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeAsync(CoinJarSocketTopics topic,
        string productId, CancellationToken cancellationToken)
        => ChangeTopicAsync(CreateTopic(topic, productId), true,
            cancellationToken);

    public ValueTask UnsubscribeAsync(CoinJarSocketTopics topic,
        string productId, CancellationToken cancellationToken)
        => ChangeTopicAsync(CreateTopic(topic, productId), false,
            cancellationToken);

    public ValueTask SubscribePrivateAsync(CancellationToken cancellationToken)
    {
        EnsureCredentials();
        return ChangeTopicAsync("private", true, cancellationToken);
    }

    public ValueTask UnsubscribePrivateAsync(CancellationToken cancellationToken)
        => ChangeTopicAsync("private", false, cancellationToken);

    public ValueTask RequestSnapshotAsync(string productId,
        CancellationToken cancellationToken)
        => SendPublicCommandAsync(CreateTopic(CoinJarSocketTopics.Book,
            productId), CoinJarSocketEvents.RequestSnapshot, false,
            cancellationToken);

    private WebSocketClient CreateClient()
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            _endpoint,
            (state, token) => OnStateChangedAsync(state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(socket, message, token),
            (s, a) => this.AddInfoLog(s, a),
            (s, a) => this.AddErrorLog(s, a),
            (s, a) => this.AddVerboseLog(s, a))
        {
            ReconnectAttempts = _reconnectAttempts,
            WorkingTime = _workingTime,
            DisableAutoResend = true,
            Indent = false,
            SendSettings = _jsonSettings,
        };
        client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
            "StockSharp-CoinJar-Connector/1.0");
        client.PostConnect += OnPostConnectAsync;
        return client;
    }

    private async ValueTask OnPostConnectAsync(bool isReconnect,
        CancellationToken cancellationToken)
    {
        _ = isReconnect;
        string[] topics;
        using (_sync.EnterScope())
            topics = [.. _topics.OrderBy(static value => value,
                StringComparer.OrdinalIgnoreCase)];
        foreach (var topic in topics)
            await JoinAsync(topic, false, cancellationToken);
    }

    private async ValueTask ChangeTopicAsync(string topic, bool isSubscribe,
        CancellationToken cancellationToken)
    {
        bool changed;
        using (_sync.EnterScope())
            changed = isSubscribe ? _topics.Add(topic) : _topics.Remove(topic);
        if (!changed || _client?.IsConnected != true)
            return;

        try
        {
            if (isSubscribe)
                await JoinAsync(topic, true, cancellationToken);
            else
                await SendPublicCommandAsync(topic, CoinJarSocketEvents.Leave, true,
                    cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
            {
                if (isSubscribe)
                    _topics.Remove(topic);
                else
                    _topics.Add(topic);
            }
            throw;
        }
    }

    private ValueTask JoinAsync(string topic, bool waitForReply,
        CancellationToken cancellationToken)
    {
        if (topic.EqualsIgnoreCase("private"))
        {
            EnsureCredentials();
            return SendPrivateJoinAsync(waitForReply, cancellationToken);
        }
        return SendPublicCommandAsync(topic, CoinJarSocketEvents.Join,
            waitForReply,
            cancellationToken);
    }

    private async ValueTask SendPrivateJoinAsync(bool waitForReply,
        CancellationToken cancellationToken)
    {
        var reference = NextReference();
        var completion = waitForReply ? AddPending(reference) : null;
        try
        {
            await SendAsync(new CoinJarSocketPrivateCommand
            {
                Topic = "private",
                Event = CoinJarSocketEvents.Join,
                Payload = new() { Token = _token },
                Reference = reference,
            }, cancellationToken);
            if (completion is not null)
                await AwaitReplyAsync(reference, completion, cancellationToken);
        }
        catch
        {
            if (completion is not null)
                RemovePending(reference);
            throw;
        }
    }

    private async ValueTask SendPublicCommandAsync(string topic,
        CoinJarSocketEvents socketEvent, bool waitForReply,
        CancellationToken cancellationToken)
    {
        var reference = NextReference();
        var completion = waitForReply ? AddPending(reference) : null;
        try
        {
            await SendAsync(new CoinJarSocketPublicCommand
            {
                Topic = topic,
                Event = socketEvent,
                Reference = reference,
            }, cancellationToken);
            if (completion is not null)
                await AwaitReplyAsync(reference, completion, cancellationToken);
        }
        catch
        {
            if (completion is not null)
                RemovePending(reference);
            throw;
        }
    }

    private async ValueTask SendAsync<TCommand>(TCommand command,
        CancellationToken cancellationToken)
        where TCommand : class
    {
        var client = _client ?? throw new InvalidOperationException(
            "CoinJar WebSocket is not initialized.");
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            await client.SendAsync(command, cancellationToken);
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
        try
        {
            var header = Deserialize<CoinJarSocketHeader>(payload);
            switch (header.Event)
            {
                case CoinJarSocketEvents.Reply:
                    ProcessReply(Deserialize<CoinJarSocketEnvelope<
                        CoinJarSocketReplyPayload>>(payload));
                    break;
                case CoinJarSocketEvents.Init:
                case CoinJarSocketEvents.Update:
                case CoinJarSocketEvents.Snapshot:
                case CoinJarSocketEvents.New:
                    await ProcessPublicAsync(header, payload, cancellationToken);
                    break;
                case CoinJarSocketEvents.PrivateOrder:
                    await RaiseAsync(Deserialize<CoinJarSocketEnvelope<
                        CoinJarSocketOrderPayload>>(payload).Payload?.Order,
                        OrderReceived, cancellationToken);
                    break;
                case CoinJarSocketEvents.PrivateFill:
                    await RaiseAsync(Deserialize<CoinJarSocketEnvelope<
                        CoinJarSocketFillPayload>>(payload).Payload?.Fill,
                        FillReceived, cancellationToken);
                    break;
                case CoinJarSocketEvents.PrivateAccount:
                    await RaiseAsync(Deserialize<CoinJarSocketEnvelope<
                        CoinJarSocketAccountPayload>>(payload).Payload?.Account,
                        AccountReceived, cancellationToken);
                    break;
                case CoinJarSocketEvents.Error:
                case CoinJarSocketEvents.Close:
                    throw new InvalidOperationException(
                        $"CoinJar WebSocket closed topic '{header.Topic}'.");
            }
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
            if (error is InvalidDataException or JsonException)
                client.Abort();
        }
    }

    private async ValueTask ProcessPublicAsync(CoinJarSocketHeader header,
        string payload, CancellationToken cancellationToken)
    {
        if (TryGetProduct(header.Topic, "ticker", out var productId))
        {
            var value = Deserialize<CoinJarSocketEnvelope<CoinJarTicker>>(payload);
            if (value.Payload is not null && TickerReceived is { } ticker)
                await ticker(productId, value.Payload, header.Event,
                    cancellationToken);
            return;
        }
        if (TryGetProduct(header.Topic, "book", out productId))
        {
            var value = Deserialize<CoinJarSocketEnvelope<CoinJarOrderBook>>(payload);
            if (value.Payload is not null && BookReceived is { } book)
                await book(productId, value.Payload, header.Event,
                    cancellationToken);
            return;
        }
        if (TryGetProduct(header.Topic, "trades", out productId))
        {
            var value = Deserialize<CoinJarSocketEnvelope<
                CoinJarSocketTradesPayload>>(payload);
            if (value.Payload is not null && TradesReceived is { } trades)
                await trades(productId, value.Payload.Trades ?? [], header.Event,
                    cancellationToken);
        }
    }

    private void ProcessReply(
        CoinJarSocketEnvelope<CoinJarSocketReplyPayload> envelope)
    {
        if (envelope.Reference is not long reference)
            return;
        TaskCompletionSource<CoinJarSocketReplyPayload> completion;
        using (_sync.EnterScope())
        {
            if (!_pendingReplies.Remove(reference, out completion))
            {
                if (envelope.Payload?.Status != CoinJarSocketReplyStatuses.Ok)
                    throw new InvalidOperationException(
                        $"CoinJar WebSocket rejected request {reference}: " +
                        (envelope.Payload?.Response?.Reason.IsEmpty(
                            "unknown error")));
                return;
            }
        }
        completion.TrySetResult(envelope.Payload ?? new());
    }

    private async ValueTask AwaitReplyAsync(long reference,
        TaskCompletionSource<CoinJarSocketReplyPayload> completion,
        CancellationToken cancellationToken)
    {
        var reply = await completion.Task.WaitAsync(cancellationToken);
        if (reply.Status != CoinJarSocketReplyStatuses.Ok)
            throw new InvalidOperationException(
                $"CoinJar WebSocket rejected request {reference}: " +
                (reply.Response?.Reason.IsEmpty("unknown error")));
    }

    private TaskCompletionSource<CoinJarSocketReplyPayload> AddPending(
        long reference)
    {
        var completion = new TaskCompletionSource<CoinJarSocketReplyPayload>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (_sync.EnterScope())
            _pendingReplies.Add(reference, completion);
        return completion;
    }

    private void RemovePending(long reference)
    {
        using (_sync.EnterScope())
            _pendingReplies.Remove(reference);
    }

    private void FailPending(Exception error)
    {
        TaskCompletionSource<CoinJarSocketReplyPayload>[] pending;
        using (_sync.EnterScope())
        {
            pending = [.. _pendingReplies.Values];
            _pendingReplies.Clear();
        }
        foreach (var completion in pending)
            completion.TrySetException(error);
    }

    private async ValueTask OnStateChangedAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state is ConnectionStates.Reconnecting or ConnectionStates.Failed)
            FailPending(new InvalidOperationException(
                "CoinJar WebSocket connection was interrupted."));
        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        await StopHeartbeatAsync();
        var client = _client;
        _client = null;
        if (client is null)
            return;
        FailPending(new InvalidOperationException(
            "CoinJar WebSocket was disconnected."));
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

    private void StartHeartbeat()
    {
        _heartbeatCancellation = new();
        _heartbeatTask = RunHeartbeatAsync(_heartbeatCancellation.Token);
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
                if (_client?.IsConnected == true)
                    await SendPublicCommandAsync("phoenix",
                        CoinJarSocketEvents.Heartbeat, false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, CancellationToken.None);
        }
    }

    private async ValueTask StopHeartbeatAsync()
    {
        var cancellation = _heartbeatCancellation;
        _heartbeatCancellation = null;
        var task = _heartbeatTask;
        _heartbeatTask = null;
        if (cancellation is null)
            return;
        cancellation.Cancel();
        try
        {
            if (task is not null)
                await task;
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
            throw new InvalidDataException(
                "CoinJar WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        where TPayload : class
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private static bool TryGetProduct(string topic, string prefix,
        out string productId)
    {
        var expected = prefix + ":";
        if (topic?.StartsWith(expected, StringComparison.OrdinalIgnoreCase) == true)
        {
            productId = topic[expected.Length..].NormalizeProduct();
            return true;
        }
        productId = null;
        return false;
    }

    private static string CreateTopic(CoinJarSocketTopics topic,
        string productId)
        => $"{topic.ToString().ToLowerInvariant()}:{productId.NormalizeProduct()}";

    private long NextReference() => Interlocked.Increment(ref _reference);

    private void EnsureCredentials()
    {
        if (!IsCredentialsAvailable)
            throw new InvalidOperationException(
                "A CoinJar Exchange API token is required for the private WebSocket channel.");
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "CoinJar WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
