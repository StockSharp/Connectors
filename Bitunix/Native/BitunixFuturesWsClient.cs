namespace StockSharp.Bitunix.Native;

readonly record struct BitunixWsSubscriptionKey(string Channel, string Symbol);

sealed class BitunixFuturesWsClient : BaseLogReceiver
{
	private sealed class SendGate : IDisposable
	{
		public SemaphoreSlim Semaphore { get; } = new(1, 1);
		public DateTime NextTime { get; set; }
		public void Dispose() => Semaphore.Dispose();
	}

	private readonly string _publicEndpoint;
	private readonly string _privateEndpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly string _apiKey;
	private readonly string _secretKey;
	private readonly Lock _sync = new();
	private readonly HashSet<BitunixWsSubscriptionKey> _publicSubscriptions = [];
	private readonly HashSet<string> _privateSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _sentPrivateSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SendGate _publicSendGate = new();
	private readonly SendGate _privateSendGate = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _publicClient;
	private WebSocketClient _privateClient;
	private bool _isPrivateLoggedIn;
	private bool _isPrivateLoginPending;

	public BitunixFuturesWsClient(string publicEndpoint, string privateEndpoint,
		SecureString key, SecureString secret, WorkingTime workingTime, int reconnectAttempts)
	{
		_publicEndpoint = publicEndpoint.ThrowIfEmpty(nameof(publicEndpoint));
		_privateEndpoint = privateEndpoint.ThrowIfEmpty(nameof(privateEndpoint));
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secretKey = secret.IsEmpty() ? null : secret.UnSecure();
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Bitunix) + "_FuturesWs";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && !_secretKey.IsEmpty();

	public event Func<string, BitunixWsTicker, long, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, BitunixWsPrice, long, CancellationToken, ValueTask> PriceReceived;
	public event Func<string, string, BitunixWsDepth, long, CancellationToken, ValueTask> DepthReceived;
	public event Func<string, BitunixWsTrade, long, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, string, BitunixWsCandle, long, CancellationToken, ValueTask> CandleReceived;
	public event Func<BitunixWsOrder, long, CancellationToken, ValueTask> OrderReceived;
	public event Func<BitunixWsBalance, long, CancellationToken, ValueTask> BalanceReceived;
	public event Func<BitunixWsPosition, long, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_publicClient?.Dispose();
		_privateClient?.Dispose();
		_connectionSync.Dispose();
		_publicSendGate.Dispose();
		_privateSendGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			if (_publicClient is not null || _privateClient is not null)
				throw new InvalidOperationException("Bitunix futures WebSocket is already initialized.");

			_publicClient = CreateClient(_publicEndpoint, false);
			await _publicClient.ConnectAsync(cancellationToken);

