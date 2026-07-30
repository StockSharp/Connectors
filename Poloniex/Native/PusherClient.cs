namespace StockSharp.Poloniex.Native;

readonly record struct PoloniexSubscription(string Channel, string Symbol);

sealed class PoloniexSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly bool _isPrivate;
	private readonly Authenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<PoloniexSubscription> _subscriptions = [];
	private readonly Dictionary<string, long> _bookSequences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	private WebSocketClient _client;
	private bool _authenticated;
	private TaskCompletionSource<bool> _authenticationCompletion;

	public PoloniexSocketClient(string endpoint, bool isPrivate, Authenticator authenticator,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_isPrivate = isPrivate;
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;

		if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Poloniex WebSocket endpoint must be an absolute WSS URI.", nameof(endpoint));

		if (_isPrivate && !_authenticator.CanSign)
			throw new InvalidOperationException(
				"Poloniex credentials are required for the private WebSocket.");
	}

	public override string Name
		=> nameof(Poloniex) + (_isPrivate ? "_PrivateWs" : "_PublicWs");

	public event Func<PoloniexTicker, CancellationToken, ValueTask> TickerChanged;
	public event Func<PoloniexBookUpdate, QuoteChangeStates, CancellationToken, ValueTask> BookChanged;
	public event Func<PoloniexPublicTrade, CancellationToken, ValueTask> NewTrade;
	public event Func<PoloniexBalanceUpdate, CancellationToken, ValueTask> BalanceChanged;
	public event Func<PoloniexOrderUpdate, CancellationToken, ValueTask> OrderChanged;
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
			throw new InvalidOperationException("Poloniex WebSocket is already initialized.");

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
			? SendAsync(client, new PoloniexWsCommand { Event = "ping" }, cancellationToken)
			: default;

	public ValueTask SubscribeTickerAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("ticker", "all"), true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("ticker", "all"), false, cancellationToken);

	public ValueTask SubscribeBookAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("book_lv2", NormalizeSymbol(symbol)), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("book_lv2", NormalizeSymbol(symbol)), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", NormalizeSymbol(symbol)), true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", NormalizeSymbol(symbol)), false,
			cancellationToken);

	public ValueTask SubscribeAccountAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionsAsync(
			[new("orders", "all"), new("balances", null)], true, cancellationToken);

	public ValueTask UnsubscribeAccountAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionsAsync(
			[new("orders", "all"), new("balances", null)], false, cancellationToken);

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
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-Poloniex-Connector/1.0");
			return default;
		};

		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		_authenticated = false;
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
				await SynchronizeSubscriptionsAsync(client, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionsAsync(PoloniexSubscription[] subscriptions,
		bool subscribe, CancellationToken cancellationToken)
	{
		foreach (var subscription in subscriptions)
			await ChangeSubscriptionAsync(subscription, subscribe, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(PoloniexSubscription subscription,
		bool subscribe, CancellationToken cancellationToken)
	{
		bool changed;

		using (_sync.EnterScope())
		{
			changed = subscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);

			if (!subscribe && subscription.Channel == "book_lv2" && subscription.Symbol is not null)
				_bookSequences.Remove(subscription.Symbol);
		}

		if (!changed || _client?.IsConnected != true || _isPrivate && !_authenticated)
			return;

		await SendSubscriptionAsync(_client, subscription, subscribe, cancellationToken);
	}

	private async ValueTask SynchronizeSubscriptionsAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		PoloniexSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions];

		foreach (var subscription in subscriptions)
			await SendSubscriptionAsync(client, subscription, true, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		PoloniexSubscription subscription, bool subscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new PoloniexWsCommand
		{
			Event = subscribe ? "subscribe" : "unsubscribe",
			Channel = [subscription.Channel],
			Symbols = subscription.Symbol is null ? null : [subscription.Symbol],
		}, cancellationToken);

	private ValueTask SendAuthenticationAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var timestamp = _authenticator.GetTimestamp();
		var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
		var signature = _authenticator.Sign($"GET\n/ws\nsignTimestamp={timestampText}");

		return SendAsync(client, new PoloniexWsCommand
		{
			Event = "subscribe",
			Channel = ["auth"],
			Parameters = new()
			{
				Key = _authenticator.Key.UnSecure(),
				Timestamp = timestamp,
				SignatureMethod = "HmacSHA256",
				SignatureVersion = "2",
				Signature = signature,
			},
		}, cancellationToken);
	}

	private async ValueTask SendAsync<T>(WebSocketClient client, T payload,
		CancellationToken cancellationToken)
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

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();

		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;

		try
		{
			var header = Deserialize<PoloniexWsHeader>(payload);

			if (header.Event == "error")
				throw new InvalidOperationException(
					$"Poloniex WebSocket error: {header.Message}".Trim());

			if (header.Event is "subscribe" or "unsubscribe" or "UNSUBSCRIBE")
				return;

			switch (header.Channel)
			{
				case "auth":
					await ProcessAuthenticationAsync(payload, cancellationToken);
					break;
				case "ticker":
					await RaiseItemsAsync(payload, TickerChanged, cancellationToken);
					break;
				case "trades":
					await RaiseItemsAsync(payload, NewTrade, cancellationToken);
					break;
				case "book_lv2":
					await ProcessBooksAsync(header.Action, payload, cancellationToken);
					break;
				case "orders":
					await RaiseItemsAsync(payload, OrderChanged, cancellationToken);
					break;
				case "balances":
					await RaiseItemsAsync(payload, BalanceChanged, cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			if (_isPrivate && !_authenticated)
				_authenticationCompletion?.TrySetException(error);

			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessAuthenticationAsync(string payload,
		CancellationToken cancellationToken)
	{
		var response = Deserialize<PoloniexWsAuthEnvelope>(payload);

		if (response.Data?.Success != true)
		{
			var error = new InvalidOperationException(
				$"Poloniex private WebSocket authentication failed: {response.Data?.Message}".Trim());
			_authenticationCompletion?.TrySetException(error);
			throw error;
		}

		_authenticated = true;
		_authenticationCompletion?.TrySetResult(true);

		if (_client is { IsConnected: true } client)
			await SynchronizeSubscriptionsAsync(client, cancellationToken);
	}

	private async ValueTask ProcessBooksAsync(string action, string payload,
		CancellationToken cancellationToken)
	{
		var response = Deserialize<PoloniexWsEnvelope<PoloniexBookUpdate>>(payload);
		var state = action.EqualsIgnoreCase("snapshot")
			? QuoteChangeStates.SnapshotComplete
			: QuoteChangeStates.Increment;

		foreach (var book in response.Data ?? [])
		{
			if (book.Symbol.IsEmpty())
				continue;

			var gap = false;
			using (_sync.EnterScope())
			{
				if (state == QuoteChangeStates.SnapshotComplete)
					_bookSequences[book.Symbol] = book.Id;
				else if (!_bookSequences.TryGetValue(book.Symbol, out var previous) ||
					book.LastId != previous)
				{
					_bookSequences.Remove(book.Symbol);
					gap = true;
				}
				else
					_bookSequences[book.Symbol] = book.Id;
			}

			if (gap)
			{
				await ResubscribeBookAsync(book.Symbol, cancellationToken);
				continue;
			}

			if (BookChanged is { } handler)
				await handler(book, state, cancellationToken);
		}
	}

	private async ValueTask ResubscribeBookAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var subscription = new PoloniexSubscription("book_lv2", symbol);
		bool expected;

		using (_sync.EnterScope())
			expected = _subscriptions.Contains(subscription);

		if (!expected || _client?.IsConnected != true)
			return;

		await SendSubscriptionAsync(_client, subscription, false, cancellationToken);
		await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		await SendSubscriptionAsync(_client, subscription, true, cancellationToken);
	}

	private async ValueTask RaiseItemsAsync<T>(string payload,
		Func<T, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
	{
		if (handler is null)
			return;

		var response = Deserialize<PoloniexWsEnvelope<T>>(payload);

		foreach (var item in response.Data ?? [])
			await handler(item, cancellationToken);
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
				throw new InvalidDataException("Poloniex WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Poloniex WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static TaskCompletionSource<bool> CreateCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
}
