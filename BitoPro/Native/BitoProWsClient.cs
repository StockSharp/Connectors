namespace StockSharp.BitoPro.Native;

sealed class BitoProWsClient : BaseLogReceiver
{
	private enum StreamTypes
	{
		Ticker,
		OrderBook,
		Trades,
	}

	private readonly record struct StreamKey(
		StreamTypes Type,
		string Symbol,
		int Depth);

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<StreamKey, WebSocketClient> _clients = [];
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private bool _isConnected;

	public BitoProWsClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "BitoPro_WS";

	public event Func<BitoProTicker, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<BitoProOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<BitoProTradePush, CancellationToken, ValueTask>
		TradesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
		}
		foreach (var client in clients)
			client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using (_sync.EnterScope())
		{
			if (_isConnected)
				throw new InvalidOperationException(
					"BitoPro WebSocket manager is already connected.");
			_isConnected = true;
		}
		return default;
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
		}
		foreach (var client in clients)
			await DisconnectClientAsync(client, cancellationToken);
	}

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SubscribeAsync(new(StreamTypes.Ticker,
			NormalizeSymbol(symbol), 0), cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(new(StreamTypes.Ticker,
			NormalizeSymbol(symbol), 0), cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> SubscribeAsync(new(StreamTypes.OrderBook,
			NormalizeSymbol(symbol),
			BitoProRestClient.NormalizeDepth(depth)),
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(new(StreamTypes.OrderBook,
			NormalizeSymbol(symbol),
			BitoProRestClient.NormalizeDepth(depth)),
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> SubscribeAsync(new(StreamTypes.Trades,
			NormalizeSymbol(symbol), 0), cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(new(StreamTypes.Trades,
			NormalizeSymbol(symbol), 0), cancellationToken);

	private async ValueTask SubscribeAsync(StreamKey key,
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_isConnected)
				throw new InvalidOperationException(
					"BitoPro WebSocket manager is disconnected.");
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

	private async ValueTask UnsubscribeAsync(StreamKey key,
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
			if (!_clients.Remove(key, out client))
				return;
		await DisconnectClientAsync(client, cancellationToken);
	}

	private WebSocketClient CreateClient(StreamKey key)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			CreateStreamEndpoint(key),
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) => OnProcessAsync(
				key, message, token),
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
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-BitoPro-Connector/1.0");
			return default;
		};
		return client;
	}

	private string CreateStreamEndpoint(StreamKey key)
		=> key.Type switch
		{
			StreamTypes.Ticker =>
				$"{_endpoint}/v1/pub/tickers/{key.Symbol}",
			StreamTypes.OrderBook =>
				$"{_endpoint}/v1/pub/order-books/" +
					$"{key.Symbol}:{key.Depth}",
			StreamTypes.Trades =>
				$"{_endpoint}/v1/pub/trades/{key.Symbol}",
			_ => throw new ArgumentOutOfRangeException(
				nameof(key), key, LocalizedStrings.InvalidValue),
		};

	private async ValueTask OnProcessAsync(StreamKey key,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			switch (key.Type)
			{
				case StreamTypes.Ticker:
					if (TickerReceived is { } tickerHandler)
						await tickerHandler(
							DeserializeMessage<BitoProTicker>(payload),
							cancellationToken);
					break;

				case StreamTypes.OrderBook:
					if (OrderBookReceived is { } bookHandler)
						await bookHandler(
							DeserializeMessage<BitoProOrderBook>(payload),
							cancellationToken);
					break;

				case StreamTypes.Trades:
					if (TradesReceived is { } tradeHandler)
						await tradeHandler(
							DeserializeMessage<BitoProTradePush>(payload),
							cancellationToken);
					break;

				default:
					throw new ArgumentOutOfRangeException(
						nameof(key), key, LocalizedStrings.InvalidValue);
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	internal static TMessage DeserializeMessage<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"BitoPro WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BitoPro WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler(state, cancellationToken)
			: default;

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static async ValueTask DisconnectClientAsync(
		WebSocketClient client, CancellationToken cancellationToken)
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

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToBitoProSymbol().ToUpperInvariant();
}
