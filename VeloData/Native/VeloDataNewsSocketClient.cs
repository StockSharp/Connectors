namespace StockSharp.VeloData.Native;

sealed class VeloDataNewsSocketClient : BaseLogReceiver
{
    private const int _maximumMessageLength = 8 * 1024 * 1024;
    private static readonly JsonSerializerSettings _settings = new()
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly Uri _endpoint;
    private readonly string _authorization;
    private readonly int _maximumAttempts;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _initialConnection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ClientWebSocket _socket;
    private Task _runTask;

    public VeloDataNewsSocketClient(string endpoint, SecureString apiKey,
        int maximumAttempts)
    {
        _endpoint = ValidateEndpoint(endpoint);
        if (apiKey.IsEmpty())
            throw new ArgumentNullException(nameof(apiKey));
        var key = apiKey.UnSecure().Trim();
        if (key.IsEmpty() || key.Length > 4096 || key.Any(char.IsControl))
            throw new ArgumentException(
                "Velo Data API key is empty or invalid.", nameof(apiKey));
        _authorization = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes("api:" + key));
        _maximumAttempts = Math.Max(1, maximumAttempts);
    }

    public override string Name => "VeloData_NEWS_WS";

    public bool IsStopped => _runTask?.IsCompleted == true;

    public event Func<VeloDataNewsStory, CancellationToken, ValueTask> NewsReceived;
    public event Func<Exception, bool, CancellationToken, ValueTask> Error;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
            throw new InvalidOperationException(
                "Velo Data News WebSocket is already running.");
        _runTask = RunAsync(_cancellation.Token);
        await _initialConnection.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisconnectAsync()
    {
        _cancellation.Cancel();
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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        var isEverConnected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                _socket = socket;
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.SetRequestHeader("Authorization", _authorization);
                socket.Options.SetRequestHeader("User-Agent",
                    "StockSharp-VeloData/1.0");
                await socket.ConnectAsync(_endpoint, cancellationToken);
                await SendTextAsync(socket, "subscribe news_priority",
                    cancellationToken);
                failures = 0;
                isEverConnected = true;
                _initialConnection.TrySetResult();
                await ReceiveAsync(socket, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                    throw new WebSocketException(
                        "Velo Data News WebSocket closed unexpectedly.");
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
                await InvokeAsync(Error, error, isTerminal, CancellationToken.None);
                if (isTerminal)
                {
                    if (!isEverConnected)
                        _initialConnection.TrySetException(error);
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
            if (IsSignal(content, "connected") || IsSignal(content, "heartbeat"))
                continue;
            if (IsSignal(content, "closed"))
                throw new WebSocketException(
                    "Velo Data News WebSocket sent a closed signal.");

            VeloDataNewsStory story;
            try
            {
                story = JsonConvert.DeserializeObject<VeloDataNewsStory>(content,
                    _settings);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    "Velo Data News WebSocket returned invalid JSON.", error);
            }
            if (story?.IsHeartbeat == true)
                continue;
            if (story is null || story.Id is null)
                throw new InvalidDataException(
                    "Velo Data News WebSocket returned an incomplete event.");
            await InvokeAsync(NewsReceived, story, cancellationToken);
        }
    }

    private static bool IsSignal(string content, string signal)
        => content.EqualsIgnoreCase(signal) ||
            content.EqualsIgnoreCase('"' + signal + '"');

    private static async ValueTask SendTextAsync(ClientWebSocket socket,
        string content, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true,
            cancellationToken);
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
                    $"Velo Data News WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException(
                    $"Velo Data News WebSocket returned unexpected {result.MessageType} data.");
            if (buffer.Length + result.Count > _maximumMessageLength)
                throw new InvalidDataException(
                    "Velo Data News WebSocket message exceeds 8 MiB.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0,
                    checked((int)buffer.Length));
        }
    }

    private static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() ||
            !uri.Query.IsEmpty() || !uri.Fragment.IsEmpty() ||
            !uri.AbsolutePath.EndsWith("/api/w/connect",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "Velo Data News WebSocket endpoint must be an absolute WSS '/api/w/connect' URI without credentials, query, or fragment.",
                nameof(endpoint));
        return uri;
    }

    private static ValueTask InvokeAsync(
        Func<VeloDataNewsStory, CancellationToken, ValueTask> handler,
        VeloDataNewsStory value, CancellationToken cancellationToken)
        => handler is null ? default : handler(value, cancellationToken);

    private static ValueTask InvokeAsync(
        Func<Exception, bool, CancellationToken, ValueTask> handler,
        Exception error, bool isTerminal, CancellationToken cancellationToken)
        => handler is null ? default : handler(error, isTerminal,
            cancellationToken);

    protected override void DisposeManaged()
    {
        DisconnectAsync().AsTask().GetAwaiter().GetResult();
        _cancellation.Dispose();
        base.DisposeManaged();
    }
}
