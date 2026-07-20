namespace StockSharp.Aevo.Native;

sealed class AevoSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly AevoAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, bool> _subscriptions =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public AevoSocketClient(string endpoint, AevoAuthenticator authenticator,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.NormalizeSocketEndpoint(nameof(endpoint));
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Aevo_WS";

	public event Func<AevoTickerData, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<AevoOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<AevoTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<AevoPositionsData, CancellationToken, ValueTask>
		PositionsReceived;
	public event Func<AevoOrdersData, CancellationToken, ValueTask>
		OrdersReceived;
	public event Func<AevoFillData, CancellationToken, ValueTask> FillReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Aevo WebSocket is already initialized.");
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

	public ValueTask SubscribeAsync(string channel, bool isPrivate,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, isPrivate, true, cancellationToken);

	public ValueTask UnsubscribeAsync(string channel,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, false, false, cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected == true)
			await SendAsync(client, new AevoSocketRequest { Operation = "ping" },
				cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(_endpoint.ToString(),
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a), static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += socket => socket.Options.DangerousDeflateOptions = new();
		return client;
	}

	private async ValueTask ChangeSubscriptionAsync(string channel,
		bool isPrivate, bool isSubscribe, CancellationToken cancellationToken)
	{
		channel = channel.ThrowIfEmpty(nameof(channel)).Trim();
		bool changed;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
				changed = _subscriptions.TryAdd(channel, isPrivate);
			else
			{
				changed = _subscriptions.TryGetValue(channel,
					out var registeredPrivate);
				if (changed)
				{
					isPrivate = registeredPrivate;
					_subscriptions.Remove(channel);
				}
			}
		}
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, channel, isPrivate, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(channel);
				else
					_subscriptions[channel] = isPrivate;
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		string channel, bool isPrivate, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var operation = isSubscribe ? "subscribe" : "unsubscribe";
		var channels = new[] { channel };
		return SendAsync(client, new()
		{
			Operation = operation,
			Data = channels,
			Authentication = isPrivate
				? _authenticator.CreateSocketAuth(operation,
					JsonConvert.SerializeObject(channels, _settings))
				: null,
		}, cancellationToken);
	}

	private async ValueTask SendAsync(WebSocketClient client,
		AevoSocketRequest request, CancellationToken cancellationToken)
	{
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<AevoSocketHeader>(payload);
			if (!header.Error.IsEmpty())
				throw new AevoApiException(
					"Aevo WebSocket error: " + header.Error);
			var channel = header.Channel;
			if (channel.IsEmpty())
				return;
			if (channel.StartsWith("ticker-", StringComparison.Ordinal))
				await RaiseAsync(TickerReceived,
					Deserialize<AevoTickerEnvelope>(payload).Data,
					cancellationToken);
			else if (channel.StartsWith("orderbook-", StringComparison.Ordinal))
				await RaiseAsync(OrderBookReceived,
					Deserialize<AevoOrderBookEnvelope>(payload).Data,
					cancellationToken);
			else if (channel.StartsWith("trades:", StringComparison.Ordinal))
				await RaiseAsync(TradeReceived,
					Deserialize<AevoTradeEnvelope>(payload).Data,
					cancellationToken);
			else if (channel == "positions")
				await RaiseAsync(PositionsReceived,
					Deserialize<AevoPositionsEnvelope>(payload).Data,
					cancellationToken);
			else if (channel == "orders")
				await RaiseAsync(OrdersReceived,
					Deserialize<AevoOrdersEnvelope>(payload).Data,
					cancellationToken);
			else if (channel == "fills")
				await RaiseAsync(FillReceived,
					Deserialize<AevoFillEnvelope>(payload).Data,
					cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process an Aevo WebSocket message.", error),
				cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _settings) ??
				throw new InvalidDataException(
					"Aevo returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Aevo returned malformed WebSocket JSON.", error);
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			KeyValuePair<string, bool>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var (channel, isPrivate) in subscriptions)
				await SendSubscriptionAsync(client, channel, isPrivate, true,
					cancellationToken);
		}
		await RaiseAsync(StateChanged, state, cancellationToken);
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
		}
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
		=> handler is null || value is null
			? default
			: handler(value, cancellationToken);

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
