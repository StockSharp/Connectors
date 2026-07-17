namespace StockSharp.XOpenHub.Native;

internal sealed class XApiStreamClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly Uri _uri;
	private readonly string _sessionId;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _ticks = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _candles = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _subscriptionsSync = new();
	private ClientWebSocket _socket;
	private Task _runTask;
	private DateTime _lastSend;
	private bool _balance;
	private bool _trades;
	private bool _tradeStatus;

	public XApiStreamClient(bool isDemo, string sessionId, int maxAttempts)
	{
		_uri = new(isDemo ? "wss://ws.xapi.pro/demoStream" : "wss://ws.xapi.pro/realStream");
		_sessionId = sessionId.ThrowIfEmpty(nameof(sessionId));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(XOpenHub) + "_Stream";

	public event Func<XApiTick, CancellationToken, ValueTask> TickReceived;
	public event Func<XApiStreamCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<XApiStreamBalance, CancellationToken, ValueTask> BalanceReceived;
	public event Func<XApiTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<XApiTradeStatus, CancellationToken, ValueTask> TradeStatusReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("X Open Hub stream is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public Task SubscribeTicks(string symbol, CancellationToken cancellationToken)
		=> SetSymbolSubscription(_ticks, symbol, true, "getTickPrices", cancellationToken);

	public Task UnsubscribeTicks(string symbol, CancellationToken cancellationToken)
		=> SetSymbolSubscription(_ticks, symbol, false, "stopTickPrices", cancellationToken);

	public Task SubscribeCandles(string symbol, CancellationToken cancellationToken)
		=> SetSymbolSubscription(_candles, symbol, true, "getCandles", cancellationToken);

	public Task UnsubscribeCandles(string symbol, CancellationToken cancellationToken)
		=> SetSymbolSubscription(_candles, symbol, false, "stopCandles", cancellationToken);

	public Task SetBalance(bool enabled, CancellationToken cancellationToken)
		=> SetGlobalSubscription(enabled, () => _balance, value => _balance = value,
			"getBalance", "stopBalance", cancellationToken);

	public Task SetTrades(bool enabled, CancellationToken cancellationToken)
		=> SetGlobalSubscription(enabled, () => _trades, value => _trades = value,
			"getTrades", "stopTrades", cancellationToken);

	public Task SetTradeStatus(bool enabled, CancellationToken cancellationToken)
		=> SetGlobalSubscription(enabled, () => _tradeStatus, value => _tradeStatus = value,
			"getTradeStatus", "stopTradeStatus", cancellationToken);

	public async Task Disconnect()
	{
		_cancellation.Cancel();
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
					"Client disconnect", CancellationToken.None);
			}
			catch (WebSocketException)
			{
			}
		}
		if (_runTask != null)
		{
			try
			{
				await _runTask;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private async Task SetSymbolSubscription(HashSet<string> subscriptions, string symbol,
		bool subscribe, string command, CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol));
		bool changed;
		lock (_subscriptionsSync)
			changed = subscribe ? subscriptions.Add(symbol) : subscriptions.Remove(symbol);
		if (!changed || _socket?.State != WebSocketState.Open)
			return;
		await Send(_socket, new()
		{
			Command = command,
			StreamSessionId = subscribe ? _sessionId : null,
			Symbol = symbol,
			MinArrivalTime = command == "getTickPrices" ? 200 : null,
			MaxLevel = command == "getTickPrices" ? 0 : null,
		}, cancellationToken);
	}

	private async Task SetGlobalSubscription(bool enabled, Func<bool> getter,
		Action<bool> setter, string subscribeCommand, string unsubscribeCommand,
		CancellationToken cancellationToken)
	{
		lock (_subscriptionsSync)
		{
			if (getter() == enabled)
				return;
			setter(enabled);
		}
		if (_socket?.State == WebSocketState.Open)
			await Send(_socket, new()
			{
				Command = enabled ? subscribeCommand : unsubscribeCommand,
				StreamSessionId = enabled ? _sessionId : null,
			}, cancellationToken);
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		var wasConnected = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Invoke(StateChanged, ConnectionStates.Connecting, cancellationToken);
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				await socket.ConnectAsync(_uri, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("X Open Hub stream closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				await Invoke(Error, error, cancellationToken);
				await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,
					1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				_socket = null;
			}
		}
		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		await Invoke(StateChanged, ConnectionStates.Disconnected, CancellationToken.None);
	}

	private async Task RestoreSubscriptions(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		string[] ticks;
		string[] candles;
		bool balance;
		bool trades;
		bool tradeStatus;
		lock (_subscriptionsSync)
		{
			ticks = _ticks.ToArray();
			candles = _candles.ToArray();
			balance = _balance;
			trades = _trades;
			tradeStatus = _tradeStatus;
		}

		await Send(socket, new() { Command = "getKeepAlive", StreamSessionId = _sessionId },
			cancellationToken);
		foreach (var symbol in ticks)
			await Send(socket, new()
			{
				Command = "getTickPrices",
				StreamSessionId = _sessionId,
				Symbol = symbol,
				MinArrivalTime = 200,
				MaxLevel = 0,
			}, cancellationToken);
		foreach (var symbol in candles)
			await Send(socket, new()
			{
				Command = "getCandles",
				StreamSessionId = _sessionId,
				Symbol = symbol,
			}, cancellationToken);
		if (balance)
			await Send(socket, new() { Command = "getBalance", StreamSessionId = _sessionId },
				cancellationToken);
		if (trades)
			await Send(socket, new() { Command = "getTrades", StreamSessionId = _sessionId },
				cancellationToken);
		if (tradeStatus)
			await Send(socket, new() { Command = "getTradeStatus", StreamSessionId = _sessionId },
				cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			var header = JsonConvert.DeserializeObject<XApiStreamHeader>(content)
				?? throw new InvalidDataException("X Open Hub returned an invalid stream message.");
			switch (header.Command)
			{
				case "tickPrices":
					await Invoke(TickReceived, Deserialize<XApiTick>(content), cancellationToken);
					break;
				case "candle":
					await Invoke(CandleReceived, Deserialize<XApiStreamCandle>(content), cancellationToken);
					break;
				case "balance":
					await Invoke(BalanceReceived, Deserialize<XApiStreamBalance>(content), cancellationToken);
					break;
				case "trade":
					await Invoke(TradeReceived, Deserialize<XApiTrade>(content), cancellationToken);
					break;
				case "tradeStatus":
					await Invoke(TradeStatusReceived, Deserialize<XApiTradeStatus>(content), cancellationToken);
					break;
				case "keepAlive":
					break;
			}
		}
	}

	private static T Deserialize<T>(string content)
		where T : class
		=> JsonConvert.DeserializeObject<XApiStreamMessage<T>>(content)?.Data
			?? throw new InvalidDataException(
				$"X Open Hub returned an invalid {typeof(T).Name} stream payload.");

	private async Task Send(ClientWebSocket socket, XApiStreamRequest request,
		CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
		if (bytes.Length > 1024)
			throw new InvalidOperationException(
				$"X Open Hub stream command '{request.Command}' exceeds the protocol's 1 KiB limit.");
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			var wait = TimeSpan.FromMilliseconds(200) - (DateTime.UtcNow - _lastSend);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);
			await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
			_lastSend = DateTime.UtcNow;
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private static async Task<string> ReceiveText(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"X Open Hub stream closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected X Open Hub stream message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("X Open Hub stream message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		base.DisposeManaged();
	}
}
