namespace StockSharp.KotakNeo.Native;

sealed class KotakNeoMarketDataClient : BaseLogReceiver
{
	private const string _url = "wss://mlhsm.kotaksecurities.com";
	private const int _maxSubscriptions = 3000;
	private const int _maxPerChannel = 200;
	private const int _maxPerRequest = 100;

	private readonly WebSocketClient _client;
	private readonly string _token;
	private readonly string _sid;
	private readonly SynchronizedDictionary<string, (string topic, int channel)> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<int, KotakNeoFeedState> _states = [];
	private int _acknowledgementFrequency;
	private int _updatesSinceAcknowledgement;
	private bool _authenticated;

	public KotakNeoMarketDataClient(KotakNeoSession session, int reconnectAttempts, WorkingTime workingTime)
	{
		if (session == null)
			throw new ArgumentNullException(nameof(session));
		_token = session.Token.ThrowIfEmpty(nameof(session.Token));
		_sid = session.Sid.ThrowIfEmpty(nameof(session.Sid));

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

	public override string Name => nameof(KotakNeo) + "_" + nameof(KotakNeoMarketDataClient);

	public event Func<KotakNeoMarketUpdate, CancellationToken, ValueTask> UpdateReceived;
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

	public async ValueTask SetSubscription(string instrumentKey, KotakNeoFeedKinds kind, bool subscribe,
		CancellationToken cancellationToken)
	{
		var (exchangeSegment, token) = instrumentKey.ParseInstrumentKey();
		var prefix = kind switch
		{
			KotakNeoFeedKinds.Scrip => "sf",
			KotakNeoFeedKinds.Index => "if",
			KotakNeoFeedKinds.Depth => "dp",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
		};
		var key = $"{prefix}|{instrumentKey}";

		if (subscribe)
		{
			if (_subscriptions.ContainsKey(key))
				return;
			if (_subscriptions.Count >= _maxSubscriptions)
				throw new InvalidOperationException($"Kotak Neo allows at most {_maxSubscriptions} market-feed subscriptions per connection.");

			var channel = AllocateChannel();
			var topic = $"{prefix}|{exchangeSegment}|{token}";
			_subscriptions[key] = (topic, channel);
			if (_authenticated)
				await SendSubscription(true, channel, [topic], cancellationToken);
		}
		else if (_subscriptions.TryGetValue(key, out var current))
		{
			if (_authenticated)
				await SendSubscription(false, current.channel, [current.topic], cancellationToken);
			_subscriptions.Remove(key);
		}
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_authenticated = false;
		_states.Clear();
		_acknowledgementFrequency = 0;
		_updatesSinceAcknowledgement = 0;
		return _client.SendAsync(CreateConnection(_token, _sid), WebSocketMessageType.Binary, cancellationToken);
	}

	private async ValueTask Resubscribe(CancellationToken cancellationToken)
	{
		foreach (var group in _subscriptions.ToArray().GroupBy(p => p.Value.channel))
		{
			var topics = group.Select(p => p.Value.topic).ToArray();
			for (var i = 0; i < topics.Length; i += _maxPerRequest)
				await SendSubscription(true, group.Key, topics.Skip(i).Take(_maxPerRequest).ToArray(), cancellationToken);
		}
	}

	private ValueTask SendSubscription(bool subscribe, int channel, string[] topics, CancellationToken cancellationToken)
		=> topics.Length == 0
			? default
			: _client.SendAsync(CreateSubscription(subscribe, channel, topics), WebSocketMessageType.Binary, cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory.ToArray();
		if (data.Length == 0)
			return;
		if (data[0] is (byte)'{' or (byte)'[')
			throw new InvalidDataException($"Unexpected Kotak Neo HSM response: {message.AsString()}");

		var result = Decode(data);
		if (result.Acknowledgement != null)
			await _client.SendAsync(result.Acknowledgement, WebSocketMessageType.Binary, cancellationToken);
		if (result.Connected && !_authenticated)
		{
			_authenticated = true;
			await Resubscribe(cancellationToken);
		}
		if (UpdateReceived is not { } handler)
			return;
		foreach (var update in result.Updates)
			await handler(update, cancellationToken);
	}

	private KotakNeoFeedDecodeResult Decode(ReadOnlySpan<byte> data)
	{
		Ensure(data, 4, "header");
		var responseType = data[2];
		if (responseType == 1)
			return DecodeConnection(data);
		if (responseType is 4 or 5 or 9)
		{
			DecodeStatus(data, responseType);
			return new();
		}
		if (responseType != 6)
			return new();

		var offset = 3;
		uint messageNumber = 0;
		if (_acknowledgementFrequency > 0)
		{
			Ensure(data, offset + 4, "message number");
			messageNumber = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
			offset += 4;
		}

		Ensure(data, offset + 2, "topic count");
		var count = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
		offset += 2;
		var updates = new List<KotakNeoMarketUpdate>(count);
		for (var i = 0; i < count; i++)
		{
			Ensure(data, offset + 3, "topic item");
			offset += 2;
			var dataType = data[offset++];
			KotakNeoFeedState state;
			switch (dataType)
			{
				case 83:
					state = DecodeSnapshot(data, ref offset);
					break;
				case 85:
					state = DecodeUpdate(data, ref offset);
					break;
				default:
					throw new InvalidDataException($"Kotak Neo HSM returned unsupported data type {dataType}.");
			}

			if (state != null)
				updates.Add(ToUpdate(state));
		}

		byte[] acknowledgement = null;
		if (_acknowledgementFrequency > 0 && ++_updatesSinceAcknowledgement >= _acknowledgementFrequency)
		{
			acknowledgement = CreateAcknowledgement(messageNumber);
			_updatesSinceAcknowledgement = 0;
		}

		return new() { Acknowledgement = acknowledgement, Updates = [.. updates] };
	}

	private KotakNeoFeedDecodeResult DecodeConnection(ReadOnlySpan<byte> data)
	{
		var offset = 3;
		Ensure(data, offset + 1, "connection field count");
		var count = data[offset++];
		string status = null;
		for (var i = 0; i < count; i++)
		{
			ReadField(data, ref offset, out var id, out var value);
			if (id == 1)
				status = Encoding.UTF8.GetString(value);
			else if (id == 2 && value.Length > 0)
				_acknowledgementFrequency = checked((int)ReadUnsigned(value));
		}

		if (status != "K")
			throw new InvalidOperationException("Kotak Neo HSM authentication failed.");
		return new() { Connected = true };
	}

	private static void DecodeStatus(ReadOnlySpan<byte> data, byte responseType)
	{
		var offset = 3;
		Ensure(data, offset + 1, "status field count");
		var count = data[offset++];
		string status = null;
		for (var i = 0; i < count; i++)
		{
			ReadField(data, ref offset, out var id, out var value);
			if (id == 1)
				status = Encoding.UTF8.GetString(value);
		}
		if (status != "K")
			throw new InvalidOperationException($"Kotak Neo HSM request type {responseType} failed.");
	}

	private KotakNeoFeedState DecodeSnapshot(ReadOnlySpan<byte> data, ref int offset)
	{
		Ensure(data, offset + 5, "snapshot topic");
		var topicId = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
		offset += 4;
		var topic = ReadString(data, ref offset);
		var parts = topic.Split('|');
		var kind = parts.FirstOrDefault() switch
		{
			"if" => KotakNeoFeedKinds.Index,
			"dp" => KotakNeoFeedKinds.Depth,
			_ => KotakNeoFeedKinds.Scrip,
		};
		var state = new KotakNeoFeedState
		{
			Kind = kind,
			Topic = topic,
			ExchangeSegment = parts.Length > 1 ? parts[1] : null,
			Token = parts.Length > 2 ? parts[2] : null,
		};

		Ensure(data, offset + 1, "snapshot numeric field count");
		var numericCount = data[offset++];
		ReadValues(data, ref offset, state, numericCount);
		Ensure(data, offset + 1, "snapshot string field count");
		var stringCount = data[offset++];
		for (var i = 0; i < stringCount; i++)
		{
			Ensure(data, offset + 2, "snapshot string field");
			var fieldId = data[offset++];
			var value = ReadString(data, ref offset);
			switch (fieldId)
			{
				case 52: state.Token = value; break;
				case 53: state.ExchangeSegment = value; break;
				case 54: state.TradingSymbol = value; break;
			}
		}

		var multiplierIndex = kind == KotakNeoFeedKinds.Scrip ? 23 : kind == KotakNeoFeedKinds.Index ? 8 : 32;
		var precisionIndex = kind == KotakNeoFeedKinds.Scrip ? 24 : kind == KotakNeoFeedKinds.Index ? 9 : 33;
		if (state.HasValues[multiplierIndex] && state.Values[multiplierIndex] > 0)
			state.Multiplier = state.Values[multiplierIndex];
		if (state.HasValues[precisionIndex] && state.Values[precisionIndex] is >= 0 and <= 12)
			state.Precision = state.Values[precisionIndex];
		_states[topicId] = state;
		return state;
	}

	private KotakNeoFeedState DecodeUpdate(ReadOnlySpan<byte> data, ref int offset)
	{
		Ensure(data, offset + 5, "update topic");
		var topicId = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
		offset += 4;
		var fieldCount = data[offset++];
		if (!_states.TryGetValue(topicId, out var state))
		{
			Ensure(data, offset + fieldCount * 4, "unknown update fields");
			offset += fieldCount * 4;
			return null;
		}
		ReadValues(data, ref offset, state, fieldCount);
		return state;
	}

	private static void ReadValues(ReadOnlySpan<byte> data, ref int offset, KotakNeoFeedState state, int count)
	{
		Ensure(data, offset + count * 4, "numeric fields");
		for (var i = 0; i < count; i++)
		{
			var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
			offset += 4;
			if (i >= state.Values.Length || value == int.MinValue)
				continue;
			state.Values[i] = value;
			state.HasValues[i] = true;
		}
	}

	private static KotakNeoMarketUpdate ToUpdate(KotakNeoFeedState state)
	{
		var scale = (decimal)Math.Max(1, state.Multiplier);
		for (var i = 0; i < state.Precision; i++)
			scale *= 10m;

		decimal? value(int index, bool price = false)
			=> index >= 0 && index < state.Values.Length && state.HasValues[index]
				? price ? state.Values[index] / scale : state.Values[index]
				: null;

		DateTime time(int index)
			=> value(index) is > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)value(index).Value).UtcDateTime : DateTime.UtcNow;

