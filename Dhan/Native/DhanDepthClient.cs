namespace StockSharp.Dhan.Native;

sealed class DhanDepthClient : BaseLogReceiver
{
	private sealed class DepthBook
	{
		public DhanDepthLevel[] Bids { get; set; }
		public DhanDepthLevel[] Asks { get; set; }
	}

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly int _depth;
	private readonly SynchronizedSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, DepthBook> _books = new(StringComparer.OrdinalIgnoreCase);

	public DhanDepthClient(string clientId, string token, int depth, int reconnectAttempts, WorkingTime workingTime)
	{
		if (depth is not 20 and not 200)
			throw new ArgumentOutOfRangeException(nameof(depth), depth, "Dhan full depth supports 20 or 200 levels.");

		_depth = depth;
		var endpoint = depth == 20 ? "wss://depth-api-feed.dhan.co/twentydepth" : "wss://full-depth-api.dhan.co/twohundreddepth";
		var url = $"{endpoint}?token={Uri.EscapeDataString(token.ThrowIfEmpty(nameof(token)))}&clientId={Uri.EscapeDataString(clientId.ThrowIfEmpty(nameof(clientId)))}&authType=2";

		_client = new(
			url,
			(state, cancellationToken) => default,
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

	public override string Name => nameof(Dhan) + $"_Depth{_depth}";

	public event Func<DhanDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();

	public async ValueTask Subscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		ValidateSegment(instrumentKey);
		if (_subscriptions.Contains(instrumentKey))
			return;
		if ((_depth == 20 && _subscriptions.Count >= 50) || (_depth == 200 && _subscriptions.Count >= 1))
			throw new InvalidOperationException($"Dhan {_depth}-level depth supports at most {(_depth == 20 ? 50 : 1)} instrument(s) per connection.");
		if (!_subscriptions.TryAdd(instrumentKey))
			return;
		await Send(true, [instrumentKey], cancellationToken);
	}

	public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(instrumentKey))
			return;
		_books.Remove(instrumentKey);
		await Send(false, [instrumentKey], cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var keys = _subscriptions.ToArray();
		var batchSize = _depth == 20 ? 50 : 1;
		for (var i = 0; i < keys.Length; i += batchSize)
			await Send(true, keys.Skip(i).Take(batchSize).ToArray(), cancellationToken);
	}

	private ValueTask Send(bool subscribe, string[] instrumentKeys, CancellationToken cancellationToken)
	{
		if (_depth == 200)
		{
			var (boardCode, securityId) = instrumentKeys.Single().ParseInstrumentKey();
			return _client.SendAsync(JsonConvert.SerializeObject(new DhanSingleSubscriptionRequest
			{
				RequestCode = subscribe ? 23 : 24,
				ExchangeSegment = boardCode,
				SecurityId = securityId,
			}, _jsonSettings), cancellationToken);
		}

		var instruments = instrumentKeys.Select(key =>
		{
			var (boardCode, securityId) = key.ParseInstrumentKey();
			return new DhanSubscriptionInstrument { ExchangeSegment = boardCode, SecurityId = securityId };
		}).ToArray();

		return _client.SendAsync(JsonConvert.SerializeObject(new DhanSubscriptionRequest
		{
			RequestCode = subscribe ? 23 : 24,
			InstrumentCount = instruments.Length,
			Instruments = instruments,
		}, _jsonSettings), cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory;
		var offset = 0;
		while (offset < data.Length)
		{
			var packet = data.Span[offset..];
			if (packet.Length < 12)
				throw new InvalidDataException("Dhan full-depth packet header is incomplete.");

			var length = BinaryPrimitives.ReadUInt16LittleEndian(packet[..2]);
			if (length < 12 || length > packet.Length)
				throw new InvalidDataException($"Dhan full-depth packet has invalid length {length}.");

			var update = Decode(packet[..length]);
			if (update != null && DepthReceived is { } handler)
				await handler(update, cancellationToken);
			offset += length;
		}
	}

	private DhanDepthUpdate Decode(ReadOnlySpan<byte> data)
	{
		var responseCode = data[2];
		var exchangeSegment = (DhanExchangeSegments)data[3];
		var securityId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4)).ToString(CultureInfo.InvariantCulture);
		var headerValue = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
		if (responseCode == 50)
		{
			var reason = data.Length >= 14 ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2)) : headerValue;
			throw new InvalidOperationException($"Dhan full-depth feed disconnected with reason {reason}.");
		}
		if (responseCode is not 41 and not 51)
			return null;

		var availableRows = (data.Length - 12) / 16;
		var count = _depth == 20
			? Math.Min(20, availableRows)
			: Math.Min(200, Math.Min((int)headerValue, availableRows));

		var levels = new List<DhanDepthLevel>(count);
		for (var i = 0; i < count; i++)
		{
			var offset = 12 + i * 16;
			var price = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8)));
			if (!double.IsFinite(price) || price <= 0)
				continue;
			var ordersCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 12, 4));
			levels.Add(new DhanDepthLevel
			{
				Price = Convert.ToDecimal(price, CultureInfo.InvariantCulture),
				Volume = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 8, 4)),
				OrdersCount = ordersCount > int.MaxValue ? int.MaxValue : (int)ordersCount,
			});
		}

		var key = exchangeSegment.ToBoardCode().ToInstrumentKey(securityId);
		var book = _books.SafeAdd(key);
		if (responseCode == 41)
			book.Bids = [.. levels.OrderByDescending(l => l.Price)];
		else
			book.Asks = [.. levels.OrderBy(l => l.Price)];

		if (book.Bids == null || book.Asks == null)
			return null;

		return new DhanDepthUpdate
		{
			ExchangeSegment = exchangeSegment,
			SecurityId = securityId,
			Bids = book.Bids,
			Asks = book.Asks,
		};
	}

	private static void ValidateSegment(string instrumentKey)
	{
		var (boardCode, _) = instrumentKey.ParseInstrumentKey();
		if (boardCode is not "NSE_EQ" and not "NSE_FNO")
			throw new InvalidOperationException("Dhan 20/200-level depth is available only for NSE_EQ and NSE_FNO instruments.");
	}
}
