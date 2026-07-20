namespace StockSharp.Nado.Native;

sealed class NadoWebSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<NadoSubscriptionKey> _subscriptions = [];
	private readonly Dictionary<long, TaskCompletionSource<NadoSocketResponse>>
		_pending = [];
	private readonly Dictionary<int, string> _depthTimestamps = [];
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

	public NadoWebSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Nado_WS";

	public event Func<NadoTradeEvent, CancellationToken, ValueTask> TradeReceived;
	public event Func<NadoBestBidOfferEvent, CancellationToken, ValueTask>
		BestBidOfferReceived;
	public event Func<NadoBookDepthEvent, CancellationToken, ValueTask>
		BookDepthReceived;
	public event Func<NadoCandleEvent, CancellationToken, ValueTask>
		CandleReceived;
	public event Func<NadoFundingRateEvent, CancellationToken, ValueTask>
		FundingRateReceived;
	public event Func<NadoFillEvent, CancellationToken, ValueTask> FillReceived;
	public event Func<NadoPositionChangeEvent, CancellationToken, ValueTask>
		PositionChangeReceived;
	public event Func<NadoOrderUpdateEvent, CancellationToken, ValueTask>
		OrderUpdateReceived;
	public event Func<int, CancellationToken, ValueTask> DepthGap;
	public event Func<DateTime, CancellationToken, ValueTask> ServerTimeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Nado WebSocket is already initialized.");
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

	public ValueTask SubscribeAsync(NadoSubscriptionKey subscription,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(subscription, true, cancellationToken);

	public ValueTask UnsubscribeAsync(NadoSubscriptionKey subscription,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(subscription, false, cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _requestId);
		var pending = CreatePending(id);
		try
		{
			var milliseconds = ((long)(DateTime.UtcNow - DateTime.UnixEpoch)
				.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
			await SendAsync(new NadoPingRequest
			{
				Id = id,
				ClientTime = milliseconds,
			}, cancellationToken);
			var response = await pending.Task.WaitAsync(TimeSpan.FromSeconds(10),
				cancellationToken);
			if (response.Result?.ServerTime is { } serverTime &&
				ServerTimeReceived is { } handler)
				await handler(serverTime.FromNadoMilliseconds(), cancellationToken);
		}
		finally
		{
			RemovePending(id);
		}
	}

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
				"StockSharp-Nado-Connector/1.0");
			socket.Options.DangerousDeflateOptions = new();
		};
		return client;
	}

	private async ValueTask ChangeSubscriptionAsync(
		NadoSubscriptionKey subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		ValidateSubscription(subscription);
		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, subscription, isSubscribe, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				if (isSubscribe)
					_subscriptions.Remove(subscription);
				else
					_subscriptions.Add(subscription);
			throw;
		}
	}

	private async ValueTask SendSubscriptionAsync(WebSocketClient client,
		NadoSubscriptionKey subscription, bool isSubscribe, bool isWaitResponse,
		CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _requestId);
		TaskCompletionSource<NadoSocketResponse> pending = null;
		if (isWaitResponse)
			pending = CreatePending(id);
		try
		{
			await SendAsync(client, new NadoSubscriptionRequest
			{
				Id = id,
				Method = isSubscribe ? "subscribe" : "unsubscribe",
				Stream = ToDefinition(subscription),
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

	private TaskCompletionSource<NadoSocketResponse> CreatePending(long id)
	{
		var pending = new TaskCompletionSource<NadoSocketResponse>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, pending);
		return pending;
	}

	private void RemovePending(long id)
	{
		using (_sync.EnterScope())
			_pending.Remove(id);
	}

	private ValueTask SendAsync<TPayload>(TPayload payload,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Nado WebSocket is not connected.");
		return SendAsync(client, payload, cancellationToken);
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
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<NadoSocketHeader>(payload);
			if (header.Id is long id)
			{
				CompletePending(id, Deserialize<NadoSocketResponse>(payload));
				return;
			}
			if (header.Type.IsEmpty())
				throw new InvalidDataException(
					"Nado WebSocket message has no event type.");
			switch (header.Type)
			{
				case "trade":
					await RaiseAsync(TradeReceived,
						Deserialize<NadoTradeEvent>(payload), cancellationToken);
					break;
				case "best_bid_offer":
					await RaiseAsync(BestBidOfferReceived,
						Deserialize<NadoBestBidOfferEvent>(payload),
						cancellationToken);
					break;
				case "book_depth":
					await ProcessDepthAsync(Deserialize<NadoBookDepthEvent>(payload),
						cancellationToken);
					break;
				case "latest_candlestick":
					await RaiseAsync(CandleReceived,
						Deserialize<NadoCandleEvent>(payload), cancellationToken);
					break;
				case "funding_rate":
					await RaiseAsync(FundingRateReceived,
						Deserialize<NadoFundingRateEvent>(payload), cancellationToken);
					break;
				case "fill":
					await RaiseAsync(FillReceived,
						Deserialize<NadoFillEvent>(payload), cancellationToken);
					break;
				case "position_change":
					await RaiseAsync(PositionChangeReceived,
						Deserialize<NadoPositionChangeEvent>(payload),
						cancellationToken);
					break;
				case "order_update":
					await RaiseAsync(OrderUpdateReceived,
						Deserialize<NadoOrderUpdateEvent>(payload),
						cancellationToken);
					break;
				default:
					this.AddWarningLog("Unknown Nado WebSocket event '{0}'.",
						header.Type);
					break;
			}
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Nado WebSocket message.", error),
				cancellationToken);
		}
	}

	private async ValueTask ProcessDepthAsync(NadoBookDepthEvent depth,
		CancellationToken cancellationToken)
	{
		bool isGap;
		using (_sync.EnterScope())
		{
			isGap = _depthTimestamps.TryGetValue(depth.ProductId,
				out var previous) &&
				!previous.Equals(depth.LastMaximumTimestamp,
					StringComparison.Ordinal);
			_depthTimestamps[depth.ProductId] = depth.MaximumTimestamp;
		}
		if (isGap && DepthGap is { } gapHandler)
			await gapHandler(depth.ProductId, cancellationToken);
		await RaiseAsync(BookDepthReceived, depth, cancellationToken);
	}

	private void CompletePending(long id, NadoSocketResponse response)
	{
		TaskCompletionSource<NadoSocketResponse> pending;
		using (_sync.EnterScope())
			_pending.TryGetValue(id, out pending);
		if (pending is null)
			return;
		if (response.Error is { } error)
			pending.TrySetException(new InvalidOperationException(
				"Nado WebSocket error " + error.Code.ToString(
					CultureInfo.InvariantCulture) + ": " + error.Message));
		else
			pending.TrySetResult(response);
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			NadoSubscriptionKey[] subscriptions;
			using (_sync.EnterScope())
			{
				_depthTimestamps.Clear();
				subscriptions = [.. _subscriptions];
			}
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true, false,
					cancellationToken);
		}
		else if (state == ConnectionStates.Failed)
			FailPending(new InvalidOperationException(
				"Nado WebSocket connection failed."));
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		FailPending(new InvalidOperationException(
			"Nado WebSocket was disconnected."));
		using (_sync.EnterScope())
			_depthTimestamps.Clear();
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
		TaskCompletionSource<NadoSocketResponse>[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var completion in pending)
			completion.TrySetException(error);
	}

	private T Deserialize<T>(string payload)
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"Nado returned an empty WebSocket JSON value.");

	private static NadoStreamDefinition ToDefinition(
		NadoSubscriptionKey subscription)
		=> new()
		{
			Type = subscription.Type switch
			{
				NadoStreamTypes.Trade => "trade",
				NadoStreamTypes.BestBidOffer => "best_bid_offer",
				NadoStreamTypes.BookDepth => "book_depth",
				NadoStreamTypes.LatestCandlestick => "latest_candlestick",
				NadoStreamTypes.FundingRate => "funding_rate",
				NadoStreamTypes.Fill => "fill",
				NadoStreamTypes.PositionChange => "position_change",
				NadoStreamTypes.OrderUpdate => "order_update",
				_ => throw new ArgumentOutOfRangeException(nameof(subscription),
					subscription, null),
			},
			ProductId = subscription.ProductId > 0
				? subscription.ProductId
				: null,
			Granularity = subscription.Granularity > 0
				? subscription.Granularity
				: null,
			Subaccount = subscription.Subaccount,
		};

	private static void ValidateSubscription(NadoSubscriptionKey subscription)
	{
		if (!Enum.IsDefined(subscription.Type))
			throw new ArgumentOutOfRangeException(nameof(subscription),
				subscription, null);
		if ((subscription.Type is NadoStreamTypes.Trade or
			NadoStreamTypes.BestBidOffer or NadoStreamTypes.BookDepth or
			NadoStreamTypes.LatestCandlestick) && subscription.ProductId <= 0)
			throw new InvalidOperationException(
				"Nado market stream requires a product ID.");
		if (subscription.Type == NadoStreamTypes.LatestCandlestick &&
			subscription.Granularity <= 0)
			throw new InvalidOperationException(
				"Nado candle stream requires a granularity.");
		if ((subscription.Type is NadoStreamTypes.Fill or
			NadoStreamTypes.PositionChange or NadoStreamTypes.OrderUpdate) &&
			subscription.Subaccount.IsEmpty())
			throw new InvalidOperationException(
				"Nado account stream requires a subaccount.");
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
