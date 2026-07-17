namespace StockSharp.Qmt;

using Native.Model;

partial class QmtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var markets = message.SecurityId.BoardCode?.ToUpperInvariant() switch
		{
			BoardCodes.Sse or "SH" => ["SH"],
			BoardCodes.Szse or "SZ" => ["SZ"],
			BoardCodes.Bse or "BJ" => ["BJ"],
			_ => Array.Empty<string>(),
		};
		var limit = message.Count is > 0
			? (int)Math.Min(message.Count.Value, 5000)
			: 1000;
		var types = message.GetSecurityTypes();

		foreach (var definition in await EnsureClient().SearchAsync(
			message.SecurityId.SecurityCode, markets, limit, cancellationToken))
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = definition.ToSecurityId(),
				SecurityType = definition.SecurityType.ToSecurityType(),
				Currency = CurrencyTypes.CNY,
				Name = definition.Name,
				ShortName = definition.Name,
				PriceStep = definition.PriceStep,
				VolumeStep = definition.VolumeStep,
				Multiplier = definition.Multiplier,
				ExpiryDate = definition.Expiry?.ToUtc(),
			};

			if (security.IsMatch(message, types))
				await SendOutMessageAsync(security, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, DataType.Level1, QmtGatewayKinds.Level1, "tick", cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, DataType.MarketDepth, QmtGatewayKinds.Depth, "tick", cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, DataType.Ticks, QmtGatewayKinds.Trade, "l2transaction", cancellationToken);

	private async ValueTask ProcessSubscriptionAsync(
		MarketDataMessage message,
		DataType dataType,
		string dataKind,
		string period,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var client = EnsureClient();
		if (!message.IsSubscribe)
		{
			await client.UnsubscribeAsync(message.OriginalTransactionId, cancellationToken);
			_marketSubscriptions.TryRemove(message.OriginalTransactionId, out _);
			return;
		}

		if (message.IsHistoryOnly() || message.From != null || message.To != null)
			throw new NotSupportedException("QMT Level1, depth, and trade subscriptions are real-time only. Use candles for history.");

		var subscription = new MarketSubscription
		{
			SecurityId = message.SecurityId,
			DataType = dataType,
			MaxDepth = Math.Max(1, message.MaxDepth ?? 5),
		};
		if (!_marketSubscriptions.TryAdd(message.TransactionId, subscription))
			throw new InvalidOperationException($"QMT subscription {message.TransactionId} already exists.");

		try
		{
			await client.SubscribeAsync(new()
			{
				SubscriptionId = message.TransactionId,
				Symbol = message.SecurityId.ToQmtSymbol(),
				DataKind = dataKind,
				Period = period,
			}, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.TryRemove(message.TransactionId, out _);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var client = EnsureClient();
		if (!message.IsSubscribe)
		{
			await client.UnsubscribeAsync(message.OriginalTransactionId, cancellationToken);
			_marketSubscriptions.TryRemove(message.OriginalTransactionId, out _);
			return;
		}

		var timeFrame = message.GetTimeFrame();
		if (!QmtExtensions.TimeFrames.Contains(timeFrame))
			throw new ArgumentOutOfRangeException(nameof(message), timeFrame, "Unsupported QMT candle time frame.");
		var period = timeFrame.ToQmtPeriod();
		var symbol = message.SecurityId.ToQmtSymbol();
		var count = message.Count is > 0
			? (int)Math.Min(message.Count.Value, 10000)
			: message.From != null ? 10000 : 1000;
		var candles = await client.GetHistoryAsync(new()
		{
			Symbol = symbol,
			Period = period,
			From = message.From?.ToUnixMilliseconds(),
			To = message.To?.ToUnixMilliseconds(),
			Count = count,
		}, cancellationToken);

		foreach (var candle in candles.OrderBy(item => item.Time))
			await SendCandleAsync(message.TransactionId, message.SecurityId, timeFrame, candle,
				CandleStates.Finished, cancellationToken);

		if (message.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			return;
		}

		var subscription = new MarketSubscription
		{
			SecurityId = message.SecurityId,
			DataType = message.DataType2,
			TimeFrame = timeFrame,
		};
		if (!_marketSubscriptions.TryAdd(message.TransactionId, subscription))
			throw new InvalidOperationException($"QMT subscription {message.TransactionId} already exists.");

		try
		{
			await client.SubscribeAsync(new()
			{
				SubscriptionId = message.TransactionId,
				Symbol = symbol,
				DataKind = QmtGatewayKinds.Candle,
				Period = period,
			}, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.TryRemove(message.TransactionId, out _);
			throw;
		}
	}

	private ValueTask ProcessLevel1Async(long subscriptionId, QmtQuote quote,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
			subscription.DataType != DataType.Level1)
			return default;

		var bestBid = quote.Bids?.FirstOrDefault();
		var bestAsk = quote.Asks?.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = subscription.SecurityId,
			ServerTime = quote.Time.ToUtc(),
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, quote.HighPrice)
		.TryAdd(Level1Fields.LowPrice, quote.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose)
		.TryAdd(Level1Fields.SettlementPrice, quote.SettlementPrice)
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.Turnover, quote.Turnover)
		.TryAdd(Level1Fields.OpenInterest, quote.OpenInterest)
		.TryAdd(Level1Fields.BestBidPrice, bestBid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bestBid?.Volume)
		.TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price)
		.TryAdd(Level1Fields.BestAskVolume, bestAsk?.Volume), cancellationToken);
	}

	private ValueTask ProcessDepthAsync(long subscriptionId, QmtQuote quote,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
			subscription.DataType != DataType.MarketDepth)
			return default;

		var bids = (quote.Bids ?? []).Where(level => level.Price > 0)
			.OrderByDescending(level => level.Price).Take(subscription.MaxDepth)
			.Select(level => new QuoteChange(level.Price, level.Volume)).ToArray();
		var asks = (quote.Asks ?? []).Where(level => level.Price > 0)
			.OrderBy(level => level.Price).Take(subscription.MaxDepth)
			.Select(level => new QuoteChange(level.Price, level.Volume)).ToArray();
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = subscription.SecurityId,
			ServerTime = quote.Time.ToUtc(),
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private ValueTask ProcessMarketTradeAsync(long subscriptionId, QmtMarketTrade trade,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
			subscription.DataType != DataType.Ticks)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscriptionId,
			SecurityId = subscription.SecurityId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side.IsEmpty() ? null : trade.Side.ToSide(),
			ServerTime = trade.Time.ToUtc(),
		}, cancellationToken);
	}

	private async ValueTask ProcessCandleAsync(long subscriptionId, QmtCandle candle,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
			!subscription.DataType.IsTFCandles)
			return;

		if (subscription.ActiveCandle != null && subscription.ActiveCandle.Time != candle.Time)
			await SendCandleAsync(subscriptionId, subscription.SecurityId, subscription.TimeFrame,
				subscription.ActiveCandle, CandleStates.Finished, cancellationToken);
		subscription.ActiveCandle = candle;
		await SendCandleAsync(subscriptionId, subscription.SecurityId, subscription.TimeFrame,
			candle, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendCandleAsync(long subscriptionId, SecurityId securityId, TimeSpan timeFrame,
		QmtCandle candle, CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = securityId,
			TypedArg = timeFrame,
			OpenTime = candle.Time.ToUtc(),
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover ?? 0,
			OpenInterest = candle.OpenInterest,
			State = state,
		}, cancellationToken);
}
