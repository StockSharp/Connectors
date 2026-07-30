namespace StockSharp.Intrinio.Native;

sealed class IntrinioRealtimeConnectionDependencies
{
	public HttpMessageHandler HttpHandler { get; init; }
	public Func<Uri, IReadOnlyDictionary<string, string>, CancellationToken,
		Task<WebSocket>> OpenWebSocketAsync { get; init; }
	public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
		static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);
	public Func<DateTime> UtcNow { get; init; } = static () => DateTime.UtcNow;
}

sealed class IntrinioRealtimeConnection : BaseLogReceiver, IDisposable
{
	private const int _initialRetryLimit = 3;
	private const int _maxFrameSize = 1 + 256 * 86;
	private static readonly TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan _stableConnectionThreshold = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan[] _reconnectDelays =
	[
		TimeSpan.FromSeconds(10),
		TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
	];

	private readonly string _apiKey;
	private readonly bool _isOption;
	private readonly IntrinioEquityProviders _equityProvider;
	private readonly IntrinioOptionProviders _optionProvider;
	private readonly bool _isDelayed;
	private readonly IntrinioRealtimeConnectionDependencies _dependencies;
	private readonly HttpClient _http;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
	private readonly Channel<IntrinioDecodedEvent>[] _eventQueues;
	private readonly Task[] _eventWorkers;
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private WebSocket _socket;
	private Task _runTask;
	private int _isStarted;
	private int _isStopped;

	public IntrinioRealtimeConnection(string apiKey, IntrinioEquityProviders provider,
		int workerCount, int bufferSize)
		: this(apiKey, false, provider, default, false, workerCount, bufferSize,
			CreateDefaultDependencies())
	{
	}

	internal IntrinioRealtimeConnection(string apiKey, IntrinioEquityProviders provider,
		int workerCount, int bufferSize,
		IntrinioRealtimeConnectionDependencies dependencies)
		: this(apiKey, false, provider, default, false, workerCount, bufferSize,
			dependencies)
	{
	}

	public IntrinioRealtimeConnection(string apiKey, IntrinioOptionProviders provider,
		bool isDelayed, int workerCount, int bufferSize)
		: this(apiKey, true, default, provider, isDelayed, workerCount, bufferSize,
			CreateDefaultDependencies())
	{
	}

	internal IntrinioRealtimeConnection(string apiKey, IntrinioOptionProviders provider,
		bool isDelayed, int workerCount, int bufferSize,
		IntrinioRealtimeConnectionDependencies dependencies)
		: this(apiKey, true, default, provider, isDelayed, workerCount, bufferSize,
			dependencies)
	{
	}

	private IntrinioRealtimeConnection(string apiKey, bool isOption,
		IntrinioEquityProviders equityProvider, IntrinioOptionProviders optionProvider,
		bool isDelayed, int workerCount, int bufferSize,
		IntrinioRealtimeConnectionDependencies dependencies)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_dependencies = dependencies
			?? throw new ArgumentNullException(nameof(dependencies));
		ArgumentNullException.ThrowIfNull(_dependencies.HttpHandler);
		ArgumentNullException.ThrowIfNull(_dependencies.OpenWebSocketAsync);
		ArgumentNullException.ThrowIfNull(_dependencies.DelayAsync);
		ArgumentNullException.ThrowIfNull(_dependencies.UtcNow);
		if (workerCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(workerCount));
		if (bufferSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(bufferSize));

		_http = new(_dependencies.HttpHandler)
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_isOption = isOption;
		_equityProvider = equityProvider;
		_optionProvider = optionProvider;
		_isDelayed = isDelayed;
		_eventQueues = new Channel<IntrinioDecodedEvent>[workerCount];
		_eventWorkers = new Task[workerCount];

