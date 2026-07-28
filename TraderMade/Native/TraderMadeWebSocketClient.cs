namespace StockSharp.TraderMade.Native;

sealed class TraderMadeWebSocketClient : BaseLogReceiver
{
	private const int _maximumMessageSize = 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly string _key;
	private readonly bool _ladder;
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private ClientWebSocket _socket;
	private CancellationTokenSource _receiveCancellation;
	private Task _receiveTask;

	public TraderMadeWebSocketClient(Uri endpoint,
		SecureString key, bool ladder)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != "wss")
			throw new ArgumentException(
				"TraderMade streaming endpoint must be an " +
					"absolute WSS URI.",
				nameof(endpoint));
		_endpoint = endpoint;
		_key = key.IsEmpty()
			? throw new ArgumentNullException(nameof(key))
			: key.UnSecure().Trim();
		_ladder = ladder;
	}

	public override string Name => "TraderMade_WS";

	public event Func<TraderMadeQuote, CancellationToken,
		ValueTask> QuoteReceived;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_socket is not null)
			throw new InvalidOperationException(
				"TraderMade WebSocket is already connected.");
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		try
		{
			await socket.ConnectAsync(_endpoint, cancellationToken);
			await SendAsync(socket,
				TraderMadeExtensions.BuildLogin(_key, _ladder),
				cancellationToken);
			while (true)
			{
				var text = await ReceiveTextAsync(socket,
					cancellationToken);
				var control = JObject.Parse(text);
				var type = control.Value<string>("type");
				if (type.EqualsIgnoreCase("login_ok"))
					break;
				if (type.EqualsIgnoreCase("login_reject"))
					throw new InvalidOperationException(
						$"TraderMade streaming login rejected: " +
							control.Value<string>("reason"));
				if (type.EqualsIgnoreCase("error"))
					throw new InvalidOperationException(
						$"TraderMade streaming error: " +
							control.Value<string>("reason"));
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
		=> SendAsync(TraderMadeExtensions.BuildSubscription(
			symbols, true), cancellationToken);

	public ValueTask UnsubscribeAsync(IEnumerable<string> symbols,
		CancellationToken cancellationToken)
		=> SendAsync(TraderMadeExtensions.BuildSubscription(
			symbols, false), cancellationToken);

	private ValueTask SendAsync(string payload,
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket?.State != WebSocketState.Open)
			throw new InvalidOperationException(
				"TraderMade WebSocket is not connected.");
		return SendAsync(socket, payload, cancellationToken);
	}

	private async ValueTask SendAsync(ClientWebSocket socket,
		string payload, CancellationToken cancellationToken)
	{
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
				TraderMadeQuote quote;
				try
				{
					quote = TraderMadeExtensions
						.ParseStreamQuote(text);
				}
				catch (Exception error)
				{
					this.AddErrorLog(error);
					continue;
				}
				if (quote is not null && QuoteReceived is not null)
					await QuoteReceived(quote, cancellationToken);
				else
				{
					var control = JObject.Parse(text);
					var type = control.Value<string>("type");
					if (type is "error" or "logout" or
						"login_reject")
						throw new InvalidOperationException(
							$"TraderMade streaming {type}: " +
								control.Value<string>("reason"));
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
					$"TraderMade WebSocket closed: " +
						$"{result.CloseStatus} " +
						result.CloseStatusDescription);
			if (result.MessageType != WebSocketMessageType.Text)
				continue;
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maximumMessageSize)
				throw new InvalidDataException(
					"TraderMade WebSocket message exceeds 1 MiB.");
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
