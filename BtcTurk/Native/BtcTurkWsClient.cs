namespace StockSharp.BtcTurk.Native;

sealed class BtcTurkWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(string Channel, string Event);

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public BtcTurkWsClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "BtcTurk_Ws";

	public event Func<BtcTurkWsOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<BtcTurkWsTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<BtcTurkWsTicker, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

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
				"BtcTurk WebSocket is already initialized.");
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

	public ValueTask SubscribeOrderBookAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orderbook",
			pairSymbol.ThrowIfEmpty(nameof(pairSymbol)).ToUpperInvariant()),
			true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orderbook",
			pairSymbol.ThrowIfEmpty(nameof(pairSymbol)).ToUpperInvariant()),
			false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trade",
			pairSymbol.ThrowIfEmpty(nameof(pairSymbol)).ToUpperInvariant()),
			true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trade",
			pairSymbol.ThrowIfEmpty(nameof(pairSymbol)).ToUpperInvariant()),
			false, cancellationToken);

	public ValueTask SubscribeTickerAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("ticker",
			pairSymbol.ThrowIfEmpty(nameof(pairSymbol)).ToUpperInvariant()),
			true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string pairSymbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("ticker",
			pairSymbol.ThrowIfEmpty(nameof(pairSymbol)).ToUpperInvariant()),
			false, cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
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
			SendSettings = _jsonSettings,
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-BtcTurk-Connector/1.0");
			return default;
		};
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
			using (_sync.EnterScope())
				_serverSubscriptions.Clear();
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				subscriptions = [.. _desiredSubscriptions];
			}
			foreach (var subscription in subscriptions)
			{
				using (_sync.EnterScope())
				if (!_desiredSubscriptions.Contains(subscription) ||
					!_serverSubscriptions.Add(subscription))
					continue;
				try
				{
					await SendSubscriptionAsync(client, subscription, true,
						cancellationToken);
				}
				catch
				{
					using (_sync.EnterScope())
						_serverSubscriptions.Remove(subscription);
					throw;
				}
			}
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = _client;
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = client?.IsConnected == true &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = client?.IsConnected == true &&
					_serverSubscriptions.Remove(subscription);
			}
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(client, subscription, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverSubscriptions.Remove(subscription);
				else
					_serverSubscriptions.Add(subscription);
			}
			throw;
		}
	}

	private async ValueTask SendSubscriptionAsync(WebSocketClient client,
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(new object[]
			{
				(int)BtcTurkWsMessageTypes.Subscription,
				new BtcTurkWsSubscription
				{
					Channel = subscription.Channel,
					Event = subscription.Event,
					IsSubscribe = isSubscribe,
				},
			}, cancellationToken);
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
			var root = JArray.Parse(payload);
			if (root.Count != 2 || root[0].Type != JTokenType.Integer)
				throw new InvalidDataException(
					"BtcTurk WebSocket returned an invalid envelope.");
			var type = (BtcTurkWsMessageTypes)root[0].Value<int>();
			switch (type)
			{
				case BtcTurkWsMessageTypes.Result:
				var result = root[1].ToObject<BtcTurkWsResult>(
					JsonSerializer.Create(_jsonSettings));
				if (result?.IsSuccess == false)
					throw new InvalidDataException(
						$"BtcTurk WebSocket request failed: {result.Message}");
				break;

				case BtcTurkWsMessageTypes.Version:
					break;

				case BtcTurkWsMessageTypes.Ticker:
					if (TickerReceived is { } tickerHandler)
						await tickerHandler.InvokeAsync(DeserializeEnvelope<
							BtcTurkWsTicker>(payload).Data,
							cancellationToken);
					break;

				case BtcTurkWsMessageTypes.OrderBook:
					if (OrderBookReceived is { } bookHandler)
						await bookHandler.InvokeAsync(DeserializeEnvelope<
							BtcTurkWsOrderBook>(payload).Data,
							cancellationToken);
					break;

				case BtcTurkWsMessageTypes.TradeHistory:
					if (TradeReceived is { } historyHandler)
					{
						var history = DeserializeEnvelope<
							BtcTurkWsTradeHistory>(payload).Data;
						foreach (var trade in history?.Items ?? [])
						{
							trade.PairSymbol =
								trade.PairSymbol.IsEmpty(history.PairSymbol);
							await historyHandler.InvokeAsync(trade, cancellationToken);
						}
					}
					break;

				case BtcTurkWsMessageTypes.Trade:
					if (TradeReceived is { } tradeHandler)
						await tradeHandler.InvokeAsync(DeserializeEnvelope<
							BtcTurkWsTrade>(payload).Data,
							cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	internal static BtcTurkWsEnvelope<TData> DeserializeEnvelope<TData>(
		string payload)
	{
		try
		{
			var root = JArray.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			if (root.Count != 2 || root[0].Type != JTokenType.Integer)
				throw new InvalidDataException(
					"BtcTurk WebSocket returned an invalid envelope.");
			var data = root[1].ToObject<TData>(JsonSerializer.Create(
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}));
			return new()
			{
				Type = (BtcTurkWsMessageTypes)root[0].Value<int>(),
				Data = data ?? throw new InvalidDataException(
					"BtcTurk WebSocket returned an empty payload."),
			};
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BtcTurk WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;
}
