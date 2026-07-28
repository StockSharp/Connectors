namespace StockSharp.DeepBook;

public partial class DeepBookMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		DeepBookMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item => item.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.DeepBook))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode) &&
				!requestedCode.EqualsIgnoreCase(market.PoolName) &&
				!requestedCode.EqualsIgnoreCase(market.PoolId))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"DeepBook indexer does not expose historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		var book = await ApiClient.GetOrderBookAsync(market, OrderBookDepth,
			cancellationToken);
		await SendLevel1Async(market, book, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"DeepBook indexer does not expose historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? OrderBookDepth).Max(2)
			.Min(OrderBookDepth);
		if (depth % 2 != 0)
			depth--;
		var book = await ApiClient.GetOrderBookAsync(market, depth,
			cancellationToken);
		await SendDepthAsync(market, book, mdMsg.TransactionId, depth, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				Depth = depth,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"DeepBook trade start time cannot be later than end time.");
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var delivered = 0;
		DateTime? lastTime = from;
		if (from is not null || mdMsg.IsHistoryOnly() || to is not null)
		{
			var history = await ApiClient.GetTradesAsync(market, from,
				to ?? DateTime.UtcNow, maximum.Min(HistoryLimit),
				cancellationToken);
			foreach (var trade in history)
			{
				if (to is DateTime finish && trade.Time > finish)
					continue;
				if (await SendTradeAsync(market, mdMsg.TransactionId, trade,
					cancellationToken))
					delivered++;
				lastTime = trade.Time;
				if (delivered >= maximum)
					break;
			}
		}
		else
			lastTime = DateTime.UtcNow;
		if (mdMsg.IsHistoryOnly() || delivered >= maximum)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				To = to,
				Maximum = maximum,
				Delivered = delivered,
				LastTime = lastTime,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToDeepBookInterval();
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"DeepBook candle start time cannot be later than end time.");
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var delivered = 0;
		var count = GetCandleCount(mdMsg, timeFrame,
			to ?? DateTime.UtcNow);
		var candles = await ApiClient.GetCandlesAsync(market, timeFrame, from,
			to, count, cancellationToken);
		foreach (var candle in candles)
		{
			if (from is DateTime begin && candle.OpenTime < begin ||
				to is DateTime finish && candle.OpenTime > finish)
				continue;
			var state = candle.OpenTime + timeFrame <= DateTime.UtcNow
				? CandleStates.Finished
				: CandleStates.Active;
			if (await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, state, cancellationToken))
				delivered++;
			if (delivered >= maximum)
				break;
		}
		if (mdMsg.IsHistoryOnly() || delivered >= maximum)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				TimeFrame = timeFrame,
				To = to,
				Maximum = maximum,
				Delivered = delivered,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(DeepBookMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.BaseToken.Symbol}/{market.QuoteToken.Symbol}",
			ShortName = market.PoolName,
			Class = "SUI-CLOB",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteToken.Symbol.ToCurrency(),
			PriceStep = market.TickSize,
			VolumeStep = market.LotSize,
			MinVolume = market.MinSize,
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.BaseToken.Symbol);

	private async ValueTask SendLevel1Async(DeepBookMarket market,
		DeepBookOrderBook book, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var bid = book.Bids[0];
		var ask = book.Asks[0];
		var fingerprint = new Level1Fingerprint(bid.Price, bid.Volume,
			ask.Price, ask.Volume);
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(target,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[target] = fingerprint;
		}
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid.Volume)
		.TryAdd(Level1Fields.BestAskPrice, ask.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask.Volume)
		.TryAdd(Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private async ValueTask SendDepthAsync(DeepBookMarket market,
		DeepBookOrderBook book, long target, int depth, bool isForced,
		CancellationToken cancellationToken)
	{
		var bids = book.Bids.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Volume))
			.ToArray();
		var asks = book.Asks.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Volume))
			.ToArray();
		var fingerprint = string.Join('|', bids.Select(static item =>
			$"{item.Price.ToString(CultureInfo.InvariantCulture)}:" +
			item.Volume.ToString(CultureInfo.InvariantCulture))) + "/" +
			string.Join('|', asks.Select(static item =>
				$"{item.Price.ToString(CultureInfo.InvariantCulture)}:" +
				item.Volume.ToString(CultureInfo.InvariantCulture)));
		using (_sync.EnterScope())
		{
			if (!isForced && _depthFingerprints.TryGetValue(target,
				out var previous) && previous == fingerprint)
				return;
			_depthFingerprints[target] = fingerprint;
		}
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private async ValueTask<bool> SendTradeAsync(DeepBookMarket market,
		long target, DeepBookTrade trade,
		CancellationToken cancellationToken)
	{
		if (!TryTrackDelivery(target, "T:" + trade.Id))
			return false;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Time,
			OriginalTransactionId = target,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.BaseVolume,
			OriginSide = trade.Side,
		}, cancellationToken);
		return true;
	}

	private async ValueTask<bool> SendCandleAsync(DeepBookMarket market,
		DeepBookCandle candle, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
	{
		var identity = "C:" + candle.OpenTime.Ticks.ToString(
			CultureInfo.InvariantCulture) + ":" +
			candle.Open.ToString(CultureInfo.InvariantCulture) + ":" +
			candle.High.ToString(CultureInfo.InvariantCulture) + ":" +
			candle.Low.ToString(CultureInfo.InvariantCulture) + ":" +
			candle.Close.ToString(CultureInfo.InvariantCulture) + ":" +
			candle.Volume.ToString(CultureInfo.InvariantCulture) + ":" + state;
		if (!TryTrackDelivery(target, identity))
			return false;
		await SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = target,
			State = state,
		}, cancellationToken);
		return true;
	}

	private async ValueTask PollMarketDataAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, Level1Subscription>[] level1;
		KeyValuePair<long, DepthSubscription>[] depths;
		KeyValuePair<long, TickSubscription>[] ticks;
		KeyValuePair<long, CandleSubscription>[] candles;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions];
			depths = [.. _depthSubscriptions];
			ticks = [.. _tickSubscriptions];
			candles = [.. _candleSubscriptions];
		}
		foreach (var item in level1)
			await PollOneAsync(async token =>
			{
				var book = await ApiClient.GetOrderBookAsync(item.Value.Market,
					OrderBookDepth, token);
				await SendLevel1Async(item.Value.Market, book, item.Key, false,
					token);
			}, cancellationToken);
		foreach (var item in depths)
			await PollOneAsync(async token =>
			{
				var book = await ApiClient.GetOrderBookAsync(item.Value.Market,
					item.Value.Depth, token);
				await SendDepthAsync(item.Value.Market, book, item.Key,
					item.Value.Depth, false, token);
			}, cancellationToken);
		foreach (var item in ticks)
			await PollOneAsync(token => PollTicksAsync(item.Key, item.Value,
				token), cancellationToken);
		foreach (var item in candles)
			await PollOneAsync(token => PollCandlesAsync(item.Key, item.Value,
				token), cancellationToken);
	}

	private async ValueTask PollTicksAsync(long target,
		TickSubscription subscription, CancellationToken cancellationToken)
	{
		var to = DateTime.UtcNow;
		var from = subscription.LastTime is DateTime last
			? last - TimeSpan.FromSeconds(1)
			: to - PollingInterval - TimeSpan.FromSeconds(1);
		var trades = await ApiClient.GetTradesAsync(subscription.Market, from,
			to, HistoryLimit, cancellationToken);
		var finished = false;
		foreach (var trade in trades)
		{
			var sent = await SendTradeAsync(subscription.Market, target, trade,
				cancellationToken);
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.TryGetValue(target, out var active))
					return;
				if (active.LastTime is null || trade.Time > active.LastTime)
					active.LastTime = trade.Time;
				if (sent)
					active.Delivered++;
				finished = active.Delivered >= active.Maximum;
				if (finished)
					RemoveMarketSubscriptionNoLock(target);
			}
			if (finished)
				break;
		}
		if (finished)
			await SendSubscriptionFinishedAsync(target, cancellationToken);
	}

	private async ValueTask PollCandlesAsync(long target,
		CandleSubscription subscription,
		CancellationToken cancellationToken)
	{
		var candles = await ApiClient.GetCandlesAsync(subscription.Market,
			subscription.TimeFrame, null, subscription.To, 2,
			cancellationToken);
		var finished = false;
		foreach (var candle in candles)
		{
			var state = candle.OpenTime + subscription.TimeFrame <=
				DateTime.UtcNow
					? CandleStates.Finished
					: CandleStates.Active;
			var sent = await SendCandleAsync(subscription.Market, candle,
				subscription.TimeFrame, target, state, cancellationToken);
			using (_sync.EnterScope())
			{
				if (!_candleSubscriptions.TryGetValue(target, out var active))
					return;
				if (sent)
					active.Delivered++;
				finished = active.Delivered >= active.Maximum;
				if (finished)
					RemoveMarketSubscriptionNoLock(target);
			}
			if (finished)
				break;
		}
		if (finished)
			await SendSubscriptionFinishedAsync(target, cancellationToken);
	}

	private async ValueTask PollOneAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private bool TryTrackDelivery(long target, string identity)
	{
		var key = new DeliveryKey(target, identity);
		using (_sync.EnterScope())
		{
			if (!_seenMarketData.Add(key))
				return false;
			_marketDataDeliveryOrder.Enqueue(key);
			while (_marketDataDeliveryOrder.Count > _maximumDeliveryKeys)
				_seenMarketData.Remove(_marketDataDeliveryOrder.Dequeue());
			return true;
		}
	}

	private void RemoveMarketSubscriptionNoLock(long target)
	{
		_level1Subscriptions.Remove(target);
		_depthSubscriptions.Remove(target);
		_tickSubscriptions.Remove(target);
		_candleSubscriptions.Remove(target);
		_level1Fingerprints.Remove(target);
		_depthFingerprints.Remove(target);
		_seenMarketData.RemoveWhere(key => key.SubscriptionId == target);
		var retained = _marketDataDeliveryOrder.Where(
			_seenMarketData.Contains).ToArray();
		_marketDataDeliveryOrder.Clear();
		foreach (var key in retained)
			_marketDataDeliveryOrder.Enqueue(key);
	}

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? int.MaxValue
			: count.Value.Min(int.MaxValue).Max(1).To<int>();

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.ToUniversalTime())
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit;
	}

	private static void RemoveFingerprintPrefix<TValue>(
		IDictionary<string, TValue> values, long target)
	{
		var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
		foreach (var key in values.Keys.Where(key =>
			key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
			values.Remove(key);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
