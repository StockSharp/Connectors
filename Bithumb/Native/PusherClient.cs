namespace StockSharp.Bithumb.Native;

class PusherClient : BaseLogReceiver
{
	private const string _ticker = "ticker";
	private const string _trade = "trade";
	private const string _orderBook = "orderbook";

	private readonly WebSocketClient _client;
	private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
	private readonly HashSet<string> _tickers = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _trades = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _orderBooks = new(StringComparer.OrdinalIgnoreCase);

	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<Transaction, CancellationToken, ValueTask> NewTrade;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public override string Name => nameof(Bithumb) + "_" + nameof(PusherClient);

	public PusherClient(string endpoint, int attemptsCount, WorkingTime workingTime)
	{
		var uri = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)), UriKind.Absolute);

		if (!uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("Bithumb WebSocket endpoint must use WSS.", nameof(endpoint));

		_client = new(
			uri.ToString(),
			async (state, token) =>
			{
				if (state == ConnectionStates.Connected)
					await SendSubscriptionsAsync(0, token);

				if (StateChanged is { } handler)
					await handler(state, token);
			},
			(error, token) =>
			{
				this.AddErrorLog(error);
				if (Error is { } handler)
					return handler(error, token);
				return default;
			},
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = attemptsCount,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		return _client.DisconnectAsync(cancellationToken);
	}

	public ValueTask SubscribeTickerAsync(long transactionId, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(transactionId, _tickers, symbol, true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(long transactionId, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(transactionId, _tickers, symbol, false, cancellationToken);

	public ValueTask SubscribeTransactionAsync(long transactionId, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(transactionId, _trades, symbol, true, cancellationToken);

	public ValueTask UnsubscribeTransactionAsync(long transactionId, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(transactionId, _trades, symbol, false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(long transactionId, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(transactionId, _orderBooks, symbol, true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(long transactionId, string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(transactionId, _orderBooks, symbol, false, cancellationToken);

	private async ValueTask ChangeSubscriptionAsync(long transactionId, HashSet<string> symbols,
		string symbol, bool subscribe, CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));

		await _subscriptionLock.WaitAsync(cancellationToken);
		try
		{
			var changed = subscribe ? symbols.Add(symbol) : symbols.Remove(symbol);

			if (changed)
				await SendSubscriptionsCoreAsync(transactionId, cancellationToken);
		}
		finally
		{
			_subscriptionLock.Release();
		}
	}

	private async ValueTask SendSubscriptionsAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		await _subscriptionLock.WaitAsync(cancellationToken);
		try
		{
			await SendSubscriptionsCoreAsync(transactionId, cancellationToken);
		}
		finally
		{
			_subscriptionLock.Release();
		}
	}

	private ValueTask SendSubscriptionsCoreAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		if (_tickers.Count == 0 && _trades.Count == 0 && _orderBooks.Count == 0)
			return default;

		var request = new List<SocketRequestField>
		{
			new SocketTicket
			{
				Ticket = transactionId == 0
					? Guid.NewGuid().ToString("N")
					: transactionId.ToString(),
			},
		};

		AddSubscription(request, _ticker, _tickers);
		AddSubscription(request, _trade, _trades);
		AddSubscription(request, _orderBook, _orderBooks);
		request.Add(new SocketFormat());

		return _client.SendAsync(request.ToArray(), cancellationToken, transactionId);
	}

	private static void AddSubscription(List<SocketRequestField> request, string type,
		HashSet<string> symbols)
	{
		if (symbols.Count == 0)
			return;

		request.Add(new SocketSubscription
		{
			Type = type,
			Codes = [.. symbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)],
		});
	}

	private async ValueTask OnProcess(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var json = message.AsString();

		if (json.IsEmpty())
			return;

		var envelope = JsonConvert.DeserializeObject<SocketEnvelope>(json);

		if (envelope?.Error != null)
		{
			var error = new InvalidOperationException(
				$"Bithumb WebSocket error {envelope.Error.Name}: {envelope.Error.Message}.");

			if (Error is { } errorHandler)
				await errorHandler(error, cancellationToken);
			else
				this.AddErrorLog(error);

			return;
		}

		switch (envelope?.Type)
		{
			case _ticker:
				if (TickerChanged is { } tickerHandler)
					await tickerHandler(
						JsonConvert.DeserializeObject<Ticker>(json)
						?? throw new InvalidOperationException("Bithumb returned an empty ticker."),
						cancellationToken);
				break;

			case _trade:
				if (NewTrade is { } tradeHandler)
					await tradeHandler(
						JsonConvert.DeserializeObject<Transaction>(json)
						?? throw new InvalidOperationException("Bithumb returned an empty trade."),
						cancellationToken);
				break;

			case _orderBook:
				if (OrderBookChanged is { } orderBookHandler)
					await orderBookHandler(
						JsonConvert.DeserializeObject<OrderBook>(json)
						?? throw new InvalidOperationException("Bithumb returned an empty order book."),
						cancellationToken);
				break;

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, envelope?.Type);
				break;
		}
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		_subscriptionLock.Dispose();
		base.DisposeManaged();
	}
}
