namespace StockSharp.HdfcSecurities.Native;

sealed class HdfcSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly string _accessToken;
	private readonly SynchronizedSet<string> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);

	public HdfcSocketClient(
		Uri address,
		SecureString apiKey,
		SecureString accessToken,
		int reconnectAttempts,
		WorkingTime workingTime)
	{
		var endpoint = AddApiKey(
			address ?? throw new ArgumentNullException(nameof(address)),
			apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure());
		_accessToken = accessToken
			.ThrowIfEmpty(nameof(accessToken))
			.UnSecure();
		_client = new(
			endpoint.AbsoluteUri,
			(state, cancellationToken) =>
				StateChanged is { } stateHandler
					? stateHandler.InvokeAsync(state, cancellationToken)
					: default,
			(error, cancellationToken) =>
				Error is { } errorHandler
					? errorHandler.InvokeAsync(error, cancellationToken)
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
		_client.InitAsync += OnInit;
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(HdfcSecurities) + "_" +
		nameof(HdfcSocketClient);

	public event Func<HdfcMarketUpdate, CancellationToken, ValueTask>
		MarketDataReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_client.InitAsync -= OnInit;
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask Disconnect(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> _client.SendAsync(
			"{\"heart_beat\":true}",
			cancellationToken);

	public async ValueTask Subscribe(
		string streamId,
		CancellationToken cancellationToken)
	{
		streamId.ThrowIfEmpty(nameof(streamId));
		if (_subscriptions.Contains(streamId))
			return;
		if (_subscriptions.Count >= 1500)
		{
			throw new InvalidOperationException(
				"HDFC Securities allows at most 1500 instruments per WebSocket connection.");
		}
		_subscriptions.Add(streamId);
		try
		{
			await _client.SendAsync(
				CreateSubscriptionCommand(true, [streamId]),
				cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(streamId);
			throw;
		}
	}

	public async ValueTask Unsubscribe(
		string streamId,
		CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(streamId))
			return;
		await _client.SendAsync(
			CreateSubscriptionCommand(false, [streamId]),
			cancellationToken);
	}

	private ValueTask OnInit(
		ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		socket.Options.SetRequestHeader("Authorization", _accessToken);
		socket.Options.SetRequestHeader(
			"User-Agent",
			"StockSharp-HDFC-Securities/1.0");
		return default;
	}

	private async ValueTask OnPostConnect(
		bool reconnect,
		CancellationToken cancellationToken)
	{
		await SendHeartbeat(cancellationToken);
		foreach (var batch in _subscriptions.ToArray().Chunk(100))
		{
			await _client.SendAsync(
				CreateSubscriptionCommand(true, batch),
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
		if (data[0] is (byte)'{' or (byte)'[')
		{
			var text = message.AsString();
			if (text.Contains(
					"error",
					StringComparison.OrdinalIgnoreCase) ||
				text.Contains(
					"fail",
					StringComparison.OrdinalIgnoreCase) ||
				text.Contains(
					"invalid",
					StringComparison.OrdinalIgnoreCase))
			{
				if (Error is { } errorHandler)
				{
					await errorHandler.InvokeAsync(
						new InvalidOperationException(
							$"HDFC Securities WebSocket: {text}"),
						cancellationToken);
				}
			}
			return;
		}

		if (MarketDataReceived is not { } handler)
			return;
		foreach (var update in Decode(data, DateTime.UtcNow))
			await handler.InvokeAsync(update, cancellationToken);
	}

	internal static string CreateSubscriptionCommand(
		bool subscribe,
		IEnumerable<string> streamIds)
	{
		var ids = streamIds?
			.Where(id => !id.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray() ??
			throw new ArgumentNullException(nameof(streamIds));
		if (ids.Length == 0)
			throw new ArgumentOutOfRangeException(nameof(streamIds));
		var entries = new JArray(ids.Select(id => new JObject
		{
			["scripId"] = id,
			["type"] = "ALL",
		}));
		return new JObject
		{
			["heart_beat"] = false,
			["subscribe"] = subscribe ? entries : new JArray(),
			["unSubscribe"] = subscribe ? new JArray() : entries,
		}.ToString(Formatting.None);
	}

	internal static HdfcMarketUpdate[] Decode(
		byte[] payload,
		DateTime fallback)
	{
		if (payload == null || payload.Length == 0)
			return [];
		GenericDTOList list;
		try
		{
			list = GenericDTOList.Parser.ParseFrom(payload);
		}
		catch (InvalidProtocolBufferException error)
		{
			throw new InvalidDataException(
				"HDFC Securities WebSocket returned invalid protobuf data.",
				error);
		}

		var result = new List<HdfcMarketUpdate>();
		foreach (var item in list.GenericDTOList_)
		{
			if (item.PacketType == PacketType.Heartbeat)
				continue;
			var streamId = item.PacketType.ToStreamId(item.InstrumentId);
			if (streamId.IsEmpty())
				continue;
			var serverTime = item.PacketTimestamp.ToHdfcTime(fallback);
			if (item.MbpData != null)
			{
				var market = item.MbpData;
				result.Add(new()
				{
					StreamId = streamId,
					InstrumentId = item.InstrumentId,
					ServerTime = (market.LastTradeTime != 0
						? market.LastTradeTime
						: item.PacketTimestamp).ToHdfcTime(serverTime),
					LastPrice = (decimal)market.LastTradedPrice,
					LastQuantity = market.LastTradeQuantity,
					OpenPrice = (decimal)market.OpenPrice,
					HighPrice = (decimal)market.HighPrice,
					LowPrice = (decimal)market.LowPrice,
					PreviousClose = (decimal)market.ClosingPrice,
					Volume = market.VolumeTradedToday,
					AveragePrice = (decimal)market.AverageTradePrice,
					TotalBuyQuantity = market.TotalBuyQuantity,
					TotalSellQuantity = market.TotalSellQuantity,
					LowerLimit = (decimal)market.LowerCircuitLimit,
					UpperLimit = (decimal)market.UpperCircuitLimit,
					OpenInterest = market.Oi,
					Depth =
					[
						..
						market.MarketDepthDTOList?.MarketDepthDTO
							.Select(level => new HdfcDepthLevel
							{
								Price = (decimal)level.Price,
								Quantity = level.Quantity,
								Orders = level.NumberOfOrders,
								IsBid = level.BuyFlag,
							}) ?? []
					],
				});
			}
			else if (item.IndexData != null)
			{
				var index = item.IndexData;
				result.Add(new()
				{
					StreamId = streamId,
					InstrumentId = item.InstrumentId,
					ServerTime = index.PacketTimeStamp.ToHdfcTime(serverTime),
					LastPrice = (decimal)index.IndexValue,
					OpenPrice = (decimal)index.OpeningIndex,
					HighPrice = (decimal)index.HighIndexValue,
					LowPrice = (decimal)index.LowIndexValue,
					PreviousClose = (decimal)index.ClosingIndex,
				});
			}
		}
		return [.. result];
	}

	private static Uri AddApiKey(Uri address, string apiKey)
	{
		var builder = new UriBuilder(address);
		var query = builder.Query.TrimStart('?');
		builder.Query =
			(query.IsEmpty() ? string.Empty : query + "&") +
			$"api_key={Uri.EscapeDataString(apiKey)}";
		return builder.Uri;
	}
}
