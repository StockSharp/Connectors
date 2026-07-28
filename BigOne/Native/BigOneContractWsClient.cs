namespace StockSharp.BigOne.Native;

sealed class BigOneContractWsClient : BaseLogReceiver
{
	private enum StreamTypes
	{
		Instrument,
		OrderBook,
		Trades,
		Candles,
		Private,
	}

	private readonly record struct StreamKey(
		StreamTypes Type,
		string Symbol,
		string Period);

	private readonly string _publicEndpoint;
	private readonly string _privateEndpoint;
	private readonly Func<string> _tokenFactory;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<StreamKey, WebSocketClient> _clients = [];
	private readonly Dictionary<string, SortedDictionary<decimal, decimal>>
		_bids = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SortedDictionary<decimal, decimal>>
		_asks = new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private bool _isConnected;

	public BigOneContractWsClient(
		string publicEndpoint,
		string privateEndpoint,
		Func<string> tokenFactory,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_publicEndpoint = publicEndpoint
			.ThrowIfEmpty(nameof(publicEndpoint)).TrimEnd('/');
		_privateEndpoint = privateEndpoint
			.ThrowIfEmpty(nameof(privateEndpoint)).TrimEnd('/');
		_tokenFactory = tokenFactory;
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "BigONE_Contract_WS";

	public event Func<BigOneContractInstrument,
		CancellationToken, ValueTask> InstrumentReceived;
	public event Func<BigOneContractDepth,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<BigOneContractTrade[],
		CancellationToken, ValueTask> TradesReceived;
	public event Func<BigOneContractCandle[],
		CancellationToken, ValueTask> CandlesReceived;
	public event Func<BigOneContractStream,
		CancellationToken, ValueTask> PrivateReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
			_bids.Clear();
			_asks.Clear();
		}
		foreach (var client in clients)
			client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using (_sync.EnterScope())
		{
			if (_isConnected)
				throw new InvalidOperationException(
					"BigONE contract WebSocket manager is already connected.");
			_isConnected = true;
		}
		if (_tokenFactory is not null)
			await SubscribeAsync(
				new(StreamTypes.Private, null, null),
				cancellationToken);
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
			_bids.Clear();
			_asks.Clear();
		}
		foreach (var client in clients)
			await DisconnectClientAsync(client, cancellationToken);
	}

	public ValueTask SubscribeInstrumentAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SubscribeAsync(
			new(StreamTypes.Instrument,
				NormalizeSymbol(symbol), null),
			cancellationToken);

	public ValueTask UnsubscribeInstrumentAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(
			new(StreamTypes.Instrument,
				NormalizeSymbol(symbol), null),
			cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SubscribeAsync(
			new(StreamTypes.OrderBook,
				NormalizeSymbol(symbol), null),
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(
			new(StreamTypes.OrderBook,
				NormalizeSymbol(symbol), null),
			cancellationToken);

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SubscribeAsync(
			new(StreamTypes.Trades,
				NormalizeSymbol(symbol), null),
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(
			new(StreamTypes.Trades,
				NormalizeSymbol(symbol), null),
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(
		string symbol,
		string period,
		CancellationToken cancellationToken)
		=> SubscribeAsync(
			new(StreamTypes.Candles,
				NormalizeSymbol(symbol),
				NormalizePeriod(period)),
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(
		string symbol,
		string period,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(
			new(StreamTypes.Candles,
				NormalizeSymbol(symbol),
				NormalizePeriod(period)),
			cancellationToken);

	internal static BigOneContractCandle[] DeserializeCandles(
		string payload)
		=> Deserialize<BigOneContractCandle[]>(payload);

	private async ValueTask SubscribeAsync(
		StreamKey key,
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_isConnected)
				throw new InvalidOperationException(
					"BigONE contract WebSocket manager is disconnected.");
			if (_clients.ContainsKey(key))
				return;
			client = CreateClient(key);
			_clients.Add(key, client);
		}
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_clients.Remove(key);
			client.Dispose();
			throw;
		}
	}

	private async ValueTask UnsubscribeAsync(
		StreamKey key,
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_clients.Remove(key, out client))
				return;
			if (key.Type == StreamTypes.OrderBook)
			{
				_bids.Remove(key.Symbol);
				_asks.Remove(key.Symbol);
			}
		}
		await DisconnectClientAsync(client, cancellationToken);
	}

	private WebSocketClient CreateClient(StreamKey key)
	{
		var client = new WebSocketClient(
			CreateStreamEndpoint(key),
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) =>
				OnProcessAsync(key, message, token),
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
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-BigONE-Connector/1.0");
			if (key.Type == StreamTypes.Private)
				socket.Options.SetRequestHeader(
					"Authorization",
					"Bearer " + _tokenFactory());
			return default;
		};
		return client;
	}

	private string CreateStreamEndpoint(StreamKey key)
		=> key.Type switch
		{
			StreamTypes.Instrument =>
				$"{_publicEndpoint}/instruments@{key.Symbol}",
			StreamTypes.OrderBook =>
				$"{_publicEndpoint}/depth@{key.Symbol}",
			StreamTypes.Trades =>
				$"{_publicEndpoint}/trades@{key.Symbol}",
			StreamTypes.Candles =>
				$"{_publicEndpoint}/candlesticks/" +
					$"{key.Period}@{key.Symbol}",
			StreamTypes.Private => _privateEndpoint,
			_ => throw new ArgumentOutOfRangeException(
				nameof(key), key, LocalizedStrings.InvalidValue),
		};

	private async ValueTask OnProcessAsync(
		StreamKey key,
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			switch (key.Type)
			{
				case StreamTypes.Instrument:
					foreach (var instrument in Deserialize<
						BigOneContractInstrument[]>(payload) ?? [])
						if (InstrumentReceived is { } instrumentHandler)
							await instrumentHandler(
								instrument, cancellationToken);
					break;

				case StreamTypes.OrderBook:
					await ProcessDepthAsync(
						key.Symbol,
						Deserialize<BigOneContractDepth>(payload),
						cancellationToken);
					break;

				case StreamTypes.Trades:
					if (TradesReceived is { } tradesHandler)
						await tradesHandler(
							Deserialize<BigOneContractTrade[]>(
								payload) ?? [],
							cancellationToken);
					break;

				case StreamTypes.Candles:
					if (CandlesReceived is { } candlesHandler)
						await candlesHandler(
							DeserializeCandles(payload) ?? [],
							cancellationToken);
					break;

				case StreamTypes.Private:
					if (PrivateReceived is { } privateHandler)
						await privateHandler(
							Deserialize<BigOneContractStream>(
								payload),
							cancellationToken);
					break;

				default:
					throw new ArgumentOutOfRangeException(
						nameof(key), key,
						LocalizedStrings.InvalidValue);
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessDepthAsync(
		string symbol,
		BigOneContractDepth update,
		CancellationToken cancellationToken)
	{
		if (update is null)
			return;
		Dictionary<string, decimal> bids;
		Dictionary<string, decimal> asks;
		using (_sync.EnterScope())
		{
			if (update.From == 0 ||
				!_bids.TryGetValue(symbol, out var bidBook))
			{
				bidBook = [];
				_bids[symbol] = bidBook;
			}
			if (update.From == 0 ||
				!_asks.TryGetValue(symbol, out var askBook))
			{
				askBook = [];
				_asks[symbol] = askBook;
			}
			if (update.From == 0)
			{
				bidBook.Clear();
				askBook.Clear();
			}
			ApplyLevels(bidBook, update.Bids);
			ApplyLevels(askBook, update.Asks);
			bids = bidBook.ToDictionary(
				static pair => pair.Key.ToString(
					CultureInfo.InvariantCulture),
				static pair => pair.Value,
				StringComparer.Ordinal);
			asks = askBook.ToDictionary(
				static pair => pair.Key.ToString(
					CultureInfo.InvariantCulture),
				static pair => pair.Value,
				StringComparer.Ordinal);
		}
		if (OrderBookReceived is { } handler)
			await handler(new()
			{
				Symbol = symbol,
				Bids = bids,
				Asks = asks,
				From = 0,
				To = update.To,
				LastPrice = update.LastPrice,
				BestPrices = update.BestPrices,
			}, cancellationToken);
	}

	private async ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static async ValueTask DisconnectClientAsync(
		WebSocketClient client,
		CancellationToken cancellationToken)
	{
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

	private static T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				});
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BigONE contract WebSocket returned malformed JSON.",
				error);
		}
	}

	private static void ApplyLevels(
		IDictionary<decimal, decimal> book,
		IDictionary<string, decimal> levels)
	{
		foreach (var pair in levels ??
			new Dictionary<string, decimal>())
		{
			if (!decimal.TryParse(
				pair.Key,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var price) || price <= 0)
				continue;
			if (pair.Value <= 0)
				book.Remove(price);
			else
				book[price] = pair.Value;
		}
	}

	private static string NormalizeSymbol(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
			.ToUpperInvariant();
		if (symbol.Contains('/') ||
			symbol.Contains('-') ||
			symbol.Contains('_'))
			throw new FormatException(
				$"Invalid BigONE contract symbol '{symbol}'.");
		return symbol;
	}

	private static string NormalizePeriod(string period)
	{
		period = period.ThrowIfEmpty(nameof(period)).Trim()
			.ToUpperInvariant();
		if (!BigOneExtensions.TimeFrames
			.Where(static timeFrame =>
				timeFrame.IsContractPeriodSupported())
			.Any(timeFrame => timeFrame.ToBigOneContractPeriod()
				.EqualsIgnoreCase(period)))
			throw new ArgumentOutOfRangeException(
				nameof(period), period,
				"Unsupported BigONE contract candle period.");
		return period;
	}
}
