namespace StockSharp.Fxcm;

public partial class FxcmMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var offer in await GetRest().GetOffers(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (offer.Symbol.IsEmpty() || offer.OfferId <= 0)
				continue;

			CacheOffer(offer);
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = offer.Symbol.ToSecurityId(offer.OfferId),
				SecurityType = offer.InstrumentType.ToSecurityType(),
				Name = offer.Symbol,
				ShortName = offer.Symbol,
				Decimals = offer.RatePrecision,
				PriceStep = offer.Pip,
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old) &&
				!_marketSubscriptions.CachedValues.Any(s => s.Symbol.EqualsIgnoreCase(old.Symbol)))
				await GetRest().UnsubscribePair(old.Symbol, cancellationToken);
			return;
		}

		var offer = await ResolveOffer(mdMsg.SecurityId, cancellationToken);
		await SendOffer(mdMsg.TransactionId, mdMsg.SecurityId, offer, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var isFirst = !_marketSubscriptions.CachedValues.Any(s =>
			s.Symbol.EqualsIgnoreCase(offer.Symbol));
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Symbol = offer.Symbol,
		};
		try
		{
			if (isFirst)
				await GetRest().SubscribePair(offer.Symbol, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var offer = await ResolveOffer(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		var count = (int)Math.Clamp(mdMsg.Count ?? 10000, 1, 10000);
		var candles = await GetRest().GetCandles(offer.OfferId, timeFrame.ToNative(), count,
			mdMsg.From, mdMsg.To, cancellationToken);
		var left = mdMsg.Count ?? long.MaxValue;
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();

		foreach (var candle in candles.OrderBy(c => c.Timestamp))
		{
			var openTime = DateTimeOffset.FromUnixTimeSeconds(candle.Timestamp).UtcDateTime;
			if (from != null && openTime < from.Value)
				continue;
			if (to != null && openTime > to.Value)
				break;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = (candle.BidOpen + candle.AskOpen) / 2,
				HighPrice = (candle.BidHigh + candle.AskHigh) / 2,
				LowPrice = (candle.BidLow + candle.AskLow) / 2,
				ClosePrice = (candle.BidClose + candle.AskClose) / 2,
				TotalVolume = candle.TickQuantity,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<FxcmOffer> ResolveOffer(SecurityId securityId, CancellationToken cancellationToken)
	{
		var nativeId = securityId.Native?.To<long?>();
		if (nativeId is > 0 && _offersById.TryGetValue(nativeId.Value, out var offer))
			return offer;
		if (!securityId.SecurityCode.IsEmpty() && _offers.TryGetValue(securityId.SecurityCode, out offer))
			return offer;

		foreach (var item in await GetRest().GetOffers(cancellationToken))
			CacheOffer(item);

		if (nativeId is > 0 && _offersById.TryGetValue(nativeId.Value, out offer))
			return offer;
		if (!securityId.SecurityCode.IsEmpty() && _offers.TryGetValue(securityId.SecurityCode, out offer))
			return offer;
		throw new InvalidOperationException($"FXCM instrument '{securityId}' was not found. Run security lookup first.");
	}

	private ValueTask ProcessPrice(FxcmPriceUpdate price, CancellationToken cancellationToken)
	{
		if (price?.Symbol.IsEmpty() != false || price.Rates is not { Length: >= 2 })
			return default;

		return ProcessPriceSubscriptions(price, cancellationToken);
	}

	private async ValueTask ProcessPriceSubscriptions(FxcmPriceUpdate price,
		CancellationToken cancellationToken)
	{
		var time = price.Updated > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(price.Updated).UtcDateTime
			: DateTime.UtcNow;
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s =>
			s.Symbol.EqualsIgnoreCase(price.Symbol)))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.BestBidPrice, price.Rates.ElementAtOrDefault(0))
			.TryAdd(Level1Fields.BestAskPrice, price.Rates.ElementAtOrDefault(1))
			.TryAdd(Level1Fields.HighPrice, price.Rates.ElementAtOrDefault(2))
			.TryAdd(Level1Fields.LowPrice, price.Rates.ElementAtOrDefault(3)), cancellationToken);
		}
	}

	private ValueTask SendOffer(long originalTransactionId, SecurityId securityId, FxcmOffer offer,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = offer.Time?.UtcKind() ?? DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.BestBidPrice, offer.Bid)
		.TryAdd(Level1Fields.BestAskPrice, offer.Ask)
		.TryAdd(Level1Fields.HighPrice, offer.High)
		.TryAdd(Level1Fields.LowPrice, offer.Low)
		.TryAdd(Level1Fields.State, offer.IsBuyTradable || offer.IsSellTradable
			? SecurityStates.Trading : SecurityStates.Stoped), cancellationToken);
}
