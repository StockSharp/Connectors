namespace StockSharp.HitBtc;

partial class HitBtcMessageAdapter
{
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedSet<SecurityId> _bookSubscriptions = [];
	private readonly SynchronizedSet<SecurityId> _tradeSubscriptions = [];
	private readonly SynchronizedSet<(SecurityId securityId, TimeSpan timeFrame)>
		_candleSubscriptions = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var symbols = await _restClient.GetSymbolsAsync(cancellationToken) ?? [];
		var securityTypes = lookupMsg.GetSecurityTypes();

		foreach (var symbol in symbols)
		{
			if (symbol.Id.IsEmpty() || symbol.BaseCurrency.IsEmpty() ||
				symbol.QuoteCurrency.IsEmpty() || !symbol.Type.EqualsIgnoreCase("spot"))
				continue;

			var securityId = symbol.ToStockSharp();
			var security = new SecurityMessage
			{
				SecurityId = securityId,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = symbol.TickSize,
				VolumeStep = symbol.QuantityIncrement,
			}.FillDefaultCryptoFields();

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			RememberSecurity(symbol.Id, securityId);
			await SendOutMessageAsync(security, cancellationToken);

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(Level1Fields.State, symbol.Status.EqualsIgnoreCase("working")
				? SecurityStates.Trading
				: SecurityStates.Stoped)
			.TryAdd(Level1Fields.CommissionTaker, symbol.TakeLiquidityRate)
			.TryAdd(Level1Fields.CommissionMaker, symbol.ProvideLiquidityRate), cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();
		RememberSecurity(symbol, mdMsg.SecurityId);

		if (mdMsg.IsSubscribe)
		{
			if (_level1Subscriptions.TryAdd(mdMsg.SecurityId))
				await _publicSocket.SubscribeTickerAsync(symbol, mdMsg.TransactionId,
					cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_level1Subscriptions.Remove(mdMsg.SecurityId))
		{
			await _publicSocket.UnsubscribeTickerAsync(symbol, mdMsg.TransactionId,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();
		RememberSecurity(symbol, mdMsg.SecurityId);

		if (mdMsg.IsSubscribe)
		{
			if (_bookSubscriptions.TryAdd(mdMsg.SecurityId))
				await _publicSocket.SubscribeOrderBookAsync(symbol, mdMsg.TransactionId,
					cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_bookSubscriptions.Remove(mdMsg.SecurityId))
		{
			await _publicSocket.UnsubscribeOrderBookAsync(symbol, mdMsg.TransactionId,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();
		RememberSecurity(symbol, mdMsg.SecurityId);

		if (!mdMsg.IsSubscribe)
		{
			if (_tradeSubscriptions.Remove(mdMsg.SecurityId))
				await _publicSocket.UnsubscribeTradesAsync(symbol, mdMsg.TransactionId,
					cancellationToken);
			return;
		}

		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
		{
			var trades = await _restClient.GetTradesAsync(symbol, mdMsg.From, mdMsg.To,
				mdMsg.Count, cancellationToken) ?? [];

			await ProcessTicksAsync(mdMsg.TransactionId, mdMsg.SecurityId,
				trades.OrderBy(static trade => trade.Time).ThenBy(static trade => trade.Id),
				cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (_tradeSubscriptions.TryAdd(mdMsg.SecurityId))
			await _publicSocket.SubscribeTradesAsync(symbol, mdMsg.TransactionId,
				cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();
		var timeFrame = mdMsg.GetTimeFrame();
		var period = timeFrame.ToNative();
		var subscription = (mdMsg.SecurityId, timeFrame);
		RememberSecurity(symbol, mdMsg.SecurityId);

		if (!mdMsg.IsSubscribe)
		{
			if (_candleSubscriptions.Remove(subscription))
				await _publicSocket.UnsubscribeCandlesAsync(symbol, period, mdMsg.TransactionId,
					cancellationToken);
			return;
		}

		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
		{
			var candles = await _restClient.GetCandlesAsync(symbol, period, mdMsg.From, mdMsg.To,
				mdMsg.Count, cancellationToken) ?? [];

			foreach (var candle in candles.OrderBy(static candle => candle.Time))
			{
				await ProcessCandleAsync(symbol, period, candle, mdMsg.TransactionId,
					candle.Time + timeFrame <= CurrentTime
						? CandleStates.Finished
						: CandleStates.Active,
					cancellationToken);
			}
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (_candleSubscriptions.TryAdd(subscription))
			await _publicSocket.SubscribeCandlesAsync(symbol, period, mdMsg.TransactionId,
				cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(Ticker ticker,
		CancellationToken cancellationToken)
	{
		var securityId = ResolveSecurityId(ticker.Symbol);

		if (!_level1Subscriptions.Contains(securityId))
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = securityId,
			ServerTime = ticker.Time,
		}
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
		.TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);
	}

	private ValueTask SessionOnNewTrades(string symbol, IEnumerable<Trade> trades,
		CancellationToken cancellationToken)
	{
		var securityId = ResolveSecurityId(symbol);

		return _tradeSubscriptions.Contains(securityId)
			? ProcessTicksAsync(0, securityId, trades, cancellationToken)
			: default;
	}

	private async ValueTask ProcessTicksAsync(long transactionId, SecurityId securityId,
		IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = securityId,
				TradeId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Quantity,
				OriginSide = trade.Side.ToSide(),
				OriginalTransactionId = transactionId,
				ServerTime = trade.Time,
				SeqNum = trade.Id,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookChanged(OrderBook book, QuoteChangeStates state,
		CancellationToken cancellationToken)
	{
		var securityId = ResolveSecurityId(book.Symbol);

		if (!_bookSubscriptions.Contains(securityId))
			return default;

		QuoteChange[] ToQuotes(decimal[][] levels)
			=> [.. (levels ?? [])
				.Where(static level => level is { Length: >= 2 })
				.Where(level => state == QuoteChangeStates.Increment || level[1] != 0)
				.Select(static level => new QuoteChange(level[0], level[1]))];

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityId,
			Bids = ToQuotes(book.Bids),
			Asks = ToQuotes(book.Asks),
			State = state,
			ServerTime = book.Timestamp.FromHitBtcMilliseconds(),
			SeqNum = book.Sequence,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(string symbol, string period, Ohlc candle,
		CancellationToken cancellationToken)
	{
		var timeFrame = period.ToTimeFrame();
		var securityId = ResolveSecurityId(symbol);

		return _candleSubscriptions.Contains((securityId, timeFrame))
			? ProcessCandleAsync(symbol, period, candle, 0,
				candle.Time + timeFrame <= CurrentTime
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken)
			: default;
	}

	private ValueTask ProcessCandleAsync(string symbol, string period, Ohlc candle,
		long originTransId, CandleStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = ResolveSecurityId(symbol),
			TypedArg = period.ToTimeFrame(),
			OpenPrice = candle.Open,
			ClosePrice = candle.Close,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			TotalVolume = candle.Volume,
			OpenTime = candle.Time,
			State = state,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private void RememberSecurity(string symbol, SecurityId securityId)
	{
		if (!symbol.IsEmpty())
			_securityIds[symbol] = securityId;
	}

	private SecurityId ResolveSecurityId(string symbol)
		=> _securityIds.TryGetValue(symbol, out var securityId)
			? securityId
			: symbol.ToStockSharp();
}
