namespace StockSharp.Poloniex;

partial class PoloniexMessageAdapter
{
	private int _level1Counter;
	//private int _trollboxCounter;
	private readonly SynchronizedPairSet<int, SecurityId> _tickerIds = [];
	private readonly SynchronizedPairSet<int, SecurityId> _currencyIds = [];

	private readonly HashSet<SecurityId> _wsSubscriptions = [];
	private readonly SynchronizedSet<SecurityId> _wsBookSubscriptions = [];
	private readonly SynchronizedSet<SecurityId> _wsTradesSubscriptions = [];

	private async Task EnsureIdAsync(CancellationToken cancellationToken)
	{
		if (_tickerIds.Count == 0)
		{
			foreach (var pair in await _httpClient.GetTickersAsync(cancellationToken))
				_tickerIds[pair.Value.TickerId] = pair.Key.ToStockSharp();
		}

		if (_currencyIds.Count == 0)
		{
			var currencies = await _httpClient.GetCurrenciesAsync(cancellationToken);

			foreach (var pair in currencies)
				_currencyIds[pair.Value.Id] = pair.Key.ToStockSharp();
		}
	}

	private async Task<int> EnsureTickerIdAsync(SecurityId secId, CancellationToken cancellationToken)
	{
		await EnsureIdAsync(cancellationToken);

		if (_tickerIds.TryGetKey(secId, out var tickerId))
			return tickerId;

		throw new ArgumentOutOfRangeException(nameof(secId), secId, LocalizedStrings.InvalidValue);
	}

