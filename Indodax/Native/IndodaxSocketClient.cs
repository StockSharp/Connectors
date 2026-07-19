namespace StockSharp.Indodax.Native;

sealed class IndodaxSocketException(int code, string message)
    : InvalidOperationException($"Indodax WebSocket error {code}: {message}")
{
    public int Code { get; } = code;
}

sealed class IndodaxSocketClient : BaseLogReceiver
{
    private const int _maximumMessageSize = 4 * 1024 * 1024;
    private const string _publicToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE5NDY2MTg0MTV9.UR1lBM6Eqh0yWz-PVirw1uPCxe60FdchR8eNVdsskeo";

    private readonly Uri _publicEndpoint;
    private readonly Uri _privateEndpoint;
    private readonly IndodaxRestClient _restClient;
    private readonly int _reconnectAttempts;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _publicSendSync = new(1, 1);
    private readonly SemaphoreSlim _privateSendSync = new(1, 1);
    private readonly HashSet<string> _publicChannels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _publicOffsets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, TaskCompletionSource<bool>>
        _publicPending = [];
    private readonly Dictionary<long, string> _publicRequestChannels = [];
    private readonly Dictionary<long, TaskCompletionSource<bool>>
        _privatePending = [];
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        Converters = [new StringEnumConverter()],
    };
    private CancellationTokenSource _lifetime;
    private ClientWebSocket _publicSocket;
    private ClientWebSocket _privateSocket;
    private Task _publicSupervisor;
    private Task _privateSupervisor;
    private TaskCompletionSource<bool> _publicReady;
    private TaskCompletionSource<bool> _privateReady;
    private long _requestId;

    public IndodaxSocketClient(string publicEndpoint, string privateEndpoint,
        IndodaxRestClient restClient, int reconnectAttempts)
    {
        _publicEndpoint = ValidateEndpoint(publicEndpoint, "market-data");
        _privateEndpoint = ValidateEndpoint(privateEndpoint, "private");
        _restClient = restClient ?? throw new ArgumentNullException(
            nameof(restClient));
        _reconnectAttempts = reconnectAttempts.Max(0);
    }

    public override string Name => "Indodax_WebSocket";

    public event Func<IndodaxSocketBook, CancellationToken, ValueTask>
        BookReceived;
    public event Func<IndodaxSocketTrade[], CancellationToken, ValueTask>
        TradesReceived;
    public event Func<IndodaxPrivateEvent[], CancellationToken, ValueTask>
        PrivateEventsReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _lifetime?.Cancel();
        _publicSocket?.Abort();
        _privateSocket?.Abort();
        _lifetime?.Dispose();
        _lifetime = null;
        _publicSocket?.Dispose();
        _publicSocket = null;
        _privateSocket?.Dispose();
        _privateSocket = null;
        _publicSendSync.Dispose();
        _privateSendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
        {
            if (_lifetime is not null)
                throw new InvalidOperationException(
                    "Indodax WebSocket is already connected.");
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _publicReady = CreateCompletion();
            _privateReady = CreateCompletion();
            _publicSupervisor = SupervisePublicAsync(_lifetime.Token);
            _privateSupervisor = _restClient.IsCredentialsAvailable
                ? SupervisePrivateAsync(_lifetime.Token)
                : Task.CompletedTask;
            if (!_restClient.IsCredentialsAvailable)
                _privateReady.TrySetResult(true);
        }

        try
        {
            await _publicReady.Task.WaitAsync(TimeSpan.FromSeconds(30),
                cancellationToken);
            await _privateReady.Task.WaitAsync(TimeSpan.FromSeconds(30),
                cancellationToken);
        }
        catch
        {
            await DisconnectAsync(CancellationToken.None);
            throw;
        }
    }

    public async ValueTask DisconnectAsync(
        CancellationToken cancellationToken)
    {
        CancellationTokenSource lifetime;
        Task[] supervisors;
        ClientWebSocket publicSocket;
        ClientWebSocket privateSocket;
        using (_sync.EnterScope())
        {
            lifetime = _lifetime;
            if (lifetime is null)
                return;
            _lifetime = null;
            publicSocket = _publicSocket;
            privateSocket = _privateSocket;
            supervisors = [_publicSupervisor ?? Task.CompletedTask,
                _privateSupervisor ?? Task.CompletedTask];
        }

        lifetime.Cancel();
        await CloseAsync(publicSocket, cancellationToken);
        await CloseAsync(privateSocket, cancellationToken);
        try
        {
            await Task.WhenAll(supervisors).WaitAsync(TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        catch (Exception error) when (error is OperationCanceledException or
            TimeoutException or WebSocketException)
        {
        }
        finally
        {
            lifetime.Dispose();
            FailPending(new OperationCanceledException(
                "Indodax WebSocket disconnected."));
        }
    }

    public ValueTask SubscribeBookAsync(string pairId, bool isSubscribe,
        CancellationToken cancellationToken)
        => ChangePublicChannelAsync(
            $"market:order-book-{pairId.NormalizePairId()}", isSubscribe,
            cancellationToken);

    public ValueTask SubscribeTradesAsync(string pairId, bool isSubscribe,
        CancellationToken cancellationToken)
        => ChangePublicChannelAsync(
            $"market:trade-activity-{pairId.NormalizePairId()}", isSubscribe,
            cancellationToken);

    private async Task SupervisePublicAsync(
        CancellationToken cancellationToken)
    {
        var failures = 0;
        var wasConnected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket socket = null;
            Task receiver = null;
            try
            {
                socket = CreateSocket();
                await socket.ConnectAsync(_publicEndpoint, cancellationToken);
                using (_sync.EnterScope())
                    _publicSocket = socket;
                receiver = ReceivePublicLoopAsync(socket, cancellationToken);

                await SendPublicAndWaitAsync(socket,
                    new IndodaxPublicSocketCommand
                    {
                        Id = NextRequestId(),
                        Parameters = new() { Token = _publicToken },
                    }, cancellationToken);
                await RestorePublicSubscriptionsAsync(socket,
                    cancellationToken);

                failures = 0;
                _publicReady.TrySetResult(true);
                if (wasConnected)
                    await RaiseStateAsync(ConnectionStates.Restored,
                        cancellationToken);
                wasConnected = true;
                _ = RunPublicHeartbeatAsync(socket, cancellationToken);
                await receiver;
                if (!cancellationToken.IsCancellationRequested)
                    throw new IOException(
                        "Indodax market-data WebSocket closed unexpectedly.");
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                failures++;
                await RaiseErrorAsync(error, cancellationToken);
                if (failures > _reconnectAttempts)
                {
                    _publicReady.TrySetException(error);
                    await RaiseStateAsync(ConnectionStates.Failed,
                        cancellationToken);
                    return;
                }
            }
            finally
            {
                using (_sync.EnterScope())
                    if (ReferenceEquals(_publicSocket, socket))
                        _publicSocket = null;
                FailPublicPending(new IOException(
                    "Indodax market-data WebSocket connection was lost."));
                socket?.Abort();
                if (receiver is not null)
                {
                    try
                    {
                        await receiver;
                    }
                    catch
                    {
                    }
                }
                socket?.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
                await Task.Delay(GetReconnectDelay(failures),
                    cancellationToken);
        }
    }

    private async Task SupervisePrivateAsync(
        CancellationToken cancellationToken)
    {
        var failures = 0;
        var wasConnected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket socket = null;
            Task receiver = null;
            try
            {
                var token = await _restClient.GeneratePrivateTokenAsync(
                    cancellationToken);
                if (token?.ConnectionToken.IsEmpty() != false ||
                    token.Channel.IsEmpty())
                    throw new InvalidDataException(
                        "Indodax returned an incomplete private WebSocket token.");

                socket = CreateSocket();
                await socket.ConnectAsync(_privateEndpoint, cancellationToken);
                using (_sync.EnterScope())
                    _privateSocket = socket;
                receiver = ReceivePrivateLoopAsync(socket, cancellationToken);

                await SendPrivateAndWaitAsync(socket,
                    new IndodaxPrivateConnectCommand
                    {
                        Id = NextRequestId(),
                        Connect = new()
                        {
                            Token = token.ConnectionToken,
                        },
                    }, cancellationToken);
                await SendPrivateAndWaitAsync(socket,
                    new IndodaxPrivateSubscribeCommand
                    {
                        Id = NextRequestId(),
                        Subscribe = new() { Channel = token.Channel },
                    }, cancellationToken);

                failures = 0;
                _privateReady.TrySetResult(true);
                if (wasConnected)
                    await RaiseStateAsync(ConnectionStates.Restored,
                        cancellationToken);
                wasConnected = true;
                await receiver;
                if (!cancellationToken.IsCancellationRequested)
                    throw new IOException(
                        "Indodax private WebSocket closed unexpectedly.");
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                failures++;
                await RaiseErrorAsync(error, cancellationToken);
                if (failures > _reconnectAttempts)
                {
                    _privateReady.TrySetException(error);
                    await RaiseStateAsync(ConnectionStates.Failed,
                        cancellationToken);
                    return;
                }
            }
            finally
            {
                using (_sync.EnterScope())
                    if (ReferenceEquals(_privateSocket, socket))
                        _privateSocket = null;
                FailPrivatePending(new IOException(
                    "Indodax private WebSocket connection was lost."));
                socket?.Abort();
                if (receiver is not null)
                {
                    try
                    {
                        await receiver;
                    }
                    catch
                    {
                    }
                }
                socket?.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
                await Task.Delay(GetReconnectDelay(failures),
                    cancellationToken);
        }
    }

    private async ValueTask ChangePublicChannelAsync(string channel,
        bool isSubscribe, CancellationToken cancellationToken)
    {
        ClientWebSocket socket;
        using (_sync.EnterScope())
        {
            var changed = isSubscribe
                ? _publicChannels.Add(channel)
                : _publicChannels.Remove(channel);
            if (!changed)
                return;
            if (!isSubscribe)
                _publicOffsets.Remove(channel);
            socket = _publicSocket;
        }

        if (socket?.State != WebSocketState.Open)
            return;
        try
        {
            await SendPublicSubscriptionAsync(socket, channel, isSubscribe,
                null, cancellationToken);
        }
        catch (IndodaxSocketException)
        {
            using (_sync.EnterScope())
            {
                if (isSubscribe)
                    _publicChannels.Remove(channel);
                else
                    _publicChannels.Add(channel);
            }
            throw;
        }
    }

    private async ValueTask RestorePublicSubscriptionsAsync(
        ClientWebSocket socket, CancellationToken cancellationToken)
    {
        KeyValuePair<string, long>[] channels;
        using (_sync.EnterScope())
            channels = [.. _publicChannels.OrderBy(static channel => channel,
                StringComparer.OrdinalIgnoreCase).Select(channel =>
                new KeyValuePair<string, long>(channel,
                    _publicOffsets.TryGetValue(channel, out var offset)
                        ? offset
                        : 0))];

        foreach (var channel in channels)
        {
            try
            {
                await SendPublicSubscriptionAsync(socket, channel.Key, true,
                    channel.Value > 0 ? channel.Value : null,
                    cancellationToken);
            }
            catch (IndodaxSocketException) when (channel.Value > 0)
            {
                using (_sync.EnterScope())
                    _publicOffsets.Remove(channel.Key);
                await SendPublicSubscriptionAsync(socket, channel.Key, true,
                    null, cancellationToken);
            }
        }
    }

    private ValueTask SendPublicSubscriptionAsync(ClientWebSocket socket,
        string channel, bool isSubscribe, long? offset,
        CancellationToken cancellationToken)
        => SendPublicAndWaitAsync(socket, new()
        {
            Id = NextRequestId(),
            Method = isSubscribe ? 1 : 2,
            Parameters = new()
            {
                Channel = channel,
                IsRecover = isSubscribe && offset > 0 ? true : null,
                Offset = isSubscribe ? offset : null,
            },
        }, cancellationToken);

    private async Task RunPublicHeartbeatAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open &&
            !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
                if (socket.State != WebSocketState.Open)
                    return;
                await SendPublicAndWaitAsync(socket, new()
                {
                    Id = NextRequestId(),
                    Method = 7,
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                await RaiseErrorAsync(error, cancellationToken);
                socket.Abort();
                return;
            }
        }
    }

    private async ValueTask SendPublicAndWaitAsync(
        ClientWebSocket socket, IndodaxPublicSocketCommand command,
        CancellationToken cancellationToken)
    {
        var completion = CreateCompletion();
        using (_sync.EnterScope())
        {
            _publicPending.Add(command.Id, completion);
            if (command.Parameters?.Channel.IsEmpty() == false)
                _publicRequestChannels[command.Id] = command.Parameters.Channel;
        }
        try
        {
            await SendAsync(socket, command, _publicSendSync,
                cancellationToken);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        finally
        {
            using (_sync.EnterScope())
            {
                _publicPending.Remove(command.Id);
                _publicRequestChannels.Remove(command.Id);
            }
        }
    }

    private async ValueTask SendPrivateAndWaitAsync<TCommand>(
        ClientWebSocket socket, TCommand command,
        CancellationToken cancellationToken)
        where TCommand : class
    {
        var id = command switch
        {
            IndodaxPrivateConnectCommand connect => connect.Id,
            IndodaxPrivateSubscribeCommand subscribe => subscribe.Id,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        var completion = CreateCompletion();
        using (_sync.EnterScope())
            _privatePending.Add(id, completion);
        try
        {
            await SendAsync(socket, command, _privateSendSync,
                cancellationToken);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        finally
        {
            using (_sync.EnterScope())
                _privatePending.Remove(id);
        }
    }

    private async ValueTask SendAsync<TPayload>(ClientWebSocket socket,
        TPayload payload, SemaphoreSlim sendSync,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload,
            _jsonSettings));
        await sendSync.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true,
                cancellationToken);
        }
        finally
        {
            sendSync.Release();
        }
    }

    private Task ReceivePublicLoopAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
        => ReceiveLoopAsync(socket, ProcessPublicAsync, cancellationToken);

    private Task ReceivePrivateLoopAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
        => ReceiveLoopAsync(socket, ProcessPrivateAsync, cancellationToken);

    private async Task ReceiveLoopAsync(ClientWebSocket socket,
        Func<string, CancellationToken, ValueTask> processor,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open &&
            !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (message.Length + result.Count > _maximumMessageSize)
                    throw new InvalidDataException(
                        "Indodax WebSocket message exceeds the size limit.");
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            await processor(Encoding.UTF8.GetString(message.GetBuffer(), 0,
                (int)message.Length), cancellationToken);
        }
    }

    private async ValueTask ProcessPublicAsync(string payload,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty())
            return;
        try
        {
            var response = Deserialize<IndodaxPublicSocketResponse>(payload);
            if (response.Id is { } id)
            {
                Exception error = response.Error is null
                    ? null
                    : new IndodaxSocketException(response.Error.Code,
                        response.Error.Message);
                if (error is null)
                {
                    string pendingChannel;
                    using (_sync.EnterScope())
                        _publicRequestChannels.TryGetValue(id, out pendingChannel);
                    if (!pendingChannel.IsEmpty())
                    {
                        try
                        {
                            await ProcessRecoveredAsync(pendingChannel, payload,
                                cancellationToken);
                        }
                        catch (Exception recoveryError)
                        {
                            error = recoveryError;
                        }
                    }
                }
                CompletePublic(id, error);
                return;
            }

            var header = Deserialize<IndodaxPublicPushHeader>(payload);
            var channel = header.Result?.Channel;
            if (channel.IsEmpty())
                return;
            if (header.Result.Publication?.Offset > 0)
                using (_sync.EnterScope())
                    _publicOffsets[channel] = header.Result.Publication.Offset;

            if (channel.StartsWith("market:order-book-",
                StringComparison.OrdinalIgnoreCase))
            {
                var message = Deserialize<
                    IndodaxPublicPushEnvelope<IndodaxSocketBook>>(payload);
                if (message.Result?.Publication?.Data is { } book &&
                    BookReceived is { } handler)
                    await handler(book, cancellationToken);
            }
            else if (channel.StartsWith("market:trade-activity-",
                StringComparison.OrdinalIgnoreCase))
            {
                var message = Deserialize<IndodaxPublicPushEnvelope<
                    IndodaxSocketTrade[]>>(payload);
                if (message.Result?.Publication?.Data is { Length: > 0 } trades &&
                    TradesReceived is { } handler)
                    await handler(trades, cancellationToken);
            }
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private async ValueTask ProcessRecoveredAsync(string channel,
        string payload, CancellationToken cancellationToken)
    {
        long offset = 0;
        if (channel.StartsWith("market:order-book-",
            StringComparison.OrdinalIgnoreCase))
        {
            var recovery = Deserialize<IndodaxPublicRecoveryEnvelope<
                IndodaxSocketBook>>(payload);
            foreach (var publication in recovery.Result?.Publications ?? [])
            {
                if (publication?.Data is { } book && BookReceived is { } handler)
                    await handler(book, cancellationToken);
                offset = Math.Max(offset, publication?.Offset ?? 0);
            }
            offset = Math.Max(offset, recovery.Result?.Offset ?? 0);
        }
        else if (channel.StartsWith("market:trade-activity-",
            StringComparison.OrdinalIgnoreCase))
        {
            var recovery = Deserialize<IndodaxPublicRecoveryEnvelope<
                IndodaxSocketTrade[]>>(payload);
            foreach (var publication in recovery.Result?.Publications ?? [])
            {
                if (publication?.Data is { Length: > 0 } trades &&
                    TradesReceived is { } handler)
                    await handler(trades, cancellationToken);
                offset = Math.Max(offset, publication?.Offset ?? 0);
            }
            offset = Math.Max(offset, recovery.Result?.Offset ?? 0);
        }
        if (offset > 0)
            using (_sync.EnterScope())
                _publicOffsets[channel] = offset;
    }

    private async ValueTask ProcessPrivateAsync(string payload,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty())
            return;
        try
        {
            var response = Deserialize<IndodaxPrivateSocketResponse>(payload);
            if (response.Id is { } id)
            {
                CompletePrivate(id, response.Error is null
                    ? null
                    : new IndodaxSocketException(response.Error.Code,
                        response.Error.Message));
                return;
            }

            var header = Deserialize<IndodaxPrivatePushHeader>(payload);
            if (header.Push?.Channel.IsEmpty() != false)
                return;
            var message = Deserialize<IndodaxPrivatePushEnvelope>(payload);
            if (message.Push?.Publication?.Events is { Length: > 0 } events &&
                PrivateEventsReceived is { } handler)
                await handler(events, cancellationToken);
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private void CompletePublic(long id, Exception error)
    {
        TaskCompletionSource<bool> completion;
        using (_sync.EnterScope())
            _publicPending.TryGetValue(id, out completion);
        if (error is null)
            completion?.TrySetResult(true);
        else
            completion?.TrySetException(error);
    }

    private void CompletePrivate(long id, Exception error)
    {
        TaskCompletionSource<bool> completion;
        using (_sync.EnterScope())
            _privatePending.TryGetValue(id, out completion);
        if (error is null)
            completion?.TrySetResult(true);
        else
            completion?.TrySetException(error);
    }

    private void FailPending(Exception error)
    {
        FailPublicPending(error);
        FailPrivatePending(error);
    }

    private void FailPublicPending(Exception error)
    {
        TaskCompletionSource<bool>[] pending;
        using (_sync.EnterScope())
        {
            pending = [.. _publicPending.Values];
            _publicPending.Clear();
            _publicRequestChannels.Clear();
        }
        foreach (var completion in pending)
            completion.TrySetException(error);
    }

    private void FailPrivatePending(Exception error)
    {
        TaskCompletionSource<bool>[] pending;
        using (_sync.EnterScope())
        {
            pending = [.. _privatePending.Values];
            _privatePending.Clear();
        }
        foreach (var completion in pending)
            completion.TrySetException(error);
    }

    private long NextRequestId() => Interlocked.Increment(ref _requestId);

    private static TaskCompletionSource<bool> CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
            throw new InvalidDataException(
                "Indodax WebSocket returned an empty JSON value.");

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private ValueTask RaiseStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
        => StateChanged is { } handler
            ? handler(state, cancellationToken)
            : default;

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(5);
        socket.Options.SetRequestHeader("User-Agent",
            "StockSharp-Indodax-Connector/1.0");
        return socket;
    }

    private static async ValueTask CloseAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        if (socket?.State != WebSocketState.Open)
            return;
        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                "disconnect", cancellationToken);
        }
        catch (Exception error) when (error is WebSocketException or
            OperationCanceledException)
        {
            socket.Abort();
        }
    }

    private static TimeSpan GetReconnectDelay(int failures)
        => TimeSpan.FromSeconds(Math.Min(30, 1 << failures.Min(5)));

    private static Uri ValidateEndpoint(string value, string name)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                $"Indodax {name} WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
