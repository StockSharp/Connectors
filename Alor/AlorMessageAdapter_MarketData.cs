namespace StockSharp.Alor;

public partial class AlorMessageAdapter
{
	private readonly SynchronizedDictionary<SecurityId, Security> _secMap = [];

	private async Task<Security> EnsureGetSecurity(SecurityId secId, CancellationToken cancellationToken)
	{
		if (!_secMap.TryGetValue(secId, out var sec))
		{
			sec = await _httpClient
				.GetSecurities(
					query: secId.SecurityCode,
					limit: 100,
					cancellationToken: cancellationToken)
				.FirstOrDefaultAsync(cancellationToken);

			if (sec is null)
				throw new ArgumentOutOfRangeException(nameof(secId), secId, LocalizedStrings.InvalidValue);

			_secMap.TryAdd2(secId, sec);
		}
		
		return sec;
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var batch = (int)5000L.Min(left);

		await foreach (var sec in _httpClient.GetSecurities(
				query: lookupMsg.SecurityId.SecurityCode,
				limit: batch,
				cficode: lookupMsg.CfiCode,
				exchange: lookupMsg.SecurityId.BoardCode,
				format: "heavy",
				includeOptions: secTypes.Contains(SecurityTypes.Option),
				cancellationToken: cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			var secCode = sec.Symbol;
			var shortName = sec.ShortName;
			var name = sec.Description;

			var secType = sec.Type.ToSecurityType();

			//var isFutures = sec.Type?.StartsWithIgnoreCase("�������") == true;

			//if (isFutures)
			//{
			//	secCode = sec.ShortName;
			//	name = sec.Type;
			//	shortName = sec.Symbol;
			//}

			var secMsg = new SecurityMessage
			{
				SecurityId = new() { SecurityCode = secCode, BoardCode = sec.Board, Isin = sec.ISIN },
				ShortName = shortName,
				Name = name,
				FaceValue = sec.FaceValue?.ToDecimal(),
				CfiCode = sec.CfiCode,
				PriceStep = sec.PriceStep?.ToDecimal(),
				VolumeStep = sec.MinStep?.ToDecimal(),
				Decimals = sec.RoundTo,
				MinVolume = sec.LotSize?.ToDecimal(),
				Multiplier = sec.PriceMultiplier?.ToDecimal(),
				Currency = sec.Currency.FromMicexCurrencyName(this.AddErrorLog),
				SecurityType = secType,
				OriginalTransactionId = lookupMsg.TransactionId,
				ExpiryDate = sec.Cancellation,
				SettlementDate = sec.Cancellation,
				IssueSize = sec.PriceShownUnits?.ToDecimal(),
				IssueDate = sec.Cancellation,
				Strike = sec.StrikePrice?.ToDecimal(),
				OptionType = sec.OptionSide.ToOptionType(),
			};

			if (!sec.UnderlyingSymbol.IsEmpty())
				secMsg.TryFillUnderlyingId(sec.UnderlyingSymbol);

			_secMap[secMsg.SecurityId] = sec;
			_secMapByBrokSymbol[$"{sec.Exchange}:{sec.Symbol}"] = secMsg.SecurityId;

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	private async Task UnSubscribe(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		foreach (var subId in GetSubTransId(mdMsg.OriginalTransactionId))
			await _dataSocketClient.UnSubscribe(subId, cancellationToken);

		await _dataSocketClient.UnSubscribe(mdMsg.OriginalTransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnSubscribe(mdMsg, cancellationToken);
			return;
		}

		var secId = mdMsg.SecurityId;
		var secCode = secId.SecurityCode;
		var sec = await EnsureGetSecurity(secId, cancellationToken);
		var exchange = sec.Exchange;

		var tf = mdMsg.GetTimeFrame();
		var from = mdMsg.From;

		if (from is not null)
		{
			var to = mdMsg.To ?? DateTime.UtcNow;
			var left = mdMsg.Count ?? long.MaxValue;
			var tfNative = tf.ToNative();
			var step = tf.Multiply(10000);

			while (from < to)
			{
				var last = from.Value + step;
				var needBreak = false;

				if (last > to)
				{
					last = to;
					needBreak = true;
				}

				var candles = _httpClient.GetCandles(sec.Symbol, exchange, tfNative, (long)from.Value.ToUnix(), (long)last.ToUnix(), cancellationToken);

				await foreach (var candle in candles)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (candle.Time < from)
						continue;

					if (candle.Time > to)
					{
						needBreak = true;
						break;
					}

					cancellationToken.ThrowIfCancellationRequested();

					await ProcessCandleAsync(transId, candle, CandleStates.Finished, cancellationToken);

					if (--left <= 0)
					{
						needBreak = true;
						break;
					}

					from = candle.Time;
				}

				if (needBreak)
					break;
			}
		}

		if (!mdMsg.IsHistoryOnly())
			await _dataSocketClient.SubscribeOhlc(secCode, exchange, (int)tf.TotalSeconds, (long?)from?.ToUnix() ?? 0, false, transId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnSubscribe(mdMsg, cancellationToken);
			return;
		}

		var secId = mdMsg.SecurityId;
		var secCode = secId.SecurityCode;
		var sec = await EnsureGetSecurity(secId, cancellationToken);

		if (!mdMsg.IsHistoryOnly())
			await _dataSocketClient.SubscribeTicks(secCode, sec.Exchange, transId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnSubscribe(mdMsg, cancellationToken);
			return;
		}

		var secId = mdMsg.SecurityId;
		var secCode = secId.SecurityCode;
		var sec = await EnsureGetSecurity(secId, cancellationToken);

		if (!mdMsg.IsHistoryOnly())
			await _dataSocketClient.SubscribeOrderBook(secCode, sec.Exchange, mdMsg.MaxDepth ?? 5, transId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnSubscribe(mdMsg, cancellationToken);
			return;
		}

		var secId = mdMsg.SecurityId;
		var secCode = secId.SecurityCode;
		var sec = await EnsureGetSecurity(secId, cancellationToken);

		if (!mdMsg.IsHistoryOnly())
		{
			await _dataSocketClient.SubscribeQuote(secCode, sec.Exchange, transId, cancellationToken);
			await _dataSocketClient.SubscribeStatus(secCode, sec.Exchange, AddSubTransId(transId), cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask OnOrderBook(long id, OrderBook obj, CancellationToken cancellationToken)
	{
		static QuoteChange ToChange(OrderBookEntry entry)
			=> new((decimal)entry.Price, (decimal)entry.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = id,
			Bids = obj.Bids?.Select(ToChange).ToArray() ?? [],
			Asks = obj.Asks?.Select(ToChange).ToArray() ?? [],
			ServerTime = obj.Timestamp,
		}, cancellationToken);
	}

	private ValueTask OnQuote(long id, Quote obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = CurrentTime,
			OriginalTransactionId = id,
		}
		.TryAdd(Level1Fields.BestBidPrice, obj.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, obj.Ask?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, obj.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.LastTradeTime, obj.LastPriceTimestamp)
		.TryAdd(Level1Fields.Change, obj.Change?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, obj.LowPrice?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, obj.HighPrice?.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, obj.OpenPrice?.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, obj.PrevClosePrice?.ToDecimal())
		.TryAdd(Level1Fields.Volume, obj.Volume?.ToDecimal())
		.TryAdd(Level1Fields.AccruedCouponIncome, obj.AccruedInt?.ToDecimal())
		.TryAdd(Level1Fields.Yield, obj.Yield?.ToDecimal())
		.TryAdd(Level1Fields.VolumeStep, obj.LotSize?.ToDecimal())
		.TryAdd(Level1Fields.BidsVolume, obj.TotalBidVol?.ToDecimal())
		.TryAdd(Level1Fields.AsksVolume, obj.TotalAskVol?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(long id, Ohlc obj, CandleStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = obj.Open?.ToDecimal() ?? 0,
			ClosePrice = obj.Close?.ToDecimal() ?? 0,
			HighPrice = obj.High?.ToDecimal() ?? 0,
			LowPrice = obj.Low?.ToDecimal() ?? 0,
			TotalVolume = obj.Volume?.ToDecimal() ?? 0,
			OpenTime = obj.Time,
			State = state,
			OriginalTransactionId = id,
		}, cancellationToken);
	}

	private ValueTask OnOhlc(long id, Ohlc obj, CancellationToken cancellationToken)
	{
		return ProcessCandleAsync(id, obj, CandleStates.Active, cancellationToken);
	}

	private ValueTask OnInstrumentStatus(long id, InstrumentStatus obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = CurrentTime,
			OriginalTransactionId = GetParentId(id),
		}
		.TryAdd(Level1Fields.MinPrice, obj.PriceMin?.ToDecimal())
		.TryAdd(Level1Fields.MaxPrice, obj.PriceMax?.ToDecimal())
		.TryAdd(Level1Fields.MarginBuy, obj.MarginBuy?.ToDecimal())
		.TryAdd(Level1Fields.MarginSell, obj.MarginSell?.ToDecimal())
		.TryAdd(Level1Fields.State, obj.TradingStatus.ToSecurityState())
		, cancellationToken);
	}

	private ValueTask OnTick(long id, Tick obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			ServerTime = obj.Timestamp,
			OriginalTransactionId = id,
			TradeId = obj.Id,
			TradePrice = obj.Price.ToDecimal(),
			TradeVolume = obj.Qty.ToDecimal(),
			OpenInterest = obj.OI.ToDecimal(),
			OriginSide = obj.Side.ToSide(),
		}, cancellationToken);
	}
}
