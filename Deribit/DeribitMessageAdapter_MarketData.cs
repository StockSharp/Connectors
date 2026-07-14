namespace StockSharp.Deribit;

public partial class DeribitMessageAdapter
{
	private readonly SynchronizedSet<SecurityId> _orderBooks = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var currency in await _httpClient.GetCurrencies(cancellationToken))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = currency.Currency.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(Level1Fields.CommissionTaker, currency.WithdrawalFee?.ToDecimal()), cancellationToken);

			foreach (var symbol in await _httpClient.GetInstruments(currency.Currency, cancellationToken))
			{
				var secMsg = new SecurityMessage
				{
					SecurityId = symbol.Instrument.ToStockSharp(),
					SecurityType = symbol.Kind.ToSecurityType(this),
					//Currency = symbol.Currency.FromMicexCurrencyName(SendOutError),
					Strike = symbol.Strike?.ToDecimal(),
					OptionType = symbol.OptionType.IsEmpty() ? null : symbol.OptionType.ToOptionType(),
					MinVolume = symbol.MinTradeAmount?.ToDecimal(),
					PriceStep = symbol.TickSize?.ToDecimal(),
					//Decimals = symbol.PricePrecision,
					VolumeStep = symbol.ContractSize?.ToDecimal(),
					ExpiryDate = symbol.Expiration,
					OriginalTransactionId = lookupMsg.TransactionId,
				}.TryFillUnderlyingId(symbol.BaseCurrency.ToUpperInvariant());

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = secMsg.SecurityId,
					ServerTime = CurrentTime,
				}
				.TryAdd(Level1Fields.CommissionTaker, (decimal?)symbol.TakerCommission)
				.TryAdd(Level1Fields.CommissionMaker, (decimal?)symbol.MakerCommission), cancellationToken);

				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		//_pusherClient.RequestInstruments(lookupMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTicker(transId, mdMsg.SecurityId.ToCurrency(), cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTicker(mdMsg.OriginalTransactionId, transId, mdMsg.SecurityId.ToCurrency(), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBook(transId, mdMsg.SecurityId.ToCurrency(), cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeOrderBook(mdMsg.OriginalTransactionId, transId, mdMsg.SecurityId.ToCurrency(), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var currency = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				const int count = 1000;

				var last = from;

				while (last < to)
				{
					var trades = await _httpClient.GetTrades(mdMsg.SecurityId.ToCurrency(), (long)last.ToUnix(false), null, count, cancellationToken);

					var needFinish = true;

					foreach (var trade in trades.OrderBy(t => t.TimeStamp))
					{
						if (trade.TimeStamp <= last)
							continue;

						if (trade.TimeStamp > to)
						{
							needFinish = true;
							break;
						}

						needFinish = false;
						last = trade.TimeStamp;

						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Ticks,
							SecurityId = trade.Instrument.ToStockSharp(),
							TradeStringId = trade.Id,
							TradePrice = (decimal)trade.Price,
							TradeVolume = (decimal)trade.Quantity,
							ServerTime = trade.TimeStamp,
							OriginSide = trade.Direction.ToSide(),
							OriginalTransactionId = transId,
						}, cancellationToken);

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (needFinish)
						break;

					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTrades(transId, currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTrades(mdMsg.OriginalTransactionId, transId, currency, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var currency = mdMsg.SecurityId.ToCurrency();
		var tf = mdMsg.GetTimeFrame();
		var resolutuon = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var last = from;

				while (last < to)
				{
					var end = last + tf.Multiply(1000);
					var candles = await _httpClient.GetCandles(currency, (long)last.ToUnix(false), (long)end.ToUnix(false), resolutuon, cancellationToken);

					var needFinish = false;

					foreach (var candle in candles)
					{
						var time = candle.Tick.FromUnix(false);

						if (time <= last)
							continue;

						if (time > to)
						{
							needFinish = true;
							break;
						}

						await SendOutMessageAsync(new TimeFrameCandleMessage
						{
							OriginalTransactionId = transId,
							OpenTime = candle.Tick.FromUnix(false),
							OpenPrice = (decimal)candle.Open,
							HighPrice = (decimal)candle.High,
							LowPrice = (decimal)candle.Low,
							ClosePrice = (decimal)candle.Close,
							TotalVolume = (decimal)candle.Volume,
							State = CandleStates.Finished,
						}, cancellationToken);

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (needFinish)
						break;

					await IterationInterval.Delay(cancellationToken);

					last = end + tf;
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeCandles(transId, currency, resolutuon, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeCandles(mdMsg.OriginalTransactionId, transId, currency, resolutuon, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeAnnouncements(transId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeAnnouncements(mdMsg.OriginalTransactionId, transId, cancellationToken);
	}

	private async ValueTask SessionOnNewTrades(IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = trade.Instrument.ToStockSharp(),
				TradeStringId = trade.Id,
				TradePrice = (decimal)trade.Price,
				TradeVolume = (decimal)trade.Quantity,
				ServerTime = trade.TimeStamp,
				OriginSide = trade.Direction.ToSide(),
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookChanged(OrderBook book, CancellationToken cancellationToken)
	{
		var secId = book.Instrument.ToStockSharp();

		var state = _orderBooks.TryAdd(secId) ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment;

		static QuoteChange ToChange(OrderBookEntry entry)
			=> new((decimal)entry.Price, (decimal)entry.Quantity);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = book.Timestamp,
			Bids = book.Bids?.Select(ToChange).ToArray() ?? [],
			Asks = book.Asks?.Select(ToChange).ToArray() ?? [],
			State = state,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewNewAnnouncement(Announcement announcement, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new NewsMessage
		{
			Id = announcement.Id,
			ServerTime = announcement.Timestamp,
			Headline = announcement.Title,
			Story = announcement.Body,
			Priority = announcement.Important ? NewsPriorities.High : NewsPriorities.Regular
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Instrument.ToStockSharp(),
			ServerTime = ticker.Timestamp,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidAmount?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskAmount?.ToDecimal())
		.TryAdd(Level1Fields.SettlementPrice, ticker.SettlementPrice?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.Stats?.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Stats?.Low?.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, ticker.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Stats?.Volume?.ToDecimal())
		.TryAdd(Level1Fields.MinPrice, ticker.MinPrice?.ToDecimal())
		.TryAdd(Level1Fields.MaxPrice, ticker.MaxPrice?.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest?.ToDecimal())
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice?.ToDecimal())
		.TryAdd(Level1Fields.ImpliedVolatility, ticker.MarkIv?.ToDecimal())
		.TryAdd(Level1Fields.Delta, ticker.Greeks?.Delta?.ToDecimal())
		.TryAdd(Level1Fields.Gamma, ticker.Greeks?.Gamma?.ToDecimal())
		.TryAdd(Level1Fields.Theta, ticker.Greeks?.Theta?.ToDecimal())
		.TryAdd(Level1Fields.Vega, ticker.Greeks?.Vega?.ToDecimal())
		.TryAdd(Level1Fields.Rho, ticker.Greeks?.Rho?.ToDecimal())
		.TryAdd(Level1Fields.State, ticker.State.ToSecurityState()), cancellationToken);
	}

	private async ValueTask SessionOnNewCandles(long transId, IEnumerable<Ohlc> candles, CancellationToken cancellationToken)
	{
		foreach (var candle in candles)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = transId,
				OpenTime = candle.Tick.FromUnix(false),
				OpenPrice = (decimal)candle.Open,
				HighPrice = (decimal)candle.High,
				LowPrice = (decimal)candle.Low,
				ClosePrice = (decimal)candle.Close,
				TotalVolume = (decimal)candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		//SendSubscriptionFinished(transId);
	}
}
