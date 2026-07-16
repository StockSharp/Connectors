namespace StockSharp.Bloomberg;

partial class BloombergMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var symbol = message.SecurityId.SecurityCode;
		if (symbol.IsEmpty())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var securityTypes = message.GetSecurityTypes();
		foreach (var info in await _client.LookupSecurityAsync(symbol, cancellationToken))
		{
			if (!info.Error.IsEmpty())
			{
				await SendOutErrorAsync(new InvalidOperationException(info.Error), cancellationToken);
				continue;
			}

			var boardCode = info.Exchange.ToBoardCode();
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = new()
				{
					SecurityCode = info.Symbol.IsEmpty(symbol),
					BoardCode = boardCode,
					Bloomberg = info.GlobalId.IsEmpty(info.Symbol.IsEmpty(symbol)),
				},
				Name = info.Name,
				SecurityType = info.SecurityType.ToSecurityType(info.MarketSector),
				Currency = info.Currency.IsEmpty() ? null : info.Currency.To<CurrencyTypes?>(),
				PriceStep = info.PriceStep,
				VolumeStep = info.LotSize,
				MinVolume = info.LotSize,
				Multiplier = info.Multiplier,
				ExpiryDate = info.ExpiryDate,
				Strike = info.Strike,
				OptionType = info.PutCall.ToOptionType(),
				UnderlyingSecurityId = info.Underlying.IsEmpty()
					? default
					: new SecurityId { SecurityCode = info.Underlying, BoardCode = boardCode, Bloomberg = info.Underlying },
			};

			if (security.IsMatch(message, securityTypes))
				await SendOutMessageAsync(security, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessLiveSubscriptionAsync(message, BloombergMarketDataKinds.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessLiveSubscriptionAsync(message, BloombergMarketDataKinds.Ticks, cancellationToken);

	private async ValueTask ProcessLiveSubscriptionAsync(
		MarketDataMessage message,
		BloombergMarketDataKinds kind,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_client.UnsubscribeMarketData(message.OriginalTransactionId);
			_marketSubscriptions.TryRemove(message.OriginalTransactionId, out _);
			return;
		}

		var symbol = message.SecurityId.SecurityCode.ThrowIfEmpty(nameof(message.SecurityId.SecurityCode));
		var subscription = new BloombergMarketSubscription
		{
			SecurityId = message.SecurityId,
			Kind = kind,
		};
		if (!_marketSubscriptions.TryAdd(message.TransactionId, subscription))
			throw new InvalidOperationException($"Bloomberg subscription {message.TransactionId} already exists.");

		try
		{
			_client.SubscribeMarketData(message.TransactionId, symbol);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.TryRemove(message.TransactionId, out _);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var timeFrame = message.GetTimeFrame();
		var (period, interval) = ToBarPeriod(timeFrame);
		var to = EnsureUtc(message.To ?? DateTime.UtcNow);
		var from = EnsureUtc(message.From ?? GetDefaultFrom(to, timeFrame, period, message.Count));
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message.From), from, LocalizedStrings.InvalidValue);

		IEnumerable<BloombergHistoricalBar> bars = (await _client.GetHistoricalBarsAsync(
			message.SecurityId.SecurityCode,
			from,
			to,
			period,
			interval,
			cancellationToken))
			.OrderBy(bar => bar.Time);

		if (message.Count is > 0 and <= int.MaxValue)
			bars = bars.TakeLast((int)message.Count.Value);

		foreach (var bar in bars)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = message.SecurityId,
				OpenTime = bar.Time,
				CloseTime = bar.Time + timeFrame,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume,
				TotalTicks = bar.Events,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessMarketDataAsync(BloombergMarketUpdate update, CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(update.SubscriptionId, out var subscription))
			return default;

		if (subscription.Kind == BloombergMarketDataKinds.Ticks)
		{
			if (update.LastPrice == null)
				return default;
			return SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = update.SubscriptionId,
				SecurityId = subscription.SecurityId,
				TradeId = update.Sequence,
				TradePrice = update.LastPrice.Value,
				TradeVolume = update.LastSize,
				ServerTime = update.ServerTime,
			}, cancellationToken);
		}

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = update.SubscriptionId,
			SecurityId = subscription.SecurityId,
			ServerTime = update.ServerTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, update.LastPrice)
		.TryAdd(Level1Fields.LastTradeVolume, update.LastSize)
		.TryAdd(Level1Fields.BestBidPrice, update.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, update.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, update.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, update.AskSize)
		.TryAdd(Level1Fields.OpenPrice, update.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, update.HighPrice)
		.TryAdd(Level1Fields.LowPrice, update.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, update.ClosePrice)
		.TryAdd(Level1Fields.Volume, update.Volume)
		.TryAdd(Level1Fields.OpenInterest, update.OpenInterest), cancellationToken);
	}

	private static (BloombergBarPeriods period, int interval) ToBarPeriod(TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(30))
			return (BloombergBarPeriods.Monthly, 1);
		if (timeFrame == TimeSpan.FromDays(7))
			return (BloombergBarPeriods.Weekly, 1);
		if (timeFrame == TimeSpan.FromDays(1))
			return (BloombergBarPeriods.Daily, 1);
		if (timeFrame.TotalMinutes is >= 1 and <= 1440 && timeFrame.TotalMinutes % 1 == 0)
			return (BloombergBarPeriods.Minute, (int)timeFrame.TotalMinutes);
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	private static DateTime GetDefaultFrom(DateTime to, TimeSpan timeFrame, BloombergBarPeriods period, long? count)
	{
		if (period != BloombergBarPeriods.Minute)
			return to.AddYears(-5);

		var bars = Math.Min(count.GetValueOrDefault(500), 10000);
		if (bars <= 0)
			bars = 500;
		var range = TimeSpan.FromTicks(timeFrame.Ticks * bars);
		return to - (range > TimeSpan.FromDays(140) ? TimeSpan.FromDays(140) : range);
	}

	private static DateTime EnsureUtc(DateTime time)
		=> time.Kind switch
		{
			DateTimeKind.Utc => time,
			DateTimeKind.Local => time.ToUniversalTime(),
			_ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
		};
}
