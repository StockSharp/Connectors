namespace StockSharp.AngelOne.Native;

sealed class AngelOneMarketDataClient : BaseLogReceiver
{
	private const string _url = "wss://smartapisocket.angelone.in/smart-stream";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _jwtToken;
	private readonly string _apiKey;
	private readonly string _clientCode;
	private readonly string _feedToken;
	private readonly SynchronizedDictionary<string, AngelOneFeedModes> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

	public AngelOneMarketDataClient(string jwtToken, string apiKey, string clientCode, string feedToken,
		int reconnectAttempts, WorkingTime workingTime)
	{
		_jwtToken = jwtToken.ThrowIfEmpty(nameof(jwtToken));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		_feedToken = feedToken.ThrowIfEmpty(nameof(feedToken));

		_client = new(
			_url,
			(state, token) => StateChanged is { } stateHandler ? stateHandler(state, token) : default,
			(error, token) => Error is { } errorHandler ? errorHandler(error, token) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.Init += OnInit;
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(AngelOne) + "_" + nameof(AngelOneMarketDataClient);

	public event Func<AngelOneMarketTick, CancellationToken, ValueTask> TickReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();
	public ValueTask SendHeartbeat(CancellationToken cancellationToken) => _client.SendAsync("ping", cancellationToken);

	private void OnInit(ClientWebSocket socket)
	{
		socket.Options.SetRequestHeader("Authorization", _jwtToken);
		socket.Options.SetRequestHeader("x-api-key", _apiKey);
		socket.Options.SetRequestHeader("x-client-code", _clientCode);
		socket.Options.SetRequestHeader("x-feed-token", _feedToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		foreach (var modeGroup in _subscriptions.ToArray().GroupBy(p => p.Value))
		{
			foreach (var exchangeGroup in modeGroup.GroupBy(p => p.Key.ParseInstrumentKey().exchangeType))
				await Send(true, modeGroup.Key, exchangeGroup.Key, [.. exchangeGroup.Select(p => p.Key.ParseInstrumentKey().token)], cancellationToken);
		}
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory;
		if (data.IsEmpty)
			return;

		var first = data.Span[0];
		if (first is >= (byte)AngelOneFeedModes.Ltp and <= (byte)AngelOneFeedModes.SnapQuote && data.Length >= 51)
		{
			var tick = Decode(data.Span);
			if (TickReceived is { } handler)
				await handler(tick, cancellationToken);
			return;
		}

		var text = message.AsString();
		if (text.IsEmpty() || text.EqualsIgnoreCase("pong"))
			return;

		var error = JsonConvert.DeserializeObject<AngelOneStreamError>(text);
		if (error != null && !error.ErrorCode.IsEmpty())
			throw new InvalidOperationException($"Angel One stream error {error.ErrorCode}: {error.ErrorMessage}");
	}

	public async ValueTask Subscribe(AngelOneExchangeTypes exchangeType, string token, CancellationToken cancellationToken)
	{
		var key = exchangeType.ToInstrumentKey(token);
		if (_subscriptions.ContainsKey(key))
			return;

		_subscriptions.Add(key, AngelOneFeedModes.SnapQuote);
		await Send(true, AngelOneFeedModes.SnapQuote, exchangeType, [token], cancellationToken);
	}

	public async ValueTask Unsubscribe(AngelOneExchangeTypes exchangeType, string token, CancellationToken cancellationToken)
	{
		var key = exchangeType.ToInstrumentKey(token);
		if (!_subscriptions.Remove(key))
			return;

		await Send(false, AngelOneFeedModes.SnapQuote, exchangeType, [token], cancellationToken);
	}

	private ValueTask Send(bool subscribe, AngelOneFeedModes mode, AngelOneExchangeTypes exchangeType,
		string[] tokens, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new AngelOneSubscriptionRequest
		{
			CorrelationId = Guid.NewGuid().ToString("N")[..10],
			Action = subscribe ? 1 : 0,
			Parameters = new()
			{
				Mode = (int)mode,
				TokenList =
				[
					new AngelOneTokenGroup
					{
						ExchangeType = (int)exchangeType,
						Tokens = tokens,
					},
				],
			},
		}, _jsonSettings), cancellationToken);

	private static AngelOneMarketTick Decode(ReadOnlySpan<byte> data)
	{
		var mode = (AngelOneFeedModes)data[0];
		var expectedLength = mode switch
		{
			AngelOneFeedModes.Ltp => 51,
			AngelOneFeedModes.Quote => 123,
			AngelOneFeedModes.SnapQuote => 379,
			_ => throw new InvalidDataException($"Unsupported Angel One feed mode {(byte)mode}."),
		};
		if (data.Length < expectedLength)
			throw new InvalidDataException($"Angel One {mode} packet has {data.Length} bytes; expected at least {expectedLength}.");

		var timestamp = ReadInt64(data, 35);
		var exchangeType = (AngelOneExchangeTypes)data[1];
		var tick = new AngelOneMarketTick
		{
			Mode = mode,
			ExchangeType = exchangeType,
			Token = Encoding.UTF8.GetString(data.Slice(2, 25)).TrimEnd('\0'),
			SequenceNumber = ReadInt64(data, 27),
			ServerTime = timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime : DateTime.UtcNow,
			LastPrice = ReadInt64(data, 43).ToStreamPrice(exchangeType),
		};

		if (mode < AngelOneFeedModes.Quote)
			return tick;

		tick.LastVolume = ReadInt64(data, 51);
		tick.AveragePrice = ReadInt64(data, 59).ToStreamPrice(exchangeType);
		tick.Volume = ReadInt64(data, 67);
		tick.TotalBuyVolume = Convert.ToDecimal(ReadDouble(data, 75), CultureInfo.InvariantCulture);
		tick.TotalSellVolume = Convert.ToDecimal(ReadDouble(data, 83), CultureInfo.InvariantCulture);
		tick.OpenPrice = ReadInt64(data, 91).ToStreamPrice(exchangeType);
		tick.HighPrice = ReadInt64(data, 99).ToStreamPrice(exchangeType);
		tick.LowPrice = ReadInt64(data, 107).ToStreamPrice(exchangeType);
		tick.ClosePrice = ReadInt64(data, 115).ToStreamPrice(exchangeType);

		if (mode < AngelOneFeedModes.SnapQuote)
			return tick;

		var lastTradeTimestamp = ReadInt64(data, 123);
		tick.LastTradeTime = lastTradeTimestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastTradeTimestamp).UtcDateTime : null;
		tick.OpenInterest = ReadInt64(data, 131);
		tick.UpperCircuit = ReadInt64(data, 347).ToStreamPrice(exchangeType);
		tick.LowerCircuit = ReadInt64(data, 355).ToStreamPrice(exchangeType);
		tick.YearHigh = ReadInt64(data, 363).ToStreamPrice(exchangeType);
		tick.YearLow = ReadInt64(data, 371).ToStreamPrice(exchangeType);

		var bids = new List<AngelOneDepthLevel>(5);
		var asks = new List<AngelOneDepthLevel>(5);
		for (var i = 0; i < 10; i++)
		{
			var offset = 147 + i * 20;
			var level = new AngelOneDepthLevel
			{
				Volume = ReadInt64(data, offset + 2),
				Price = ReadInt64(data, offset + 10).ToStreamPrice(exchangeType),
				OrdersCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 18, 2)),
			};

			if (level.Price <= 0)
				continue;
			if (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)) == 1)
				bids.Add(level);
			else
				asks.Add(level);
		}

		tick.Bids = [.. bids.OrderByDescending(l => l.Price)];
		tick.Asks = [.. asks.OrderBy(l => l.Price)];
		return tick;
	}

	private static long ReadInt64(ReadOnlySpan<byte> data, int offset)
		=> BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, sizeof(long)));

	private static double ReadDouble(ReadOnlySpan<byte> data, int offset)
		=> BitConverter.Int64BitsToDouble(ReadInt64(data, offset));
}
