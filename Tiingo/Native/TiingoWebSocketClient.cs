namespace StockSharp.Tiingo.Native;

sealed class TiingoWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly TiingoMarkets _market;
	private readonly Uri _uri;
	private readonly string _token;
	private readonly int _threshold;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _activeSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _subscriptionsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;
	private TaskCompletionSource<long> _subscriptionReady;
	private long? _subscriptionId;

	public TiingoWebSocketClient(TiingoMarkets market, Uri address, string token,
		int threshold, int maxAttempts)
	{
		_market = market;
		_uri = address ?? throw new ArgumentNullException(nameof(address));
		_token = token.ThrowIfEmpty(nameof(token));
		_threshold = threshold;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => $"{nameof(Tiingo)}_{_market}_{nameof(TiingoWebSocketClient)}";

	public event Func<TiingoMarkets, TiingoStreamData, CancellationToken, ValueTask> DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task Subscribe(string ticker, CancellationToken cancellationToken)
	{
		ticker = Extensions.NormalizeTicker(ticker.ThrowIfEmpty(nameof(ticker)));
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.Add(ticker))
				return;
			if (_runTask == null)
			{
				_runTask = Run(_cancellation.Token);
			}
		}

		try
		{
			await _initialConnection.Task.WaitAsync(cancellationToken);
			var subscriptionId = await GetSubscriptionReadyTask().WaitAsync(cancellationToken);
			if (_market == TiingoMarkets.Crypto)
				return;

			var isActive = false;
			lock (_subscriptionsSync)
				isActive = _activeSubscriptions.Contains(ticker);
			if (!isActive)
			{
				var socket = _socket ?? throw new WebSocketException(
					"Tiingo WebSocket is not connected.");
				await SendUpdate(socket, subscriptionId, ticker, true, cancellationToken);
				lock (_subscriptionsSync)
					_activeSubscriptions.Add(ticker);
			}
		}
		catch
		{
			lock (_subscriptionsSync)
				_subscriptions.Remove(ticker);
			throw;
		}
	}

	public async Task Unsubscribe(string ticker, CancellationToken cancellationToken)
	{
		ticker = Extensions.NormalizeTicker(ticker);
		var wasActive = false;
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.Remove(ticker))
				return;
			wasActive = _activeSubscriptions.Remove(ticker);
		}

		if (_market == TiingoMarkets.Crypto || !wasActive)
			return;
		var socket = _socket;
		var subscriptionId = _subscriptionId;
		if (socket?.State == WebSocketState.Open && subscriptionId != null)
			await SendUpdate(socket, subscriptionId.Value, ticker, false, cancellationToken);
	}

	public async Task Disconnect()
	{
		RequestStop();
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

	public void RequestStop() => _cancellation.Cancel();

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		var wasConnected = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				await socket.ConnectAsync(_uri, cancellationToken);

				string[] subscriptions;
				lock (_subscriptionsSync)
				{
					subscriptions = [.. _subscriptions];
					_activeSubscriptions.Clear();
					if (_market != TiingoMarkets.Crypto)
						_activeSubscriptions.UnionWith(subscriptions);
					_subscriptionId = null;
					_subscriptionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
				}
				if (subscriptions.Length == 0)
					break;

				await SendInitial(socket, subscriptions, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Tiingo WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				GetSubscriptionReadySource()?.TrySetException(error);
				await Invoke(Error, error, cancellationToken);
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
				lock (_subscriptionsSync)
				{
					_subscriptionId = null;
					_activeSubscriptions.Clear();
				}
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		GetSubscriptionReadySource()?.TrySetCanceled(cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			TiingoStreamEnvelope envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<TiingoStreamEnvelope>(content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException("Invalid Tiingo WebSocket message.", error);
			}

			if (envelope == null || envelope.MessageType.EqualsIgnoreCase("H"))
				continue;
			if (envelope.MessageType.EqualsIgnoreCase("E") || envelope.Response?.Code is >= 400)
				throw CreateStreamError(envelope);
			if (envelope.MessageType.EqualsIgnoreCase("I"))
			{
				if (envelope.Data?.SubscriptionId is { } subscriptionId)
				{
					lock (_subscriptionsSync)
						_subscriptionId = subscriptionId;
					GetSubscriptionReadySource()?.TrySetResult(subscriptionId);
				}
				continue;
			}
			if (envelope.Data?.MarketData is { } marketData)
				await Invoke(DataReceived, _market, marketData, cancellationToken);
		}
	}

	private Task SendInitial(ClientWebSocket socket, string[] subscriptions,
		CancellationToken cancellationToken)
		=> Send(socket, new TiingoStreamRequest
		{
			EventName = "subscribe",
			Authorization = _token,
			EventData = new()
			{
				ThresholdLevel = _threshold,
				Tickers = _market == TiingoMarkets.Crypto ? null : subscriptions,
			},
		}, cancellationToken);

	private Task SendUpdate(ClientWebSocket socket, long subscriptionId, string ticker,
		bool isSubscribe, CancellationToken cancellationToken)
		=> Send(socket, new TiingoStreamRequest
		{
			EventName = isSubscribe ? "subscribe" : "unsubscribe",
			Authorization = _token,
			EventData = new()
			{
				SubscriptionId = subscriptionId,
				Tickers = [ticker],
			},
		}, cancellationToken);

	private async Task Send(ClientWebSocket socket, TiingoStreamRequest request,
		CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request, _jsonSettings));
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private Task<long> GetSubscriptionReadyTask()
	{
		lock (_subscriptionsSync)
			return (_subscriptionReady ?? throw new InvalidOperationException(
				"Tiingo WebSocket subscription was not initialized.")).Task;
	}

	private TaskCompletionSource<long> GetSubscriptionReadySource()
	{
		lock (_subscriptionsSync)
			return _subscriptionReady;
	}

	private static InvalidOperationException CreateStreamError(TiingoStreamEnvelope envelope)
	{
		var code = envelope.Response?.Code == null ? string.Empty :
			$" ({envelope.Response.Code.Value})";
		return new($"Tiingo WebSocket error{code}: " +
			(envelope.Response?.Message).IsEmpty("unknown error"));
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
				throw new WebSocketException($"Tiingo WebSocket closed: {result.CloseStatus} " +
					result.CloseStatusDescription);
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected Tiingo WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Tiingo WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	private static ValueTask Invoke<T1, T2>(Func<T1, T2, CancellationToken, ValueTask> handler,
		T1 value1, T2 value2, CancellationToken cancellationToken)
		=> handler == null ? default : handler(value1, value2, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		base.DisposeManaged();
	}
}
