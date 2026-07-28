namespace StockSharp.Xrpl.Native;

sealed class XrplSocketClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly string _account;
	private readonly Func<JObject, ValueTask> _messageHandler;
	private readonly Func<Exception, ValueTask> _errorHandler;
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private TaskCompletionSource<bool> _subscriptionCompletion;
	private bool _isDisposed;

	public XrplSocketClient(string endpoint, string account,
		Func<JObject, ValueTask> messageHandler,
		Func<Exception, ValueTask> errorHandler)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "wss://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase("ws") ||
				_endpoint.Scheme.EqualsIgnoreCase("wss")) ||
			(_endpoint.Scheme.EqualsIgnoreCase("ws") &&
				!_endpoint.IsLoopback))
			throw new ArgumentException(
				"XRPL streaming endpoint must use WSS, except for a local node.",
				nameof(endpoint));
		if (!account.IsEmpty() && !XrplCodec.IsValidClassicAddress(account))
			throw new ArgumentException(
				$"XRPL account '{account}' is not a valid classic address.",
				nameof(account));
		_account = account?.Trim();
		_messageHandler = messageHandler ?? throw new
			ArgumentNullException(nameof(messageHandler));
		_errorHandler = errorHandler ?? throw new
			ArgumentNullException(nameof(errorHandler));
	}

	public override string Name => "XRPL_WebSocket";

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The XRPL WebSocket is already initialized.");
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-XRPL-Connector/1.0");
		var lifetime = new CancellationTokenSource();
		try
		{
			await socket.ConnectAsync(_endpoint, cancellationToken);
			_socket = socket;
			_lifetime = lifetime;
			_subscriptionCompletion = new(
				TaskCreationOptions.RunContinuationsAsynchronously);
			_receiveTask = ReceiveLoopAsync(lifetime.Token);
			var request = new JObject
			{
				["id"] = 1,
				["command"] = "subscribe",
				["api_version"] = 2,
				["streams"] = new JArray("ledger", "book_changes"),
			};
			if (!_account.IsEmpty())
				request["accounts"] = new JArray(_account);
			await SendAsync(request, cancellationToken);
			await _subscriptionCompletion.Task.WaitAsync(
				cancellationToken);
		}
		catch
		{
			lifetime.Cancel();
			lifetime.Dispose();
			socket.Abort();
			socket.Dispose();
			_socket = null;
			_lifetime = null;
			throw;
		}
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_lifetime?.Cancel();
		_socket?.Abort();
		_socket?.Dispose();
		_lifetime?.Dispose();
		_subscriptionCompletion?.TrySetCanceled();
		_sendGate.Dispose();
		_socket = null;
		_lifetime = null;
		_receiveTask = null;
		base.DisposeManaged();
	}

	private async ValueTask SendAsync(JObject message,
		CancellationToken cancellationToken)
	{
		var socket = _socket ?? throw new InvalidOperationException(
			"The XRPL WebSocket is not connected.");
		var data = Encoding.UTF8.GetBytes(
			JsonConvert.SerializeObject(message, _jsonSettings));
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(data, WebSocketMessageType.Text, true,
				cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var payload = await ReceiveAsync(cancellationToken);
				JObject message;
				try
				{
					message = JsonConvert.DeserializeObject<JObject>(
						payload, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"XRPL WebSocket returned an unexpected payload.",
						error);
				}
				if (message is null)
					throw new InvalidDataException(
						"XRPL WebSocket returned an empty payload.");
				if (message.Value<long?>("id") == 1)
				{
					if (!message.Value<string>("status")
						.EqualsIgnoreCase("success"))
						_subscriptionCompletion.TrySetException(
							new InvalidOperationException(
								"XRPL WebSocket subscription failed: " +
								(message.Value<string>("error_message") ??
									message.Value<string>("error") ??
									"request rejected")));
					else
						_subscriptionCompletion.TrySetResult(true);
					continue;
				}
				await _messageHandler(message);
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error) when (!_isDisposed)
		{
			_subscriptionCompletion?.TrySetException(error);
			await _errorHandler(error);
		}
	}

	private async ValueTask<string> ReceiveAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket ?? throw new InvalidOperationException(
			"The XRPL WebSocket is not connected.");
		using var target = new MemoryStream();
		var buffer = new byte[8192];
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer,
				cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"XRPL WebSocket closed with status " +
						$"'{socket.CloseStatus}'.");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					"XRPL WebSocket returned a non-text message.");
			if (target.Length + result.Count > _maximumMessageBytes)
				throw new InvalidDataException(
					"XRPL WebSocket message exceeds 16 MiB.");
			target.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(target.ToArray());
		}
	}
}
