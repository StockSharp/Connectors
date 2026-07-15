namespace StockSharp.Fyers.Native;

sealed class FyersMarketDataClient : BaseLogReceiver
{
	private const string _url = "wss://socket.fyers.in/hsm/v1-5/prod";
	private const string _source = "StockSharp-FYERS-v3";
	private const int _channel = 11;
	private const int _symbolLimit = 5000;

	private readonly WebSocketClient _client;
	private readonly string _accessToken;
	private readonly string _authorization;
	private readonly string _hsmKey;
	private readonly SynchronizedDictionary<string, FyersFeedSubscriptions> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FyersInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _topicSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<ushort, FyersFeedState> _states = [];
	private int _acknowledgementFrequency;
	private int _updatesSinceAcknowledgement;

	public FyersMarketDataClient(string clientId, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		clientId.ThrowIfEmpty(nameof(clientId));
		_accessToken = token.ThrowIfEmpty(nameof(token));
		_authorization = $"{clientId}:{_accessToken}";
		_hsmKey = DecodeToken(_accessToken);

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

	public override string Name => nameof(Fyers) + "_" + nameof(FyersMarketDataClient);

	public event Func<FyersMarketTick, CancellationToken, ValueTask> TickReceived;
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
	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> _client.SendAsync([0, 1, 11], WebSocketMessageType.Binary, cancellationToken);

	public async ValueTask SetSubscription(FyersInstrument instrument, FyersFeedSubscriptions subscriptions, CancellationToken cancellationToken)
	{
		if (instrument == null)
			throw new ArgumentNullException(nameof(instrument));

		_subscriptions.TryGetValue(instrument.Symbol, out var current);
		if (current == subscriptions)
			return;

		var count = _subscriptions.Values.Sum(CountFlags) - CountFlags(current) + CountFlags(subscriptions);
		if (count > _symbolLimit)
			throw new InvalidOperationException($"FYERS allows at most {_symbolLimit} HSM symbol subscriptions per connection.");

		_instruments[instrument.Symbol] = instrument;
		var removed = current & ~subscriptions;
		var added = subscriptions & ~current;
		if (removed.HasFlag(FyersFeedSubscriptions.Symbol))
			await SendSubscription(false, [RegisterTopic(instrument, false)], cancellationToken);
		if (removed.HasFlag(FyersFeedSubscriptions.Depth))
			await SendSubscription(false, [RegisterTopic(instrument, true)], cancellationToken);

		if (subscriptions == FyersFeedSubscriptions.None)
			_subscriptions.Remove(instrument.Symbol);
		else
			_subscriptions[instrument.Symbol] = subscriptions;

		if (added.HasFlag(FyersFeedSubscriptions.Symbol))
			await SendSubscription(true, [RegisterTopic(instrument, false)], cancellationToken);
		if (added.HasFlag(FyersFeedSubscriptions.Depth))
			await SendSubscription(true, [RegisterTopic(instrument, true)], cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_states.Clear();
		_acknowledgementFrequency = 0;
		_updatesSinceAcknowledgement = 0;
		await _client.SendAsync(CreateAuthentication(), WebSocketMessageType.Binary, cancellationToken);
		await _client.SendAsync(CreateFullMode(), WebSocketMessageType.Binary, cancellationToken);

		var topics = new List<string>();
		foreach (var pair in _subscriptions.ToArray())
		{
			if (!_instruments.TryGetValue(pair.Key, out var instrument))
				continue;
			if (pair.Value.HasFlag(FyersFeedSubscriptions.Symbol))
				topics.Add(RegisterTopic(instrument, false));
			if (pair.Value.HasFlag(FyersFeedSubscriptions.Depth))
				topics.Add(RegisterTopic(instrument, true));
		}

		for (var i = 0; i < topics.Count; i += 1500)
			await SendSubscription(true, topics.Skip(i).Take(1500).ToArray(), cancellationToken);
	}

	private string RegisterTopic(FyersInstrument instrument, bool isDepth)
	{
		var topic = instrument.ToHsmTopic(isDepth);
		_topicSymbols[topic] = instrument.Symbol;
		return topic;
	}

	private ValueTask SendSubscription(bool isSubscribe, string[] topics, CancellationToken cancellationToken)
		=> topics.Length == 0
			? default
			: _client.SendAsync(CreateSubscription(isSubscribe, topics), WebSocketMessageType.Binary, cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory.ToArray();
		if (data.Length == 0)
			return;
		if (data[0] == (byte)'{' || data[0] == (byte)'[')
			throw new InvalidDataException($"Unexpected FYERS HSM response: {message.AsString()}");

		var result = Decode(data);
		if (result.Acknowledgement != null)
			await _client.SendAsync(result.Acknowledgement, WebSocketMessageType.Binary, cancellationToken);
		if (TickReceived is not { } handler)
			return;
		foreach (var tick in result.Ticks)
			await handler(tick, cancellationToken);
	}

	private FyersFeedDecodeResult Decode(ReadOnlySpan<byte> data)
	{
		Ensure(data, 3, "header");
		var responseType = data[2];
		if (responseType == 1)
		{
			DecodeAuthentication(data);
			return new();
		}
		if (responseType != 6)
			return new();

		Ensure(data, 9, "data-feed header");
		var messageNumber = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(3, 4));
		byte[] acknowledgement = null;
		if (_acknowledgementFrequency > 0 && ++_updatesSinceAcknowledgement >= _acknowledgementFrequency)
		{
			acknowledgement = CreateAcknowledgement(messageNumber);
			_updatesSinceAcknowledgement = 0;
		}

		var count = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(7, 2));
		var offset = 9;
		var ticks = new List<FyersMarketTick>(count);
		for (var i = 0; i < count; i++)
		{
			Ensure(data, offset + 1, "data-feed item");
			var dataType = data[offset++];
			FyersFeedState state;
			switch (dataType)
			{
				case 83:
					state = DecodeSnapshot(data, ref offset);
					break;
				case 85:
					state = DecodeUpdate(data, ref offset);
					break;
				case 76:
					state = DecodeLiteUpdate(data, ref offset);
					break;
				default:
					throw new InvalidDataException($"FYERS HSM returned unsupported data type {dataType}.");
			}

			if (state != null)
				ticks.Add(ToTick(state));
		}

		return new FyersFeedDecodeResult { Acknowledgement = acknowledgement, Ticks = [.. ticks] };
	}

