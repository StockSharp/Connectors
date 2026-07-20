namespace StockSharp.QFEX.Native;

enum QFEXPendingResponseKinds
{
	Order,
	Orders,
	Trades,
	Acknowledgement,
}

sealed class QFEXPendingRequest
{
	public QFEXPendingResponseKinds Kind { get; init; }
	public string MatchId { get; init; }
	public TaskCompletionSource<QFEXTradeMessage> Completion { get; init; }
}

sealed class QFEXTradeWebSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly QFEXAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<QFEXTradeChannels> _subscriptions = [];
	private readonly List<QFEXPendingRequest> _pending = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly SemaphoreSlim _querySync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authenticationPending;
	private bool _isAuthenticated;

	public QFEXTradeWebSocketClient(string endpoint, string publicKey,
		SecureString secret, string accountId, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_authenticator = new(publicKey, secret, accountId);
		if (!_authenticator.IsAvailable)
			throw new ArgumentException(
				"QFEX API key and secret are required for the trade WebSocket.");
		_endpoint = AddApiKey(endpoint, publicKey);
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "QFEX_TRADE_WS";

	public bool IsAuthenticated
	{
		get
		{
			using (_sync.EnterScope())
				return _isAuthenticated;
		}
	}

	public event Func<QFEXOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<QFEXFill, CancellationToken, ValueTask> FillReceived;
	public event Func<QFEXBalance, CancellationToken, ValueTask> BalanceReceived;
	public event Func<QFEXPosition, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"QFEX trade WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await AuthenticateAsync(client, cancellationToken);
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
		FailAuthentication(new InvalidOperationException(
			"QFEX trade WebSocket disconnected."));
		FailPending(new InvalidOperationException(
			"QFEX trade WebSocket disconnected."));
		using (_sync.EnterScope())
			_isAuthenticated = false;
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
			using (_sync.EnterScope())
				_subscriptions.Clear();
		}
	}

	public async ValueTask SubscribeAsync(QFEXTradeChannels channel,
		CancellationToken cancellationToken)
	{
		bool added;
		using (_sync.EnterScope())
			added = _subscriptions.Add(channel);
		if (!added)
			return;
		try
		{
			EnsureReady();
			await SendAsync(_client, CreateSubscription(channel, true),
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(channel);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(QFEXTradeChannels channel,
		CancellationToken cancellationToken)
	{
		bool removed;
		using (_sync.EnterScope())
			removed = _subscriptions.Remove(channel);
		if (!removed || _client?.IsConnected != true)
			return;
		try
		{
			await SendAsync(_client, CreateSubscription(channel, false),
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Add(channel);
			throw;
		}
	}

	public async ValueTask<QFEXOrder> PlaceOrderAsync(
		QFEXAddOrderParameters parameters, CancellationToken cancellationToken)
	{
		parameters = parameters ?? throw new ArgumentNullException(
			nameof(parameters));
		var response = await SendRequestAsync(new QFEXTradeRequest<
			QFEXAddOrderParameters>
		{
			Type = QFEXTradeRequestTypes.AddOrder,
			Parameters = parameters,
		}, QFEXPendingResponseKinds.Order, parameters.ClientOrderId,
			cancellationToken);
		return response.OrderResponse ?? throw new InvalidDataException(
			"QFEX returned no order after add_order.");
	}

	public async ValueTask<QFEXOrder> CancelOrderAsync(
		QFEXCancelOrderParameters parameters,
		CancellationToken cancellationToken)
	{
		parameters = parameters ?? throw new ArgumentNullException(
			nameof(parameters));
		var response = await SendRequestAsync(new QFEXTradeRequest<
			QFEXCancelOrderParameters>
		{
			Type = QFEXTradeRequestTypes.CancelOrder,
			Parameters = parameters,
		}, QFEXPendingResponseKinds.Order,
			parameters.OrderIdType == QFEXCancelOrderIdTypes.OrderId
				? parameters.OrderId
				: string.Empty, cancellationToken);
		return response.OrderResponse ?? throw new InvalidDataException(
			"QFEX returned no order after cancel_order.");
	}

	public async ValueTask<QFEXOrder> ModifyOrderAsync(
		QFEXModifyOrderParameters parameters,
		CancellationToken cancellationToken)
	{
		parameters = parameters ?? throw new ArgumentNullException(
			nameof(parameters));
		var response = await SendRequestAsync(new QFEXTradeRequest<
			QFEXModifyOrderParameters>
		{
			Type = QFEXTradeRequestTypes.ModifyOrder,
			Parameters = parameters,
		}, QFEXPendingResponseKinds.Order, parameters.OrderId,
			cancellationToken);
		return response.OrderResponse ?? throw new InvalidDataException(
			"QFEX returned no order after modify_order.");
	}

	public async ValueTask CancelAllOrdersAsync(
		CancellationToken cancellationToken)
	{
		await _querySync.WaitAsync(cancellationToken);
		try
		{
			await SendRequestAsync(new QFEXTradeRequest<
				QFEXCancelAllOrdersParameters>
			{
				Type = QFEXTradeRequestTypes.CancelAllOrders,
				Parameters = new(),
			}, QFEXPendingResponseKinds.Acknowledgement, null,
				cancellationToken);
		}
		finally
		{
			_querySync.Release();
		}
	}

	public async ValueTask<QFEXOrder[]> GetOpenOrdersAsync(string symbol,
		int limit, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		await _querySync.WaitAsync(cancellationToken);
		try
		{
			var response = await SendRequestAsync(new QFEXTradeRequest<
				QFEXGetUserOrdersParameters>
			{
				Type = QFEXTradeRequestTypes.GetUserOrders,
				Parameters = new()
				{
					Limit = limit,
					Offset = 0,
					Symbol = symbol,
				},
			}, QFEXPendingResponseKinds.Orders, null, cancellationToken);
			return response.AllOrdersResponse?.Orders ?? [];
		}
		finally
		{
			_querySync.Release();
		}
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, token),
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
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent", "StockSharp-QFEX-Connector/1.0");
			var credentials = _authenticator.CreateCredentials();
			socket.Options.SetRequestHeader("x-qfex-public-key",
				credentials.PublicKey);
			socket.Options.SetRequestHeader("x-qfex-hmac-signature",
				credentials.Signature);
			socket.Options.SetRequestHeader("x-qfex-nonce",
				credentials.Nonce);
			socket.Options.SetRequestHeader("x-qfex-timestamp",
				credentials.UnixTimestamp.ToString(CultureInfo.InvariantCulture));
			if (!_authenticator.AccountId.IsEmpty())
				socket.Options.SetRequestHeader(
					"x-qfex-requested-account-id", _authenticator.AccountId);
		};
		return client;
	}

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var pending = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
		{
			if (_authenticationPending is not null)
				throw new InvalidOperationException(
					"QFEX authentication is already pending.");
			_authenticationPending = pending;
			_isAuthenticated = false;
		}
		try
		{
			await SendAsync(client, new QFEXTradeRequest<
				QFEXAuthenticationParameters>
			{
				Type = QFEXTradeRequestTypes.Authenticate,
				Parameters = new()
				{
					Hmac = _authenticator.CreateCredentials(),
					AccountId = _authenticator.AccountId,
				},
			}, cancellationToken);
			await pending.Task.WaitAsync(TimeSpan.FromSeconds(15),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				if (ReferenceEquals(_authenticationPending, pending))
					_authenticationPending = null;
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			await AuthenticateAsync(client, cancellationToken);
			QFEXTradeChannels[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendAsync(client, CreateSubscription(subscription, true),
					cancellationToken);
		}
		else if (state is ConnectionStates.Failed or
			ConnectionStates.Disconnected)
		{
			var error = new InvalidOperationException(
				$"QFEX trade WebSocket entered '{state}' state.");
			using (_sync.EnterScope())
				_isAuthenticated = false;
			FailAuthentication(error);
			FailPending(error);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask<QFEXTradeMessage> SendRequestAsync<TParameters>(
		QFEXTradeRequest<TParameters> request, QFEXPendingResponseKinds kind,
		string matchId, CancellationToken cancellationToken)
	{
		EnsureReady();
		var pending = new QFEXPendingRequest
		{
			Kind = kind,
			MatchId = matchId,
			Completion = new(TaskCreationOptions.RunContinuationsAsynchronously),
		};
		using (_sync.EnterScope())
			_pending.Add(pending);
		try
		{
			await SendAsync(_client, request, cancellationToken);
			return await pending.Completion.Task.WaitAsync(
				TimeSpan.FromSeconds(15), cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(pending);
		}
	}

	private static QFEXTradeRequest<QFEXTradeSubscriptionParameters>
		CreateSubscription(QFEXTradeChannels channel, bool isSubscribe)
		=> new()
		{
			Type = isSubscribe
				? QFEXTradeRequestTypes.Subscribe
				: QFEXTradeRequestTypes.Unsubscribe,
			Parameters = new() { Channels = [channel] },
		};

	private async ValueTask SendAsync<T>(WebSocketClient client, T message,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(message, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var first = payload.FirstOrDefault(static character =>
				!char.IsWhiteSpace(character));
			var messages = first == '['
				? Deserialize<QFEXTradeMessage[]>(payload)
				: [Deserialize<QFEXTradeMessage>(payload)];
			foreach (var item in messages.Where(static item => item is not null))
				await ProcessAsync(item, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessAsync(QFEXTradeMessage message,
		CancellationToken cancellationToken)
	{
		if (message.IsAuthenticated.HasValue ||
			message.Type == QFEXTradeEnvelopeTypes.Authenticate)
		{
			var succeeded = message.IsAuthenticated == true ||
				message.Result.EqualsIgnoreCase("success");
			if (!succeeded)
			{
				FailAuthentication(new InvalidOperationException(
					"QFEX trade WebSocket authentication failed."));
				return;
			}
			using (_sync.EnterScope())
				_isAuthenticated = true;
			AcknowledgeAuthentication();
			return;
		}

		var error = message.Error ?? (message.ErrorCode is QFEXErrorCodes code
			? new QFEXError { ErrorCode = code, Message = message.ErrorMessage }
			: null);
		if (error is not null || message.Type == QFEXTradeEnvelopeTypes.Error)
		{
			var exception = error is null
				? new InvalidOperationException(
					"QFEX trade WebSocket returned an error.")
				: CreateError(error);
			FailFirstPending(exception);
			await RaiseErrorAsync(exception, cancellationToken);
			return;
		}

		if (message.OrderResponse is { } order)
		{
			var isRequestResponse = CompletePending(
				QFEXPendingResponseKinds.Order, message,
				order.ClientOrderId, order.OrderId);
			if (!isRequestResponse && OrderReceived is { } handler)
				await handler(order, cancellationToken);
			return;
		}

		if (message.FillResponse is { } fill)
		{
			if (FillReceived is { } handler)
				await handler(fill, cancellationToken);
			return;
		}

		if (message.AllOrdersResponse is not null)
		{
			CompletePending(QFEXPendingResponseKinds.Orders, message);
			return;
		}

		if (message.UserTradesResponse is not null)
		{
			CompletePending(QFEXPendingResponseKinds.Trades, message);
			return;
		}

		if (message.Acknowledgement is not null)
		{
			CompletePending(QFEXPendingResponseKinds.Acknowledgement, message);
			return;
		}

		if (message.BalanceResponse is { } balance)
		{
			if (BalanceReceived is { } handler)
				await handler(balance, cancellationToken);
			return;
		}

		if (message.PositionResponse is { } position)
		{
			if (PositionReceived is { } handler)
				await handler(position, cancellationToken);
			return;
		}

		if (message.Contents is not { Length: > 0 })
			return;
		if (message.Channel == QFEXTradeStreamChannels.Balances ||
			message.Type == QFEXTradeEnvelopeTypes.BalanceUpdate ||
			message.Type == QFEXTradeEnvelopeTypes.Balances)
		{
			if (BalanceReceived is { } handler)
				foreach (var item in message.Contents)
					await handler(item.ToBalance(), cancellationToken);
		}
		else if (message.Channel == QFEXTradeStreamChannels.Positions ||
			message.Type == QFEXTradeEnvelopeTypes.PositionUpdate ||
			message.Type == QFEXTradeEnvelopeTypes.Positions)
		{
			if (PositionReceived is { } handler)
				foreach (var item in message.Contents)
					await handler(item.ToPosition(), cancellationToken);
		}
	}

	private bool CompletePending(QFEXPendingResponseKinds kind,
		QFEXTradeMessage message, params string[] identifiers)
	{
		QFEXPendingRequest pending = null;
		using (_sync.EnterScope())
		{
			pending = _pending.FirstOrDefault(item => item.Kind == kind &&
				(item.MatchId.IsEmpty() || identifiers.Any(identifier =>
					!identifier.IsEmpty() && identifier.Equals(item.MatchId,
						StringComparison.OrdinalIgnoreCase))));
			if (pending is not null)
				_pending.Remove(pending);
		}
		if (pending is null)
			return false;
		pending.Completion.TrySetResult(message);
		return true;
	}

	private void FailFirstPending(Exception error)
	{
		QFEXPendingRequest pending = null;
		using (_sync.EnterScope())
		{
			if (_pending.Count > 0)
			{
				pending = _pending[0];
				_pending.RemoveAt(0);
			}
		}
		pending?.Completion.TrySetException(error);
	}

	private void FailPending(Exception error)
	{
		QFEXPendingRequest[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending];
			_pending.Clear();
		}
		foreach (var request in pending)
			request.Completion.TrySetException(error);
	}

	private void AcknowledgeAuthentication()
	{
		TaskCompletionSource<bool> pending;
		using (_sync.EnterScope())
			pending = _authenticationPending;
		pending?.TrySetResult(true);
	}

	private void FailAuthentication(Exception error)
	{
		TaskCompletionSource<bool> pending;
		using (_sync.EnterScope())
			pending = _authenticationPending;
		pending?.TrySetException(error);
	}

	private void EnsureReady()
	{
		if (_client?.IsConnected != true || !IsAuthenticated)
			throw new InvalidOperationException(
				"QFEX trade WebSocket is not authenticated.");
	}

	private T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"QFEX trade WebSocket returned an empty message.");

	private static Exception CreateError(QFEXError error)
		=> new InvalidOperationException(
			$"QFEX WebSocket error {error.ErrorCode}: " +
			(error.Message.IsEmpty() ? "No details." : error.Message));

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static string AddApiKey(string endpoint, string publicKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "wss://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"QFEX trade endpoint must use WS or WSS.", nameof(endpoint));
		var builder = new UriBuilder(uri);
		var query = builder.Query.TrimStart('?');
		builder.Query = (query.IsEmpty() ? string.Empty : query + "&") +
			"api_key=" + Uri.EscapeDataString(
				publicKey.ThrowIfEmpty(nameof(publicKey)).Trim());
		return builder.Uri.AbsoluteUri;
	}

	protected override void DisposeManaged()
	{
		FailAuthentication(new ObjectDisposedException(
			nameof(QFEXTradeWebSocketClient)));
		FailPending(new ObjectDisposedException(
			nameof(QFEXTradeWebSocketClient)));
		_client?.Dispose();
		_querySync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
