namespace StockSharp.Tradier;

public partial class TradierMessageAdapter
{
	private static string GetExchangeBoard(string exchange)
	{
		if (exchange.IsEmpty())
			exchange = BoardCodes.Tradier;

		return exchange;
	}

	private readonly SynchronizedDictionary<(string symbol, DataType dt), long> _mdTransIds = [];

	private static (string, DataType) CreateKey(string symbol, DataType dt)
		=> (symbol.ToUpperInvariant(), dt);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (secTypes.Contains(SecurityTypes.Option))
		{
			var underlying = lookupMsg.GetUnderlyingCode().IsEmpty(lookupMsg.SecurityId.SecurityCode);
			var expirations = lookupMsg.ExpiryDate == null ? await _httpClient.GetOptionExpirations(underlying, cancellationToken) : [lookupMsg.ExpiryDate.Value];

			foreach (var expiration in expirations)
			{
				foreach (var option in await _httpClient.GetOptionChain(underlying, expiration, cancellationToken))
				{
					var secMsg = new SecurityMessage
					{
						SecurityId = new()
						{
							SecurityCode = option.Symbol,
							BoardCode = GetExchangeBoard(option.Exchange),
						},
						SecurityType = SecurityTypes.Option,
						Name = option.Description,
						OptionType = option.OptionType.ToOptionType(),
						Multiplier = (decimal)option.ContractSize,
						Strike = option.Strike.ToDecimal(),
						ExpiryDate = expiration,
						OriginalTransactionId = lookupMsg.TransactionId,
					}.TryFillUnderlyingId(underlying);

					if (!secMsg.IsMatch(lookupMsg, secTypes))
						continue;

					await SendOutMessageAsync(secMsg, cancellationToken);

					if (--left <= 0)
						break;
				}

				if (left <= 0)
					break;
			}
		}

		if (left > 0)
		{
			foreach (var symbol in await _httpClient.GetSymbols(lookupMsg.SecurityId.SecurityCode, lookupMsg.SecurityId.BoardCode, secTypes.Select(t => t.TryToNative()).WhereNotNull().Select(t => t.ToNative()).JoinComma(), cancellationToken))
			{
				var secMsg = new SecurityMessage
				{
					SecurityId = new()
					{
						SecurityCode = symbol.Code,
						BoardCode = GetExchangeBoard(symbol.Exchange),
					},
					SecurityType = symbol.Type.ToSecurityType(),
					Name = symbol.Description,
					OriginalTransactionId = lookupMsg.TransactionId,
				};

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.Fields?.FirstOrDefault() == Level1Fields.Dividend)
			{
				var dividends = await _httpClient.GetDividends(symbol, cancellationToken);

				foreach (var dividend in dividends)
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						ServerTime = dividend.PayDate ?? dividend.DeclarationDate ?? dividend.RecordDate.Value,
					}
					.TryAdd(Level1Fields.Dividend, dividend.CashAmount.ToDecimal())
					, cancellationToken);
				}

				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_mdTransIds[CreateKey(symbol, DataType.Level1)] = mdMsg.TransactionId;
				await _mdClient.SubscribeQuote(mdMsg.TransactionId, symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_mdTransIds.Remove(CreateKey(symbol, DataType.Level1));
			await _mdClient.UnSubscribeQuote(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				_mdTransIds[CreateKey(symbol, DataType.Ticks)] = mdMsg.TransactionId;
				await _mdClient.SubscribeTrades(mdMsg.TransactionId, symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_mdTransIds.Remove(CreateKey(symbol, DataType.Ticks));
			await _mdClient.UnSubscribeTrades(mdMsg.OriginalTransactionId, symbol, cancellationToken);
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
			var candles = await _httpClient.GetCandles(tf.TotalDays < 1, symbol, tfName, mdMsg.From, mdMsg.To, mdMsg.IsRegularTradingHours == true, cancellationToken);

			foreach (var candle in candles)
			{
				await ProcessCandle(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);
			}

			//if (!mdMsg.IsHistoryOnly())
			//	await _mdClient.SubscribeTimeSales(symbol, cancellationToken);

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			//await _mdClient.UnSubscribeTimeSales(symbol, cancellationToken);
		}
	}

	private ValueTask ProcessCandle(Ohlc candle, SecurityId securityId, TimeSpan timeFrame, long originTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityId,
			TypedArg = timeFrame,
			OpenPrice = candle.Open.ToDecimal() ?? 0,
			ClosePrice = candle.Close.ToDecimal() ?? 0,
			HighPrice = candle.High.ToDecimal() ?? 0,
			LowPrice = candle.Low.ToDecimal() ?? 0,
			TotalVolume = candle.Volume.ToDecimal() ?? 0,
			OpenTime = (candle.Time ?? candle.Date).Value,
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private ValueTask SessionOnQuoteReceived(Quote quote, CancellationToken cancellationToken)
	{
		if (!_mdTransIds.TryGetValue(CreateKey(quote.Symbol, DataType.Level1), out var transId))
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transId,
			ServerTime = quote.BidDate ?? quote.BidDate2 ?? quote.AskDate ?? quote.AskDate2 ?? CurrentTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, (quote.BidSize ?? quote.BidSize2)?.ToDecimal())
		.TryAdd(Level1Fields.BestBidTime, quote.BidDate)
		.TryAdd(Level1Fields.BestAskPrice, quote.Ask?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, (quote.AskSize ?? quote.AskSize2)?.ToDecimal())
		.TryAdd(Level1Fields.BestAskTime, quote.AskDate)
		.TryAdd(Level1Fields.OpenPrice, quote.Open?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, quote.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, quote.Low?.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, quote.Close?.ToDecimal())
		.TryAdd(Level1Fields.Change, quote.Change?.ToDecimal())
		.TryAdd(Level1Fields.VWAP, quote.AverageVolume?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice52Week, quote.Week52High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice52Week, quote.Week52Low?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, quote.Last?.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, quote.LastVolume?.ToDecimal())
		.TryAdd(Level1Fields.LastTradeTime, quote.TradeDate)
		, cancellationToken);
	}

	private ValueTask SessionOnTradeReceived(Trade trade, CancellationToken cancellationToken)
	{
		if (!_mdTransIds.TryGetValue(CreateKey(trade.Symbol, DataType.Ticks), out var transId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transId,
			ServerTime = trade.Time,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Size,
		}, cancellationToken);
	}

	private ValueTask SessionOnSummaryReceived(Summary summary, CancellationToken cancellationToken)
	{
		if (!_mdTransIds.TryGetValue(CreateKey(summary.Symbol, DataType.Level1), out var transId))
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transId,
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.OpenPrice, summary.Open?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, summary.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, summary.Low?.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, summary.Close?.ToDecimal())
		, cancellationToken);
	}
}