	private void DecodeAuthentication(ReadOnlySpan<byte> data)
	{
		var offset = 4;
		ReadField(data, ref offset, out _, out var status);
		if (status.Length != 1 || status[0] != (byte)'K')
			throw new InvalidOperationException("FYERS HSM authentication failed.");
		ReadField(data, ref offset, out _, out var acknowledgement);
		if (acknowledgement.Length >= 4)
			_acknowledgementFrequency = checked((int)BinaryPrimitives.ReadUInt32BigEndian(acknowledgement[..4]));
	}

	private FyersFeedState DecodeSnapshot(ReadOnlySpan<byte> data, ref int offset)
	{
		Ensure(data, offset + 3, "snapshot topic");
		var topicId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
		offset += 2;
		var topic = ReadString(data, ref offset);
		Ensure(data, offset + 1, "snapshot field count");
		var fieldCount = data[offset++];

		var kind = topic.StartsWith("dp|", StringComparison.Ordinal) ? FyersFeedKinds.Depth
			: topic.StartsWith("if|", StringComparison.Ordinal) ? FyersFeedKinds.Index
			: FyersFeedKinds.Symbol;
		var symbol = _topicSymbols.TryGetValue(topic, out var mappedSymbol) ? mappedSymbol : null;
		var state = new FyersFeedState
		{
			Kind = kind,
			Topic = topic,
			Symbol = symbol,
		};
		ReadValues(data, ref offset, state, fieldCount);

		Ensure(data, offset + 5, "snapshot scale");
		offset += 2;
		state.Multiplier = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
		offset += 2;
		state.Precision = data[offset++];
		var exchange = ReadString(data, ref offset);
		ReadString(data, ref offset);
		var wireSymbol = ReadString(data, ref offset);
		if (state.Symbol.IsEmpty())
			state.Symbol = wireSymbol.Contains(':') ? wireSymbol : $"{exchange}:{wireSymbol}";

		_states[topicId] = state;
		return state;
	}

