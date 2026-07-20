namespace StockSharp.Ostium.Native;

sealed class OstiumSocketClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 4 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private CancellationTokenSource _source;
	private ClientWebSocket _socket;
	private Task _receiveTask;
	private bool _isDisposed;

	public OstiumSocketClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("ws" or "wss") ||
			(_endpoint.Scheme == "ws" && !_endpoint.IsLoopback))
			throw new ArgumentException(
				"Ostium price-stream endpoint must use WSS, except locally.",
				nameof(endpoint));
	}

	public override string Name => "Ostium_PricesWS";

	public event Func<OstiumPrice, CancellationToken, ValueTask> PriceReceived;
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
					"Ostium price stream is already connected.");
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

	public async ValueTask SubscribeAsync(string pair,
		CancellationToken cancellationToken)
	{
		pair = pair.ThrowIfEmpty(nameof(pair)).Trim().ToUpperInvariant();
		var added = false;
		ClientWebSocket socket;
		using (_sync.EnterScope())
		{
			added = _subscriptions.Add(pair);
			socket = _socket;
		}
		if (added && socket?.State == WebSocketState.Open)
			await SendAsync(socket, "subscribe", [pair], cancellationToken);
	}

	public async ValueTask UnsubscribeAsync(string pair,
		CancellationToken cancellationToken)
	{
		pair = pair.ThrowIfEmpty(nameof(pair)).Trim().ToUpperInvariant();
		var removed = false;
		ClientWebSocket socket;
		using (_sync.EnterScope())
		{
			removed = _subscriptions.Remove(pair);
			socket = _socket;
		}
		if (removed && socket?.State == WebSocketState.Open)
			await SendAsync(socket, "unsubscribe", [pair], cancellationToken);
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
			_subscriptions.Clear();
		}
		if (source is null)
			return;
		source.Cancel();
		if (socket?.State is WebSocketState.Open or
			WebSocketState.CloseReceived)
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
						"Ostium price stream closed unexpectedly.");
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
		next.Options.SetRequestHeader("User-Agent", "StockSharp-Ostium/1.0");
		try
		{
			await next.ConnectAsync(_endpoint, cancellationToken);
			string[] pairs;
			using (_sync.EnterScope())
				pairs = [.. _subscriptions.OrderBy(static pair => pair,
					StringComparer.Ordinal)];
			if (pairs.Length > 0)
				await SendAsync(next, "subscribe", pairs, cancellationToken);
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

	private async ValueTask SendAsync(ClientWebSocket socket, string type,
		string[] pairs, CancellationToken cancellationToken)
	{
		var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
			new OstiumPriceSubscriptionRequest
			{
				Type = type,
				Pairs = pairs,
			}, _settings));
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(payload, WebSocketMessageType.Text, true,
				cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async ValueTask ReceiveAsync(CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		using (_sync.EnterScope())
			socket = _socket ?? throw new InvalidOperationException(
				"Ostium price stream is not connected.");
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
						"Ostium price stream returned a binary message.");
				if (stream.Length + result.Count > _maximumMessageBytes)
					throw new InvalidDataException(
						"Ostium price-stream message is too large.");
				stream.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);
			var text = Encoding.UTF8.GetString(stream.GetBuffer(), 0,
				checked((int)stream.Length));
			await ProcessAsync(text, cancellationToken);
		}
	}

	private async ValueTask ProcessAsync(string text,
		CancellationToken cancellationToken)
	{
		OstiumPriceSocketHeader header;
		try
		{
			header = JsonConvert.DeserializeObject<OstiumPriceSocketHeader>(text,
				_settings) ?? throw new InvalidDataException(
					"Ostium price stream returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Ostium price stream returned invalid JSON.", error);
		}
		switch (header.Type?.ToLowerInvariant())
		{
			case "snapshot":
				var snapshot = JsonConvert.DeserializeObject<OstiumPriceSnapshot>(
					text, _settings) ?? throw new InvalidDataException(
						"Ostium price stream returned an empty snapshot.");
				foreach (var price in snapshot.Data ?? [])
					if (price is not null && PriceReceived is not null)
						await PriceReceived(price, cancellationToken);
				break;
			case "tick":
				var tick = JsonConvert.DeserializeObject<OstiumPriceTick>(text,
					_settings) ?? throw new InvalidDataException(
						"Ostium price stream returned an empty tick.");
				if (tick.Data is not null && PriceReceived is not null)
					await PriceReceived(tick.Data, cancellationToken);
				break;
			case "error":
				var error = JsonConvert.DeserializeObject<OstiumPriceSocketError>(
					text, _settings);
				throw new InvalidOperationException(
					"Ostium price stream: " +
					(error?.Message.IsEmpty() == false
						? error.Message
						: error?.Error ?? "request rejected"));
		}
	}

	protected override void DisposeManaged()
	{
		using (_sync.EnterScope())
			_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
