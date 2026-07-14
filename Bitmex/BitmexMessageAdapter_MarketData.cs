namespace StockSharp.Bitmex;

public partial class BitmexMessageAdapter
{
	private readonly Dictionary<long, bool> _buildFromFields = new();
	private readonly SynchronizedSet<string> _olSnapshots = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _liveQuoteCandles = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _liveTradeCandles = new(StringComparer.InvariantCultureIgnoreCase);
	private const long _maxFetch = 500;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var symbols = (await _httpClient.GetInstrumentsActiveAndIndices(cancellationToken)).ToArray();
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in symbols)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var type = symbol.Typ.Iso10962ToSecurityType();

			if (type == SecurityTypes.Currency)
				type = SecurityTypes.CryptoCurrency;

			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Id.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
				Multiplier = symbol.Multiplier < 0 ? null : symbol.Multiplier?.ToDecimal(),
				SecurityType = type,
				UnderlyingSecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = symbol.TickSize?.ToDecimal(),
				VolumeStep = symbol.LotSize?.ToDecimal(),
				ExpiryDate = symbol.Expiry,
				SettlementDate = symbol.Settle,
				MaxVolume = (decimal?)symbol.MaxOrderQty,
			}.TryFillUnderlyingId(symbol.UnderlyingSymbol?.ToUpperInvariant());

			if (symbol.OptionStrikePrice > 0)
				secMsg.Strike = symbol.OptionStrikePrice.Value.ToDecimal();

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);

		await SessionOnTickersChanged(Actions.Partial, symbols, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTicker(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTicker(mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBook(mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderLog(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderLog(mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var ids = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
				var last = from;

				while (last < to)
				{
					var required = _maxFetch.Min(left);
					var trades = await _httpClient.GetTrades(symbol, required, default, last, default, cancellationToken);

					var needFinish = false;

					foreach (var trade in trades.OrderBy(t => t.Timestamp))
					{
						cancellationToken.ThrowIfCancellationRequested();

						if (trade.Timestamp < last)
							continue;

						if (trade.Timestamp > to)
						{
							needFinish = true;
							break;
						}

						if (!ids.Add(trade.MatchId))
							continue;

						await ProcessTick(mdMsg.TransactionId, trade, cancellationToken);
						last = trade.Timestamp;

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (needFinish || trades.Length < (required / 2))
						break;

					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTrades(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTrades(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var last = from;

				while (last < to)
				{
					var required = _maxFetch.Min(left);
					var candles = await _httpClient.GetCandles(symbol, tfName, required, default, last, default, cancellationToken);

					var needFinish = false;

					foreach (var ohlc in candles.OrderBy(c => c.Timestamp))
					{
						if (ohlc.Timestamp <= last)
							continue;

						if (ohlc.Timestamp > to)
						{
							needFinish = true;
							break;
						}

						await ProcessCandle(tf, mdMsg.TransactionId, ohlc, cancellationToken);
						last = ohlc.Timestamp;

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (needFinish || candles.Length < (required / 2))
						break;

					last += tf;
					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				var isTrade = mdMsg.BuildField == null || mdMsg.BuildField == Level1Fields.LastTradePrice;

				_buildFromFields.Add(mdMsg.TransactionId, isTrade);

				var dict = isTrade ? _liveTradeCandles : _liveQuoteCandles;

				using (dict.EnterScope())
				{
					if (!dict.ContainsKey(symbol))
						dict.Add(symbol, mdMsg.TransactionId);
				}

				await _pusherClient.SubscribeCandles(mdMsg.TransactionId, symbol, isTrade, tfName, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeCandles(mdMsg.OriginalTransactionId, symbol, _buildFromFields.TryGetValue(mdMsg.OriginalTransactionId), tfName, cancellationToken);
	}

	private ValueTask ProcessTick(long transactionId, Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			TradeStringId = trade.MatchId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Size?.ToDecimal(),
			ServerTime = trade.Timestamp,
			IsUpTick = trade.TickDirection.ToTickDirection(),
			OriginSide = trade.Side.ToSide(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask ProcessCandle(TimeSpan timeFrame, long originTransId, TradeOhlc ohlc, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = ohlc.Symbol.ToStockSharp(),
			TypedArg = timeFrame,
			OpenPrice = ohlc.Open?.ToDecimal() ?? 0,
			HighPrice = ohlc.High?.ToDecimal() ?? 0,
			LowPrice = ohlc.Low?.ToDecimal() ?? 0,
			ClosePrice = ohlc.Close?.ToDecimal() ?? 0,
			TotalVolume = ohlc.Volume?.ToDecimal() ?? 0,
			OpenTime = ohlc.Timestamp - timeFrame,
			TotalTicks = ohlc.Trades == 0 ? null : ohlc.Trades,
			OriginalTransactionId = originTransId,
			State = CandleStates.Finished,
		}, cancellationToken);
	}

	private ValueTask ProcessCandle(TimeSpan timeFrame, long originTransId, QuoteOhlc candle, CancellationToken cancellationToken)
	{
		var openPrice = candle.BidPrice?.ToDecimal() ?? 0;
		var closePrice = candle.AskPrice?.ToDecimal() ?? 0;

		if (openPrice == 0 || closePrice == 0)
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Symbol.ToStockSharp(),
			TypedArg = timeFrame,
			OpenPrice = openPrice,
			ClosePrice = closePrice,
			HighPrice = closePrice,
			LowPrice = openPrice,
			TotalVolume = (candle.AskSize + candle.BidSize)?.ToDecimal() ?? 0,
			OpenTime = candle.Timestamp,
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private async ValueTask SessionOnTickersChanged(string action, IEnumerable<Symbol> tickers, CancellationToken cancellationToken)
	{
		foreach (var symbol in tickers)
		{
			var level1 = new Level1ChangeMessage
			{
				SecurityId = symbol.Id.ToStockSharp(),
				ServerTime = symbol.Timestamp,
			}
			.TryAdd(Level1Fields.LastTradePrice, symbol.LastPrice?.ToDecimal())
			.TryAdd(Level1Fields.LastTradeUpDown, symbol.LastTickDirection.ToTickDirection())
			.TryAdd(Level1Fields.BestAskPrice, symbol.AskPrice?.ToDecimal())
			.TryAdd(Level1Fields.BestBidPrice, symbol.BidPrice?.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, symbol.HighPrice?.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, symbol.LowPrice?.ToDecimal())
			.TryAdd(Level1Fields.Volume, symbol.Volume?.ToDecimal())
			.TryAdd(Level1Fields.SettlementPrice, symbol.SettledPrice?.ToDecimal())
			.TryAdd(Level1Fields.VWAP, symbol.Vwap?.ToDecimal())
			.TryAdd(Level1Fields.CommissionMaker, symbol.MakerFee?.ToDecimal())
			.TryAdd(Level1Fields.CommissionTaker, symbol.TakerFee?.ToDecimal())
			.TryAdd(Level1Fields.Turnover, symbol.Turnover?.ToDecimal())
			.TryAdd(Level1Fields.OpenInterest, symbol.OpenInterest?.ToDecimal())
			.TryAdd(Level1Fields.Index, symbol.MarkPrice?.ToDecimal())
			//.TryAdd(Level1Fields.Multiplier, symbol.Multiplier?.ToDecimal())
			.TryAdd(Level1Fields.MinPrice, symbol.LimitDownPrice?.ToDecimal())
			.TryAdd(Level1Fields.MaxPrice, symbol.LimitUpPrice?.ToDecimal())
			;

			if (level1.HasChanges())
				await SendOutMessageAsync(level1, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewTrades(string action, IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await ProcessTick(0, trade, cancellationToken);
		}
	}

	private async ValueTask SessionOnOrderBooksChanged(string action, IEnumerable<OrderBook> books, CancellationToken cancellationToken)
	{
		foreach (var book in books)
		{
			if (book.Symbol.IsEmpty())
				continue;

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = book.Symbol.ToStockSharp(),
				Bids = book.Bids.Select(p => new QuoteChange(p.Price, p.Size)).ToArray(),
				Asks = book.Asks.Select(p => new QuoteChange(p.Price, p.Size)).ToArray(),
				ServerTime = book.Timestamp,
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewQuoteCandles(string action, string interval, IEnumerable<QuoteOhlc> candles, CancellationToken cancellationToken)
	{
		var timeFrame = TimeSpan.Zero;

		foreach (var candle in candles)
		{
			if (!_liveQuoteCandles.TryGetValue(candle.Symbol, out var originTransId))
				continue;

			await ProcessCandle(timeFrame, originTransId, candle, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewTradeCandles(string action, string interval, IEnumerable<TradeOhlc> candles, CancellationToken cancellationToken)
	{
		var timeFrame = interval.ToTimeFrame();

		foreach (var candle in candles)
		{
			if (!_liveTradeCandles.TryGetValue(candle.Symbol, out var originTransId))
				continue;

			await ProcessCandle(timeFrame, originTransId, candle, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewOrderLog(string action, IEnumerable<Level2> levels, CancellationToken cancellationToken)
	{
		var isSnapshot = action == Actions.Partial;

		foreach (var level2 in levels)
		{
			if (!_olSnapshots.Contains(level2.Symbol))
			{
				// from doc: If you receive any messages before the partial, ignore them.
				// https://www.bitmex.com/app/wsAPI#Subscriptions
				if (!isSnapshot)
					continue;

				_olSnapshots.Add(level2.Symbol);
			}

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.OrderLog,
				SecurityId = level2.Symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				OrderPrice = level2.Price.ToDecimal() ?? 0,
				OrderVolume = level2.Size.ToDecimal(),
				Side = level2.Side.ToSide(),
				OrderId = level2.Id,
				OrderState = action == Actions.Delete ? OrderStates.Done : OrderStates.Active,
			}, cancellationToken);
		}
	}
}
