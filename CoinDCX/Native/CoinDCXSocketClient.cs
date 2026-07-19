namespace StockSharp.CoinDCX.Native;

sealed class CoinDCXSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly CoinDCXRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _desiredChannels =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _serverChannels =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _namespaceReady;
	private bool _isNamespaceReady;
	private bool _isRestoring;

	public CoinDCXSocketClient(string endpoint, CoinDCXRestClient restClient,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = CreateEndpoint(endpoint);
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinDCX_Socket";

	public event Func<CoinDCXWebSocketTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<CoinDCXWebSocketDepth, bool, CancellationToken, ValueTask>
		DepthReceived;
	public event Func<CoinDCXWebSocketCandle, CancellationToken, ValueTask>
		CandleReceived;
	public event Func<CoinDCXPrivateBalance[], CancellationToken, ValueTask>
		BalancesReceived;
	public event Func<CoinDCXPrivateOrder[], CancellationToken, ValueTask>
		OrdersReceived;
	public event Func<CoinDCXPrivateTrade[], CancellationToken, ValueTask>
		AccountTradesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"CoinDCX WebSocket is already initialized.");
		ResetNamespace(false);
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			Task<bool> readyTask;
			using (_sync.EnterScope())
				readyTask = _namespaceReady.Task;
			if (!await readyTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken))
				throw new TimeoutException(
					"CoinDCX Socket.IO namespace handshake timed out.");
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTradesAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{pair.ThrowIfEmpty(nameof(pair))}@trades", true, cancellationToken);

	public ValueTask ReleaseTradesAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{pair.ThrowIfEmpty(nameof(pair))}@trades", false, cancellationToken);

	public ValueTask SubscribeDepthAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{pair.ThrowIfEmpty(nameof(pair))}@orderbook@50", true,
			cancellationToken);

	public ValueTask ReleaseDepthAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{pair.ThrowIfEmpty(nameof(pair))}@orderbook@50", false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string pair, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{pair.ThrowIfEmpty(nameof(pair))}_{interval.ThrowIfEmpty(nameof(interval))}",
			true, cancellationToken);

	public ValueTask ReleaseCandlesAsync(string pair, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{pair.ThrowIfEmpty(nameof(pair))}_{interval.ThrowIfEmpty(nameof(interval))}",
			false, cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-CoinDCX-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
			ResetNamespace(false);
		}
	}

	private ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		_ = client;
		if (state == ConnectionStates.Restored)
		{
			ResetNamespace(true);
			return default;
		}
		if (state == ConnectionStates.Connected)
		{
			ResetNamespace(false);
			return default;
		}
		if (state == ConnectionStates.Failed)
		{
			TaskCompletionSource<bool> ready;
			using (_sync.EnterScope())
				ready = _namespaceReady;
			ready?.TrySetException(new InvalidOperationException(
				"CoinDCX WebSocket connection failed."));
		}
		return StateChanged is { } handler
			? handler(state, cancellationToken)
			: default;
	}

	private void ResetNamespace(bool isRestoring)
	{
		using (_sync.EnterScope())
		{
			_isNamespaceReady = false;
			_isRestoring = isRestoring;
			_serverChannels.Clear();
			_namespaceReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	private async ValueTask ChangeSubscriptionAsync(string channel,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (!_desiredChannels.Add(channel))
					return;
				send = _isNamespaceReady && _serverChannels.Add(channel);
			}
			else
			{
				if (!_desiredChannels.Remove(channel))
					return;
				send = _isNamespaceReady && _serverChannels.Remove(channel);
			}
		}
		if (!send)
			return;
		try
		{
			await SendChannelCommandAsync(isSubscribe
				? CoinDCXSocketCommands.Join
				: CoinDCXSocketCommands.Leave, channel, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverChannels.Remove(channel);
				else
					_serverChannels.Add(channel);
			}
			throw;
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			if (payload[0] == '0')
			{
				var handshake = Deserialize<CoinDCXEngineHandshake>(payload[1..]);
				if (handshake.SessionId.IsEmpty())
					throw new InvalidDataException(
						"CoinDCX Engine.IO handshake has no session identifier.");
				await SendRawAsync(client, "40", cancellationToken);
				return;
			}
			if (payload[0] == '2')
			{
				await SendRawAsync(client, "3", cancellationToken);
				return;
			}
			if (payload.StartsWith("40", StringComparison.Ordinal))
			{
				await CompleteNamespaceHandshakeAsync(client, cancellationToken);
				return;
			}
			if (payload.StartsWith("42", StringComparison.Ordinal))
			{
				await ProcessEventAsync(payload[2..], cancellationToken);
				return;
			}
			if (payload.StartsWith("44", StringComparison.Ordinal))
				throw new InvalidOperationException(
					$"CoinDCX Socket.IO namespace error: {payload[2..]}");
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask CompleteNamespaceHandshakeAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		string[] channels;
		bool isRestoring;
		TaskCompletionSource<bool> ready;
		using (_sync.EnterScope())
		{
			if (_isNamespaceReady)
				return;
			_isNamespaceReady = true;
			channels = [.. _desiredChannels];
			_serverChannels.UnionWith(channels);
			isRestoring = _isRestoring;
			_isRestoring = false;
			ready = _namespaceReady;
		}

		try
		{
			if (_restClient.IsCredentialsAvailable)
				await SendPrivateJoinAsync(client, cancellationToken);
			foreach (var channel in channels)
				await SendChannelCommandAsync(CoinDCXSocketCommands.Join, channel,
					cancellationToken);
			ready.TrySetResult(true);
			if (isRestoring && StateChanged is { } handler)
				await handler(ConnectionStates.Restored, cancellationToken);
		}
		catch (Exception error)
		{
			using (_sync.EnterScope())
			{
				_isNamespaceReady = false;
				_serverChannels.Clear();
			}
			ready.TrySetException(error);
			throw;
		}
	}

	private async ValueTask ProcessEventAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<CoinDCXSocketEnvelope>(payload);
		if (envelope.Payload?.Data.IsEmpty() != false)
			return;
		switch (envelope.Event)
		{
			case CoinDCXSocketEvents.NewTrade:
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(Deserialize<CoinDCXWebSocketTrade>(
						envelope.Payload.Data), cancellationToken);
				break;
			case CoinDCXSocketEvents.DepthSnapshot:
			case CoinDCXSocketEvents.DepthUpdate:
				if (DepthReceived is { } depthHandler)
					await depthHandler(Deserialize<CoinDCXWebSocketDepth>(
						envelope.Payload.Data),
						envelope.Event == CoinDCXSocketEvents.DepthSnapshot,
						cancellationToken);
				break;
			case CoinDCXSocketEvents.Candlestick:
				if (CandleReceived is { } candleHandler)
					await candleHandler(Deserialize<CoinDCXWebSocketCandle>(
						envelope.Payload.Data), cancellationToken);
				break;
			case CoinDCXSocketEvents.BalanceUpdate:
				if (BalancesReceived is { } balanceHandler)
					await balanceHandler(Deserialize<CoinDCXPrivateBalance[]>(
						envelope.Payload.Data), cancellationToken);
				break;
			case CoinDCXSocketEvents.OrderUpdate:
				if (OrdersReceived is { } orderHandler)
					await orderHandler(Deserialize<CoinDCXPrivateOrder[]>(
						envelope.Payload.Data), cancellationToken);
				break;
			case CoinDCXSocketEvents.TradeUpdate:
				if (AccountTradesReceived is { } accountTradeHandler)
					await accountTradeHandler(Deserialize<CoinDCXPrivateTrade[]>(
						envelope.Payload.Data), cancellationToken);
				break;
		}
	}

	private ValueTask SendChannelCommandAsync(CoinDCXSocketCommands command,
		string channel, CancellationToken cancellationToken)
		=> SendSocketPacketAsync(_client, new CoinDCXChannelCommand
		{
			Command = command,
			Data = new() { ChannelName = channel },
		}, cancellationToken);

	private ValueTask SendPrivateJoinAsync(WebSocketClient client,
		CancellationToken cancellationToken)
		=> SendSocketPacketAsync(client, new CoinDCXPrivateChannelCommand
		{
			Data = new()
			{
				ChannelName = "coindcx",
				AuthenticationSignature = _restClient.CreateSocketSignature(),
				ApiKey = _restClient.ApiKey,
			},
		}, cancellationToken);

	private ValueTask SendSocketPacketAsync<TPacket>(WebSocketClient client,
		TPacket packet, CancellationToken cancellationToken)
		=> SendRawAsync(client, "42" + JsonConvert.SerializeObject(packet,
			_jsonSettings), cancellationToken);

	private async ValueTask SendRawAsync(WebSocketClient client, string payload,
		CancellationToken cancellationToken)
	{
		if (client is null || !client.IsConnected)
			throw new InvalidOperationException(
				"CoinDCX WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"CoinDCX Socket.IO returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinDCX Socket.IO returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string CreateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint.ThrowIfEmpty(nameof(endpoint)).Trim(),
			UriKind.Absolute, out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"CoinDCX WebSocket endpoint must be an absolute WSS URI.",
				nameof(endpoint));
		var builder = new UriBuilder(uri)
		{
			Path = "/socket.io/",
			Query = "EIO=4&transport=websocket",
		};
		return builder.Uri.AbsoluteUri;
	}
}
