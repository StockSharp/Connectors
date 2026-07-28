namespace StockSharp.Dexalot.Native;

sealed class DexalotSocketClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;

	public DexalotSocketClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase("ws") ||
				_endpoint.Scheme.EqualsIgnoreCase("wss")))
			throw new ArgumentException(
				"Dexalot WebSocket endpoint must be an absolute WS or WSS URI.",
				nameof(endpoint));
	}

	public override string Name => "Dexalot_WebSocket";

	public event Action<JObject> MessageReceived;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Dexalot WebSocket is already connected.");
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Dexalot-Connector/1.0");
		var lifetime = new CancellationTokenSource();
		try
		{
			await socket.ConnectAsync(_endpoint, cancellationToken);
			_socket = socket;
			_lifetime = lifetime;
			_receiveTask = ReceiveLoopAsync(lifetime.Token);
		}
		catch
		{
			lifetime.Cancel();
			lifetime.Dispose();
			socket.Abort();
			socket.Dispose();
			throw;
		}
	}

	public ValueTask SubscribePairAsync(DexalotPair pair, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pair);
		return SendAsync(new JObject
		{
			["data"] = pair.Pair,
			["pair"] = pair.Pair,
			["type"] = isSubscribe ? "subscribe" : "unsubscribe",
			["decimal"] = pair.QuoteDisplayDecimals,
		}, cancellationToken);
	}

	public ValueTask SubscribeChartAsync(DexalotPair pair,
		TimeSpan timeFrame, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pair);
		return SendAsync(new JObject
		{
			["data"] = pair.Pair,
			["pair"] = pair.Pair,
			["chart"] = timeFrame.ToChartCode(),
			["type"] = isSubscribe
				? "chartsubscribe"
				: "chartunsubscribe",
		}, cancellationToken);
	}

	protected override void DisposeManaged()
	{
		_lifetime?.Cancel();
		_socket?.Abort();
		_socket?.Dispose();
		_lifetime?.Dispose();
		_sendGate.Dispose();
		_socket = null;
		_lifetime = null;
		_receiveTask = null;
		base.DisposeManaged();
	}

	private async ValueTask SendAsync(JObject message,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(message);
		var socket = _socket ?? throw new InvalidOperationException(
			"The Dexalot WebSocket is not connected.");
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
						"Dexalot WebSocket returned an unexpected payload.",
						error);
				}
				if (message is null)
					throw new InvalidDataException(
						"Dexalot WebSocket returned an empty payload.");
				try
				{
					MessageReceived?.Invoke(message);
				}
				catch (Exception error)
				{
					this.AddErrorLog(error);
				}
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			this.AddWarningLog(
				"Dexalot WebSocket receive loop stopped: {0}",
				error.Message);
		}
	}

	private async ValueTask<string> ReceiveAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket ?? throw new InvalidOperationException(
			"The Dexalot WebSocket is not connected.");
		using var target = new MemoryStream();
		var buffer = new byte[8192];
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"Dexalot WebSocket closed with status " +
						$"'{socket.CloseStatus}'.");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					"Dexalot WebSocket returned a non-text message.");
			if (target.Length + result.Count > _maximumMessageBytes)
				throw new InvalidDataException(
					"Dexalot WebSocket message exceeds 16 MiB.");
			target.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(target.ToArray());
		}
	}
}