			if (IsCredentialsAvailable)
			{
				_privateClient = CreateClient(_privateEndpoint, true);
				await _privateClient.ConnectAsync(cancellationToken);
			}
		}
		catch
		{
			await DisposeSocketsAsync(cancellationToken);
			throw;
		}
		finally
		{
			_connectionSync.Release();
		}
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			await DisposeSocketsAsync(cancellationToken);
		}
		finally
		{
			_connectionSync.Release();
		}
	}

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new("ticker", symbol), true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new("ticker", symbol), false, cancellationToken);

	public ValueTask SubscribePriceAsync(string symbol, CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new("price", symbol), true, cancellationToken);

	public ValueTask UnsubscribePriceAsync(string symbol, CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new("price", symbol), false, cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new($"depth_book{depth}", symbol), true,
			cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new($"depth_book{depth}", symbol), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new("trade", symbol), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new("trade", symbol), false, cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new($"last_kline_{interval}", symbol), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangePublicSubscriptionAsync(new($"last_kline_{interval}", symbol), false,
			cancellationToken);

	public async ValueTask SetPrivateSubscriptionsAsync(bool orders, bool positions,
		bool balances, CancellationToken cancellationToken)
	{
		if (!IsCredentialsAvailable)
		{
			if (orders || positions || balances)
				throw new InvalidOperationException(
					"Bitunix API key and secret are required for private WebSocket channels.");
			return;
		}

		using (_sync.EnterScope())
		{
			SetPrivateChannel("order", orders);
			SetPrivateChannel("position", positions);
			SetPrivateChannel("balance", balances);
		}
		if (_isPrivateLoggedIn)
			await SynchronizePrivateSubscriptionsAsync(cancellationToken);
	}

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		if (_publicClient is { IsConnected: true } publicClient)
			await SendPublicAsync(publicClient, new BitunixWsPing { Time = time }, cancellationToken);
		if (_privateClient is { IsConnected: true } privateClient)
			await SendPrivateAsync(privateClient, new BitunixWsPing { Time = time }, cancellationToken);
	}

	private void SetPrivateChannel(string channel, bool isEnabled)
	{
		if (isEnabled)
			_privateSubscriptions.Add(channel);
		else
			_privateSubscriptions.Remove(channel);
	}

	private async ValueTask DisposeSocketsAsync(CancellationToken cancellationToken)
	{
		var publicClient = _publicClient;
		var privateClient = _privateClient;
		_publicClient = null;
		_privateClient = null;
		_isPrivateLoggedIn = false;
		_isPrivateLoginPending = false;
		using (_sync.EnterScope())
			_sentPrivateSubscriptions.Clear();

		if (privateClient is not null)
		{
			try
			{
				if (privateClient.IsConnected)
					await privateClient.DisconnectAsync(cancellationToken);
			}
			finally
			{
				privateClient.Dispose();
			}
		}

		if (publicClient is not null)
		{
			try
			{
				if (publicClient.IsConnected)
					await publicClient.DisconnectAsync(cancellationToken);
			}
			finally
			{
				publicClient.Dispose();
			}
		}
	}

	private ValueTask ChangePublicSubscriptionAsync(BitunixWsSubscriptionKey subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = isSubscribe
				? _publicSubscriptions.Add(subscription)
				: _publicSubscriptions.Remove(subscription);
		if (!shouldSend || _publicClient?.IsConnected != true)
			return default;
		return SendPublicSubscriptionAsync(_publicClient, subscription, isSubscribe,
			cancellationToken);
	}

	private WebSocketClient CreateClient(string endpoint, bool isPrivate)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			endpoint,
			(state, token) => OnStateChangedAsync(client, isPrivate, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, isPrivate, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Bitunix-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client, bool isPrivate,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (isPrivate)
		{
			if (state == ConnectionStates.Disconnected)
			{
				_isPrivateLoggedIn = false;
				_isPrivateLoginPending = false;
				using (_sync.EnterScope())
					_sentPrivateSubscriptions.Clear();
			}
			return;
		}

		if (state == ConnectionStates.Restored)
		{
			try
			{
				BitunixWsSubscriptionKey[] subscriptions;
				using (_sync.EnterScope())
					subscriptions = [.. _publicSubscriptions];
				foreach (var subscription in subscriptions)
					await SendPublicSubscriptionAsync(client, subscription, true, cancellationToken);
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask SendPublicSubscriptionAsync(WebSocketClient client,
		BitunixWsSubscriptionKey subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendPublicAsync(client, new BitunixWsCommand<BitunixWsSubscription>
		{
			Operation = isSubscribe ? "subscribe" : "unsubscribe",
			Arguments =
			[
				new()
				{
					Channel = subscription.Channel,
					Symbol = subscription.Symbol,
				},
			],
		}, cancellationToken);

	private async ValueTask SendLoginAsync(CancellationToken cancellationToken)
	{
		if (_isPrivateLoggedIn || _isPrivateLoginPending)
			return;
		var client = _privateClient;
		if (client?.IsConnected != true)
			return;
		_isPrivateLoginPending = true;
		var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var digest = Sha256(nonce + timestamp.ToString(CultureInfo.InvariantCulture) + _apiKey);
		var signature = Sha256(digest + _secretKey);
		await SendPrivateAsync(client, new BitunixWsCommand<BitunixWsLogin>
		{
			Operation = "login",
			Arguments =
			[
				new()
				{
					ApiKey = _apiKey,
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
		var client = _privateClient;
		if (client?.IsConnected != true || !_isPrivateLoggedIn)
			return;

		string[] subscribe;
		string[] unsubscribe;
		using (_sync.EnterScope())
		{
			subscribe = [.. _privateSubscriptions.Except(_sentPrivateSubscriptions,
				StringComparer.OrdinalIgnoreCase)];
			unsubscribe = [.. _sentPrivateSubscriptions.Except(_privateSubscriptions,
				StringComparer.OrdinalIgnoreCase)];
		}

		foreach (var channel in subscribe)
		{
			await SendPrivateSubscriptionAsync(client, channel, true, cancellationToken);
			using (_sync.EnterScope())
				_sentPrivateSubscriptions.Add(channel);
		}
		foreach (var channel in unsubscribe)
		{
			await SendPrivateSubscriptionAsync(client, channel, false, cancellationToken);
			using (_sync.EnterScope())
				_sentPrivateSubscriptions.Remove(channel);
		}
	}

	private ValueTask SendPrivateSubscriptionAsync(WebSocketClient client, string channel,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendPrivateAsync(client, new BitunixWsCommand<BitunixWsPrivateSubscription>
		{
			Operation = isSubscribe ? "subscribe" : "unsubscribe",
			Arguments = [new() { Channel = channel }],
		}, cancellationToken);

	private ValueTask SendPublicAsync<T>(WebSocketClient client, T payload,
		CancellationToken cancellationToken)
		=> SendAsync(client, payload, _publicSendGate, cancellationToken);

	private ValueTask SendPrivateAsync<T>(WebSocketClient client, T payload,
		CancellationToken cancellationToken)
		=> SendAsync(client, payload, _privateSendGate, cancellationToken);

	private static async ValueTask SendAsync<T>(WebSocketClient client, T payload,
		SendGate gate, CancellationToken cancellationToken)
	{
		await gate.Semaphore.WaitAsync(cancellationToken);
		try
		{
			var delay = gate.NextTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			await client.SendAsync(payload, cancellationToken);
			gate.NextTime = DateTime.UtcNow.AddMilliseconds(220);
		}
		finally
		{
			gate.Semaphore.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		bool isPrivate, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<BitunixWsHeader>(payload);
			if (!header.Operation.IsEmpty())
			{
				await ProcessOperationAsync(header.Operation, payload, isPrivate, cancellationToken);
				return;
			}
			if (header.Channel.IsEmpty())
				return;

			switch (header.Channel)
			{
				case "ticker":
				{
					var data = Deserialize<BitunixWsEnvelope<BitunixWsTicker>>(payload);
					if (data.Data is not null && TickerReceived is { } handler)
						await handler(data.Symbol.IsEmpty(data.Data.Symbol), data.Data, data.Time,
							cancellationToken);
					break;
				}
				case "price":
				{
					var data = Deserialize<BitunixWsEnvelope<BitunixWsPrice>>(payload);
					if (data.Data is not null && PriceReceived is { } handler)
						await handler(data.Symbol, data.Data, data.Time, cancellationToken);
					break;
				}
				case "trade":
				{
					var data = Deserialize<BitunixWsEnvelope<BitunixWsTrade[]>>(payload);
					if (TradeReceived is { } handler)
					{
						foreach (var trade in data.Data ?? [])
							await handler(data.Symbol, trade, data.Time, cancellationToken);
					}
					break;
				}
				case "order":
				{
					var data = Deserialize<BitunixWsEnvelope<BitunixWsOrder>>(payload);
					if (data.Data is not null && OrderReceived is { } handler)
						await handler(data.Data, data.Time, cancellationToken);
					break;
				}
				case "balance":
				{
					var data = Deserialize<BitunixWsEnvelope<BitunixWsBalance>>(payload);
					if (data.Data is not null && BalanceReceived is { } handler)
						await handler(data.Data, data.Time, cancellationToken);
					break;
				}
				case "position":
				{
					var data = Deserialize<BitunixWsEnvelope<BitunixWsPosition>>(payload);
					if (data.Data is not null && PositionReceived is { } handler)
						await handler(data.Data, data.Time, cancellationToken);
					break;
				}
				default:
					if (header.Channel.StartsWith("depth_book", StringComparison.OrdinalIgnoreCase))
					{
						var data = Deserialize<BitunixWsEnvelope<BitunixWsDepth>>(payload);
						if (data.Data is not null && DepthReceived is { } depthHandler)
							await depthHandler(data.Symbol, data.Channel, data.Data, data.Time,
								cancellationToken);
					}
					else if (header.Channel.Contains("_kline_", StringComparison.OrdinalIgnoreCase))
					{
						var data = Deserialize<BitunixWsEnvelope<BitunixWsCandle>>(payload);
						if (data.Data is not null && CandleReceived is { } candleHandler)
							await candleHandler(data.Symbol, data.Channel, data.Data, data.Time,
								cancellationToken);
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

	private async ValueTask ProcessOperationAsync(string operation, string payload,
		bool isPrivate, CancellationToken cancellationToken)
	{
		if (operation.EqualsIgnoreCase("ping"))
			return;
		if (operation.EqualsIgnoreCase("connect"))
		{
			if (isPrivate)
				await SendLoginAsync(cancellationToken);
			return;
		}
		if (operation.EqualsIgnoreCase("login"))
		{
			var response = Deserialize<BitunixWsOperation>(payload);
			_isPrivateLoginPending = false;
			if (response.Data?.Result == false)
				throw new InvalidOperationException(
					$"Bitunix private WebSocket login failed: {response.Message}".Trim());
			_isPrivateLoggedIn = true;
			using (_sync.EnterScope())
				_sentPrivateSubscriptions.Clear();
			await SynchronizePrivateSubscriptionsAsync(cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Bitunix WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string Sha256(string value)
		=> Convert.ToHexString(SHA256.HashData(value.UTF8())).ToLowerInvariant();
}
