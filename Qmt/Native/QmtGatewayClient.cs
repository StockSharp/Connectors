namespace StockSharp.Qmt.Native;

using Model;

internal sealed class QmtGatewayClient : IDisposable
{
	private readonly string _host;
	private readonly int _port;
	private readonly string _token;
	private readonly int _maxAttempts;
	private readonly TimeSpan _requestTimeout;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly ConcurrentDictionary<long, TaskCompletionSource<QmtGatewayEnvelope>> _pending = [];
	private readonly ConcurrentDictionary<long, QmtSubscriptionRequest> _subscriptions = [];
	private readonly TaskCompletionSource _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private TcpClient _tcpClient;
	private NetworkStream _stream;
	private Task _runTask;
	private long _requestId;

	public QmtGatewayClient(string host, int port, string token, int maxAttempts, TimeSpan requestTimeout)
	{
		_host = host.ThrowIfEmpty(nameof(host));
		_port = port is > 0 and <= ushort.MaxValue ? port : throw new ArgumentOutOfRangeException(nameof(port));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_requestTimeout = requestTimeout > TimeSpan.Zero ? requestTimeout : throw new ArgumentOutOfRangeException(nameof(requestTimeout));
	}

	public QmtHello Session { get; private set; }

	public event Func<long, QmtQuote, CancellationToken, ValueTask> Level1Received;
	public event Func<long, QmtQuote, CancellationToken, ValueTask> DepthReceived;
	public event Func<long, QmtMarketTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<long, QmtCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<QmtOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<QmtFill, CancellationToken, ValueTask> FillReceived;
	public event Func<QmtAsset, CancellationToken, ValueTask> AssetReceived;
	public event Func<QmtPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("The QMT gateway client is already running.");

		_runTask = RunAsync(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task<QmtSecurity[]> SearchAsync(string query, string[] markets, int limit,
		CancellationToken cancellationToken)
	{
		var response = await SendRequestAsync(new()
		{
			Kind = QmtGatewayKinds.Search,
			SearchRequest = new()
			{
				Query = query,
				Markets = markets ?? [],
				Limit = Math.Clamp(limit, 1, 5000),
			},
		}, cancellationToken);
		return response.Securities ?? [];
	}

	public async Task<QmtSecurity> GetSecurityAsync(string symbol, CancellationToken cancellationToken)
	{
		var response = await SendRequestAsync(new()
		{
			Kind = QmtGatewayKinds.Security,
			SecurityRequest = new() { Symbol = symbol.ThrowIfEmpty(nameof(symbol)) },
		}, cancellationToken);
		return response.Securities?.FirstOrDefault();
	}

	public async Task<QmtCandle[]> GetHistoryAsync(QmtHistoryRequest request, CancellationToken cancellationToken)
	{
		var response = await SendRequestAsync(new()
		{
			Kind = QmtGatewayKinds.History,
			HistoryRequest = request ?? throw new ArgumentNullException(nameof(request)),
		}, cancellationToken);
		return response.Candles ?? [];
	}

	public async Task<QmtGatewayEnvelope> GetAccountsAsync(CancellationToken cancellationToken)
		=> await SendRequestAsync(new() { Kind = QmtGatewayKinds.Accounts }, cancellationToken);

	public async Task<QmtPosition[]> GetPositionsAsync(CancellationToken cancellationToken)
		=> (await SendRequestAsync(new() { Kind = QmtGatewayKinds.Positions }, cancellationToken)).Positions ?? [];

	public async Task<QmtOrder[]> GetOrdersAsync(CancellationToken cancellationToken)
		=> (await SendRequestAsync(new() { Kind = QmtGatewayKinds.Orders }, cancellationToken)).Orders ?? [];

	public async Task<QmtFill[]> GetFillsAsync(CancellationToken cancellationToken)
		=> (await SendRequestAsync(new() { Kind = QmtGatewayKinds.Fills }, cancellationToken)).Fills ?? [];

	public async Task<long> PlaceOrderAsync(QmtOrderRequest request, CancellationToken cancellationToken)
	{
		var response = await SendRequestAsync(new()
		{
			Kind = QmtGatewayKinds.PlaceOrder,
			OrderRequest = request ?? throw new ArgumentNullException(nameof(request)),
		}, cancellationToken);
		return response.OrderId is > 0
			? response.OrderId.Value
			: throw new InvalidOperationException("The QMT gateway returned no order identifier.");
	}

	public Task CancelOrderAsync(QmtCancelRequest request, CancellationToken cancellationToken)
		=> SendRequestAsync(new()
		{
			Kind = QmtGatewayKinds.CancelOrder,
			CancelRequest = request ?? throw new ArgumentNullException(nameof(request)),
		}, cancellationToken);

	public async Task SubscribeAsync(QmtSubscriptionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!_subscriptions.TryAdd(request.SubscriptionId, request))
			throw new InvalidOperationException($"QMT subscription {request.SubscriptionId} already exists.");

		try
		{
			await SendRequestAsync(new()
			{
				Kind = QmtGatewayKinds.Subscribe,
				SubscriptionRequest = request,
			}, cancellationToken);
		}
		catch
		{
			_subscriptions.TryRemove(request.SubscriptionId, out _);
			throw;
		}
	}

