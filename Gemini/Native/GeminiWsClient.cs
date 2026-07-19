namespace StockSharp.Gemini.Native;

sealed class GeminiWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly GeminiRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly bool _isCancelOnDisconnect;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, TaskCompletionSource<GeminiWsResponse>> _pending = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _requestId;

	public GeminiWsClient(string endpoint, GeminiRestClient restClient,
		bool isCancelOnDisconnect, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_isCancelOnDisconnect = isCancelOnDisconnect;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Gemini) + "_Ws";

	public bool IsPrivateAvailable => _restClient.IsAccountKey;

	public event Func<GeminiWsBookTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<GeminiWsDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<GeminiWsTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<GeminiWsOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<GeminiWsBalanceUpdate, CancellationToken, ValueTask> BalanceReceived;
	public event Func<GeminiWsPositionReport, CancellationToken, ValueTask> PositionReceived;
	public event Func<CancellationToken, ValueTask> Resynchronizing;
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
			throw new InvalidOperationException("Gemini WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(symbol.ToLowerInvariant() + "@bookTicker", true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(symbol.ToLowerInvariant() + "@bookTicker", false,
			cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(symbol.ToLowerInvariant() + "@depth@100ms", true,
			cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(symbol.ToLowerInvariant() + "@depth@100ms", false,
			cancellationToken);

	public async ValueTask ResubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var stream = symbol.ToLowerInvariant() + "@depth@100ms";
		using (_sync.EnterScope())
			if (!_subscriptions.Contains(stream))
				return;
		await SendSubscriptionAsync([stream], false, cancellationToken);
		await SendSubscriptionAsync([stream], true, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(symbol.ToLowerInvariant() + "@trade", true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(symbol.ToLowerInvariant() + "@trade", false,
			cancellationToken);

	public ValueTask SubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync("orders@account", true, cancellationToken);

	public ValueTask UnsubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync("orders@account", false, cancellationToken);

	public ValueTask SubscribeBalancesAsync(CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync("balances@account", true, cancellationToken);

	public ValueTask UnsubscribeBalancesAsync(CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync("balances@account", false, cancellationToken);

	public ValueTask SubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync("positions@account", true, cancellationToken);

	public ValueTask UnsubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync("positions@account", false, cancellationToken);

	public ValueTask PlaceOrderAsync(GeminiWsPlaceOrderParams parameters,
		CancellationToken cancellationToken)
	{
		EnsurePrivateAvailable();
		ArgumentNullException.ThrowIfNull(parameters);
		var id = NextRequestId();
		return SendAndWaitAsync(id, new GeminiWsPlaceOrderRequest
		{
			Id = id,
			Params = parameters,
		}, cancellationToken);
	}

	public ValueTask CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
	{
		EnsurePrivateAvailable();
		var id = NextRequestId();
		return SendAndWaitAsync(id, new GeminiWsCancelOrderRequest
		{
			Id = id,
			Params = new() { OrderId = orderId.ThrowIfEmpty(nameof(orderId)) },
		}, cancellationToken);
	}

	public ValueTask CancelAllOrdersAsync(CancellationToken cancellationToken)
	{
		EnsurePrivateAvailable();
		var id = NextRequestId();
		return SendAndWaitAsync(id, new GeminiWsSimpleRequest
		{
			Id = id,
			Method = GeminiWsMethods.CancelAllOrders,
		}, cancellationToken);
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
	{
		if (_client?.IsConnected != true)
			return default;
		var id = NextRequestId();
		return SendAndWaitAsync(id, new GeminiWsSimpleRequest
		{
			Id = id,
			Method = GeminiWsMethods.Ping,
		}, cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		var separator = _endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
		var endpoint = _endpoint + separator + "snapshot=-1&cancelOnDisconnect=" +
			(_isCancelOnDisconnect ? "true" : "false");
		client = new WebSocketClient(
			endpoint,
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
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-Gemini-Connector/1.0");
			if (!IsPrivateAvailable)
				return;
			var auth = _restClient.CreateWebSocketAuthentication();
			socket.Options.SetRequestHeader("X-GEMINI-APIKEY", _restClient.ApiKey);
			socket.Options.SetRequestHeader("X-GEMINI-NONCE", auth.Nonce);
			socket.Options.SetRequestHeader("X-GEMINI-PAYLOAD", auth.Payload);
			socket.Options.SetRequestHeader("X-GEMINI-SIGNATURE", auth.Signature);
		};
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		CancelPending();
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			CancelPending();
			if (Resynchronizing is { } resynchronizing)
				await resynchronizing(cancellationToken);
			string[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			if (subscriptions.Length > 0)
				await SendSubscriptionAsync(client, subscriptions, true,
					cancellationToken);
		}
		else if (state is ConnectionStates.Disconnected or ConnectionStates.Failed)
			CancelPending();

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask ChangePrivateSubscriptionAsync(string stream, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		EnsurePrivateAvailable();
		return ChangeSubscriptionAsync(stream, isSubscribe, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(string stream, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		stream.ThrowIfEmpty(nameof(stream));
		using (_sync.EnterScope())
			if (isSubscribe ? !_subscriptions.Add(stream) : !_subscriptions.Remove(stream))
				return;

		if (_client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAndWaitAsync([stream], isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(stream);
				else
					_subscriptions.Add(stream);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(string[] streams, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendSubscriptionAsync(_client, streams, isSubscribe, cancellationToken);

	private ValueTask SendSubscriptionAndWaitAsync(string[] streams, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		return SendAndWaitAsync(id, new GeminiWsSubscriptionRequest
		{
			Id = id,
			Method = isSubscribe ? GeminiWsMethods.Subscribe : GeminiWsMethods.Unsubscribe,
			Streams = streams,
		}, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, string[] streams,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		return SendOnlyAsync(client, new GeminiWsSubscriptionRequest
		{
			Id = id,
			Method = isSubscribe ? GeminiWsMethods.Subscribe : GeminiWsMethods.Unsubscribe,
			Streams = streams,
		}, cancellationToken);
	}

	private async ValueTask SendOnlyAsync<TRequest>(WebSocketClient client,
		TRequest request, CancellationToken cancellationToken)
	{
		if (client?.IsConnected != true)
			throw new InvalidOperationException("Gemini WebSocket is not connected.");
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

	private ValueTask SendAndWaitAsync<TRequest>(long id, TRequest request,
		CancellationToken cancellationToken)
		=> SendAndWaitAsync(_client, id, request, cancellationToken);

	private async ValueTask SendAndWaitAsync<TRequest>(WebSocketClient client, long id,
		TRequest request, CancellationToken cancellationToken)
	{
		if (client?.IsConnected != true)
			throw new InvalidOperationException("Gemini WebSocket is not connected.");
		var completion = new TaskCompletionSource<GeminiWsResponse>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, completion);
		try
		{
			await _sendSync.WaitAsync(cancellationToken);
			try
			{
				await client.SendAsync(request, cancellationToken);
			}
			finally
			{
				_sendSync.Release();
			}
			var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15),
				cancellationToken);
			if (response.Error is not null || response.Status >= 400)
				throw new InvalidOperationException(
					$"Gemini WebSocket error {response.Error?.Code}: " +
					$"{response.Error?.Message ?? "request rejected"} (status {response.Status}).");
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(id);
		}
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
			var header = Deserialize<GeminiWsHeader>(payload);
			if (header.Id is long id && header.Status is not null)
			{
				TaskCompletionSource<GeminiWsResponse> completion;
				using (_sync.EnterScope())
					_pending.TryGetValue(id, out completion);
				var response = Deserialize<GeminiWsResponse>(payload);
				if (completion is not null)
					completion.TrySetResult(response);
				else if (response.Error is not null || response.Status >= 400)
					throw new InvalidOperationException(
						$"Gemini WebSocket error {response.Error?.Code}: " +
						$"{response.Error?.Message ?? "request rejected"} " +
						$"(status {response.Status}).");
				return;
			}

			switch (header.Event)
			{
				case "depthUpdate":
					if (DepthReceived is { } depthHandler)
						await depthHandler(Deserialize<GeminiWsDepthUpdate>(payload),
							cancellationToken);
					return;
				case "orderUpdate":
					if (OrderReceived is { } orderHandler)
						await orderHandler(Deserialize<GeminiWsOrderUpdate>(payload),
							cancellationToken);
					return;
				case "balanceUpdate":
					if (BalanceReceived is { } balanceHandler)
						await balanceHandler(Deserialize<GeminiWsBalanceUpdate>(payload),
							cancellationToken);
					return;
				case "positionReport":
					if (PositionReceived is { } positionHandler)
						await positionHandler(Deserialize<GeminiWsPositionReport>(payload),
							cancellationToken);
					return;
				case null:
					break;
				default:
					this.AddVerboseLog("Ignore Gemini WebSocket event {0}.",
						header.Event);
					return;
			}

			if (header.TradeId is not null)
			{
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(Deserialize<GeminiWsTrade>(payload),
						cancellationToken);
			}
			else if (header.Bid is not null && header.BidSize is not null &&
				!header.Symbol.IsEmpty() && TickerReceived is { } tickerHandler)
				await tickerHandler(Deserialize<GeminiWsBookTicker>(payload),
					cancellationToken);
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException(
					"Gemini WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Gemini WebSocket returned malformed JSON.", error);
		}
	}

	private long NextRequestId() => Interlocked.Increment(ref _requestId);

	private void CancelPending()
	{
		TaskCompletionSource<GeminiWsResponse>[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var completion in pending)
			completion.TrySetCanceled();
	}

	private void EnsurePrivateAvailable()
	{
		if (!IsPrivateAvailable)
			throw new InvalidOperationException(
				"Gemini WebSocket trading requires an account-scoped API key " +
				"whose name starts with 'account-'.");
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
