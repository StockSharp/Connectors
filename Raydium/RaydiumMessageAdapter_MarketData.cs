namespace StockSharp.Raydium;

public partial class RaydiumMessageAdapter
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
		RaydiumMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Raydium))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode))
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
				"Raydium quote probes do not expose historical Level1 events.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, cancellationToken);
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
				"Raydium does not retain historical routed quote ladders.");
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
		var from = (mdMsg.From ?? now - TimeSpan.FromHours(1))
			.ToUniversalTime();
		var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var trades = await LoadTradesAsync(market, from, to,
			maximum.Min(MaximumHistoryTransactions), cancellationToken);
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
				LastTime = trades.Length > 0 ? trades[^1].Time : from,
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
				$"Raydium does not support the {timeFrame} candle interval.");
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
			requestedTo.ToUniversalTime() <= now || candles.Length >= maximum)
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
				LastTime = candles.Length > 0 ? candles[^1].OpenTime : from,
				Maximum = maximum,
				Delivered = candles.Length,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(RaydiumMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.TokenA.Symbol}/{market.TokenB.Symbol}",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.TokenB.Symbol.ToCurrency(),
			PriceStep = DecimalStep(market.TokenB.Decimals),
			VolumeStep = DecimalStep(market.TokenA.Decimals),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.TokenA.Symbol);

	private async ValueTask SendLevel1Async(RaydiumMarket market, long target,
		CancellationToken cancellationToken)
	{
		var snapshot = await LoadLevel1Async(market, cancellationToken);
		var message = new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ProbeVolume);
		if (market.ReferencePrice > 0)
			message.TryAdd(Level1Fields.LastTradePrice, market.ReferencePrice);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<(decimal Bid, decimal Ask)> LoadLevel1Async(
		RaydiumMarket market, CancellationToken cancellationToken)
	{
		var units = ProbeVolume.ToBaseUnits(market.TokenA.Decimals);
		if (units <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base units.");
		var slippage = GetSlippageBasisPoints();
		var bidQuote = await _apiClient.GetQuoteAsync(market, Sides.Sell,
			units, slippage, cancellationToken);
		var askQuote = await _apiClient.GetQuoteAsync(market, Sides.Buy,
			units, slippage, cancellationToken);
		var bid = GetQuotePrice(bidQuote);
		var ask = GetQuotePrice(askQuote);
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Raydium returned a non-positive executable quote.");
		return (bid, ask);
	}

	private async ValueTask SendDepthAsync(RaydiumMarket market, int depth,
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
		LoadDepthAsync(RaydiumMarket market, int depth,
		CancellationToken cancellationToken)
	{
		var increment = ProbeVolume.ToBaseUnits(market.TokenA.Decimals);
		if (increment <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base units.");
		var bids = new List<QuoteChange>(depth);
		var asks = new List<QuoteChange>(depth);
		var previousBidOutput = BigInteger.Zero;
		var previousAskInput = BigInteger.Zero;
		var slippage = GetSlippageBasisPoints();
		for (var level = 1; level <= depth; level++)
		{
			var cumulative = increment * level;
			var bidQuote = await _apiClient.GetQuoteAsync(market, Sides.Sell,
				cumulative, slippage, cancellationToken);
			var askQuote = await _apiClient.GetQuoteAsync(market, Sides.Buy,
				cumulative, slippage, cancellationToken);
			var bidOutput = ParseAmount(bidQuote.Data.OutputAmount,
				"outputAmount");
			var askInput = ParseAmount(askQuote.Data.InputAmount, "inputAmount");
			var bidValue = (bidOutput - previousBidOutput).FromBaseUnits(
				market.TokenB.Decimals);
			var askValue = (askInput - previousAskInput).FromBaseUnits(
				market.TokenB.Decimals);
			var bidPrice = bidValue / ProbeVolume;
			var askPrice = askValue / ProbeVolume;
			if (bidPrice <= 0 || askPrice <= 0)
				throw new InvalidDataException(
					"Raydium returned a non-positive depth level.");
			bids.Add(new(bidPrice, ProbeVolume));
			asks.Add(new(askPrice, ProbeVolume));
			previousBidOutput = bidOutput;
			previousAskInput = askInput;
		}
		return ([.. bids.OrderByDescending(static quote => quote.Price)],
			[.. asks.OrderBy(static quote => quote.Price)]);
	}

	private async ValueTask<RaydiumTrade[]> LoadTradesAsync(
		RaydiumMarket market, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		from = from.ToUniversalTime();
		to = to.ToUniversalTime();
		maximum = maximum.Min(MaximumHistoryTransactions).Max(1);
		var candidates = new Dictionary<string, RaydiumRpcSignatureInfo>(
			StringComparer.Ordinal);
		foreach (var pool in market.Pools)
		{
			var signatures = await RpcClient.GetSignaturesAsync(
				pool.PoolAddress, null, MaximumHistoryTransactions,
				cancellationToken) ?? [];
			foreach (var signature in signatures)
			{
				if (signature?.Signature.IsEmpty() != false ||
					signature.Error is not null)
					continue;
				if (signature.BlockTime is long seconds)
				{
					var time = seconds.FromUnix();
					if (time < from || time > to)
						continue;
				}
				candidates[signature.Signature] = signature;
			}
		}
		var trades = new List<RaydiumTrade>();
		foreach (var candidate in candidates.Values.OrderByDescending(
			static item => item.BlockTime ?? long.MinValue)
			.Take(MaximumHistoryTransactions))
		{
			var transaction = await RpcClient.GetTransactionAsync(
				candidate.Signature, cancellationToken);
			if (transaction?.Meta?.Error is not null)
				continue;
			var time = (transaction?.BlockTime ?? candidate.BlockTime)?.FromUnix()
				?? DateTime.UtcNow;
			foreach (var trade in RaydiumExtensions.DecodeTrades(
				candidate.Signature, transaction, market, time))
				if (trade.Time >= from && trade.Time <= to)
					trades.Add(trade);
		}
		return [.. trades.GroupBy(static trade => trade.Id,
			StringComparer.Ordinal).Select(static group => group.First())
			.OrderBy(static trade => trade.Time).TakeLast(maximum)];
	}

	private async ValueTask<bool> SendTradeAsync(RaydiumMarket market,
		RaydiumTrade trade, long target, CancellationToken cancellationToken)
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

	private async ValueTask<RaydiumCandle[]> LoadCandlesAsync(
		RaydiumMarket market, TimeSpan timeFrame, DateTime from, DateTime to,
		int maximum, CancellationToken cancellationToken)
	{
		var trades = await LoadTradesAsync(market, from, to,
			MaximumHistoryTransactions, cancellationToken);
		return [.. trades.GroupBy(trade => FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key).Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time).ToArray();
				return new RaydiumCandle
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

	private ValueTask SendCandleAsync(RaydiumMarket market,
		RaydiumCandle candle, TimeSpan timeFrame, long target,
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

	private async ValueTask ProcessRealtimeTransactionAsync(string signature)
	{
		try
		{
			using (_sync.EnterScope())
			{
				if (!_seenRealtimeSignatures.Add(signature))
					return;
				_realtimeSignatureOrder.Enqueue(signature);
				while (_realtimeSignatureOrder.Count > _maximumDeliveryKeys)
					_seenRealtimeSignatures.Remove(
						_realtimeSignatureOrder.Dequeue());
			}
			RaydiumRpcTransaction transaction = null;
			for (var attempt = 0; attempt < 3 && transaction is null; attempt++)
			{
				transaction = await RpcClient.GetTransactionAsync(signature,
					CancellationToken.None);
				if (transaction is null)
					await Task.Delay(TimeSpan.FromMilliseconds(150),
						CancellationToken.None);
			}
			if (transaction?.Meta?.Error is not null)
				return;
			if (transaction is null)
			{
				using (_sync.EnterScope())
					_seenRealtimeSignatures.Remove(signature);
				return;
			}
			var accountKeys = (transaction.Transaction?.Message?.AccountKeys ?? [])
				.Concat(transaction.Meta.LoadedAddresses?.Writable ?? [])
				.Concat(transaction.Meta.LoadedAddresses?.ReadOnly ?? []).ToArray();
			RaydiumMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _marketsByPool.Where(pair =>
					accountKeys.Contains(pair.Key, StringComparer.Ordinal))
					.Select(static pair => pair.Value).Distinct()];
			var time = transaction.BlockTime?.FromUnix() ?? DateTime.UtcNow;
			foreach (var market in markets)
				foreach (var trade in RaydiumExtensions.DecodeTrades(signature,
					transaction, market, time))
					await DispatchRealtimeTradeAsync(market, trade);
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, CancellationToken.None);
		}
	}

	private async ValueTask DispatchRealtimeTradeAsync(RaydiumMarket market,
		RaydiumTrade trade)
	{
		(long Id, TickSubscription Subscription)[] targets;
		using (_sync.EnterScope())
			targets = [.. _tickSubscriptions.Where(pair =>
				ReferenceEquals(pair.Value.Market, market)).Select(
					static pair => (pair.Key, pair.Value))];
		foreach (var target in targets)
		{
			if (target.Subscription.From is DateTime from && trade.Time < from ||
				target.Subscription.To is DateTime to && trade.Time > to)
				continue;
			if (!await SendTradeAsync(market, trade, target.Id,
				CancellationToken.None))
				continue;
			var isFinished = false;
			using (_sync.EnterScope())
			{
				target.Subscription.Delivered++;
				target.Subscription.LastTime = trade.Time.Max(
					target.Subscription.LastTime);
				isFinished = target.Subscription.Delivered >=
					target.Subscription.Maximum;
			}
			if (isFinished)
			{
				UnsubscribeTicks(target.Id);
				await SendSubscriptionFinishedAsync(target.Id,
					CancellationToken.None);
			}
		}
	}

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		await PollLevel1Async(cancellationToken);
		await PollDepthAsync(cancellationToken);
		await PollTicksAsync(cancellationToken);
		await PollCandlesAsync(cancellationToken);
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(RaydiumMarket Market, long[] Targets)[] groups;
		using (_sync.EnterScope())
			groups = [.. _level1Subscriptions.GroupBy(static pair =>
					pair.Value.Market.MintPairKey, StringComparer.Ordinal)
				.Select(group => (group.First().Value.Market,
					group.Select(static pair => pair.Key).ToArray()))];
		foreach (var group in groups)
		{
			try
			{
				var snapshot = await LoadLevel1Async(group.Market,
					cancellationToken);
				foreach (var target in group.Targets)
				{
					var message = new Level1ChangeMessage
					{
						SecurityId = group.Market.ToStockSharp(),
						ServerTime = CurrentTime,
						OriginalTransactionId = target,
					}
					.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
					.TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
					.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
					.TryAdd(Level1Fields.BestAskVolume, ProbeVolume);
					await SendOutMessageAsync(message, cancellationToken);
				}
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask PollDepthAsync(
		CancellationToken cancellationToken)
	{
		(long Id, DepthSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Select(static pair =>
				(pair.Key, pair.Value))];
		foreach (var item in subscriptions)
		{
			try
			{
				await SendDepthAsync(item.Subscription.Market,
					item.Subscription.Depth, item.Id, cancellationToken);
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
		var finished = new List<long>();
		foreach (var item in subscriptions)
		{
			var from = item.Subscription.LastTime - TimeSpan.FromSeconds(1);
			var now = DateTime.UtcNow;
			var to = (item.Subscription.To ?? now).ToUniversalTime().Min(now);
			var trades = await LoadTradesAsync(item.Subscription.Market, from,
				to, MaximumHistoryTransactions, cancellationToken);
			foreach (var trade in trades)
			{
				if (item.Subscription.From is DateTime requestedFrom &&
					trade.Time < requestedFrom ||
					item.Subscription.To is DateTime requestedTo &&
					trade.Time > requestedTo)
					continue;
				if (await SendTradeAsync(item.Subscription.Market, trade,
					item.Id, cancellationToken))
					item.Subscription.Delivered++;
				item.Subscription.LastTime = trade.Time.Max(
					item.Subscription.LastTime);
				if (item.Subscription.Delivered >= item.Subscription.Maximum)
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
			var from = item.Subscription.LastTime - item.Subscription.TimeFrame;
			var maximum = (item.Subscription.Maximum -
				item.Subscription.Delivered).Min(MaximumHistoryTransactions)
				.Max(1);
			var candles = await LoadCandlesAsync(item.Subscription.Market,
				item.Subscription.TimeFrame, from, to, maximum,
				cancellationToken);
			foreach (var candle in candles)
			{
				var key = $"{item.Id}:{candle.OpenTime.Ticks}";
				var fingerprint = new CandleFingerprint(candle.Open, candle.High,
					candle.Low, candle.Close, candle.Volume, candle.TradeCount);
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
					item.Subscription.TimeFrame, item.Id, cancellationToken);
				if (isNew)
					item.Subscription.Delivered++;
				item.Subscription.LastTime = candle.OpenTime;
				if (item.Subscription.Delivered >= item.Subscription.Maximum)
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

	private decimal GetQuotePrice(RaydiumQuote quote)
	{
		var input = ParseAmount(quote.Data.InputAmount, "inputAmount");
		var output = ParseAmount(quote.Data.OutputAmount, "outputAmount");
		decimal baseVolume;
		decimal quoteVolume;
		if (quote.Side == Sides.Sell)
		{
			baseVolume = input.FromBaseUnits(quote.Market.TokenA.Decimals);
			quoteVolume = output.FromBaseUnits(quote.Market.TokenB.Decimals);
		}
		else
		{
			baseVolume = output.FromBaseUnits(quote.Market.TokenA.Decimals);
			quoteVolume = input.FromBaseUnits(quote.Market.TokenB.Decimals);
		}
		if (baseVolume <= 0 || quoteVolume <= 0)
			throw new InvalidDataException(
				"Raydium quote contains non-positive token amounts.");
		return quoteVolume / baseVolume;
	}

	private int GetSlippageBasisPoints()
		=> checked((int)(SlippageTolerance * 100m));

	private static BigInteger ParseAmount(string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var amount) || amount <= 0)
			throw new InvalidDataException(
				$"Raydium returned invalid {field} '{value}'.");
		return amount;
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
		=> count is null ? int.MaxValue : count.Value.Min(1000).Max(1).To<int>();

	private int GetCandleHistoryCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to, int maximum)
	{
		if (message.Count is not null)
			return maximum.Min(MaximumHistoryTransactions);
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(MaximumHistoryTransactions).Max(1).To<int>();
		return 100.Min(MaximumHistoryTransactions);
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
