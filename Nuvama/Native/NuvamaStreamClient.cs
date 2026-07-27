namespace StockSharp.Nuvama.Native;

sealed class NuvamaStreamClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly string _host;
    private readonly int _port;
    private readonly string _accountType;
    private readonly string _accountId;
    private readonly string _userId;
    private readonly int _reconnectAttempts;
    private readonly object _sync = new();
    private readonly HashSet<string> _quoteSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _depthSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource _cancellation;
    private Task _runTask;
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private string _appIdKey;
    private bool _ordersSubscribed;
    private bool _wasConnected;

    public NuvamaStreamClient(
        string host,
        int port,
        string accountType,
        string accountId,
        string userId,
        string appIdKey,
        int reconnectAttempts)
    {
        _host = host.ThrowIfEmpty(nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, null);
        _port = port;
        _accountType = accountType.IsEmpty("EQ").ToUpperInvariant();
        _accountId = accountId.ThrowIfEmpty(nameof(accountId));
        _userId = userId.ThrowIfEmpty(nameof(userId));
        _appIdKey = appIdKey.ThrowIfEmpty(nameof(appIdKey));
        if (reconnectAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectAttempts),
                reconnectAttempts,
                "Reconnect attempts cannot be negative.");
        }
        _reconnectAttempts = reconnectAttempts;
    }

    public override string Name => nameof(Nuvama) + "_" +
        nameof(NuvamaStreamClient);

    public event Func<NuvamaQuote, CancellationToken, ValueTask> QuoteReceived;

    public event Func<NuvamaDepth, CancellationToken, ValueTask> DepthReceived;

    public event Func<JToken, CancellationToken, ValueTask> OrderReceived;

    public event Func<Exception, CancellationToken, ValueTask> Error;

    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        _cancellation?.Cancel();
        _tcpClient?.Dispose();
        _cancellation?.Dispose();
        _sendLock.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask Connect(CancellationToken cancellationToken)
    {
        if (_runTask != null)
            throw new InvalidOperationException("Nuvama stream is already running.");

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _runTask = Run(completion, _cancellation.Token);
        await completion.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask Disconnect(CancellationToken cancellationToken)
    {
        if (_runTask == null)
            return;

        await NotifyState(ConnectionStates.Disconnecting, cancellationToken);
        _cancellation.Cancel();
        _tcpClient?.Dispose();
        try
        {
            await _runTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (
            _cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _runTask = null;
            _stream = null;
            _tcpClient = null;
            _cancellation.Dispose();
            _cancellation = null;
        }
        await NotifyState(ConnectionStates.Disconnected, cancellationToken);
    }

    public void SetAppIdKey(string appIdKey)
    {
        if (!appIdKey.IsEmpty())
            _appIdKey = appIdKey;
    }

    public async ValueTask SubscribeQuotes(
        string streamingSymbol,
        CancellationToken cancellationToken)
    {
        streamingSymbol.ThrowIfEmpty(nameof(streamingSymbol));
        var added = false;
        lock (_sync)
            added = _quoteSubscriptions.Add(streamingSymbol);
        if (added)
        {
            await SendSubscription(
                "quote",
                [streamingSymbol],
                true,
                cancellationToken);
        }
    }

    public async ValueTask UnsubscribeQuotes(
        string streamingSymbol,
        CancellationToken cancellationToken)
    {
        var removed = false;
        lock (_sync)
            removed = _quoteSubscriptions.Remove(streamingSymbol);
        if (removed && IsConnected)
        {
            await SendSubscription(
                "quote",
                [streamingSymbol],
                false,
                cancellationToken);
        }
    }

    public async ValueTask SubscribeDepth(
        string streamingSymbol,
        CancellationToken cancellationToken)
    {
        streamingSymbol.ThrowIfEmpty(nameof(streamingSymbol));
        var added = false;
        lock (_sync)
            added = _depthSubscriptions.Add(streamingSymbol);
        if (added)
        {
            await SendSubscription(
                "quote2",
                [streamingSymbol],
                true,
                cancellationToken);
        }
    }

    public async ValueTask UnsubscribeDepth(
        string streamingSymbol,
        CancellationToken cancellationToken)
    {
        var removed = false;
        lock (_sync)
            removed = _depthSubscriptions.Remove(streamingSymbol);
        if (removed && IsConnected)
        {
            await SendSubscription(
                "quote2",
                [streamingSymbol],
                false,
                cancellationToken);
        }
    }

    public async ValueTask SubscribeOrders(
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_ordersSubscribed)
                return;
            _ordersSubscribed = true;
        }
        await SendOrderSubscription(true, cancellationToken);
    }

    public async ValueTask UnsubscribeOrders(
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_ordersSubscribed)
                return;
            _ordersSubscribed = false;
        }
        if (IsConnected)
            await SendOrderSubscription(false, cancellationToken);
    }

    public ValueTask SendHeartbeat(CancellationToken cancellationToken)
        => IsConnected
            ? SendRaw("{}", cancellationToken)
            : default;

    internal JObject CreateMarketSubscriptionRequest(
        string streamingType,
        IEnumerable<string> symbols,
        bool subscribe)
    {
        var request = new JObject
        {
            ["request"] = new JObject
            {
                ["streaming_type"] = streamingType,
                ["data"] = new JObject
                {
                    ["accType"] = _accountType,
                    ["symbols"] = new JArray(
                        symbols.Select(symbol => new JObject
                        {
                            ["symbol"] = symbol.Trim(),
                        })),
                },
                ["appID"] = _appIdKey,
                ["response_format"] = "json",
                ["request_type"] =
                    subscribe ? "subscribe" : "unsubscribe",
            },
            ["echo"] = new JObject(),
        };
        return request;
    }

    internal JObject CreateOrderSubscriptionRequest(bool subscribe)
        => new()
        {
            ["request"] = new JObject
            {
                ["streaming_type"] = "orderFiler",
                ["data"] = new JObject
                {
                    ["accType"] = _accountType,
                    ["userID"] = _userId,
                    ["accID"] = _accountId,
                    ["responseType"] = new JArray(
                        "ORDER_UPDATE",
                        "TRADE_UPDATE"),
                },
                ["appID"] = _appIdKey,
                ["response_format"] = "json",
                ["request_type"] =
                    subscribe ? "subscribe" : "unsubscribe",
            },
            ["echo"] = new JObject(),
        };

    internal static string[] ExtractJsonFrames(StringBuilder buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var result = new List<string>();
        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;
        var consumed = 0;

        for (var index = 0; index < buffer.Length; index++)
        {
            var character = buffer[index];
            if (start < 0)
            {
                if (character == '{')
                {
                    start = index;
                    depth = 1;
                }
                else if (!char.IsWhiteSpace(character))
                {
                    throw new InvalidDataException(
                        "Nuvama stream contained data outside a JSON object.");
                }
                continue;
            }

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == '"')
                    inString = false;
                continue;
            }

            if (character == '"')
                inString = true;
            else if (character == '{')
                depth++;
            else if (character == '}' && --depth == 0)
            {
                result.Add(buffer.ToString(start, index - start + 1));
                consumed = index + 1;
                start = -1;
            }
        }

        if (consumed > 0)
            buffer.Remove(0, consumed);
        if (buffer.Length > 4 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Nuvama stream JSON frame exceeded four megabytes.");
        }
        return [.. result];
    }

    internal async ValueTask ProcessFrame(
        string frame,
        CancellationToken cancellationToken)
    {
        JObject envelope;
        try
        {
            envelope = JObject.Parse(frame);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Nuvama stream returned invalid JSON.",
                error);
        }

        var response = NuvamaExtensions.FindToken(
            envelope,
            "response") as JObject;
        if (response == null)
            return;
        var streamingType = NuvamaExtensions.FindString(
            response,
            "streaming_type");
        var data = NuvamaExtensions.FindToken(response, "data");
        if (data == null)
            return;

        var items = data is JArray array ? array : new JArray(data);
        switch (streamingType?.ToLowerInvariant())
        {
            case "quote":
            case "miniquote":
                if (QuoteReceived is { } quoteHandler)
                {
                    foreach (var item in items)
                    {
                        var quote = item.ToObject<NuvamaQuote>();
                        if (quote != null && !quote.Symbol.IsEmpty())
                            await quoteHandler(quote, cancellationToken);
                    }
                }
                break;

            case "quote2":
                if (DepthReceived is { } depthHandler)
                {
                    foreach (var item in items)
                    {
                        var depth = item.ToObject<NuvamaDepth>();
                        if (depth != null)
                            await depthHandler(depth, cancellationToken);
                    }
                }
                break;

            case "orderfiler":
                if (OrderReceived is { } orderHandler)
                {
                    foreach (var item in items)
                        await orderHandler(item, cancellationToken);
                }
                break;

            default:
                this.AddVerboseLog(
                    "Ignored Nuvama stream type {0}.",
                    streamingType);
                break;
        }
    }

    private bool IsConnected => _stream != null && _tcpClient?.Connected == true;

    private async Task Run(
        TaskCompletionSource<bool> initialCompletion,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;
        for (var attempt = 0;
            !cancellationToken.IsCancellationRequested &&
            attempt <= _reconnectAttempts;
            attempt++)
        {
            try
            {
                await NotifyState(
                    attempt == 0
                        ? ConnectionStates.Connecting
                        : ConnectionStates.Reconnecting,
                    cancellationToken);
                using var client = new TcpClient();
                _tcpClient = client;
                await client.ConnectAsync(
                    _host,
                    _port,
                    cancellationToken);
                _stream = client.GetStream();
                await NotifyState(
                    _wasConnected
                        ? ConnectionStates.Restored
                        : ConnectionStates.Connected,
                    cancellationToken);
                _wasConnected = true;
                await RestoreSubscriptions(cancellationToken);
                initialCompletion.TrySetResult(true);
                await ReceiveLoop(_stream, cancellationToken);
                throw new IOException("Nuvama stream closed the TCP connection.");
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                lastError = error;
                _stream = null;
                _tcpClient = null;
                if (Error is { } errorHandler)
                    await errorHandler(error, cancellationToken);
                if (attempt >= _reconnectAttempts)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            finally
            {
                _stream = null;
                _tcpClient = null;
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            var error = lastError ?? new IOException(
                "Nuvama stream connection failed.");
            initialCompletion.TrySetException(error);
            await NotifyState(ConnectionStates.Failed, cancellationToken);
        }
        else
        {
            initialCompletion.TrySetCanceled(cancellationToken);
        }
    }

    private async Task ReceiveLoop(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[16 * 1024];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var decoder = Encoding.UTF8.GetDecoder();
        var buffer = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var count = await stream.ReadAsync(bytes, cancellationToken);
            if (count == 0)
                return;
            var chars = decoder.GetChars(bytes, 0, count, characters, 0);
            buffer.Append(characters, 0, chars);
            foreach (var frame in ExtractJsonFrames(buffer))
                await ProcessFrame(frame, cancellationToken);
        }
    }

    private async ValueTask RestoreSubscriptions(
        CancellationToken cancellationToken)
    {
        string[] quotes;
        string[] depths;
        bool orders;
        lock (_sync)
        {
            quotes = [.. _quoteSubscriptions];
            depths = [.. _depthSubscriptions];
            orders = _ordersSubscribed;
        }

        if (quotes.Length > 0)
            await SendSubscription("quote", quotes, true, cancellationToken);
        if (depths.Length > 0)
            await SendSubscription("quote2", depths, true, cancellationToken);
        if (orders)
            await SendOrderSubscription(true, cancellationToken);
    }

    private ValueTask SendSubscription(
        string streamingType,
        IEnumerable<string> symbols,
        bool subscribe,
        CancellationToken cancellationToken)
        => SendRaw(
            CreateMarketSubscriptionRequest(
                streamingType,
                symbols,
                subscribe).ToString(Formatting.None),
            cancellationToken);

    private ValueTask SendOrderSubscription(
        bool subscribe,
        CancellationToken cancellationToken)
        => SendRaw(
            CreateOrderSubscriptionRequest(subscribe)
                .ToString(Formatting.None),
            cancellationToken);

    private async ValueTask SendRaw(
        string json,
        CancellationToken cancellationToken)
    {
        var stream = _stream ??
            throw new InvalidOperationException(
                "Nuvama stream is not connected.");
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private ValueTask NotifyState(
        ConnectionStates state,
        CancellationToken cancellationToken)
        => StateChanged is { } handler
            ? handler(state, cancellationToken)
            : default;
}
