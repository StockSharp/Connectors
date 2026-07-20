namespace StockSharp.OrderlyNetwork.Native;

sealed class OrderlyNetworkSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _accountId;
	private readonly OrderlyNetworkSigner _signer;
	private readonly Func<DateTime> _serverTimeProvider;
	private readonly bool _isPrivate;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string,
		TaskCompletionSource<OrderlyNetworkSocketHeader>> _pending =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _requestId;

	public OrderlyNetworkSocketClient(string endpoint, string accountId,
		OrderlyNetworkSigner signer, Func<DateTime> serverTimeProvider,
		bool isPrivate, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		_accountId = accountId.ThrowIfEmpty(nameof(accountId)).Trim();
		_signer = signer ?? throw new ArgumentNullException(nameof(signer));
		_serverTimeProvider = serverTimeProvider ?? throw new ArgumentNullException(
			nameof(serverTimeProvider));
		_isPrivate = isPrivate;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
		if (_isPrivate && !_signer.IsSigningAvailable)
			throw new ArgumentException(
				"A signing key is required for the private Orderly WebSocket.",
				nameof(signer));
	}

	public override string Name => _isPrivate
		? "OrderlyNetwork_PrivateWS"
		: "OrderlyNetwork_PublicWS";

	public event Func<OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketBbo>,
		CancellationToken, ValueTask> BboReceived;
	public event Func<OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketTicker>,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketTrade>,
		CancellationToken, ValueTask> TradeReceived;
	public event Func<OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketCandle>,
		CancellationToken, ValueTask> CandleReceived;
	public event Func<OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketDepth>,
		CancellationToken, ValueTask> DepthReceived;
	public event Func<OrderlyNetworkSocketEnvelope<
		OrderlyNetworkSocketBalancesData>, CancellationToken, ValueTask>
		BalancesReceived;
	public event Func<OrderlyNetworkSocketEnvelope<
		OrderlyNetworkSocketPositionsData>, CancellationToken, ValueTask>
		PositionsReceived;
	public event Func<OrderlyNetworkSocketEnvelope<
		OrderlyNetworkExecutionReport>, CancellationToken, ValueTask>
		ExecutionReceived;
	public event Func<long, CancellationToken, ValueTask> ServerTimeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Orderly WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			if (_isPrivate)
				await AuthenticateAsync(client, true, cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeAsync(string topic,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(topic, true, cancellationToken);

	public ValueTask UnsubscribeAsync(string topic,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(topic, false, cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			return;
		await SendAsync(client, new OrderlyNetworkSocketEvent
		{
			Event = "ping",
		}, cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint + "/" + Uri.EscapeDataString(_accountId),
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-OrderlyNetwork-Connector/1.0");
			socket.Options.DangerousDeflateOptions = new();
		};
		return client;
	}

	private async ValueTask ChangeSubscriptionAsync(string topic,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		topic = topic.ThrowIfEmpty(nameof(topic)).Trim();
		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe
				? _subscriptions.Add(topic)
				: _subscriptions.Remove(topic);
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, topic, isSubscribe, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				if (isSubscribe)
					_subscriptions.Remove(topic);
				else
					_subscriptions.Add(topic);
			throw;
		}
	}

	private async ValueTask SendSubscriptionAsync(WebSocketClient client,
		string topic, bool isSubscribe, bool isWaitResponse,
		CancellationToken cancellationToken)
	{
		var id = NextId();
		TaskCompletionSource<OrderlyNetworkSocketHeader> pending = null;
		if (isWaitResponse)
			pending = CreatePending(id);
		try
		{
			await SendAsync(client, new OrderlyNetworkSocketCommand
			{
				Id = id,
				Topic = topic,
				Event = isSubscribe ? "subscribe" : "unsubscribe",
			}, cancellationToken);
			if (pending is not null)
				await pending.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		finally
		{
			if (pending is not null)
				RemovePending(id);
		}
	}

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		bool isWaitResponse, CancellationToken cancellationToken)
	{
		var id = NextId();
		TaskCompletionSource<OrderlyNetworkSocketHeader> pending = null;
		if (isWaitResponse)
			pending = CreatePending(id);
		try
		{
			var signature = _signer.SignTimestamp(_serverTimeProvider());
			await SendAsync(client, new OrderlyNetworkSocketAuthCommand
			{
				Id = id,
				Parameters = new()
				{
					PublicKey = signature.PublicKey,
					Signature = signature.Value,
					Timestamp = signature.Timestamp,
				},
			}, cancellationToken);
			if (pending is not null)
				await pending.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		finally
		{
			if (pending is not null)
				RemovePending(id);
		}
	}

	private string NextId()
		=> Interlocked.Increment(ref _requestId).ToString(
			CultureInfo.InvariantCulture);

	private TaskCompletionSource<OrderlyNetworkSocketHeader> CreatePending(
		string id)
	{
		var pending = new TaskCompletionSource<OrderlyNetworkSocketHeader>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, pending);
		return pending;
	}

	private void RemovePending(string id)
	{
		using (_sync.EnterScope())
			_pending.Remove(id);
	}

	private async ValueTask SendAsync<TPayload>(WebSocketClient client,
		TPayload payload, CancellationToken cancellationToken)
	{
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

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<OrderlyNetworkSocketHeader>(payload);
			if (header.Event.EqualsIgnoreCase("ping"))
			{
				await SendAsync(client, new OrderlyNetworkSocketEvent
				{
					Event = "pong",
				}, cancellationToken);
				if (header.Timestamp > 0)
					await RaiseAsync(ServerTimeReceived, header.Timestamp,
						cancellationToken);
				return;
			}
			if (header.Event.EqualsIgnoreCase("pong"))
			{
				if (header.Timestamp > 0)
					await RaiseAsync(ServerTimeReceived, header.Timestamp,
						cancellationToken);
				return;
			}
			if (!header.Id.IsEmpty() && header.IsSuccess is not null)
			{
				if (!CompletePending(header) && header.IsSuccess != true)
					throw new InvalidOperationException(
						"Orderly WebSocket request failed: " +
						(header.Message.IsEmpty()
							? "unknown error"
							: header.Message));
				if (header.Timestamp > 0)
					await RaiseAsync(ServerTimeReceived, header.Timestamp,
						cancellationToken);
				return;
			}
			if (header.Topic.IsEmpty())
				throw new InvalidDataException(
					"Orderly WebSocket message has no topic.");

			if (header.Topic.EndsWith("@bbo", StringComparison.Ordinal))
				await RaiseAsync(BboReceived, Deserialize<OrderlyNetworkSocketEnvelope<
					OrderlyNetworkSocketBbo>>(payload), cancellationToken);
			else if (header.Topic.EndsWith("@ticker", StringComparison.Ordinal))
				await RaiseAsync(TickerReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketTicker>>(payload),
					cancellationToken);
			else if (header.Topic.EndsWith("@trade", StringComparison.Ordinal))
				await RaiseAsync(TradeReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketTrade>>(payload),
					cancellationToken);
			else if (header.Topic.Contains("@kline_", StringComparison.Ordinal))
				await RaiseAsync(CandleReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketCandle>>(payload),
					cancellationToken);
			else if (header.Topic.EndsWith("@orderbookupdate",
				StringComparison.Ordinal))
				await RaiseAsync(DepthReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketDepth>>(payload),
					cancellationToken);
			else if (header.Topic.Equals("balance", StringComparison.Ordinal))
				await RaiseAsync(BalancesReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketBalancesData>>(
						payload), cancellationToken);
			else if (header.Topic.Equals("position", StringComparison.Ordinal))
				await RaiseAsync(PositionsReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketPositionsData>>(
						payload), cancellationToken);
			else if (header.Topic.Equals("executionreport",
				StringComparison.Ordinal))
				await RaiseAsync(ExecutionReceived, Deserialize<
					OrderlyNetworkSocketEnvelope<OrderlyNetworkExecutionReport>>(
						payload), cancellationToken);
			else
				this.AddWarningLog("Unknown Orderly WebSocket topic '{0}'.",
					header.Topic);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process an Orderly WebSocket message.", error),
				cancellationToken);
		}
	}

	private bool CompletePending(OrderlyNetworkSocketHeader response)
	{
		TaskCompletionSource<OrderlyNetworkSocketHeader> pending;
		using (_sync.EnterScope())
			_pending.TryGetValue(response.Id, out pending);
		if (pending is null)
			return false;
		if (response.IsSuccess != true)
			pending.TrySetException(new InvalidOperationException(
				"Orderly WebSocket request failed: " +
				(response.Message.IsEmpty() ? "unknown error" : response.Message)));
		else
			pending.TrySetResult(response);
		return true;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (_isPrivate)
				await AuthenticateAsync(client, false, cancellationToken);
			string[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var topic in subscriptions)
				await SendSubscriptionAsync(client, topic, true, false,
					cancellationToken);
		}
		else if (state == ConnectionStates.Failed)
			FailPending(new InvalidOperationException(
				"Orderly WebSocket connection failed."));
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		FailPending(new InvalidOperationException(
			"Orderly WebSocket was disconnected."));
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
		}
	}

	private void FailPending(Exception error)
	{
		TaskCompletionSource<OrderlyNetworkSocketHeader>[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var completion in pending)
			completion.TrySetException(error);
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"Orderly returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Orderly returned malformed WebSocket JSON.", error);
		}
	}

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static ValueTask RaiseAsync<T>(
		Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler is null ? default : handler(value, cancellationToken);

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
