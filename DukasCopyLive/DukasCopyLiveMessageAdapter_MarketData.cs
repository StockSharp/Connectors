namespace StockSharp.DukasCopyLive;

public partial class DukasCopyLiveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await GetClient().GetInstruments(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (instrument.Symbol.IsEmpty())
				continue;

			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.Symbol.ToSecurityId(),
				SecurityType = instrument.Type.ToSecurityType(),
				Name = instrument.Name.IsEmpty(instrument.Symbol),
				ShortName = instrument.Symbol,
				Currency = instrument.SecondaryCurrency.ToCurrency(),
				Decimals = instrument.TickScale > 0 ? instrument.TickScale : instrument.PipScale + 1,
				PriceStep = instrument.PipValue > 0 ? instrument.PipValue / 10 : null,
				VolumeStep = 0.001m,
				Multiplier = 1_000_000m,
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
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessTickSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessTickSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessTickSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessTickSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveMarketSubscription(mdMsg, cancellationToken);
			return;
		}

		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
			var count = (int)Math.Clamp(mdMsg.Count ?? 10000, 1, 100000);
			var from = (mdMsg.From ?? to - TimeSpan.FromDays(1)).ToUniversalTime();
			foreach (var tick in await GetClient().GetTicks(mdMsg.SecurityId.SecurityCode.NormalizeDukasSymbol(),
				from, to, count, cancellationToken))
				await SendTick(mdMsg.TransactionId, mdMsg.SecurityId, dataType, tick, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddMarketSubscription(mdMsg, dataType, null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveMarketSubscription(mdMsg, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
			var count = (int)Math.Clamp(mdMsg.Count ?? 10000, 1, 100000);
			var from = (mdMsg.From ?? to - TimeSpan.FromTicks(timeFrame.Ticks * count * 2L)).ToUniversalTime();
			foreach (var bar in await GetClient().GetBars(mdMsg.SecurityId.SecurityCode.NormalizeDukasSymbol(),
				timeFrame.ToNative(), from, to, count, cancellationToken))
				await SendBar(mdMsg.TransactionId, mdMsg.SecurityId, timeFrame, mdMsg.DataType2, bar,
					cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddMarketSubscription(mdMsg, mdMsg.DataType2, timeFrame, cancellationToken);
	}

	private async ValueTask ProcessTick(DukasCopyLiveTick tick, CancellationToken cancellationToken)
	{
		if (tick?.Symbol.IsEmpty() != false)
			return;

		var symbol = tick.Symbol.NormalizeDukasSymbol();
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s =>
			s.SecurityId.SecurityCode.NormalizeDukasSymbol().EqualsIgnoreCase(symbol) &&
			(s.DataType == DataType.Ticks || s.DataType == DataType.Level1 ||
			 s.DataType == DataType.MarketDepth)))
		{
			await SendTick(subscription.TransactionId, subscription.SecurityId,
				subscription.DataType, tick, cancellationToken);
		}
	}

	private async ValueTask ProcessBar(DukasCopyLiveBar bar, CancellationToken cancellationToken)
	{
		if (bar?.Symbol.IsEmpty() != false)
			return;

		var timeFrame = bar.Period.ToTimeFrame();
		if (timeFrame == null)
			return;
		var symbol = bar.Symbol.NormalizeDukasSymbol();
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s =>
			s.SecurityId.SecurityCode.NormalizeDukasSymbol().EqualsIgnoreCase(symbol) &&
			s.TimeFrame == timeFrame))
		{
			await SendBar(subscription.TransactionId, subscription.SecurityId, timeFrame.Value,
				subscription.DataType, bar, cancellationToken);
		}
	}

	private ValueTask SendTick(long originalTransactionId, SecurityId securityId, DataType dataType,
		DukasCopyLiveTick tick, CancellationToken cancellationToken)
	{
		var time = tick.Time.ToUtc();
		if (dataType == DataType.Level1)
		{
			return SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				SecurityId = securityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.BestBidPrice, tick.Bid > 0 ? tick.Bid : null)
			.TryAdd(Level1Fields.BestBidVolume, tick.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, tick.Ask > 0 ? tick.Ask : null)
			.TryAdd(Level1Fields.BestAskVolume, tick.AskVolume)
			.TryAdd(Level1Fields.BidsVolume, tick.TotalBidVolume)
			.TryAdd(Level1Fields.AsksVolume, tick.TotalAskVolume), cancellationToken);
		}

		if (dataType == DataType.MarketDepth)
		{
			return SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				SecurityId = securityId,
				ServerTime = time,
				Bids = CreateDepth(tick.BidPrices, tick.BidVolumes, tick.Bid, tick.BidVolume),
				Asks = CreateDepth(tick.AskPrices, tick.AskVolumes, tick.Ask, tick.AskVolume),
			}, cancellationToken);
		}

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			TradeStringId = $"{tick.Symbol}:{tick.Time}:{tick.Bid}:{tick.Ask}",
			TradePrice = DukasCopyLiveExtensions.Mid(tick.Bid, tick.Ask),
			TradeVolume = tick.BidVolume + tick.AskVolume,
			ServerTime = time,
		}, cancellationToken);
	}

	private static QuoteChange[] CreateDepth(decimal[] prices, decimal[] volumes,
		decimal fallbackPrice, decimal fallbackVolume)
	{
		if (prices is { Length: > 0 })
		{
			return [.. prices.Select((price, index) => new QuoteChange(price,
				volumes?.ElementAtOrDefault(index) ?? 0)).Where(quote => quote.Price > 0)];
		}

		return fallbackPrice > 0 ? [new(fallbackPrice, fallbackVolume)] : [];
	}

	private ValueTask SendBar(long originalTransactionId, SecurityId securityId, TimeSpan timeFrame,
		DataType dataType, DukasCopyLiveBar bar, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			DataType = dataType,
			OpenTime = bar.Time.ToUtc(),
			CloseTime = bar.Time.ToUtc() + timeFrame,
			OpenPrice = DukasCopyLiveExtensions.Mid(bar.BidOpen, bar.AskOpen),
			HighPrice = DukasCopyLiveExtensions.Mid(bar.BidHigh, bar.AskHigh),
			LowPrice = DukasCopyLiveExtensions.Mid(bar.BidLow, bar.AskLow),
			ClosePrice = DukasCopyLiveExtensions.Mid(bar.BidClose, bar.AskClose),
			TotalVolume = bar.BidVolume + bar.AskVolume,
			State = CandleStates.Finished,
		}, cancellationToken);
}
