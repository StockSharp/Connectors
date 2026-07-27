namespace StockSharp.Nubra.Native;

sealed class NubraMarketDataClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly string _token;
	private readonly SynchronizedSet<long> _subscriptions = [];

	public NubraMarketDataClient(
		Uri address,
		SecureString token,
		int reconnectAttempts,
		WorkingTime workingTime)
	{
		_token = token.ThrowIfEmpty(nameof(token)).UnSecure();
		_client = new(
			(address ?? throw new ArgumentNullException(nameof(address))).ToString(),
			(state, cancellationToken) =>
				StateChanged is { } stateHandler
					? stateHandler(state, cancellationToken)
					: default,
			(error, cancellationToken) =>
				Error is { } errorHandler
					? errorHandler(error, cancellationToken)
					: default,
			Process,
			(message, args) => this.AddInfoLog(message, args),
			(message, args) => this.AddErrorLog(message, args),
			(message, args) => this.AddVerboseLog(message, args))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Nubra) + "_" +
		nameof(NubraMarketDataClient);

	public event Func<NubraMarketUpdate, CancellationToken, ValueTask>
		MarketDataReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask Disconnect(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public async ValueTask Subscribe(
		long refId,
		CancellationToken cancellationToken)
	{
		if (_subscriptions.Contains(refId))
			return;
		_subscriptions.Add(refId);
		await _client.SendAsync(
			CreateSubscriptionCommand(true, _token, [refId]),
			cancellationToken);
	}

	public async ValueTask Unsubscribe(
		long refId,
		CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(refId))
			return;
		await _client.SendAsync(
			CreateSubscriptionCommand(false, _token, [refId]),
			cancellationToken);
	}

	private async ValueTask OnPostConnect(
		bool reconnect,
		CancellationToken cancellationToken)
	{
		await _client.SendAsync(
			$"batch_subscribe {_token} orderbook_depth 20",
			cancellationToken);
		foreach (var batch in _subscriptions.ToArray().Chunk(100))
		{
			await _client.SendAsync(
				CreateSubscriptionCommand(true, _token, batch),
				cancellationToken);
		}
	}

	private async ValueTask Process(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var data = message.Memory.ToArray();
		if (data.Length == 0)
			return;
		if (data[0] is >= 0x20 and <= 0x7e)
		{
			var text = message.AsString()?.Trim();
			if (text.IsEmpty() ||
				text.EqualsIgnoreCase("Pong") ||
				text.Contains("subscribed", StringComparison.OrdinalIgnoreCase))
				return;

			if (text.Contains("Invalid Token", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("Failed", StringComparison.OrdinalIgnoreCase))
			{
				if (Error is { } errorHandler)
				await errorHandler(new InvalidOperationException(
					$"Nubra WebSocket: {text}"), cancellationToken);
			}
			return;
		}

		if (MarketDataReceived is not { } handler)
			return;
		foreach (var update in Decode(data))
			await handler(update, cancellationToken);
	}

	internal static string CreateSubscriptionCommand(
		bool subscribe,
		string token,
		IEnumerable<long> refIds)
	{
		token.ThrowIfEmpty(nameof(token));
		var ids = refIds?.Distinct().ToArray() ??
			throw new ArgumentNullException(nameof(refIds));
		if (ids.Length == 0)
			throw new ArgumentOutOfRangeException(nameof(refIds));
		return
			$"batch_{(subscribe ? "subscribe" : "unsubscribe")} {token} " +
			$"orderbook {{\"instruments\":[{ids
				.Select(id => id.ToString(CultureInfo.InvariantCulture))
				.JoinComma()}]}}";
	}

	internal static NubraMarketUpdate[] Decode(byte[] payload)
	{
		if (payload == null || payload.Length == 0)
			return [];

		Any message;
		try
		{
			var outer = Any.Parser.ParseFrom(payload);
			if (outer.TypeUrl.EndsWith(
				nameof(BatchWebSocketOrderbookMessage),
				StringComparison.Ordinal))
			{
				message = outer;
			}
			else
			{
				message = Any.Parser.ParseFrom(outer.Value);
			}
		}
		catch (InvalidProtocolBufferException error)
		{
			throw new InvalidDataException(
				"Nubra WebSocket returned an invalid protobuf envelope.",
				error);
		}

		if (!message.TypeUrl.EndsWith(
			nameof(BatchWebSocketOrderbookMessage),
			StringComparison.Ordinal))
			return [];

		BatchWebSocketOrderbookMessage batch;
		try
		{
			batch = BatchWebSocketOrderbookMessage.Parser.ParseFrom(
				message.Value);
		}
		catch (InvalidProtocolBufferException error)
		{
			throw new InvalidDataException(
				"Nubra WebSocket returned an invalid order-book payload.",
				error);
		}

		return
		[
			..
				batch.Instruments.Select(item => new NubraMarketUpdate
				{
					RefId = item.RefId,
					Timestamp = item.Timestamp != 0
						? item.Timestamp
						: batch.Timestamp,
					LastPrice = item.Ltp,
					LastQuantity = item.Ltq,
					Volume = item.Volume,
					Bids =
					[
						..
							item.Bids.Select(level => new NubraDepthLevel
							{
								Price = level.Price,
								Quantity = level.Quantity,
								Orders = level.Orders,
							})
					],
					Asks =
					[
						..
							item.Asks.Select(level => new NubraDepthLevel
							{
								Price = level.Price,
								Quantity = level.Quantity,
								Orders = level.Orders,
							})
					],
				})
		];
	}
}
