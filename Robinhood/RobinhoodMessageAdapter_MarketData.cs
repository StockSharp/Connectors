namespace StockSharp.Robinhood;

partial class RobinhoodMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var query = message.SecurityId.SecurityCode;
		if (query.IsEmpty())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var types = message.GetSecurityTypes();
		var left = message.Count ?? long.MaxValue;
		foreach (var result in await _client.Search(query, cancellationToken) ?? [])
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = result.Symbol.ToSecurityId(),
				Name = result.Name.IsEmpty(result.SimpleName),
				SecurityType = SecurityTypes.Stock,
			};

			if (!security.IsMatch(message, types))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.IsSubscribe)
		{
			_level1Subscriptions[message.TransactionId] = message.SecurityId;
			await SendSubscriptionResultAsync(message, cancellationToken);
			var quotes = await _client.GetQuotes([message.SecurityId.SecurityCode], cancellationToken);
			if (quotes?.FirstOrDefault() is { } quote)
				await ProcessQuote(quote, [new(message.TransactionId, message.SecurityId)], cancellationToken);
		}
		else
			_level1Subscriptions.Remove(message.OriginalTransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var timeFrame = message.GetTimeFrame();
		var request = new RobinhoodHistoricalRequest
		{
			Symbols = [message.SecurityId.SecurityCode],
			StartTime = GetStartTime(message.From, message.To, message.Count, timeFrame),
			EndTime = message.To?.ToUniversalTime(),
			Interval = ToInterval(timeFrame),
			Bounds = message.IsRegularTradingHours == true ? RobinhoodHistoricalBounds.Regular : RobinhoodHistoricalBounds.Extended,
		};

		foreach (var result in await _client.GetHistoricals(request, cancellationToken) ?? [])
		{
			foreach (var bar in result.Bars ?? [])
			{
				if (bar.IsInterpolated)
					continue;
				var openTime = bar.BeginsAt.ToUtcDateTime(DateTime.UtcNow);
				if (message.From is DateTime from && openTime < from.ToUniversalTime())
					continue;
				if (message.To is DateTime to && openTime > to.ToUniversalTime())
					continue;

				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = message.SecurityId,
					OpenTime = openTime,
					CloseTime = openTime + timeFrame,
					OpenPrice = bar.OpenPrice,
					HighPrice = bar.HighPrice,
					LowPrice = bar.LowPrice,
					ClosePrice = bar.ClosePrice,
					TotalVolume = bar.Volume,
					State = CandleStates.Finished,
				}, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask ProcessQuote(RobinhoodQuoteResult result, KeyValuePair<long, SecurityId>[] subscriptions, CancellationToken cancellationToken)
	{
		var quote = result?.Quote;
		if (quote?.Symbol.IsEmpty() != false)
			return;
		var lastTradeTime = quote.LastTradeTime.ToUtcDateTime(DateTime.MinValue);
		var lastNonRegularTradeTime = quote.LastNonRegularTradeTime.ToUtcDateTime(DateTime.MinValue);
		var lastPrice = lastNonRegularTradeTime > lastTradeTime ? quote.LastNonRegularTradePrice : quote.LastTradePrice;
		var serverTime = new[]
		{
			lastTradeTime,
			lastNonRegularTradeTime,
			quote.BidTime.ToUtcDateTime(DateTime.MinValue),
			quote.AskTime.ToUtcDateTime(DateTime.MinValue),
		}.Max();
		if (serverTime == DateTime.MinValue)
			serverTime = DateTime.UtcNow;

		foreach (var subscription in subscriptions.Where(p => p.Value.SecurityCode.EqualsIgnoreCase(quote.Symbol)))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.Key,
				SecurityId = subscription.Value,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, lastPrice)
			.TryAdd(Level1Fields.ClosePrice, result.Close?.Price ?? quote.AdjustedPreviousClose ?? quote.PreviousClose)
			.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskSize), cancellationToken);
		}
	}

	private static RobinhoodHistoricalInterval ToInterval(TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromSeconds(15))
			return RobinhoodHistoricalInterval.FifteenSeconds;
		if (timeFrame == TimeSpan.FromSeconds(30))
			return RobinhoodHistoricalInterval.ThirtySeconds;
		if (timeFrame == TimeSpan.FromMinutes(1))
			return RobinhoodHistoricalInterval.Minute;
		if (timeFrame == TimeSpan.FromMinutes(5))
			return RobinhoodHistoricalInterval.FiveMinutes;
		if (timeFrame == TimeSpan.FromMinutes(10))
			return RobinhoodHistoricalInterval.TenMinutes;
		if (timeFrame == TimeSpan.FromMinutes(30))
			return RobinhoodHistoricalInterval.ThirtyMinutes;
		if (timeFrame == TimeSpan.FromHours(1))
			return RobinhoodHistoricalInterval.Hour;
		if (timeFrame == TimeSpan.FromHours(4))
			return RobinhoodHistoricalInterval.FourHours;
		if (timeFrame == TimeSpan.FromDays(1))
			return RobinhoodHistoricalInterval.Day;
		if (timeFrame == TimeSpan.FromDays(7))
			return RobinhoodHistoricalInterval.Week;
		if (timeFrame == TimeSpan.FromDays(30))
			return RobinhoodHistoricalInterval.Month;
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	private static DateTime GetStartTime(DateTime? from, DateTime? to, long? count, TimeSpan timeFrame)
	{
		if (from is DateTime start)
			return start.ToUniversalTime();
		var end = to?.ToUniversalTime() ?? DateTime.UtcNow;
		if (count is > 0 && count <= TimeSpan.MaxValue.Ticks / timeFrame.Ticks)
			return end - TimeSpan.FromTicks(timeFrame.Ticks * count.Value);
		return end - TimeSpan.FromDays(1);
	}
}
