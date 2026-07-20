namespace StockSharp.THORChain;

public partial class THORChainMessageAdapter
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
		THORChainMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.THORChain))
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
			{
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
				RemoveFingerprintPrefix(_level1Fingerprints,
					mdMsg.OriginalTransactionId);
			}
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"THORChain executable quotes do not expose historical " +
				"Level1 events.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, true,
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
		var trades = await LoadTradesAsync(market, from, to, maximum,
			cancellationToken);
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
				$"THORChain does not support the {timeFrame} candle interval.");
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var historyCount = GetCandleHistoryCount(mdMsg, timeFrame, to,
			maximum);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * historyCount);
		var candles = await LoadCandlesAsync(market, timeFrame, from, to,
			historyCount, cancellationToken);
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

	private SecurityMessage CreateSecurity(THORChainMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"RUNE/{market.Ticker} ({market.Asset})",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Ticker.ToCurrency(),
			PriceStep = DecimalStep(8),
			VolumeStep = DecimalStep(8),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId("RUNE");

	private async ValueTask SendLevel1Async(THORChainMarket market,
		long target, bool isForced, CancellationToken cancellationToken)
	{
		var snapshot = await LoadLevel1Async(market, cancellationToken);
		var fingerprint = new Level1Fingerprint(snapshot.Bid, snapshot.Ask,
			snapshot.Volume24Hours);
		var key = $"{target}:{market.SecurityCode}";
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[key] = fingerprint;
		}
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
		if (snapshot.Volume24Hours > 0)
			message.TryAdd(Level1Fields.Volume, snapshot.Volume24Hours);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<(decimal Bid, decimal Ask, decimal Volume24Hours)>
		LoadLevel1Async(THORChainMarket market,
			CancellationToken cancellationToken)
	{
		THORChainPool pool;
		using (_sync.EnterScope())
			pool = market.Pool;
		var runeAmount = ProbeVolume.ToProtocolAmount();
		var assetPrice = THORChainExtensions.ParseDecimal(pool.AssetPrice,
			"pool asset price");
		if (assetPrice <= 0)
			throw new InvalidDataException(
				$"THORChain pool '{market.Asset}' has no positive price.");
		var assetProbe = ProbeVolume / assetPrice;
		var assetAmount = assetProbe.ToProtocolAmount();
		var bidQuote = await ApiClient.GetQuoteAsync(
			THORChainExtensions.RuneAsset, market.Asset, runeAmount, null, null,
			1, 0, null, cancellationToken);
		var askQuote = await ApiClient.GetQuoteAsync(market.Asset,
			THORChainExtensions.RuneAsset, assetAmount, null, null, 1, 0, null,
			cancellationToken);
		var bidOutput = bidQuote.ExpectedOutput.FromProtocolAmount(
			"quote expected output");
		var askOutput = askQuote.ExpectedOutput.FromProtocolAmount(
			"quote expected output");
		var bid = bidOutput / ProbeVolume;
		var ask = assetProbe / askOutput;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"THORChain returned a non-positive executable quote.");
		var volume = pool.Volume24Hours.FromProtocolAmount(
			"24-hour volume");
		return (bid, ask, volume);
	}

	private async ValueTask<THORChainTrade[]> LoadTradesAsync(
		THORChainMarket market, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		maximum = maximum.Min(HistoryMaximum).Max(1);
		var actions = new List<THORChainAction>();
		for (var offset = 0; offset < HistoryMaximum &&
			actions.Count < HistoryMaximum; offset += 50)
		{
			var page = await ApiClient.GetActionsAsync(market.Asset, null, null,
				50, offset, cancellationToken);
			var items = page?.Actions ?? [];
			if (items.Length == 0)
				break;
			actions.AddRange(items.Where(static action => action is not null));
			if (items.Length < 50)
				break;
			var oldest = items.Select(static action =>
			{
				try
				{
					return action.Date.ParseActionTime();
				}
				catch (Exception error) when (error is InvalidDataException or
					OverflowException)
				{
					return DateTime.MaxValue;
				}
			}).Min();
			if (oldest < from)
				break;
		}
		var result = new List<THORChainTrade>();
		foreach (var action in actions)
			if (TryCreateTrade(market, action, out var trade) &&
				trade.Time >= from && trade.Time <= to)
				result.Add(trade);
		return [.. result.OrderBy(static trade => trade.Time)
			.ThenBy(static trade => trade.Id, StringComparer.Ordinal)
			.TakeLast(maximum)];
	}

	private static bool TryCreateTrade(THORChainMarket market,
		THORChainAction action, out THORChainTrade trade)
	{
		trade = null;
		if (action is null || action.Type != THORChainActionTypes.Swap ||
			action.Status != THORChainActionStatuses.Success)
			return false;
		try
		{
			THORChainActionTransaction runeTransaction;
			THORChainCoinAmount runeCoin;
			THORChainActionTransaction assetTransaction;
			THORChainCoinAmount assetCoin;
			Sides side;
			if (TryFindCoin(action.Inputs, THORChainExtensions.RuneAsset,
				out runeTransaction, out runeCoin) &&
				TryFindCoin(action.Outputs, market.Asset, out assetTransaction,
					out assetCoin))
			{
				side = Sides.Sell;
			}
			else if (TryFindCoin(action.Inputs, market.Asset,
				out assetTransaction, out assetCoin) &&
				TryFindCoin(action.Outputs, THORChainExtensions.RuneAsset,
					out runeTransaction, out runeCoin))
			{
				side = Sides.Buy;
			}
			else
			{
				return false;
			}
			var volume = runeCoin.Amount.FromProtocolAmount("trade RUNE amount");
			var quote = assetCoin.Amount.FromProtocolAmount("trade asset amount");
			if (volume <= 0 || quote <= 0)
				return false;
			var inputHash = (action.Inputs ?? []).FirstOrDefault(static tx =>
				!tx.TransactionId.IsEmpty())?.TransactionId
				.NormalizeTransactionHash();
			var outputHash = (action.Outputs ?? []).FirstOrDefault(static tx =>
				!tx.TransactionId.IsEmpty())?.TransactionId;
			if (!outputHash.IsEmpty())
				outputHash = outputHash.NormalizeTransactionHash();
			trade = new()
			{
				Id = outputHash.IsEmpty()
					? $"{inputHash}:{market.Asset}"
					: $"{inputHash}:{outputHash}:{market.Asset}",
				TransactionHash = inputHash,
				Time = action.Date.ParseActionTime(),
				Price = quote / volume,
				Volume = volume,
				QuoteVolume = quote,
				Side = side,
			};
			return true;
		}
		catch (Exception error) when (error is InvalidDataException or
			FormatException or OverflowException)
		{
			return false;
		}
	}

	private static bool TryFindCoin(
		IEnumerable<THORChainActionTransaction> transactions, string asset,
		out THORChainActionTransaction transaction,
		out THORChainCoinAmount coin)
	{
		foreach (var item in transactions ?? [])
		{
			if (item is null || item.IsAffiliate == true)
				continue;
			var found = (item.Coins ?? []).FirstOrDefault(candidate =>
				candidate?.Asset.EqualsIgnoreCase(asset) == true);
			if (found is null)
				continue;
			transaction = item;
			coin = found;
			return true;
		}
		transaction = null;
		coin = null;
		return false;
	}

	private async ValueTask<bool> SendTradeAsync(THORChainMarket market,
		THORChainTrade trade, long target,
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

	private async ValueTask<THORChainCandle[]> LoadCandlesAsync(
		THORChainMarket market, TimeSpan timeFrame, DateTime from, DateTime to,
		int maximum, CancellationToken cancellationToken)
	{
		var trades = await LoadTradesAsync(market, from, to, HistoryMaximum,
			cancellationToken);
		return [.. trades.GroupBy(trade =>
				THORChainExtensions.FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key)
			.Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time).ToArray();
				return new THORChainCandle
				{
					OpenTime = group.Key,
					Open = ordered[0].Price,
					High = ordered.Max(static trade => trade.Price),
					Low = ordered.Min(static trade => trade.Price),
					Close = ordered[^1].Price,
					Volume = ordered.Sum(static trade => trade.Volume),
					Turnover = ordered.Sum(static trade =>
						trade.QuoteVolume),
					TradeCount = ordered.Length,
				};
			}).TakeLast(maximum)];
	}

	private ValueTask SendCandleAsync(THORChainMarket market,
		THORChainCandle candle, TimeSpan timeFrame, long target,
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
		await RefreshPoolsAsync(cancellationToken);
		await PollLevel1Async(cancellationToken);
		await PollTicksAsync(cancellationToken);
		await PollCandlesAsync(cancellationToken);
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(THORChainMarket Market, long[] Targets)[] groups;
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
				{
					var fingerprint = new Level1Fingerprint(snapshot.Bid,
						snapshot.Ask, snapshot.Volume24Hours);
					var key = $"{target}:{group.Market.SecurityCode}";
					var isChanged = false;
					using (_sync.EnterScope())
					{
						isChanged = !_level1Fingerprints.TryGetValue(key,
							out var previous) || previous != fingerprint;
						if (isChanged)
							_level1Fingerprints[key] = fingerprint;
					}
					if (!isChanged)
						continue;
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
					if (snapshot.Volume24Hours > 0)
						message.TryAdd(Level1Fields.Volume,
							snapshot.Volume24Hours);
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
