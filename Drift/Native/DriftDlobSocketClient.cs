namespace StockSharp.Drift.Native;

sealed class DriftDlobSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, DriftDlobSubscribeRequest>
		_subscriptions = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public DriftDlobSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.NormalizeSocketEndpoint(nameof(endpoint));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Drift_DLOB_WS";

	public event Func<DriftDlobBook, CancellationToken, ValueTask> BookReceived;
	public event Func<DriftTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Drift DLOB WebSocket is already initialized.");
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

	public ValueTask SubscribeBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(Key(DriftDlobChannels.OrderBook, symbol),
			CreateRequest("subscribe", DriftDlobChannels.OrderBook, symbol), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(Key(DriftDlobChannels.OrderBook, symbol),
			CreateRequest("unsubscribe", DriftDlobChannels.OrderBook, symbol),
			false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(Key(DriftDlobChannels.Trades, symbol),
			CreateRequest("subscribe", DriftDlobChannels.Trades, symbol), true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(Key(DriftDlobChannels.Trades, symbol),
			CreateRequest("unsubscribe", DriftDlobChannels.Trades, symbol), false,
			cancellationToken);

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

	private async ValueTask ChangeSubscriptionAsync(string key,
		DriftDlobSubscribeRequest request, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		bool changed;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
				changed = _subscriptions.TryAdd(key, request);
			else
				changed = _subscriptions.Remove(key);
		}
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendAsync(_client, new DriftDlobSubscribeRequest
			{
				Type = isSubscribe ? "subscribe" : "unsubscribe",
				Market = request.Market,
				MarketType = request.MarketType,
				Channel = request.Channel,
				Grouping = request.Grouping,
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(key);
				else
					_subscriptions[key] = request;
			}
			throw;
		}
	}

	private async ValueTask SendAsync<T>(WebSocketClient client, T request,
		CancellationToken cancellationToken)
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
			var envelope = Deserialize<DriftDlobSocketEnvelope>(payload);
			if (!envelope.Error.IsEmpty())
				throw new DriftApiException(
					"Drift DLOB WebSocket error: " + envelope.Error);
			if (envelope.Channel.IsEmpty() || envelope.Channel == "heartbeat" ||
				envelope.Data.IsEmpty())
				return;
			if (envelope.Channel.StartsWith("orderbook_",
				StringComparison.Ordinal))
				await RaiseAsync(BookReceived,
					Deserialize<DriftDlobBook>(envelope.Data), cancellationToken);
			else if (envelope.Channel.StartsWith("trades_",
				StringComparison.Ordinal))
			{
				var data = envelope.Data.TrimStart();
				if (data.StartsWith('['))
					foreach (var trade in Deserialize<DriftTrade[]>(data))
						if (trade is not null)
							await RaiseAsync(TradeReceived, trade,
								cancellationToken);
				else
					await RaiseAsync(TradeReceived,
						Deserialize<DriftTrade>(data), cancellationToken);
			}
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Drift DLOB WebSocket message.", error),
				cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _settings) ??
				throw new InvalidDataException(
					"Drift returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Drift returned malformed WebSocket JSON.", error);
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			DriftDlobSubscribeRequest[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions.Values];
			foreach (var request in subscriptions)
				await SendAsync(client, request, cancellationToken);
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
		=> handler is null ? default : handler(value, cancellationToken);

	private static DriftDlobSubscribeRequest CreateRequest(string type,
		DriftDlobChannels channel, string symbol)
		=> new()
		{
			Type = type,
			Market = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant(),
			MarketType = DriftMarketTypes.Perpetual,
			Channel = channel,
			Grouping = channel == DriftDlobChannels.OrderBook ? "1" : null,
		};

	private static string Key(DriftDlobChannels channel, string symbol)
		=> channel + "|" + symbol.ThrowIfEmpty(nameof(symbol)).Trim()
			.ToUpperInvariant();

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