		if (state.Kind == KotakNeoFeedKinds.Depth)
		{
			var bids = new List<KotakNeoDepthLevel>(5);
			var asks = new List<KotakNeoDepthLevel>(5);
			for (var i = 0; i < 5; i++)
			{
				var bidPrice = value(2 + i, true);
				var askPrice = value(7 + i, true);
				if (bidPrice is > 0)
					bids.Add(new() { Price = bidPrice.Value, Volume = value(12 + i) ?? 0, OrdersCount = value(22 + i)?.To<int>() });
				if (askPrice is > 0)
					asks.Add(new() { Price = askPrice.Value, Volume = value(17 + i) ?? 0, OrdersCount = value(27 + i)?.To<int>() });
			}
			var update = CreateBase(state, time(0), null);
			update.Bids = [.. bids.OrderByDescending(l => l.Price)];
			update.Asks = [.. asks.OrderBy(l => l.Price)];
			return update;
		}

		if (state.Kind == KotakNeoFeedKinds.Index)
		{
			var update = CreateBase(state, time(4), null);
			update.LastPrice = value(2, true);
			update.ClosePrice = value(3, true);
			update.HighPrice = value(5, true);
			update.LowPrice = value(6, true);
			update.OpenPrice = value(7, true);
			return update;
		}

