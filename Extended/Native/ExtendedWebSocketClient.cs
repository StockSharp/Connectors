namespace StockSharp.Extended.Native;

sealed class ExtendedWebSocketClient : BaseLogReceiver
{
	private sealed class PendingRequest
	{
		public ExtendedSubscriptionKey Subscription { get; init; }
		public bool IsSubscription { get; init; }
		public bool IsSubscribe { get; init; }
		public TaskCompletionSource<ExtendedRpcResult> Completion { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private readonly string _endpoint;
	private readonly string _apiKey;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<ExtendedSubscriptionKey> _subscriptions = [];
	private readonly Dictionary<string, ExtendedSubscriptionKey>
		_subscriptionByTopic = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PendingRequest> _pendingRequests =
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
	private long? _lastSequence;

	public ExtendedWebSocketClient(string endpoint, string apiKey,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_apiKey = apiKey?.Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Extended_WS";

	public event Func<ExtendedOrderBook, string, long, long, bool,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<ExtendedPublicTrade[], long, long, CancellationToken,
		ValueTask> TradesReceived;
	public event Func<ExtendedFundingRate, long, long, CancellationToken,
		ValueTask> FundingRateReceived;
	public event Func<ExtendedPriceUpdate, ExtendedPriceTypes, long, long,
		CancellationToken, ValueTask> PriceReceived;
	public event Func<string, string, ExtendedCandle[], long, long,
		CancellationToken, ValueTask> CandlesReceived;
	public event Func<ExtendedPosition[], bool, long, long, CancellationToken,
		ValueTask> PositionsReceived;
	public event Func<ExtendedOrder[], bool, long, long, CancellationToken,
		ValueTask> OrdersReceived;
	public event Func<ExtendedAccountTrade[], bool, long, long,
		CancellationToken, ValueTask> AccountTradesReceived;
	public event Func<ExtendedBalance, bool, long, long, CancellationToken,
		ValueTask> BalanceReceived;
	public event Func<ExtendedSpotBalance[], bool, long, long,
		CancellationToken, ValueTask> SpotBalancesReceived;
	public event Func<long, long, CancellationToken, ValueTask> SequenceGap;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Extended WebSocket is already initialized.");
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

	public ValueTask SubscribeAsync(ExtendedSubscriptionKey subscription,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(subscription, true, cancellationToken);

	public ValueTask UnsubscribeAsync(ExtendedSubscriptionKey subscription,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(subscription, false, cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		var pending = new PendingRequest();
		using (_sync.EnterScope())
			_pendingRequests.Add(id, pending);
		try
		{
			await SendAsync(new ExtendedRpcSimpleRequest
			{
				Method = "ping",
				Id = id,
			}, cancellationToken);
			await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_pendingRequests.Remove(id);
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
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Extended-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		FailPending(new InvalidOperationException(
			"Extended WebSocket was disconnected."));
		using (_sync.EnterScope())
		{
			_subscriptionByTopic.Clear();
			_lastSequence = null;
		}
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
			ExtendedSubscriptionKey[] subscriptions;
			using (_sync.EnterScope())
			{
				_lastSequence = null;
				_subscriptionByTopic.Clear();
				subscriptions = [.. _subscriptions];
				foreach (var subscription in subscriptions)
					_subscriptionByTopic[GetTopic(subscription)] = subscription;
			}
			foreach (var subscription in subscriptions)
				await SendSubscriptionRequestAsync(client, subscription, true, false,
					cancellationToken);
		}
		else if (state == ConnectionStates.Failed)
			FailPending(new InvalidOperationException(
				"Extended WebSocket connection failed."));
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		ExtendedSubscriptionKey subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		ValidateSubscription(subscription);
		bool changed;
		using (_sync.EnterScope())
		{
			changed = isSubscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);
			if (changed)
			{
				var topic = GetTopic(subscription);
				if (isSubscribe)
					_subscriptionByTopic[topic] = subscription;
				else
					_subscriptionByTopic.Remove(topic);
			}
		}
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionRequestAsync(_client, subscription, isSubscribe,
				true, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				var topic = GetTopic(subscription);
				if (isSubscribe)
				{
					_subscriptions.Remove(subscription);
					_subscriptionByTopic.Remove(topic);
				}
				else
				{
					_subscriptions.Add(subscription);
					_subscriptionByTopic[topic] = subscription;
				}
			}
			throw;
		}
	}

	private async ValueTask SendSubscriptionRequestAsync(WebSocketClient client,
		ExtendedSubscriptionKey subscription, bool isSubscribe, bool waitResponse,
		CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		PendingRequest pending = null;
		if (waitResponse)
		{
			pending = new()
			{
				Subscription = subscription,
				IsSubscription = true,
				IsSubscribe = isSubscribe,
			};
			using (_sync.EnterScope())
				_pendingRequests.Add(id, pending);
		}
		try
		{
			await SendAsync(client, new ExtendedRpcRequest
			{
				Method = isSubscribe ? "subscribe" : "unsubscribe",
				Id = id,
				Parameters = subscription.ToParameters(_apiKey),
			}, cancellationToken);
			if (pending is not null)
				await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		finally
		{
			if (pending is not null)
				using (_sync.EnterScope())
					_pendingRequests.Remove(id);
		}
	}

	private string NextRequestId()
		=> Interlocked.Increment(ref _requestId).ToString(
			CultureInfo.InvariantCulture);

	private ValueTask SendAsync<TPayload>(TPayload payload,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Extended WebSocket is not connected.");
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
			var header = Deserialize<ExtendedRpcHeader>(payload);
			if (!header.Id.IsEmpty())
			{
				ProcessResponse(header);
				return;
			}
			if (header.RpcError is { } rpcError)
				throw new InvalidOperationException(
					"Extended WebSocket error " + rpcError.Code + ": " +
					rpcError.Message);
			if (header.Sequence is not long sequence ||
				header.Subscription.IsEmpty())
				throw new InvalidDataException(
					"Extended WebSocket message has no sequence or subscription.");
			long? previous;
			ExtendedSubscriptionKey subscription;
			using (_sync.EnterScope())
			{
				previous = _lastSequence;
				_lastSequence = sequence;
				_subscriptionByTopic.TryGetValue(header.Subscription,
					out subscription);
			}
			if (previous is long last && sequence != last + 1 &&
				SequenceGap is { } gapHandler)
				await gapHandler(last, sequence, cancellationToken);
			if (subscription == default)
				return;
			await DispatchAsync(subscription, header, payload, cancellationToken);
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void ProcessResponse(ExtendedRpcHeader response)
	{
		PendingRequest pending;
		using (_sync.EnterScope())
			_pendingRequests.TryGetValue(response.Id, out pending);
		if (pending is null)
			return;
		if (response.RpcError is { } error)
		{
			pending.Completion.TrySetException(new InvalidOperationException(
				"Extended RPC error " + error.Code + ": " + error.Message));
			return;
		}
		var result = response.Result ?? new ExtendedRpcResult
		{
			Status = "OK",
		};
		if (!result.Status.IsEmpty() &&
			!result.Status.Equals("OK", StringComparison.Ordinal))
		{
			pending.Completion.TrySetException(new InvalidOperationException(
				"Extended RPC request failed with status '" + result.Status + "'."));
			return;
		}
		if (pending.IsSubscription && !result.Subscription.IsEmpty())
			using (_sync.EnterScope())
			{
				if (pending.IsSubscribe)
					_subscriptionByTopic[result.Subscription] = pending.Subscription;
				else
					_subscriptionByTopic.Remove(result.Subscription);
			}
		pending.Completion.TrySetResult(result);
	}

	private async ValueTask DispatchAsync(ExtendedSubscriptionKey subscription,
		ExtendedRpcHeader header, string payload,
		CancellationToken cancellationToken)
	{
		switch (subscription.Scope)
		{
			case ExtendedStreamScopes.OrderBooks:
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<ExtendedOrderBook>>(
					payload);
				ValidateEnvelope(envelope.Error);
				if (envelope.Data is not null && envelope.Data.Market.IsEmpty())
					envelope.Data.Market = subscription.Market;
				if (OrderBookReceived is { } handler)
					await handler(envelope.Data, subscription.Detail,
						envelope.Timestamp,
						envelope.Sequence,
						envelope.Data?.UpdateType.Equals("SNAPSHOT",
							StringComparison.Ordinal) == true ||
							header.Type?.EndsWith(".SNAPSHOT",
								StringComparison.Ordinal) == true, cancellationToken);
				break;
			}
			case ExtendedStreamScopes.Trades:
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedPublicTrade[]>>(payload);
				ValidateEnvelope(envelope.Error);
				if (TradesReceived is { } handler)
					await handler(envelope.Data ?? [], envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case ExtendedStreamScopes.FundingRates:
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedFundingRate>>(payload);
				ValidateEnvelope(envelope.Error);
				if (FundingRateReceived is { } handler)
					await handler(envelope.Data, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case ExtendedStreamScopes.Prices:
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedPriceUpdate>>(payload);
				ValidateEnvelope(envelope.Error);
				var priceType = subscription.Detail switch
				{
					"mark" => ExtendedPriceTypes.Mark,
					"index" => ExtendedPriceTypes.Index,
					_ => throw new InvalidDataException(
						"Extended returned an unknown price stream."),
				};
				if (PriceReceived is { } handler)
					await handler(envelope.Data, priceType, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case ExtendedStreamScopes.Candles:
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<ExtendedCandle[]>>(
					payload);
				ValidateEnvelope(envelope.Error);
				if (CandlesReceived is { } handler)
					await handler(subscription.Market, subscription.Interval,
						envelope.Data ?? [], envelope.Timestamp, envelope.Sequence,
						cancellationToken);
				break;
			}
			case ExtendedStreamScopes.Account:
				await DispatchAccountAsync(header.Type, payload, cancellationToken);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(subscription),
					subscription, "Unsupported Extended stream scope.");
		}
	}

	private async ValueTask DispatchAccountAsync(string messageType,
		string payload, CancellationToken cancellationToken)
	{
		switch (messageType)
		{
			case "ACCOUNT.POSITION":
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedAccountPositionsUpdate>>(payload);
				ValidateEnvelope(envelope.Error);
				if (PositionsReceived is { } handler)
					await handler(envelope.Data?.Positions ?? [],
						envelope.Data?.IsSnapshot == true, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case "ACCOUNT.ORDER":
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedAccountOrdersUpdate>>(payload);
				ValidateEnvelope(envelope.Error);
				if (OrdersReceived is { } handler)
					await handler(envelope.Data?.Orders ?? [],
						envelope.Data?.IsSnapshot == true, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case "ACCOUNT.TRADE":
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedAccountTradesUpdate>>(payload);
				ValidateEnvelope(envelope.Error);
				if (AccountTradesReceived is { } handler)
					await handler(envelope.Data?.Trades ?? [],
						envelope.Data?.IsSnapshot == true, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case "ACCOUNT.BALANCE":
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedAccountBalanceUpdate>>(payload);
				ValidateEnvelope(envelope.Error);
				if (BalanceReceived is { } handler)
					await handler(envelope.Data?.Balance,
						envelope.Data?.IsSnapshot == true, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case "ACCOUNT.SPOT_BALANCE":
			{
				var envelope = Deserialize<ExtendedStreamEnvelope<
					ExtendedAccountSpotBalancesUpdate>>(payload);
				ValidateEnvelope(envelope.Error);
				if (SpotBalancesReceived is { } handler)
					await handler(envelope.Data?.SpotBalances ?? [],
						envelope.Data?.IsSnapshot == true, envelope.Timestamp,
						envelope.Sequence, cancellationToken);
				break;
			}
			case "ACCOUNT.DEPOSIT":
			case "ACCOUNT.WITHDRAWAL":
				break;
			default:
				throw new InvalidDataException(
					"Extended returned unknown account stream type '" +
					messageType + "'.");
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"Extended returned an empty WebSocket payload.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Extended returned malformed WebSocket JSON.", error);
		}
	}

	private static void ValidateEnvelope(string error)
	{
		if (!error.IsEmpty())
			throw new InvalidOperationException(
				"Extended WebSocket stream error: " + error);
	}

	private static string GetTopic(ExtendedSubscriptionKey key)
		=> key.Scope switch
		{
			ExtendedStreamScopes.OrderBooks when key.Detail == "1" =>
				"orderbooks.1." + key.Market,
			ExtendedStreamScopes.OrderBooks when key.Detail == "full" =>
				"orderbooks." + key.Market,
			ExtendedStreamScopes.Trades => "trades." + key.Market,
			ExtendedStreamScopes.FundingRates =>
				"funding-rates." + key.Market,
			ExtendedStreamScopes.Prices =>
				"prices." + key.Detail + "." + key.Market,
			ExtendedStreamScopes.Candles =>
				"candles." + key.Detail + "." + key.Market + "." + key.Interval,
			ExtendedStreamScopes.Account => "account." + key.Account,
			_ => throw new ArgumentOutOfRangeException(nameof(key), key,
				"Unsupported Extended subscription."),
		};

	private static void ValidateSubscription(ExtendedSubscriptionKey key)
	{
		_ = GetTopic(key);
		if (key.Scope == ExtendedStreamScopes.Account)
			return;
		key.Market.ThrowIfEmpty(nameof(key.Market));
	}

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private void FailPending(Exception error)
	{
		PendingRequest[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pendingRequests.Values];
			_pendingRequests.Clear();
		}
		foreach (var request in pending)
			request.Completion.TrySetException(error);
	}

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
