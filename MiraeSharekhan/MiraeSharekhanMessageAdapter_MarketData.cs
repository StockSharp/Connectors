namespace StockSharp.MiraeSharekhan;

public partial class MiraeSharekhanMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var requestedBoard = lookupMsg.SecurityId.BoardCode.ToNativeExchange();
		var exchanges = requestedBoard.IsEmpty() ? AssociatedBoards : [requestedBoard];
		Exception firstError = null;
		var received = false;

		foreach (var exchange in exchanges.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			MiraeSharekhanInstrument[] instruments;
			try
			{
				instruments = await GetRest().GetInstruments(exchange, cancellationToken);
			}
			catch (Exception ex)
			{
				firstError ??= ex;
				if (!requestedBoard.IsEmpty())
					throw;
				continue;
			}

			foreach (var instrument in instruments)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var scripCode = instrument.GetScripCode();
				if (scripCode <= 0)
					continue;
				CacheInstrument(instrument);
				var securityId = CreateSecurityId(instrument.Exchange, scripCode, instrument.GetSymbol());
				securityId.Isin = instrument.Isin;
				var security = new SecurityMessage
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = securityId,
					SecurityType = instrument.ToSecurityType(),
					Name = instrument.GetName(),
					ShortName = instrument.GetSymbol(),
					Class = instrument.Segment.IsEmpty(instrument.InstrumentType),
					Currency = CurrencyTypes.INR,
					PriceStep = instrument.TickSize is > 0 ? instrument.TickSize : null,
					VolumeStep = instrument.LotSize is > 0 ? instrument.LotSize : 1,
					Multiplier = instrument.LotSize is > 0 ? instrument.LotSize : 1,
					ExpiryDate = instrument.GetExpiryDate(),
					Strike = instrument.StrikePrice is > 0 ? instrument.StrikePrice : null,
					OptionType = instrument.OptionType.ToOptionType(),
				};
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				received = true;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		if (!received && firstError != null)
			throw firstError;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessLiveSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessLiveSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessLiveSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessLiveSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var removed))
				await RefreshSubscription(removed.StreamKey, cancellationToken);
			return;
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (_stream == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		CacheInstrument(instrument);
		var streamKey = instrument.Exchange.ToStreamKey(instrument.GetScripCode());
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = CreateSecurityId(instrument.Exchange, instrument.GetScripCode(),
				mdMsg.SecurityId.SecurityCode.IsEmpty(instrument.GetSymbol())),
			StreamKey = streamKey,
			DataType = dataType,
		};
		try
		{
			await RefreshSubscription(streamKey, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask RefreshSubscription(string streamKey, CancellationToken cancellationToken)
	{
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(subscription => subscription.StreamKey.EqualsIgnoreCase(streamKey)).ToArray();
		var mode = subscriptions.Any(subscription => subscription.DataType == DataType.MarketDepth)
			? "depth"
			: subscriptions.Length > 0 ? "ltp" : null;
		return new(_stream.SetSubscription(streamKey, mode, cancellationToken));
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		var timeFrame = mdMsg.GetTimeFrame();
		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		var candles = await GetRest().GetCandles(instrument.Exchange, instrument.GetScripCode(),
			timeFrame.ToNative(), cancellationToken);
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		var ordered = candles
			.Select(candle => (candle, time: candle.GetTime()))
			.Where(item => item.time != null && (from == null || item.time >= from) &&
				(to == null || item.time <= to))
			.OrderBy(item => item.time)
			.ToArray();
		if (mdMsg.Count is long count && ordered.LongLength > count)
			ordered = [.. ordered.TakeLast((int)Math.Min(count, int.MaxValue))];

		foreach (var (candle, time) in ordered)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				OpenTime = time.Value,
				CloseTime = time.Value + timeFrame,
				OpenPrice = candle.OpenPrice,
				HighPrice = candle.HighPrice,
				LowPrice = candle.LowPrice,
				ClosePrice = candle.ClosePrice,
				TotalVolume = candle.Volume,
				OpenInterest = candle.OpenInterest ?? candle.OpenInterest2,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessFeed(MiraeSharekhanStreamMessage feed,
		CancellationToken cancellationToken)
	{
		var streamKey = feed?.GetStreamKey();
		if (streamKey.IsEmpty())
			return;
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(subscription => subscription.StreamKey.EqualsIgnoreCase(streamKey)).ToArray();
		if (subscriptions.Length == 0)
			return;
		var serverTime = feed.GetTime() ?? CurrentTime;
		var lastPrice = feed.GetLastPrice();
		var lastQuantity = feed.GetLastQuantity();
		var bids = feed.Bids ?? [];
		var asks = feed.Asks ?? [];

		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Level1))
		{
			var bestBid = bids.FirstOrDefault();
			var bestAsk = asks.FirstOrDefault();
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, lastPrice)
			.TryAdd(Level1Fields.LastTradeVolume, lastQuantity)
			.TryAdd(Level1Fields.LastTradeTime, lastPrice != null ? serverTime : null)
			.TryAdd(Level1Fields.OpenPrice, feed.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, feed.HighPrice)
			.TryAdd(Level1Fields.LowPrice, feed.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, feed.ClosePrice)
			.TryAdd(Level1Fields.Volume, feed.Volume)
			.TryAdd(Level1Fields.OpenInterest, feed.GetOpenInterest())
			.TryAdd(Level1Fields.BestBidPrice, bestBid?.Price ?? feed.BestBidPrice)
			.TryAdd(Level1Fields.BestBidVolume, bestBid?.GetQuantity() ?? feed.BestBidQuantity)
			.TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price ?? feed.BestAskPrice)
			.TryAdd(Level1Fields.BestAskVolume, bestAsk?.GetQuantity() ?? feed.BestAskQuantity),
				cancellationToken);
		}

		if (lastPrice is > 0 && lastQuantity is > 0)
		{
			var tick = (serverTime, lastPrice.Value, lastQuantity.Value);
			if (!_lastTicks.TryGetValue(streamKey, out var previous) || previous != tick)
			{
				_lastTicks[streamKey] = tick;
				foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Ticks))
				{
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						OriginalTransactionId = subscription.TransactionId,
						SecurityId = subscription.SecurityId,
						TradeStringId = $"{streamKey}:{serverTime.Ticks}:{lastPrice}:{lastQuantity}",
						TradePrice = lastPrice,
						TradeVolume = lastQuantity,
						ServerTime = serverTime,
					}, cancellationToken);
				}
			}
		}

		if (bids.Length == 0 && asks.Length == 0)
			return;
		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.MarketDepth))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
				Bids = [.. bids.Where(level => level.Price > 0)
					.OrderByDescending(level => level.Price)
					.Select(level => new QuoteChange(level.Price, level.GetQuantity())
					{
						OrdersCount = level.GetOrders(),
					})],
				Asks = [.. asks.Where(level => level.Price > 0)
					.OrderBy(level => level.Price)
					.Select(level => new QuoteChange(level.Price, level.GetQuantity())
					{
						OrdersCount = level.GetOrders(),
					})],
			}, cancellationToken);
		}
	}
}
