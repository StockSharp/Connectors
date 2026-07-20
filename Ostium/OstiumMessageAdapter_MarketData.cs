namespace StockSharp.Ostium;

public partial class OstiumMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in GetMarkets().OrderBy(static item => item.Symbol,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Ostium))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.OrdinalIgnoreCase))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Future))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Ostium does not publish historical Level1 changes.");

		var market = GetMarket(mdMsg.SecurityId);
		var price = GetPrice(market);
		if (price is null)
		{
			var prices = await ApiClient.GetPricesAsync(cancellationToken);
			foreach (var item in prices?.Prices ?? [])
				if (item is not null)
					StorePrice(item);
			price = GetPrice(market) ?? throw new InvalidDataException(
				"Ostium returned no current price for " + market.Symbol + ".");
		}
		await SendLevel1Async(market, price, mdMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var isFirst = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, market.PairIndex);
			_priceReferences.TryGetValue(market.PairIndex, out var references);
			_priceReferences[market.PairIndex] = references + 1;
			isFirst = references == 0;
		}
		if (!isFirst)
			return;
		try
		{
			await SocketClient.SubscribeAsync(market.ApiPair, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				_priceReferences.Remove(market.PairIndex);
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToOstiumResolution();
		var to = (mdMsg.To ?? ServerTime).EnsureOstiumUtc();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.EnsureOstiumUtc() ??
			to - TimeSpan.FromTicks(checked(timeFrame.Ticks *
				Math.Max(1, count - 1)));
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Ostium candle start time cannot be later than end time.");
		var response = await ApiClient.GetCandlesAsync(market.ApiPair, from, to,
			timeFrame, count, cancellationToken);
		var candles = (response?.Data ?? [])
			.Where(static candle => candle is not null && candle.Time > 0)
			.Where(candle => candle.Time.FromUnix(false).EnsureOstiumUtc() >= from &&
				candle.Time.FromUnix(false).EnsureOstiumUtc() <= to)
			.OrderBy(static candle => candle.Time)
			.TakeLast(count)
			.ToArray();
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
				TimeFrame = timeFrame,
				LastOpenTime = candles.LastOrDefault()?.Time
					.FromUnix(false).EnsureOstiumUtc() ?? default,
				NextPollTime = DateTime.UtcNow +
					GetCandlePollInterval(timeFrame),
			});
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		var pairIndex = -1;
		var isLast = false;
		using (_sync.EnterScope())
		{
			if (!_level1Subscriptions.Remove(transactionId, out pairIndex))
				return;
			if (!_priceReferences.TryGetValue(pairIndex, out var references) ||
				references <= 1)
			{
				_priceReferences.Remove(pairIndex);
				isLast = true;
			}
			else
				_priceReferences[pairIndex] = references - 1;
		}
		if (isLast && GetMarket(pairIndex) is { } market)
			await SocketClient.UnsubscribeAsync(market.ApiPair,
				cancellationToken);
	}

	private async ValueTask OnPriceAsync(OstiumPrice price,
		CancellationToken cancellationToken)
	{
		price = StorePrice(price);
		if (price is null)
			return;
		var key = price.Pair.IsEmpty()
			? price.From.NormalizePairName() + "-" +
				price.To.NormalizePairName()
			: price.Pair.Trim().ToUpperInvariant();
		var market = GetMarketByPricePair(key);
		if (market is null || price.Mid <= 0)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value == market.PairIndex).Select(static pair => pair.Key)];
		foreach (var transactionId in subscriptions)
			await SendLevel1Async(market, price, transactionId,
				cancellationToken);
	}

	private ValueTask SendLevel1Async(OstiumMarket market, OstiumPrice price,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = price?.TimestampSeconds > 0
			? price.TimestampSeconds.FromUnix().EnsureOstiumUtc()
			: ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.PriceStep)
		.TryAdd(Level1Fields.VolumeStep, market.VolumeStep)
		.TryAdd(Level1Fields.State, price is not null && price.IsMarketOpen
				? SecurityStates.Trading
				: SecurityStates.Stoped)
		.TryAdd(Level1Fields.Index, price?.Mid)
		.TryAdd(Level1Fields.BestBidPrice, price?.Bid > 0 ? price.Bid : null)
		.TryAdd(Level1Fields.BestBidTime,
			price?.Bid > 0 ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, price?.Ask > 0 ? price.Ask : null)
		.TryAdd(Level1Fields.BestAskTime,
			price?.Ask > 0 ? time : null)
		.TryAdd(Level1Fields.OpenInterest,
			market.LongOpenInterest + market.ShortOpenInterest),
			cancellationToken);
	}

	private async ValueTask PollCandleAsync(CandleSubscription subscription,
		CancellationToken cancellationToken)
	{
		var to = ServerTime;
		var from = to - TimeSpan.FromTicks(subscription.TimeFrame.Ticks * 3);
		var response = await ApiClient.GetCandlesAsync(
			subscription.Market.ApiPair, from, to, subscription.TimeFrame,
			3, cancellationToken);
		var candles = (response?.Data ?? [])
			.Where(static candle => candle is not null && candle.Time > 0)
			.OrderBy(static candle => candle.Time)
			.ToArray();
		foreach (var candle in candles)
		{
			var openTime = candle.Time.FromUnix(false).EnsureOstiumUtc();
			if (subscription.LastOpenTime != default &&
				openTime < subscription.LastOpenTime)
				continue;
			await SendCandleAsync(subscription.Market, candle,
				subscription.TimeFrame, subscription.TransactionId,
				cancellationToken);
			if (openTime > subscription.LastOpenTime)
				subscription.LastOpenTime = openTime;
		}
	}

	private ValueTask SendCandleAsync(OstiumMarket market, OstiumCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		if (candle.Open <= 0 || candle.High <= 0 || candle.Low <= 0 ||
			candle.Close <= 0 || candle.High < candle.Low ||
			candle.High < candle.Open || candle.High < candle.Close ||
			candle.Low > candle.Open || candle.Low > candle.Close)
			throw new InvalidDataException(
				"Ostium returned an invalid OHLC candle.");
		var openTime = candle.Time.FromUnix(false).EnsureOstiumUtc();
		var closeTime = openTime + timeFrame;
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = 0m,
			OriginalTransactionId = transactionId,
			State = closeTime <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(OstiumMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Symbol + " Ostium perpetual",
			ShortName = market.Symbol,
			Class = market.Category,
			SecurityType = SecurityTypes.Future,
			Currency = market.QuoteAsset.ToOstiumCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = OstiumExtensions.MinimumCollateral,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(market.BaseAsset);
		return message;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from &&
			to > from.EnsureOstiumUtc())
			return ((to - from.EnsureOstiumUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit;
	}
}
