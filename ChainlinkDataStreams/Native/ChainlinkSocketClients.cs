namespace StockSharp.ChainlinkDataStreams.Native;

sealed class ChainlinkStreamPool : BaseLogReceiver
{
    private readonly record struct ConnectionOutcome(
        ChainlinkSocketConnection Connection, bool IsSuccess, Exception Error);

    private readonly Lock _sync = new();
    private readonly HashSet<ChainlinkSocketConnection> _terminal = [];
    private readonly ChainlinkSocketConnection[] _connections;
    private bool _isStarting;
    private bool _isRunning;
    private bool _isDisposed;

    public ChainlinkStreamPool(long transactionId, string feedId, string endpoint,
        IEnumerable<string> origins, SecureString key, SecureString secret,
        int maximumAttempts)
    {
        if (transactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(transactionId));
        TransactionId = transactionId;
        FeedId = feedId.ParseFeed().FeedId;
        var addresses = (origins ?? [])
            .Select(static origin => origin?.Trim())
            .Where(static origin => !origin.IsEmpty())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (addresses.Length == 0)
            addresses = [string.Empty];
        _connections = addresses.Select(origin => new ChainlinkSocketConnection(
            endpoint, FeedId, origin, key, secret, maximumAttempts)
        {
            Parent = this,
        }).ToArray();
        foreach (var connection in _connections)
        {
            connection.MessageReceived += OnMessageReceivedAsync;
            connection.Error += OnErrorAsync;
        }
    }

    public override string Name => "Chainlink_Data_Streams_WS_POOL";

    public long TransactionId { get; }
    public string FeedId { get; }

    public event Func<ChainlinkStreamPool, ChainlinkReportEnvelope,
        CancellationToken, ValueTask> ReportReceived;
    public event Func<ChainlinkStreamPool, Exception, bool, CancellationToken,
        ValueTask> Error;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        using (_sync.EnterScope())
        {
            if (_isRunning || _isStarting)
                throw new InvalidOperationException(
                    "Chainlink WebSocket pool is already running.");
            _terminal.Clear();
            _isStarting = true;
        }

        var failures = new List<Exception>();
        var pending = _connections.Select(connection =>
            ConnectOneAsync(connection, cancellationToken)).ToList();
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var outcome = await completed;
                if (!outcome.IsSuccess)
                {
                    failures.Add(outcome.Error);
                    continue;
                }

                var isActive = false;
                using (_sync.EnterScope())
                {
                    if (!_terminal.Contains(outcome.Connection))
                    {
                        _isRunning = true;
                        _isStarting = false;
                        isActive = true;
                    }
                }
                if (!isActive)
                {
                    failures.Add(new IOException(
                        "A Chainlink WebSocket origin disconnected during startup."));
                    continue;
                }

                foreach (var failure in failures)
                    this.AddWarningLog(
                        "A Chainlink WebSocket origin was unavailable during startup: {0}",
                        failure.Message);
                return;
            }
        }
        finally
        {
            using (_sync.EnterScope())
                _isStarting = false;
        }

        throw new AggregateException(
            "Unable to connect to any Chainlink WebSocket origin.", failures);
    }

    public void Stop()
    {
        using (_sync.EnterScope())
            _isRunning = false;
        foreach (var connection in _connections)
            connection.Stop();
    }

    public async ValueTask DisconnectAsync()
    {
        Stop();
        foreach (var connection in _connections)
            await connection.DisconnectAsync();
        using (_sync.EnterScope())
            _terminal.Clear();
    }

    private static async Task<ConnectionOutcome> ConnectOneAsync(
        ChainlinkSocketConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ConnectAsync(cancellationToken);
            return new(connection, true, null);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            return new(connection, false, error);
        }
    }

    private ValueTask OnMessageReceivedAsync(
        ChainlinkReportEnvelope report, CancellationToken cancellationToken)
        => ReportReceived is { } handler
            ? handler(this, report, cancellationToken)
            : default;

    private ValueTask OnErrorAsync(ChainlinkSocketConnection connection,
        Exception error, bool isTerminal, CancellationToken cancellationToken)
    {
        var isPoolTerminal = false;
        bool isStarting;
        if (isTerminal)
        {
            using (_sync.EnterScope())
            {
                _terminal.Add(connection);
                isPoolTerminal = _terminal.Count == _connections.Length;
                if (isPoolTerminal)
                    _isRunning = false;
                isStarting = _isStarting;
            }
        }
        else
        {
            using (_sync.EnterScope())
                isStarting = _isStarting;
        }
        return !isStarting && Error is { } handler
            ? handler(this, error, isPoolTerminal, cancellationToken)
            : default;
    }

    protected override void DisposeManaged()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        DisconnectAsync().AsTask().GetAwaiter().GetResult();
        foreach (var connection in _connections)
        {
            connection.MessageReceived -= OnMessageReceivedAsync;
            connection.Error -= OnErrorAsync;
            connection.Dispose();
        }
        base.DisposeManaged();
    }
}

