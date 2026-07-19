namespace StockSharp.Orca;

public partial class OrcaMessageAdapter
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
		OrcaMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Orca))
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
				"Orca quote probes do not expose historical Level1 events.");
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
				$"Orca does not support the {timeFrame} candle interval.");
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

	private SecurityMessage CreateSecurity(OrcaMarket market,
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

	private async ValueTask SendLevel1Async(OrcaMarket market, long target,
		CancellationToken cancellationToken)
	{
		var snapshot = LoadLevel1(market);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ProbeVolume), cancellationToken);
	}

	private (decimal Bid, decimal Ask) LoadLevel1(OrcaMarket market)
	{
		var units = ProbeVolume.ToBaseUnits(market.TokenA.Decimals);
		if (units <= 0)
			throw new InvalidOperationException(
				"The configured quote probe volume rounds to zero base units.");
		var bidQuote = market.GetQuote(Sides.Sell, units,
			SlippageTolerance);
		var askQuote = market.GetQuote(Sides.Buy, units,
			SlippageTolerance);
		var bid = bidQuote.QuoteAmount.FromBaseUnits(market.TokenB.Decimals) /
			ProbeVolume;
		var ask = askQuote.QuoteAmount.FromBaseUnits(market.TokenB.Decimals) /
			ProbeVolume;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Orca returned a non-positive executable quote.");
		return (bid, ask);
	}

	private async ValueTask<OrcaTrade[]> LoadTradesAsync(OrcaMarket market,
		DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		from = from.ToUniversalTime();
		to = to.ToUniversalTime();
		maximum = maximum.Min(MaximumHistoryTransactions).Max(1);
		var trades = new List<OrcaTrade>();
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
				foreach (var orcaEvent in OrcaExtensions.DecodeEvents(
					signature.Signature,
					transaction?.Meta?.LogMessages ?? [], time))
				{
					if (!orcaEvent.PoolAddress.Equals(market.PoolAddress,
						StringComparison.Ordinal))
						continue;
					var trade = ToTrade(market, orcaEvent);
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

	private static OrcaTrade ToTrade(OrcaMarket market, OrcaEvent orcaEvent)
	{
		BigInteger baseAmount;
		BigInteger quoteAmount;
		Sides side;
		if (orcaEvent.IsAToB)
		{
			baseAmount = orcaEvent.InputAmount;
			quoteAmount = new BigInteger(orcaEvent.OutputAmount) -
				orcaEvent.OutputTransferFee;
			side = Sides.Sell;
		}
		else
		{
			baseAmount = new BigInteger(orcaEvent.OutputAmount) -
				orcaEvent.OutputTransferFee;
			quoteAmount = orcaEvent.InputAmount;
			side = Sides.Buy;
		}
		if (baseAmount <= 0 || quoteAmount <= 0)
			return null;
		var volume = baseAmount.FromBaseUnits(market.TokenA.Decimals);
		var quote = quoteAmount.FromBaseUnits(market.TokenB.Decimals);
		if (volume <= 0 || quote <= 0)
			return null;
		return new()
		{
			Id = $"{orcaEvent.Signature}:{orcaEvent.EventIndex}",
			Signature = orcaEvent.Signature,
			PoolAddress = orcaEvent.PoolAddress,
			Time = orcaEvent.Time,
			Side = side,
			Price = quote / volume,
			Volume = volume,
		};
	}

	private async ValueTask<bool> SendTradeAsync(OrcaMarket market,
		OrcaTrade trade, long target, CancellationToken cancellationToken)
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

	private async ValueTask<OrcaCandle[]> LoadCandlesAsync(OrcaMarket market,
		TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var trades = await LoadTradesAsync(market, from, to,
			MaximumHistoryTransactions, cancellationToken);
		return [.. trades.GroupBy(trade => FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key).Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time).ToArray();
				return new OrcaCandle
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

	private ValueTask SendCandleAsync(OrcaMarket market, OrcaCandle candle,
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

	private async ValueTask ProcessRealtimeEventsAsync(string signature,
		string[] logs)
	{
		try
		{
			foreach (var orcaEvent in OrcaExtensions.DecodeEvents(signature,
				logs, DateTime.UtcNow))
			{
				OrcaMarket market;
				(long Id, TickSubscription Subscription)[] targets;
				using (_sync.EnterScope())
				{
					if (!_marketsByPool.TryGetValue(orcaEvent.PoolAddress,
						out market))
						continue;
					market.SqrtPrice = orcaEvent.PostSqrtPrice;
					market.CurrentTickIndex = OrcaExtensions.SqrtPriceToTickIndex(
						orcaEvent.PostSqrtPrice);
					targets = [.. _tickSubscriptions.Where(pair =>
						ReferenceEquals(pair.Value.Market, market)).Select(
							static pair => (pair.Key, pair.Value))];
				}
				var trade = ToTrade(market, orcaEvent);
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
		await PollTicksAsync(cancellationToken);
		await PollCandlesAsync(cancellationToken);
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(OrcaMarket Market, long[] Targets)[] groups;
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

	private async ValueTask RefreshMarketAsync(OrcaMarket market,
		CancellationToken cancellationToken)
	{
		var poolAccount = await RpcClient.GetAccountAsync(market.PoolAddress,
			cancellationToken);
		if (poolAccount is null)
			throw new InvalidDataException(
				$"Orca pool '{market.PoolAddress}' was not found.");
		var current = OrcaExtensions.DecodeWhirlpool(market.PoolAddress,
			poolAccount);
		if (!current.WhirlpoolsConfig.Equals(market.WhirlpoolsConfig,
				StringComparison.Ordinal) ||
			current.TickSpacing != market.TickSpacing ||
			current.FeeTierIndexSeed != market.FeeTierIndexSeed ||
			!current.TokenA.Mint.Equals(market.TokenA.Mint,
				StringComparison.Ordinal) ||
			!current.TokenB.Mint.Equals(market.TokenB.Mint,
				StringComparison.Ordinal) ||
			!current.TokenVaultA.Equals(market.TokenVaultA,
				StringComparison.Ordinal) ||
			!current.TokenVaultB.Equals(market.TokenVaultB,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Orca pool '{market.PoolAddress}' changed its immutable state.");
		var first = OrcaExtensions.GetTickArrayStartIndex(
			current.CurrentTickIndex, current.TickSpacing);
		var offset = current.TickSpacing * OrcaExtensions.TickArraySize;
		var starts = new[]
		{
			first,
			first + offset,
			first + offset * 2,
			first - offset,
			first - offset * 2,
		};
		var addresses = starts.Select(start =>
			OrcaExtensions.TickArrayAddress(market.PoolAddress, start)).ToArray();
		var accounts = await RpcClient.GetAccountsAsync(addresses,
			cancellationToken);
		if (accounts.Length != addresses.Length)
			throw new InvalidDataException(
				$"Orca pool '{market.PoolAddress}' tick-array snapshot is " +
				"incomplete.");
		var arrays = new OrcaTickArray[addresses.Length];
		for (var index = 0; index < arrays.Length; index++)
			arrays[index] = OrcaExtensions.DecodeTickArray(addresses[index],
				accounts[index], market.PoolAddress, starts[index]);
		using (_sync.EnterScope())
		{
			market.FeeRate = current.FeeRate;
			market.Liquidity = current.Liquidity;
			market.SqrtPrice = current.SqrtPrice;
			market.CurrentTickIndex = current.CurrentTickIndex;
			market.TickArrays = arrays;
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
