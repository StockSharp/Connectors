namespace StockSharp.PintuPro.Native;

sealed class PintuProSocketClient(string endpoint,
    PintuProRestClient restClient, int reconnectAttempts) : BaseLogReceiver
{
    private const int _maximumMessageSize = 4 * 1024 * 1024;

    private readonly Uri _endpoint = ValidateEndpoint(endpoint);
    private readonly PintuProRestClient _restClient = restClient ??
        throw new ArgumentNullException(nameof(restClient));
    private readonly int _reconnectAttempts = reconnectAttempts.Max(0);
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly HashSet<string> _channels =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _cancellation;
    private Task _runTask;
    private ClientWebSocket _socket;
    private TaskCompletionSource<bool> _initialConnection;
    private TaskCompletionSource<bool> _authentication;
    private bool _isDisconnecting;

    public override string Name => "PintuPro_WebSocket";

    public event Func<PintuProBookStreamMessage, CancellationToken,
        ValueTask> BookReceived;
    public event Func<PintuProPublicTradeStreamMessage, CancellationToken,
        ValueTask> PublicTradeReceived;
    public event Func<PintuProOrderStreamMessage, CancellationToken,
        ValueTask> OrderReceived;
    public event Func<PintuProAccountTradeStreamMessage, CancellationToken,
        ValueTask> AccountTradeReceived;
    public event Func<PintuProBalanceStreamMessage, CancellationToken,
        ValueTask> BalanceReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
        {
            if (_runTask is not null)
                throw new InvalidOperationException(
                    "Pintu Pro WebSocket is already running.");
            _isDisconnecting = false;
            _cancellation = new CancellationTokenSource();
            _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _runTask = RunAsync(_cancellation.Token);
        }
        await _initialConnection.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        Task runTask;
        ClientWebSocket socket;
        using (_sync.EnterScope())
        {
            if (_runTask is null)
                return;
            _isDisconnecting = true;
            _cancellation.Cancel();
            runTask = _runTask;
            socket = _socket;
        }
        if (socket?.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                    "disconnect", cancellationToken);
            }
            catch (WebSocketException)
            {
            }
        }
        try
        {
            await runTask.WaitAsync(cancellationToken);
        }
        finally
        {
            using (_sync.EnterScope())
            {
                _runTask = null;
                _cancellation?.Dispose();
                _cancellation = null;
                _socket = null;
            }
        }
    }

    public ValueTask SubscribeBookAsync(string symbol, bool isSubscribe,
        CancellationToken cancellationToken)
        => ChangeChannelAsync($"aggrbook.snapshot.10.{symbol.NormalizeSymbol()}",
            isSubscribe, cancellationToken);

    public ValueTask SubscribeTradesAsync(string symbol, bool isSubscribe,
        CancellationToken cancellationToken)
        => ChangeChannelAsync($"trades.{symbol.NormalizeSymbol()}", isSubscribe,
            cancellationToken);

    public async ValueTask SetPrivateChannelsAsync(bool isSubscribe,
        CancellationToken cancellationToken)
    {
        if (!_restClient.IsCredentialsAvailable)
        {
            if (isSubscribe)
                throw new InvalidOperationException(
                    "Pintu Pro credentials are required for private streams.");
            return;
        }
        foreach (var channel in new[]
        {
            "user.orders",
            "user.trades",
            "user.balance.snapshot",
        })
            await ChangeChannelAsync(channel, isSubscribe, cancellationToken);
    }

    protected override void DisposeManaged()
    {
        _isDisconnecting = true;
        _cancellation?.Cancel();
        _socket?.Abort();
        _socket?.Dispose();
        _cancellation?.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        var connectedOnce = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var socket = CreateSocket();
                using (_sync.EnterScope())
                    _socket = socket;
                try
                {
                    await socket.ConnectAsync(_endpoint, cancellationToken);
                    var receiveTask = ReceiveLoopAsync(socket,
                        cancellationToken);
                    if (_restClient.IsCredentialsAvailable)
                        await AuthenticateAsync(socket, cancellationToken);
                    await RestoreSubscriptionsAsync(socket,
                        cancellationToken);
                    failures = 0;

                    if (!connectedOnce)
                    {
                        connectedOnce = true;
                        _initialConnection.TrySetResult(true);
                    }
                    else
                        await RaiseStateAsync(ConnectionStates.Restored,
                            cancellationToken);
                    await receiveTask;
                    if (!_isDisconnecting &&
                        !cancellationToken.IsCancellationRequested)
                        throw new WebSocketException(
                            "Pintu Pro WebSocket closed unexpectedly.");
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    failures++;
                    await RaiseErrorAsync(error, cancellationToken);
                    if (failures > _reconnectAttempts)
                    {
                        _initialConnection.TrySetException(error);
                        await RaiseStateAsync(ConnectionStates.Failed,
                            cancellationToken);
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(
                        Math.Min(30, 1 << Math.Min(failures, 5))),
                        cancellationToken);
                }
                finally
                {
                    using (_sync.EnterScope())
                        if (ReferenceEquals(_socket, socket))
                            _socket = null;
                }
            }
        }
        finally
        {
            if (!_initialConnection.Task.IsCompleted)
            {
                if (_isDisconnecting)
                    _initialConnection.TrySetCanceled(cancellationToken);
                else
                    _initialConnection.TrySetException(new WebSocketException(
                        "Pintu Pro WebSocket could not connect."));
            }
            await RaiseStateAsync(ConnectionStates.Disconnected,
                CancellationToken.None);
        }
    }

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(5);
        socket.Options.SetRequestHeader("User-Agent",
            "StockSharp-PintuPro-Connector/1.0");
        return socket;
    }

    private async ValueTask AuthenticateAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var auth = _restClient.CreateSocketAuthentication();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (_sync.EnterScope())
            _authentication = completion;
        try
        {
            await SendAsync(socket, new PintuProSocketAuthRequest
            {
                RequestId = auth.RequestId,
                Timestamp = auth.Timestamp,
                Method = "public/auth",
                ApiKey = auth.ApiKey,
                Signature = auth.Signature,
            }, cancellationToken);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        finally
        {
            using (_sync.EnterScope())
                if (ReferenceEquals(_authentication, completion))
                    _authentication = null;
        }
    }

    private async ValueTask RestoreSubscriptionsAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        string[] channels;
        using (_sync.EnterScope())
            channels = [.. _channels.OrderBy(static value => value,
                StringComparer.OrdinalIgnoreCase)];
        foreach (var chunk in channels.Chunk(50))
            await SendChannelRequestAsync(socket, "subscribe", chunk,
                cancellationToken);
    }

    private async ValueTask ChangeChannelAsync(string channel,
        bool isSubscribe, CancellationToken cancellationToken)
    {
        ClientWebSocket socket;
        using (_sync.EnterScope())
        {
            var changed = isSubscribe
                ? _channels.Add(channel)
                : _channels.Remove(channel);
            if (!changed)
                return;
            try
            {
                ValidateChannelLimits();
            }
            catch
            {
                if (isSubscribe)
                    _channels.Remove(channel);
                else
                    _channels.Add(channel);
                throw;
            }
            socket = _socket;
        }
        if (socket?.State != WebSocketState.Open)
            return;
        try
        {
            await SendChannelRequestAsync(socket,
                isSubscribe ? "subscribe" : "unsubscribe", [channel],
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
            {
                if (isSubscribe)
                    _channels.Remove(channel);
                else
                    _channels.Add(channel);
            }
            throw;
        }
    }

    private void ValidateChannelLimits()
    {
        var privateCount = _channels.Count(static value =>
            value.StartsWith("user.", StringComparison.OrdinalIgnoreCase));
        var publicCount = _channels.Count - privateCount;
        if (publicCount > 128 || privateCount > 128)
            throw new InvalidOperationException(
                "A Pintu Pro WebSocket supports at most 128 public and 128 private channels.");
    }

    private ValueTask SendChannelRequestAsync(ClientWebSocket socket,
        string method, string[] channels,
        CancellationToken cancellationToken)
        => SendAsync(socket, new PintuProSocketRequest<
            PintuProSubscriptionParams>
        {
            RequestId = Guid.NewGuid().ToString(),
            Timestamp = _restClient.GetTimestamp(),
            Method = method,
            Parameters = new() { Channels = channels },
        }, cancellationToken);

    private async ValueTask SendAsync<TPayload>(ClientWebSocket socket,
        TPayload payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None,
                Culture = CultureInfo.InvariantCulture,
                Converters = [new StringEnumConverter()],
            }));
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true,
                cancellationToken);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket,
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
                        "Pintu Pro WebSocket message exceeds the size limit.");
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            await ProcessAsync(socket, Encoding.UTF8.GetString(
                message.GetBuffer(), 0, (int)message.Length), cancellationToken);
        }
    }

    private async ValueTask ProcessAsync(ClientWebSocket socket, string payload,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty())
            return;
        try
        {
            var header = Deserialize<PintuProSocketHeader>(payload);
            if (header.Method.EqualsIgnoreCase("heartbeat-request"))
            {
                await SendAsync(socket, new PintuProHeartbeatResponse
                {
                    RequestId = header.RequestId,
                    Timestamp = _restClient.GetTimestamp(),
                }, cancellationToken);
                return;
            }

            if (header.Method.EqualsIgnoreCase("public/auth"))
            {
                TaskCompletionSource<bool> completion;
                using (_sync.EnterScope())
                    completion = _authentication;
                if (IsExplicitSuccess(header.Code))
                    completion?.TrySetResult(true);
                else
                    completion?.TrySetException(CreateSocketError(header));
                return;
            }

            if (!header.Code.IsEmpty() && !IsSuccess(header.Code))
            {
                await RaiseErrorAsync(CreateSocketError(header),
                    cancellationToken);
                return;
            }
            if (!header.Method.EqualsIgnoreCase("subscription") ||
                header.Channel.IsEmpty())
                return;

            if (header.Channel.StartsWith("user.orders",
                StringComparison.OrdinalIgnoreCase))
                await RaiseAsync(Deserialize<PintuProOrderStreamMessage>(payload),
                    OrderReceived, cancellationToken);
            else if (header.Channel.StartsWith("user.trades",
                StringComparison.OrdinalIgnoreCase))
                await RaiseAsync(Deserialize<
                    PintuProAccountTradeStreamMessage>(payload),
                    AccountTradeReceived, cancellationToken);
            else if (header.Channel.EqualsIgnoreCase(
                "user.balance.snapshot"))
                await RaiseAsync(Deserialize<PintuProBalanceStreamMessage>(payload),
                    BalanceReceived, cancellationToken);
            else if (header.Channel.StartsWith("aggrbook.snapshot.",
                StringComparison.OrdinalIgnoreCase))
                await RaiseAsync(Deserialize<PintuProBookStreamMessage>(payload),
                    BookReceived, cancellationToken);
            else if (header.Channel.StartsWith("trades.",
                StringComparison.OrdinalIgnoreCase))
                await RaiseAsync(Deserialize<PintuProPublicTradeStreamMessage>(
                    payload), PublicTradeReceived, cancellationToken);
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private static InvalidOperationException CreateSocketError(
        PintuProSocketHeader header)
        => new($"Pintu Pro WebSocket error {header.Code}: {header.Message}" +
            (header.Reason.IsEmpty() ? string.Empty : $" ({header.Reason})"));

    private static bool IsSuccess(string code)
        => code.IsEmpty() || code.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);

    private static bool IsExplicitSuccess(string code)
        => !code.IsEmpty() && IsSuccess(code);

    private static TPayload Deserialize<TPayload>(string payload)
        => JsonConvert.DeserializeObject<TPayload>(payload,
            new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
                Culture = CultureInfo.InvariantCulture,
                Converters = [new StringEnumConverter()],
            }) ?? throw new InvalidDataException(
                "Pintu Pro WebSocket returned an empty JSON value.");

    private static ValueTask RaiseAsync<TPayload>(TPayload payload,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
        => payload is null || handler is null
            ? default
            : handler(payload, cancellationToken);

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private ValueTask RaiseStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
        => StateChanged is { } handler
            ? handler(state, cancellationToken)
            : default;

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "Pintu Pro WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
