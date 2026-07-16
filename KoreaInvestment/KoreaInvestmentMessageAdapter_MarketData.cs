namespace StockSharp.KoreaInvestment;

public partial class KoreaInvestmentMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var code = lookupMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(lookupMsg.SecurityId.SecurityCode));
		var requestedType = lookupMsg.SecurityType ?? lookupMsg.GetSecurityTypes().FirstOrDefault();
		var info = lookupMsg.SecurityId.ToKis(requestedType);
		var quote = await _rest.GetQuote(info, cancellationToken);
		var securityId = info.ToSecurityId();
		var security = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = securityId,
			SecurityType = info.SecurityType,
			Currency = info.Currency,
			Name = code,
			ShortName = code,
		};
		if (security.IsMatch(lookupMsg, lookupMsg.GetSecurityTypes()))
		{
			_securityInfos[GetSecurityKey(securityId)] = info;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(CreateLevel1(lookupMsg.TransactionId, securityId, quote), cancellationToken);
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var previous))
				await UpdateNativeSubscription(previous.Security, previous, false, cancellationToken);
			return;
		}

		var info = ResolveSecurity(mdMsg.SecurityId);
		if (dataType == DataType.Level1)
		{
			var quote = await _rest.GetQuote(info, cancellationToken);
			await SendOutMessageAsync(CreateLevel1(mdMsg.TransactionId, mdMsg.SecurityId, quote), cancellationToken);
		}

		if (!mdMsg.IsHistoryOnly())
		{
			var subscription = new MarketSubscription
			{
				TransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				Security = info,
				DataType = dataType,
				MaxDepth = Math.Max(1, mdMsg.MaxDepth ?? 10),
			};
			_marketSubscriptions[mdMsg.TransactionId] = subscription;
			await UpdateNativeSubscription(info, subscription, true, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var previous))
				await UpdateNativeSubscription(previous.Security, previous, false, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!KoreaInvestmentExtensions.TimeFrames.Contains(timeFrame))
			throw new ArgumentOutOfRangeException(nameof(mdMsg), timeFrame, "Unsupported KIS candle time frame.");
		var info = ResolveSecurity(mdMsg.SecurityId);
		foreach (var candle in await _rest.GetCandles(info, timeFrame, mdMsg.From, mdMsg.To, mdMsg.Count, cancellationToken))
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
				TotalPrice = candle.Turnover ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Security = info,
			DataType = mdMsg.DataType2,
			TimeFrame = timeFrame,
		};
		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		await UpdateNativeSubscription(info, subscription, true, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UpdateNativeSubscription(KisSecurityInfo info, MarketSubscription changed,
		bool isAdding, CancellationToken cancellationToken)
	{
		var isDepth = changed.DataType == DataType.MarketDepth;
		var channel = isDepth ? info.GetDepthChannel() : info.GetTradeChannel();
		var hasOther = _marketSubscriptions.CachedValues.Any(subscription =>
			subscription.TransactionId != changed.TransactionId && IsSame(subscription.Security, info) &&
			(isDepth ? subscription.DataType == DataType.MarketDepth : subscription.DataType != DataType.MarketDepth));

		if (isAdding && !hasOther)
			await _stream.Subscribe(channel, info.StreamKey, info, cancellationToken);
		else if (!isAdding && !hasOther)
			await _stream.Unsubscribe(channel, info.StreamKey, cancellationToken);
	}

	private async ValueTask ProcessRealtimeTrade(KisRealtimeTrade trade, CancellationToken cancellationToken)
	{
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(subscription =>
			IsSameStream(subscription.Security, trade.Channel, trade.Symbol) && subscription.DataType != DataType.MarketDepth))
		{
			if (subscription.DataType == DataType.Level1)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = trade.ServerTime,
				}
				.TryAdd(Level1Fields.LastTradePrice, trade.Price)
				.TryAdd(Level1Fields.LastTradeVolume, trade.Volume)
				.TryAdd(Level1Fields.LastTradeTime, trade.ServerTime)
				.TryAdd(Level1Fields.OpenPrice, trade.OpenPrice)
				.TryAdd(Level1Fields.HighPrice, trade.HighPrice)
				.TryAdd(Level1Fields.LowPrice, trade.LowPrice)
				.TryAdd(Level1Fields.Volume, trade.TotalVolume)
				.TryAdd(Level1Fields.Turnover, trade.Turnover)
				.TryAdd(Level1Fields.BestBidPrice, trade.BidPrice)
				.TryAdd(Level1Fields.BestBidVolume, trade.BidVolume)
				.TryAdd(Level1Fields.BestAskPrice, trade.AskPrice)
				.TryAdd(Level1Fields.BestAskVolume, trade.AskVolume)
				.TryAdd(Level1Fields.OpenInterest, trade.OpenInterest), cancellationToken);
			}
			else if (subscription.DataType == DataType.Ticks)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = trade.ServerTime,
					TradePrice = trade.Price,
					TradeVolume = trade.Volume,
				}, cancellationToken);
			}
			else if (subscription.DataType.IsTFCandles)
			{
				await UpdateCandle(subscription, trade, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessRealtimeDepth(KisRealtimeDepth depth, CancellationToken cancellationToken)
	{
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(subscription =>
			subscription.DataType == DataType.MarketDepth && IsSameStream(subscription.Security, depth.Channel, depth.Symbol)))
		{
			var maxDepth = subscription.MaxDepth;
			var bids = depth.BidPrices.Zip(depth.BidVolumes)
				.Where(pair => pair.First > 0).OrderByDescending(pair => pair.First).Take(maxDepth)
				.Select(pair => new QuoteChange(pair.First, pair.Second)).ToArray();
			var asks = depth.AskPrices.Zip(depth.AskVolumes)
				.Where(pair => pair.First > 0).OrderBy(pair => pair.First).Take(maxDepth)
				.Select(pair => new QuoteChange(pair.First, pair.Second)).ToArray();
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = depth.ServerTime,
				Bids = bids,
				Asks = asks,
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}

	private async ValueTask UpdateCandle(MarketSubscription subscription, KisRealtimeTrade trade,
		CancellationToken cancellationToken)
	{
		var openTime = trade.ServerTime.Floor(subscription.TimeFrame);
		var candle = subscription.Candle;
		if (candle == null || candle.OpenTime != openTime)
		{
			if (candle != null)
				await SendCandle(subscription, candle, CandleStates.Finished, cancellationToken);
			candle = new()
			{
				OpenTime = openTime,
				Open = trade.Price,
				High = trade.Price,
				Low = trade.Price,
				Close = trade.Price,
				Volume = trade.Volume,
			};
			subscription.Candle = candle;
		}
		else
		{
			candle.High = Math.Max(candle.High, trade.Price);
			candle.Low = Math.Min(candle.Low, trade.Price);
			candle.Close = trade.Price;
			candle.Volume += trade.Volume;
		}
		await SendCandle(subscription, candle, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendCandle(MarketSubscription subscription, ActiveCandle candle, CandleStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TypedArg = subscription.TimeFrame,
			OpenTime = candle.OpenTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			State = state,
		}, cancellationToken);

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId, KisQuoteSnapshot quote)
		=> new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.ServerTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, quote.HighPrice)
		.TryAdd(Level1Fields.LowPrice, quote.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose)
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.Turnover, quote.Turnover)
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskVolume)
		.TryAdd(Level1Fields.OpenInterest, quote.OpenInterest);

	private static bool IsSameStream(KisSecurityInfo info, KisRealtimeChannels channel, string symbol)
		=> (info.GetTradeChannel() == channel || info.GetDepthChannel() == channel) &&
			(info.Code.EqualsIgnoreCase(symbol) || info.StreamKey.EndsWith(symbol, StringComparison.OrdinalIgnoreCase));
}
