namespace StockSharp.MasterLink.Native;

internal sealed class MasterLinkGatewayClient : Disposable
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly string _nodePath;
    private readonly string _gatewayDirectory;
    private readonly ConcurrentDictionary<long,
        TaskCompletionSource<MasterLinkGatewayMessage>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process _process;
    private StreamWriter _writer;
    private CancellationTokenSource _receiveCts;
    private Task _receiveTask;
    private Task _errorTask;
    private long _requestId;

    public MasterLinkGatewayClient(string nodePath, string gatewayDirectory)
    {
        _nodePath = nodePath.ThrowIfEmpty(nameof(nodePath));
        _gatewayDirectory =
            gatewayDirectory.ThrowIfEmpty(nameof(gatewayDirectory));
    }

    public event Func<long, string, JToken, CancellationToken, ValueTask>
        MarketDataReceived;
    public event Func<JToken, CancellationToken, ValueTask> OrderReceived;
    public event Func<JToken, CancellationToken, ValueTask> FillReceived;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<Exception, CancellationToken, ValueTask> Disconnected;
    public event Func<int, string, CancellationToken, ValueTask> Log;

    public MasterLinkConnectResult Connection { get; private set; }

    public async Task<MasterLinkConnectResult> Connect(
        string login,
        SecureString password,
        string certificatePath,
        SecureString certificatePassword,
        string account,
        bool registerApiAuth,
        MasterLinkMarketDataModes mode,
        CancellationToken cancellationToken)
    {
        if (_process != null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        var secrets = new Dictionary<string, object>
        {
            ["personal_id"] = login.ThrowIfEmpty(nameof(login)),
            ["password"] = password?.UnSecure().ThrowIfEmpty(nameof(password)),
            ["certificate_path"] =
                certificatePath.ThrowIfEmpty(nameof(certificatePath)),
            ["certificate_password"] =
                certificatePassword?.UnSecure().ThrowIfEmpty(
                    nameof(certificatePassword)),
            ["account"] = account,
            ["register_api_auth"] = registerApiAuth,
            ["mode"] = mode == MasterLinkMarketDataModes.Speed
                ? "Speed"
                : "Normal",
        };

        StartProcess();
        try
        {
            Connection = await Request<MasterLinkConnectResult>(
                "connect", secrets, cancellationToken);
            return Connection;
        }
        catch
        {
            CloseProcess();
            throw;
        }
        finally
        {
            secrets["password"] = null;
            secrets["certificate_password"] = null;
        }
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        if (_process == null)
            return;
        try
        {
            await Request<JToken>(
                "disconnect", null, cancellationToken);
        }
        catch (Exception error) when (
            error is IOException or ObjectDisposedException or
                InvalidOperationException)
        {
        }
        finally
        {
            CloseProcess();
        }
    }

    public Task<MasterLinkSecurity[]> Lookup(
        string query,
        string board,
        int limit,
        CancellationToken cancellationToken)
        => Request<MasterLinkSecurity[]>(
            "lookup",
            new
            {
                query,
                board,
                limit,
            },
            cancellationToken);

    public Task<MasterLinkSecurity> GetSecurity(
        string symbol,
        bool oddLot,
        CancellationToken cancellationToken)
        => Request<MasterLinkSecurity>(
            "ticker",
            new
            {
                symbol,
                odd_lot = oddLot,
            },
            cancellationToken);

    public Task<MasterLinkQuote> GetQuote(
        string symbol,
        bool oddLot,
        CancellationToken cancellationToken)
        => Request<MasterLinkQuote>(
            "quote",
            new
            {
                symbol,
                odd_lot = oddLot,
            },
            cancellationToken);

    public Task<MasterLinkTrade[]> GetTrades(
        string symbol,
        bool oddLot,
        int limit,
        CancellationToken cancellationToken)
        => Request<MasterLinkTrade[]>(
            "trades",
            new
            {
                symbol,
                odd_lot = oddLot,
                limit,
            },
            cancellationToken);

    public Task<MasterLinkCandleResponse> GetCandles(
        string symbol,
        DateTime? from,
        DateTime? to,
        string timeFrame,
        bool adjusted,
        CancellationToken cancellationToken)
        => Request<MasterLinkCandleResponse>(
            "candles",
            new
            {
                symbol,
                from = FormatDate(from),
                to = FormatDate(to),
                timeframe = timeFrame,
                adjusted,
            },
            cancellationToken);

    public Task Subscribe(
        long subscriptionId,
        string dataKind,
        string symbol,
        bool oddLot,
        CancellationToken cancellationToken)
        => Request<JToken>(
            "subscribe",
            new
            {
                subscription_id = subscriptionId,
                data_kind = dataKind,
                symbol,
                odd_lot = oddLot,
            },
            cancellationToken);

    public Task Unsubscribe(
        long subscriptionId,
        CancellationToken cancellationToken)
        => Request<JToken>(
            "unsubscribe",
            new { subscription_id = subscriptionId },
            cancellationToken);

    public Task<MasterLinkOrderRecord[]> GetOrders(
        string symbol,
        string queryType,
        CancellationToken cancellationToken)
        => Request<MasterLinkOrderRecord[]>(
            "orders",
            new
            {
                symbol,
                query_type = queryType,
            },
            cancellationToken);

    public Task<MasterLinkFill[]> GetFills(
        string symbol,
        CancellationToken cancellationToken)
        => Request<MasterLinkFill[]>(
            "fills",
            new { symbol },
            cancellationToken);

    public Task<MasterLinkOrderResponse> PlaceOrder(
        MasterLinkOrderRequest order,
        CancellationToken cancellationToken)
        => Request<MasterLinkOrderResponse>(
            "place_order", order, cancellationToken);

    public Task<MasterLinkModifiedResponse> ModifyPrice(
        string orderNo,
        string sequenceNo,
        decimal price,
        string priceType,
        CancellationToken cancellationToken)
        => Request<MasterLinkModifiedResponse>(
            "modify_price",
            new
            {
                order_no = orderNo,
                sequence_no = sequenceNo,
                price = price.ToString(CultureInfo.InvariantCulture),
                price_type = priceType,
            },
            cancellationToken);

    public Task<MasterLinkModifiedResponse> ModifyVolume(
        string orderNo,
        string sequenceNo,
        int decreaseQuantity,
        CancellationToken cancellationToken)
        => Request<MasterLinkModifiedResponse>(
            "modify_volume",
            new
            {
                order_no = orderNo,
                sequence_no = sequenceNo,
                decrease_quantity = decreaseQuantity,
            },
            cancellationToken);

    public Task<MasterLinkModifiedResponse> CancelOrder(
        string orderNo,
        string sequenceNo,
        CancellationToken cancellationToken)
        => Request<MasterLinkModifiedResponse>(
            "cancel_order",
            new
            {
                order_no = orderNo,
                sequence_no = sequenceNo,
            },
            cancellationToken);

    public Task<MasterLinkPortfolioSnapshot> GetPortfolio(
        CancellationToken cancellationToken)
        => Request<MasterLinkPortfolioSnapshot>(
            "portfolio", null, cancellationToken);

    public Task Ping(CancellationToken cancellationToken)
        => Request<JToken>("ping", null, cancellationToken);

    private void StartProcess()
    {
        var directory = Path.GetFullPath(_gatewayDirectory);
        var scriptPath = Path.Combine(
            directory, "masterlink_gateway.cjs");
        var packagePath = Path.Combine(directory, "package.json");
        var sdkPath = Path.Combine(
            directory,
            "node_modules",
            "taishin-sdk",
            "package.json");

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "The MasterLink typed Node gateway was not found.",
                scriptPath);
        }
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                "The MasterLink gateway package.json was not found.",
                packagePath);
        }
        if (!File.Exists(sdkPath))
        {
            throw new FileNotFoundException(
                "The official taishin-sdk package is not installed. Install the broker-downloaded tgz package in the configured gateway directory.",
                sdkPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _nodePath,
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.Environment["TZ"] = "Asia/Taipei";

        _process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Unable to start the MasterLink gateway process.");
        _writer = _process.StandardInput;
        _writer.AutoFlush = true;
        _receiveCts = new();
        _receiveTask = ReceiveLoop(
            _process.StandardOutput, _receiveCts.Token);
        _errorTask = ErrorLoop(
            _process.StandardError, _receiveCts.Token);
    }

    private async Task<T> Request<T>(
        string command,
        object data,
        CancellationToken cancellationToken)
    {
        if (_writer == null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);

        var request = new MasterLinkGatewayRequest
        {
            RequestId = Interlocked.Increment(ref _requestId),
            Command = command,
            Data = data,
        };
        var completion =
            new TaskCompletionSource<MasterLinkGatewayMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException(
                $"Duplicate MasterLink gateway request id {request.RequestId}.");
        }

        try
        {
            var line = JsonConvert.SerializeObject(
                request, Formatting.None, _jsonSettings);
            if (line.Length > MasterLinkGatewayProtocol.MaxMessageLength)
            {
                throw new InvalidDataException(
                    "MasterLink gateway request exceeded the 16 MiB limit.");
            }

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _writer.WriteLineAsync(
                    line.AsMemory(), cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            var response = await completion.Task.WaitAsync(cancellationToken);
            if (response.Error != null)
            {
                throw new MasterLinkGatewayException(
                    response.Error.Code,
                    response.Error.Message);
            }
            if (response.Data == null ||
                response.Data.Type == JTokenType.Null)
            {
                return default;
            }
            return response.Data.ToObject<T>();
        }
        finally
        {
            _pending.TryRemove(request.RequestId, out _);
        }
    }

    private async Task ReceiveLoop(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                    throw ProcessExitError();
                if (line.Length == 0)
                    continue;
                if (line.Length >
                    MasterLinkGatewayProtocol.MaxMessageLength)
                {
                    throw new InvalidDataException(
                        "MasterLink gateway response exceeded the 16 MiB limit.");
                }

                var message =
                    JsonConvert.DeserializeObject<MasterLinkGatewayMessage>(
                        line) ??
                    throw new InvalidDataException(
                        "MasterLink gateway returned empty JSON.");
                if (message.Version !=
                    MasterLinkGatewayProtocol.Version)
                {
                    throw new InvalidDataException(
                        $"Unsupported MasterLink gateway protocol version {message.Version}.");
                }

                await Dispatch(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            FailPending(error);
            if (Error != null)
                await Error(error, CancellationToken.None);
        }
    }

    private async Task ErrorLoop(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                    return;
                if (!line.IsEmpty() && Log != null)
                    await Log(3, line, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (
            error is IOException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested &&
                Error != null)
            {
                await Error(error, CancellationToken.None);
            }
        }
    }

    private async ValueTask Dispatch(
        MasterLinkGatewayMessage message,
        CancellationToken cancellationToken)
    {
        switch (message.Kind)
        {
            case MasterLinkGatewayMessageKinds.Response:
                if (_pending.TryGetValue(
                    message.RequestId, out var completion))
                {
                    completion.TrySetResult(message);
                }
                break;

            case MasterLinkGatewayMessageKinds.MarketData
                when message.SubscriptionId != null &&
                    message.Data != null &&
                    MarketDataReceived != null:
                await MarketDataReceived(
                    message.SubscriptionId.Value,
                    message.Channel,
                    message.Data,
                    cancellationToken);
                break;

            case MasterLinkGatewayMessageKinds.Order
                when message.Data != null &&
                    OrderReceived != null:
                await OrderReceived(message.Data, cancellationToken);
                break;

            case MasterLinkGatewayMessageKinds.Fill
                when message.Data != null &&
                    FillReceived != null:
                await FillReceived(message.Data, cancellationToken);
                break;

            case MasterLinkGatewayMessageKinds.Error
                when Error != null:
                await Error(
                    new MasterLinkGatewayException(
                        message.Error?.Code,
                        message.Error?.Message ??
                            "MasterLink gateway reported an error."),
                    cancellationToken);
                break;

            case MasterLinkGatewayMessageKinds.Disconnected
                when Disconnected != null:
                await Disconnected(
                    new IOException(
                        message.Error?.Message ??
                            "MasterLink gateway connection was lost."),
                    cancellationToken);
                break;

            case MasterLinkGatewayMessageKinds.Log
                when Log != null:
                await Log(
                    message.LogLevel ?? 1,
                    message.LogMessage,
                    cancellationToken);
                break;
        }
    }

    private Exception ProcessExitError()
    {
        var process = _process;
        if (process == null)
        {
            return new IOException(
                "MasterLink gateway closed its output stream.");
        }
        try
        {
            return process.HasExited
                ? new IOException(
                    $"MasterLink gateway exited with code {process.ExitCode}.")
                : new IOException(
                    "MasterLink gateway closed its output stream.");
        }
        catch (InvalidOperationException)
        {
            return new IOException(
                "MasterLink gateway process is no longer available.");
        }
    }

    private void FailPending(Exception error)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(error);
        _pending.Clear();
    }

    private void CloseProcess()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;
        _writer?.Dispose();
        _writer = null;
        _receiveTask = null;
        _errorTask = null;
        FailPending(new IOException(
            "MasterLink gateway connection was closed."));

        var process = _process;
        _process = null;
        if (process != null)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (Exception error) when (
                error is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
            {
            }
            process.Dispose();
        }
        Connection = null;
    }

    private static string FormatDate(DateTime? value)
        => value?.ToUniversalTime().ToString(
            "yyyy-MM-dd", CultureInfo.InvariantCulture);

    protected override void DisposeManaged()
    {
        CloseProcess();
        _writeLock.Dispose();
        base.DisposeManaged();
    }
}
