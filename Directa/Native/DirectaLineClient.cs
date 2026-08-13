namespace StockSharp.Directa.Native;

sealed class DirectaLineClient : BaseLogReceiver
{
    private const int _maxLineLength = 1024 * 1024;

    private readonly EndPoint _address;
    private readonly string _name;
    private readonly SemaphoreSlim _writeSync = new(1, 1);
    private TcpClient _client;
    private StreamReader _reader;
    private StreamWriter _writer;
    private CancellationTokenSource _lifetime;
    private Task _receiveTask;
    private bool _stopping;

    public DirectaLineClient(
        EndPoint address, string name)
    {
        _address = address ??
            throw new ArgumentNullException(nameof(address));
        _name = name.ThrowIfEmpty(nameof(name));
        _ = GetAddress(address);
    }

    public override string Name => _name;

    public bool IsConnected => _client is not null;

    public event Func<string,
        CancellationToken, ValueTask> LineReceived;
    public event Func<Exception,
        CancellationToken, ValueTask> Error;

    public async Task Connect(
        CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }

        var (host, port) = GetAddress(_address);
        _stopping = false;
        _client = new() { NoDelay = true };
        try
        {
            await _client.ConnectAsync(
                host, port, cancellationToken);
            var stream = _client.GetStream();
            _reader = new(
                stream, new UTF8Encoding(false, true),
                true, 64 * 1024, true);
            _writer = new(
                stream, new UTF8Encoding(false),
                64 * 1024, true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            _lifetime = new();
            _receiveTask = ReceiveLoop(_lifetime.Token);
        }
        catch
        {
            CloseSocket();
            throw;
        }
    }

    public async ValueTask Send(
        string command,
        CancellationToken cancellationToken)
    {
        command = command.ThrowIfEmpty(nameof(command));
        if (command.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException(
                "Directa command cannot contain a newline.",
                nameof(command));
        }

        await _writeSync.WaitAsync(cancellationToken);
        try
        {
            var writer = _writer ??
                throw new InvalidOperationException(
                    LocalizedStrings.ConnectionNotOk);
            this.AddVerboseLog("TX {0}", command);
            await writer.WriteLineAsync(
                command.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeSync.Release();
        }
    }

    public async Task Disconnect(
        CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        _stopping = true;
        _lifetime?.Cancel();
        CloseSocket();

        var receiveTask = _receiveTask;
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.WaitAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested ||
                    _lifetime?.IsCancellationRequested == true)
            {
            }
            catch (Exception) when (_stopping)
            {
            }
        }

        _receiveTask = null;
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private async Task ReceiveLoop(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(
                    cancellationToken);
                if (line is null)
                {
                    throw new EndOfStreamException(
                        $"{Name} closed the connection.");
                }
                if (line.Length > _maxLineLength)
                {
                    throw new InvalidDataException(
                        $"{Name} line exceeded 1 MiB.");
                }

                line = line.Trim();
                if (line.IsEmpty())
                    continue;
                this.AddVerboseLog("RX {0}", line);
                if (line == "H")
                {
                    await Send("H", cancellationToken);
                    continue;
                }
                if (LineReceived is { } handler)
                {
                    await handler.InvokeAsync(line, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_stopping && Error is { } handler)
                await handler.InvokeAsync(error, CancellationToken.None);
        }
    }

    private static (string Host, int Port) GetAddress(
        EndPoint address)
        => address switch
        {
            DnsEndPoint dns when
                dns.Port is > IPEndPoint.MinPort and
                    <= IPEndPoint.MaxPort
                => (dns.Host.ThrowIfEmpty(
                    nameof(dns.Host)), dns.Port),
            IPEndPoint ip when
                ip.Port is > IPEndPoint.MinPort and
                    <= IPEndPoint.MaxPort
                => (ip.Address.ToString(), ip.Port),
            _ => throw new ArgumentException(
                "Directa address must be an IP or DNS endpoint with a valid port.",
                nameof(address)),
        };

    private void CloseSocket()
    {
        _reader?.Dispose();
        _reader = null;
        _writer?.Dispose();
        _writer = null;
        _client?.Dispose();
        _client = null;
    }

    protected override void DisposeManaged()
    {
        _stopping = true;
        _lifetime?.Cancel();
        CloseSocket();
        _lifetime?.Dispose();
        _lifetime = null;
        _receiveTask = null;
        _writeSync.Dispose();
        base.DisposeManaged();
    }
}