	private SecurityId EnsureSecId(int tickerId, bool isCurrency = false)
	{
		if (isCurrency)
		{
			if (_currencyIds.TryGetValue(tickerId, out var secId))
				return secId;
		}
		else
		{
			if (_tickerIds.TryGetValue(tickerId, out var secId))
				return secId;
		}

		throw new ArgumentOutOfRangeException(nameof(tickerId), tickerId, LocalizedStrings.InvalidValue);
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();

		var tickers = await _httpClient.GetTickersAsync(cancellationToken);

		foreach (var pair in tickers)
		{
			var secId = pair.Key.ToStockSharp();
			var ticker = pair.Value;

			_tickerIds[ticker.TickerId] = secId;

			var secMsg = new SecurityMessage
			{
				SecurityId = secId,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			await SessionOnTickerChanged(ticker.TickerId, ticker, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);

		var currencies = await _httpClient.GetCurrenciesAsync(cancellationToken);

		foreach (var pair in currencies)
		{
			var secId = pair.Key.ToStockSharp();

			_currencyIds[pair.Value.Id] = secId;

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = secId,
				ServerTime = CurrentTime,
			}.TryAdd(Level1Fields.CommissionTaker, (decimal?)pair.Value.TxFee), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (_level1Counter == 0)
				await _pusherClient.SubscribeToTicker(cancellationToken);

			_level1Counter++;

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_level1Counter--;

			if (_level1Counter == 0)
				await _pusherClient.UnSubscribeFromTicker(cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_wsBookSubscriptions.Add(mdMsg.SecurityId);

			if (_wsSubscriptions.Add(mdMsg.SecurityId))
				await _pusherClient.SubscribeTicker(await EnsureTickerIdAsync(mdMsg.SecurityId, cancellationToken), cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_wsBookSubscriptions.Remove(mdMsg.SecurityId);

			if (_wsSubscriptions.Remove(mdMsg.SecurityId))
				await _pusherClient.UnSubscribeTicker(await EnsureTickerIdAsync(mdMsg.SecurityId, cancellationToken), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				var trades = await _httpClient.GetTradeHistoryAsync(currency, (long?)mdMsg.From?.ToUnix(), (long?)mdMsg.To?.ToUnix(), cancellationToken);

				foreach (var trade in trades.OrderBy(t => t.GlobalId))
				{
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						SecurityId = mdMsg.SecurityId,
						TradeId = trade.GlobalId,
						TradePrice = (decimal)trade.Rate,
						TradeVolume = (decimal)trade.Amount,
						ServerTime = trade.Date.ToDto(),
						OriginSide = trade.Type.ToSide(),
						OriginalTransactionId = mdMsg.TransactionId,
					}, cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_wsTradesSubscriptions.Add(mdMsg.SecurityId);

				if (_wsSubscriptions.Add(mdMsg.SecurityId))
					await _pusherClient.SubscribeTicker(await EnsureTickerIdAsync(mdMsg.SecurityId, cancellationToken), cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_wsTradesSubscriptions.Remove(mdMsg.SecurityId);
			//_pusherClient.UnSubscribeTrades(currency);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var candles = await _httpClient.GetChartDataAsync(mdMsg.SecurityId.ToCurrency(), (int)mdMsg.GetTimeFrame().TotalSeconds, mdMsg.From?.ToUnix() ?? 1, mdMsg.To?.ToUnix() ?? 9999999999, cancellationToken);

			foreach (var candle in candles)
			{
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					SecurityId = mdMsg.SecurityId,
					TypedArg = mdMsg.GetTimeFrame(),
					OpenPrice = candle.Open,
					ClosePrice = candle.Close,
					HighPrice = candle.High,
					LowPrice = candle.Low,
					TotalVolume = candle.Volume,
					OpenTime = candle.Date.FromUnix(),
					State = CandleStates.Finished,
					OriginalTransactionId = mdMsg.TransactionId,
				}, cancellationToken);
			}

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			// No unsubscribe behavior for candles in original logic
		}
	}

	private ValueTask SessionOnNewTrade(int tickerId, Trade trade, CancellationToken cancellationToken)
	{
		var secId = EnsureSecId(tickerId);

		if (!_wsTradesSubscriptions.Contains(secId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			TradeId = trade.Id == 0 ? null : trade.Id,
			TradePrice = (decimal)trade.Rate,
			TradeVolume = (decimal)trade.Amount,
			ServerTime = trade.Date.FromUnix(),
			OriginSide = trade.Type.ToSide(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookSnapshot(int tickerId, OrderBook book, CancellationToken cancellationToken)
	{
		static QuoteChange ToQuote(OrderEntry entry)
			=> new((decimal)entry.PricePerCoin, (decimal)entry.AmountQuote);

		return ProcessBook(tickerId, () => [.. book.Bids.Select(ToQuote)], () => [.. book.Asks.Select(ToQuote)], QuoteChangeStates.SnapshotComplete, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(int tickerId, Trade trade, CancellationToken cancellationToken)
	{
		QuoteChange[] bids = null, asks = null;

		QuoteChange ToQuote()
			=> new((decimal)trade.Rate, (decimal)trade.Amount);

		if (trade.Type == 1)
			bids = [ToQuote()];
		else
			asks = [ToQuote()];

		return ProcessBook(tickerId, () => bids, () => asks, QuoteChangeStates.Increment, cancellationToken);
	}

	private ValueTask ProcessBook(int tickerId, Func<QuoteChange[]> getBids, Func<QuoteChange[]> getAsks, QuoteChangeStates state, CancellationToken cancellationToken)
	{
		var secId = EnsureSecId(tickerId);

		//if (!_wsBookSubscriptions.Contains(info.Item1))
		//	return;

		var quotesMsg = new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = CurrentTime,
			State = state,
		};

		var bids = getBids();
		if (bids != null)
			quotesMsg.Bids = bids;

		var asks = getAsks();
		if (asks != null)
			quotesMsg.Asks = asks;

		return SendOutMessageAsync(quotesMsg, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(int tickerId, Ticker ticker, CancellationToken cancellationToken)
	{
		var secId = EnsureSecId(tickerId);

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = secId,
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.HighBidPrice, (decimal?)ticker.HighestBid)
		.TryAdd(Level1Fields.LowAskPrice, (decimal?)ticker.LowestAsk)
		.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.Last)
		.TryAdd(Level1Fields.Volume, (decimal?)ticker.QuoteVolume)
		.TryAdd(Level1Fields.State, ticker.IsFrozen ? SecurityStates.Stoped : SecurityStates.Trading), cancellationToken);
	}
}
