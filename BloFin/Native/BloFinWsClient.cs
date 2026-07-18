namespace StockSharp.BloFin.Native;

readonly record struct BloFinWsSubscriptionKey(string Channel, string InstrumentId);

sealed class BloFinWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly bool _isPrivate;
	private readonly string _apiKey;
	private readonly string _secret;
	private readonly string _passphrase;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<BloFinWsSubscriptionKey> _subscriptions = [];
	private readonly HashSet<BloFinWsSubscriptionKey> _sentSubscriptions = [];
	private readonly Dictionary<string, long> _bookSequences = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private bool _isLoggedIn;
	private TaskCompletionSource<bool> _loginCompletion;

	public BloFinWsClient(string endpoint, bool isPrivate, SecureString key, SecureString secret,
		SecureString passphrase, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_isPrivate = isPrivate;
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secret = secret.IsEmpty() ? null : secret.UnSecure();
		_passphrase = passphrase.IsEmpty() ? null : passphrase.UnSecure();
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(BloFin) + (_isPrivate ? "_PrivateWs" : "_PublicWs");

	public bool IsCredentialsAvailable
		=> !_apiKey.IsEmpty() && !_secret.IsEmpty() && !_passphrase.IsEmpty();

	public event Func<BloFinTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, string, BloFinBook, QuoteChangeStates, CancellationToken, ValueTask> BookReceived;
	public event Func<BloFinTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, string, BloFinCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<BloFinFundingRate, CancellationToken, ValueTask> FundingRateReceived;
	public event Func<BloFinOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<BloFinPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<BloFinAccount, CancellationToken, ValueTask> AccountReceived;
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
			throw new InvalidOperationException("BloFin WebSocket is already initialized.");
		if (_isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BloFin API key, secret, and passphrase are required for private WebSocket access.");

		_loginCompletion = _isPrivate ? CreateLoginCompletion() : null;
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			if (_isPrivate)
				await _loginCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTickerAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("tickers", instrumentId), true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("tickers", instrumentId), false, cancellationToken);

	public ValueTask SubscribeBookAsync(string instrumentId, bool isFiveLevels,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(isFiveLevels ? "books5" : "books", instrumentId), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string instrumentId, bool isFiveLevels,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(isFiveLevels ? "books5" : "books", instrumentId), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", instrumentId), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", instrumentId), false, cancellationToken);

	public ValueTask SubscribeCandlesAsync(string instrumentId, BloFinCandleIntervals interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("candle" + interval.ToBloFin(), instrumentId), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string instrumentId, BloFinCandleIntervals interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("candle" + interval.ToBloFin(), instrumentId), false,
			cancellationToken);

	public ValueTask SubscribeFundingRateAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("funding-rate", instrumentId), true, cancellationToken);

	public ValueTask UnsubscribeFundingRateAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("funding-rate", instrumentId), false, cancellationToken);

	public ValueTask SubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orders", null), true, cancellationToken);

	public ValueTask UnsubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orders", null), false, cancellationToken);

	public ValueTask SubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("positions", null), true, cancellationToken);

	public ValueTask UnsubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("positions", null), false, cancellationToken);

	public ValueTask SubscribeAccountAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("account", null), true, cancellationToken);

	public ValueTask UnsubscribeAccountAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("account", null), false, cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, "ping", cancellationToken)
			: default;

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
			"StockSharp-BloFin-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		_isLoggedIn = false;
		_loginCompletion?.TrySetCanceled(cancellationToken);
		_loginCompletion = null;
		using (_sync.EnterScope())
		{
			_sentSubscriptions.Clear();
			_bookSequences.Clear();
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Disconnected or ConnectionStates.Failed)
		{
			_isLoggedIn = false;
			using (_sync.EnterScope())
			{
				_sentSubscriptions.Clear();
				_bookSequences.Clear();
			}
		}

		if (state is ConnectionStates.Connected or ConnectionStates.Restored)
		{
			if (_isPrivate)
			{
				_isLoggedIn = false;
				using (_sync.EnterScope())
					_sentSubscriptions.Clear();
				if (state == ConnectionStates.Restored || _loginCompletion is null)
					_loginCompletion = CreateLoginCompletion();
				await SendLoginAsync(client, cancellationToken);
			}
			else if (state == ConnectionStates.Restored)
			{
				BloFinWsSubscriptionKey[] subscriptions;
				using (_sync.EnterScope())
				{
					_bookSequences.Clear();
					subscriptions = [.. _subscriptions];
				}
				foreach (var subscription in subscriptions)
					await SendSubscriptionAsync(client, subscription, true, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(BloFinWsSubscriptionKey subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		bool shouldSend;
		using (_sync.EnterScope())
		{
			shouldSend = isSubscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);
			if (!isSubscribe && subscription.Channel.StartsWith("books", StringComparison.Ordinal))
				_bookSequences.Remove(subscription.InstrumentId ?? string.Empty);
		}
		if (!shouldSend || _client?.IsConnected != true || _isPrivate && !_isLoggedIn)
			return;

		await SendSubscriptionAsync(_client, subscription, isSubscribe, cancellationToken);
		if (_isPrivate)
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_sentSubscriptions.Add(subscription);
				else
					_sentSubscriptions.Remove(subscription);
			}
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		BloFinWsSubscriptionKey subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new BloFinWsCommand<BloFinWsSubscription>
		{
			Operation = isSubscribe ? BloFinWsOperations.Subscribe : BloFinWsOperations.Unsubscribe,
			Arguments =
			[
				new()
				{
					Channel = subscription.Channel,
					InstrumentId = subscription.InstrumentId,
				},
			],
		}, cancellationToken);

	private async ValueTask SendLoginAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
		var prehash = "/users/self/verify" + "GET" + timestamp + nonce;
		var hash = HMACSHA256.HashData(_secret.UTF8(), prehash.UTF8());
		var hex = Convert.ToHexString(hash).ToLowerInvariant();
		var signature = Convert.ToBase64String(hex.UTF8());
		await SendAsync(client, new BloFinWsCommand<BloFinWsLogin>
		{
			Operation = BloFinWsOperations.Login,
			Arguments =
			[
				new()
				{
					ApiKey = _apiKey,
					Passphrase = _passphrase,
					Timestamp = timestamp,
					Nonce = nonce,
					Signature = signature,
				},
			],
		}, cancellationToken);
	}

	private async ValueTask SynchronizePrivateSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		if (!_isPrivate || !_isLoggedIn || _client?.IsConnected != true)
			return;

		BloFinWsSubscriptionKey[] subscribe;
		BloFinWsSubscriptionKey[] unsubscribe;
		using (_sync.EnterScope())
		{
			subscribe = [.. _subscriptions.Except(_sentSubscriptions)];
			unsubscribe = [.. _sentSubscriptions.Except(_subscriptions)];
		}
		foreach (var item in subscribe)
		{
			await SendSubscriptionAsync(_client, item, true, cancellationToken);
			using (_sync.EnterScope())
				_sentSubscriptions.Add(item);
		}
		foreach (var item in unsubscribe)
		{
			await SendSubscriptionAsync(_client, item, false, cancellationToken);
			using (_sync.EnterScope())
				_sentSubscriptions.Remove(item);
		}
	}

	private async ValueTask SendAsync(WebSocketClient client, string payload,
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

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
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
			var header = Deserialize<BloFinWsHeader>(payload);
			if (header.Event is not null)
			{
				await ProcessEventAsync(header, cancellationToken);
				return;
			}

			var channel = header.Argument?.Channel;
			if (channel.IsEmpty())
				return;
			switch (channel)
			{
				case "tickers":
					{
						var envelope = Deserialize<BloFinWsEnvelope<BloFinTicker>>(payload);
						if (TickerReceived is { } handler)
						{
							foreach (var item in envelope.Data ?? [])
								await handler(item, cancellationToken);
						}
						break;
					}
				case "books":
				case "books5":
					await ProcessBooksAsync(channel, payload, cancellationToken);
					break;
				case "trades":
					{
						var envelope = Deserialize<BloFinWsEnvelope<BloFinTrade>>(payload);
						if (TradeReceived is { } handler)
						{
							foreach (var item in envelope.Data ?? [])
								await handler(item, cancellationToken);
						}
						break;
					}
				case "funding-rate":
					{
						var envelope = Deserialize<BloFinWsEnvelope<BloFinFundingRate>>(payload);
						if (FundingRateReceived is { } handler)
						{
							foreach (var item in envelope.Data ?? [])
								await handler(item, cancellationToken);
						}
						break;
					}
				case "orders":
					{
						var envelope = Deserialize<BloFinWsEnvelope<BloFinOrder>>(payload);
						if (OrderReceived is { } handler)
						{
							foreach (var item in envelope.Data ?? [])
								await handler(item, cancellationToken);
						}
						break;
					}
				case "positions":
					{
						var envelope = Deserialize<BloFinWsEnvelope<BloFinPosition>>(payload);
						if (PositionReceived is { } handler)
						{
							foreach (var item in envelope.Data ?? [])
								await handler(item, cancellationToken);
						}
						break;
					}
				case "account":
					{
						var envelope = Deserialize<BloFinWsObjectEnvelope<BloFinAccount>>(payload);
						if (envelope.Data is not null && AccountReceived is { } handler)
							await handler(envelope.Data, cancellationToken);
						break;
					}
				default:
					if (channel.StartsWith("candle", StringComparison.OrdinalIgnoreCase))
					{
						var envelope = Deserialize<BloFinWsEnvelope<BloFinCandle>>(payload);
						var instrumentId = envelope.Argument?.InstrumentId;
						if (!instrumentId.IsEmpty() && CandleReceived is { } handler)
						{
							foreach (var item in envelope.Data ?? [])
								await handler(channel, instrumentId, item, cancellationToken);
						}
					}
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessEventAsync(BloFinWsHeader header,
		CancellationToken cancellationToken)
	{
		if (header.Event == BloFinWsEvents.Login)
		{
			if (header.Code != "0")
			{
				var error = new InvalidOperationException(
					$"BloFin private WebSocket login failed ({header.Code}): {header.Message}".Trim());
				_loginCompletion?.TrySetException(error);
				throw error;
			}
			_isLoggedIn = true;
			_loginCompletion?.TrySetResult(true);
			await SynchronizePrivateSubscriptionsAsync(cancellationToken);
			return;
		}

		if (header.Event == BloFinWsEvents.Error)
		{
			var error = new InvalidOperationException(
				$"BloFin WebSocket error {header.Code}: {header.Message}".Trim());
			if (_isPrivate && !_isLoggedIn)
				_loginCompletion?.TrySetException(error);
			throw error;
		}
	}

	private async ValueTask ProcessBooksAsync(string channel, string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<BloFinWsEnvelope<BloFinBook>>(payload);
		var instrumentId = envelope.Argument?.InstrumentId;
		if (instrumentId.IsEmpty())
			return;

		foreach (var book in envelope.Data ?? [])
		{
			var state = channel == "books5" || envelope.Action == BloFinWsActions.Snapshot
				? QuoteChangeStates.SnapshotComplete
				: QuoteChangeStates.Increment;
			var hasGap = false;
			if (channel == "books")
			{
				using (_sync.EnterScope())
				{
					if (state == QuoteChangeStates.SnapshotComplete)
						_bookSequences[instrumentId] = book.SequenceId;
					else if (!_bookSequences.TryGetValue(instrumentId, out var previous) ||
						book.PreviousSequenceId != previous)
					{
						hasGap = true;
						_bookSequences.Remove(instrumentId);
					}
					else
						_bookSequences[instrumentId] = book.SequenceId;
				}
			}
			if (hasGap)
			{
				await ResubscribeBookAsync(instrumentId, cancellationToken);
				continue;
			}
			if (BookReceived is { } handler)
				await handler(channel, instrumentId, book, state, cancellationToken);
		}
	}

	private async ValueTask ResubscribeBookAsync(string instrumentId,
		CancellationToken cancellationToken)
	{
		var subscription = new BloFinWsSubscriptionKey("books", instrumentId);
		bool isExpected;
		using (_sync.EnterScope())
			isExpected = _subscriptions.Contains(subscription);
		if (!isExpected || _client?.IsConnected != true)
			return;
		await SendSubscriptionAsync(_client, subscription, false, cancellationToken);
		await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		await SendSubscriptionAsync(_client, subscription, true, cancellationToken);
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings);
			if (result is null)
				throw new InvalidDataException("BloFin WebSocket returned an empty message.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("BloFin WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static TaskCompletionSource<bool> CreateLoginCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
