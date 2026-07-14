namespace StockSharp.Bithumb;

public partial class BithumbMessageAdapter
{
	private readonly HashSet<SecurityId> _orderBookSubscriptions = [];
	private readonly Dictionary<SecurityId, long?> _tradesSubscriptions = [];
	private readonly HashSet<SecurityId> _level1Subscriptions = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var pair in await _httpClient.GetAllTickersAsync(cancellationToken))
		{
			var secId = pair.Key.ToStockSharp();

			var secMsg = new SecurityMessage
			{
				SecurityId = secId,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			await ProcessTickerAsync(secId, ticker: pair.Value, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask ProcessTickerAsync(SecurityId secId, Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = CurrentTime,
			SecurityId = secId,
		}
		.TryAdd(Level1Fields.OpenPrice, ticker.OpeningPrice?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low?.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, ticker.ClosingPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal())
		.TryAdd(Level1Fields.AveragePrice, ticker.VWAP?.ToDecimal()), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTickerAsync(mdMsg.TransactionId, symbol, cancellationToken);
			_level1Subscriptions.Add(mdMsg.SecurityId);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_level1Subscriptions.Remove(mdMsg.SecurityId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(mdMsg.TransactionId, symbol, cancellationToken);
			_orderBookSubscriptions.Add(mdMsg.SecurityId);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_orderBookSubscriptions.Remove(mdMsg.SecurityId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;

				var trades = (await _httpClient.GetTransactionsAsync(symbol, cancellationToken)).ToArray();

				foreach (var trade in trades)
				{
					var time = trade.Time.ToDto();

					if (time < from)
						continue;

					if (time > to)
						break;

					await ProcessTickAsync(mdMsg.TransactionId, mdMsg.SecurityId, trade, cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				await _pusherClient.SubscribeTransactionAsync(mdMsg.TransactionId, symbol, cancellationToken);
				_tradesSubscriptions.Add(mdMsg.SecurityId, null);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tradesSubscriptions.Remove(mdMsg.SecurityId);
		}
	}

	private ValueTask ProcessTickAsync(long originTransId, SecurityId secId, Transaction trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			TradeId = trade.Id,
			TradePrice = (decimal)trade.Price,
			TradeVolume = trade.Amount.ToDecimal(),
			ServerTime = trade.Time.ToDto(),
			OriginalTransactionId = originTransId,
			OriginSide = trade.Type.ToSide(),
		}, cancellationToken);
	}

	private async ValueTask SessionOnTickersChanged(IDictionary<string, Ticker> tickers, CancellationToken cancellationToken)
	{
		foreach (var pair in tickers)
		{
			var secId = pair.Key.ToStockSharp();

			if (!_level1Subscriptions.Contains(secId))
				continue;

			await ProcessTickerAsync(secId, pair.Value, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewTicks(string currency, IEnumerable<Transaction> ticks, CancellationToken cancellationToken)
	{
		var secId = currency.ToStockSharp();

		if (!_tradesSubscriptions.ContainsKey(secId))
			return;

		foreach (var tick in ticks)
		{
			await ProcessTickAsync(0, secId, tick, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookChanged(string currency, OrderBook book, CancellationToken cancellationToken)
	{
		var secId = currency.ToStockSharp();

		if (!_orderBookSubscriptions.Contains(secId))
			return default;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			Bids = [.. book.Bids.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Quantity))],
			Asks = [.. book.Asks.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Quantity))],
			ServerTime = CurrentTime,
		}, cancellationToken);
	}
}