		for (var i = 0; i < _eventQueues.Length; i++)
		{
			_eventQueues[i] = Channel.CreateBounded<IntrinioDecodedEvent>(
				new BoundedChannelOptions(bufferSize)
			{
				SingleReader = true,
				SingleWriter = true,
				FullMode = BoundedChannelFullMode.Wait,
				AllowSynchronousContinuations = false,
			});
		}
	}

	public event Func<IntrinioDecodedEvent, CancellationToken, ValueTask> EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _isStarted, 1) != 0)
			throw new InvalidOperationException("Intrinio real-time connection is already started.");

		for (var i = 0; i < _eventWorkers.Length; i++)
			_eventWorkers[i] = ProcessEventsAsync(_eventQueues[i].Reader, _cancellation.Token);

		_runTask = RunAsync(_cancellation.Token);
		try
		{
			await _initialConnection.Task.WaitAsync(cancellationToken);
		}
		catch
		{
			await StopAsync();
			throw;
		}
	}

	public async Task JoinAsync(string symbol, CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		var message = EncodeJoin(symbol);
		Exception sendError = null;
		var added = false;
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			added = _channels.Add(symbol);
			if (!added)
				return;

			var socket = _socket;
			if (socket?.State == WebSocketState.Open)
			{
				try
				{
					await SendAsync(socket, message, cancellationToken);
				}
				catch (Exception error) when (error is WebSocketException or
					ObjectDisposedException or InvalidOperationException)
				{
					sendError = error;
					socket.Abort();
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (added)
				_channels.Remove(symbol);
			_socket?.Abort();
			throw;
		}
		finally
		{
			_sendLock.Release();
		}

		if (sendError != null)
			await ReportErrorAsync(sendError, cancellationToken);
	}

	public async Task LeaveAsync(string symbol, CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		var message = EncodeLeave(symbol);
		Exception sendError = null;
		var removed = false;
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			removed = _channels.Remove(symbol);
			if (!removed)
				return;

			var socket = _socket;
			if (socket?.State == WebSocketState.Open)
			{
				try
				{
					await SendAsync(socket, message, cancellationToken);
				}
				catch (Exception error) when (error is WebSocketException or
					ObjectDisposedException or InvalidOperationException)
				{
					sendError = error;
					socket.Abort();
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (removed)
				_channels.Add(symbol);
			_socket?.Abort();
			throw;
		}
		finally
		{
			_sendLock.Release();
		}

		if (sendError != null)
			await ReportErrorAsync(sendError, cancellationToken);
	}

	public async Task StopAsync()
	{
		if (Interlocked.Exchange(ref _isStopped, 1) != 0)
			return;

		_cancellation.Cancel();
		WebSocket socket;
		await _sendLock.WaitAsync();
		try
		{
			socket = _socket;
			_socket = null;
		}
		finally
		{
			_sendLock.Release();
		}

		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
					"Client disconnect", CancellationToken.None)
					.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (Exception error) when (error is WebSocketException or
				ObjectDisposedException or TimeoutException)
			{
			}
		}
		socket?.Dispose();

		if (_runTask != null)
		{
			try
			{
				await _runTask;
			}
			catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
			{
			}
		}

		foreach (var queue in _eventQueues)
			queue.Writer.TryComplete();

		try
		{
			await Task.WhenAll(_eventWorkers.Where(task => task != null));
		}
		catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
		{
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var wasConnected = false;
		var failureCount = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			WebSocket socket = null;
			var connectedThisAttempt = false;
			var connectedAt = default(DateTime);
			TimeSpan? reconnectDelay = null;
			try
			{
				socket = await ConnectAsync(cancellationToken);
				await ActivateAsync(socket, cancellationToken);
				connectedThisAttempt = true;
				connectedAt = _dependencies.UtcNow();
				wasConnected = true;
				_initialConnection.TrySetResult();
				this.AddInfoLog("Intrinio {0} real-time WebSocket connected.",
					_isOption ? "options" : "equities");
				await RunConnectionAsync(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Intrinio real-time WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				if (cancellationToken.IsCancellationRequested)
					break;
				if (!wasConnected &&
					(!IsTransientInitialError(error) ||
					failureCount >= _initialRetryLimit))
				{
					_initialConnection.TrySetException(error);
					break;
				}

				await ReportErrorAsync(error, cancellationToken);
				if (connectedThisAttempt &&
					_dependencies.UtcNow() - connectedAt >= _stableConnectionThreshold)
					failureCount = 0;
				else
				{
					reconnectDelay = _reconnectDelays[Math.Min(failureCount,
						_reconnectDelays.Length - 1)];
					failureCount++;
				}
			}
			finally
			{
				await DeactivateAsync(socket);
				socket?.Dispose();
			}

			if (reconnectDelay is { } delay)
			{
				try
				{
					await _dependencies.DelayAsync(delay, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
	}

	private async Task<WebSocket> ConnectAsync(CancellationToken cancellationToken)
	{
		var authUri = _isOption
			? IntrinioRealtimeProtocol.GetOptionsAuthUri(_optionProvider, _apiKey)
			: IntrinioRealtimeProtocol.GetEquityAuthUri(_equityProvider, _apiKey);
		var authHeaders = _isOption
			? IntrinioRealtimeProtocol.GetOptionsAuthHeaders(_isDelayed)
			: IntrinioRealtimeProtocol.GetEquityAuthHeaders();

		using var request = new HttpRequestMessage(HttpMethod.Get, authUri);

		foreach (var header in authHeaders)
			request.Headers.TryAddWithoutValidation(header.Key, header.Value);

		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseContentRead, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Intrinio real-time authorization failed ({(int)response.StatusCode} " +
				$"{response.StatusCode}): {Truncate(body, 1000)}",
				null, response.StatusCode);
		}

		var token = body.Trim();
		if (token.IsEmpty())
			throw new InvalidDataException("Intrinio real-time authorization returned an empty token.");

		var socketUri = _isOption
			? IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				_optionProvider, token, _isDelayed)
			: IntrinioRealtimeProtocol.GetEquityWebSocketUri(_equityProvider, token);
		var socketHeaders = _isOption
			? IntrinioRealtimeProtocol.GetOptionsWebSocketHeaders(_isDelayed)
			: IntrinioRealtimeProtocol.GetEquityWebSocketHeaders();

		return await _dependencies.OpenWebSocketAsync(
			socketUri, socketHeaders, cancellationToken)
			?? throw new InvalidOperationException(
				"Intrinio WebSocket factory returned no socket.");
	}

	private static async Task<WebSocket> OpenClientWebSocketAsync(Uri socketUri,
		IReadOnlyDictionary<string, string> socketHeaders,
		CancellationToken cancellationToken)
	{
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = _keepAliveInterval;
		socket.Options.KeepAliveTimeout = _keepAliveInterval;

		foreach (var header in socketHeaders)
			socket.Options.SetRequestHeader(header.Key, header.Value);

		try
		{
			await socket.ConnectAsync(socketUri, cancellationToken);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private async Task ActivateAsync(WebSocket socket,
		CancellationToken cancellationToken)
	{
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			foreach (var channel in _channels)
				await SendAsync(socket, EncodeJoin(channel), cancellationToken);

			_socket = socket;
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task DeactivateAsync(WebSocket socket)
	{
		if (socket == null)
			return;

		await _sendLock.WaitAsync();
		try
		{
			if (ReferenceEquals(_socket, socket))
				_socket = null;
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task RunConnectionAsync(WebSocket socket,
		CancellationToken cancellationToken)
		=> await ReceiveAsync(socket, cancellationToken);

	private async Task ReceiveAsync(WebSocket socket,
		CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open &&
			!cancellationToken.IsCancellationRequested)
		{
			var message = await ReceiveMessageAsync(socket, cancellationToken);
			if (message.Type == WebSocketMessageType.Text)
			{
				this.AddWarningLog("Intrinio real-time warning: {0}",
					Truncate(Encoding.UTF8.GetString(message.Data), 4096));
				continue;
			}
			if (message.Data.Length == 0)
				continue;

			IReadOnlyList<IntrinioDecodedEvent> events;
			try
			{
				events = _isOption
					? IntrinioRealtimeProtocol.DecodeOptions(message.Data)
					: IntrinioRealtimeProtocol.DecodeEquity(message.Data);
			}
			catch (Exception error) when (error is FormatException or InvalidDataException)
			{
				await ReportErrorAsync(error, cancellationToken);
				continue;
			}

			foreach (var update in events)
			{
				var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(update.Symbol);
				var index = (hash & int.MaxValue) % _eventQueues.Length;
				await _eventQueues[index].Writer.WriteAsync(update, cancellationToken);
			}
		}
	}

	private async Task ProcessEventsAsync(ChannelReader<IntrinioDecodedEvent> reader,
		CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var update in reader.ReadAllAsync(cancellationToken))
			{
				try
				{
					if (EventReceived is { } handler)
						await handler(update, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
				}
				catch (Exception error)
				{
					await ReportErrorAsync(error, cancellationToken);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async ValueTask ReportErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		if (Error is not { } handler)
		{
			this.AddErrorLog(error);
			return;
		}

		try
		{
			await handler(error, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception handlerError)
		{
			this.AddErrorLog(handlerError);
		}
	}

	private byte[] EncodeJoin(string symbol)
		=> _isOption
			? IntrinioRealtimeProtocol.EncodeOptionsJoin(symbol, false)
			: IntrinioRealtimeProtocol.EncodeEquityJoin(symbol, false);

	private byte[] EncodeLeave(string symbol)
		=> _isOption
			? IntrinioRealtimeProtocol.EncodeOptionsLeave(symbol)
			: IntrinioRealtimeProtocol.EncodeEquityLeave(symbol);

	private static Task SendAsync(WebSocket socket, byte[] data,
		CancellationToken cancellationToken)
		=> socket.SendAsync(new ArraySegment<byte>(data),
			WebSocketMessageType.Binary, true, cancellationToken);

	internal static async Task<(WebSocketMessageType Type, byte[] Data)> ReceiveMessageAsync(
		WebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
		try
		{
			using var stream = new MemoryStream();
			WebSocketMessageType? type = null;

			while (true)
			{
				var result = await socket.ReceiveAsync(
					new ArraySegment<byte>(buffer), cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					throw new WebSocketException(
						$"Intrinio real-time WebSocket closed: {result.CloseStatus} " +
						result.CloseStatusDescription);
				}
				if (type != null && type != result.MessageType)
				{
					throw new InvalidDataException(
						"Intrinio WebSocket changed message type within a fragmented frame.");
				}

				type = result.MessageType;
				stream.Write(buffer, 0, result.Count);
				if (stream.Length > _maxFrameSize)
					throw new InvalidDataException("Intrinio real-time frame is too large.");
				if (result.EndOfMessage)
					return (type.Value, stream.ToArray());
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static string Truncate(string value, int maxLength)
	{
		if (value.IsEmpty() || value.Length <= maxLength)
			return value;
		return value[..maxLength];
	}

	private static IntrinioRealtimeConnectionDependencies CreateDefaultDependencies()
		=> new()
		{
			HttpHandler = new HttpClientHandler(),
			OpenWebSocketAsync = OpenClientWebSocketAsync,
		};

	private static bool IsTransientInitialError(Exception error)
	{
		if (error is TaskCanceledException or TimeoutException)
			return true;
		if (error is WebSocketException webSocketError)
		{
			return webSocketError.InnerException is HttpRequestException httpError
				? IsTransientHttpError(httpError)
				: true;
		}
		if (error is not HttpRequestException directHttpError)
			return false;

		return IsTransientHttpError(directHttpError);
	}

	private static bool IsTransientHttpError(HttpRequestException error)
	{
		if (error.StatusCode is not { } statusCode)
			return true;

		var code = (int)statusCode;
		return statusCode is HttpStatusCode.RequestTimeout or
			HttpStatusCode.TooManyRequests || code >= 500;
	}

	protected override void DisposeManaged()
	{
		StopAsync().GetAwaiter().GetResult();
		_http.Dispose();
		_cancellation.Dispose();
		_sendLock.Dispose();
		base.DisposeManaged();
	}
}
