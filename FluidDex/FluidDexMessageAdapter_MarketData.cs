namespace StockSharp.FluidDex;

public partial class FluidDexMessageAdapter
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
		FluidDexMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.FluidDex))
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
				"Fluid DEX quote probes do not expose historical Level1 " +
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
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Fluid DEX does not retain historical quote ladders.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = Math.Clamp(mdMsg.MaxDepth ?? DepthLevelCount, 1,
			DepthLevelCount);
		await SendDepthAsync(market, depth, mdMsg.TransactionId,
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
		var latestBlock = await RpcClient.GetLatestBlockNumberAsync(
			cancellationToken);
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				From = mdMsg.From?.ToUniversalTime(),
				To = mdMsg.To?.ToUniversalTime(),
				LastTime = trades.Length > 0 ? trades[^1].Time : now,
				LastBlock = latestBlock,
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
				$"Fluid DEX does not support the {timeFrame} candle interval.");
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

	private SecurityMessage CreateSecurity(FluidDexMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.BaseToken.Symbol}/{market.QuoteToken.Symbol} " +
				$"Fluid DEX {FormatFee(market.Fee)}%",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteToken.Symbol.ToCurrency(),
			PriceStep = DecimalStep(market.QuoteToken.Decimals),
			VolumeStep = DecimalStep(market.BaseToken.Decimals),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.BaseToken.Symbol);

	private async ValueTask SendLevel1Async(FluidDexMarket market,
		long target, CancellationToken cancellationToken)
	{
		var snapshot = await LoadLevel1Async(market, cancellationToken);
		await SendLevel1Async(market, target, snapshot.Bid, snapshot.Ask,
			cancellationToken);
	}

	private async ValueTask<(decimal Bid, decimal Ask)> LoadLevel1Async(
		FluidDexMarket market, CancellationToken cancellationToken)
	{
		var units = ProbeVolume.ToBaseUnits(market.BaseToken.Decimals);
		if (units <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base " +
				"units.");
		var bidQuote = await RpcClient.GetQuoteAsync(market,
			FluidDexTradeTypes.ExactInput, units, cancellationToken);
		var askQuote = await RpcClient.GetQuoteAsync(market,
			FluidDexTradeTypes.ExactOutput, units, cancellationToken);
		var bidOutput = bidQuote.OutputAmount
			.FromBaseUnits(market.QuoteToken.Decimals);
		var askInput = askQuote.InputAmount
			.FromBaseUnits(market.QuoteToken.Decimals);
		var bid = bidOutput / ProbeVolume;
		var ask = askInput / ProbeVolume;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Fluid DEX returned a non-positive quote price.");
		return (bid, ask);
	}

	private ValueTask SendLevel1Async(FluidDexMarket market, long target,
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

	private async ValueTask SendDepthAsync(FluidDexMarket market, int depth,
		long target, CancellationToken cancellationToken)
	{
		var book = await LoadDepthAsync(market, depth, cancellationToken);
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = book.Bids,
			Asks = book.Asks,
		}, cancellationToken);
	}

	private async ValueTask<(QuoteChange[] Bids, QuoteChange[] Asks)>
		LoadDepthAsync(FluidDexMarket market, int depth,
		CancellationToken cancellationToken)
	{
		var increment = ProbeVolume.ToBaseUnits(market.BaseToken.Decimals);
		if (increment <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base units.");
		var bids = new List<QuoteChange>(depth);
		var asks = new List<QuoteChange>(depth);
		var previousBidOutput = BigInteger.Zero;
		var previousAskInput = BigInteger.Zero;
		for (var level = 1; level <= depth; level++)
		{
			var cumulative = increment * level;
			var bidQuote = await RpcClient.GetQuoteAsync(market,
				FluidDexTradeTypes.ExactInput, cumulative, cancellationToken);
			var askQuote = await RpcClient.GetQuoteAsync(market,
				FluidDexTradeTypes.ExactOutput, cumulative, cancellationToken);
			var bidValue = (bidQuote.OutputAmount - previousBidOutput)
				.FromBaseUnits(market.QuoteToken.Decimals);
			var askValue = (askQuote.InputAmount - previousAskInput)
				.FromBaseUnits(market.QuoteToken.Decimals);
			var bidPrice = bidValue / ProbeVolume;
			var askPrice = askValue / ProbeVolume;
			if (bidPrice <= 0 || askPrice <= 0)
				throw new InvalidDataException(
					"Fluid DEX returned a non-positive depth level.");
			bids.Add(new(bidPrice, ProbeVolume));
			asks.Add(new(askPrice, ProbeVolume));
			previousBidOutput = bidQuote.OutputAmount;
			previousAskInput = askQuote.InputAmount;
		}
		return ([.. bids.OrderByDescending(static quote => quote.Price)],
			[.. asks.OrderBy(static quote => quote.Price)]);
	}

	private async ValueTask<FluidDexTrade[]> LoadTradesAsync(
		FluidDexMarket market, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var latest = await RpcClient.GetLatestBlockNumberAsync(
			cancellationToken);
		var fromBlock = from <= DateTime.UnixEpoch
			? BigInteger.Max(BigInteger.Zero, latest - HistoryBlockCount)
			: await RpcClient.FindBlockAsync(from, latest, cancellationToken);
		var toBlock = to >= DateTime.UtcNow - TimeSpan.FromSeconds(3)
			? latest
			: await RpcClient.FindBlockAsync(to, latest, cancellationToken);
		return await LoadTradesByBlocksAsync(market, fromBlock, toBlock,
			maximum, from, to, cancellationToken);
	}

	private async ValueTask<FluidDexTrade[]> LoadTradesByBlocksAsync(
		FluidDexMarket market, BigInteger fromBlock, BigInteger toBlock,
		int maximum, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		if (toBlock < fromBlock)
			return [];
		var logs = new List<FluidDexRpcLog>();
		var end = toBlock;
		while (end >= fromBlock && logs.Count < maximum)
		{
			var start = BigInteger.Max(fromBlock,
				end - HistoryBlockRange + 1);
			logs.AddRange(await RpcClient.GetLogsAsync(market, start, end,
				cancellationToken) ?? []);
			if (start == fromBlock)
				break;
			end = start - 1;
		}
		var result = new List<FluidDexTrade>();
		foreach (var log in logs)
		{
			var trade = await ToTradeAsync(market, log, cancellationToken);
			if (trade is not null && trade.Time >= from.ToUniversalTime() &&
				trade.Time <= to.ToUniversalTime())
				result.Add(trade);
		}
		return [.. result.GroupBy(static trade => trade.Id,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum)];
	}

	private async ValueTask<FluidDexTrade> ToTradeAsync(
		FluidDexMarket market, FluidDexRpcLog log,
		CancellationToken cancellationToken)
	{
		if (log is null || log.IsRemoved || log.Address.IsEmpty() ||
			log.BlockNumber.IsEmpty() || log.TransactionHash.IsEmpty() ||
			log.LogIndex.IsEmpty() ||
			log.Topics is not { Length: > 0 } topics ||
			!topics[0].EqualsIgnoreCase(FluidDexExtensions.SwapTopic) ||
			!log.Address.NormalizeAddress().EqualsIgnoreCase(market.PoolId))
			return null;
		BigInteger signedAmount0;
		BigInteger signedAmount1;
		try
		{
			var direction = FluidDexExtensions.ReadAbiWord(log.Data, 0);
			var amountIn = FluidDexExtensions.ReadAbiWord(log.Data, 1);
			var amountOut = FluidDexExtensions.ReadAbiWord(log.Data, 2);
			if (direction < 0 || direction > 1 || amountIn <= 0 ||
				amountOut <= 0)
				return null;
			var swap0To1 = direction == BigInteger.One;
			signedAmount0 = swap0To1 ? amountIn : -amountOut;
			signedAmount1 = swap0To1 ? -amountOut : amountIn;
		}
		catch (InvalidDataException)
		{
			return null;
		}
		var isBaseToken0 = market.BaseToken.Address.EqualsIgnoreCase(
			market.Token0.Address);
		var signedBase = isBaseToken0 ? signedAmount0 : signedAmount1;
		var signedQuote = isBaseToken0 ? signedAmount1 : signedAmount0;
		if (signedBase == 0 || signedQuote == 0 ||
			signedBase.Sign == signedQuote.Sign)
			return null;
		decimal volume;
		decimal quote;
		try
		{
			volume = BigInteger.Abs(signedBase).FromBaseUnits(
				market.BaseToken.Decimals);
			quote = BigInteger.Abs(signedQuote).FromBaseUnits(
				market.QuoteToken.Decimals);
		}
		catch (OverflowException)
		{
			return null;
		}
		if (volume <= 0 || quote <= 0)
			return null;
		var blockNumber = log.BlockNumber.ParseInteger();
		return new()
		{
			Id = $"{log.TransactionHash.ToLowerInvariant()}:" +
				log.LogIndex.ToLowerInvariant(),
			Time = await GetBlockTimeAsync(blockNumber, cancellationToken),
			Price = quote / volume,
			Volume = volume,
			Side = signedBase > 0 ? Sides.Sell : Sides.Buy,
			TransactionHash = log.TransactionHash.ToLowerInvariant(),
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

	private async ValueTask<bool> SendTradeAsync(FluidDexMarket market,
		FluidDexTrade trade, long target,
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

	private async ValueTask<FluidDexCandle[]> LoadCandlesAsync(
		FluidDexMarket market, TimeSpan timeFrame, DateTime from, DateTime to,
		int maximum, CancellationToken cancellationToken)
	{
		var trades = await LoadTradesAsync(market, from, to, 10_000,
			cancellationToken);
		return [.. trades.GroupBy(trade => FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key)
			.Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time)
					.ToArray();
				return new FluidDexCandle
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

	private ValueTask SendCandleAsync(FluidDexMarket market,
		FluidDexCandle candle, TimeSpan timeFrame, long target,
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
		await PollDepthAsync(cancellationToken);
		await PollTicksAsync(cancellationToken);
		await PollCandlesAsync(cancellationToken);
	}

	private async ValueTask PollDepthAsync(
		CancellationToken cancellationToken)
	{
		(FluidDexMarket Market, int Depth, long[] Targets)[] groups;
		using (_sync.EnterScope())
			groups = [.. _depthSubscriptions.GroupBy(static pair =>
					pair.Value.Market.PoolId,
					StringComparer.OrdinalIgnoreCase)
				.Select(group => (group.First().Value.Market,
					group.Max(static pair => pair.Value.Depth),
					group.Select(static pair => pair.Key).ToArray()))];
		foreach (var group in groups)
		{
			try
			{
				var book = await LoadDepthAsync(group.Market, group.Depth,
					cancellationToken);
				foreach (var target in group.Targets)
				{
					int targetDepth;
					using (_sync.EnterScope())
						targetDepth = _depthSubscriptions.TryGetValue(target,
							out var subscription)
							? subscription.Depth
							: 0;
					if (targetDepth <= 0)
						continue;
					await SendOutMessageAsync(new QuoteChangeMessage
					{
						SecurityId = group.Market.ToStockSharp(),
						ServerTime = CurrentTime,
						OriginalTransactionId = target,
						State = QuoteChangeStates.SnapshotComplete,
						Bids = [.. book.Bids.Take(targetDepth)],
						Asks = [.. book.Asks.Take(targetDepth)],
					}, cancellationToken);
				}
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask DrainRealtimeAsync(
		CancellationToken cancellationToken)
	{
		FluidDexRpcLog[] logs;
		using (_sync.EnterScope())
		{
			logs = [.. _realtimeLogs];
			_realtimeLogs.Clear();
		}
		var finished = new HashSet<long>();
		foreach (var log in logs)
		{
			FluidDexMarket market;
			try
			{
				var pool = log.Address.NormalizeAddress();
				using (_sync.EnterScope())
					_marketsByPool.TryGetValue(pool, out market);
			}
			catch (ArgumentException)
			{
				continue;
			}
			if (market is null)
				continue;
			var trade = await ToTradeAsync(market, log, cancellationToken);
			if (trade is null)
				continue;
			(long Id, TickSubscription Subscription)[] targets;
			using (_sync.EnterScope())
				targets = [.. _tickSubscriptions
					.Where(pair => pair.Value.Market.PoolId.EqualsIgnoreCase(
						market.PoolId))
					.Select(static pair => (pair.Key, pair.Value))];
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
				if (!log.BlockNumber.IsEmpty())
					target.Subscription.LastBlock = BigInteger.Max(
						target.Subscription.LastBlock,
						log.BlockNumber.ParseInteger());
				if (target.Subscription.Delivered >=
					target.Subscription.Maximum)
					finished.Add(target.Id);
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
		(FluidDexMarket Market, long[] Targets)[] groups;
		using (_sync.EnterScope())
			groups = [.. _level1Subscriptions.GroupBy(static pair =>
					pair.Value.Market.PoolId,
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
		var latest = await RpcClient.GetLatestBlockNumberAsync(
			cancellationToken);
		var finished = new List<long>();
		foreach (var item in subscriptions)
		{
			var fromBlock = BigInteger.Max(BigInteger.Zero,
				item.Subscription.LastBlock - 12);
			var trades = await LoadTradesByBlocksAsync(
				item.Subscription.Market, fromBlock, latest, 10_000,
				item.Subscription.From ?? DateTime.UnixEpoch,
				item.Subscription.To ?? DateTime.MaxValue,
				cancellationToken);
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
			item.Subscription.LastBlock = latest;
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

	private static string FormatFee(BigInteger fee)
		=> ((decimal)fee / 10_000m).ToString("0.####",
			CultureInfo.InvariantCulture);

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
