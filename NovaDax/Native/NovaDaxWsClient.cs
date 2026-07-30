namespace StockSharp.NovaDax.Native;

sealed class NovaDaxWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _desiredTopics =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _serverTopics =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _namespaceReady;
	private bool _isNamespaceReady;
	private bool _isRestoring;

	public NovaDaxWsClient(
		string endpoint,
		int engineIoVersion,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = NovaDaxSocketProtocol.CreateEndpoint(
			endpoint, engineIoVersion);
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "NOVADAX_SOCKET_IO";

	public event Func<NovaDaxTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<NovaDaxOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<NovaDaxTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"NovaDAX Socket.IO is already initialized.");

		ResetNamespace(false);
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			Task<bool> readyTask;
			using (_sync.EnterScope())
				readyTask = _namespaceReady.Task;
			if (!await readyTask.WaitAsync(
				TimeSpan.FromSeconds(15), cancellationToken))
				throw new TimeoutException(
					"NovaDAX Socket.IO namespace handshake timed out.");
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
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

	public ValueTask SubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"MARKET.{NormalizeSymbol(symbol)}.TICKER",
			true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"MARKET.{NormalizeSymbol(symbol)}.TICKER",
			false,
			cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		_ = NovaDaxRestClient.NormalizeDepth(depth);
		return ChangeSubscriptionAsync(
			$"MARKET.{NormalizeSymbol(symbol)}.DEPTH.LEVEL0",
			true,
			cancellationToken);
	}

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		_ = NovaDaxRestClient.NormalizeDepth(depth);
		return ChangeSubscriptionAsync(
			$"MARKET.{NormalizeSymbol(symbol)}.DEPTH.LEVEL0",
			false,
			cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"MARKET.{NormalizeSymbol(symbol)}.TRADE",
			true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"MARKET.{NormalizeSymbol(symbol)}.TRADE",
			false,
			cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) =>
				OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) =>
				OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			},
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-NovaDAX-Connector/1.0");
			return default;
		};
		return client;
	}

	private ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
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
				"NovaDAX Socket.IO connection failed."));
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
			_serverTopics.Clear();
			_namespaceReady = new(
				TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	private async ValueTask ChangeSubscriptionAsync(
		string topic,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (!_desiredTopics.Add(topic))
					return;
				send = _isNamespaceReady &&
					_serverTopics.Add(topic);
			}
			else
			{
				if (!_desiredTopics.Remove(topic))
					return;
				send = _isNamespaceReady &&
					_serverTopics.Remove(topic);
			}
		}
		if (!send)
			return;

		try
		{
			await SendEventAsync(
				isSubscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
				new[] { topic },
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverTopics.Remove(topic);
				else
					_serverTopics.Add(topic);
			}
			throw;
		}
	}

	private async ValueTask OnProcessAsync(
		WebSocketClient client,
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			if (payload[0] == '0')
			{
				var handshake = JObject.Parse(payload[1..]);
				if (handshake.Value<string>("sid").IsEmpty())
					throw new InvalidDataException(
						"NovaDAX Engine.IO handshake has no session ID.");
				await SendRawAsync(
					client, "40", cancellationToken);
				return;
			}
			if (payload[0] == '2')
			{
				await SendRawAsync(
					client, "3", cancellationToken);
				return;
			}
			if (payload.StartsWith(
				"40", StringComparison.Ordinal))
			{
				await CompleteNamespaceHandshakeAsync(
					cancellationToken);
				return;
			}
			if (payload.StartsWith(
				"42", StringComparison.Ordinal))
			{
				await ProcessEventAsync(
					payload, cancellationToken);
				return;
			}
			if (payload.StartsWith(
				"44", StringComparison.Ordinal))
				throw new InvalidOperationException(
					"NovaDAX Socket.IO namespace error: " +
						payload[2..]);
		}
		catch (Exception error) when (
			error is JsonException or
			InvalidDataException or
			InvalidOperationException or
			FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask CompleteNamespaceHandshakeAsync(
		CancellationToken cancellationToken)
	{
		string[] topics;
		bool isRestoring;
		TaskCompletionSource<bool> ready;

		using (_sync.EnterScope())
		{
			if (_isNamespaceReady)
				return;
			_isNamespaceReady = true;
			topics = [.. _desiredTopics];
			_serverTopics.UnionWith(topics);
			isRestoring = _isRestoring;
			_isRestoring = false;
			ready = _namespaceReady;
		}

		try
		{
			if (topics.Length > 0)
				await SendEventAsync(
					"SUBSCRIBE", topics, cancellationToken);
			ready.TrySetResult(true);
			if (isRestoring && StateChanged is { } handler)
				await handler(
					ConnectionStates.Restored,
					cancellationToken);
		}
		catch (Exception error)
		{
			using (_sync.EnterScope())
			{
				_isNamespaceReady = false;
				_serverTopics.Clear();
			}
			ready.TrySetException(error);
			throw;
		}
	}

	private async ValueTask ProcessEventAsync(
		string frame,
		CancellationToken cancellationToken)
	{
		if (!NovaDaxSocketProtocol.TryParseEvent(
			frame, out var eventName, out var payload))
			throw new InvalidDataException(
				"NovaDAX Socket.IO returned an invalid event.");

		var symbol = ExtractSymbol(eventName);
		if (eventName.EndsWith(
			".TICKER", StringComparison.OrdinalIgnoreCase))
		{
			var ticker = payload.ToObject<NovaDaxTicker>();
			if (ticker is not null)
			{
				ticker.Pair = ticker.Pair.IsEmpty(symbol);
				if (TickerReceived is { } handler)
					await handler(ticker, cancellationToken);
			}
			return;
		}
		if (eventName.EndsWith(
			".DEPTH.LEVEL0", StringComparison.OrdinalIgnoreCase))
		{
			var book = payload.ToObject<NovaDaxOrderBook>();
			if (book is not null)
			{
				book.Pair = symbol;
				book.Limit = 50;
				if (OrderBookReceived is { } handler)
					await handler(book, cancellationToken);
			}
			return;
		}
		if (eventName.EndsWith(
			".TRADE", StringComparison.OrdinalIgnoreCase))
		{
			var trades = payload.ToObject<NovaDaxTrade[]>() ?? [];

			foreach (var trade in trades)
				trade.Pair = symbol;

			if (trades.Length > 0 && TradesReceived is { } handler)
				await handler(new NovaDaxTradePush
				{
					Pair = symbol,
					EventId = trades[0].Timestamp.ToString(
						CultureInfo.InvariantCulture),
					Data = trades,
				}, cancellationToken);
		}
	}

	private ValueTask SendEventAsync(
		string name,
		object payload,
		CancellationToken cancellationToken)
		=> SendRawAsync(
			_client,
			NovaDaxSocketProtocol.EncodeEvent(name, payload),
			cancellationToken);

	private async ValueTask SendRawAsync(
		WebSocketClient client,
		string payload,
		CancellationToken cancellationToken)
	{
		if (client is null || !client.IsConnected)
			throw new InvalidOperationException(
				"NovaDAX Socket.IO is not connected.");
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

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static string ExtractSymbol(string eventName)
	{
		var parts = eventName?.Split('.');
		if (parts is not { Length: >= 3 } ||
			!parts[0].EqualsIgnoreCase("MARKET"))
			throw new InvalidDataException(
				$"Invalid NovaDAX market topic '{eventName}'.");
		return parts[1].ToNovaDaxSymbol();
	}

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).ToNovaDaxSymbol();
}
