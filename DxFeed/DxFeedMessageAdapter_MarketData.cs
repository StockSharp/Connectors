namespace StockSharp.DxFeed;

public partial class DxFeedMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var securityIds = lookupMsg.SecurityIds
			.Concat(lookupMsg.SecurityId.SecurityCode.IsEmpty() ? [] : [lookupMsg.SecurityId])
			.Where(id => !id.SecurityCode.IsEmpty())
			.GroupBy(id => id.SecurityCode, StringComparer.OrdinalIgnoreCase)
			.Select(g => NormalizeSecurityId(g.First()))
			.Skip((int)Math.Min(int.MaxValue, Math.Max(0, lookupMsg.Skip ?? 0)))
			.Take((int)Math.Min(int.MaxValue, Math.Max(0, lookupMsg.Count ?? int.MaxValue)))
			.ToArray();

		if (securityIds.Length == 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var securityTypes = lookupMsg.GetSecurityTypes();
		var securityType = securityTypes.Count == 1 ? securityTypes.First() : (SecurityTypes?)null;
		var context = new SecurityLookupContext(lookupMsg, securityIds, securityType);
		_securityLookups[lookupMsg.TransactionId] = context;

		var subscribed = new List<string>();
		try
		{
			foreach (var securityId in securityIds)
			{
				await SafeClient().SubscribeFeed(DxFeedEventTypes.Profile,
					securityId.SecurityCode, null, null, cancellationToken);
				subscribed.Add(securityId.SecurityCode);
			}
		}
		catch
		{
			_securityLookups.Remove(lookupMsg.TransactionId);
			foreach (var symbol in subscribed)
				await SafeClient().UnsubscribeFeed(DxFeedEventTypes.Profile,
					symbol, null, null, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscription(message.OriginalTransactionId, cancellationToken);
			return;
		}

		if (message.Count is <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			return;
		}

		var securityType = message.SecurityType ?? message.SecurityId.SecurityCode.InferSecurityType();
		var eventTypes = new List<string>
		{
			DxFeedEventTypes.Quote,
			DxFeedEventTypes.Trade,
			DxFeedEventTypes.Summary,
			DxFeedEventTypes.Profile,
		};

		if (securityType == SecurityTypes.Stock)
			eventTypes.Add(DxFeedEventTypes.TradeEth);
		if (securityType == SecurityTypes.Option)
		{
			eventTypes.Add(DxFeedEventTypes.Greeks);
			eventTypes.Add(DxFeedEventTypes.TheoPrice);
			eventTypes.Add(DxFeedEventTypes.Underlying);
		}

		var subscription = CreateSubscription(message, message.SecurityId.SecurityCode,
			[.. eventTypes], null, null, false);
		await AddMarketSubscription(message, subscription, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscription(message.OriginalTransactionId, cancellationToken);
			return;
		}

		if (message.Count is <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			return;
		}

		var from = message.From ?? DateTime.UtcNow;
		var subscription = CreateSubscription(message, message.SecurityId.SecurityCode,
			[DxFeedEventTypes.TimeAndSale], from, null, false);
		await AddMarketSubscription(message, subscription, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscription(message.OriginalTransactionId, cancellationToken);
			return;
		}

		if (message.Count is <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			return;
		}

		var timeFrame = message.GetTimeFrame();
		var symbol = $"{message.SecurityId.SecurityCode}{{={timeFrame.ToDxCandlePeriod()}}}";
		var from = message.From ?? DateTime.UtcNow;
		var subscription = CreateSubscription(message, symbol,
			[DxFeedEventTypes.Candle], from, timeFrame, false);
		await AddMarketSubscription(message, subscription, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscription(message.OriginalTransactionId, cancellationToken);
			return;
		}

		if (message.Count is <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			return;
		}

		var subscription = CreateSubscription(message, message.SecurityId.SecurityCode,
			[], null, null, true);
		_marketSubscriptions.Add(message.TransactionId, subscription);

		try
		{
			await SafeClient().SubscribeDom(subscription.Symbol, _depthSources, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(message.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscription(message.OriginalTransactionId, cancellationToken);
			return;
		}

		if (message.Count is <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			return;
		}

		var subscription = CreateSubscription(message, message.SecurityId.SecurityCode,
			[DxFeedEventTypes.Order], null, null, false, _depthSources);
		await AddMarketSubscription(message, subscription, cancellationToken);
	}

	private MarketSubscription CreateSubscription(MarketDataMessage message, string symbol,
		string[] eventTypes, DateTime? from, TimeSpan? timeFrame, bool isDepth,
		string[] sources = null)
	{
		message.SecurityId.SecurityCode.ThrowIfEmpty(nameof(message.SecurityId.SecurityCode));
		var subscription = new MarketSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = NormalizeSecurityId(message.SecurityId),
			SecurityType = message.SecurityType,
			DataType = message.DataType2,
			Symbol = symbol,
			EventTypes = eventTypes,
			Sources = sources ?? [],
			From = from?.ToUniversalTime(),
			To = message.To?.ToUniversalTime(),
			TimeFrame = timeFrame,
			IsHistoryOnly = message.IsHistoryOnly(),
			IsDepth = isDepth,
		};
		subscription.SetCount(message.Count);
		return subscription;
	}

	private async ValueTask AddMarketSubscription(MarketDataMessage message,
		MarketSubscription subscription, CancellationToken cancellationToken)
	{
		_marketSubscriptions.Add(message.TransactionId, subscription);
		var subscribed = new List<(string eventType, string source)>();

		try
		{
			foreach (var eventType in subscription.EventTypes)
			{
				foreach (var source in GetEventSources(subscription, eventType))
				{
					await SafeClient().SubscribeFeed(eventType, subscription.Symbol,
						subscription.From, source, cancellationToken);
					subscribed.Add((eventType, source));
				}
			}
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(message.TransactionId);
			foreach (var item in subscribed)
				await SafeClient().UnsubscribeFeed(item.eventType, subscription.Symbol,
					subscription.From, item.source, cancellationToken);
			throw;
		}
	}

	private async ValueTask RemoveMarketSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetAndRemove(transactionId, out var subscription) ||
			!subscription.TryComplete())
			return;

		await UnsubscribeNative(subscription, cancellationToken);
	}

	private async ValueTask FinishMarketSubscription(MarketSubscription subscription,
		CancellationToken cancellationToken)
	{
		if (!subscription.TryComplete())
			return;

		_marketSubscriptions.Remove(subscription.TransactionId);
		await UnsubscribeNative(subscription, cancellationToken);
		await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
	}

	private async ValueTask UnsubscribeNative(MarketSubscription subscription,
		CancellationToken cancellationToken)
	{
		if (subscription.IsDepth)
		{
			await SafeClient().UnsubscribeDom(subscription.Symbol, _depthSources, cancellationToken);
			return;
		}

		foreach (var eventType in subscription.EventTypes)
		{
			foreach (var source in GetEventSources(subscription, eventType))
				await SafeClient().UnsubscribeFeed(eventType, subscription.Symbol,
					subscription.From, source, cancellationToken);
		}
	}

	private async ValueTask ProcessFeedData(DxFeedEvent data,
		CancellationToken cancellationToken)
	{
		if (data.EventType.EqualsIgnoreCase(DxFeedEventTypes.Profile))
			await ProcessProfileLookups(data, cancellationToken);

		var subscriptions = _marketSubscriptions
			.Where(p => !p.Value.IsDepth &&
				p.Value.Symbol.EqualsIgnoreCase(data.EventSymbol) &&
				p.Value.EventTypes.Contains(data.EventType, StringComparer.OrdinalIgnoreCase) &&
				(p.Value.Sources.Length == 0 || data.Source.IsEmpty() ||
					p.Value.Sources.Contains(data.Source, StringComparer.OrdinalIgnoreCase)))
			.Select(p => p.Value)
			.ToArray();

		foreach (var subscription in subscriptions)
		{
			var time = GetEventTime(data);
			var isRemoved = data.EventFlags.IsRemoved();
			Message output = null;

			if ((data.EventType.EqualsIgnoreCase(DxFeedEventTypes.Order) || !isRemoved) &&
				subscription.IsTimeAllowed(time))
			{
				output = data.EventType switch
				{
					DxFeedEventTypes.TimeAndSale => CreateTick(subscription, data, time),
					DxFeedEventTypes.Candle => CreateCandle(subscription, data, time),
					DxFeedEventTypes.Order => CreateOrderLog(subscription, data, time),
					_ => CreateLevel1(subscription, data, time),
				};
			}

			if (output != null && subscription.TryConsume())
				await SendOutMessageAsync(output, cancellationToken);

			var isSnapshotComplete = subscription.UpdateSnapshotState(data.Source, data.EventFlags);

			if (subscription.IsCountExhausted ||
				(subscription.IsHistoryOnly && isSnapshotComplete) ||
				(subscription.IsHistoryOnly && subscription.DataType == DataType.Level1 && output != null))
				await FinishMarketSubscription(subscription, cancellationToken);
		}
	}

	private async ValueTask ProcessDomSnapshot(string symbol, DxDomSnapshot snapshot,
		CancellationToken cancellationToken)
	{
		var subscriptions = _marketSubscriptions
			.Where(p => p.Value.IsDepth && p.Value.Symbol.EqualsIgnoreCase(symbol))
			.Select(p => p.Value)
			.ToArray();

		foreach (var subscription in subscriptions)
		{
			if (!subscription.TryConsume())
				continue;

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = snapshot.Time.ToUtcTime(),
				Bids = ToQuotes(snapshot.Bids),
				Asks = ToQuotes(snapshot.Asks),
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);

			if (subscription.IsHistoryOnly || subscription.IsCountExhausted)
				await FinishMarketSubscription(subscription, cancellationToken);
		}
	}

	private async ValueTask ProcessProfileLookups(DxFeedEvent profile,
		CancellationToken cancellationToken)
	{
		foreach (var pair in _securityLookups.ToArray())
		{
			var context = pair.Value;
			if (!context.TryTake(profile.EventSymbol, out var securityId, out var isComplete))
				continue;

			await SendOutMessageAsync(CreateSecurityMessage(context, securityId, profile), cancellationToken);
			await SafeClient().UnsubscribeFeed(DxFeedEventTypes.Profile,
				securityId.SecurityCode, null, null, cancellationToken);

			if (isComplete && _securityLookups.TryGetAndRemove(pair.Key, out _))
				await SendSubscriptionResultAsync(context.Message, cancellationToken);
		}
	}

	private static SecurityMessage CreateSecurityMessage(SecurityLookupContext context,
		SecurityId securityId, DxFeedEvent profile)
		=> new()
		{
			OriginalTransactionId = context.Message.TransactionId,
			SecurityId = securityId,
			Name = context.Message.OnlySecurityId ? null : profile?.Description,
			SecurityType = context.SecurityType ?? securityId.SecurityCode.InferSecurityType(),
		};

	private static Level1ChangeMessage CreateLevel1(MarketSubscription subscription,
		DxFeedEvent data, DateTime time)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		};

		switch (data.EventType)
		{
			case DxFeedEventTypes.Quote:
				message
					.TryAdd(Level1Fields.BestBidPrice, data.BidPrice)
					.TryAdd(Level1Fields.BestBidVolume, data.BidSize)
					.TryAdd(Level1Fields.BestBidTime, data.BidTime is > 0 ? data.BidTime.ToUtcTime() : null)
					.TryAdd(Level1Fields.BestAskPrice, data.AskPrice)
					.TryAdd(Level1Fields.BestAskVolume, data.AskSize)
					.TryAdd(Level1Fields.BestAskTime, data.AskTime is > 0 ? data.AskTime.ToUtcTime() : null);
				break;

			case DxFeedEventTypes.Trade:
			case DxFeedEventTypes.TradeEth:
				message
					.TryAdd(Level1Fields.LastTradePrice, data.Price)
					.TryAdd(Level1Fields.LastTradeVolume, data.Size)
					.TryAdd(Level1Fields.LastTradeTime, data.Time is > 0 ? data.Time.ToUtcTime() : null)
					.TryAdd(Level1Fields.LastTradeUpDown, data.TickDirection.ToUpDown())
					.TryAdd(Level1Fields.Change, data.Change)
					.TryAdd(Level1Fields.Volume, data.DayVolume);
				break;

			case DxFeedEventTypes.Summary:
				message
					.TryAdd(Level1Fields.OpenPrice, data.DayOpenPrice)
					.TryAdd(Level1Fields.HighPrice, data.DayHighPrice)
					.TryAdd(Level1Fields.LowPrice, data.DayLowPrice)
					.TryAdd(Level1Fields.ClosePrice, data.DayClosePrice ?? data.PreviousDayClosePrice)
					.TryAdd(Level1Fields.OpenInterest, data.OpenInterest);
				break;

			case DxFeedEventTypes.Profile:
				message
					.TryAdd(Level1Fields.MinPrice, data.LowLimitPrice)
					.TryAdd(Level1Fields.MaxPrice, data.HighLimitPrice)
					.TryAdd(Level1Fields.HighPrice52Week, data.High52WeekPrice)
					.TryAdd(Level1Fields.LowPrice52Week, data.Low52WeekPrice)
					.TryAdd(Level1Fields.Beta, data.Beta)
					.TryAdd(Level1Fields.Dividend, data.ExDividendAmount)
					.TryAdd(Level1Fields.SharesOutstanding, data.Shares)
					.TryAdd(Level1Fields.SharesFloat, data.FreeFloat)
					.TryAdd(Level1Fields.State, data.TradingStatus.ToSecurityState());
				break;

			case DxFeedEventTypes.Greeks:
				message
					.TryAdd(Level1Fields.ImpliedVolatility, data.Volatility)
					.TryAdd(Level1Fields.Delta, data.Delta)
					.TryAdd(Level1Fields.Gamma, data.Gamma)
					.TryAdd(Level1Fields.Theta, data.Theta)
					.TryAdd(Level1Fields.Rho, data.Rho)
					.TryAdd(Level1Fields.Vega, data.Vega);
				break;

			case DxFeedEventTypes.TheoPrice:
				message
					.TryAdd(Level1Fields.TheorPrice, data.Price)
					.TryAdd(Level1Fields.UnderlyingPrice, data.UnderlyingPrice)
					.TryAdd(Level1Fields.Delta, data.Delta)
					.TryAdd(Level1Fields.Gamma, data.Gamma)
					.TryAdd(Level1Fields.Dividend, data.Dividend);
				break;

			case DxFeedEventTypes.Underlying:
				message.TryAdd(Level1Fields.HistoricalVolatility, data.Volatility);
				break;
		}

		return message.Changes.Count == 0 ? null : message;
	}

	private static ExecutionMessage CreateTick(MarketSubscription subscription,
		DxFeedEvent data, DateTime time)
	{
		if (data.Price == null || data.IsValidTick == false)
			return null;

		return new()
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
			TradeId = data.TradeId ?? data.Index,
			TradePrice = data.Price,
			TradeVolume = data.Size,
			OriginSide = data.AggressorSide.ToSide(),
			SeqNum = data.Sequence ?? 0,
			TradeStatus = data.EventFlags,
		};
	}

	private static TimeFrameCandleMessage CreateCandle(MarketSubscription subscription,
		DxFeedEvent data, DateTime time)
	{
		if (data.Open == null || data.High == null || data.Low == null || data.Close == null ||
			subscription.TimeFrame == null)
			return null;

		var timeFrame = subscription.TimeFrame.Value;
		return new()
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			DataType = subscription.DataType,
			OpenTime = time,
			CloseTime = time + timeFrame,
			OpenPrice = data.Open.Value,
			HighPrice = data.High.Value,
			LowPrice = data.Low.Value,
			ClosePrice = data.Close.Value,
			TotalVolume = data.Volume ?? 0,
			BuyVolume = data.AskVolume,
			SellVolume = data.BidVolume,
			OpenInterest = data.OpenInterest,
			TotalTicks = data.Count is >= 0 ? (int)Math.Min(int.MaxValue, data.Count.Value) : null,
			SeqNum = data.Sequence ?? 0,
			State = time + timeFrame <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
		};
	}

	private static ExecutionMessage CreateOrderLog(MarketSubscription subscription,
		DxFeedEvent data, DateTime time)
	{
		var isRemoved = data.EventFlags.IsRemoved() ||
			data.Action.EqualsIgnoreCase("DELETE") || data.Action.EqualsIgnoreCase("REMOVE");

		return new()
		{
			DataTypeEx = DataType.OrderLog,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = data.ActionTime is > 0 ? data.ActionTime.ToUtcTime() : time,
			OrderId = data.OrderId ?? data.Index,
			OrderPrice = data.Price ?? 0,
			OrderVolume = data.Size,
			Balance = data.Size,
			Side = data.OrderSide.ToSide() ?? default,
			OrderState = isRemoved ? OrderStates.Done : OrderStates.Active,
			TradeId = data.TradeId,
			TradePrice = data.TradePrice,
			TradeVolume = data.TradeSize,
			BrokerCode = data.MarketMaker.IsEmpty(data.Source),
			SeqNum = data.Sequence ?? 0,
			OrderStatus = data.EventFlags,
		};
	}

	private static QuoteChange[] ToQuotes(DxDomLevel[] levels)
		=> (levels ?? [])
			.Where(level => level.Price != null && level.Size != null)
			.Select(level => new QuoteChange(level.Price.Value, level.Size.Value, level.Count))
			.ToArray();

	private static DateTime GetEventTime(DxFeedEvent data)
	{
		foreach (var value in new[] { data.Time, data.EventTime, data.BidTime, data.AskTime })
		{
			if (value is > 0)
				return value.ToUtcTime();
		}
		return DateTime.UtcNow;
	}

	private static SecurityId NormalizeSecurityId(SecurityId securityId)
		=> new()
		{
			SecurityCode = securityId.SecurityCode,
			BoardCode = securityId.BoardCode.IsEmpty("DXFEED"),
			Native = securityId.Native,
		};

	private static IEnumerable<string> GetEventSources(MarketSubscription subscription,
		string eventType)
		=> eventType.EqualsIgnoreCase(DxFeedEventTypes.Order)
			? subscription.Sources
			: [null];
}
