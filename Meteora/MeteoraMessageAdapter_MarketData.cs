namespace StockSharp.Meteora;

public partial class MeteoraMessageAdapter
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
		MeteoraMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Meteora))
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
				"Meteora quote probes do not expose historical Level1 events.");
		var market = GetMarket(mdMsg.SecurityId);
		await RefreshMarketAsync(market, cancellationToken);
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
				"Meteora does not retain historical bin-book snapshots.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = Math.Clamp(mdMsg.MaxDepth ?? 20, 1,
			MaximumBinArraysPerSide * MeteoraExtensions.BinArraySize * 2);
		await RefreshMarketAsync(market, cancellationToken);
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
				$"Meteora does not support the {timeFrame} candle interval.");
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

	private SecurityMessage CreateSecurity(MeteoraMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.TokenX.Symbol}/{market.TokenY.Symbol}",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.TokenY.Symbol.ToCurrency(),
			PriceStep = DecimalStep(market.TokenY.Decimals),
			VolumeStep = DecimalStep(market.TokenX.Decimals),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.TokenX.Symbol);

	private async ValueTask SendLevel1Async(MeteoraMarket market, long target,
		CancellationToken cancellationToken)
	{
		var snapshot = LoadLevel1(market);
		var book = market.GetBook(1);
		var message = new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestBidVolume,
			book.Bids.FirstOrDefault().Volume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
		.TryAdd(Level1Fields.BestAskVolume,
			book.Asks.FirstOrDefault().Volume)
		.TryAdd(Level1Fields.LastTradePrice,
			market.CurrentPrice > 0 ? market.CurrentPrice : null)
		.TryAdd(Level1Fields.Volume,
			market.OneDayVolume > 0 ? market.OneDayVolume : null);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendDepthAsync(MeteoraMarket market, int depth,
		long target, CancellationToken cancellationToken)
	{
		var book = market.GetBook(depth);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = book.Bids,
			Asks = book.Asks,
		}, cancellationToken);
	}

	private (decimal Bid, decimal Ask) LoadLevel1(MeteoraMarket market)
	{
		var units = ProbeVolume.ToBaseUnits(market.TokenX.Decimals);
		if (units <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base units.");
		var bidQuote = market.GetQuote(Sides.Sell, units,
			SlippageTolerance);
		var askQuote = market.GetQuote(Sides.Buy, units,
			SlippageTolerance);
		var bid = bidQuote.QuoteAmount.FromBaseUnits(market.TokenY.Decimals) /
			ProbeVolume;
		var ask = askQuote.QuoteAmount.FromBaseUnits(market.TokenY.Decimals) /
			ProbeVolume;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Meteora returned a non-positive executable quote.");
		return (bid, ask);
	}

	private async ValueTask<MeteoraTrade[]> LoadTradesAsync(MeteoraMarket market,
		DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		from = from.ToUniversalTime();
		to = to.ToUniversalTime();
		maximum = maximum.Min(MaximumHistoryTransactions).Max(1);
		var trades = new List<MeteoraTrade>();
		var inspected = 0;
		string before = null;
		var isOldRangeReached = false;
		while (inspected < MaximumHistoryTransactions &&
			trades.Count < maximum && !isOldRangeReached)
		{
			var pageSize = (MaximumHistoryTransactions - inspected)
				.Min(100).Max(1);
			var signatures = await RpcClient.GetSignaturesAsync(
				market.PoolAddress, before, pageSize, cancellationToken) ?? [];
			if (signatures.Length == 0)
				break;
			foreach (var signature in signatures)
			{
				inspected++;
				if (signature.BlockTime is long seconds)
				{
					var signatureTime = seconds.FromUnix();
					if (signatureTime < from)
					{
						isOldRangeReached = true;
						break;
					}
					if (signatureTime > to)
						continue;
				}
				if (signature.Error is not null)
					continue;
				var transaction = await RpcClient.GetTransactionAsync(
					signature.Signature, cancellationToken);
				if (transaction?.Meta?.Error is not null)
					continue;
				var time = (transaction?.BlockTime ?? signature.BlockTime)?.FromUnix()
					?? DateTime.UtcNow;
				foreach (var meteoraEvent in MeteoraExtensions.DecodeEvents(
					signature.Signature, transaction, time))
				{
					if (!meteoraEvent.PoolAddress.Equals(market.PoolAddress,
						StringComparison.Ordinal))
						continue;
					var trade = ToTrade(market, meteoraEvent);
					if (trade is not null && trade.Time >= from && trade.Time <= to)
						trades.Add(trade);
				}
			}
			before = signatures[^1].Signature;
			if (signatures.Length < pageSize)
				break;
		}
		return [.. trades.GroupBy(static trade => trade.Id,
			StringComparer.Ordinal).Select(static group => group.First())
			.OrderBy(static trade => trade.Time).TakeLast(maximum)];
	}

	private static MeteoraTrade ToTrade(MeteoraMarket market, MeteoraEvent meteoraEvent)
	{
		BigInteger baseAmount;
		BigInteger quoteAmount;
		Sides side;
		var consumedInput = new BigInteger(meteoraEvent.InputAmount) -
			meteoraEvent.InputAmountLeft;
		if (meteoraEvent.IsSwapForY)
		{
			baseAmount = consumedInput;
			quoteAmount = meteoraEvent.OutputAmount;
			side = Sides.Sell;
		}
		else
		{
			baseAmount = meteoraEvent.OutputAmount;
			quoteAmount = consumedInput;
			side = Sides.Buy;
		}
		if (baseAmount <= 0 || quoteAmount <= 0)
			return null;
		var volume = baseAmount.FromBaseUnits(market.TokenX.Decimals);
		var quote = quoteAmount.FromBaseUnits(market.TokenY.Decimals);
		if (volume <= 0 || quote <= 0)
			return null;
		return new()
		{
			Id = $"{meteoraEvent.Signature}:{meteoraEvent.EventIndex}",
			Signature = meteoraEvent.Signature,
			PoolAddress = meteoraEvent.PoolAddress,
			Time = meteoraEvent.Time,
			Side = side,
			Price = quote / volume,
			Volume = volume,
		};
	}

	private async ValueTask<bool> SendTradeAsync(MeteoraMarket market,
		MeteoraTrade trade, long target, CancellationToken cancellationToken)
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

	private async ValueTask<MeteoraCandle[]> LoadCandlesAsync(MeteoraMarket market,
		TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null)
		{
			var response = await _apiClient.GetCandlesAsync(market.PoolAddress,
				timeFrame.GetTimeFrameCode(), from, to, cancellationToken);
			return [.. (response?.Data ?? []).Where(item => item is not null &&
				item.Timestamp > 0 && item.Open > 0 && item.High > 0 &&
				item.Low > 0 && item.Close > 0)
				.Select(static item => new MeteoraCandle
				{
					OpenTime = item.Timestamp.FromUnix(),
					Open = item.Open,
					High = item.High,
					Low = item.Low,
					Close = item.Close,
					Volume = item.Volume > 0 ? item.Volume / item.Close : 0m,
					Turnover = item.Volume,
				})
				.Where(candle => candle.OpenTime >= from.ToUniversalTime() &&
					candle.OpenTime <= to.ToUniversalTime())
				.OrderBy(static candle => candle.OpenTime).TakeLast(maximum)];
		}
		var trades = await LoadTradesAsync(market, from, to,
			MaximumHistoryTransactions, cancellationToken);
		return [.. trades.GroupBy(trade => FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key).Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time).ToArray();
				return new MeteoraCandle
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

	private ValueTask SendCandleAsync(MeteoraMarket market, MeteoraCandle candle,
		TimeSpan timeFrame, long target, CancellationToken cancellationToken)
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

	private async ValueTask ProcessRealtimeSignatureAsync(string signature,
		string[] logs)
	{
		try
		{
			_ = logs;
			var transaction = await RpcClient.GetTransactionAsync(signature,
				CancellationToken.None);
			if (transaction?.Meta?.Error is not null)
				return;
			var time = transaction?.BlockTime?.FromUnix() ?? DateTime.UtcNow;
			foreach (var meteoraEvent in MeteoraExtensions.DecodeEvents(signature,
				transaction, time))
			{
				MeteoraMarket market;
				(long Id, TickSubscription Subscription)[] targets;
				using (_sync.EnterScope())
				{
					if (!_marketsByPool.TryGetValue(meteoraEvent.PoolAddress,
						out market))
						continue;
					market.ActiveId = meteoraEvent.EndBinId;
					market.CurrentPrice = MeteoraExtensions.ToHumanPrice(
						MeteoraExtensions.GetRawPrice(market.BinStep,
							meteoraEvent.EndBinId), market.TokenX.Decimals,
						market.TokenY.Decimals);
					targets = [.. _tickSubscriptions.Where(pair =>
						ReferenceEquals(pair.Value.Market, market)).Select(
							static pair => (pair.Key, pair.Value))];
				}
				var trade = ToTrade(market, meteoraEvent);
				if (trade is null)
					continue;
				foreach (var target in targets)
				{
					if (target.Subscription.From is DateTime from &&
						trade.Time < from ||
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
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, CancellationToken.None);
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

	private async ValueTask PollDepthAsync(
		CancellationToken cancellationToken)
	{
		(MeteoraMarket Market, (long Id, int Depth)[] Targets)[] groups;
		using (_sync.EnterScope())
			groups = [.. _depthSubscriptions.GroupBy(static pair =>
					pair.Value.Market.PoolAddress, StringComparer.Ordinal)
				.Select(group => (group.First().Value.Market,
					group.Select(static pair => (pair.Key,
						pair.Value.Depth)).ToArray()))];
		foreach (var group in groups)
		{
			try
			{
				await RefreshMarketAsync(group.Market, cancellationToken);
				foreach (var target in group.Targets)
					await SendDepthAsync(group.Market, target.Depth, target.Id,
						cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(MeteoraMarket Market, long[] Targets)[] groups;
		using (_sync.EnterScope())
			groups = [.. _level1Subscriptions.GroupBy(static pair =>
					pair.Value.Market.PoolAddress, StringComparer.Ordinal)
				.Select(group => (group.First().Value.Market,
					group.Select(static pair => pair.Key).ToArray()))];
		foreach (var group in groups)
		{
			try
			{
				await RefreshMarketAsync(group.Market, cancellationToken);
				foreach (var target in group.Targets)
					await SendLevel1Async(group.Market, target,
						cancellationToken);
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

	private async ValueTask RefreshMarketAsync(MeteoraMarket market,
		CancellationToken cancellationToken)
	{
		var poolAccount = await RpcClient.GetAccountAsync(market.PoolAddress,
			cancellationToken);
		if (poolAccount is null)
			throw new InvalidDataException(
				$"Meteora pool '{market.PoolAddress}' was not found.");
		var current = MeteoraExtensions.DecodeLbPair(market.PoolAddress,
			poolAccount);
		if (current.BinStep != market.BinStep ||
			!current.TokenX.Mint.Equals(market.TokenX.Mint,
				StringComparison.Ordinal) ||
			!current.TokenY.Mint.Equals(market.TokenY.Mint,
				StringComparison.Ordinal) ||
			!current.TokenVaultX.Equals(market.TokenVaultX,
				StringComparison.Ordinal) ||
			!current.TokenVaultY.Equals(market.TokenVaultY,
				StringComparison.Ordinal) ||
			!current.Oracle.Equals(market.Oracle,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Meteora pool '{market.PoolAddress}' changed its immutable state.");
		var activeArrayIndex = MeteoraExtensions.GetBinArrayIndex(
			current.ActiveId);
		var scan = MaximumBinArraysPerSide * 8;
		var indexes = new List<long>(scan * 2 + 1) { activeArrayIndex };
		for (var distance = 1; distance <= scan; distance++)
		{
			indexes.Add(activeArrayIndex - distance);
			indexes.Add(activeArrayIndex + distance);
		}
		var addresses = indexes.Select(index =>
			MeteoraExtensions.BinArrayAddress(market.PoolAddress, index)).ToArray();
		var decoded = new List<MeteoraBinArray>();
		for (var offset = 0; offset < addresses.Length; offset += 100)
		{
			var chunkAddresses = addresses.Skip(offset).Take(100).ToArray();
			var accounts = await RpcClient.GetAccountsAsync(chunkAddresses,
				cancellationToken);
			if (accounts.Length != chunkAddresses.Length)
				throw new InvalidDataException(
					$"Meteora pool '{market.PoolAddress}' returned an incomplete " +
					"bin-array snapshot.");
			for (var index = 0; index < accounts.Length; index++)
			{
				var candidateIndex = indexes[offset + index];
				var array = MeteoraExtensions.DecodeBinArray(
					chunkAddresses[index], accounts[index], market.PoolAddress,
					candidateIndex);
				if (array is not null)
					decoded.Add(array);
			}
		}
		var arrays = decoded.Where(array => array.Index <= activeArrayIndex)
			.OrderByDescending(static array => array.Index)
			.Take(MaximumBinArraysPerSide)
			.Concat(decoded.Where(array => array.Index >= activeArrayIndex)
				.OrderBy(static array => array.Index)
				.Take(MaximumBinArraysPerSide))
			.GroupBy(static array => array.Address, StringComparer.Ordinal)
			.Select(static group => group.First()).ToArray();
		MeteoraApiPool apiPool = null;
		if (_apiClient is not null)
		{
			try
			{
				apiPool = await _apiClient.GetPoolAsync(market.PoolAddress,
					cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				this.AddDebugLog("Meteora pool metrics refresh failed: {0}",
					error.Message);
			}
		}
		current.TokenX = market.TokenX;
		current.TokenY = market.TokenY;
		current.BinArrays = arrays;
		current.IsBitmapExtensionInitialized =
			market.IsBitmapExtensionInitialized;
		current.CurrentPrice = MeteoraExtensions.ToHumanPrice(
			MeteoraExtensions.GetRawPrice(current.BinStep, current.ActiveId),
			market.TokenX.Decimals, market.TokenY.Decimals);
		ApplyApiPool(current, apiPool);
		using (_sync.EnterScope())
		{
			market.Parameters = current.Parameters;
			market.VariableParameters = current.VariableParameters;
			market.PairType = current.PairType;
			market.ActiveId = current.ActiveId;
			market.State = current.State;
			market.RewardMints = current.RewardMints;
			market.BinArrays = current.BinArrays;
			market.CurrentPrice = current.CurrentPrice;
			market.TotalValueLocked = current.TotalValueLocked;
			market.OneDayVolume = current.OneDayVolume;
			market.OneDayFees = current.OneDayFees;
			market.DynamicFeePercent = current.DynamicFeePercent;
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
