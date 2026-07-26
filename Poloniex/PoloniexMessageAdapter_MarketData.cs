namespace StockSharp.Poloniex;

partial class PoloniexMessageAdapter
{
	private int _level1Counter;
	private readonly SynchronizedSet<SecurityId> _wsBookSubscriptions = [];
	private readonly SynchronizedSet<SecurityId> _wsTradesSubscriptions = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var markets = await _restClient.GetMarketsAsync(cancellationToken) ?? [];
		var tickers = (await _restClient.GetTickersAsync(cancellationToken) ?? [])
			.Where(static ticker => !ticker.Symbol.IsEmpty())
			.ToDictionary(static ticker => ticker.Symbol, StringComparer.OrdinalIgnoreCase);
		var securityTypes = lookupMsg.GetSecurityTypes();

		foreach (var market in markets)
		{
			if (market.Symbol.IsEmpty())
				continue;

			var securityId = market.Symbol.ToStockSharp();
			var tradeLimit = market.TradeLimit;
			var security = new SecurityMessage
			{
				SecurityId = securityId,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = ToStep(tradeLimit?.PriceScale),
				VolumeStep = ToStep(tradeLimit?.QuantityScale),
				MinVolume = Positive(tradeLimit?.MinQuantity),
			}.FillDefaultCryptoFields();

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			await SendOutMessageAsync(security, cancellationToken);

			if (tickers.TryGetValue(market.Symbol, out var ticker))
				await SessionOnTickerChanged(ticker, cancellationToken);

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}.TryAdd(Level1Fields.State, market.State.EqualsIgnoreCase("NORMAL")
				? SecurityStates.Trading
				: SecurityStates.Stoped), cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (_level1Counter++ == 0)
				await _publicSocket.SubscribeTickerAsync(cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_level1Counter > 0 && --_level1Counter == 0)
		{
			await _publicSocket.UnsubscribeTickerAsync(cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var symbol = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (!_wsBookSubscriptions.Contains(mdMsg.SecurityId))
			{
				_wsBookSubscriptions.Add(mdMsg.SecurityId);
				await _publicSocket.SubscribeBookAsync(symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_wsBookSubscriptions.Contains(mdMsg.SecurityId))
		{
			_wsBookSubscriptions.Remove(mdMsg.SecurityId);
			await _publicSocket.UnsubscribeBookAsync(symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var symbol = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
			{
				var trades = await _restClient.GetTradeHistoryAsync(symbol, mdMsg.From, mdMsg.To,
					cancellationToken);

				foreach (var trade in trades.OrderBy(static trade => trade.Id))
				{
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						SecurityId = mdMsg.SecurityId,
						TradeId = trade.Id,
						TradePrice = trade.Price,
						TradeVolume = trade.Quantity,
						ServerTime = trade.CreateTime.FromUnix(false),
						OriginSide = trade.TakerSide.ToSide(),
						OriginalTransactionId = mdMsg.TransactionId,
					}, cancellationToken);
				}
			}

			if (mdMsg.IsHistoryOnly())
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			if (!_wsTradesSubscriptions.Contains(mdMsg.SecurityId))
			{
				_wsTradesSubscriptions.Add(mdMsg.SecurityId);
				await _publicSocket.SubscribeTradesAsync(symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_wsTradesSubscriptions.Contains(mdMsg.SecurityId))
		{
			_wsTradesSubscriptions.Remove(mdMsg.SecurityId);
			await _publicSocket.UnsubscribeTradesAsync(symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var timeFrame = mdMsg.GetTimeFrame();
		var candles = await _restClient.GetCandlesAsync(mdMsg.SecurityId.ToCurrency(), timeFrame,
			mdMsg.From, mdMsg.To, cancellationToken) ?? [];

		foreach (var candle in candles.OrderBy(static candle => candle.StartTime))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenPrice = candle.Open,
				ClosePrice = candle.Close,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				TotalVolume = candle.Quantity,
				OpenTime = candle.StartTime.FromUnix(false),
				State = CandleStates.Finished,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private ValueTask SessionOnNewTrade(PoloniexPublicTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade.Symbol.IsEmpty())
			return default;

		var securityId = trade.Symbol.ToStockSharp();
		if (!_wsTradesSubscriptions.Contains(securityId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = securityId,
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = trade.CreateTime.FromUnix(false),
			OriginSide = trade.TakerSide.ToSide(),
			SeqNum = trade.Id,
		}, cancellationToken);
	}

	private ValueTask SessionOnBookChanged(PoloniexBookUpdate book, QuoteChangeStates state,
		CancellationToken cancellationToken)
	{
		if (book.Symbol.IsEmpty())
			return default;

		var securityId = book.Symbol.ToStockSharp();
		if (!_wsBookSubscriptions.Contains(securityId))
			return default;

		static QuoteChange[] ToQuotes(decimal[][] levels)
			=> [.. (levels ?? [])
				.Where(static level => level is { Length: >= 2 })
				.Select(static level => new QuoteChange(level[0], level[1]))];

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityId,
			ServerTime = (book.CreateTime != 0 ? book.CreateTime : book.Timestamp).FromUnix(false),
			State = state,
			Bids = ToQuotes(book.Bids),
			Asks = ToQuotes(book.Asks),
			SeqNum = book.Id,
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(PoloniexTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker.Symbol.IsEmpty())
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(),
			ServerTime = (ticker.Timestamp != 0 ? ticker.Timestamp : ticker.CloseTime)
				.FromUnix(false),
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidQuantity)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskQuantity)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Quantity), cancellationToken);
	}

	private static decimal? ToStep(int? scale)
	{
		if (scale is null or < 0 or > 28)
			return null;

		var result = 1m;
		for (var i = 0; i < scale; i++)
			result /= 10m;
		return result;
	}

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;
}
