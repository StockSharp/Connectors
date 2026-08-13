namespace StockSharp.Finage.Native;

sealed class FinageWebSocketClient : BaseLogReceiver
{
	private const int _maximumMessageSize = 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly string _token;
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private ClientWebSocket _socket;
	private CancellationTokenSource _receiveCancellation;
	private Task _receiveTask;

	public FinageWebSocketClient(Uri endpoint, SecureString token)
	{
		_endpoint = endpoint ?? throw new ArgumentNullException(
			nameof(endpoint));
		_token = token.IsEmpty()
			? throw new ArgumentNullException(nameof(token))
			: token.UnSecure().Trim();

		_ = _endpoint.BuildStreamingUri(_token);
	}

	public override string Name => "Finage_WS";

	public event Func<FinageQuote, CancellationToken,
		ValueTask> QuoteReceived;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_socket is not null)
			throw new InvalidOperationException(
				"Finage WebSocket is already connected.");

		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

		try
		{
			await socket.ConnectAsync(
				_endpoint.BuildStreamingUri(_token),
				cancellationToken);

			while (true)
			{
				var text = await ReceiveTextAsync(socket,
					cancellationToken);
				var control = JObject.Parse(text);
				var status = control.Value<int?>("status_code");
				var message = control.Value<string>("message");

				if (status >= 400)
					throw new InvalidOperationException(
						$"Finage streaming authorization failed: " +
							message.IsEmpty($"status {status}"));

				if (status == 200 &&
					message?.Contains("connected to the adapter",
						StringComparison.OrdinalIgnoreCase) == true)
					break;
			}
		}
		catch
		{
			socket.Dispose();
			throw;
		}

		_socket = socket;
		_receiveCancellation = new();
		_receiveTask = ReceiveLoopAsync(socket,
			_receiveCancellation.Token);
	}

	public ValueTask SubscribeAsync(IEnumerable<string> symbols,
		CancellationToken cancellationToken)
		=> SendAsync(FinageExtensions.BuildSubscription(
			symbols, true), cancellationToken);

	public ValueTask UnsubscribeAsync(IEnumerable<string> symbols,
		CancellationToken cancellationToken)
		=> SendAsync(FinageExtensions.BuildSubscription(
			symbols, false), cancellationToken);

	private async ValueTask SendAsync(string payload,
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket?.State != WebSocketState.Open)
			throw new InvalidOperationException(
				"Finage WebSocket is not connected.");

		var data = Encoding.UTF8.GetBytes(payload);
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(data,
				WebSocketMessageType.Text, true,
				cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async Task ReceiveLoopAsync(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested &&
				socket.State == WebSocketState.Open)
			{
				var text = await ReceiveTextAsync(socket,
					cancellationToken);
				FinageQuote quote;

				try
				{
					quote = FinageExtensions.ParseStreamQuote(text);
				}
				catch (Exception error)
				{
					this.AddErrorLog(error);
					continue;
				}

				if (quote is not null && QuoteReceived is not null)
					await QuoteReceived.InvokeAsync(quote, cancellationToken);
				else
				{
					var control = JObject.Parse(text);
					var status =
						control.Value<int?>("status_code");
					if (status >= 400)
						throw new InvalidOperationException(
							$"Finage streaming error: " +
								control.Value<string>("message")
									.IsEmpty($"status {status}"));
				}
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			this.AddErrorLog(error);
		}
	}

	private static async ValueTask<string> ReceiveTextAsync(
		ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16384];
		using var stream = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(buffer,
				cancellationToken);

			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"Finage WebSocket closed: " +
						$"{result.CloseStatus} " +
						result.CloseStatusDescription);

			if (result.MessageType != WebSocketMessageType.Text)
				continue;

			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maximumMessageSize)
				throw new InvalidDataException(
					"Finage WebSocket message exceeds 1 MiB.");

			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(
					stream.GetBuffer(), 0,
					checked((int)stream.Length));
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket is null)
			return;

		_receiveCancellation?.Cancel();
		try
		{
			if (socket.State is WebSocketState.Open or
				WebSocketState.CloseReceived)
				await socket.CloseOutputAsync(
					WebSocketCloseStatus.NormalClosure,
					"disconnect", cancellationToken);
		}
		catch (WebSocketException)
		{
		}

		if (_receiveTask is not null)
			try
			{
				await _receiveTask;
			}
			catch (OperationCanceledException)
			{
			}

		socket.Dispose();
		_receiveCancellation?.Dispose();
		_socket = null;
		_receiveCancellation = null;
		_receiveTask = null;
	}

	protected override void DisposeManaged()
	{
		DisconnectAsync(CancellationToken.None)
			.AsTask().GetAwaiter().GetResult();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
