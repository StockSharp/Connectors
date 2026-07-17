namespace StockSharp.Yuanta;

public partial class YuantaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var symbol = lookupMsg.SecurityId.SecurityCode;
		if (symbol.IsEmpty())
			throw new NotSupportedException(
				"Yuanta SPARK supports exact-symbol lookup; specify SecurityId.SecurityCode.");
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedType = securityTypes.Count == 1 ? securityTypes.First() : (SecurityTypes?)null;
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var security in await _client.GetSecuritiesAsync(lookupMsg.GetLookupMarkets(),
			symbol, requestedType, cancellationToken))
		{
			CacheSecurity(security);
			var message = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = security.ToSecurityId(),
				SecurityType = security.SecurityType,
				Name = security.Name.IsEmpty(security.ExtendedName),
				ShortName = security.Symbol,
				Class = security.Market.ToBoardCode(),
				Currency = security.Market < 200 ? CurrencyTypes.TWD : null,
				PriceStep = security.PriceStep > 0 ? security.PriceStep : null,
			};
			if (!message.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, YuantaMarketDataKinds.Level1,
			DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var security = mdMsg.SecurityId.ParseYuantaSecurity(mdMsg.SecurityType);
		CacheSecurity(security);
		if (mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null)
			await SendTickHistory(mdMsg, security, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		await _client.SubscribeAsync(CreateSubscription(mdMsg, security,
			YuantaMarketDataKinds.Trades, DataType.Ticks), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth,
				"Yuanta SPARK publishes five market-depth levels through this connector.");
		return ProcessRealtimeSubscription(mdMsg, YuantaMarketDataKinds.MarketDepth,
			DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg,
		YuantaMarketDataKinds kind, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		var security = mdMsg.SecurityId.ParseYuantaSecurity(mdMsg.SecurityType);
		CacheSecurity(security);
		await _client.SubscribeAsync(CreateSubscription(mdMsg, security, kind, dataType), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			_liveCandles.Remove(mdMsg.OriginalTransactionId);
			await _client.UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var security = mdMsg.SecurityId.ParseYuantaSecurity(mdMsg.SecurityType);
		CacheSecurity(security);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToYuantaKLineType();
		if (mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null)
			await SendCandleHistory(mdMsg, security, timeFrame, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await _client.SubscribeAsync(CreateSubscription(mdMsg, security,
			YuantaMarketDataKinds.Trades, mdMsg.DataType2, timeFrame), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendTickHistory(MarketDataMessage mdMsg, YuantaSecurityInfo security,
		CancellationToken cancellationToken)
	{
		var now = CurrentTime;
		var taipeiToday = now.ToTaipeiTime().Date;
		var from = NormalizeUtc(mdMsg.From ?? taipeiToday.FromTaipeiTime());
		var to = NormalizeUtc(mdMsg.To ?? now);
		var count = (int)Math.Min(mdMsg.Count ?? 1000, int.MaxValue);
		if (count <= 0)
			return;
		foreach (var tick in (await _client.GetTicksAsync(security, from, to, count, cancellationToken))
			.Where(item => item.ServerTime >= from && item.ServerTime <= to)
			.OrderBy(item => item.ServerTime).Take(count))
		{
			await SendTick(mdMsg.TransactionId, mdMsg.SecurityId, tick, cancellationToken);
		}
	}

	private async ValueTask SendCandleHistory(MarketDataMessage mdMsg, YuantaSecurityInfo security,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var to = NormalizeUtc(mdMsg.To ?? CurrentTime);
		var estimated = Math.Max(1, mdMsg.Count ?? 500);
		var multiplier = timeFrame < TimeSpan.FromDays(1) ? 3 : 2;
		var lookbackBars = Math.Min(Math.Min(estimated, 10000) * multiplier, 10000);
		var lookbackTicks = Math.Min(to.Ticks, timeFrame.Ticks * lookbackBars);
		var from = NormalizeUtc(mdMsg.From ?? to - TimeSpan.FromTicks(lookbackTicks));
		var left = mdMsg.Count ?? long.MaxValue;
		foreach (var candle in (await _client.GetCandlesAsync(security, timeFrame,
			from, to, cancellationToken)).Where(item => item.OpenTime >= from && item.OpenTime <= to)
			.OrderBy(item => item.OpenTime))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
	}

	private async ValueTask OnLevel1(YuantaSubscription subscription, YuantaLevel1Update update,
		CancellationToken cancellationToken)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, update.BuyPrice)
		.TryAdd(Level1Fields.BestAskPrice, update.SellPrice)
		.TryAdd(Level1Fields.LastTradePrice, update.LastPrice)
		.TryAdd(Level1Fields.LastTradeVolume, update.LastVolume)
		.TryAdd(Level1Fields.LastTradeTime, update.LastPrice != null ? update.ServerTime : null)
		.TryAdd(Level1Fields.Volume, update.TotalVolume)
		.TryAdd(Level1Fields.BidsVolume, update.TotalBuyVolume)
		.TryAdd(Level1Fields.AsksVolume, update.TotalSellVolume);

		switch (update.Field)
		{
			case 0: message.TryAdd(Level1Fields.OpenPrice, update.Value); break;
			case 1: message.TryAdd(Level1Fields.HighPrice, update.Value); break;
			case 2: message.TryAdd(Level1Fields.LowPrice, update.Value); break;
			case 3: message.TryAdd(Level1Fields.BestBidPrice, update.Value); break;
			case 5: message.TryAdd(Level1Fields.BestAskPrice, update.Value); break;
			case 7: message.TryAdd(Level1Fields.LastTradePrice, update.Value); break;
			case 8: message.TryAdd(Level1Fields.Turnover, update.Value); break;
			case 9: message.TryAdd(Level1Fields.LastTradeVolume, update.Value); break;
			case 10: message.TryAdd(Level1Fields.Volume, update.Value); break;
			case 12: message.TryAdd(Level1Fields.OpenInterest, update.Value); break;
			case 13: message.TryAdd(Level1Fields.SettlementPrice, update.Value); break;
			case 201: message.TryAdd(Level1Fields.ClosePrice, update.Value); break;
			case 202: message.TryAdd(Level1Fields.MaxPrice, update.Value); break;
			case 203: message.TryAdd(Level1Fields.MinPrice, update.Value); break;
		}
		if (message.Changes.Count > 0)
			await SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask OnBook(YuantaSubscription subscription, YuantaBookUpdate update,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
			Bids = [.. update.Bids.Select(level => new QuoteChange(level.Price, level.Volume))],
			Asks = [.. update.Asks.Select(level => new QuoteChange(level.Price, level.Volume))],
		}, cancellationToken);

	private ValueTask OnTrade(YuantaSubscription subscription, YuantaTradeUpdate update,
		CancellationToken cancellationToken)
		=> subscription.DataType == DataType.Ticks
			? SendTick(subscription.TransactionId, subscription.SecurityId, update, cancellationToken)
			: ProcessLiveCandle(subscription, update, cancellationToken);

	private ValueTask SendTick(long transactionId, SecurityId securityId, YuantaTradeUpdate update,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			TradeStringId = update.Sequence > 0
				? update.Sequence.ToString(CultureInfo.InvariantCulture)
				: $"{update.Symbol}:{update.ServerTime.Ticks}:{update.Price}:{update.Volume}",
			TradePrice = update.Price,
			TradeVolume = update.Volume > 0 ? update.Volume : null,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
			OriginSide = update.InOutFlag switch
			{
				1 => Sides.Buy,
				2 => Sides.Sell,
				_ => null,
			},
		}, cancellationToken);

	private async ValueTask ProcessLiveCandle(YuantaSubscription subscription,
		YuantaTradeUpdate update, CancellationToken cancellationToken)
	{
		if (subscription.TimeFrame is not TimeSpan timeFrame || update.Price <= 0)
			return;
		var openTime = GetCandleOpenTime(update.ServerTime, timeFrame);
		if (_liveCandles.TryGetValue(subscription.TransactionId, out var state) &&
			state.OpenTime < openTime)
		{
			await SendLiveCandle(subscription, state, timeFrame, CandleStates.Finished, cancellationToken);
			state = null;
		}
		if (state == null)
		{
			state = new()
			{
				OpenTime = openTime,
				Open = update.Price,
				High = update.Price,
				Low = update.Price,
				Close = update.Price,
			};
			_liveCandles[subscription.TransactionId] = state;
		}
		else
		{
			state.High = Math.Max(state.High, update.Price);
			state.Low = Math.Min(state.Low, update.Price);
			state.Close = update.Price;
		}
		state.Volume += update.Volume;
		await SendLiveCandle(subscription, state, timeFrame, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendLiveCandle(YuantaSubscription subscription, LiveCandleState state,
		TimeSpan timeFrame, CandleStates candleState, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TypedArg = timeFrame,
			OpenTime = state.OpenTime,
			OpenPrice = state.Open,
			HighPrice = state.High,
			LowPrice = state.Low,
			ClosePrice = state.Close,
			TotalVolume = state.Volume,
			State = candleState,
		}, cancellationToken);

	private static DateTime GetCandleOpenTime(DateTime time, TimeSpan timeFrame)
	{
		var local = NormalizeUtc(time).ToTaipeiTime();
		var ticks = timeFrame >= TimeSpan.FromDays(1)
			? local.Date.Ticks - local.Date.Ticks % timeFrame.Ticks
			: local.Date.Ticks + local.TimeOfDay.Ticks - local.TimeOfDay.Ticks % timeFrame.Ticks;
		return new DateTime(ticks, DateTimeKind.Unspecified).FromTaipeiTime();
	}

	private static YuantaSubscription CreateSubscription(MarketDataMessage message,
		YuantaSecurityInfo security, YuantaMarketDataKinds kind, DataType dataType,
		TimeSpan? timeFrame = null)
		=> new()
		{
			TransactionId = message.TransactionId,
			Kind = kind,
			Market = security.Market,
			Symbol = security.Symbol,
			SecurityId = message.SecurityId,
			DataType = dataType,
			TimeFrame = timeFrame,
		};
}
