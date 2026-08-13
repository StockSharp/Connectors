namespace StockSharp.Samco.Native;

sealed class SamcoSocketClient : IAsyncDisposable
{
	private readonly string _endpoint;
	private readonly string _sessionToken;
	private readonly CancellationTokenSource _source = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private ClientWebSocket _socket;
	private Task _reader;

	public SamcoSocketClient(string endpoint, string sessionToken)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_sessionToken = sessionToken.ThrowIfEmpty(
			nameof(sessionToken));
	}

	public event Func<string, CancellationToken, ValueTask>
		MessageReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_socket is not null)
			throw new InvalidOperationException(
				"Samco WebSocket is already initialized.");
		_socket = new();
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		_socket.Options.SetRequestHeader("x-session-token",
			_sessionToken);
		await _socket.ConnectAsync(new(_endpoint),
			cancellationToken);
		_reader = ReadLoopAsync(_source.Token);
	}

	public ValueTask SubscribeAsync(string symbolCode,
		CancellationToken cancellationToken)
		=> SendAsync(CreateSubscription([symbolCode], true),
			cancellationToken);

	public ValueTask UnsubscribeAsync(string symbolCode,
		CancellationToken cancellationToken)
		=> SendAsync(CreateSubscription([symbolCode], false),
			cancellationToken);

	internal static string CreateSubscription(
		IEnumerable<string> symbolCodes, bool subscribe)
	{
		var symbols = symbolCodes
			.Select(static value =>
				value.ThrowIfEmpty("symbolCode"))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(static value => new JObject
			{
				["symbol"] = value,
			})
			.ToArray();
		if (symbols.Length == 0)
			throw new ArgumentException(
				"At least one Samco symbol code is required.",
				nameof(symbolCodes));
		return new JObject
		{
			["request"] = new JObject
			{
				["streaming_type"] = "quote2",
				["data"] = new JObject
				{
					["symbols"] = new JArray(symbols),
				},
				["request_type"] =
					subscribe ? "subscribe" : "unsubscribe",
				["response_format"] = "json",
			},
		}.ToString(Formatting.None);
	}

	private async ValueTask SendAsync(string text,
		CancellationToken cancellationToken)
	{
		if (_socket?.State != WebSocketState.Open)
			throw new InvalidOperationException(
				"Samco WebSocket is not connected.");
		var data = Encoding.UTF8.GetBytes(text);
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await _socket.SendAsync(data,
				WebSocketMessageType.Text, true, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async Task ReadLoopAsync(
		CancellationToken cancellationToken)
	{
		var buffer = new byte[8192];
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				using var stream = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await _socket.ReceiveAsync(buffer,
						cancellationToken);
					if (result.MessageType ==
						WebSocketMessageType.Close)
						throw new EndOfStreamException(
							"Samco WebSocket closed the session.");
					stream.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);
				if (result.MessageType ==
					WebSocketMessageType.Text &&
					MessageReceived is not null)
					await MessageReceived.InvokeAsync(
						Encoding.UTF8.GetString(stream.ToArray()),
						cancellationToken);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			if (Error is not null)
				await Error.InvokeAsync(error, CancellationToken.None);
		}
	}

	public async ValueTask DisposeAsync()
	{
		_source.Cancel();
		if (_socket is not null)
		{
			if (_socket.State == WebSocketState.Open)
			{
				try
				{
					await _socket.CloseAsync(
						WebSocketCloseStatus.NormalClosure,
						"Disconnect", CancellationToken.None);
				}
				catch (Exception)
				{
				}
			}
			_socket.Dispose();
			_socket = null;
		}
		if (_reader is not null)
		{
			try
			{
				await _reader;
			}
			catch (Exception)
			{
			}
			_reader = null;
		}
		_sendSync.Dispose();
		_source.Dispose();
	}
}
