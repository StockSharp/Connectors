namespace StockSharp.AngelOne;

public partial class AngelOneMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (DateTime time, decimal price, decimal volume)> _lastTicks = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(cancellationToken))
		{
			if (instrument.Token.IsEmpty() || instrument.Exchange.IsEmpty())
				continue;

			SecurityId securityId;
			try
			{
				securityId = instrument.ToSecurityId();
			}
			catch (ArgumentOutOfRangeException)
			{
				continue;
			}

			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = instrument.ToSecurityType(),
				Name = instrument.Name,
				ShortName = instrument.Symbol,
				PriceStep = instrument.TickSize > 0 ? instrument.TickSize.ToMasterPrice(instrument.Exchange.ToExchangeType()) : null,
				VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
				Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
				ExpiryDate = instrument.Expiry.ToExpiry(),
				Strike = instrument.Strike > 0 ? instrument.Strike.ToMasterPrice(instrument.Exchange.ToExchangeType()) : null,
				OptionType = instrument.InstrumentType.ToOptionType(),
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			_securityIds[securityId.Native.ToString()] = securityId;
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
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
		var (exchangeType, token) = instrumentKey.ParseInstrumentKey();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
				var first = subscriptions.Count == 0;
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[instrumentKey] = mdMsg.SecurityId;

				if (first)
					await _marketClient.Subscribe(exchangeType, token, cancellationToken);
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
				await _marketClient.Unsubscribe(exchangeType, token, cancellationToken);
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var (_, token) = mdMsg.SecurityId.ToInstrumentKey().ParseInstrumentKey();
		var candles = await _restClient.GetCandles(mdMsg.SecurityId.BoardCode, token, mdMsg.GetTimeFrame(), mdMsg.From, mdMsg.To, cancellationToken);
		IEnumerable<AngelOneCandle> ordered = candles.OrderBy(c => c.Time);
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

	private async ValueTask OnTickReceived(AngelOneMarketTick tick, CancellationToken cancellationToken)
	{
		var instrumentKey = tick.ExchangeType.ToInstrumentKey(tick.Token);
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(instrumentKey) ?? tick.ExchangeType.ToSecurityId(tick.Token);
		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = tick.ServerTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, tick.LastPrice > 0 ? tick.LastPrice : null)
			.TryAdd(Level1Fields.LastTradeVolume, tick.LastVolume > 0 ? tick.LastVolume : null)
			.TryAdd(Level1Fields.LastTradeTime, tick.LastTradeTime)
			.TryAdd(Level1Fields.AveragePrice, tick.AveragePrice > 0 ? tick.AveragePrice : null)
			.TryAdd(Level1Fields.Volume, tick.Volume > 0 ? tick.Volume : null)
			.TryAdd(Level1Fields.OpenPrice, tick.OpenPrice > 0 ? tick.OpenPrice : null)
			.TryAdd(Level1Fields.HighPrice, tick.HighPrice > 0 ? tick.HighPrice : null)
			.TryAdd(Level1Fields.LowPrice, tick.LowPrice > 0 ? tick.LowPrice : null)
			.TryAdd(Level1Fields.ClosePrice, tick.ClosePrice > 0 ? tick.ClosePrice : null)
			.TryAdd(Level1Fields.OpenInterest, tick.OpenInterest > 0 ? tick.OpenInterest : null)
			.TryAdd(Level1Fields.BidsVolume, tick.TotalBuyVolume > 0 ? tick.TotalBuyVolume : null)
			.TryAdd(Level1Fields.AsksVolume, tick.TotalSellVolume > 0 ? tick.TotalSellVolume : null)
			.TryAdd(Level1Fields.MinPrice, tick.LowerCircuit > 0 ? tick.LowerCircuit : null)
			.TryAdd(Level1Fields.MaxPrice, tick.UpperCircuit > 0 ? tick.UpperCircuit : null)
			.TryAdd(Level1Fields.BestBidPrice, tick.Bids.FirstOrDefault()?.Price)
			.TryAdd(Level1Fields.BestBidVolume, tick.Bids.FirstOrDefault()?.Volume)
			.TryAdd(Level1Fields.BestAskPrice, tick.Asks.FirstOrDefault()?.Price)
			.TryAdd(Level1Fields.BestAskVolume, tick.Asks.FirstOrDefault()?.Volume), cancellationToken);
		}

		if (tick.LastTradeTime is DateTime tradeTime && tick.LastVolume > 0 && subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (tradeTime, tick.LastPrice, tick.LastVolume);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
			{
				_lastTicks[instrumentKey] = trade;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = ticksId,
					SecurityId = securityId,
					ServerTime = tradeTime,
					TradePrice = tick.LastPrice,
					TradeVolume = tick.LastVolume,
				}, cancellationToken);
			}
		}

		if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = tick.ServerTime,
				Bids = [.. tick.Bids.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
				Asks = [.. tick.Asks.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
			}, cancellationToken);
		}
	}
}
