namespace StockSharp.HitBtc.Native;

readonly record struct HitBtcSubscription(string Channel, string Symbol);

sealed class HitBtcSocketClient : BaseLogReceiver
{
	private enum Requests
	{
		PlaceOrder,
		CancelOrder,
		ReplaceOrder,
		ActiveOrders,
		Balance,
	}

	private readonly string _endpoint;
	private readonly bool _isPrivate;
	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<HitBtcSubscription> _subscriptions = [];
	private readonly Dictionary<string, long> _bookSequences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, Requests> _requests = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authenticationCompletion;
	private bool _authenticated;
	private bool _reportsSubscribed;
	private bool _balancesSubscribed;
	private long _nextInternalId;

	public HitBtcSocketClient(string endpoint, bool isPrivate, SecureString key, SecureString secret,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_isPrivate = isPrivate;
		_key = key;
		_secret = secret;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;

		if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"HitBTC WebSocket endpoint must be an absolute WSS URI.", nameof(endpoint));

		if (_isPrivate)
		{
			if (_key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (_secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}
	}

	public override string Name
		=> nameof(HitBtc) + (_isPrivate ? "_TradingWs" : "_PublicWs");

	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, IEnumerable<Trade>, CancellationToken, ValueTask> NewTrades;
	public event Func<string, string, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<OrderBook, QuoteChangeStates, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<long, Order, CancellationToken, ValueTask> OrderChanged;
	public event Func<long, Order[], CancellationToken, ValueTask> NewOrders;
	public event Func<long, Balance[], CancellationToken, ValueTask> BalanceChanged;
	public event Func<long, string, CancellationToken, ValueTask> OrderError;
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
			throw new InvalidOperationException("HitBTC WebSocket is already initialized.");

		_authenticationCompletion = _isPrivate ? CreateCompletion() : null;
		var client = _client = CreateClient();

		try
		{
			await client.ConnectAsync(cancellationToken);

			if (_isPrivate)
				await _authenticationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendCommandAsync(client, "ping", null, EmptyRequest.Instance, NextInternalId(),
				cancellationToken)
			: default;

	public ValueTask SubscribeTickerAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("ticker/1s", NormalizeSymbol(symbol)), true,
			transactionId, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("ticker/1s", NormalizeSymbol(symbol)), false,
			transactionId, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", NormalizeSymbol(symbol)), true,
			transactionId, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", NormalizeSymbol(symbol)), false,
			transactionId, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orderbook/full", NormalizeSymbol(symbol)), true,
			transactionId, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orderbook/full", NormalizeSymbol(symbol)), false,
			transactionId, cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, string period, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new($"candles/{period}", NormalizeSymbol(symbol)), true,
			transactionId, cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, string period, long transactionId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new($"candles/{period}", NormalizeSymbol(symbol)), false,
			transactionId, cancellationToken);

	public ValueTask PlaceOrderAsync(string clientOrderId, string symbol, string side, string type,
		decimal? price, decimal quantity, string timeInForce, decimal? stopPrice,
		DateTime? expireTime, long transactionId, CancellationToken cancellationToken)
		=> SendPrivateRequestAsync("spot_new_order", new NewOrderRequest
		{
			ClientOrderId = clientOrderId,
			Symbol = NormalizeSymbol(symbol),
			Side = side,
			Type = type,
			Price = price,
			Quantity = quantity,
			TimeInForce = timeInForce,
			StopPrice = stopPrice,
			ExpireTime = expireTime,
		}, transactionId, Requests.PlaceOrder, cancellationToken);

	public ValueTask CancelOrderAsync(string clientOrderId, long transactionId,
		CancellationToken cancellationToken)
		=> SendPrivateRequestAsync("spot_cancel_order", new CancelOrderRequest
		{
			ClientOrderId = clientOrderId,
		}, transactionId, Requests.CancelOrder, cancellationToken);

	public ValueTask ReplaceOrderAsync(string clientOrderId, string newClientOrderId, decimal price,
		decimal? quantity, long transactionId, CancellationToken cancellationToken)
		=> SendPrivateRequestAsync("spot_replace_order", new ReplaceOrderRequest
		{
			ClientOrderId = clientOrderId,
			NewClientOrderId = newClientOrderId,
			Price = price,
			Quantity = quantity,
		}, transactionId, Requests.ReplaceOrder, cancellationToken);

	public ValueTask RequestActiveOrdersAsync(long transactionId,
		CancellationToken cancellationToken)
		=> SendPrivateRequestAsync("spot_get_orders", EmptyRequest.Instance, transactionId,
			Requests.ActiveOrders, cancellationToken);

	public ValueTask RequestBalanceAsync(long transactionId, CancellationToken cancellationToken)
		=> SendPrivateRequestAsync("spot_balances", EmptyRequest.Instance, transactionId,
			Requests.Balance, cancellationToken);

	public ValueTask SubscribeReportsAsync(bool subscribe, CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync(true, subscribe, cancellationToken);

	public ValueTask SubscribeBalancesAsync(bool subscribe, CancellationToken cancellationToken)
		=> ChangePrivateSubscriptionAsync(false, subscribe, cancellationToken);

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

		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-HitBTC-Connector/3.0");
			return default;
		};

		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		_authenticated = false;
		_requests.Clear();
		_authenticationCompletion?.TrySetCanceled(cancellationToken);
		_authenticationCompletion = null;

		using (_sync.EnterScope())
			_bookSequences.Clear();

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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Disconnected or ConnectionStates.Failed)
		{
			_authenticated = false;
			using (_sync.EnterScope())
				_bookSequences.Clear();
		}

