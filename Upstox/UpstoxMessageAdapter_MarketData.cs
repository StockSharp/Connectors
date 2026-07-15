namespace StockSharp.Upstox;

public partial class UpstoxMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (long time, decimal price, decimal volume)> _lastTicks = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(cancellationToken))
		{
			var securityType = instrument.ToSecurityType();
			var securityId = instrument.ToSecurityId();
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = securityType,
				Name = instrument.Name,
				ShortName = instrument.TradingSymbol,
				PriceStep = instrument.TickSize,
				VolumeStep = instrument.LotSize,
				Multiplier = instrument.QuantityMultiplier ?? instrument.LotSize,
				ExpiryDate = instrument.Expiry is long expiry ? DateTimeOffset.FromUnixTimeMilliseconds(expiry).UtcDateTime : null,
				Strike = instrument.StrikePrice,
				OptionType = instrument.InstrumentType.ToOptionType(),
				UnderlyingSecurityId = instrument.UnderlyingKey.IsEmpty()
					? default
					: instrument.UnderlyingKey.ToUpstoxSecurityId(instrument.UnderlyingSymbol, null),
			};

			if (!security.IsMatch(lookupMsg, secTypes))
				continue;

			_securityIds[instrument.InstrumentKey] = securityId;
			await SendOutMessageAsync(security, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (_marketClient == null)
			throw new InvalidOperationException(IsDemo ? "Upstox sandbox supports order APIs only; live market streaming is unavailable." : LocalizedStrings.ConnectionNotOk);

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
				var first = subscriptions.Count == 0;
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[instrumentKey] = mdMsg.SecurityId;

				if (first)
					await _marketClient.Subscribe(instrumentKey, UpstoxFeedModes.Full, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
		{
			subscriptions.Remove(dataType);
			if (subscriptions.Count == 0)
			{
				_marketSubscriptions.Remove(instrumentKey);
				_securityIds.Remove(instrumentKey);
				_lastTicks.Remove(instrumentKey);
				await _marketClient.Unsubscribe(instrumentKey, cancellationToken);
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var candles = await _restClient.GetCandles(mdMsg.SecurityId.ToInstrumentKey(), mdMsg.GetTimeFrame(), mdMsg.From, mdMsg.To, cancellationToken);
		var ordered = candles.OrderBy(c => c.Time);
		if (mdMsg.Count is long count)
			ordered = ordered.TakeLast((int)Math.Min(count, int.MaxValue)).OrderBy(c => c.Time);

		foreach (var candle in ordered)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = mdMsg.GetTimeFrame(),
				OpenTime = candle.Time,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask OnFeedReceived(string instrumentKey, Feed feed, long currentTimestamp, CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(instrumentKey) ?? instrumentKey.ToUpstoxSecurityId();
		LTPC ltpc = null;
		MarketFullFeed market = null;
		IndexFullFeed index = null;
		FirstLevelWithGreeks first = null;

		switch (feed.FeedUnionCase)
		{
			case Feed.FeedUnionOneofCase.Ltpc:
				ltpc = feed.Ltpc;
				break;
			case Feed.FeedUnionOneofCase.FullFeed:
				market = feed.FullFeed.MarketFF;
				index = feed.FullFeed.IndexFF;
				ltpc = market?.Ltpc ?? index?.Ltpc;
				break;
			case Feed.FeedUnionOneofCase.FirstLevelWithGreeks:
				first = feed.FirstLevelWithGreeks;
				ltpc = first.Ltpc;
				break;
		}

		var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(ltpc?.Ltt > 0 ? ltpc.Ltt : currentTimestamp).UtcDateTime;

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var l1 = new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, ltpc?.Ltp > 0 ? (decimal?)ltpc.Ltp : null)
			.TryAdd(Level1Fields.LastTradeVolume, ltpc?.Ltq > 0 ? (decimal?)ltpc.Ltq : null)
			.TryAdd(Level1Fields.LastTradeTime, ltpc?.Ltt > 0 ? (DateTime?)serverTime : null)
			.TryAdd(Level1Fields.ClosePrice, ltpc?.Cp > 0 ? (decimal?)ltpc.Cp : null)
			.TryAdd(Level1Fields.AveragePrice, market?.Atp > 0 ? (decimal?)market.Atp : null)
			.TryAdd(Level1Fields.Volume, market?.Vtt > 0 ? (decimal?)market.Vtt : first?.Vtt is > 0 ? (decimal?)first.Vtt : null)
			.TryAdd(Level1Fields.OpenInterest, market?.Oi > 0 ? (decimal?)market.Oi : first?.Oi is > 0 ? (decimal?)first.Oi : null)
			.TryAdd(Level1Fields.ImpliedVolatility, market?.Iv > 0 ? (decimal?)market.Iv : first?.Iv > 0 ? (decimal?)first.Iv : null);

			var greeks = market?.OptionGreeks ?? first?.OptionGreeks;
			if (greeks != null)
			{
				l1.TryAdd(Level1Fields.Delta, (decimal)greeks.Delta)
					.TryAdd(Level1Fields.Gamma, (decimal)greeks.Gamma)
					.TryAdd(Level1Fields.Theta, (decimal)greeks.Theta)
					.TryAdd(Level1Fields.Vega, (decimal)greeks.Vega)
					.TryAdd(Level1Fields.Rho, (decimal)greeks.Rho);
			}

			var firstQuote = market?.MarketLevel?.BidAskQuote.FirstOrDefault() ?? first?.FirstDepth;
			if (firstQuote != null)
			{
				l1.TryAdd(Level1Fields.BestBidPrice, firstQuote.BidP > 0 ? (decimal?)firstQuote.BidP : null)
					.TryAdd(Level1Fields.BestBidVolume, firstQuote.BidQ > 0 ? (decimal?)firstQuote.BidQ : null)
					.TryAdd(Level1Fields.BestAskPrice, firstQuote.AskP > 0 ? (decimal?)firstQuote.AskP : null)
					.TryAdd(Level1Fields.BestAskVolume, firstQuote.AskQ > 0 ? (decimal?)firstQuote.AskQ : null);
			}

			await SendOutMessageAsync(l1, cancellationToken);
		}

		if (ltpc?.Ltt > 0 && ltpc.Ltq > 0 && subscriptions.TryGetValue(DataType.Ticks, out var tickId))
		{
			var tick = (ltpc.Ltt, (decimal)ltpc.Ltp, (decimal)ltpc.Ltq);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != tick)
			{
				_lastTicks[instrumentKey] = tick;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = tickId,
					SecurityId = securityId,
					ServerTime = serverTime,
					TradePrice = tick.Item2,
					TradeVolume = tick.Item3,
				}, cancellationToken);
			}
		}

		if (market?.MarketLevel != null && subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = serverTime,
				Bids = [.. market.MarketLevel.BidAskQuote.Where(q => q.BidP > 0).Select(q => new QuoteChange((decimal)q.BidP, (decimal)q.BidQ))],
				Asks = [.. market.MarketLevel.BidAskQuote.Where(q => q.AskP > 0).Select(q => new QuoteChange((decimal)q.AskP, (decimal)q.AskQ))],
			}, cancellationToken);
		}
	}

	private async ValueTask OnMarketInfoReceived(MarketInfo info, long currentTimestamp, CancellationToken cancellationToken)
	{
		var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(currentTimestamp).UtcDateTime;

		foreach (var pair in info.SegmentStatus)
		{
			var isTrading = pair.Value is MarketStatus.NormalOpen or MarketStatus.PreOpenStart or MarketStatus.ClosingStart;
			foreach (var instrumentKey in _marketSubscriptions.Keys.Where(k => k.StartsWith(pair.Key + "|", StringComparison.OrdinalIgnoreCase)).ToArray())
			{
				if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions) || !subscriptions.TryGetValue(DataType.Level1, out var transactionId))
					continue;

				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = _securityIds.TryGetValue2(instrumentKey) ?? instrumentKey.ToUpstoxSecurityId(),
					ServerTime = serverTime,
				}.Add(Level1Fields.State, isTrading ? SecurityStates.Trading : SecurityStates.Stoped), cancellationToken);
			}
		}
	}
}
