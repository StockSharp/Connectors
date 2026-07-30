namespace StockSharp.StonFi;

public partial class StonFiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var types = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		StonMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in markets.OrderBy(
			static item => item.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.StonFi))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode))
				continue;
			var security = CreateSecurity(market,
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, types))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
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
				RemoveMarketSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"STON.fi does not expose historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
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
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"STON.fi tick start time cannot be later than end time.");
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var historyOnly = mdMsg.IsHistoryOnly() ||
			to is DateTime requestedTo &&
			requestedTo <= DateTime.UtcNow;
		var trades = await LoadTradesAsync(market,
			from ?? DateTime.UtcNow.AddMinutes(-10),
			to ?? DateTime.UtcNow, cancellationToken);
		if (trades.Length > maximum)
			trades = [.. trades.TakeLast(maximum)];
		var delivered = 0;

		foreach (var trade in trades)
		{
			if (await SendTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken))
				delivered++;
		}

		if (historyOnly || delivered >= maximum)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				From = from,
				To = to,
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
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"STON.fi does not support candle interval '{timeFrame}'.");
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"STON.fi candle start time cannot be later than end time.");
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var historyOnly = mdMsg.IsHistoryOnly() ||
			to is DateTime requestedTo &&
			requestedTo <= DateTime.UtcNow;
		var requestTo = to ?? DateTime.UtcNow;
		var requestFrom = from ?? requestTo -
			timeFrame.Multiply(Math.Min(maximum, 100));
		var trades = await LoadTradesAsync(market, requestFrom, requestTo,
			cancellationToken);
		var candles = StonFiExtensions.AggregateTrades(trades, timeFrame)
			.Where(candle => candle.OpenTime >= requestFrom &&
				candle.OpenTime <= requestTo)
			.ToArray();
		if (candles.Length > maximum)
			candles = [.. candles.TakeLast(maximum)];
		var subscription = new CandleSubscription
		{
			Market = market,
			TimeFrame = timeFrame,
			From = from,
			To = to,
			Maximum = maximum,
		};

		foreach (var candle in candles)
		{
			var state = candle.OpenTime + timeFrame <= DateTime.UtcNow
				? CandleStates.Finished
				: CandleStates.Active;
			if (await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, state, cancellationToken))
				subscription.Delivered++;
			if (state == CandleStates.Active)
				subscription.CurrentCandle = candle;
		}

		if (historyOnly || subscription.Delivered >= maximum)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(StonMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.Asset0.GetName()} / " +
				market.Asset1.GetName(),
			ShortName = market.SecurityCode,
			Class = (market.Pool.Tags ?? []).FirstOrDefault(tag =>
				tag.StartsWith("pool:dex_major_version:",
					StringComparison.OrdinalIgnoreCase)) is string version
					? "STON-AMM-V" + version[(version.LastIndexOf(':') + 1)..]
					: "STON-AMM",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Asset1.GetSymbol().ToCurrency(),
			PriceStep = StonFiExtensions.GetStep(
				Math.Min(9, market.Asset1.GetDecimals())),
			VolumeStep = StonFiExtensions.GetStep(
				market.Asset0.GetDecimals()),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.Asset0.GetSymbol());

	private async ValueTask SendLevel1Async(StonMarket market, long target,
		bool isForced, CancellationToken cancellationToken)
	{
		var pool = await RestClient.GetPoolAsync(market.Pool.Address,
			cancellationToken);
		market.Pool = pool;
		var baseUnits = ProbeVolume.ToBaseUnits(
			market.Asset0.GetDecimals());
		if (baseUnits <= 0)
			baseUnits = BigInteger.One;
		var sell = await RestClient.SimulateSwapAsync(
			market.Asset0.Address, market.Asset1.Address, baseUnits,
			SlippageTolerance / 100m, pool.Address, false,
			cancellationToken);
		var buy = await RestClient.SimulateSwapAsync(
			market.Asset1.Address, market.Asset0.Address, baseUnits,
			SlippageTolerance / 100m, pool.Address, true,
			cancellationToken);
		ValidateSimulation(market, sell, market.Asset0, market.Asset1);
		ValidateSimulation(market, buy, market.Asset1, market.Asset0);

		var sellBase = sell.OfferUnits.ParseInteger("offer_units")
			.FromBaseUnits(market.Asset0.GetDecimals());
		var sellQuote = sell.AskUnits.ParseInteger("ask_units")
			.FromBaseUnits(market.Asset1.GetDecimals());
		var buyBase = buy.AskUnits.ParseInteger("ask_units")
			.FromBaseUnits(market.Asset0.GetDecimals());
		var buyQuote = buy.OfferUnits.ParseInteger("offer_units")
			.FromBaseUnits(market.Asset1.GetDecimals());
		if (sellBase <= 0 || sellQuote <= 0 || buyBase <= 0 ||
			buyQuote <= 0)
			throw new InvalidDataException(
				"STON.fi quote contains non-positive amounts.");
		var bid = sellQuote / sellBase;
		var ask = buyQuote / buyBase;
		if (bid <= 0 || ask <= 0 || bid > ask)
			throw new InvalidDataException(
				$"STON.fi returned an invalid executable quote " +
					$"'{bid}/{ask}'.");
		StonTrade last;
		using (_sync.EnterScope())
			_lastTrades.TryGetValue(
				market.Pool.Address.NormalizeTonAddress(), out last);
		var lastPrice = last?.Price ?? (bid + ask) / 2m;
		var fingerprint = new Level1Fingerprint(bid, ask, lastPrice);
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(target,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[target] = fingerprint;
		}
		var message = new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestBidVolume, sellBase)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestAskVolume, buyBase)
		.TryAdd(Level1Fields.LastTradePrice, lastPrice)
		.TryAdd(Level1Fields.State, SecurityStates.Trading);
		if (last is not null)
			message.TryAdd(Level1Fields.LastTradeVolume, last.Volume);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<StonTrade[]> LoadTradesAsync(StonMarket market,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		from = from.ToUniversalTime();
		to = to.ToUniversalTime();
		if (from > to)
			return [];
		var latest = await RestClient.GetLatestBlockAsync(cancellationToken);
		if (latest?.Block is null || latest.Block.Number <= 0 ||
			latest.Block.Timestamp <= 0)
			throw new InvalidDataException(
				"STON.fi returned no latest event block.");
		var latestTime = latest.Block.Timestamp.ToUtcTime();
		var secondsBack = Math.Max(0,
			(latestTime - from).TotalSeconds);
		var estimatedBlocks = checked((int)Math.Min(
			HistoryBlockLimit,
			Math.Ceiling(secondsBack * 4) +
				StonFiExtensions.MaximumEventBlockRange));
		var fromBlock = Math.Max(0,
			latest.Block.Number - Math.Max(
				StonFiExtensions.MaximumEventBlockRange,
				estimatedBlocks));
		var events = await LoadEventsAsync(fromBlock,
			latest.Block.Number, cancellationToken);
		var poolAddress = market.Pool.Address.NormalizeTonAddress();
		return
		[
			.. events.Where(item => item is not null &&
					item.PoolAddress.SameTonAddress(poolAddress))
				.Select(static item => item.ToTrade())
				.Where(trade => trade is not null &&
					trade.Time >= from && trade.Time <= to)
				.GroupBy(static trade => trade.Id,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())
				.OrderBy(static trade => trade.Time)
		];
	}

	private async ValueTask<StonEvent[]> LoadEventsAsync(int fromBlock,
		int toBlock, CancellationToken cancellationToken)
	{
		if (fromBlock > toBlock)
			return [];
		var result = new List<StonEvent>();

		for (var current = fromBlock; current <= toBlock;)
		{
			var end = Math.Min(toBlock,
				current + StonFiExtensions.MaximumEventBlockRange);
			result.AddRange(await RestClient.GetEventsAsync(current, end,
				cancellationToken));
			if (end == int.MaxValue)
				break;
			current = end + 1;
		}

		return [.. result];
	}

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, Level1Subscription>[] level1;
		using (_sync.EnterScope())
			level1 = [.. _level1Subscriptions];

		foreach (var item in level1)
			await PollSafelyAsync(token => SendLevel1Async(
				item.Value.Market, item.Key, false, token),
				cancellationToken);

		int fromBlock;
		using (_sync.EnterScope())
			fromBlock = _lastEventBlock;
		var latest = await RestClient.GetLatestBlockAsync(cancellationToken);
		if (latest?.Block is null || latest.Block.Number <= 0)
			throw new InvalidDataException(
				"STON.fi returned no latest event block.");
		if (fromBlock <= 0 ||
			fromBlock > latest.Block.Number +
				StonFiExtensions.MaximumEventBlockRange)
			fromBlock = Math.Max(0, latest.Block.Number -
				StonFiExtensions.MaximumEventBlockRange);
		if (latest.Block.Number - fromBlock > HistoryBlockLimit)
			fromBlock = latest.Block.Number - HistoryBlockLimit;
		var events = await LoadEventsAsync(fromBlock,
			latest.Block.Number, cancellationToken);
		using (_sync.EnterScope())
			_lastEventBlock = latest.Block.Number == int.MaxValue
				? int.MaxValue
				: latest.Block.Number + 1;
		await ProcessEventsAsync(events, cancellationToken);
		await FinishExpiredCandlesAsync(cancellationToken);
	}

	private async ValueTask ProcessEventsAsync(StonEvent[] events,
		CancellationToken cancellationToken)
	{
		var grouped = new Dictionary<string, List<StonTrade>>(
			StringComparer.OrdinalIgnoreCase);

		foreach (var item in events.OrderBy(
			static value => value?.Block?.Number ?? int.MinValue)
			.ThenBy(static value => value?.TransactionIndex ??
				long.MinValue)
			.ThenBy(static value => value?.EventIndex ?? long.MinValue))
		{
			var trade = item?.ToTrade();
			if (trade is null)
				continue;
			StonMarket market;
			var pool = item.PoolAddress.NormalizeTonAddress();
			using (_sync.EnterScope())
			{
				if (!_marketsByPool.TryGetValue(pool, out market))
					continue;
				_lastTrades[pool] = trade;
			}
			if (!grouped.TryGetValue(pool, out var trades))
				grouped.Add(pool, trades = []);
			trades.Add(trade);
			await DispatchTradeAsync(market, trade, cancellationToken);
		}

		foreach (var pair in grouped)
		{
			StonMarket market;
			using (_sync.EnterScope())
				_marketsByPool.TryGetValue(pair.Key, out market);
			if (market is not null)
				await DispatchCandlesAsync(market, pair.Value,
					cancellationToken);
		}
	}

	private async ValueTask DispatchTradeAsync(StonMarket market,
		StonTrade trade, CancellationToken cancellationToken)
	{
		KeyValuePair<long, TickSubscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _tickSubscriptions.Where(item =>
				ReferenceEquals(item.Value.Market, market))];

		foreach (var item in targets)
		{
			var subscription = item.Value;
			if (subscription.From is DateTime from && trade.Time < from ||
				subscription.To is DateTime to && trade.Time > to)
				continue;
			var sent = await SendTradeAsync(market, trade, item.Key,
				cancellationToken);
			var finished = false;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.TryGetValue(item.Key,
					out var active))
					continue;
				if (sent)
					active.Delivered++;
				finished = active.Delivered >= active.Maximum ||
					active.To is DateTime until &&
					trade.Time >= until;
				if (finished)
					RemoveMarketSubscriptionNoLock(item.Key);
			}
			if (finished)
				await SendSubscriptionFinishedAsync(item.Key,
					cancellationToken);
		}
	}

	private async ValueTask DispatchCandlesAsync(StonMarket market,
		List<StonTrade> trades, CancellationToken cancellationToken)
	{
		KeyValuePair<long, CandleSubscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _candleSubscriptions.Where(item =>
				ReferenceEquals(item.Value.Market, market))];

		foreach (var item in targets)
		{
			var subscription = item.Value;
			var values = trades.Where(trade =>
					(subscription.From is null ||
						trade.Time >= subscription.From) &&
					(subscription.To is null ||
						trade.Time <= subscription.To))
				.ToArray();
			if (values.Length == 0)
				continue;

			foreach (var incoming in StonFiExtensions.AggregateTrades(
				values, subscription.TimeFrame))
			{
				var previous = subscription.CurrentCandle;
				if (previous is not null &&
					incoming.OpenTime < previous.OpenTime)
					continue;
				if (previous is not null &&
					incoming.OpenTime > previous.OpenTime)
				{
					await SendCandleAsync(market, previous,
						subscription.TimeFrame, item.Key,
						CandleStates.Finished, cancellationToken);
					subscription.CurrentCandle = incoming;
				}
				else if (previous is null)
					subscription.CurrentCandle = incoming;
				else
					subscription.CurrentCandle = MergeCandles(previous,
						incoming);

				var state = subscription.CurrentCandle.OpenTime +
					subscription.TimeFrame <= DateTime.UtcNow
						? CandleStates.Finished
						: CandleStates.Active;
				var sent = await SendCandleAsync(market,
					subscription.CurrentCandle, subscription.TimeFrame,
					item.Key, state, cancellationToken);
				var finished = false;
				using (_sync.EnterScope())
				{
					if (!_candleSubscriptions.TryGetValue(item.Key,
						out var active))
						break;
					if (sent)
						active.Delivered++;
					finished = active.Delivered >= active.Maximum ||
						active.To is DateTime until &&
						subscription.CurrentCandle.OpenTime >= until;
					if (finished)
						RemoveMarketSubscriptionNoLock(item.Key);
				}
				if (finished)
				{
					await SendSubscriptionFinishedAsync(item.Key,
						cancellationToken);
					break;
				}
			}
		}
	}

	private async ValueTask FinishExpiredCandlesAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, CandleSubscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _candleSubscriptions.Where(item =>
				item.Value.CurrentCandle is not null &&
				item.Value.CurrentCandle.OpenTime +
					item.Value.TimeFrame <= DateTime.UtcNow)];

		foreach (var item in targets)
		{
			var sent = await SendCandleAsync(item.Value.Market,
				item.Value.CurrentCandle, item.Value.TimeFrame, item.Key,
				CandleStates.Finished, cancellationToken);
			using (_sync.EnterScope())
			{
				if (_candleSubscriptions.TryGetValue(item.Key,
					out var active))
				{
					if (sent)
						active.Delivered++;
					active.CurrentCandle = null;
				}
			}
		}
	}

	private async ValueTask<bool> SendTradeAsync(StonMarket market,
		StonTrade trade, long target,
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
			TradeVolume = trade.Volume,
			OriginSide = trade.Side,
		}, cancellationToken);
		return true;
	}

	private async ValueTask<bool> SendCandleAsync(StonMarket market,
		StonCandle candle, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
	{
		var identity = "C:" +
			candle.OpenTime.Ticks.ToString(CultureInfo.InvariantCulture) +
			":" + candle.Open.ToString(CultureInfo.InvariantCulture) +
			":" + candle.High.ToString(CultureInfo.InvariantCulture) +
			":" + candle.Low.ToString(CultureInfo.InvariantCulture) +
			":" + candle.Close.ToString(CultureInfo.InvariantCulture) +
			":" + candle.Volume.ToString(CultureInfo.InvariantCulture) +
			":" + candle.TradeCount.ToString(
				CultureInfo.InvariantCulture) + ":" + state;
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
			TotalPrice = candle.Turnover,
			TotalTicks = candle.TradeCount,
			TypedArg = timeFrame,
			OriginalTransactionId = target,
			State = state,
		}, cancellationToken);
		return true;
	}

	private static StonCandle MergeCandles(StonCandle current,
		StonCandle incoming)
	{
		if (current.OpenTime != incoming.OpenTime)
			throw new ArgumentException(
				"Only candles from the same interval can be merged.");
		return new()
		{
			OpenTime = current.OpenTime,
			Open = current.Open,
			High = Math.Max(current.High, incoming.High),
			Low = Math.Min(current.Low, incoming.Low),
			Close = incoming.Close,
			Volume = current.Volume + incoming.Volume,
			Turnover = current.Turnover + incoming.Turnover,
			TradeCount = current.TradeCount + incoming.TradeCount,
		};
	}

	private static void ValidateSimulation(StonMarket market,
		StonSwapSimulation quote, StonAssetInfo offer,
		StonAssetInfo ask)
	{
		ArgumentNullException.ThrowIfNull(quote);
		if (!quote.PoolAddress.SameTonAddress(market.Pool.Address) ||
			!quote.OfferAddress.SameTonAddress(offer.Address) ||
			!quote.AskAddress.SameTonAddress(ask.Address) ||
			quote.Router is null ||
			!quote.RouterAddress.SameTonAddress(
				market.Pool.RouterAddress) ||
			!quote.RouterAddress.SameTonAddress(quote.Router.Address))
			throw new InvalidDataException(
				"STON.fi simulation does not match the requested pool.");
	}

	private bool TryTrackDelivery(long target, string identity)
	{
		var key = new DeliveryKey(target, identity);
		using (_sync.EnterScope())
		{
			if (!_seenMarketData.Add(key))
				return false;
			_deliveryOrder.Enqueue(key);

			while (_deliveryOrder.Count > _maximumDeliveryKeys)
				_seenMarketData.Remove(_deliveryOrder.Dequeue());

			return true;
		}
	}

	private void RemoveMarketSubscriptionNoLock(long target)
	{
		_level1Subscriptions.Remove(target);
		_tickSubscriptions.Remove(target);
		_candleSubscriptions.Remove(target);
		_level1Fingerprints.Remove(target);
		_seenMarketData.RemoveWhere(key =>
			key.SubscriptionId == target);
		var retained = _deliveryOrder.Where(_seenMarketData.Contains)
			.ToArray();
		_deliveryOrder.Clear();

		foreach (var item in retained)
			_deliveryOrder.Enqueue(item);
	}

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? int.MaxValue
			: count.Value.Min(int.MaxValue).Max(1).To<int>();

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
