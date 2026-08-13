namespace StockSharp.MStock.Native;

sealed class MStockSocketClient : IAsyncDisposable
{
	private readonly string _endpoint;
	private readonly string _apiKey;
	private readonly string _accessToken;
	private readonly CancellationTokenSource _source = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private ClientWebSocket _socket;
	private Task _reader;
	private int _faulted;

	public MStockSocketClient(string endpoint, string apiKey,
		string accessToken)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_accessToken = accessToken.ThrowIfEmpty(
			nameof(accessToken));
	}

	public bool IsConnected =>
		Volatile.Read(ref _faulted) == 0 &&
		_socket?.State == WebSocketState.Open;

	public event Func<byte[], CancellationToken, ValueTask>
		BinaryReceived;

	public event Func<string, CancellationToken, ValueTask>
		TextReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_socket is not null)
			throw new InvalidOperationException(
				"m.Stock WebSocket is already initialized.");
		_socket = new();
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		await _socket.ConnectAsync(BuildUri(_endpoint, _apiKey,
			_accessToken), cancellationToken);
		Volatile.Write(ref _faulted, 0);
		_reader = ReadLoopAsync(_source.Token);
		await SendTextAsync($"LOGIN:{_accessToken}",
			cancellationToken);
	}

	public ValueTask SubscribeAsync(MStockInstrumentRef instrument,
		int mode, CancellationToken cancellationToken)
		=> SendTextAsync(CreateSubscription(instrument.Exchange,
			[instrument.Token], mode, true), cancellationToken);

	public ValueTask UnsubscribeAsync(
		MStockInstrumentRef instrument,
		CancellationToken cancellationToken)
		=> SendTextAsync(CreateSubscription(instrument.Exchange,
			[instrument.Token], 3, false), cancellationToken);

	internal static string CreateSubscription(string exchange,
		IEnumerable<string> tokens, int mode, bool subscribe)
	{
		if (mode is < 1 or > 3)
			throw new ArgumentOutOfRangeException(nameof(mode));
		var values = tokens
			.Select(static value => value.ThrowIfEmpty("token"))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		if (values.Length == 0)
			throw new ArgumentException(
				"At least one m.Stock token is required.",
				nameof(tokens));
		return new JObject
		{
			["correlationID"] = string.Empty,
			["action"] = subscribe ? 1 : 0,
			["params"] = new JObject
			{
				["mode"] = mode,
				["tokenList"] = new JArray
				{
					new JObject
					{
						["exchangeType"] =
							exchange.ToMStockExchangeType(),
						["tokens"] = new JArray(values),
					},
				},
			},
		}.ToString(Formatting.None);
	}

	internal static Uri BuildUri(string endpoint, string apiKey,
		string accessToken)
	{
		var separator = endpoint.Contains('?') ? '&' : '?';
		return new($"{endpoint}{separator}ACCESS_TOKEN=" +
			$"{Uri.EscapeDataString(accessToken)}&API_KEY=" +
			Uri.EscapeDataString(apiKey));
	}

	private async ValueTask SendTextAsync(string text,
		CancellationToken cancellationToken)
	{
		if (_socket?.State != WebSocketState.Open)
			throw new InvalidOperationException(
				"m.Stock WebSocket is not connected.");
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
				using var message = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await _socket.ReceiveAsync(buffer,
						cancellationToken);
					if (result.MessageType ==
						WebSocketMessageType.Close)
						throw new EndOfStreamException(
							"m.Stock WebSocket closed the session.");
					message.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				var payload = message.ToArray();
				if (result.MessageType ==
					WebSocketMessageType.Binary)
				{
					if (BinaryReceived is not null)
						await BinaryReceived.InvokeAsync(payload,
							cancellationToken);
				}
				else if (TextReceived is not null)
					await TextReceived.InvokeAsync(
						Encoding.UTF8.GetString(payload),
						cancellationToken);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			Volatile.Write(ref _faulted, 1);
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
