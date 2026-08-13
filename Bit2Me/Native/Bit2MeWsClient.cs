namespace StockSharp.Bit2Me.Native;

sealed class Bit2MeWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(string Channel, string Symbol);

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

	public Bit2MeWsClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Bit2Me_Ws";

	public event Func<string, Bit2MeOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<string, Bit2MeWsTrade, CancellationToken, ValueTask>
		TradeReceived;
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
				"Bit2Me WebSocket is already initialized.");
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

	public ValueTask SubscribeOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("order-book",
			symbol.NormalizeSymbol()), true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("order-book",
			symbol.NormalizeSymbol()), false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("public-trades",
			symbol.NormalizeSymbol()), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("public-trades",
			symbol.NormalizeSymbol()), false, cancellationToken);

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
				"StockSharp-Bit2Me-Connector/1.0");
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
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
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
			await client.SendAsync(new Bit2MeWsSubscriptionCommand
			{
				Event = isSubscribe ? "subscribe" : "unsubscribe",
				Symbol = subscription.Symbol,
				Subscription = new() { Name = subscription.Channel },
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
			var header = Deserialize<Bit2MeWsHeader>(payload);
			if (!header.Error.IsEmpty())
				throw new InvalidDataException(
					$"Bit2Me WebSocket error: {header.Error}");
			if (header.Event.EqualsIgnoreCase("subscribe") ||
				header.Event.EqualsIgnoreCase("unsubscribe"))
			{
				if (header.Result.IsEmpty())
					throw new InvalidDataException(
						"Bit2Me returned an invalid subscription response.");
				return;
			}
			if (header.Event.EqualsIgnoreCase("order-book"))
			{
				var envelope =
					Deserialize<Bit2MeWsEnvelope<Bit2MeOrderBook>>(payload);
				if (envelope.Data is not null && OrderBookReceived is { } handler)
					await handler.InvokeAsync(envelope.Symbol.IsEmpty(envelope.Data.Symbol),
						envelope.Data, cancellationToken);
				return;
			}
			if (header.Event.EqualsIgnoreCase("public-trades"))
			{
				var envelope = Deserialize<Bit2MeWsEnvelope<Bit2MeWsTrade>>(
					payload);
				if (envelope.Data is not null && TradeReceived is { } handler)
					await handler.InvokeAsync(envelope.Symbol, envelope.Data,
						cancellationToken);
				return;
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	internal static TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload,
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				})
				?? throw new InvalidDataException(
					"Bit2Me WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bit2Me WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;
}
