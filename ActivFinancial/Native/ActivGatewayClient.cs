namespace StockSharp.ActivFinancial.Native;

internal sealed class ActivGatewayException : InvalidOperationException
{
	public ActivGatewayException(string code, string message)
		: base(code.IsEmpty() ? message : $"{message} [{code}]")
	{
		Code = code;
	}

	public string Code { get; }
}

internal sealed class ActivGatewayClient : Disposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _nodePath;
	private readonly string _gatewayDirectory;
	private readonly ConcurrentDictionary<long, TaskCompletionSource<ActivGatewayMessage>> _pending = new();
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private Process _process;
	private StreamWriter _writer;
	private CancellationTokenSource _receiveCts;
	private Task _receiveTask;
	private Task _errorTask;
	private long _requestId;

	public ActivGatewayClient(string nodePath, string gatewayDirectory)
	{
		nodePath.ThrowIfEmpty(nameof(nodePath));
		gatewayDirectory.ThrowIfEmpty(nameof(gatewayDirectory));
		_nodePath = nodePath;
		_gatewayDirectory = gatewayDirectory;
	}

	public event Func<long, ActivGatewayRecord, CancellationToken, ValueTask> RecordReceived;
	public event Func<long, CancellationToken, ValueTask> SubscriptionFinished;
	public event Func<long?, Exception, CancellationToken, ValueTask> Error;
	public event Func<int, string, CancellationToken, ValueTask> Log;

	public string GatewayVersion { get; private set; }
	public string OneApiVersion { get; private set; }

	public async Task Connect(string host, string user, string password,
		CancellationToken cancellationToken)
	{
		if (_process != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		StartProcess();
		var request = new ActivGatewayRequest
		{
			Command = ActivGatewayCommands.Connect,
			Host = host,
			User = user,
			Password = password,
		};
		try
		{
			var response = await Request(request, cancellationToken);
			GatewayVersion = response.GatewayVersion;
			OneApiVersion = response.OneApiVersion;
		}
		catch
		{
			CloseProcess();
			throw;
		}
		finally
		{
			request.Password = null;
		}
	}

	public async Task Disconnect(CancellationToken cancellationToken)
	{
		if (_process == null)
			return;
		try
		{
			await Request(new() { Command = ActivGatewayCommands.Disconnect }, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException or
			InvalidOperationException)
		{
		}
		finally
		{
			CloseProcess();
		}
	}

	public async Task<ActivGatewayRecord[]> Lookup(int dataSourceId, string query,
		int skip, int limit, string fallbackTimeZone, CancellationToken cancellationToken)
		=> (await Request(new()
		{
			Command = ActivGatewayCommands.Lookup,
			DataSourceId = dataSourceId,
			Query = query,
			Skip = skip,
			Limit = limit,
			FallbackTimeZone = fallbackTimeZone,
		}, cancellationToken)).Records ?? [];

	public async Task<ActivGatewayRecord[]> Snapshot(int dataSourceId, int symbologyId,
		string symbol, string fallbackTimeZone, CancellationToken cancellationToken)
		=> (await Request(CreateTopicRequest(ActivGatewayCommands.Snapshot,
			dataSourceId, symbologyId, symbol, fallbackTimeZone), cancellationToken)).Records ?? [];

	public async Task<ActivGatewayRecord[]> GetTicks(int dataSourceId, int symbologyId,
		string symbol, DateTime? from, DateTime? to, int limit, string fallbackTimeZone,
		CancellationToken cancellationToken)
	{
		var request = CreateTopicRequest(ActivGatewayCommands.HistoryTicks,
			dataSourceId, symbologyId, symbol, fallbackTimeZone);
		request.FromUtc = from.ToUnixMilliseconds();
		request.ToUtc = to.ToUnixMilliseconds();
		request.Limit = limit;
		return (await Request(request, cancellationToken)).Records ?? [];
	}

	public async Task<ActivGatewayRecord[]> GetBars(int dataSourceId, int symbologyId,
		string symbol, DateTime? from, DateTime? to, int limit, int timeFrameMinutes,
		string fallbackTimeZone, CancellationToken cancellationToken)
	{
		var request = CreateTopicRequest(ActivGatewayCommands.HistoryBars,
			dataSourceId, symbologyId, symbol, fallbackTimeZone);
		request.FromUtc = from.ToUnixMilliseconds();
		request.ToUtc = to.ToUnixMilliseconds();
		request.Limit = limit;
		request.TimeFrameMinutes = timeFrameMinutes;
		return (await Request(request, cancellationToken)).Records ?? [];
	}

	public Task Subscribe(long subscriptionId, ActivGatewayDataKinds dataKind,
		int dataSourceId, int symbologyId, string symbol, int? count,
		string fallbackTimeZone, CancellationToken cancellationToken)
	{
		var request = CreateTopicRequest(ActivGatewayCommands.Subscribe,
			dataSourceId, symbologyId, symbol, fallbackTimeZone);
		request.SubscriptionId = subscriptionId;
		request.DataKind = dataKind;
		request.Count = count;
		return Request(request, cancellationToken);
	}

	public Task Unsubscribe(long subscriptionId, CancellationToken cancellationToken)
		=> Request(new()
		{
			Command = ActivGatewayCommands.Unsubscribe,
			SubscriptionId = subscriptionId,
		}, cancellationToken);

	private static ActivGatewayRequest CreateTopicRequest(ActivGatewayCommands command,
		int dataSourceId, int symbologyId, string symbol, string fallbackTimeZone)
		=> new()
		{
			Command = command,
			DataSourceId = dataSourceId,
			SymbologyId = symbologyId,
			Symbol = symbol,
			FallbackTimeZone = fallbackTimeZone,
		};

	private void StartProcess()
	{
		var directory = Path.GetFullPath(_gatewayDirectory);
		var scriptPath = Path.Combine(directory, "activ_gateway.cjs");
		var packagePath = Path.Combine(directory, "package.json");
		var oneApiPath = Path.Combine(directory, "node_modules", "@activfinancial",
			"one-api", "package.json");
		if (!File.Exists(scriptPath))
			throw new FileNotFoundException("The ACTIV typed Node gateway was not found.", scriptPath);
		if (!File.Exists(packagePath))
			throw new FileNotFoundException("The ACTIV gateway package.json was not found.", packagePath);
		if (!File.Exists(oneApiPath))
		{
			throw new FileNotFoundException(
				"The official @activfinancial/one-api package is not installed. Run 'npm install --omit=dev' in the configured gateway directory.",
				oneApiPath);
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
		startInfo.Environment["TZ"] = "Etc/UTC";

		_process = Process.Start(startInfo) ??
			throw new InvalidOperationException("Unable to start the ACTIV Node gateway process.");
		_writer = _process.StandardInput;
		_writer.AutoFlush = true;
		_receiveCts = new();
		_receiveTask = ReceiveLoop(_process.StandardOutput, _receiveCts.Token);
		_errorTask = ErrorLoop(_process.StandardError, _receiveCts.Token);
	}

	private async Task<ActivGatewayMessage> Request(ActivGatewayRequest request,
		CancellationToken cancellationToken)
	{
		if (_writer == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		request.RequestId = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<ActivGatewayMessage>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_pending.TryAdd(request.RequestId, completion))
			throw new InvalidOperationException($"Duplicate ACTIV gateway request id {request.RequestId}.");

		try
		{
			var line = JsonConvert.SerializeObject(request, Formatting.None, _jsonSettings);
			if (line.Length > ActivGatewayProtocol.MaxMessageLength)
				throw new InvalidDataException("ACTIV gateway request exceeded the 16 MiB limit.");
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
			if (response.Error != null)
				throw new ActivGatewayException(response.Error.Code, response.Error.Message);
			return response;
		}
		finally
		{
			_pending.TryRemove(request.RequestId, out _);
		}
	}

	private async Task ReceiveLoop(StreamReader reader, CancellationToken cancellationToken)
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
				if (line.Length > ActivGatewayProtocol.MaxMessageLength)
					throw new InvalidDataException("ACTIV gateway response exceeded the 16 MiB limit.");

				var message = JsonConvert.DeserializeObject<ActivGatewayMessage>(line) ??
					throw new InvalidDataException("ACTIV gateway returned an empty JSON message.");
				if (message.Version != ActivGatewayProtocol.Version)
				{
					throw new InvalidDataException(
						$"Unsupported ACTIV gateway protocol version {message.Version}.");
				}
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
				await Error(null, ex, CancellationToken.None);
		}
	}

	private async Task ErrorLoop(StreamReader reader, CancellationToken cancellationToken)
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
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (IOException ex)
		{
			if (!cancellationToken.IsCancellationRequested && Error != null)
				await Error(null, ex, CancellationToken.None);
		}
		catch (ObjectDisposedException ex)
		{
			if (!cancellationToken.IsCancellationRequested && Error != null)
				await Error(null, ex, CancellationToken.None);
		}
	}

	private async ValueTask Dispatch(ActivGatewayMessage message,
		CancellationToken cancellationToken)
	{
		switch (message.Kind)
		{
			case ActivGatewayMessageKinds.Response:
				if (_pending.TryGetValue(message.RequestId, out var completion))
					completion.TrySetResult(message);
				break;
			case ActivGatewayMessageKinds.Record when message.SubscriptionId != null &&
				message.Record != null && RecordReceived != null:
				await RecordReceived(message.SubscriptionId.Value, message.Record, cancellationToken);
				break;
			case ActivGatewayMessageKinds.SubscriptionFinished when
				message.SubscriptionId != null && SubscriptionFinished != null:
				await SubscriptionFinished(message.SubscriptionId.Value, cancellationToken);
				break;
			case ActivGatewayMessageKinds.Error when Error != null:
				await Error(message.SubscriptionId,
					new ActivGatewayException(message.Error?.Code, message.Error?.Message ??
						"ACTIV gateway reported an error."), cancellationToken);
				break;
			case ActivGatewayMessageKinds.Log when Log != null:
				await Log(message.LogLevel ?? 1, message.LogMessage, cancellationToken);
				break;
			default:
				throw new InvalidDataException($"Unsupported ACTIV gateway message kind {message.Kind}.");
		}
	}

	private Exception ProcessExitError()
	{
		var process = _process;
		if (process == null)
			return new IOException("ACTIV gateway closed its output stream.");
		try
		{
			return process.HasExited
				? new IOException($"ACTIV gateway exited with code {process.ExitCode}.")
				: new IOException("ACTIV gateway closed its output stream.");
		}
		catch (InvalidOperationException)
		{
			return new IOException("ACTIV gateway process is no longer available.");
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
		FailPending(new IOException("ACTIV gateway connection was closed."));

		var process = _process;
		_process = null;
		if (process != null)
		{
			try
			{
				if (!process.HasExited)
					process.Kill(true);
			}
			catch (Exception ex) when (ex is InvalidOperationException or
				System.ComponentModel.Win32Exception or NotSupportedException)
			{
			}
			process.Dispose();
		}
		GatewayVersion = null;
		OneApiVersion = null;
	}

	protected override void DisposeManaged()
	{
		CloseProcess();
		_writeLock.Dispose();
		base.DisposeManaged();
	}
}

internal static class ActivGatewayDateExtensions
{
	public static long? ToUnixMilliseconds(this DateTime? value)
		=> value == null ? null : new DateTimeOffset(value.Value.ToUtc()).ToUnixTimeMilliseconds();

	private static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};
}
