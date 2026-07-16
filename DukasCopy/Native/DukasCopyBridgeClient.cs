namespace StockSharp.DukasCopy.Native;

internal sealed class DukasCopyBridgeClient : Disposable
{
	private const int _maxMessageLength = 16 * 1024 * 1024;
	private readonly int _port;
	private readonly string _bridgeJarPath;
	private readonly ConcurrentDictionary<long, TaskCompletionSource<DukasCopyBridgeMessage>> _pending = new();
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private TcpClient _tcpClient;
	private StreamReader _reader;
	private StreamWriter _writer;
	private CancellationTokenSource _receiveCts;
	private Task _receiveTask;
	private Process _bridgeProcess;
	private long _requestId;

	public DukasCopyBridgeClient(int port, string bridgeJarPath)
	{
		if (port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
			throw new ArgumentOutOfRangeException(nameof(port), port, LocalizedStrings.InvalidValue);

		_port = port;
		_bridgeJarPath = bridgeJarPath;
	}

	public event Func<DukasCopyTick, CancellationToken, ValueTask> TickReceived;
	public event Func<DukasCopyBar, CancellationToken, ValueTask> BarReceived;
	public event Func<DukasCopyOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<DukasCopyAccount, CancellationToken, ValueTask> AccountReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task Connect(string userName, string password, bool isDemo,
		CancellationToken cancellationToken)
	{
		if (_tcpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		StartBridge();
		_tcpClient = new() { NoDelay = true };

		var deadline = DateTime.UtcNow + (_bridgeProcess == null ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30));
		while (true)
		{
			try
			{
				await _tcpClient.ConnectAsync(IPAddress.Loopback, _port, cancellationToken);
				break;
			}
			catch (SocketException) when (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
			{
				if (_bridgeProcess?.HasExited == true)
					throw new InvalidOperationException($"Dukascopy bridge exited with code {_bridgeProcess.ExitCode}.");
				await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
			}
		}

		var stream = _tcpClient.GetStream();
		_reader = new(stream, new UTF8Encoding(false), true, 1 << 16, true);
		_writer = new(stream, new UTF8Encoding(false), 1 << 16, true) { AutoFlush = true };
		_receiveCts = new();
		_receiveTask = ReceiveLoop(_receiveCts.Token);

		await Request(new()
		{
			Command = DukasCopyBridgeCommands.Connect,
			UserName = userName,
			Password = password,
			IsDemo = isDemo,
		}, cancellationToken);
	}

	public async Task Disconnect(CancellationToken cancellationToken)
	{
		if (_tcpClient == null)
			return;

		try
		{
			await Request(new() { Command = DukasCopyBridgeCommands.Disconnect }, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
		}
		finally
		{
			CloseConnection();
		}
	}

	public async Task<DukasCopyInstrument[]> GetInstruments(CancellationToken cancellationToken)
		=> (await Request(new() { Command = DukasCopyBridgeCommands.Instruments }, cancellationToken)).Instruments ?? [];

	public Task Subscribe(IEnumerable<string> symbols, CancellationToken cancellationToken)
		=> Request(new()
		{
			Command = DukasCopyBridgeCommands.Subscribe,
			Symbols = NormalizeSymbols(symbols),
		}, cancellationToken);

	public Task Unsubscribe(IEnumerable<string> symbols, CancellationToken cancellationToken)
		=> Request(new()
		{
			Command = DukasCopyBridgeCommands.Unsubscribe,
			Symbols = NormalizeSymbols(symbols),
		}, cancellationToken);

	public async Task<DukasCopyTick[]> GetTicks(string symbol, DateTime from, DateTime to, int count,
		CancellationToken cancellationToken)
		=> (await Request(new()
		{
			Command = DukasCopyBridgeCommands.HistoryTicks,
			Symbol = symbol,
			From = new DateTimeOffset(from.ToUniversalTime()).ToUnixTimeMilliseconds(),
			To = new DateTimeOffset(to.ToUniversalTime()).ToUnixTimeMilliseconds(),
			Count = count,
		}, cancellationToken)).Ticks ?? [];

	public async Task<DukasCopyBar[]> GetBars(string symbol, string period, DateTime from, DateTime to,
		int count, CancellationToken cancellationToken)
		=> (await Request(new()
		{
			Command = DukasCopyBridgeCommands.HistoryBars,
			Symbol = symbol,
			Period = period,
			From = new DateTimeOffset(from.ToUniversalTime()).ToUnixTimeMilliseconds(),
			To = new DateTimeOffset(to.ToUniversalTime()).ToUnixTimeMilliseconds(),
			Count = count,
		}, cancellationToken)).Bars ?? [];

	public async Task<DukasCopyOrder> PlaceOrder(DukasCopyBridgeRequest request,
		CancellationToken cancellationToken)
	{
		request.Command = DukasCopyBridgeCommands.PlaceOrder;
		return (await Request(request, cancellationToken)).Order ??
			throw new InvalidOperationException("Dukascopy returned no order after placement.");
	}

	public async Task<DukasCopyOrder> ReplaceOrder(DukasCopyBridgeRequest request,
		CancellationToken cancellationToken)
	{
		request.Command = DukasCopyBridgeCommands.ReplaceOrder;
		return (await Request(request, cancellationToken)).Order ??
			throw new InvalidOperationException("Dukascopy returned no order after replacement.");
	}

	public Task CancelOrder(string orderId, CancellationToken cancellationToken)
		=> Request(new() { Command = DukasCopyBridgeCommands.CancelOrder, OrderId = orderId }, cancellationToken);

	public async Task<DukasCopyOrder[]> GetOrders(CancellationToken cancellationToken)
		=> (await Request(new() { Command = DukasCopyBridgeCommands.Orders }, cancellationToken)).Orders ?? [];

	public async Task<DukasCopyAccount> GetAccount(CancellationToken cancellationToken)
		=> (await Request(new() { Command = DukasCopyBridgeCommands.Account }, cancellationToken)).Account;

	private static string[] NormalizeSymbols(IEnumerable<string> symbols)
		=> symbols?.Where(s => !s.IsEmpty()).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

	private void StartBridge()
	{
		if (_bridgeJarPath.IsEmpty())
			return;

		var jarPath = Path.GetFullPath(_bridgeJarPath);
		if (!File.Exists(jarPath))
			throw new FileNotFoundException("Dukascopy bridge JAR was not found.", jarPath);

		var startInfo = new ProcessStartInfo
		{
			FileName = "java",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		startInfo.ArgumentList.Add("-jar");
		startInfo.ArgumentList.Add(jarPath);
		startInfo.ArgumentList.Add("--port");
		startInfo.ArgumentList.Add(_port.ToString(CultureInfo.InvariantCulture));
		_bridgeProcess = Process.Start(startInfo) ??
			throw new InvalidOperationException("Unable to start the Dukascopy bridge process.");
		_bridgeProcess.BeginOutputReadLine();
		_bridgeProcess.BeginErrorReadLine();
	}

	private async Task<DukasCopyBridgeMessage> Request(DukasCopyBridgeRequest request,
		CancellationToken cancellationToken)
	{
		if (_writer == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		request.RequestId = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<DukasCopyBridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_pending.TryAdd(request.RequestId, completion))
			throw new InvalidOperationException($"Duplicate bridge request id {request.RequestId}.");

		try
		{
			var line = JsonConvert.SerializeObject(request, Formatting.None,
				new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
			await _writeLock.WaitAsync(cancellationToken);
			try
			{
				await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
			}
			finally
			{
				_writeLock.Release();
			}

			var response = await completion.Task.WaitAsync(cancellationToken);
			if (!response.Error.IsEmpty())
				throw new InvalidOperationException(response.Error);
			return response;
		}
		finally
		{
			_pending.TryRemove(request.RequestId, out _);
		}
	}

	private async Task ReceiveLoop(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var line = await _reader.ReadLineAsync(cancellationToken);
				if (line == null)
					throw new IOException("Dukascopy bridge closed the connection.");
				if (line.Length == 0)
					continue;
				if (line.Length > _maxMessageLength)
					throw new InvalidDataException("Dukascopy bridge message exceeded the 16 MiB limit.");

				var message = JsonConvert.DeserializeObject<DukasCopyBridgeMessage>(line) ??
					throw new InvalidDataException("Dukascopy bridge returned an empty JSON message.");
				await Dispatch(message, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			FailPending(ex);
			if (Error != null)
				await Error(ex, CancellationToken.None);
		}
	}

	private async ValueTask Dispatch(DukasCopyBridgeMessage message, CancellationToken cancellationToken)
	{
		switch (message.Kind)
		{
			case DukasCopyBridgeKinds.Response:
				if (_pending.TryGetValue(message.RequestId, out var completion))
					completion.TrySetResult(message);
				break;
			case DukasCopyBridgeKinds.Tick when message.Tick != null && TickReceived != null:
				await TickReceived(message.Tick, cancellationToken);
				break;
			case DukasCopyBridgeKinds.Bar when message.Bar != null && BarReceived != null:
				await BarReceived(message.Bar, cancellationToken);
				break;
			case DukasCopyBridgeKinds.Order when message.Order != null && OrderReceived != null:
				await OrderReceived(message.Order, cancellationToken);
				break;
			case DukasCopyBridgeKinds.Account when message.Account != null && AccountReceived != null:
				await AccountReceived(message.Account, cancellationToken);
				break;
			case DukasCopyBridgeKinds.Error when Error != null:
				await Error(new IOException(message.Error.IsEmpty("Dukascopy bridge reported an error.")),
					cancellationToken);
				break;
		}
	}

	private void FailPending(Exception error)
	{
		foreach (var completion in _pending.Values)
			completion.TrySetException(error);
		_pending.Clear();
	}

	private void CloseConnection()
	{
		_receiveCts?.Cancel();
		_receiveCts?.Dispose();
		_receiveCts = null;
		_reader?.Dispose();
		_reader = null;
		_writer?.Dispose();
		_writer = null;
		_tcpClient?.Dispose();
		_tcpClient = null;
		_receiveTask = null;
		FailPending(new IOException("Dukascopy bridge connection was closed."));

		if (_bridgeProcess != null)
		{
			try
			{
				if (!_bridgeProcess.HasExited)
					_bridgeProcess.Kill(true);
			}
			catch (Exception ex) when (ex is InvalidOperationException or
				System.ComponentModel.Win32Exception or NotSupportedException)
			{
			}
			_bridgeProcess.Dispose();
			_bridgeProcess = null;
		}
	}

	protected override void DisposeManaged()
	{
		CloseConnection();
		_writeLock.Dispose();
		base.DisposeManaged();
	}
}
