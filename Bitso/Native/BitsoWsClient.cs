namespace StockSharp.Bitso.Native;

sealed class BitsoWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(BitsoWsChannels Channel, string Book);

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

	public BitsoWsClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Bitso_Ws";

	public event Func<string, BitsoWsTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<string, BitsoWsOrders, long, CancellationToken, ValueTask>
		OrdersReceived;
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
			throw new InvalidOperationException("Bitso WebSocket is already initialized.");
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

	public ValueTask SubscribeTradesAsync(string book,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitsoWsChannels.Trades,
			book.ThrowIfEmpty(nameof(book))), true, cancellationToken);

	public ValueTask ReleaseTradesAsync(string book,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitsoWsChannels.Trades,
			book.ThrowIfEmpty(nameof(book))), false, cancellationToken);

	public ValueTask SubscribeOrdersAsync(string book,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitsoWsChannels.Orders,
			book.ThrowIfEmpty(nameof(book))), true, cancellationToken);

	public ValueTask ReleaseOrdersAsync(string book,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitsoWsChannels.Orders,
			book.ThrowIfEmpty(nameof(book))), false, cancellationToken);

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
			"StockSharp-Bitso-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
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
				await SendSubscriptionAsync(client, subscription, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(Subscription subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var send = false;
		var client = _client;
		using (_sync.EnterScope())
		{
			if (!isSubscribe)
			{
				_desiredSubscriptions.Remove(subscription);
				return;
			}
			_desiredSubscriptions.Add(subscription);
			send = client?.IsConnected == true &&
				_serverSubscriptions.Add(subscription);
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(client, subscription, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_serverSubscriptions.Remove(subscription);
			throw;
		}
	}

	private async ValueTask SendSubscriptionAsync(WebSocketClient client,
		Subscription subscription, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_serverSubscriptions.Add(subscription);
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(new BitsoWsSubscriptionRequest
			{
				Action = BitsoWsActions.Subscribe,
				Book = subscription.Book,
				Channel = subscription.Channel,
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
			var header = Deserialize<BitsoWsHeader>(payload);
			if (header.Action == BitsoWsActions.Subscribe)
			{
				if (!header.Response.EqualsIgnoreCase("ok"))
					throw new InvalidDataException(
						$"Bitso rejected a WebSocket subscription: {header.Response}");
				return;
			}
			switch (header.Channel)
			{
				case BitsoWsChannels.KeepAlive:
					return;
				case BitsoWsChannels.Trades:
				{
					var envelope = Deserialize<BitsoWsEnvelope<BitsoWsTrade[]>>(payload);
					if (TradeReceived is { } handler)
						foreach (var trade in envelope.Payload ?? [])
							await handler(envelope.Book, trade, cancellationToken);
					return;
				}
				case BitsoWsChannels.Orders:
				{
					var envelope = Deserialize<BitsoWsEnvelope<BitsoWsOrders>>(payload);
					if (envelope.Payload is not null && OrdersReceived is { } handler)
						await handler(envelope.Book, envelope.Payload, envelope.Sent,
							cancellationToken);
					return;
				}
				default:
					throw new InvalidDataException(
						"Bitso WebSocket message has an unsupported channel.");
			}
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
					"Bitso WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitso WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