sealed class ChainlinkSocketConnection : BaseLogReceiver
{
    private const int _maximumMessageLength = 16 * 1024 * 1024;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private readonly Uri _endpoint;
    private readonly string _feedId;
    private readonly string _origin;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly int _maximumAttempts;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _initialConnection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly JsonSerializerSettings _settings = new()
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };
    private ClientWebSocket _socket;
    private Task _runTask;
    private bool _isDisposed;

    public ChainlinkSocketConnection(string endpoint, string feedId,
        string origin, SecureString key, SecureString secret, int maximumAttempts)
    {
        _endpoint = ChainlinkExtensions.ValidateWebSocketEndpoint(endpoint);
        _feedId = feedId.ParseFeed().FeedId;
        _origin = origin?.Trim() ?? string.Empty;
        if (_origin.Length > 512 || _origin.Any(char.IsControl))
            throw new ArgumentException("Chainlink WebSocket origin is invalid.",
                nameof(origin));
        if (key.IsEmpty())
            throw new ArgumentNullException(nameof(key));
        if (secret.IsEmpty())
            throw new ArgumentNullException(nameof(secret));
        _apiKey = ValidateCredential(key.UnSecure(), "API key", nameof(key));
        _apiSecret = ValidateCredential(secret.UnSecure(), "API secret",
            nameof(secret));
        _maximumAttempts = Math.Max(1, maximumAttempts);
    }

    public override string Name => "Chainlink_Data_Streams_WS_" +
        (_origin.IsEmpty() ? _endpoint.Host : _origin);

    public event Func<ChainlinkReportEnvelope, CancellationToken, ValueTask>
        MessageReceived;
    public event Func<ChainlinkSocketConnection, Exception, bool,
        CancellationToken, ValueTask> Error;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_runTask is not null)
            throw new InvalidOperationException(
                "Chainlink WebSocket is already running.");
        _runTask = RunAsync(_cancellation.Token);
        await _initialConnection.Task.WaitAsync(cancellationToken);
    }

    public void Stop() => _cancellation.Cancel();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        var isEverConnected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var streamUri = CreateStreamUri();
                using var socket = new ClientWebSocket();
                _socket = socket;
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(2);
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(2);
                var authentication = ChainlinkAuthenticator.Create(HttpMethod.Get,
                    streamUri, ReadOnlySpan<byte>.Empty, _apiKey, _apiSecret,
                    DateTime.UtcNow);
                ChainlinkAuthenticator.Apply(socket.Options, authentication);
                socket.Options.SetRequestHeader("User-Agent",
                    "StockSharp-ChainlinkDataStreams/1.0");
                if (!_origin.IsEmpty())
                    socket.Options.SetRequestHeader("X-Cll-Origin", _origin);
                await socket.ConnectAsync(streamUri, cancellationToken);
                failures = 0;
                isEverConnected = true;
                _initialConnection.TrySetResult();
                await ReceiveAsync(socket, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                    throw new WebSocketException(
                        "Chainlink WebSocket closed unexpectedly.");
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                failures++;
                var isTerminal = failures >= _maximumAttempts;
                var redacted = Redact(error);
                if (Error is { } handler)
                    await handler(this, redacted, isTerminal,
                        CancellationToken.None);
                if (isTerminal)
                {
                    if (!isEverConnected)
                        _initialConnection.TrySetException(redacted);
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(
                    Math.Min(30, 1 << Math.Min(failures, 5))), cancellationToken);
            }
            finally
            {
                _socket = null;
            }
        }
        if (!_initialConnection.Task.IsCompleted)
            _initialConnection.TrySetCanceled(cancellationToken);
    }

    private async Task ReceiveAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open &&
            !cancellationToken.IsCancellationRequested)
        {
            var content = await ReceiveTextAsync(socket, cancellationToken);
            ChainlinkStreamMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<ChainlinkStreamMessage>(
                    content, _settings);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    "Chainlink WebSocket returned invalid JSON.", error);
            }
            if (message?.Report is null || message.Report.FeedId.IsEmpty() ||
                message.Report.FullReport.IsEmpty())
                throw new InvalidDataException(
                    "Chainlink WebSocket returned an incomplete report.");
            if (MessageReceived is { } handler)
                await handler(message.Report, cancellationToken);
        }
    }

    private static async ValueTask<string> ReceiveTextAsync(
        ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var chunk = new byte[16384];
        using var buffer = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException(
                    $"Chainlink WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException(
                    $"Chainlink WebSocket returned unexpected {result.MessageType} data.");
            if (buffer.Length + result.Count > _maximumMessageLength)
                throw new InvalidDataException(
                    "Chainlink WebSocket message exceeds 16 MiB.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage)
            {
                try
                {
                    return _strictUtf8.GetString(buffer.GetBuffer(), 0,
                        checked((int)buffer.Length));
                }
                catch (DecoderFallbackException error)
                {
                    throw new InvalidDataException(
                        "Chainlink WebSocket returned invalid UTF-8.", error);
                }
            }
        }
    }

    private Uri CreateStreamUri()
    {
        var builder = new UriBuilder(_endpoint)
        {
            Path = "/api/v2/ws",
            Query = "feedIDs=" + Uri.EscapeDataString(_feedId),
            Fragment = string.Empty,
        };
        return builder.Uri;
    }

    public async ValueTask DisconnectAsync()
    {
        Stop();
        var socket = _socket;
        if (socket?.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                    "Client disconnect", CancellationToken.None);
            }
            catch (Exception error) when (error is WebSocketException or
                ObjectDisposedException)
            {
            }
        }
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private IOException Redact(Exception error)
        => new(error.Message
            .Replace(_apiKey, "***", StringComparison.Ordinal)
            .Replace(_apiSecret, "***", StringComparison.Ordinal));

    private static string ValidateCredential(string value, string name,
        string parameterName)
    {
        value = value?.Trim();
        if (value.IsEmpty() || value.Length > 8192 || value.Any(char.IsControl))
            throw new ArgumentException($"Chainlink {name} is empty or invalid.",
                parameterName);
        return value;
    }

    protected override void DisposeManaged()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        DisconnectAsync().AsTask().GetAwaiter().GetResult();
        _cancellation.Dispose();
        base.DisposeManaged();
    }
}
