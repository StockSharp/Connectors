namespace StockSharp.Curve;

public partial class CurveMessageAdapter
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
		CurveMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Curve))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode))
				continue;
			var security = CreateSecurity(market,
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
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
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Curve quote probes do not expose historical Level1 " +
				"events.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId,
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
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			UnsubscribeTicks(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var now = DateTime.UtcNow;
		var from = mdMsg.From?.ToUniversalTime() ?? DateTime.UnixEpoch;
		var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var trades = await LoadTradesAsync(market, from, to,
			maximum, cancellationToken);
		var delivered = 0;
		foreach (var trade in trades)
			if (await SendTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken))
				delivered++;
		if (mdMsg.IsHistoryOnly() || mdMsg.To is DateTime requestedTo &&
			requestedTo.ToUniversalTime() <= now || delivered >= maximum)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				From = mdMsg.From?.ToUniversalTime(),
				To = mdMsg.To?.ToUniversalTime(),
				LastTime = trades.Length > 0 ? trades[^1].Time : now,
				Maximum = maximum,
				Delivered = delivered,
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
			UnsubscribeCandles(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Curve does not support the {timeFrame} candle interval.");
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var historyMaximum = GetCandleHistoryCount(mdMsg, timeFrame, to,
			maximum);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * historyMaximum);
		var candles = await LoadCandlesAsync(market, timeFrame, from, to,
			historyMaximum, cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly() || mdMsg.To is DateTime requestedTo &&
			requestedTo.ToUniversalTime() <= now ||
			candles.Length >= maximum)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				TimeFrame = timeFrame,
				To = mdMsg.To?.ToUniversalTime(),
				LastTime = candles.Length > 0
					? candles[^1].OpenTime
					: from,
				Maximum = maximum,
				Delivered = candles.Length,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(CurveMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.BaseToken.Symbol}/{market.QuoteToken.Symbol} " +
				$"{market.PoolType}",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteToken.Symbol.ToCurrency(),
			PriceStep = DecimalStep(market.QuoteToken.Decimals),
			VolumeStep = DecimalStep(market.BaseToken.Decimals),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.BaseToken.Symbol);

	private async ValueTask SendLevel1Async(CurveMarket market,
		long target, CancellationToken cancellationToken)
	{
		var snapshot = await LoadLevel1Async(market, cancellationToken);
		await SendLevel1Async(market, target, snapshot.Bid, snapshot.Ask,
			cancellationToken);
	}

	private async ValueTask<(decimal Bid, decimal Ask)> LoadLevel1Async(
		CurveMarket market, CancellationToken cancellationToken)
	{
		var units = ProbeVolume.ToBaseUnits(market.BaseToken.Decimals);
		if (units <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base " +
				"units.");
		var bidQuote = await RpcClient.GetQuoteAsync(market,
			CurveTradeTypes.ExactInput, units, cancellationToken);
		var askQuote = await RpcClient.GetQuoteAsync(market,
			CurveTradeTypes.ExactOutput, units, cancellationToken);
		var bidOutput = bidQuote.OutputAmount
			.FromBaseUnits(market.QuoteToken.Decimals);
		var askInput = askQuote.InputAmount
			.FromBaseUnits(market.QuoteToken.Decimals);
		var bid = bidOutput / ProbeVolume;
		var ask = askInput / ProbeVolume;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Curve returned a non-positive quote price.");
		return (bid, ask);
	}

	private ValueTask SendLevel1Async(CurveMarket market, long target,
		decimal bid, decimal ask, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestAskVolume, ProbeVolume),
			cancellationToken);

	private async ValueTask<CurveTrade[]> LoadTradesAsync(
		CurveMarket market, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
		=> await ApiClient.GetTradesAsync(market, from, to,
			maximum.Min(HistoryMaximum).Max(1), cancellationToken);

	private async ValueTask<CurveTrade> ToTradeAsync(
		CurveMarket market, CurveRpcLog log,
		CancellationToken cancellationToken)
	{
		if (log is null || log.IsRemoved || log.Address.IsEmpty() ||
			log.BlockNumber.IsEmpty() || log.TransactionHash.IsEmpty() ||
			log.LogIndex.IsEmpty() ||
			log.Topics is not { Length: > 0 } topics ||
			!CurveExtensions.SwapTopics.Contains(topics[0],
				StringComparer.OrdinalIgnoreCase) ||
			!log.Address.NormalizeAddress().EqualsIgnoreCase(market.PoolId))
			return null;
		int soldIndex;
		int boughtIndex;
		BigInteger soldAmount;
		BigInteger boughtAmount;
		try
		{
			var offset = topics.Length > 1 ? 0 : 1;
			var sold = CurveExtensions.ReadAbiWord(log.Data, offset);
			var bought = CurveExtensions.ReadAbiWord(log.Data, offset + 2);
			if (sold > int.MaxValue || bought > int.MaxValue)
				return null;
			soldIndex = (int)sold;
			soldAmount = CurveExtensions.ReadAbiWord(log.Data, offset + 1);
			boughtIndex = (int)bought;
			boughtAmount = CurveExtensions.ReadAbiWord(log.Data, offset + 3);
		}
		catch (Exception error) when (error is InvalidDataException or
			ArgumentOutOfRangeException or OverflowException)
		{
			return null;
		}
		if (soldAmount <= 0 || boughtAmount <= 0)
			return null;
		Sides side;
		BigInteger baseAmount;
		BigInteger quoteAmount;
		if (soldIndex == market.BaseToken.PoolIndex &&
			boughtIndex == market.QuoteToken.PoolIndex)
		{
			side = Sides.Sell;
			baseAmount = soldAmount;
			quoteAmount = boughtAmount;
		}
		else if (soldIndex == market.QuoteToken.PoolIndex &&
			boughtIndex == market.BaseToken.PoolIndex)
		{
			side = Sides.Buy;
			baseAmount = boughtAmount;
			quoteAmount = soldAmount;
		}
		else
		{
			return null;
		}
		decimal volume;
		decimal quote;
		try
		{
			volume = baseAmount.FromBaseUnits(market.BaseToken.Decimals);
			quote = quoteAmount.FromBaseUnits(market.QuoteToken.Decimals);
		}
		catch (OverflowException)
		{
			return null;
		}
		if (volume <= 0 || quote <= 0)
			return null;
		var blockNumber = log.BlockNumber.ParseInteger();
		var hash = log.TransactionHash.NormalizeHash();
		return new()
		{
			Id = CurveExtensions.CreateTradeId(hash, soldIndex, boughtIndex),
			Time = await GetBlockTimeAsync(blockNumber, cancellationToken),
			Price = quote / volume,
			Volume = volume,
			Side = side,
			TransactionHash = hash,
		};
	}

	private async ValueTask<DateTime> GetBlockTimeAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_blockTimes.TryGetValue(blockNumber, out var cached))
				return cached;
		var time = await RpcClient.GetBlockTimeAsync(blockNumber,
			cancellationToken);
		using (_sync.EnterScope())
		{
			if (!_blockTimes.ContainsKey(blockNumber))
			{
				_blockTimes.Add(blockNumber, time);
				_blockTimeOrder.Enqueue(blockNumber);
				while (_blockTimeOrder.Count > 20_000)
					_blockTimes.Remove(_blockTimeOrder.Dequeue());
			}
			return _blockTimes[blockNumber];
		}
	}

	private async ValueTask<bool> SendTradeAsync(CurveMarket market,
		CurveTrade trade, long target,
		CancellationToken cancellationToken)
	{
		var key = new DeliveryKey(target, trade.Id);
		using (_sync.EnterScope())
		{
			if (!_seenTrades.Add(key))
				return false;
			_tradeDeliveryOrder.Enqueue(key);
			while (_tradeDeliveryOrder.Count > _maximumDeliveryKeys)
				_seenTrades.Remove(_tradeDeliveryOrder.Dequeue());
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Time,
			OriginalTransactionId = target,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side,
		}, cancellationToken);
		return true;
	}

	private async ValueTask<CurveCandle[]> LoadCandlesAsync(
		CurveMarket market, TimeSpan timeFrame, DateTime from, DateTime to,
		int maximum, CancellationToken cancellationToken)
	{
		var trades = await LoadTradesAsync(market, from, to, HistoryMaximum,
			cancellationToken);
		return [.. trades.GroupBy(trade => FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key)
			.Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time)
					.ToArray();
				return new CurveCandle
				{
					OpenTime = group.Key,
					Open = ordered[0].Price,
					High = ordered.Max(static trade => trade.Price),
					Low = ordered.Min(static trade => trade.Price),
					Close = ordered[^1].Price,
					Volume = ordered.Sum(static trade => trade.Volume),
					Turnover = ordered.Sum(static trade =>
						trade.Price * trade.Volume),
					TradeCount = ordered.Length,
				};
			}).TakeLast(maximum)];
	}

	private ValueTask SendCandleAsync(CurveMarket market,
		CurveCandle candle, TimeSpan timeFrame, long target,
		CancellationToken cancellationToken)
	{
		var fingerprint = new CandleFingerprint(candle.Open, candle.High,
			candle.Low, candle.Close, candle.Volume, candle.TradeCount);
		var key = $"{target}:{candle.OpenTime.Ticks}";
		using (_sync.EnterScope())
			_candleFingerprints[key] = fingerprint;
		var closeTime = candle.OpenTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover,
			TotalTicks = candle.TradeCount,
			TypedArg = timeFrame,
			OriginalTransactionId = target,
			State = closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		await DrainRealtimeAsync(cancellationToken);
		await PollLevel1Async(cancellationToken);
		await PollTicksAsync(cancellationToken);
		await PollCandlesAsync(cancellationToken);
	}

	private async ValueTask DrainRealtimeAsync(
		CancellationToken cancellationToken)
	{
		CurveRpcLog[] logs;
		using (_sync.EnterScope())
		{
			logs = [.. _realtimeLogs];
			_realtimeLogs.Clear();
		}
		var finished = new HashSet<long>();
		foreach (var log in logs)
		{
			CurveMarket[] markets;
			try
			{
				var pool = log.Address.NormalizeAddress();
				using (_sync.EnterScope())
					markets = _marketsByPool.TryGetValue(pool,
						out var poolMarkets)
						? [.. poolMarkets]
						: [];
			}
			catch (ArgumentException)
			{
				continue;
			}
			foreach (var market in markets)
			{
				var trade = await ToTradeAsync(market, log,
					cancellationToken);
				if (trade is null)
					continue;
				(long Id, TickSubscription Subscription)[] targets;
				using (_sync.EnterScope())
					targets = [.. _tickSubscriptions.Where(pair =>
						pair.Value.Market.SecurityCode.EqualsIgnoreCase(
							market.SecurityCode)).Select(static pair =>
							(pair.Key, pair.Value))];
				foreach (var target in targets)
				{
					if (target.Subscription.From is DateTime requestedFrom &&
						trade.Time < requestedFrom ||
						target.Subscription.To is DateTime requestedTo &&
						trade.Time > requestedTo)
						continue;
					if (await SendTradeAsync(market, trade, target.Id,
						cancellationToken))
						target.Subscription.Delivered++;
					target.Subscription.LastTime =
						target.Subscription.LastTime.Max(trade.Time);
					if (target.Subscription.Delivered >=
						target.Subscription.Maximum)
						finished.Add(target.Id);
				}
			}
		}
		foreach (var target in finished)
		{
			UnsubscribeTicks(target);
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		}
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(CurveMarket Market, long[] Targets)[] groups;
		using (_sync.EnterScope())
			groups = [.. _level1Subscriptions.GroupBy(static pair =>
					pair.Value.Market.SecurityCode,
					StringComparer.OrdinalIgnoreCase)
				.Select(group => (group.First().Value.Market,
					group.Select(static pair => pair.Key).ToArray()))];
		foreach (var group in groups)
		{
			try
			{
				var snapshot = await LoadLevel1Async(group.Market,
					cancellationToken);
				foreach (var target in group.Targets)
					await SendLevel1Async(group.Market, target,
						snapshot.Bid, snapshot.Ask, cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask PollTicksAsync(
		CancellationToken cancellationToken)
	{
		(long Id, TickSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Select(static pair =>
				(pair.Key, pair.Value))];
		if (subscriptions.Length == 0)
			return;
		var finished = new List<long>();
		foreach (var item in subscriptions)
		{
			var now = DateTime.UtcNow;
			var to = (item.Subscription.To ?? now).ToUniversalTime().Min(now);
			var from = item.Subscription.LastTime - TimeSpan.FromSeconds(15);
			if (item.Subscription.From is DateTime requestedFrom)
				from = from.Max(requestedFrom);
			var remaining = (item.Subscription.Maximum -
				item.Subscription.Delivered).Min(HistoryMaximum).Max(1);
			var trades = await LoadTradesAsync(item.Subscription.Market,
				from, to, remaining, cancellationToken);
			foreach (var trade in trades)
			{
				if (await SendTradeAsync(item.Subscription.Market, trade,
					item.Id, cancellationToken))
					item.Subscription.Delivered++;
				item.Subscription.LastTime =
					item.Subscription.LastTime.Max(trade.Time);
				if (item.Subscription.Delivered >=
					item.Subscription.Maximum)
					break;
			}
			if (item.Subscription.Delivered >= item.Subscription.Maximum ||
				item.Subscription.To is DateTime end && CurrentTime >= end)
				finished.Add(item.Id);
		}
		foreach (var target in finished)
		{
			UnsubscribeTicks(target);
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		}
	}

	private async ValueTask PollCandlesAsync(
		CancellationToken cancellationToken)
	{
		(long Id, CandleSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Select(static pair =>
				(pair.Key, pair.Value))];
		var finished = new List<long>();
		foreach (var item in subscriptions)
		{
			var now = DateTime.UtcNow;
			var to = (item.Subscription.To ?? now).ToUniversalTime().Min(now);
			var from = item.Subscription.LastTime -
				item.Subscription.TimeFrame;
			var maximum = (item.Subscription.Maximum -
				item.Subscription.Delivered).Min(1000).Max(1);
			var candles = await LoadCandlesAsync(item.Subscription.Market,
				item.Subscription.TimeFrame, from, to, maximum,
				cancellationToken);
			foreach (var candle in candles)
			{
				var key = $"{item.Id}:{candle.OpenTime.Ticks}";
				var fingerprint = new CandleFingerprint(candle.Open,
					candle.High, candle.Low, candle.Close, candle.Volume,
					candle.TradeCount);
				var isChanged = false;
				var isNew = false;
				using (_sync.EnterScope())
				{
					isNew = !_candleFingerprints.TryGetValue(key,
						out var previous);
					isChanged = isNew || previous != fingerprint;
					if (isChanged)
						_candleFingerprints[key] = fingerprint;
				}
				if (!isChanged)
					continue;
				await SendCandleAsync(item.Subscription.Market, candle,
					item.Subscription.TimeFrame, item.Id,
					cancellationToken);
				if (isNew)
					item.Subscription.Delivered++;
				item.Subscription.LastTime = candle.OpenTime;
				if (item.Subscription.Delivered >=
					item.Subscription.Maximum)
					break;
			}
			if (item.Subscription.Delivered >= item.Subscription.Maximum ||
				item.Subscription.To is DateTime end && CurrentTime >= end)
				finished.Add(item.Id);
		}
		foreach (var target in finished)
		{
			UnsubscribeCandles(target);
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		}
	}

	private void UnsubscribeTicks(long target)
	{
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Remove(target);
			_seenTrades.RemoveWhere(key => key.SubscriptionId == target);
			var retained = _tradeDeliveryOrder.Where(_seenTrades.Contains)
				.ToArray();
			_tradeDeliveryOrder.Clear();
			foreach (var key in retained)
				_tradeDeliveryOrder.Enqueue(key);
		}
	}

	private void UnsubscribeCandles(long target)
	{
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Remove(target);
			RemoveFingerprintPrefix(_candleFingerprints, target);
		}
	}

	private static DateTime FloorTime(DateTime value, TimeSpan interval)
		=> value.ToUniversalTime().Truncate(interval);

	private static decimal? DecimalStep(int decimals)
	{
		if (decimals is < 0 or > 28)
			return null;
		var result = 1m;
		for (var index = 0; index < decimals; index++)
			result /= 10m;
		return result;
	}

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? 10_000
			: count.Value.Min(10_000).Max(1).To<int>();

	private static int GetCandleHistoryCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to, int maximum)
	{
		if (message.Count is not null)
			return maximum;
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks /
				timeFrame.Ticks + 1).Min(1000L).Max(1L).To<int>();
		return 300;
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
