namespace StockSharp.Zerodha;

public partial class ZerodhaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await GetRest().GetInstruments(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			CacheInstrument(instrument);
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.TradingSymbol.ToSecurityId(instrument.Exchange,
					instrument.InstrumentToken),
				SecurityType = instrument.ToSecurityType(),
				Name = instrument.Name.IsEmpty(instrument.TradingSymbol),
				ShortName = instrument.TradingSymbol,
				Class = instrument.Segment,
				ExpiryDate = instrument.ExpiryDate,
				Strike = instrument.Strike is > 0 ? instrument.Strike : null,
				OptionType = instrument.InstrumentType.ToOptionType(),
				PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
				VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
				Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old))
				await RefreshTokenSubscription(old.InstrumentToken, cancellationToken);
			return;
		}

		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			InstrumentToken = instrument.InstrumentToken,
			DataType = dataType,
		};
		try
		{
			await RefreshTokenSubscription(instrument.InstrumentToken, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RefreshTokenSubscription(long token, CancellationToken cancellationToken)
	{
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(s => s.InstrumentToken == token).ToArray();
		if (subscriptions.Length == 0)
		{
			await _stream.Unsubscribe(token, cancellationToken);
			_lastTicks.Remove(token);
			return;
		}

		var mode = subscriptions.Any(s => s.DataType == DataType.MarketDepth || s.DataType == DataType.Ticks)
			? KiteSocketModes.Full
			: KiteSocketModes.Quote;
		await _stream.Subscribe(token, mode, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var estimatedCount = Math.Clamp(mdMsg.Count ?? 1000, 1, 10000);
		var from = (mdMsg.From ?? to - TimeSpan.FromTicks(timeFrame.Ticks * estimatedCount *
			(timeFrame >= TimeSpan.FromDays(1) ? 2 : 3))).ToUniversalTime();
		var candles = await GetRest().GetCandles(instrument.InstrumentToken, timeFrame.ToNative(), from,
			to, true, cancellationToken);
		var left = mdMsg.Count ?? long.MaxValue;

		foreach (var candle in candles.OrderBy(c => c.OpenTime))
		{
			if (mdMsg.From != null && candle.OpenTime < mdMsg.From.Value.ToUniversalTime())
				continue;
			if (mdMsg.To != null && candle.OpenTime > mdMsg.To.Value.ToUniversalTime())
				break;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				OpenTime = candle.OpenTime,
				CloseTime = candle.OpenTime + timeFrame,
				OpenPrice = candle.OpenPrice,
				HighPrice = candle.HighPrice,
				LowPrice = candle.LowPrice,
				ClosePrice = candle.ClosePrice,
				TotalVolume = candle.Volume,
				OpenInterest = candle.OpenInterest,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessTick(KiteTick tick, CancellationToken cancellationToken)
	{
		if (tick == null || tick.InstrumentToken <= 0)
			return;
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(s => s.InstrumentToken == tick.InstrumentToken).ToArray();
		if (subscriptions.Length == 0)
			return;
		var serverTime = tick.ExchangeTime ?? tick.LastTradeTime ?? DateTime.UtcNow;

		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Level1))
		{
			var bid = tick.Bids?.FirstOrDefault();
			var ask = tick.Asks?.FirstOrDefault();
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, tick.LastPrice > 0 ? tick.LastPrice : null)
			.TryAdd(Level1Fields.LastTradeVolume, tick.LastQuantity)
			.TryAdd(Level1Fields.LastTradeTime, tick.LastTradeTime)
			.TryAdd(Level1Fields.AveragePrice, tick.AveragePrice)
			.TryAdd(Level1Fields.Volume, tick.Volume)
			.TryAdd(Level1Fields.BidsVolume, tick.TotalBuyQuantity)
			.TryAdd(Level1Fields.AsksVolume, tick.TotalSellQuantity)
			.TryAdd(Level1Fields.OpenPrice, tick.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, tick.HighPrice)
			.TryAdd(Level1Fields.LowPrice, tick.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, tick.ClosePrice)
			.TryAdd(Level1Fields.OpenInterest, tick.OpenInterest)
			.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
			.TryAdd(Level1Fields.BestBidVolume, bid?.Quantity)
			.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
			.TryAdd(Level1Fields.BestAskVolume, ask?.Quantity)
			.TryAdd(Level1Fields.State, tick.IsTradable ? SecurityStates.Trading : SecurityStates.Stoped),
				cancellationToken);
		}

		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.MarketDepth &&
			tick.Bids != null && tick.Asks != null))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
				Bids = tick.Bids.Where(d => d.Price > 0).Select(d => new QuoteChange(d.Price, d.Quantity)
				{
					OrdersCount = d.Orders,
				}).ToArray(),
				Asks = tick.Asks.Where(d => d.Price > 0).Select(d => new QuoteChange(d.Price, d.Quantity)
				{
					OrdersCount = d.Orders,
				}).ToArray(),
			}, cancellationToken);
		}

		var tickSubscriptions = subscriptions.Where(s => s.DataType == DataType.Ticks).ToArray();
		if (tickSubscriptions.Length == 0 || tick.LastPrice <= 0)
			return;
		var tradeKey = $"{tick.LastTradeTime?.Ticks ?? 0}:{tick.LastPrice}:{tick.LastQuantity}";
		if (_lastTicks.TryGetValue(tick.InstrumentToken, out var previous) && previous == tradeKey)
			return;
		_lastTicks[tick.InstrumentToken] = tradeKey;
		foreach (var subscription in tickSubscriptions)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TradeStringId = $"{tick.InstrumentToken}:{tradeKey}",
				TradePrice = tick.LastPrice,
				TradeVolume = tick.LastQuantity,
				OpenInterest = tick.OpenInterest,
				ServerTime = tick.LastTradeTime ?? serverTime,
			}, cancellationToken);
		}
	}
}
