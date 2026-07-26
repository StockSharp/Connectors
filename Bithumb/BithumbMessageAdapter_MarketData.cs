namespace StockSharp.Bithumb;

public partial class BithumbMessageAdapter
{
	private readonly HashSet<SecurityId> _orderBookSubscriptions = [];
	private readonly Dictionary<SecurityId, long> _tradesSubscriptions = [];
	private readonly HashSet<SecurityId> _level1Subscriptions = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Market.ToStockSharp(),
				Name = symbol.EnglishName,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask ProcessTickerAsync(Ticker ticker, CancellationToken cancellationToken)
	{
		var serverTime = ticker.Timestamp == default ? CurrentTime : ticker.Timestamp;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = serverTime,
			SecurityId = ticker.Symbol.ToStockSharp(),
		}
		.TryAdd(Level1Fields.OpenPrice, ticker.OpeningPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.LastTradePrice, ticker.TradePrice)
		.TryAdd(Level1Fields.LastTradeVolume, ticker.TradeVolume)
		.TryAdd(Level1Fields.LastTradeTime, ticker.TradeTimestamp)
		.TryAdd(Level1Fields.LastTradeOrigin, ticker.AskBid.ToOriginSide())
		.TryAdd(Level1Fields.Change, ticker.ChangePrice)
		.TryAdd(Level1Fields.Volume, ticker.AccumulatedVolume24H), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_level1Subscriptions.Add(mdMsg.SecurityId);
			await _pusherClient.SubscribeTickerAsync(mdMsg.TransactionId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_level1Subscriptions.Remove(mdMsg.SecurityId);
			await _pusherClient.UnsubscribeTickerAsync(mdMsg.TransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_orderBookSubscriptions.Add(mdMsg.SecurityId);
			await _pusherClient.SubscribeOrderBookAsync(mdMsg.TransactionId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_orderBookSubscriptions.Remove(mdMsg.SecurityId);
			await _pusherClient.UnsubscribeOrderBookAsync(mdMsg.TransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var trades = await _httpClient.GetTransactionsAsync(symbol, cancellationToken);

				foreach (var trade in trades.OrderBy(t => t.Time))
				{
					if (trade.Time < from || trade.Time > to)
						continue;

					await ProcessTickAsync(mdMsg.TransactionId, trade, cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_tradesSubscriptions[mdMsg.SecurityId] = mdMsg.TransactionId;
				await _pusherClient.SubscribeTransactionAsync(mdMsg.TransactionId, symbol,
					cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tradesSubscriptions.Remove(mdMsg.SecurityId);
			await _pusherClient.UnsubscribeTransactionAsync(mdMsg.TransactionId, symbol,
				cancellationToken);
		}
	}

	private ValueTask ProcessTickAsync(long originTransId, Transaction trade,
		CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			ServerTime = trade.Time,
			OriginalTransactionId = originTransId,
			OriginSide = trade.Side.ToOriginSide(),
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(Ticker ticker,
		CancellationToken cancellationToken)
	{
		var securityId = ticker.Symbol.ToStockSharp();

		return _level1Subscriptions.Contains(securityId)
			? ProcessTickerAsync(ticker, cancellationToken)
			: default;
	}

	private ValueTask SessionOnNewTrade(Transaction trade,
		CancellationToken cancellationToken)
	{
		var securityId = trade.Symbol.ToStockSharp();

		return _tradesSubscriptions.TryGetValue(securityId, out var transactionId)
			? ProcessTickAsync(transactionId, trade, cancellationToken)
			: default;
	}

	private ValueTask SessionOnOrderBookChanged(OrderBook book,
		CancellationToken cancellationToken)
	{
		var securityId = book.Symbol.ToStockSharp();

		if (!_orderBookSubscriptions.Contains(securityId))
			return default;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityId,
			Bids = [.. book.Units.Select(unit => new QuoteChange(unit.BidPrice, unit.BidSize))],
			Asks = [.. book.Units.Select(unit => new QuoteChange(unit.AskPrice, unit.AskSize))],
			ServerTime = book.Timestamp,
		}, cancellationToken);
	}
}
