namespace StockSharp.Pacifica.Native;

sealed class PacificaWebSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<PacificaSubscriptionKey> _subscriptions = [];
	private readonly Dictionary<string, TaskCompletionSource<PacificaActionResponse>>
		_pendingActions = new(StringComparer.Ordinal);
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

	public PacificaWebSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Pacifica_WS";

	public event Func<PacificaPrice[], CancellationToken, ValueTask> PricesReceived;
	public event Func<PacificaBestBidOffer, CancellationToken, ValueTask>
		BestBidOfferReceived;
	public event Func<PacificaBook, CancellationToken, ValueTask> BookReceived;
	public event Func<PacificaPublicTrade[], CancellationToken, ValueTask>
		TradesReceived;
	public event Func<PacificaCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<PacificaAccountInfoUpdate, CancellationToken, ValueTask>
		AccountInfoReceived;
	public event Func<PacificaPositionUpdate[], CancellationToken, ValueTask>
		PositionsReceived;
	public event Func<PacificaOrder[], CancellationToken, ValueTask> OrdersReceived;
	public event Func<PacificaAccountTrade[], CancellationToken, ValueTask>
		AccountTradesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Pacifica WebSocket is already initialized.");
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

	public ValueTask SubscribeAsync(PacificaSubscriptionKey key,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(key, true, cancellationToken);

	public ValueTask UnsubscribeAsync(PacificaSubscriptionKey key,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(key, false, cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> SendAsync(new PacificaPingRequest(), cancellationToken);

	public ValueTask<PacificaActionResponse> CreateOrderAsync(
		PacificaCreateOrderPayload payload, PacificaSigner signer, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		var signature = signer.Sign(PacificaOperationTypes.CreateOrder, payload,
			serverTime);
		return SendActionAsync(new PacificaCreateOrderRequest
		{
			Id = Guid.NewGuid().ToString("D"),
			Parameters = new()
			{
				CreateOrder = new()
				{
					Account = signature.Account,
					AgentWallet = signature.AgentWallet,
					Signature = signature.Value,
					Timestamp = signature.Timestamp,
					ExpiryWindow = signature.ExpiryWindow,
					Symbol = payload.Symbol,
					Price = payload.Price,
					IsReduceOnly = payload.IsReduceOnly,
					Amount = payload.Amount,
					Side = payload.Side,
					TimeInForce = payload.TimeInForce,
					ClientOrderId = payload.ClientOrderId,
					BuilderCode = payload.BuilderCode,
					TakeProfit = payload.TakeProfit,
					StopLoss = payload.StopLoss,
				},
			},
		}, cancellationToken);
	}

	public ValueTask<PacificaActionResponse> CreateMarketOrderAsync(
		PacificaCreateMarketOrderPayload payload, PacificaSigner signer,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		var signature = signer.Sign(PacificaOperationTypes.CreateMarketOrder,
			payload, serverTime);
		return SendActionAsync(new PacificaCreateMarketOrderRequest
		{
			Id = Guid.NewGuid().ToString("D"),
			Parameters = new()
			{
				CreateMarketOrder = new()
				{
					Account = signature.Account,
					AgentWallet = signature.AgentWallet,
					Signature = signature.Value,
					Timestamp = signature.Timestamp,
					ExpiryWindow = signature.ExpiryWindow,
					Symbol = payload.Symbol,
					IsReduceOnly = payload.IsReduceOnly,
					Amount = payload.Amount,
					Side = payload.Side,
					SlippagePercent = payload.SlippagePercent,
					ClientOrderId = payload.ClientOrderId,
					BuilderCode = payload.BuilderCode,
					TakeProfit = payload.TakeProfit,
					StopLoss = payload.StopLoss,
				},
			},
		}, cancellationToken);
	}

	public ValueTask<PacificaActionResponse> EditOrderAsync(
		PacificaEditOrderPayload payload, PacificaSigner signer, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		var signature = signer.Sign(PacificaOperationTypes.EditOrder, payload,
			serverTime);
		return SendActionAsync(new PacificaEditOrderRequest
		{
			Id = Guid.NewGuid().ToString("D"),
			Parameters = new()
			{
				EditOrder = new()
				{
					Account = signature.Account,
					AgentWallet = signature.AgentWallet,
					Signature = signature.Value,
					Timestamp = signature.Timestamp,
					ExpiryWindow = signature.ExpiryWindow,
					Symbol = payload.Symbol,
					Price = payload.Price,
					Amount = payload.Amount,
					OrderId = payload.OrderId,
					ClientOrderId = payload.ClientOrderId,
				},
			},
		}, cancellationToken);
	}

	public ValueTask<PacificaActionResponse> CancelOrderAsync(
		PacificaCancelOrderPayload payload, PacificaSigner signer,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		var signature = signer.Sign(PacificaOperationTypes.CancelOrder, payload,
			serverTime);
		return SendActionAsync(new PacificaCancelOrderRequest
		{
			Id = Guid.NewGuid().ToString("D"),
			Parameters = new()
			{
				CancelOrder = new()
				{
					Account = signature.Account,
					AgentWallet = signature.AgentWallet,
					Signature = signature.Value,
					Timestamp = signature.Timestamp,
					ExpiryWindow = signature.ExpiryWindow,
					Symbol = payload.Symbol,
					OrderId = payload.OrderId,
					ClientOrderId = payload.ClientOrderId,
				},
			},
		}, cancellationToken);
	}

	public ValueTask<PacificaActionResponse> CancelAllOrdersAsync(
		PacificaCancelAllOrdersPayload payload, PacificaSigner signer,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		var signature = signer.Sign(PacificaOperationTypes.CancelAllOrders,
			payload, serverTime);
		return SendActionAsync(new PacificaCancelAllOrdersRequest
		{
			Id = Guid.NewGuid().ToString("D"),
			Parameters = new()
			{
				CancelAllOrders = new()
				{
					Account = signature.Account,
					AgentWallet = signature.AgentWallet,
					Signature = signature.Value,
					Timestamp = signature.Timestamp,
					ExpiryWindow = signature.ExpiryWindow,
					IsAllSymbols = payload.IsAllSymbols,
					IsExcludeReduceOnly = payload.IsExcludeReduceOnly,
					Symbol = payload.Symbol,
				},
			},
		}, cancellationToken);
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
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Pacifica-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		FailPending(new InvalidOperationException(
			"Pacifica WebSocket was disconnected."));
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
			PacificaSubscriptionKey[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
		}
		else if (state == ConnectionStates.Failed)
			FailPending(new InvalidOperationException(
				"Pacifica WebSocket connection failed."));
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(PacificaSubscriptionKey key,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		ValidateSubscription(key);
		using (_sync.EnterScope())
			if (isSubscribe ? !_subscriptions.Add(key) : !_subscriptions.Remove(key))
				return;
		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, key, isSubscribe, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		PacificaSubscriptionKey key, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new PacificaSubscriptionRequest
		{
			Method = isSubscribe
				? PacificaWebSocketMethods.Subscribe
				: PacificaWebSocketMethods.Unsubscribe,
			Parameters = new()
			{
				Source = key.Source,
				Symbol = key.Symbol,
				Interval = key.Interval,
				AggregationLevel = key.AggregationLevel,
				Account = key.Account,
			},
		}, cancellationToken);

	private async ValueTask<PacificaActionResponse> SendActionAsync<TRequest>(
		TRequest request, CancellationToken cancellationToken)
	{
		var id = request switch
		{
			PacificaCreateOrderRequest value => value.Id,
			PacificaCreateMarketOrderRequest value => value.Id,
			PacificaEditOrderRequest value => value.Id,
			PacificaCancelOrderRequest value => value.Id,
			PacificaCancelAllOrdersRequest value => value.Id,
			_ => throw new ArgumentOutOfRangeException(nameof(request)),
		};
		var completion = new TaskCompletionSource<PacificaActionResponse>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pendingActions.Add(id, completion);
		try
		{
			await SendAsync(request, cancellationToken);
			var response = await completion.Task.WaitAsync(cancellationToken);
			if (response.Code is < 200 or >= 300 || !response.Error.IsEmpty())
				throw new InvalidOperationException(
					"Pacifica " + response.Type + " failed (" + response.Code +
					"): " + (response.Error.IsEmpty() ? "request rejected" :
					response.Error));
			return response;
		}
		finally
		{
			using (_sync.EnterScope())
				_pendingActions.Remove(id);
		}
	}

	private ValueTask SendAsync<TPayload>(TPayload payload,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Pacifica WebSocket is not connected.");
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
			var header = Deserialize<PacificaWebSocketHeader>(payload);
			if (!header.Id.IsEmpty() && header.Type is not null)
			{
				var action = Deserialize<PacificaActionResponse>(payload);
				TaskCompletionSource<PacificaActionResponse> completion;
				using (_sync.EnterScope())
					_pendingActions.TryGetValue(action.Id, out completion);
				completion?.TrySetResult(action);
				return;
			}

			switch (header.Channel)
			{
				case PacificaChannels.Subscribe:
				case PacificaChannels.Unsubscribe:
					_ = Deserialize<PacificaWebSocketEnvelope<
						PacificaSubscriptionAcknowledgement>>(payload);
					break;
				case PacificaChannels.Pong:
					break;
				case PacificaChannels.Prices:
					await RaiseAsync(PricesReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaPrice[]>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.BestBidOffer:
					await RaiseAsync(BestBidOfferReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaBestBidOffer>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.Book:
					var bookEnvelope = Deserialize<PacificaWebSocketEnvelope<PacificaBook>>(
						payload);
					await RaiseAsync(BookReceived, bookEnvelope.Data, cancellationToken);
					break;
				case PacificaChannels.Trades:
					await RaiseAsync(TradesReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaPublicTrade[]>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.Candle:
					await RaiseAsync(CandleReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaCandle>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.AccountInfo:
					await RaiseAsync(AccountInfoReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaAccountInfoUpdate>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.AccountPositions:
					await RaiseAsync(PositionsReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaPositionUpdate[]>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.AccountOrderUpdates:
					await RaiseAsync(OrdersReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaOrder[]>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.AccountTrades:
					await RaiseAsync(AccountTradesReceived,
						Deserialize<PacificaWebSocketEnvelope<PacificaAccountTrade[]>>(payload).Data,
						cancellationToken);
					break;
				case PacificaChannels.Error:
					throw new InvalidOperationException(
						"Pacifica WebSocket error: " +
						(header.Error.IsEmpty() ? payload : header.Error));
				case null:
					throw new InvalidDataException(
						"Pacifica WebSocket message has no channel or operation ID.");
				default:
					throw new InvalidDataException(
						"Unsupported Pacifica WebSocket channel '" +
						header.Channel + "'.");
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"Pacifica WebSocket returned an empty message.");

	private static ValueTask RaiseAsync<T>(
		Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> value is not null && handler is not null
			? handler(value, cancellationToken)
			: default;

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private void FailPending(Exception error)
	{
		TaskCompletionSource<PacificaActionResponse>[] completions;
		using (_sync.EnterScope())
		{
			completions = [.. _pendingActions.Values];
			_pendingActions.Clear();
		}
		foreach (var completion in completions)
			completion.TrySetException(error);
	}

	private static void ValidateSubscription(PacificaSubscriptionKey key)
	{
		if (key.Source is PacificaSources.BestBidOffer or PacificaSources.Book or
			PacificaSources.Trades or PacificaSources.Candle && key.Symbol.IsEmpty())
			throw new ArgumentException(
				"Pacifica symbol is required for this subscription.", nameof(key));
		if (key.Source == PacificaSources.Candle && key.Interval is null)
			throw new ArgumentException(
				"Pacifica candle interval is required.", nameof(key));
		if (key.Source is PacificaSources.AccountInfo or
			PacificaSources.AccountPositions or PacificaSources.AccountOrderUpdates or
			PacificaSources.AccountTrades && key.Account.IsEmpty())
			throw new ArgumentException(
				"Pacifica account is required for this subscription.", nameof(key));
	}

	protected override void DisposeManaged()
	{
		FailPending(new ObjectDisposedException(nameof(PacificaWebSocketClient)));
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
