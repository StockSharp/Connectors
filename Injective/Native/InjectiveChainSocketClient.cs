namespace StockSharp.Injective.Native;

sealed class InjectiveChainSocketClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 4 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly Lock _sync = new();
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private CancellationTokenSource _source;
	private ClientWebSocket _socket;
	private Task _receiveTask;
	private bool _isDisposed;

	public InjectiveChainSocketClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("ws" or "wss") ||
			(_endpoint.Scheme == "ws" && !_endpoint.IsLoopback))
			throw new ArgumentException(
				"Injective chain WebSocket endpoint must use WSS, except locally.",
				nameof(endpoint));
	}

	public override string Name => "Injective_ChainWS";

	public event Func<InjectiveBlockHeader, CancellationToken, ValueTask>
		BlockReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);
			if (_source is not null)
				throw new InvalidOperationException(
					"Injective chain WebSocket is already connected.");
			_source = new();
		}
		try
		{
			await OpenAsync(cancellationToken);
			using (_sync.EnterScope())
				_receiveTask = RunAsync(_source.Token);
		}
		catch
		{
			await DisconnectAsync(default);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		CancellationTokenSource source;
		ClientWebSocket socket;
		Task receiveTask;
		using (_sync.EnterScope())
		{
			source = _source;
			socket = _socket;
			receiveTask = _receiveTask;
			_source = null;
			_socket = null;
			_receiveTask = null;
		}
		if (source is null)
			return;
		source.Cancel();
		if (socket?.State is WebSocketState.Open or WebSocketState.CloseReceived)
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
		socket?.Abort();
		if (receiveTask is not null)
		{
			try
			{
				await receiveTask.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
		}
		socket?.Dispose();
		source.Dispose();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		for (var attempt = 0; !cancellationToken.IsCancellationRequested;
			attempt++)
		{
			try
			{
				await ReceiveAsync(cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException(
						"Injective chain WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				if (Error is not null)
					await Error(error, cancellationToken);
				if (StateChanged is not null)
					await StateChanged(ConnectionStates.Failed, cancellationToken);
			}
			if (cancellationToken.IsCancellationRequested)
				break;
			await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,
				1 << Math.Min(attempt, 5))), cancellationToken);
			try
			{
				await OpenAsync(cancellationToken);
				attempt = 0;
				if (StateChanged is not null)
					await StateChanged(ConnectionStates.Restored,
						cancellationToken);
			}
			catch (Exception error)
			{
				if (Error is not null)
					await Error(error, cancellationToken);
			}
		}
	}

	private async ValueTask OpenAsync(CancellationToken cancellationToken)
	{
		var next = new ClientWebSocket();
		next.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
		next.Options.SetRequestHeader("User-Agent", "StockSharp-Injective/1.0");
		try
		{
			await next.ConnectAsync(_endpoint, cancellationToken);
			var request = new InjectiveChainSocketRequest
			{
				JsonRpc = "2.0",
				Id = 1,
				Method = "subscribe",
				Params = new()
				{
					Query = "tm.event='NewBlock'",
				},
			};
			var payload = Encoding.UTF8.GetBytes(
				JsonConvert.SerializeObject(request, _settings));
			await next.SendAsync(payload, WebSocketMessageType.Text, true,
				cancellationToken);
		}
		catch
		{
			next.Dispose();
			throw;
		}
		ClientWebSocket previous;
		using (_sync.EnterScope())
		{
			previous = _socket;
			_socket = next;
		}
		previous?.Abort();
		previous?.Dispose();
	}

	private async ValueTask ReceiveAsync(CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		using (_sync.EnterScope())
			socket = _socket ?? throw new InvalidOperationException(
				"Injective chain WebSocket is not connected.");
		var buffer = new byte[81920];
		while (!cancellationToken.IsCancellationRequested &&
			socket.State == WebSocketState.Open)
		{
			using var stream = new MemoryStream();
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
					return;
				if (result.MessageType != WebSocketMessageType.Text)
					throw new InvalidDataException(
						"Injective chain WebSocket returned a binary message.");
				if (stream.Length + result.Count > _maximumMessageBytes)
					throw new InvalidDataException(
						"Injective chain WebSocket message is too large.");
				stream.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);
			var text = Encoding.UTF8.GetString(stream.GetBuffer(), 0,
				checked((int)stream.Length));
			var envelope = JsonConvert.DeserializeObject<
				InjectiveChainSocketEnvelope>(text, _settings) ??
				throw new InvalidDataException(
					"Injective chain WebSocket returned an empty message.");
			if (envelope.Error is not null)
				throw new InvalidOperationException(
					$"Injective chain WebSocket error {envelope.Error.Code}: " +
					envelope.Error.Message);
			var header = envelope.Result?.Data?.Value?.Block?.Header;
			if (header is not null && BlockReceived is not null)
				await BlockReceived(header, cancellationToken);
		}
	}

	protected override void DisposeManaged()
	{
		using (_sync.EnterScope())
			_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