	public async Task UnsubscribeAsync(long subscriptionId, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryRemove(subscriptionId, out var request))
			return;

		await SendRequestAsync(new()
		{
			Kind = QmtGatewayKinds.Unsubscribe,
			SubscriptionRequest = request,
		}, cancellationToken);
	}

	public async Task DisconnectAsync()
	{
		_cancellation.Cancel();
		var client = _tcpClient;
		if (client != null)
		{
			try { client.Close(); }
			catch { }
		}
		if (_runTask != null)
		{
			try { await _runTask; }
			catch (OperationCanceledException) { }
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var failures = 0;
		var wasConnected = false;

		while (!cancellationToken.IsCancellationRequested)
		{
			using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			try
			{
				await InvokeAsync(StateChanged, ConnectionStates.Connecting, cancellationToken);
				var client = new TcpClient { NoDelay = true };
				_tcpClient = client;
				await client.ConnectAsync(_host, _port, cancellationToken);
				_stream = client.GetStream();

				var receiveTask = ReceiveLoopAsync(_stream, connectionCancellation.Token);
				var hello = await SendRequestAsync(new()
				{
					Kind = QmtGatewayKinds.Hello,
					HelloRequest = new() { Token = _token, Client = "StockSharp" },
				}, connectionCancellation.Token);
				Session = hello.Hello ?? throw new InvalidDataException("The QMT gateway returned no session metadata.");
				await RestoreSubscriptionsAsync(connectionCancellation.Token);

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await InvokeAsync(StateChanged, ConnectionStates.Connected, cancellationToken);

				var heartbeatTask = HeartbeatLoopAsync(connectionCancellation.Token);
				var completed = await Task.WhenAny(receiveTask, heartbeatTask);
				await completed;
				if (!cancellationToken.IsCancellationRequested)
					throw new IOException("The QMT gateway connection closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				await InvokeAsync(Error, error, cancellationToken);
				await InvokeAsync(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				connectionCancellation.Cancel();
				_stream = null;
				var client = Interlocked.Exchange(ref _tcpClient, null);
				try { client?.Dispose(); }
				catch { }
				FailPending(new IOException("The QMT gateway connection is not available."));
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		await InvokeAsync(StateChanged, ConnectionStates.Disconnected, CancellationToken.None);
	}

	private async Task RestoreSubscriptionsAsync(CancellationToken cancellationToken)
	{
		foreach (var request in _subscriptions.Values.OrderBy(item => item.SubscriptionId))
		{
			await SendRequestAsync(new()
			{
				Kind = QmtGatewayKinds.Subscribe,
				SubscriptionRequest = request,
			}, cancellationToken);
		}
	}

	private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
			await SendRequestAsync(new() { Kind = QmtGatewayKinds.Ping }, cancellationToken);
		}
	}

	private async Task<QmtGatewayEnvelope> SendRequestAsync(QmtGatewayEnvelope request,
		CancellationToken cancellationToken)
	{
		var stream = _stream ?? throw new InvalidOperationException("The QMT gateway is not connected.");
		request.Version = QmtGatewayProtocol.Version;
		request.RequestId = Interlocked.Increment(ref _requestId);
		var source = new TaskCompletionSource<QmtGatewayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_pending.TryAdd(request.RequestId, source))
			throw new InvalidOperationException($"Duplicate QMT request {request.RequestId}.");

		try
		{
			await SendFrameAsync(stream, request, cancellationToken);
			var response = await source.Task.WaitAsync(_requestTimeout, cancellationToken);
			if (response.Version != QmtGatewayProtocol.Version)
				throw new InvalidDataException($"Unsupported QMT gateway protocol version {response.Version}.");
			if (response.IsSuccess == false || response.Error != null)
				throw new InvalidOperationException($"QMT gateway {response.Error?.Code}: {response.Error?.Message}".TrimEnd(':', ' '));
			return response;
		}
		finally
		{
			_pending.TryRemove(request.RequestId, out _);
		}
	}

	private async Task SendFrameAsync(NetworkStream stream, QmtGatewayEnvelope message,
		CancellationToken cancellationToken)
	{
		var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, _jsonSettings));
		if (payload.Length > QmtGatewayProtocol.MaxFrameSize)
			throw new InvalidDataException("The QMT gateway request exceeds 16 MiB.");
		var header = new byte[4]
		{
			(byte)(payload.Length >> 24),
			(byte)(payload.Length >> 16),
			(byte)(payload.Length >> 8),
			(byte)payload.Length,
		};

		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			await stream.WriteAsync(header, cancellationToken);
			await stream.WriteAsync(payload, cancellationToken);
			await stream.FlushAsync(cancellationToken);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		var header = new byte[4];
		while (!cancellationToken.IsCancellationRequested)
		{
			await ReadExactlyAsync(stream, header, cancellationToken);
			var length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
			if (length <= 0 || length > QmtGatewayProtocol.MaxFrameSize)
				throw new InvalidDataException($"Invalid QMT gateway frame length {length}.");

			var payload = new byte[length];
			await ReadExactlyAsync(stream, payload, cancellationToken);
			var message = JsonConvert.DeserializeObject<QmtGatewayEnvelope>(Encoding.UTF8.GetString(payload), _jsonSettings)
				?? throw new InvalidDataException("The QMT gateway returned an empty message.");
			await ProcessMessageAsync(message, cancellationToken);
		}
	}

	private async Task ProcessMessageAsync(QmtGatewayEnvelope message, CancellationToken cancellationToken)
	{
		if (message.RequestId != 0 && _pending.TryRemove(message.RequestId, out var source))
		{
			source.TrySetResult(message);
			return;
		}

		switch (message.Kind)
		{
			case QmtGatewayKinds.Level1:
				if (message.Quote != null)
					await InvokeAsync(Level1Received, message.SubscriptionId, message.Quote, cancellationToken);
				break;
			case QmtGatewayKinds.Depth:
				if (message.Quote != null)
					await InvokeAsync(DepthReceived, message.SubscriptionId, message.Quote, cancellationToken);
				break;
			case QmtGatewayKinds.Trade:
				if (message.Trade != null)
					await InvokeAsync(TradeReceived, message.SubscriptionId, message.Trade, cancellationToken);
				break;
			case QmtGatewayKinds.Candle:
				if (message.Candle != null)
					await InvokeAsync(CandleReceived, message.SubscriptionId, message.Candle, cancellationToken);
				break;
			case QmtGatewayKinds.Order:
				if (message.Order != null)
					await InvokeAsync(OrderReceived, message.Order, cancellationToken);
				break;
			case QmtGatewayKinds.Fill:
				if (message.Fill != null)
					await InvokeAsync(FillReceived, message.Fill, cancellationToken);
				break;
			case QmtGatewayKinds.Asset:
				if (message.Asset != null)
					await InvokeAsync(AssetReceived, message.Asset, cancellationToken);
				break;
			case QmtGatewayKinds.Position:
				if (message.Position != null)
					await InvokeAsync(PositionReceived, message.Position, cancellationToken);
				break;
			case QmtGatewayKinds.Connection:
				if (message.Connection?.IsConnected == false)
					await InvokeAsync(Error, new IOException(message.Connection.Message.IsEmpty("MiniQMT disconnected.")), cancellationToken);
				break;
			case QmtGatewayKinds.Error:
				if (message.Error != null)
					await InvokeAsync(Error, new InvalidOperationException(
						$"QMT gateway {message.Error.Code}: {message.Error.Message}".TrimEnd(':', ' ')), cancellationToken);
				break;
		}
	}

	private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer,
		CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
			if (read == 0)
				throw new EndOfStreamException("The QMT gateway closed the TCP stream.");
			offset += read;
		}
	}

	private void FailPending(Exception error)
	{
		foreach (var pair in _pending.ToArray())
		{
			if (_pending.TryRemove(pair.Key, out var source))
				source.TrySetException(error);
		}
	}

	private static ValueTask InvokeAsync<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	private static ValueTask InvokeAsync<T1, T2>(Func<T1, T2, CancellationToken, ValueTask> handler,
		T1 value1, T2 value2, CancellationToken cancellationToken)
		=> handler == null ? default : handler(value1, value2, cancellationToken);

	public void Dispose()
	{
		DisconnectAsync().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
