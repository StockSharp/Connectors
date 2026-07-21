namespace StockSharp.BitGo.Native;

sealed class BitGoSocketClient : BaseLogReceiver
{
	private sealed class PendingSubscription
	{
		public BitGoSocketActions Action { get; init; }
		public TaskCompletionSource<BitGoSubscriptionResponse> Completion
			{ get; init; }
	}

	private readonly Uri _endpoint;
	private readonly string _accessToken;
	private readonly string _accountId;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly Dictionary<string, BitGoSocketRequest> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PendingSubscription> _pending =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters =
		{
			new BitGoTimeInForceConverter(),
			new StringEnumConverter(),
		},
	};
	private WebSocketClient _client;
	private bool _isDisposed;

	public BitGoSocketClient(string endpoint, SecureString accessToken,
		string accountId, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken)).UnSecure()
			.Trim();
		if (_accessToken.IsEmpty())
			throw new ArgumentException("BitGo access token is empty.",
				nameof(accessToken));
		_accountId = accountId.ThrowIfEmpty(nameof(accountId)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "BitGo_WS";

	public event Func<BitGoBookMessage, CancellationToken, ValueTask>
		BookReceived;
	public event Func<BitGoOrderUpdate, CancellationToken, ValueTask>
		OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask EnsureConnectedAsync(
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _connectSync.WaitAsync(cancellationToken);
		try
		{
			EnsureClient();
			if (!_client.IsConnected)
				await _client.ConnectAsync(cancellationToken);
		}
		finally
		{
			_connectSync.Release();
		}
	}

	public ValueTask SubscribeOrdersAsync(CancellationToken cancellationToken)
		=> SubscribeAsync(BitGoSocketChannels.Orders, null, cancellationToken);

	public ValueTask SubscribeBookAsync(string productId,
		CancellationToken cancellationToken)
		=> SubscribeAsync(BitGoSocketChannels.Level2,
			productId.ThrowIfEmpty(nameof(productId)), cancellationToken);

	public ValueTask UnsubscribeBookAsync(string productId,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(BitGoSocketChannels.Level2,
			productId.ThrowIfEmpty(nameof(productId)), cancellationToken);

	private async ValueTask SubscribeAsync(BitGoSocketChannels channel,
		string productId, CancellationToken cancellationToken)
	{
		await EnsureConnectedAsync(cancellationToken);
		var key = GetKey(channel, productId);
		var request = new BitGoSocketRequest
		{
			Type = BitGoSocketActions.Subscribe,
			Channel = channel,
			AccountId = _accountId,
			ProductId = productId,
			IsIncludeCumulative = channel == BitGoSocketChannels.Level2
				? false
				: null,
		};
		using (_sync.EnterScope())
		{
			if (_subscriptions.ContainsKey(key))
				return;
			_subscriptions.Add(key, request);
		}
		try
		{
			await SendAndWaitAsync(key, request, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(key);
			throw;
		}
	}

	private async ValueTask UnsubscribeAsync(BitGoSocketChannels channel,
		string productId, CancellationToken cancellationToken)
	{
		var key = GetKey(channel, productId);
		BitGoSocketRequest subscribed;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.Remove(key, out subscribed))
				return;
		}
		var request = new BitGoSocketRequest
		{
			Type = BitGoSocketActions.Unsubscribe,
			Channel = channel,
			AccountId = _accountId,
			ProductId = productId,
			IsIncludeCumulative = subscribed.IsIncludeCumulative,
		};
		try
		{
			await SendAndWaitAsync(key, request, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions[key] = subscribed;
			throw;
		}
	}

	private async ValueTask SendAndWaitAsync(string key,
		BitGoSocketRequest request, CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<BitGoSubscriptionResponse>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
		{
			if (_pending.ContainsKey(key))
				throw new InvalidOperationException(
					"A BitGo subscription operation is already pending for " + key +
					".");
			_pending.Add(key, new()
			{
				Action = request.Type,
				Completion = completion,
			});
		}
		try
		{
			await SendAsync(request, cancellationToken);
			await completion.Task.WaitAsync(TimeSpan.FromSeconds(30),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(key);
		}
	}

	private async ValueTask SendAsync<TRequest>(TRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"BitGo WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private void EnsureClient()
	{
		if (_client is not null)
			return;
		var client = new WebSocketClient(
			_endpoint.AbsoluteUri,
			(state, token) => OnStateChangedAsync(state, token),
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
			SendSettings = _settings,
		};
		client.Init += socket => socket.Options.SetRequestHeader(
			"Authorization", "Bearer " + _accessToken);
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			BitGoSocketRequest[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions.Values];
			foreach (var subscription in subscriptions)
				await SendAsync(subscription, cancellationToken);
		}
		if (state is ConnectionStates.Disconnected or ConnectionStates.Failed)
		{
			PendingSubscription[] pending;
			using (_sync.EnterScope())
				pending = [.. _pending.Values];
			foreach (var operation in pending)
				operation.Completion.TrySetException(new InvalidOperationException(
					"BitGo WebSocket disconnected before subscription acknowledgement."));
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<BitGoSocketHeader>(payload);
			if (header.Type == BitGoSocketMessageTypes.SubscriptionResponse)
			{
				ProcessSubscription(Deserialize<BitGoSubscriptionResponse>(payload));
				return;
			}
			if (header.Channel == BitGoSocketChannels.Level2 &&
				header.Type is BitGoSocketMessageTypes.Snapshot or
					BitGoSocketMessageTypes.Update)
			{
				if (BookReceived is { } bookHandler)
					await bookHandler(Deserialize<BitGoBookMessage>(payload),
						cancellationToken);
				return;
			}
			if (header.Channel == BitGoSocketChannels.Orders &&
				header.Type is BitGoSocketMessageTypes.Market or
					BitGoSocketMessageTypes.Limit or
					BitGoSocketMessageTypes.Twap or
					BitGoSocketMessageTypes.SteadyPace or
					BitGoSocketMessageTypes.Stop)
			{
				if (OrderReceived is { } orderHandler)
					await orderHandler(Deserialize<BitGoOrderUpdate>(payload),
						cancellationToken);
				return;
			}
			var error = header.Message;
			if (error.IsEmpty())
				error = header.Error;
			if (error.IsEmpty())
				error = header.ErrorName;
			throw new InvalidDataException(error.IsEmpty()
				? "BitGo WebSocket returned an unsupported message."
				: "BitGo WebSocket error: " + error);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or FormatException or InvalidOperationException)
		{
			FailPending(error);
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void FailPending(Exception error)
	{
		PendingSubscription[] pending;
		using (_sync.EnterScope())
			pending = [.. _pending.Values];
		foreach (var operation in pending)
			operation.Completion.TrySetException(error);
	}

	private void ProcessSubscription(BitGoSubscriptionResponse response)
	{
		var key = GetKey(response.Channel, response.ProductId);
		PendingSubscription operation;
		using (_sync.EnterScope())
			_pending.TryGetValue(key, out operation);
		if (operation is null)
			return;
		var isAccepted = operation.Action switch
		{
			BitGoSocketActions.Subscribe => response.Status is
				BitGoSubscriptionStatuses.Subscribed or
				BitGoSubscriptionStatuses.AlreadySubscribed,
			BitGoSocketActions.Unsubscribe => response.Status ==
				BitGoSubscriptionStatuses.Unsubscribed,
			_ => false,
		};
		if (isAccepted)
			operation.Completion.TrySetResult(response);
		else
			operation.Completion.TrySetException(new InvalidDataException(
				"Unexpected BitGo subscription status " + response.Status + "."));
	}

	private TResponse Deserialize<TResponse>(string payload)
		=> JsonConvert.DeserializeObject<TResponse>(payload, _settings) ??
			throw new InvalidDataException(
				"BitGo WebSocket returned an empty JSON message.");

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string GetKey(BitGoSocketChannels channel, string productId)
		=> channel + ":" + (productId ?? string.Empty).Trim();

	private static Uri NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) ||
			address.Scheme is not ("wss" or "ws"))
			throw new ArgumentException(
				"BitGo WebSocket endpoint must use WS or WSS.", nameof(endpoint));
		return address;
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		PendingSubscription[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
			_subscriptions.Clear();
		}
		foreach (var operation in pending)
			operation.Completion.TrySetCanceled(cancellationToken);
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

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_connectSync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
