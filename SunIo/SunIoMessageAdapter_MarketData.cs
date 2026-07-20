namespace StockSharp.SunIo;

public partial class SunIoMessageAdapter
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
		SunIoMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.SunIo))
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
				"SUN.io executable routes do not expose historical Level1 events.");
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
				$"SUN.io does not support the {timeFrame} candle interval.");
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

	private SecurityMessage CreateSecurity(SunIoMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"TRX/{market.Token.Symbol} ({market.Token.Address})",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Token.Symbol.ToCurrency(),
			PriceStep = DecimalStep(8),
			VolumeStep = DecimalStep(6),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId("TRX");

	private async ValueTask SendLevel1Async(SunIoMarket market, long target,
		bool isForced, CancellationToken cancellationToken)
	{
		var snapshot = await LoadLevel1Async(market, cancellationToken);
		var fingerprint = new Level1Fingerprint(snapshot.Bid, snapshot.Ask,
			snapshot.BidVolume, snapshot.AskVolume);
		var key = $"{target}:{market.SecurityCode}";
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[key] = fingerprint;
		}
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestBidVolume, snapshot.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
		.TryAdd(Level1Fields.BestAskVolume, snapshot.AskVolume),
			cancellationToken);
	}

	private async ValueTask<(decimal Bid, decimal Ask, decimal BidVolume,
		decimal AskVolume)> LoadLevel1Async(SunIoMarket market,
		CancellationToken cancellationToken)
	{
		SunIoToken token;
		using (_sync.EnterScope())
			token = market.Token;
		var trxRaw = ProbeVolume.ToRawAmount(6);
		var bidRoute = SelectBestRoute(await ApiClient.GetRoutesAsync(
			SunIoExtensions.NativeTrxAddress, token.Address, trxRaw,
			cancellationToken), SunIoExtensions.NativeTrxAddress,
			token.Address, trxRaw);
		var tokenRaw = bidRoute.RawOutputAmount.ParseInteger(
			"route output amount");
		var tokenVolume = bidRoute.RawOutputAmount.FromRawAmount(
			token.Decimals, "route output amount");
		var askRoute = SelectBestRoute(await ApiClient.GetRoutesAsync(
			token.Address, SunIoExtensions.NativeTrxAddress, tokenRaw,
			cancellationToken), token.Address,
			SunIoExtensions.NativeTrxAddress, tokenRaw);
		var askTrxVolume = askRoute.RawOutputAmount.FromRawAmount(6,
			"route TRX output amount");
		var bid = tokenVolume / ProbeVolume;
		var ask = tokenVolume / askTrxVolume;
		if (bid <= 0 || ask <= 0 || askTrxVolume <= 0)
			throw new InvalidDataException(
				"SUN.io returned a non-positive executable quote.");
		return (bid, ask, ProbeVolume, askTrxVolume);
	}

	private static SunIoRoute SelectBestRoute(IEnumerable<SunIoRoute> source,
		string fromToken, string toToken, BigInteger amount)
	{
		var valid = new List<(SunIoRoute Route, BigInteger Output)>();
		foreach (var route in source ?? [])
		{
			try
			{
				ValidateRoute(route, fromToken, toToken, amount);
				valid.Add((route, route.RawOutputAmount.ParseInteger(
					"route output amount")));
			}
			catch (Exception error) when (error is InvalidDataException or
				FormatException or OverflowException)
			{
			}
		}
		if (valid.Count == 0)
			throw new InvalidDataException(
				"SUN.io returned no executable Smart Router path.");
		return valid.OrderByDescending(static item => item.Output).First().Route;
	}

	private static void ValidateRoute(SunIoRoute route, string fromToken,
		string toToken, BigInteger amount)
	{
		ArgumentNullException.ThrowIfNull(route);
		if (route.IsUnverifiedHookPresent ||
			(route.PoolKeys ?? []).Any(static key => key is not null))
			throw new InvalidDataException(
				"SUN.io route contains a V4 hook unsupported by Smart Router.");
		var tokens = route.Tokens ?? [];
		var versions = route.PoolVersions ?? [];
		if (tokens.Length < 2 || versions.Length != tokens.Length - 1 ||
			(route.PoolFees?.Length ?? 0) < versions.Length ||
			!SunIoSigner.AreSameAddresses(tokens[0], fromToken) ||
			!SunIoSigner.AreSameAddresses(tokens[^1], toToken) ||
			route.RawInputAmount.ParseInteger("route input amount") != amount ||
			route.RawOutputAmount.ParseInteger("route output amount") <= 0)
			throw new InvalidDataException(
				"SUN.io returned an inconsistent routing path.");
		foreach (var token in tokens)
			_ = token.NormalizeTronAddress();
	}

	private async ValueTask<SunIoTrade[]> LoadTradesAsync(SunIoMarket market,
		DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		maximum = maximum.Min(HistoryMaximum).Max(1);
		var transactions = new List<SunIoRouterTransaction>();
		string offset = null;
		for (var page = 0; transactions.Count < HistoryMaximum; page++)
		{
			var response = await ApiClient.GetRouterTransactionsAsync(
				market.Token.Address, null, from, to, 100, offset,
				cancellationToken);
			var items = response.Items ?? [];
			transactions.AddRange(items.Where(static item => item is not null));
			if (items.Length == 0 || response.Meta?.IsMoreAvailable != true)
				break;
			var next = items[^1].Offset;
			if (next.IsEmpty() || next.Equals(offset,
				StringComparison.Ordinal))
				break;
			offset = next;
			if (page >= HistoryMaximum / 100)
				break;
		}
		var result = new List<SunIoTrade>();
		foreach (var transaction in transactions)
			if (TryCreateTrade(market, transaction, out var trade) &&
				trade.Time >= from && trade.Time <= to)
				result.Add(trade);
		return [.. result.GroupBy(static trade => trade.Id,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(trade =>
				trade.Time).First())
			.OrderBy(static trade => trade.Time)
			.ThenBy(static trade => trade.Id, StringComparer.Ordinal)
			.TakeLast(maximum)];
	}

	private static bool TryCreateTrade(SunIoMarket market,
		SunIoRouterTransaction transaction, out SunIoTrade trade)
	{
		trade = null;
		try
		{
			if (transaction is null)
				return false;
			decimal volume;
			decimal quote;
			Sides side;
			if (SunIoSigner.AreSameAddresses(transaction.FromTokenAddress,
				SunIoExtensions.NativeTrxAddress) &&
				SunIoSigner.AreSameAddresses(transaction.ToTokenAddress,
					market.Token.Address))
			{
				volume = transaction.FromTokenAmount.ParseDecimal(
					"swap TRX amount");
				quote = transaction.ToTokenAmount.ParseDecimal(
					"swap token amount");
				side = Sides.Sell;
			}
			else if (SunIoSigner.AreSameAddresses(transaction.FromTokenAddress,
				market.Token.Address) &&
				SunIoSigner.AreSameAddresses(transaction.ToTokenAddress,
					SunIoExtensions.NativeTrxAddress))
			{
				volume = transaction.ToTokenAmount.ParseDecimal(
					"swap TRX amount");
				quote = transaction.FromTokenAmount.ParseDecimal(
					"swap token amount");
				side = Sides.Buy;
			}
			else
			{
				return false;
			}
			if (volume <= 0 || quote <= 0)
				return false;
			var hash = transaction.TransactionId.NormalizeTransactionHash();
			trade = new()
			{
				Id = hash,
				TransactionHash = hash,
				Time = transaction.SwapTime.ParseApiTime(),
				Price = quote / volume,
				Volume = volume,
				QuoteVolume = quote,
				Side = side,
				UserAddress = transaction.UserAddress,
			};
			return true;
		}
		catch (Exception error) when (error is InvalidDataException or
			FormatException or OverflowException)
		{
			return false;
		}
	}

	private async ValueTask<bool> SendTradeAsync(SunIoMarket market,
		SunIoTrade trade, long target, CancellationToken cancellationToken)
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

	private async ValueTask<SunIoCandle[]> LoadCandlesAsync(
		SunIoMarket market, TimeSpan timeFrame, DateTime from, DateTime to,
		int maximum, CancellationToken cancellationToken)
	{
		var trades = await LoadTradesAsync(market, from, to, HistoryMaximum,
			cancellationToken);
		return [.. trades.GroupBy(trade =>
				SunIoExtensions.FloorTime(trade.Time, timeFrame))
			.OrderBy(static group => group.Key)
			.Select(group =>
			{
				var ordered = group.OrderBy(static trade => trade.Time).ToArray();
				return new SunIoCandle
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

	private ValueTask SendCandleAsync(SunIoMarket market, SunIoCandle candle,
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

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		await RefreshTokensAsync(cancellationToken);
		await PollLevel1Async(cancellationToken);
		await PollTicksAsync(cancellationToken);
		await PollCandlesAsync(cancellationToken);
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(SunIoMarket Market, long[] Targets)[] groups;
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
						snapshot.Ask, snapshot.BidVolume,
						snapshot.AskVolume);
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
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						SecurityId = group.Market.ToStockSharp(),
						ServerTime = CurrentTime,
						OriginalTransactionId = target,
					}
					.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
					.TryAdd(Level1Fields.BestBidVolume, snapshot.BidVolume)
					.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
					.TryAdd(Level1Fields.BestAskVolume, snapshot.AskVolume),
						cancellationToken);
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
