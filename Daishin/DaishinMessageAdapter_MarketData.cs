namespace StockSharp.Daishin;

public partial class DaishinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Any(type => type is not SecurityTypes.Stock and not SecurityTypes.Etf and
			not SecurityTypes.Future and not SecurityTypes.Option))
			throw new NotSupportedException(
				"Daishin CYBOS Plus lookup supports stocks, ETFs, futures, and options.");

		var securities = await _client.GetSecuritiesAsync(
			lookupMsg.SecurityId.SecurityCode, securityTypes, cancellationToken);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var security in securities)
		{
			CacheSecurity(security);
			var message = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = security.ToSecurityId(),
				SecurityType = security.SecurityType,
				Name = security.Name,
				ShortName = security.Code,
				Class = security.Board,
				Currency = CurrencyTypes.KRW,
				PriceStep = security.PriceStep,
				ExpiryDate = security.ExpiryDate,
				Strike = security.Strike,
				OptionType = security.OptionType,
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
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var security = await ResolveSecurityAsync(mdMsg.SecurityId, mdMsg.SecurityType, cancellationToken);
		var snapshot = await _client.GetSnapshotAsync(security, cancellationToken);
		await SendLevel1(mdMsg.TransactionId, security.ToSecurityId(), snapshot, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await _client.SubscribeAsync(CreateSubscription(mdMsg, security,
			DaishinMarketDataKinds.Current, DataType.Level1), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

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
		if (mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException(
				"CYBOS Plus does not expose historical individual trades through this connector; use candle history.");

		var security = await ResolveSecurityAsync(mdMsg.SecurityId, mdMsg.SecurityType, cancellationToken);
		await _client.SubscribeAsync(CreateSubscription(mdMsg, security,
			DaishinMarketDataKinds.Current, DataType.Ticks), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
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

		var security = await ResolveSecurityAsync(mdMsg.SecurityId, mdMsg.SecurityType, cancellationToken);
		var maximumDepth = security.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf ? 10 : 5;
		var depth = mdMsg.MaxDepth ?? maximumDepth;
		if (depth is < 1 || depth > maximumDepth)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth,
				$"CYBOS Plus publishes up to {maximumDepth} levels for {security.SecurityType}.");

		await _client.SubscribeAsync(CreateSubscription(mdMsg, security,
			DaishinMarketDataKinds.MarketDepth, DataType.MarketDepth), cancellationToken);
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

		var security = await ResolveSecurityAsync(mdMsg.SecurityId, mdMsg.SecurityType, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!_timeFrames.Contains(timeFrame))
			throw new NotSupportedException($"CYBOS Plus does not support the {timeFrame} chart period.");

		if (mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null)
			await SendCandleHistory(mdMsg, security, timeFrame, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await _client.SubscribeAsync(CreateSubscription(mdMsg, security,
			DaishinMarketDataKinds.Current, mdMsg.DataType2, timeFrame), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendCandleHistory(MarketDataMessage message,
		DaishinSecurityInfo security, TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var count = (int)Math.Clamp(message.Count ?? 500, 1, 2000);
		var from = message.From is DateTime fromValue ? NormalizeUtc(fromValue) : DateTime.MinValue;
		var to = message.To is DateTime toValue ? NormalizeUtc(toValue) : CurrentTime;
		var left = message.Count ?? long.MaxValue;
		foreach (var candle in (await _client.GetCandlesAsync(security, timeFrame,
			count, cancellationToken)).Where(item => item.OpenTime >= from && item.OpenTime <= to))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = security.ToSecurityId(),
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TotalPrice = candle.Turnover ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
	}

	private ValueTask OnLevel1(DaishinSubscription subscription,
		DaishinLevel1Update update, CancellationToken cancellationToken)
		=> subscription.DataType == DataType.Level1
			? SendLevel1(subscription.TransactionId, subscription.SecurityId, update, cancellationToken)
			: subscription.DataType == DataType.Ticks
				? SendTick(subscription, update, cancellationToken)
				: ProcessLiveCandle(subscription, update, cancellationToken);

	private ValueTask SendLevel1(long transactionId, SecurityId securityId,
		DaishinLevel1Update update, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
		}
		.TryAdd(Level1Fields.OpenPrice, update.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, update.HighPrice)
		.TryAdd(Level1Fields.LowPrice, update.LowPrice)
		.TryAdd(Level1Fields.LastTradePrice, update.LastPrice)
		.TryAdd(Level1Fields.LastTradeVolume, update.LastVolume)
		.TryAdd(Level1Fields.LastTradeTime, update.LastPrice != null ? update.ServerTime : null)
		.TryAdd(Level1Fields.BestBidPrice, update.BestBidPrice)
		.TryAdd(Level1Fields.BestBidVolume, update.BestBidVolume)
		.TryAdd(Level1Fields.BestAskPrice, update.BestAskPrice)
		.TryAdd(Level1Fields.BestAskVolume, update.BestAskVolume)
		.TryAdd(Level1Fields.Volume, update.TotalVolume)
		.TryAdd(Level1Fields.Turnover, update.Turnover)
		.TryAdd(Level1Fields.OpenInterest, update.OpenInterest), cancellationToken);

	private ValueTask SendTick(DaishinSubscription subscription,
		DaishinLevel1Update update, CancellationToken cancellationToken)
	{
		if (update.LastPrice is not decimal price || update.LastVolume is not decimal volume || volume <= 0)
			return default;
		var serverTime = update.ServerTime == default ? CurrentTime : update.ServerTime;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TradeStringId = $"{subscription.Code}:{serverTime.Ticks}:{Interlocked.Increment(ref _tickSequence)}",
			TradePrice = price,
			TradeVolume = volume,
			ServerTime = serverTime,
			OriginSide = update.OriginSide,
		}, cancellationToken);
	}

	private ValueTask OnBook(DaishinSubscription subscription,
		DaishinBookUpdate update, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
			Bids = [.. update.Bids.Select(level => new QuoteChange(level.Price, level.Volume))],
			Asks = [.. update.Asks.Select(level => new QuoteChange(level.Price, level.Volume))],
		}, cancellationToken);

	private async ValueTask ProcessLiveCandle(DaishinSubscription subscription,
		DaishinLevel1Update update, CancellationToken cancellationToken)
	{
		if (subscription.TimeFrame is not TimeSpan timeFrame ||
			update.LastPrice is not decimal price || update.LastVolume is not decimal volume || volume <= 0)
			return;

		var serverTime = update.ServerTime == default ? CurrentTime : update.ServerTime;
		var openTime = GetCandleOpenTime(serverTime, timeFrame);
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
				Open = price,
				High = price,
				Low = price,
				Close = price,
			};
			_liveCandles[subscription.TransactionId] = state;
		}
		else
		{
			state.High = Math.Max(state.High, price);
			state.Low = Math.Min(state.Low, price);
			state.Close = price;
		}
		state.Volume += volume;
		await SendLiveCandle(subscription, state, timeFrame, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendLiveCandle(DaishinSubscription subscription, LiveCandleState state,
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
		var local = NormalizeUtc(time).ToKoreaTime();
		DateTime openTime;
		if (timeFrame == TimeSpan.FromDays(30))
			openTime = new(local.Year, local.Month, 1);
		else if (timeFrame == TimeSpan.FromDays(7))
			openTime = local.Date.AddDays(-((7 + (int)local.DayOfWeek - (int)DayOfWeek.Monday) % 7));
		else if (timeFrame >= TimeSpan.FromDays(1))
			openTime = local.Date;
		else
			openTime = local.Date.AddTicks(local.TimeOfDay.Ticks - local.TimeOfDay.Ticks % timeFrame.Ticks);
		return openTime.FromKoreaTime();
	}

	private DaishinSubscription CreateSubscription(MarketDataMessage message,
		DaishinSecurityInfo security, DaishinMarketDataKinds kind, DataType dataType,
		TimeSpan? timeFrame = null)
		=> new()
		{
			TransactionId = message.TransactionId,
			Kind = kind,
			SecurityId = security.ToSecurityId(),
			SecurityType = security.SecurityType,
			Code = security.Code,
			DataType = dataType,
			TimeFrame = timeFrame,
			StockMarket = Market,
		};
}
