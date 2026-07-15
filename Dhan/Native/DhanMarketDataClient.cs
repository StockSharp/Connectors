namespace StockSharp.Dhan.Native;

sealed class DhanMarketDataClient : BaseLogReceiver
{
	private const string _url = "wss://api-feed.dhan.co";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly SynchronizedDictionary<string, DhanFeedModes> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

	public DhanMarketDataClient(string clientId, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		clientId.ThrowIfEmpty(nameof(clientId));
		token.ThrowIfEmpty(nameof(token));
		var url = $"{_url}?version=2&token={Uri.EscapeDataString(token)}&clientId={Uri.EscapeDataString(clientId)}&authType=2";

		_client = new(
			url,
			(state, cancellationToken) => StateChanged is { } stateHandler ? stateHandler(state, cancellationToken) : default,
			(error, cancellationToken) => Error is { } errorHandler ? errorHandler(error, cancellationToken) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Dhan) + "_" + nameof(DhanMarketDataClient);

	public event Func<DhanMarketTick, CancellationToken, ValueTask> TickReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();

	public async ValueTask SetSubscription(string instrumentKey, DhanFeedModes? mode, CancellationToken cancellationToken)
	{
		if (_subscriptions.TryGetValue(instrumentKey, out var currentMode))
		{
			if (currentMode == mode)
				return;
			_subscriptions.Remove(instrumentKey);
			await Send(false, currentMode, [instrumentKey], cancellationToken);
		}

		if (mode == null)
			return;
		if (_subscriptions.Count >= 5000)
			throw new InvalidOperationException("Dhan allows at most 5000 instruments on one market-feed connection.");

		_subscriptions[instrumentKey] = mode.Value;
		await Send(true, mode.Value, [instrumentKey], cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		foreach (var group in _subscriptions.ToArray().GroupBy(p => p.Value))
		{
			var keys = group.Select(p => p.Key).ToArray();
			for (var i = 0; i < keys.Length; i += 100)
				await Send(true, group.Key, keys.Skip(i).Take(100).ToArray(), cancellationToken);
		}
	}

	private ValueTask Send(bool subscribe, DhanFeedModes mode, string[] instrumentKeys, CancellationToken cancellationToken)
	{
		var instruments = instrumentKeys.Select(key =>
		{
			var (boardCode, securityId) = key.ParseInstrumentKey();
			return new DhanSubscriptionInstrument { ExchangeSegment = boardCode, SecurityId = securityId };
		}).ToArray();

		return _client.SendAsync(JsonConvert.SerializeObject(new DhanSubscriptionRequest
		{
			RequestCode = (int)mode + (subscribe ? 0 : 1),
			InstrumentCount = instruments.Length,
			Instruments = instruments,
		}, _jsonSettings), cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory;
		if (data.IsEmpty)
			return;

		if (data.Span[0] == (byte)'{')
			throw new InvalidDataException($"Unexpected Dhan market-feed response: {message.AsString()}");

		var offset = 0;
		while (offset < data.Length)
		{
			var packet = data.Span[offset..];
			if (packet.Length < 8)
				throw new InvalidDataException("Dhan market-feed packet header is incomplete.");

			var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(1, 2));
			if (length < 8 || length > packet.Length)
				throw new InvalidDataException($"Dhan market-feed packet has invalid length {length}.");

			var tick = Decode(packet[..length]);
			if (tick != null && TickReceived is { } handler)
				await handler(tick, cancellationToken);
			offset += length;
		}
	}

	private static DhanMarketTick Decode(ReadOnlySpan<byte> data)
	{
		var responseCode = data[0];
		var exchangeSegment = (DhanExchangeSegments)data[3];
		var securityId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4)).ToString(CultureInfo.InvariantCulture);
		var tick = new DhanMarketTick
		{
			ExchangeSegment = exchangeSegment,
			SecurityId = securityId,
			ServerTime = DateTime.UtcNow,
		};

		switch (responseCode)
		{
			case 2:
				EnsureLength(data, 16, "ticker");
				tick.LastPrice = ReadSingle(data, 8);
				tick.LastTradeTime = FromEpoch(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4)));
				tick.ServerTime = tick.LastTradeTime ?? tick.ServerTime;
				break;

