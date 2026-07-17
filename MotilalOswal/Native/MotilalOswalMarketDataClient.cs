namespace StockSharp.MotilalOswal.Native;

sealed class MotilalOswalMarketDataClient : BaseLogReceiver
{
	private const string _url = "wss://ws1feed.motilaloswal.com/jwebsocket/jwebsocket";
	private const int _packetLength = 30;
	private const string _protocolVersion = "VER 2.0";

	private readonly WebSocketClient _client;
	private readonly string _clientCode;
	private readonly int _maximumSubscriptions;
	private readonly SynchronizedDictionary<string, bool> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _wireExchanges = new(StringComparer.OrdinalIgnoreCase);
	private byte[] _pending = [];

	public MotilalOswalMarketDataClient(string clientCode, int maximumSubscriptions,
		int reconnectAttempts, WorkingTime workingTime)
	{
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		if (Encoding.ASCII.GetByteCount(_clientCode) > 15)
			throw new ArgumentOutOfRangeException(nameof(clientCode), clientCode, "The MO market-feed client code is limited to 15 ASCII bytes.");
		_maximumSubscriptions = maximumSubscriptions > 0 ? maximumSubscriptions : 200;

		_client = new(
			_url,
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

	public override string Name => nameof(MotilalOswal) + "_" + nameof(MotilalOswalMarketDataClient);

	public event Func<MotilalOswalMarketUpdate, CancellationToken, ValueTask> UpdateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public ValueTask Disconnect(CancellationToken cancellationToken) => _client.DisconnectAsync(cancellationToken);

	public async ValueTask Subscribe(string instrumentKey, bool isIndex, CancellationToken cancellationToken)
	{
		if (_subscriptions.ContainsKey(instrumentKey))
			return;
		if (!isIndex && _subscriptions.Count(p => !p.Value) >= _maximumSubscriptions)
			throw new InvalidOperationException($"The Motilal Oswal market-feed limit of {_maximumSubscriptions} instruments has been reached.");

		var (exchange, scripCode) = instrumentKey.ParseInstrumentKey();
		_subscriptions.Add(instrumentKey, isIndex);
		_wireExchanges[ToWireKey(exchange.ToWireExchange(), scripCode)] = exchange;
		if (isIndex)
			await _client.SendAsync(CreateLoginPacket(), WebSocketMessageType.Binary, cancellationToken);
		else
			await SendSubscription(exchange, scripCode, true, cancellationToken);
	}

	public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryGetAndRemove(instrumentKey, out var isIndex))
			return;

		var (exchange, scripCode) = instrumentKey.ParseInstrumentKey();
		_wireExchanges.Remove(ToWireKey(exchange.ToWireExchange(), scripCode));
		if (!isIndex)
			await SendSubscription(exchange, scripCode, false, cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_pending = [];
		await _client.SendAsync(CreateLoginPacket(), WebSocketMessageType.Binary, cancellationToken);
		foreach (var subscription in _subscriptions.ToArray())
		{
			if (subscription.Value)
				continue;
			var (exchange, scripCode) = subscription.Key.ParseInstrumentKey();
			await SendSubscription(exchange, scripCode, true, cancellationToken);
		}
	}

	private async ValueTask SendSubscription(string exchange, long scripCode, bool subscribe, CancellationToken cancellationToken)
	{
		if (scripCode is <= 0 or > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(scripCode), scripCode, "The MO broadcast protocol uses a signed 32-bit scrip code.");

		var packet = new byte[10];
		packet[0] = (byte)'D';
		BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1, 2), 7);
		packet[3] = (byte)exchange.ToWireExchange();
		packet[4] = (byte)exchange.ToWireExchangeType();
		BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5, 4), (int)scripCode);
		packet[9] = subscribe ? (byte)1 : (byte)0;
		await _client.SendAsync(CreateLoginPacket(), WebSocketMessageType.Binary, cancellationToken);
		await _client.SendAsync(packet, WebSocketMessageType.Binary, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var memory = message.Memory;
		if (memory.IsEmpty)
			return;

		if (memory.Span[0] == (byte)'{')
		{
			var error = JsonConvert.DeserializeObject<MotilalOswalStatusResponse>(message.AsString());
			if (error != null && !error.ErrorCode.IsEmpty())
				throw new InvalidOperationException($"Motilal Oswal market-feed error {error.ErrorCode}: {error.Message}");
			return;
		}

		var data = new byte[_pending.Length + memory.Length];
		_pending.CopyTo(data, 0);
		memory.CopyTo(data.AsMemory(_pending.Length));

		var completeLength = data.Length - data.Length % _packetLength;
		for (var offset = 0; offset < completeLength; offset += _packetLength)
		{
			var packet = data.AsSpan(offset, _packetLength);
			if ((char)packet[9] == '1')
			{
				await _client.SendAsync([(byte)'1', 0, 0], WebSocketMessageType.Binary, cancellationToken);
				continue;
			}

			var update = Decode(packet);
			if (update != null && UpdateReceived is { } handler)
				await handler(update, cancellationToken);
		}

		_pending = data[completeLength..];
		if (_pending.Length > _packetLength)
			throw new InvalidDataException("The Motilal Oswal market-feed remainder exceeded one packet.");
	}

	private MotilalOswalMarketUpdate Decode(ReadOnlySpan<byte> packet)
	{
		var exchangeCode = (char)packet[0];
		var scripCode = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(1, 4));
		var seconds = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(5, 4));
		var wireKey = ToWireKey(exchangeCode, scripCode);
		var exchange = _wireExchanges.TryGetValue(wireKey, out var subscribedExchange)
			? subscribedExchange
			: exchangeCode.FromWireExchange(scripCode);
		DateTime serverTime;
		try
		{
			serverTime = seconds.FromMotilalSeconds();
		}
		catch (ArgumentOutOfRangeException)
		{
			serverTime = DateTime.UtcNow;
		}

		var update = new MotilalOswalMarketUpdate
		{
			Exchange = exchange,
			ScripCode = scripCode,
			ServerTime = serverTime,
		};

		switch ((char)packet[9])
		{
			case 'A':
				update.MessageType = MotilalOswalMarketMessageTypes.LastTrade;
				update.LastPrice = ReadSingle(packet, 10);
				update.LastQuantity = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(14, 4));
				update.CumulativeQuantity = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(18, 4));
				update.AveragePrice = ReadSingle(packet, 22);
				update.OpenInterest = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(26, 4));
				break;

			case >= 'B' and <= 'F':
				update.MessageType = MotilalOswalMarketMessageTypes.Depth;
				update.DepthLevel = packet[9] - (byte)'B' + 1;
				update.BidPrice = ReadSingle(packet, 10);
				update.BidQuantity = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(14, 4));
				update.BidOrders = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(18, 2));
				update.AskPrice = ReadSingle(packet, 20);
				update.AskQuantity = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(24, 4));
				update.AskOrders = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(28, 2));
				break;

			case 'G':
				update.MessageType = MotilalOswalMarketMessageTypes.DayOhlc;
				update.OpenPrice = ReadSingle(packet, 10);
				update.HighPrice = ReadSingle(packet, 14);
				update.LowPrice = ReadSingle(packet, 18);
				update.PreviousClose = ReadSingle(packet, 22);
				break;

			case 'W':
				update.MessageType = MotilalOswalMarketMessageTypes.CircuitLimits;
				update.UpperCircuit = ReadSingle(packet, 10);
				update.LowerCircuit = ReadSingle(packet, 14);
				break;

			case 'm':
				update.MessageType = MotilalOswalMarketMessageTypes.OpenInterest;
				update.OpenInterest = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(10, 4));
				update.OpenInterestHigh = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(14, 4));
				update.OpenInterestLow = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(18, 4));
				break;

			case 'H':
				update.MessageType = MotilalOswalMarketMessageTypes.Index;
				update.LastPrice = ReadSingle(packet, 10);
				break;

			default:
				return null;
		}

		return update;
	}

	private byte[] CreateLoginPacket()
	{
		var packet = Enumerable.Repeat((byte)' ', 114).ToArray();
		packet[0] = (byte)'Q';
		BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1, 2), 111);
		var clientBytes = Encoding.ASCII.GetBytes(_clientCode);
		packet[3] = (byte)clientBytes.Length;
		clientBytes.CopyTo(packet, 4);
		packet[19] = (byte)clientBytes.Length;
		clientBytes.CopyTo(packet, 20);
		packet[50] = 1;
		packet[51] = 1;
		packet[52] = 1;
		var versionBytes = Encoding.ASCII.GetBytes(_protocolVersion);
		packet[53] = (byte)versionBytes.Length;
		versionBytes.CopyTo(packet, 54);
		packet[64] = 0;
		packet[65] = 0;
		packet[66] = 0;
		packet[67] = 0;
		packet[68] = 1;
		return packet;
	}

	private static decimal ReadSingle(ReadOnlySpan<byte> data, int offset)
	{
		var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));
		return float.IsFinite(value) ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0;
	}

	private static string ToWireKey(char exchange, long scripCode)
		=> $"{exchange}|{scripCode.ToString(CultureInfo.InvariantCulture)}";
}
