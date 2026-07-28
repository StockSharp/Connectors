namespace StockSharp.Pendle;

public partial class PendleMessageAdapter
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
		PendleSecurity[] securities;
		using (_sync.EnterScope())
			securities = [.. _securities.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var item in securities.OrderBy(static security =>
			security.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Pendle))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(item.SecurityCode) &&
				!requestedCode.EqualsIgnoreCase(item.Token.Address) &&
				!requestedCode.EqualsIgnoreCase(item.Market.Address))
				continue;
			var security = CreateSecurity(item, lookupMsg.TransactionId);
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
				"Pendle API does not expose historical Level1 events.");
		var security = GetSecurity(mdMsg.SecurityId);
		await SendLevel1Async(security, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Security = security,
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
		var security = GetSecurity(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Pendle does not support candle interval '{timeFrame}'.");
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime() ?? DateTime.UtcNow;
		if (from is DateTime begin && begin > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Pendle candle start time cannot be later than end time.");
		var maximum = GetSubscriptionMaximum(mdMsg.Count);
		var requestMaximum = Math.Min(maximum, HistoryLimit);
		var requestFrom = from ?? SubtractIntervals(to, timeFrame,
			requestMaximum);
		if ((to - requestFrom).Ticks / timeFrame.Ticks + 1 > HistoryLimit)
			requestFrom = SubtractIntervals(to, timeFrame, HistoryLimit);
		var points = await HttpClient.GetHistoryAsync(
			security.Market.Address, timeFrame, requestFrom, to,
			cancellationToken);
		var delivered = 0;
		foreach (var candle in ConvertHistory(security, points))
		{
			if (from is DateTime start && candle.OpenTime < start ||
				candle.OpenTime > to)
				continue;
			var state = candle.OpenTime + timeFrame <= DateTime.UtcNow
				? CandleStates.Finished
				: CandleStates.Active;
			if (await SendCandleAsync(security, candle, timeFrame,
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
				Security = security,
				TimeFrame = timeFrame,
				To = mdMsg.To?.ToUniversalTime(),
				Maximum = maximum,
				Delivered = delivered,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(PendleSecurity security,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = security.ToStockSharp(),
			Name = security.Token.Name,
			ShortName = security.SecurityCode,
			Class = security.Kind == PendleAssetKinds.Principal
				? "PENDLE-PT"
				: "PENDLE-YT",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = security.Market.UnderlyingToken.Symbol.ToCurrency(),
			PriceStep = DecimalStep(
				security.Market.UnderlyingToken.Decimals),
			VolumeStep = DecimalStep(security.Token.Decimals),
			ExpiryDate = security.Market.Expiry,
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(security.Market.UnderlyingToken.Symbol);

	private async ValueTask SendLevel1Async(PendleSecurity security,
		long target, bool isForced, CancellationToken cancellationToken)
		=> await SendLevel1Async(security,
			await GetLevel1Async(security, cancellationToken), target,
			isForced, cancellationToken);

	private async ValueTask SendLevel1Async(PendleSecurity security,
		PendleLevel1 snapshot, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var fingerprint = new Level1Fingerprint(snapshot.Bid, snapshot.Ask,
			snapshot.ImpliedApy);
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(target,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[target] = fingerprint;
		}
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = security.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ProbeVolume)
		.TryAdd(Level1Fields.LastTradePrice,
			(snapshot.Bid + snapshot.Ask) / 2m)
		.TryAdd(Level1Fields.Yield, snapshot.ImpliedApy * 100m)
		.TryAdd(Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private static PendleCandle[] ConvertHistory(PendleSecurity security,
		IEnumerable<PendleHistoricalPoint> source)
	{
		ArgumentNullException.ThrowIfNull(security);
		ArgumentNullException.ThrowIfNull(source);
		var result = new List<PendleCandle>();
		foreach (var point in source)
		{
			if (point is null || !DateTime.TryParse(point.Timestamp,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal, out var time) ||
				point.StandardizedYieldPrice is not > 0)
				continue;
			var assetPrice = security.Kind == PendleAssetKinds.Principal
				? point.PrincipalPrice
				: point.YieldPrice;
			if (assetPrice is not > 0)
				continue;
			var price = assetPrice.Value /
				point.StandardizedYieldPrice.Value;
			if (price <= 0)
				continue;
			var volume = point.TradingVolume is > 0
				? point.TradingVolume.Value / assetPrice.Value
				: 0m;
			result.Add(new()
			{
				OpenTime = time,
				Price = price,
				Volume = volume,
				ImpliedApy = point.ImpliedApy ?? 0m,
			});
		}
		return [.. result.OrderBy(static candle => candle.OpenTime)];
	}

	private async ValueTask<bool> SendCandleAsync(PendleSecurity security,
		PendleCandle candle, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
	{
		var identity = "C:" + candle.OpenTime.Ticks.ToString(
			CultureInfo.InvariantCulture) + ":" +
			candle.Price.ToString(CultureInfo.InvariantCulture) + ":" +
			candle.Volume.ToString(CultureInfo.InvariantCulture) + ":" +
			candle.ImpliedApy.ToString(CultureInfo.InvariantCulture) + ":" +
			state;
		if (!TryTrackDelivery(target, identity))
			return false;
		await SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = security.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.Price,
			HighPrice = candle.Price,
			LowPrice = candle.Price,
			ClosePrice = candle.Price,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = target,
			State = state,
		}, cancellationToken);
		return true;
	}

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, Level1Subscription>[] level1;
		KeyValuePair<long, CandleSubscription>[] candles;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions];
			candles = [.. _candleSubscriptions];
		}
		foreach (var group in level1.GroupBy(item =>
			item.Value.Security.Market.Address,
			StringComparer.OrdinalIgnoreCase))
		{
			await PollOneAsync(async token =>
			{
				var response = await HttpClient.GetPricesAsync(
					group.First().Value.Security.Market.Address, token);
				foreach (var item in group)
					await SendLevel1Async(item.Value.Security,
						ValidatePrices(item.Value.Security, response),
						item.Key, false, token);
			}, cancellationToken);
		}
		foreach (var item in candles)
			await PollOneAsync(token => PollCandlesAsync(item.Key, item.Value,
				token), cancellationToken);
	}

	private async ValueTask PollCandlesAsync(long target,
		CandleSubscription subscription,
		CancellationToken cancellationToken)
	{
		var to = subscription.To ?? DateTime.UtcNow;
		var from = SubtractIntervals(to, subscription.TimeFrame, 2);
		var points = await HttpClient.GetHistoryAsync(
			subscription.Security.Market.Address, subscription.TimeFrame,
			from, to, cancellationToken);
		var finished = false;
		foreach (var candle in ConvertHistory(subscription.Security, points))
		{
			var state = candle.OpenTime + subscription.TimeFrame <=
				DateTime.UtcNow
					? CandleStates.Finished
					: CandleStates.Active;
			var sent = await SendCandleAsync(subscription.Security, candle,
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
		_candleSubscriptions.Remove(target);
		_level1Fingerprints.Remove(target);
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

	private static DateTime SubtractIntervals(DateTime to,
		TimeSpan timeFrame, int count)
	{
		var ticks = checked(timeFrame.Ticks * (long)Math.Max(1, count));
		return to.Ticks > ticks
			? new DateTime(to.Ticks - ticks, DateTimeKind.Utc)
			: DateTime.UnixEpoch;
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

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
