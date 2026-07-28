namespace StockSharp.Settrade;

public partial class SettradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var symbol = lookupMsg.SecurityId.SecurityCode?.Trim();
		if (!symbol.IsEmpty())
		{
			var quote = await RestClient.GetQuoteAsync(symbol,
				cancellationToken);
			var level1 = quote.ToLevel1();
			await SendOutMessageAsync(new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = ToSecurityId(
					level1.Symbol.IsEmpty() ? symbol : level1.Symbol),
				Name = symbol,
				ShortName = symbol,
				Class = AccountType == SettradeAccountTypes.Equity
					? "SET"
					: "TFEX",
				SecurityType = SecurityType,
				Currency = CurrencyTypes.THB,
				PriceStep = quote.Value<decimal?>("priceStep") ?? 0.01m,
				VolumeStep = 1,
			}, cancellationToken);
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
			SecurityId securityId;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId, out securityId))
					return;
			}
			await UnsubscribeTopicAsync(InfoTopic(
				securityId.SecurityCode), cancellationToken);
			await UnsubscribeTopicAsync(BookTopic(
				securityId.SecurityCode), cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		var quote = await RestClient.GetQuoteAsync(
			security.SecurityCode, cancellationToken);
		await SendLevel1Async(quote.ToLevel1(),
			quote.ToOrderBook(), security, mdMsg.TransactionId,
			cancellationToken);
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = security;
		try
		{
			await SubscribeTopicAsync(InfoTopic(security.SecurityCode),
				cancellationToken);
			await SubscribeTopicAsync(BookTopic(security.SecurityCode),
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			await UnsubscribeTopicAsync(InfoTopic(
				security.SecurityCode), CancellationToken.None);
			await UnsubscribeTopicAsync(BookTopic(
				security.SecurityCode), CancellationToken.None);
			throw;
		}
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
			SecurityId securityId;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out securityId))
					return;
			}
			await UnsubscribeTopicAsync(BookTopic(
				securityId.SecurityCode), cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		var quote = await RestClient.GetQuoteAsync(
			security.SecurityCode, cancellationToken);
		await SendOrderBookAsync(quote.ToOrderBook(), security,
			mdMsg.TransactionId, cancellationToken);
		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = security;
		try
		{
			await SubscribeTopicAsync(BookTopic(security.SecurityCode),
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
			CandleSubscription removedSubscription;
			using (_sync.EnterScope())
			{
				if (!_candleSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out removedSubscription))
					return;
			}
			await UnsubscribeTopicAsync(CandleTopic(
				removedSubscription.SecurityId.SecurityCode,
				removedSubscription.TimeFrame), cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToSettradeInterval();
		var maximum = mdMsg.Count is > 0
			? checked((int)Math.Min(mdMsg.Count.Value, int.MaxValue))
			: 500;
		var candles = await RestClient.GetCandlesAsync(
			security.SecurityCode, interval, maximum,
			mdMsg.From, mdMsg.To, cancellationToken);
		foreach (var candle in candles.ToCandles().Take(maximum))
			await SendCandleAsync(candle, security, timeFrame,
				mdMsg.TransactionId, candle.Time + timeFrame <= CurrentTime
					? CandleStates.Finished
					: CandleStates.Active, cancellationToken);
		if (mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime end &&
			end.ToUniversalTime() <= CurrentTime)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var subscription = new CandleSubscription
		{
			SecurityId = security,
			TimeFrame = timeFrame,
		};
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = subscription;
		try
		{
			await SubscribeTopicAsync(CandleTopic(
				security.SecurityCode, timeFrame), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityId Normalize(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not " +
					$"associated with Settrade {BoardCode}.");
		return ToSecurityId(securityId.SecurityCode
			.ThrowIfEmpty(nameof(securityId)).Trim());
	}

	private static string InfoTopic(string symbol)
		=> $"proto/topic/infov3/{symbol}";

	private static string BookTopic(string symbol)
		=> $"proto/topic/bidofferv3/{symbol}";

	private static string CandleTopic(string symbol, TimeSpan timeFrame)
		=> $"proto/topic/cdlv3/{symbol}/" +
			timeFrame.ToSettradeInterval();

	private async ValueTask OnStreamMessageAsync(string topic,
		byte[] payload, CancellationToken cancellationToken)
	{
		try
		{
			if (topic.StartsWith("proto/topic/infov3/",
				StringComparison.Ordinal))
			{
				var value = SettradeProtoDecoder.DecodeLevel1(payload);
				var targets = FindTargets(_level1Subscriptions,
					value.Symbol);
				foreach (var target in targets)
					await SendLevel1Async(value, null, target.SecurityId,
						target.Id, cancellationToken);
				return;
			}
			if (topic.StartsWith("proto/topic/bidofferv3/",
				StringComparison.Ordinal))
			{
				var value = SettradeProtoDecoder.DecodeOrderBook(payload);
				foreach (var target in FindTargets(
					_level1Subscriptions, value.Symbol))
					await SendLevel1Async(null, value,
						target.SecurityId, target.Id, cancellationToken);
				foreach (var target in FindTargets(
					_depthSubscriptions, value.Symbol))
					await SendOrderBookAsync(value, target.SecurityId,
						target.Id, cancellationToken);
				return;
			}
			if (topic.StartsWith("proto/topic/cdlv3/",
				StringComparison.Ordinal))
			{
				var value = SettradeProtoDecoder.DecodeCandle(payload);
				(long Id, CandleSubscription Value)[] targets;
				using (_sync.EnterScope())
					targets =
					[
						.. _candleSubscriptions
							.Where(pair =>
								pair.Value.SecurityId.SecurityCode
									.EqualsIgnoreCase(value.Symbol) &&
								pair.Value.TimeFrame.ToSettradeInterval()
									.EqualsIgnoreCase(value.Interval))
							.Select(static pair =>
								(pair.Key, pair.Value))
					];
				foreach (var target in targets)
					await SendCandleAsync(value,
						target.Value.SecurityId,
						target.Value.TimeFrame, target.Id,
						CandleStates.Active, cancellationToken);
				return;
			}
			if (topic.Contains("/ordereqv3",
				StringComparison.OrdinalIgnoreCase))
			{
				await ProcessStreamOrderAsync(
					SettradeProtoDecoder.DecodeEquityOrder(payload),
					cancellationToken);
				return;
			}
			if (topic.Contains("/orderdvv3",
				StringComparison.OrdinalIgnoreCase))
				await ProcessStreamOrderAsync(
					SettradeProtoDecoder.DecodeDerivativeOrder(payload),
					cancellationToken);
		}
		catch (Exception error)
			when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private (long Id, SecurityId SecurityId)[] FindTargets(
		Dictionary<long, SecurityId> subscriptions, string symbol)
	{
		using (_sync.EnterScope())
			return subscriptions
				.Where(pair => pair.Value.SecurityCode
					.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value))
				.ToArray();
	}

	private ValueTask SendLevel1Async(SettradeLevel1 level1,
		SettradeOrderBook book, SecurityId securityId, long target,
		CancellationToken cancellationToken)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			ServerTime = CurrentTime,
		};
		if (level1 is not null)
		{
			message
				.TryAdd(Level1Fields.OpenPrice,
					level1.ProjectedOpenPrice)
				.TryAdd(Level1Fields.HighPrice, level1.High)
				.TryAdd(Level1Fields.LowPrice, level1.Low)
				.TryAdd(Level1Fields.LastTradePrice, level1.Last)
				.TryAdd(Level1Fields.Volume, level1.TotalVolume)
				.TryAdd(Level1Fields.Turnover, level1.TotalValue);
		}
		if (book is not null)
		{
			var bid = book.Bids.FirstOrDefault();
			var ask = book.Asks.FirstOrDefault();
			if (bid.Price > 0)
				message
					.TryAdd(Level1Fields.BestBidPrice, bid.Price)
					.TryAdd(Level1Fields.BestBidVolume, bid.Volume);
			if (ask.Price > 0)
				message
					.TryAdd(Level1Fields.BestAskPrice, ask.Price)
					.TryAdd(Level1Fields.BestAskVolume, ask.Volume);
		}
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendOrderBookAsync(SettradeOrderBook book,
		SecurityId securityId, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			ServerTime = CurrentTime,
			Bids = book.Bids.Select(static level =>
				new QuoteChange(level.Price, level.Volume)).ToArray(),
			Asks = book.Asks.Select(static level =>
				new QuoteChange(level.Price, level.Volume)).ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);

	private ValueTask SendCandleAsync(SettradeCandle candle,
		SecurityId securityId, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			OpenTime = candle.Time,
			CloseTime = candle.Time + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover,
			TypedArg = timeFrame,
			State = state,
		}, cancellationToken);

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