	private FyersFeedState DecodeUpdate(ReadOnlySpan<byte> data, ref int offset)
	{
		Ensure(data, offset + 3, "update topic");
		var topicId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
		offset += 2;
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

	private FyersFeedState DecodeLiteUpdate(ReadOnlySpan<byte> data, ref int offset)
	{
		Ensure(data, offset + 6, "lite update");
		var topicId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
		offset += 2;
		var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
		offset += 4;
		if (!_states.TryGetValue(topicId, out var state))
			return null;
		if (value != int.MinValue)
		{
			state.Values[0] = value;
			state.HasValues[0] = true;
		}
		return state;
	}

	private static void ReadValues(ReadOnlySpan<byte> data, ref int offset, FyersFeedState state, int count)
	{
		Ensure(data, offset + count * 4, "data-feed fields");
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

	private static FyersMarketTick ToTick(FyersFeedState state)
	{
		decimal scale = state.Multiplier <= 0 ? 1 : state.Multiplier;
		for (var i = 0; i < state.Precision; i++)
			scale *= 10;

		decimal? value(int index, bool isPrice = false)
			=> index >= 0 && index < state.Values.Length && state.HasValues[index]
				? isPrice ? state.Values[index] / scale : state.Values[index]
				: null;

		DateTime serverTime(int index)
		{
			var seconds = value(index);
			return seconds is > 0 ? ((long)seconds.Value).FromUnix() : DateTime.UtcNow;
		}

		if (state.Kind == FyersFeedKinds.Depth)
		{
			var bids = new List<FyersDepthLevel>(5);
			var asks = new List<FyersDepthLevel>(5);
			for (var i = 0; i < 5; i++)
			{
				var depthBidPrice = value(i, true);
				var depthAskPrice = value(i + 5, true);
				if (depthBidPrice is > 0)
					bids.Add(new FyersDepthLevel { Price = depthBidPrice.Value, Volume = value(i + 10) ?? 0, OrdersCount = value(i + 20)?.To<int>(), Position = i + 1 });
				if (depthAskPrice is > 0)
					asks.Add(new FyersDepthLevel { Price = depthAskPrice.Value, Volume = value(i + 15) ?? 0, OrdersCount = value(i + 25)?.To<int>(), Position = i + 1 });
			}
			return new FyersMarketTick
			{
				Symbol = state.Symbol,
				IsDepth = true,
				ServerTime = DateTime.UtcNow,
				Bids = [.. bids.OrderByDescending(l => l.Price)],
				Asks = [.. asks.OrderBy(l => l.Price)],
			};
		}

		if (state.Kind == FyersFeedKinds.Index)
		{
			return new FyersMarketTick
			{
				Symbol = state.Symbol,
				ServerTime = serverTime(2),
				LastPrice = value(0, true),
				ClosePrice = value(1, true),
				HighPrice = value(3, true),
				LowPrice = value(4, true),
				OpenPrice = value(5, true),
			};
		}

		var bidPrice = value(6, true);
		var askPrice = value(7, true);
		return new FyersMarketTick
		{
			Symbol = state.Symbol,
			ServerTime = serverTime(3),
			LastTradeTime = value(2) is > 0 ? ((long)value(2).Value).FromUnix() : null,
			LastPrice = value(0, true),
			LastVolume = value(8),
			Volume = value(1),
			BidPrice = bidPrice,
			BidVolume = value(4),
			AskPrice = askPrice,
			AskVolume = value(5),
			TotalBuyVolume = value(9),
			TotalSellVolume = value(10),
			AveragePrice = value(11, true),
			OpenInterest = value(12),
			LowPrice = value(13, true),
			HighPrice = value(14, true),
			LowerCircuit = value(17, true),
			UpperCircuit = value(18, true),
			OpenPrice = value(19, true),
			ClosePrice = value(20, true),
			Bids = bidPrice is > 0 ? [new FyersDepthLevel { Price = bidPrice.Value, Volume = value(4) ?? 0 }] : [],
			Asks = askPrice is > 0 ? [new FyersDepthLevel { Price = askPrice.Value, Volume = value(5) ?? 0 }] : [],
		};
	}

	private byte[] CreateAuthentication()
	{
		var key = Encoding.UTF8.GetBytes(_hsmKey);
		var source = Encoding.UTF8.GetBytes(_source);
		var data = new byte[18 + key.Length + source.Length];
		var offset = 0;
		WriteUInt16(data, ref offset, data.Length - 2);
		data[offset++] = 1;
		data[offset++] = 4;
		WriteField(data, ref offset, 1, key);
		WriteField(data, ref offset, 2, [(byte)'P']);
		WriteField(data, ref offset, 3, [1]);
		WriteField(data, ref offset, 4, source);
		return data;
	}

	private static byte[] CreateFullMode()
	{
		var data = new byte[19];
		var offset = 2;
		data[offset++] = 12;
		data[offset++] = 2;
		data[offset++] = 1;
		BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), 8);
		offset += 2;
		BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset, 8), 1UL << _channel);
		offset += 8;
		data[offset++] = 2;
		BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), 1);
		offset += 2;
		data[offset] = (byte)'F';
		return data;
	}

	private byte[] CreateSubscription(bool isSubscribe, string[] topics)
	{
		var topicBytes = topics.Select(Encoding.ASCII.GetBytes).ToArray();
		var topicsLength = 2 + topicBytes.Sum(b => 1 + b.Length);
		var data = new byte[11 + topicsLength];
		var offset = 0;
		var officialLength = 18 + topicsLength + _authorization.Length + _source.Length;
		WriteUInt16(data, ref offset, officialLength);
		data[offset++] = isSubscribe ? (byte)4 : (byte)5;
		data[offset++] = 2;
		data[offset++] = 1;
		WriteUInt16(data, ref offset, topicsLength);
		WriteUInt16(data, ref offset, topics.Length);
		foreach (var topic in topicBytes)
		{
			if (topic.Length > byte.MaxValue)
				throw new InvalidOperationException("FYERS HSM topic is too long.");
			data[offset++] = (byte)topic.Length;
			topic.CopyTo(data, offset);
			offset += topic.Length;
		}
		data[offset++] = 2;
		WriteUInt16(data, ref offset, 1);
		data[offset] = _channel;
		return data;
	}

	private static byte[] CreateAcknowledgement(uint messageNumber)
	{
		var data = new byte[11];
		var offset = 0;
		WriteUInt16(data, ref offset, 9);
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
			throw new InvalidOperationException($"FYERS HSM field length {value} is out of range.");
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

	private static int CountFlags(FyersFeedSubscriptions subscriptions)
		=> (subscriptions.HasFlag(FyersFeedSubscriptions.Symbol) ? 1 : 0) +
			(subscriptions.HasFlag(FyersFeedSubscriptions.Depth) ? 1 : 0);

	private static string DecodeToken(string token)
	{
		var jwt = token.Contains(':') ? token[(token.IndexOf(':') + 1)..] : token;
		var parts = jwt.Split('.');
		if (parts.Length < 2)
			throw new InvalidOperationException("FYERS access token is not a valid JWT.");

		var payload = parts[1].Replace('-', '+').Replace('_', '/');
		payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
		var tokenData = JsonConvert.DeserializeObject<FyersTokenPayload>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))
			?? throw new InvalidOperationException("FYERS access token payload is empty.");
		if (tokenData.HsmKey.IsEmpty())
			throw new InvalidOperationException("FYERS access token does not contain an HSM key.");
		if (tokenData.ExpiresAt > 0 && tokenData.ExpiresAt <= (long)DateTime.UtcNow.ToUnix())
			throw new InvalidOperationException("FYERS access token has expired.");
		return tokenData.HsmKey;
	}

	private static void Ensure(ReadOnlySpan<byte> data, int requiredLength, string part)
	{
		if (requiredLength < 0 || data.Length < requiredLength)
			throw new InvalidDataException($"FYERS HSM {part} is truncated.");
	}
}