			case 4:
				EnsureLength(data, 50, "quote");
				DecodeQuote(data, tick, 8);
				break;

			case 5:
				EnsureLength(data, 12, "open-interest");
				tick.OpenInterest = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
				break;

			case 6:
				EnsureLength(data, 16, "previous-close");
				tick.ClosePrice = ReadSingle(data, 8);
				tick.OpenInterest = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
				break;

			case 7:
				return null;

			case 8:
				EnsureLength(data, 162, "full");
				DecodeFull(data, tick);
				break;

			case 50:
				EnsureLength(data, 10, "disconnect");
				var reason = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
				throw new InvalidOperationException($"Dhan market feed disconnected with reason {reason}.");

			default:
				return null;
		}

		return tick;
	}

	private static void DecodeQuote(ReadOnlySpan<byte> data, DhanMarketTick tick, int offset)
	{
		tick.LastPrice = ReadSingle(data, offset);
		tick.LastVolume = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 4, 2));
		tick.LastTradeTime = FromEpoch(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 6, 4)));
		tick.ServerTime = tick.LastTradeTime ?? tick.ServerTime;
		tick.AveragePrice = ReadSingle(data, offset + 10);
		tick.Volume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 14, 4));
		tick.TotalSellVolume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 18, 4));
		tick.TotalBuyVolume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 22, 4));
		tick.OpenPrice = ReadSingle(data, offset + 26);
		tick.ClosePrice = ReadSingle(data, offset + 30);
		tick.HighPrice = ReadSingle(data, offset + 34);
		tick.LowPrice = ReadSingle(data, offset + 38);
	}

	private static void DecodeFull(ReadOnlySpan<byte> data, DhanMarketTick tick)
	{
		tick.LastPrice = ReadSingle(data, 8);
		tick.LastVolume = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2));
		tick.LastTradeTime = FromEpoch(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(14, 4)));
		tick.ServerTime = tick.LastTradeTime ?? tick.ServerTime;
		tick.AveragePrice = ReadSingle(data, 18);
		tick.Volume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(22, 4));
		tick.TotalSellVolume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(26, 4));
		tick.TotalBuyVolume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(30, 4));
		tick.OpenInterest = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(34, 4));
		tick.OpenPrice = ReadSingle(data, 46);
		tick.ClosePrice = ReadSingle(data, 50);
		tick.HighPrice = ReadSingle(data, 54);
		tick.LowPrice = ReadSingle(data, 58);

		var bids = new List<DhanDepthLevel>(5);
		var asks = new List<DhanDepthLevel>(5);
		for (var i = 0; i < 5; i++)
		{
			var offset = 62 + i * 20;
			var bidPrice = ReadSingle(data, offset + 12);
			var askPrice = ReadSingle(data, offset + 16);
			if (bidPrice > 0)
			{
				bids.Add(new DhanDepthLevel
				{
					Price = bidPrice,
					Volume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)),
					OrdersCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 8, 2)),
				});
			}
			if (askPrice > 0)
			{
				asks.Add(new DhanDepthLevel
				{
					Price = askPrice,
					Volume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4)),
					OrdersCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 10, 2)),
				});
			}
		}

		tick.Bids = [.. bids.OrderByDescending(l => l.Price)];
		tick.Asks = [.. asks.OrderBy(l => l.Price)];
	}

	private static decimal ReadSingle(ReadOnlySpan<byte> data, int offset)
		=> Convert.ToDecimal(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4))), CultureInfo.InvariantCulture);

	private static DateTime? FromEpoch(uint value)
		=> value == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;

	private static void EnsureLength(ReadOnlySpan<byte> data, int expected, string packetName)
	{
		if (data.Length < expected)
			throw new InvalidDataException($"Dhan {packetName} packet has {data.Length} bytes; expected at least {expected}.");
	}
}