		if (state is ConnectionStates.Connected or ConnectionStates.Restored)
		{
			if (_isPrivate)
			{
				_authenticated = false;

				if (state == ConnectionStates.Restored || _authenticationCompletion is null)
					_authenticationCompletion = CreateCompletion();

				await SendAuthenticationAsync(client, cancellationToken);
			}
			else if (state == ConnectionStates.Restored)
			{
				await SynchronizePublicSubscriptionsAsync(client, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();

		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;

		try
		{
			if (_isPrivate && !_authenticated)
			{
				await ProcessAuthenticationAsync(payload, cancellationToken);
				return;
			}

			var header = Deserialize<WsHeader>(payload);

			if (header.Error is not null)
			{
				await ProcessErrorAsync(header.Id, header.Error, cancellationToken);
				return;
			}

			if (_isPrivate)
				await ProcessPrivateMessageAsync(header, payload, cancellationToken);
			else
				await ProcessPublicMessageAsync(header, payload, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException or
			ArgumentException)
		{
			if (_isPrivate && !_authenticated)
				_authenticationCompletion?.TrySetException(error);

			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessAuthenticationAsync(string payload,
		CancellationToken cancellationToken)
	{
		var response = Deserialize<WsResponse<bool>>(payload);

		if (response.Error is not null || !response.Result)
		{
			var error = new InvalidOperationException(
				$"HitBTC trading WebSocket authentication failed: {response.Error}");
			_authenticationCompletion?.TrySetException(error);
			throw error;
		}

		_authenticated = true;
		_authenticationCompletion?.TrySetResult(true);

		if (_client is { IsConnected: true } client)
			await SynchronizePrivateSubscriptionsAsync(client, cancellationToken);
	}

	private async ValueTask ProcessPublicMessageAsync(WsHeader header, string payload,
		CancellationToken cancellationToken)
	{
		if (header.Channel.IsEmpty())
			return;

		if (header.Channel.StartsWithIgnoreCase("ticker/"))
		{
			var response = Deserialize<WsFeed<Ticker>>(payload);

			foreach (var pair in response.Data ?? [])
			{
				pair.Value.Symbol = pair.Key;

				if (TickerChanged is { } handler)
					await handler.InvokeAsync(pair.Value, cancellationToken);
			}

			return;
		}

		switch (header.Channel)
		{
			case "trades":
				await ProcessTradesAsync(payload, cancellationToken);
				break;

			case "orderbook/full":
				await ProcessOrderBooksAsync(payload, cancellationToken);
				break;

			default:
				if (header.Channel.StartsWithIgnoreCase("candles/"))
					await ProcessCandlesAsync(header.Channel, payload, cancellationToken);
				else
					this.AddWarningLog("Unknown HitBTC public channel: {0}.", header.Channel);
				break;
		}
	}

	private async ValueTask ProcessTradesAsync(string payload, CancellationToken cancellationToken)
	{
		var response = Deserialize<WsFeed<WsTrade[]>>(payload);
		var data = response.Snapshot ?? response.Update ?? response.Data;

		if (data is null || NewTrades is not { } handler)
			return;

		foreach (var pair in data)
			await handler.InvokeAsync(pair.Key, (pair.Value ?? []).Select(static trade => trade.ToTrade()),
				cancellationToken);
	}

	private async ValueTask ProcessCandlesAsync(string channel, string payload,
		CancellationToken cancellationToken)
	{
		var response = Deserialize<WsFeed<WsOhlc[]>>(payload);
		var data = response.Snapshot ?? response.Update ?? response.Data;
		var period = channel[(channel.IndexOf('/') + 1)..];

		if (data is null || NewCandle is not { } handler)
			return;

		foreach (var pair in data)
		{
			foreach (var candle in pair.Value ?? [])
				await handler.InvokeAsync(pair.Key, period, candle.ToOhlc(), cancellationToken);
		}
	}

	private async ValueTask ProcessOrderBooksAsync(string payload,
		CancellationToken cancellationToken)
	{
		var response = Deserialize<WsFeed<OrderBook>>(payload);
		var isSnapshot = response.Snapshot is not null;
		var data = response.Snapshot ?? response.Update ?? response.Data;

		if (data is null)
			return;

		foreach (var pair in data)
		{
			var book = pair.Value;
			book.Symbol = pair.Key;
			var gap = false;

			using (_sync.EnterScope())
			{
				if (isSnapshot)
					_bookSequences[pair.Key] = book.Sequence;
				else if (!_bookSequences.TryGetValue(pair.Key, out var previous) ||
					book.Sequence != previous + 1)
				{
					_bookSequences.Remove(pair.Key);
					gap = true;
				}
				else
					_bookSequences[pair.Key] = book.Sequence;
			}

			if (gap)
			{
				this.AddWarningLog(
					"HitBTC order book sequence gap for {0}. Resubscribing.", pair.Key);
				await ResubscribeBookAsync(pair.Key, cancellationToken);
				continue;
			}

			if (OrderBookChanged is { } handler)
				await handler.InvokeAsync(book, isSnapshot
					? QuoteChangeStates.SnapshotComplete
					: QuoteChangeStates.Increment, cancellationToken);
		}
	}

	private async ValueTask ProcessPrivateMessageAsync(WsHeader header, string payload,
		CancellationToken cancellationToken)
	{
		if (header.Id is long id && TryTakeRequest(id, out var request))
		{
			switch (request)
			{
				case Requests.PlaceOrder:
				case Requests.CancelOrder:
				case Requests.ReplaceOrder:
				{
					var response = Deserialize<WsResponse<Order>>(payload);
					if (response.Result is not null && OrderChanged is { } handler)
						await handler.InvokeAsync(id, response.Result, cancellationToken);
					break;
				}

				case Requests.ActiveOrders:
				{
					var response = Deserialize<WsResponse<Order[]>>(payload);
					if (NewOrders is { } handler)
						await handler.InvokeAsync(id, response.Result ?? [], cancellationToken);
					break;
				}

				case Requests.Balance:
				{
					var response = Deserialize<WsResponse<Balance[]>>(payload);
					if (BalanceChanged is { } handler)
						await handler.InvokeAsync(id, response.Result ?? [], cancellationToken);
					break;
				}

				default:
					throw new ArgumentOutOfRangeException(nameof(request), request, null);
			}

			return;
		}

		switch (header.Method)
		{
			case "spot_order":
			{
				var notification = Deserialize<WsNotification<Order>>(payload);
				if (notification.Params is not null && OrderChanged is { } handler)
					await handler.InvokeAsync(0, notification.Params, cancellationToken);
				break;
			}

			case "spot_orders":
			{
				var notification = Deserialize<WsNotification<Order[]>>(payload);
				if (NewOrders is { } handler)
					await handler.InvokeAsync(0, notification.Params ?? [], cancellationToken);
				break;
			}

			case "spot_balance":
			{
				var notification = Deserialize<WsNotification<Balance[]>>(payload);
				if (BalanceChanged is { } handler)
					await handler.InvokeAsync(0, notification.Params ?? [], cancellationToken);
				break;
			}
		}
	}

	private async ValueTask ProcessErrorAsync(long? id, ApiError error,
		CancellationToken cancellationToken)
	{
		if (id is long requestId && TryTakeRequest(requestId, out var request) &&
			request is Requests.PlaceOrder or Requests.CancelOrder or Requests.ReplaceOrder)
		{
			if (OrderError is { } handler)
				await handler.InvokeAsync(requestId, error.ToString(), cancellationToken);
			return;
		}

		await RaiseErrorAsync(new InvalidOperationException($"HitBTC WebSocket error: {error}"),
			cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(HitBtcSubscription subscription,
		bool subscribe, long transactionId, CancellationToken cancellationToken)
	{
		EnsurePublic();
		bool changed;

		using (_sync.EnterScope())
		{
			changed = subscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);

			if (!subscribe && subscription.Channel == "orderbook/full")
				_bookSequences.Remove(subscription.Symbol);
		}

		if (!changed || _client?.IsConnected != true)
			return;

		await SendPublicSubscriptionAsync(_client, subscription, subscribe, transactionId,
			cancellationToken);
	}

	private async ValueTask ChangePrivateSubscriptionAsync(bool reports, bool subscribe,
		CancellationToken cancellationToken)
	{
		EnsurePrivate();
		bool changed;

		using (_sync.EnterScope())
		{
			if (reports)
			{
				changed = _reportsSubscribed != subscribe;
				_reportsSubscribed = subscribe;
			}
			else
			{
				changed = _balancesSubscribed != subscribe;
				_balancesSubscribed = subscribe;
			}
		}

		if (!changed || !_authenticated || _client?.IsConnected != true)
			return;

		await SendPrivateSubscriptionAsync(_client, reports, subscribe, cancellationToken);
	}

	private async ValueTask SynchronizePublicSubscriptionsAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		HitBtcSubscription[] subscriptions;

		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions];

		foreach (var subscription in subscriptions)
			await SendPublicSubscriptionAsync(client, subscription, true, NextInternalId(),
				cancellationToken);
	}

	private async ValueTask SynchronizePrivateSubscriptionsAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		bool reports;
		bool balances;

		using (_sync.EnterScope())
		{
			reports = _reportsSubscribed;
			balances = _balancesSubscribed;
		}

		if (reports)
			await SendPrivateSubscriptionAsync(client, true, true, cancellationToken);

		if (balances)
			await SendPrivateSubscriptionAsync(client, false, true, cancellationToken);
	}

	private ValueTask SendPublicSubscriptionAsync(WebSocketClient client,
		HitBtcSubscription subscription, bool subscribe, long transactionId,
		CancellationToken cancellationToken)
		=> SendCommandAsync(client, subscribe ? "subscribe" : "unsubscribe",
			subscription.Channel, new SubscriptionRequest
			{
				Symbols = [subscription.Symbol],
			}, transactionId, cancellationToken);

	private ValueTask SendPrivateSubscriptionAsync(WebSocketClient client, bool reports,
		bool subscribe, CancellationToken cancellationToken)
	{
		var method = reports
			? subscribe ? "spot_subscribe" : "spot_unsubscribe"
			: subscribe ? "spot_balance_subscribe" : "spot_balance_unsubscribe";

		return reports
			? SendCommandAsync(client, method, null, EmptyRequest.Instance, NextInternalId(),
				cancellationToken)
			: SendCommandAsync(client, method, null, new BalanceSubscriptionRequest
				{
					Mode = "updates",
				}, NextInternalId(), cancellationToken);
	}

	private async ValueTask ResubscribeBookAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var subscription = new HitBtcSubscription("orderbook/full", symbol);
		bool expected;

		using (_sync.EnterScope())
			expected = _subscriptions.Contains(subscription);

		if (!expected || _client?.IsConnected != true)
			return;

		await SendPublicSubscriptionAsync(_client, subscription, false, NextInternalId(),
			cancellationToken);
		await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		await SendPublicSubscriptionAsync(_client, subscription, true, NextInternalId(),
			cancellationToken);
	}

	private ValueTask SendAuthenticationAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		const int window = 10000;
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		using var hasher = new HMACSHA256(_secret.UnSecure().UTF8());
		var signature = hasher.ComputeHash($"{timestamp}{window}".UTF8())
			.Digest()
			.ToLowerInvariant();

		return SendCommandAsync(client, "login", null, new LoginRequest
		{
			Type = "HS256",
			ApiKey = _key.UnSecure(),
			Timestamp = timestamp,
			Window = window,
			Signature = signature,
		}, null, cancellationToken);
	}

	private async ValueTask SendPrivateRequestAsync<T>(string method, T parameters,
		long transactionId, Requests request, CancellationToken cancellationToken)
	{
		EnsurePrivate();

		if (!_authenticated || _client?.IsConnected != true)
			throw new InvalidOperationException("HitBTC trading WebSocket is not authenticated.");

		_requests.Add(transactionId, request);

		try
		{
			await SendCommandAsync(_client, method, null, parameters, transactionId,
				cancellationToken);
		}
		catch
		{
			_requests.Remove(transactionId);
			throw;
		}
	}

	private async ValueTask SendCommandAsync<T>(WebSocketClient client, string method,
		string channel, T parameters, long? id, CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);

		try
		{
			await client.SendAsync(new WsCommand<T>
			{
				Method = method,
				Channel = channel,
				Params = parameters,
				Id = id,
			}, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private bool TryTakeRequest(long id, out Requests request)
	{
		if (!_requests.TryGetValue(id, out request))
			return false;

		_requests.Remove(id);
		return true;
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
				throw new InvalidDataException("HitBTC WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("HitBTC WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;

	private void EnsurePublic()
	{
		if (_isPrivate)
			throw new InvalidOperationException("Public subscription requested on trading socket.");
	}

	private void EnsurePrivate()
	{
		if (!_isPrivate)
			throw new InvalidOperationException("Trading request sent to public socket.");
	}

	private long NextInternalId()
		=> Interlocked.Decrement(ref _nextInternalId);

	private static TaskCompletionSource<bool> CreateCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
}