		var bid = value(9, true);
		var ask = value(10, true);
		var stockUpdate = CreateBase(state, time(2), value(3) is > 0 ? time(3) : null);
		stockUpdate.LastPrice = value(5, true);
		stockUpdate.LastVolume = value(6);
		stockUpdate.Volume = value(4);
		stockUpdate.TotalBuyVolume = value(7);
		stockUpdate.TotalSellVolume = value(8);
		stockUpdate.BestBidPrice = bid;
		stockUpdate.BestAskPrice = ask;
		stockUpdate.BestBidVolume = value(11);
		stockUpdate.BestAskVolume = value(12);
		stockUpdate.AveragePrice = value(13, true);
		stockUpdate.LowPrice = value(14, true);
		stockUpdate.HighPrice = value(15, true);
		stockUpdate.LowerCircuit = value(16, true);
		stockUpdate.UpperCircuit = value(17, true);
		stockUpdate.YearHigh = value(18, true);
		stockUpdate.YearLow = value(19, true);
		stockUpdate.OpenPrice = value(20, true);
		stockUpdate.ClosePrice = value(21, true);
		stockUpdate.OpenInterest = value(22);
		stockUpdate.Bids = bid is > 0 ? [new() { Price = bid.Value, Volume = value(11) ?? 0 }] : [];
		stockUpdate.Asks = ask is > 0 ? [new() { Price = ask.Value, Volume = value(12) ?? 0 }] : [];
		return stockUpdate;
	}

	private static KotakNeoMarketUpdate CreateBase(KotakNeoFeedState state, DateTime serverTime, DateTime? lastTradeTime)
		=> new()
		{
			FeedType = state.Kind.ToString(),
			ExchangeSegment = state.ExchangeSegment,
			Token = state.Token,
			TradingSymbol = state.TradingSymbol,
			ServerTime = serverTime,
			LastTradeTime = lastTradeTime,
		};

	private int AllocateChannel()
	{
		for (var channel = 2; channel <= 16; channel++)
		{
			if (_subscriptions.Values.Count(v => v.channel == channel) < _maxPerChannel)
				return channel;
		}
		throw new InvalidOperationException("Kotak Neo market-feed channels are full.");
	}

	private static byte[] CreateConnection(string token, string sid)
	{
		var tokenBytes = Encoding.UTF8.GetBytes(token);
		var sidBytes = Encoding.UTF8.GetBytes(sid);
		var sourceBytes = Encoding.ASCII.GetBytes("JS_API");
		var data = new byte[13 + tokenBytes.Length + sidBytes.Length + sourceBytes.Length];
		var offset = 0;
		WriteUInt16(data, ref offset, data.Length - 2);
		data[offset++] = 1;
		data[offset++] = 3;
		WriteField(data, ref offset, 1, tokenBytes);
		WriteField(data, ref offset, 2, sidBytes);
		WriteField(data, ref offset, 3, sourceBytes);
		return data;
	}

	private static byte[] CreateSubscription(bool subscribe, int channel, string[] topics)
	{
		if (topics.Length > _maxPerRequest)
			throw new InvalidOperationException($"Kotak Neo allows at most {_maxPerRequest} instruments in one subscription request.");
		var topicBytes = topics.Select(Encoding.UTF8.GetBytes).ToArray();
		if (topicBytes.Any(t => t.Length > byte.MaxValue))
			throw new InvalidOperationException("Kotak Neo HSM topic is too long.");
		var topicsLength = 2 + topicBytes.Sum(t => 1 + t.Length);
		var data = new byte[11 + topicsLength];
		var offset = 0;
		WriteUInt16(data, ref offset, data.Length - 2);
		data[offset++] = subscribe ? (byte)4 : (byte)5;
		data[offset++] = 2;
		data[offset++] = 1;
		WriteUInt16(data, ref offset, topicsLength);
		WriteUInt16(data, ref offset, topicBytes.Length);
		foreach (var topic in topicBytes)
		{
			data[offset++] = (byte)topic.Length;
			topic.CopyTo(data, offset);
			offset += topic.Length;
		}
		data[offset++] = 2;
		WriteUInt16(data, ref offset, 1);
		data[offset] = checked((byte)channel);
		return data;
	}

	private static byte[] CreateAcknowledgement(uint messageNumber)
	{
		var data = new byte[11];
		var offset = 0;
		WriteUInt16(data, ref offset, data.Length - 2);
		data[offset++] = 3;
		data[offset++] = 1;
		data[offset++] = 1;
		WriteUInt16(data, ref offset, 4);
		BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), messageNumber);
		return data;
	}

	private static void WriteField(byte[] data, ref int offset, byte id, byte[] value)
	{
		data[offset++] = id;
		WriteUInt16(data, ref offset, value.Length);
		value.CopyTo(data, offset);
		offset += value.Length;
	}

	private static void WriteUInt16(byte[] data, ref int offset, int value)
	{
		if (value is < 0 or > ushort.MaxValue)
			throw new InvalidOperationException($"Kotak Neo HSM field length {value} is out of range.");
		BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), (ushort)value);
		offset += 2;
	}

	private static void ReadField(ReadOnlySpan<byte> data, ref int offset, out byte id, out ReadOnlySpan<byte> value)
	{
		Ensure(data, offset + 3, "response field");
		id = data[offset++];
		var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
		offset += 2;
		Ensure(data, offset + length, "response field value");
		value = data.Slice(offset, length);
		offset += length;
	}

	private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
	{
		Ensure(data, offset + 1, "string length");
		var length = data[offset++];
		Ensure(data, offset + length, "string value");
		var value = Encoding.UTF8.GetString(data.Slice(offset, length));
		offset += length;
		return value;
	}

	private static ulong ReadUnsigned(ReadOnlySpan<byte> data)
	{
		if (data.Length > 8)
			throw new InvalidDataException("Kotak Neo HSM integer field is too long.");
		ulong value = 0;
		foreach (var current in data)
			value = (value << 8) | current;
		return value;
	}

	private static void Ensure(ReadOnlySpan<byte> data, int requiredLength, string part)
	{
		if (requiredLength < 0 || data.Length < requiredLength)
			throw new InvalidDataException($"Kotak Neo HSM {part} is truncated.");
	}
}
