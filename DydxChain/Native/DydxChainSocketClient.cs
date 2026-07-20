namespace StockSharp.DydxChain.Native;

sealed class DydxChainSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<DydxChainSocketSubscriptionKey> _subscriptions = [];
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
	private long _pingId;

	public DydxChainSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "dYdX_WS";

	public event Func<DydxChainMarketsResponse, CancellationToken, ValueTask>
		MarketsSnapshotReceived;
	public event Func<DydxChainMarketUpdate, CancellationToken, ValueTask>
		MarketsUpdateReceived;
	public event Func<string, DydxChainOrderbookResponse, CancellationToken,
		ValueTask> OrderbookSnapshotReceived;
	public event Func<string, DydxChainOrderbookUpdate, CancellationToken,
		ValueTask> OrderbookUpdateReceived;
	public event Func<string, DydxChainTrade[], CancellationToken, ValueTask>
		TradesReceived;
	public event Func<string, DydxChainCandle[], CancellationToken, ValueTask>
		CandlesReceived;
	public event Func<DydxChainSubaccountSnapshot, CancellationToken, ValueTask>
		SubaccountSnapshotReceived;
	public event Func<DydxChainSubaccountUpdate, CancellationToken, ValueTask>
		SubaccountUpdateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"dYdX WebSocket is already initialized.");
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

	public ValueTask SubscribeAsync(DydxChainSocketSubscriptionKey key,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(key, true, cancellationToken);

	public ValueTask UnsubscribeAsync(DydxChainSocketSubscriptionKey key,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(key, false, cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> SendAsync(new DydxChainSocketPingRequest
		{
			Id = Interlocked.Increment(ref _pingId),
		}, cancellationToken);

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
			"StockSharp-dYdX-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			DydxChainSocketSubscriptionKey[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		DydxChainSocketSubscriptionKey key, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		ValidateSubscription(key);
		using (_sync.EnterScope())
			if (isSubscribe
				? !_subscriptions.Add(key)
				: !_subscriptions.Remove(key))
				return;
		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, key, isSubscribe,
				cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		DydxChainSocketSubscriptionKey key, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new DydxChainSocketSubscriptionRequest
		{
			Type = isSubscribe
				? DydxChainSocketRequestTypes.Subscribe
				: DydxChainSocketRequestTypes.Unsubscribe,
			Channel = key.Channel,
			Id = key.Id,
			IsBatched = isSubscribe &&
				key.Channel != DydxChainSocketChannels.Subaccounts
					? true
					: null,
		}, cancellationToken);

	private ValueTask SendAsync<TPayload>(TPayload payload,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"dYdX WebSocket is not connected.");
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

	private async ValueTask SendRawAsync(WebSocketClient client,
		string payload, CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(Encoding.UTF8.GetBytes(payload),
				WebSocketMessageType.Text, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		if (payload.Equals("PING", StringComparison.Ordinal))
		{
			await SendRawAsync(client, "PONG", cancellationToken);
			return;
		}
		try
		{
			var header = Deserialize<DydxChainSocketHeader>(payload);
			switch (header.Type)
			{
				case DydxChainSocketMessageTypes.Connected:
				case DydxChainSocketMessageTypes.Unsubscribed:
				case DydxChainSocketMessageTypes.Pong:
					return;
				case DydxChainSocketMessageTypes.Error:
					throw new InvalidOperationException(
						"dYdX WebSocket error: " +
						(header.Message.IsEmpty() ? payload : header.Message));
				case DydxChainSocketMessageTypes.Subscribed:
					await ProcessSnapshotAsync(header, payload,
						cancellationToken);
					return;
				case DydxChainSocketMessageTypes.ChannelData:
					await ProcessUpdateAsync(header, payload, false,
						cancellationToken);
					return;
				case DydxChainSocketMessageTypes.ChannelBatchData:
					await ProcessUpdateAsync(header, payload, true,
						cancellationToken);
					return;
				default:
					throw new InvalidDataException(
						$"Unsupported dYdX WebSocket message type " +
						$"'{header.Type}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessSnapshotAsync(DydxChainSocketHeader header,
		string payload, CancellationToken cancellationToken)
	{
		switch (RequireChannel(header))
		{
			case DydxChainSocketChannels.Markets:
				await RaiseAsync(MarketsSnapshotReceived,
					Deserialize<DydxChainSocketEnvelope<
						DydxChainMarketsResponse>>(payload).Contents,
					cancellationToken);
				break;
			case DydxChainSocketChannels.Orderbook:
				if (OrderbookSnapshotReceived is { } bookHandler)
					await bookHandler(RequireId(header),
						Deserialize<DydxChainSocketEnvelope<
							DydxChainOrderbookResponse>>(payload).Contents,
						cancellationToken);
				break;
			case DydxChainSocketChannels.Trades:
				if (TradesReceived is { } tradesHandler)
					await tradesHandler(RequireId(header),
						Deserialize<DydxChainSocketEnvelope<
							DydxChainTradesResponse>>(payload).Contents.Trades ?? [],
						cancellationToken);
				break;
			case DydxChainSocketChannels.Candles:
				if (CandlesReceived is { } candlesHandler)
					await candlesHandler(RequireId(header),
						Deserialize<DydxChainSocketEnvelope<
							DydxChainCandlesResponse>>(payload).Contents.Candles ?? [],
						cancellationToken);
				break;
			case DydxChainSocketChannels.Subaccounts:
				await RaiseAsync(SubaccountSnapshotReceived,
					Deserialize<DydxChainSocketEnvelope<
						DydxChainSubaccountSnapshot>>(payload).Contents,
					cancellationToken);
				break;
			default:
				throw new InvalidDataException(
					"Unsupported dYdX snapshot channel.");
		}
	}

	private async ValueTask ProcessUpdateAsync(DydxChainSocketHeader header,
		string payload, bool isBatch, CancellationToken cancellationToken)
	{
		var channel = RequireChannel(header);
		if (!isBatch)
		{
			switch (channel)
			{
				case DydxChainSocketChannels.Markets:
					await RaiseAsync(MarketsUpdateReceived,
						Deserialize<DydxChainSocketEnvelope<
							DydxChainMarketUpdate>>(payload).Contents,
						cancellationToken);
					break;
				case DydxChainSocketChannels.Orderbook:
					if (OrderbookUpdateReceived is { } bookHandler)
						await bookHandler(RequireId(header),
							Deserialize<DydxChainSocketEnvelope<
								DydxChainOrderbookUpdate>>(payload).Contents,
							cancellationToken);
					break;
				case DydxChainSocketChannels.Trades:
					if (TradesReceived is { } tradesHandler)
						await tradesHandler(RequireId(header),
							Deserialize<DydxChainSocketEnvelope<
								DydxChainTradesResponse>>(payload).Contents.Trades ?? [],
							cancellationToken);
					break;
				case DydxChainSocketChannels.Candles:
					if (CandlesReceived is { } candlesHandler)
						await candlesHandler(RequireId(header),
							[Deserialize<DydxChainSocketEnvelope<
								DydxChainCandle>>(payload).Contents],
							cancellationToken);
					break;
				case DydxChainSocketChannels.Subaccounts:
					await RaiseAsync(SubaccountUpdateReceived,
						Deserialize<DydxChainSocketEnvelope<
							DydxChainSubaccountUpdate>>(payload).Contents,
						cancellationToken);
					break;
				default:
					throw new InvalidDataException(
						"Unsupported dYdX update channel.");
			}
			return;
		}

		switch (channel)
		{
			case DydxChainSocketChannels.Markets:
				foreach (var item in Deserialize<DydxChainSocketBatchEnvelope<
					DydxChainMarketUpdate>>(payload).Contents ?? [])
					await RaiseAsync(MarketsUpdateReceived, item,
						cancellationToken);
				break;
			case DydxChainSocketChannels.Orderbook:
				if (OrderbookUpdateReceived is { } bookHandler)
					foreach (var item in Deserialize<
						DydxChainSocketBatchEnvelope<
							DydxChainOrderbookUpdate>>(payload).Contents ?? [])
						await bookHandler(RequireId(header), item,
							cancellationToken);
				break;
			case DydxChainSocketChannels.Trades:
				if (TradesReceived is { } tradesHandler)
					foreach (var item in Deserialize<
						DydxChainSocketBatchEnvelope<
							DydxChainTradesResponse>>(payload).Contents ?? [])
						await tradesHandler(RequireId(header), item.Trades ?? [],
							cancellationToken);
				break;
			case DydxChainSocketChannels.Candles:
				if (CandlesReceived is { } candlesHandler)
					await candlesHandler(RequireId(header),
						Deserialize<DydxChainSocketBatchEnvelope<
							DydxChainCandle>>(payload).Contents ?? [],
						cancellationToken);
				break;
			case DydxChainSocketChannels.Subaccounts:
				foreach (var item in Deserialize<DydxChainSocketBatchEnvelope<
					DydxChainSubaccountUpdate>>(payload).Contents ?? [])
					await RaiseAsync(SubaccountUpdateReceived, item,
						cancellationToken);
				break;
			default:
				throw new InvalidDataException(
					"Unsupported dYdX batch channel.");
		}
	}

	private T Deserialize<T>(string payload)
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"dYdX WebSocket returned an empty message.");

	private static DydxChainSocketChannels RequireChannel(
		DydxChainSocketHeader header)
		=> header.Channel ?? throw new InvalidDataException(
			"dYdX WebSocket message has no channel.");

	private static string RequireId(DydxChainSocketHeader header)
		=> header.Id.ThrowIfEmpty("dYdX WebSocket subscription ID");

	private static ValueTask RaiseAsync<T>(
		Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> value is not null && handler is not null
			? handler(value, cancellationToken)
			: default;

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

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

	private static void ValidateSubscription(
		DydxChainSocketSubscriptionKey key)
	{
		if (key.Channel == DydxChainSocketChannels.Markets)
		{
			if (!key.Id.IsEmpty())
				throw new ArgumentException(
					"dYdX markets subscription must not contain an ID.",
					nameof(key));
			return;
		}
		if (key.Id.IsEmpty())
			throw new ArgumentException(
				"dYdX subscription ID is required.", nameof(key));
		if (key.Channel is DydxChainSocketChannels.Orderbook or
			DydxChainSocketChannels.Trades)
			_ = key.Id.NormalizeTicker();
		else if (key.Channel == DydxChainSocketChannels.Candles)
		{
			var separator = key.Id.LastIndexOf('/');
			if (separator <= 0 || separator == key.Id.Length - 1)
				throw new ArgumentException(
					"dYdX candle subscription must use TICKER/RESOLUTION.",
					nameof(key));
			_ = key.Id[..separator].NormalizeTicker();
		}
		else if (key.Channel == DydxChainSocketChannels.Subaccounts)
		{
			var separator = key.Id.LastIndexOf('/');
			if (separator <= 0 || separator == key.Id.Length - 1 ||
				!int.TryParse(key.Id[(separator + 1)..],
					NumberStyles.None, CultureInfo.InvariantCulture,
					out var number) || number < 0)
				throw new ArgumentException(
					"dYdX subaccount subscription must use ADDRESS/NUMBER.",
					nameof(key));
			_ = key.Id[..separator].NormalizeAddress();
		}
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "wss://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss") || !uri.UserInfo.IsEmpty() ||
			(uri.Scheme == "ws" && !uri.IsLoopback))
			throw new ArgumentException(
				"dYdX WebSocket endpoint must use WSS, except for a local node.",
				nameof(endpoint));
		return endpoint;
	}

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
