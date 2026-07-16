namespace StockSharp.Kiwoom.Native;

sealed class KiwoomWebSocketClient : BaseLogReceiver
{
	private const int _maxSymbols = 200;
	private const int _maxMessageSize = 8 * 1024 * 1024;

	private readonly Func<CancellationToken, Task<string>> _tokenProvider;
	private readonly KiwoomAssetClasses _assetClass;
	private readonly bool _isDemo;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<KiwoomStreamSubscription, KiwoomSecurityInfo> _subscriptions = [];
	private readonly object _sync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public KiwoomWebSocketClient(Func<CancellationToken, Task<string>> tokenProvider, KiwoomAssetClasses assetClass,
		bool isDemo, int maxAttempts)
	{
		_tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
		_assetClass = assetClass;
		_isDemo = isDemo;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => $"{nameof(Kiwoom)}_{_assetClass}_WebSocket";

	public event Func<KiwoomRealtimeMessage, CancellationToken, ValueTask> EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Kiwoom WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public Task Subscribe(KiwoomSecurityInfo security, string type, CancellationToken cancellationToken)
		=> ChangeSubscription(security ?? throw new ArgumentNullException(nameof(security)), type, true, cancellationToken);

	public Task Unsubscribe(KiwoomSecurityInfo security, string type, CancellationToken cancellationToken)
		=> ChangeSubscription(security ?? throw new ArgumentNullException(nameof(security)), type, false, cancellationToken);

	public Task SubscribePrivate(string type, CancellationToken cancellationToken)
		=> ChangeSubscription(KiwoomSecurityInfo.Create("_", _assetClass == KiwoomAssetClasses.DomesticStock
			? KiwoomMarkets.Krx : KiwoomMarkets.Nasdaq), type, true, cancellationToken);

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

	private async Task ChangeSubscription(KiwoomSecurityInfo security, string type, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		type.ThrowIfEmpty(nameof(type));
		if (security.AssetClass != _assetClass)
			throw new ArgumentException($"{security.AssetClass} cannot be registered on {_assetClass} WebSocket.", nameof(security));
		var subscription = new KiwoomStreamSubscription(_assetClass, security.Code, security.ExchangeCode, type);
		lock (_sync)
		{
			if (isSubscribe)
			{
				if (_subscriptions.ContainsKey(subscription))
					return;
				var symbolCount = _subscriptions.Keys.Where(item => item.Code != "_")
					.Select(item => $"{item.ExchangeCode}:{item.Code}").Distinct(StringComparer.OrdinalIgnoreCase).Count();
				if (security.Code != "_" && symbolCount >= _maxSymbols)
					throw new InvalidOperationException($"Kiwoom WebSocket supports at most {_maxSymbols} symbols per session.");
				_subscriptions.Add(subscription, security);
			}
			else if (!_subscriptions.Remove(subscription))
				return;
		}

		try
		{
			var socket = _socket;
			if (socket?.State == WebSocketState.Open)
				await SendSubscription(socket, subscription, isSubscribe, cancellationToken);
		}
		catch
		{
			if (isSubscribe)
			{
				lock (_sync)
					_subscriptions.Remove(subscription);
			}
			throw;
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
				await socket.ConnectAsync(GetUri(), cancellationToken);
				await Login(socket, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Kiwoom WebSocket closed unexpectedly.");
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
			}
		}
		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		await Invoke(StateChanged, ConnectionStates.Disconnected, CancellationToken.None);
	}

	private async Task Login(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		await Send(socket, new KiwoomStreamLoginRequest
		{
			Token = (await _tokenProvider(cancellationToken)).ThrowIfEmpty("accessToken"),
		}, cancellationToken);
		while (true)
		{
			var content = await ReceiveText(socket, cancellationToken);
			var response = Deserialize(content);
			if (response.TransactionName.EqualsIgnoreCase("PING"))
			{
				await SendText(socket, content, cancellationToken);
				continue;
			}
			if (!response.TransactionName.EqualsIgnoreCase("LOGIN"))
				throw new InvalidDataException($"Unexpected Kiwoom login response '{response.TransactionName}'.");
			if (response.ReturnCode != 0)
				throw new InvalidOperationException($"Kiwoom WebSocket login error {response.ReturnCode}: {response.ReturnMessage}");
			return;
		}
	}

	private async Task RestoreSubscriptions(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		KiwoomStreamSubscription[] subscriptions;
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
			var response = Deserialize(content);
			if (response.TransactionName.EqualsIgnoreCase("PING"))
			{
				await SendText(socket, content, cancellationToken);
				continue;
			}
			if (response.TransactionName.EqualsIgnoreCase("SYSTEM"))
				throw new InvalidOperationException($"Kiwoom WebSocket system message: {response.ReturnMessage}");
			if (response.ReturnCode is not null and not 0)
			{
				await Invoke(Error, new InvalidOperationException(
					$"Kiwoom WebSocket {response.TransactionName} error {response.ReturnCode}: {response.ReturnMessage}"), cancellationToken);
				continue;
			}
			if (!response.TransactionName.EqualsIgnoreCase("REAL"))
				continue;
			foreach (var data in response.Data ?? [])
			{
				if (data.Values != null)
					await Invoke(EventReceived, new KiwoomRealtimeMessage(_assetClass, data), cancellationToken);
			}
		}
	}

	private Task SendSubscription(ClientWebSocket socket, KiwoomStreamSubscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		if (_assetClass == KiwoomAssetClasses.DomesticStock)
		{
			return Send(socket, new KiwoomStreamRequest<string>
			{
				TransactionName = isSubscribe ? "REG" : "REMOVE",
				Refresh = isSubscribe ? "1" : null,
				Data =
				[
					new()
					{
						Items = subscription.Code == "_" ? [] : [subscription.Code],
						Types = [subscription.Type],
					},
				],
			}, cancellationToken);
		}

		return Send(socket, new KiwoomStreamRequest<KiwoomUsStreamItem>
		{
			TransactionName = isSubscribe ? "REG" : "REMOVE",
			Refresh = isSubscribe ? "1" : null,
			Data =
			[
				new()
				{
					Items = subscription.Code == "_" ? [] :
					[
						new() { SecurityCode = subscription.Code, ExchangeType = subscription.ExchangeCode },
					],
					Types = [subscription.Type],
				},
			],
		}, cancellationToken);
	}

	private Task Send<T>(ClientWebSocket socket, T request, CancellationToken cancellationToken)
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

	private KiwoomStreamEnvelope Deserialize(string content)
		=> JsonConvert.DeserializeObject<KiwoomStreamEnvelope>(content, _jsonSettings)
			?? throw new InvalidDataException("Invalid Kiwoom WebSocket response.");

	private static async Task<string> ReceiveText(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException($"Kiwoom WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException($"Unexpected Kiwoom WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Kiwoom WebSocket message exceeds 8 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	private Uri GetUri()
	{
		var host = _isDemo ? "mockapi.kiwoom.com" : "api.kiwoom.com";
		var path = _assetClass == KiwoomAssetClasses.DomesticStock ? "/api/dostk/websocket" : "/api/us/websocket";
		return new($"wss://{host}:10000{path}");
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
