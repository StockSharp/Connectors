namespace StockSharp.KoreaInvestment.Native;

sealed class KoreaInvestmentWebSocketClient : BaseLogReceiver
{
	private static readonly Uri _productionUri = new("ws://ops.koreainvestment.com:21000/tryitout");
	private static readonly Uri _simulationUri = new("ws://ops.koreainvestment.com:31000/tryitout");
	private const int _maxSubscriptions = 40;
	private const int _maxMessageSize = 8 * 1024 * 1024;

	private readonly string _approvalKey;
	private readonly bool _isDemo;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<KisStreamSubscription, KisSecurityInfo> _subscriptions = [];
	private readonly Dictionary<KisRealtimeChannels, KisStreamEncryption> _encryption = [];
	private readonly object _sync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public KoreaInvestmentWebSocketClient(string approvalKey, bool isDemo, int maxAttempts)
	{
		_approvalKey = approvalKey.ThrowIfEmpty(nameof(approvalKey));
		_isDemo = isDemo;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(KoreaInvestment) + "_WebSocket";

	public event Func<KisRealtimeEvent, CancellationToken, ValueTask> EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("KIS WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Subscribe(KisRealtimeChannels channel, string key, KisSecurityInfo security,
		CancellationToken cancellationToken)
	{
		var subscription = new KisStreamSubscription(channel, key.ThrowIfEmpty(nameof(key)));
		lock (_sync)
		{
			if (_subscriptions.ContainsKey(subscription))
				return;
			if (_subscriptions.Count >= _maxSubscriptions)
				throw new InvalidOperationException($"KIS WebSocket supports at most {_maxSubscriptions} active subscriptions.");
			_subscriptions.Add(subscription, security ?? throw new ArgumentNullException(nameof(security)));
		}

		try
		{
			var socket = _socket;
			if (socket?.State == WebSocketState.Open)
				await SendSubscription(socket, subscription, true, cancellationToken);
		}
		catch
		{
			lock (_sync)
				_subscriptions.Remove(subscription);
			throw;
		}
	}

	public async Task Unsubscribe(KisRealtimeChannels channel, string key, CancellationToken cancellationToken)
	{
		var subscription = new KisStreamSubscription(channel, key);
		lock (_sync)
		{
			if (!_subscriptions.Remove(subscription))
				return;
		}
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
			await SendSubscription(socket, subscription, false, cancellationToken);
	}

	public async Task Disconnect()
	{
		_cancellation.Cancel();
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
			}
			catch (WebSocketException)
			{
			}
		}
		if (_runTask != null)
		{
			try { await _runTask; }
			catch (OperationCanceledException) { }
		}
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
				await socket.ConnectAsync(_isDemo ? _simulationUri : _productionUri, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("KIS WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				failures++;
				await Invoke(Error, ex, cancellationToken);
				await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(ex);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				_socket = null;
				lock (_sync)
					_encryption.Clear();
			}
		}
		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		await Invoke(StateChanged, ConnectionStates.Disconnected, CancellationToken.None);
	}

	private async Task RestoreSubscriptions(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		KisStreamSubscription[] subscriptions;
		lock (_sync)
			subscriptions = [.. _subscriptions.Keys];
		foreach (var subscription in subscriptions)
		{
			await SendSubscription(socket, subscription, true, cancellationToken);
			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			if (content.IsEmpty())
				continue;
			if (content[0] is '0' or '1')
				await ProcessData(content, cancellationToken);
			else
				await ProcessSystem(socket, content, cancellationToken);
		}
	}

	private async Task ProcessData(string content, CancellationToken cancellationToken)
	{
		var parts = content.Split('|', 4);
		if (parts.Length != 4 || !KisRoutes.TryGetRealtime(parts[1], _isDemo, out var channel))
			throw new InvalidDataException("Invalid KIS realtime data envelope.");
		var payload = parts[3];
		KisStreamEncryption encryption;
		lock (_sync)
			_encryption.TryGetValue(channel, out encryption);
		if (content[0] == '1' || encryption?.Key.IsEmpty() == false)
			payload = Decrypt(payload, encryption ?? throw new InvalidDataException($"Missing KIS encryption key for {channel}."));

		var values = payload.Split('^');
		var fieldCount = KisRealtimeParser.GetFieldCount(channel);
		var recordCount = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 1;
		if (fieldCount <= 0 || values.Length < fieldCount)
			throw new InvalidDataException($"Invalid KIS {channel} payload field count {values.Length}.");

		for (var index = 0; index < recordCount && (index + 1) * fieldCount <= values.Length; index++)
		{
			var fields = values[(index * fieldCount)..((index + 1) * fieldCount)];
			var security = ResolveSecurity(channel, fields[0]);
			await Invoke(EventReceived, KisRealtimeParser.Parse(channel, fields, security), cancellationToken);
		}
	}

	private async Task ProcessSystem(ClientWebSocket socket, string content, CancellationToken cancellationToken)
	{
		var response = JsonConvert.DeserializeObject<KisStreamSystemResponse>(content, _jsonSettings)
			?? throw new InvalidDataException("Invalid KIS WebSocket system response.");
		var transactionId = response.Header?.TransactionId;
		if (transactionId.EqualsIgnoreCase("PINGPONG"))
		{
			await SendText(socket, content, cancellationToken);
			return;
		}
		if (!KisRoutes.TryGetRealtime(transactionId, _isDemo, out var channel))
			return;
		if (response.Body?.ReturnCode != "0")
		{
			await Invoke(Error, new InvalidOperationException($"KIS WebSocket {channel} error " +
				$"{response.Body?.MessageCode}: {response.Body?.Message}"), cancellationToken);
			return;
		}
		if (response.Header?.Encryption == "Y" && response.Body?.Output?.Key.IsEmpty() == false)
		{
			lock (_sync)
				_encryption[channel] = response.Body.Output;
		}
	}

	private KisSecurityInfo ResolveSecurity(KisRealtimeChannels channel, string symbol)
	{
		lock (_sync)
		{
			foreach (var pair in _subscriptions)
			{
				if (pair.Key.Channel != channel)
					continue;
				if (channel is KisRealtimeChannels.DomesticOrderNotice or KisRealtimeChannels.DerivativeOrderNotice or
					KisRealtimeChannels.OverseasOrderNotice || pair.Value.Code.EqualsIgnoreCase(symbol) ||
					pair.Key.Key.EndsWith(symbol, StringComparison.OrdinalIgnoreCase))
					return pair.Value.Code == "_" ? KisSecurityInfo.Create(symbol, pair.Value.Market, pair.Value.SecurityType) : pair.Value;
			}
		}
		return channel switch
		{
			KisRealtimeChannels.OverseasTrade or KisRealtimeChannels.OverseasDepth or KisRealtimeChannels.OverseasOrderNotice =>
				KisSecurityInfo.Create(symbol, KoreaInvestmentMarkets.Nasdaq, SecurityTypes.Stock),
			KisRealtimeChannels.DerivativeTrade or KisRealtimeChannels.DerivativeDepth or KisRealtimeChannels.DerivativeOrderNotice =>
				KisSecurityInfo.Create(symbol, KoreaInvestmentMarkets.KrxDerivatives, SecurityTypes.Future),
			KisRealtimeChannels.OptionTrade or KisRealtimeChannels.OptionDepth =>
				KisSecurityInfo.Create(symbol, KoreaInvestmentMarkets.KrxDerivatives, SecurityTypes.Option),
			_ => KisSecurityInfo.Create(symbol, KoreaInvestmentMarkets.Krx, SecurityTypes.Stock),
		};
	}

	private Task SendSubscription(ClientWebSocket socket, KisStreamSubscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> Send(socket, new KisStreamRequest
		{
			Header = new()
			{
				ApprovalKey = _approvalKey,
				TransactionType = isSubscribe ? "1" : "2",
			},
			Body = new()
			{
				Input = new()
				{
					TransactionId = KisRoutes.Get(subscription.Channel, _isDemo),
					TransactionKey = subscription.Key,
				},
			},
		}, cancellationToken);

	private Task Send(ClientWebSocket socket, KisStreamRequest request, CancellationToken cancellationToken)
		=> SendText(socket, JsonConvert.SerializeObject(request, _jsonSettings), cancellationToken);

	private async Task SendText(ClientWebSocket socket, string content, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(content);
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

	private static async Task<string> ReceiveText(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException($"KIS WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException($"Unexpected KIS WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("KIS WebSocket message exceeds 8 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	private static string Decrypt(string payload, KisStreamEncryption encryption)
	{
		using var aes = Aes.Create();
		aes.Mode = CipherMode.CBC;
		aes.Padding = PaddingMode.PKCS7;
		aes.Key = Encoding.UTF8.GetBytes(encryption.Key.ThrowIfEmpty(nameof(encryption.Key)));
		aes.IV = Encoding.UTF8.GetBytes(encryption.InitializationVector.ThrowIfEmpty(nameof(encryption.InitializationVector)));
		using var decryptor = aes.CreateDecryptor();
		var bytes = Convert.FromBase64String(payload);
		return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(bytes, 0, bytes.Length));
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
