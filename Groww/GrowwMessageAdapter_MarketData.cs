namespace StockSharp.Groww;

public partial class GrowwMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _rest.GetInstruments(cancellationToken))
		{
			var securityType = instrument.ToSecurityType();
			var securityId = instrument.ToSecurityId();
			var message = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = securityType,
				Name = instrument.Name.IsEmpty(instrument.TradingSymbol),
				ShortName = instrument.TradingSymbol,
				Currency = CurrencyTypes.INR,
				PriceStep = instrument.TickSize,
				VolumeStep = instrument.LotSize is > 0 ? instrument.LotSize : 1,
				Multiplier = instrument.LotSize is > 0 ? instrument.LotSize : 1,
				ExpiryDate = instrument.ExpiryDate,
				Strike = instrument.StrikePrice,
				OptionType = instrument.InstrumentType.ToOptionType(),
				UnderlyingSecurityId = instrument.UnderlyingSymbol.IsEmpty()
					? default
					: new SecurityId
					{
						SecurityCode = instrument.UnderlyingSymbol,
						BoardCode = instrument.Exchange,
					},
			};

			if (!message.IsMatch(lookupMsg, securityTypes))
				continue;

			CacheSecurity(GrowwSecurityInfo.FromInstrument(instrument));
			await SendOutMessageAsync(message, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var security = ResolveSecurity(mdMsg.SecurityId, mdMsg.SecurityType);
		var key = security.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.IsHistoryOnly())
			{
				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			if (!_marketSubscriptions.TryGetValue(key, out var subscription))
			{
				if (_marketSubscriptions.Count >= 1000)
					throw new InvalidOperationException("Groww permits at most 1000 simultaneously subscribed instruments.");
				subscription = new()
				{
					SecurityId = mdMsg.SecurityId,
					Security = security,
				};
				_marketSubscriptions.Add(key, subscription);
			}

			if (dataType == DataType.MarketDepth)
			{
				var first = subscription.DepthId == 0;
				subscription.DepthId = mdMsg.TransactionId;
				if (first)
					await SubscribeMarketSubject(GrowwFeedTopics.GetDepth(security), key, FeedKinds.Depth, cancellationToken);
			}
			else
			{
				var first = subscription.Level1Id == 0 && subscription.TickId == 0;
				if (dataType == DataType.Level1)
					subscription.Level1Id = mdMsg.TransactionId;
				else
					subscription.TickId = mdMsg.TransactionId;
				if (first)
				{
					var isIndex = GrowwFeedTopics.IsIndex(security);
					await SubscribeMarketSubject(isIndex ? GrowwFeedTopics.GetIndex(security) : GrowwFeedTopics.GetPrice(security),
						key, isIndex ? FeedKinds.Index : FeedKinds.Price, cancellationToken);
				}
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		if (!_marketSubscriptions.TryGetValue(key, out var existing))
			return;

		if (dataType == DataType.MarketDepth)
		{
			existing.DepthId = 0;
			await UnsubscribeMarketSubject(GrowwFeedTopics.GetDepth(security));
		}
		else
		{
			if (dataType == DataType.Level1)
				existing.Level1Id = 0;
			else
				existing.TickId = 0;
			if (existing.Level1Id == 0 && existing.TickId == 0)
			{
				var isIndex = GrowwFeedTopics.IsIndex(security);
				await UnsubscribeMarketSubject(isIndex ? GrowwFeedTopics.GetIndex(security) : GrowwFeedTopics.GetPrice(security));
			}
		}

		if (existing.Level1Id == 0 && existing.TickId == 0 && existing.DepthId == 0)
			_marketSubscriptions.Remove(key);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var security = ResolveSecurity(mdMsg.SecurityId, mdMsg.SecurityType);
		IEnumerable<GrowwCandle> candles = await _rest.GetCandles(security, mdMsg.GetTimeFrame(), mdMsg.From, mdMsg.To, cancellationToken);
		if (mdMsg.Count is > 0)
			candles = candles.TakeLast((int)Math.Min(mdMsg.Count.Value, int.MaxValue));

		foreach (var candle in candles)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = mdMsg.GetTimeFrame(),
				OpenTime = candle.OpenTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				OpenInterest = candle.OpenInterest,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask SubscribeMarketSubject(string subject, string securityKey, FeedKinds kind, CancellationToken cancellationToken)
	{
		_feedRoutes[subject] = new() { Kind = kind, SecurityKey = securityKey };
		try
		{
			await _feed.Subscribe(subject, cancellationToken);
		}
		catch
		{
			_feedRoutes.Remove(subject);
			throw;
		}
	}

	private async ValueTask UnsubscribeMarketSubject(string subject)
	{
		_feedRoutes.Remove(subject);
		await _feed.Unsubscribe(subject);
	}

	private async ValueTask ProcessMarketFeed(FeedRoute route, byte[] data, CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(route.SecurityKey, out var subscription))
			return;

		var response = StocksSocketResponseProtoDto.Parser.ParseFrom(data);
		switch (route.Kind)
		{
			case FeedKinds.Price when response.StockLivePrice != null:
				await ProcessPrice(subscription, response.StockLivePrice, cancellationToken);
				break;
			case FeedKinds.Index when response.StocksLiveIndices != null:
				await ProcessIndex(subscription, response.StocksLiveIndices, cancellationToken);
				break;
			case FeedKinds.Depth when response.StocksMarketDepth != null:
				await ProcessDepth(subscription, response.StocksMarketDepth, cancellationToken);
				break;
		}
	}

	private async ValueTask ProcessPrice(MarketSubscription subscription, StocksLivePriceProto price, CancellationToken cancellationToken)
	{
		var serverTime = GrowwNativeExtensions.FromFeedTimestamp(price.TsInMillis);
		if (subscription.Level1Id != 0)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.Level1Id,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, price.Ltp > 0 ? (decimal?)price.Ltp : null)
			.TryAdd(Level1Fields.OpenPrice, price.Open > 0 ? (decimal?)price.Open : null)
			.TryAdd(Level1Fields.HighPrice, price.High > 0 ? (decimal?)price.High : null)
			.TryAdd(Level1Fields.LowPrice, price.Low > 0 ? (decimal?)price.Low : null)
			.TryAdd(Level1Fields.ClosePrice, price.Close > 0 ? (decimal?)price.Close : null)
			.TryAdd(Level1Fields.Volume, price.Volume > 0 ? (decimal?)price.Volume : null)
			.TryAdd(Level1Fields.AveragePrice, price.AvgPrice > 0 ? (decimal?)price.AvgPrice : null)
			.TryAdd(Level1Fields.OpenInterest, price.OpenInterest > 0 ? (decimal?)price.OpenInterest : null)
			.TryAdd(Level1Fields.MaxPrice, price.HighPriceRange > 0 ? (decimal?)price.HighPriceRange : null)
			.TryAdd(Level1Fields.MinPrice, price.LowPriceRange > 0 ? (decimal?)price.LowPriceRange : null), cancellationToken);
		}

		if (subscription.TickId != 0 && price.Ltp > 0)
		{
			var tick = (new DateTimeOffset(serverTime).ToUnixTimeMilliseconds(), (decimal)price.Ltp);
			if (subscription.LastTick != tick)
			{
				subscription.LastTick = tick;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.TickId,
					SecurityId = subscription.SecurityId,
					ServerTime = serverTime,
					TradePrice = tick.Item2,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessIndex(MarketSubscription subscription, StocksLiveIndicesProto index, CancellationToken cancellationToken)
	{
		var serverTime = GrowwNativeExtensions.FromFeedTimestamp(index.TsInMillis);
		if (subscription.Level1Id != 0 && index.Value > 0)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.Level1Id,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}.Add(Level1Fields.LastTradePrice, (decimal)index.Value), cancellationToken);
		}

		if (subscription.TickId != 0 && index.Value > 0)
		{
			var tick = (new DateTimeOffset(serverTime).ToUnixTimeMilliseconds(), (decimal)index.Value);
			if (subscription.LastTick != tick)
			{
				subscription.LastTick = tick;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.TickId,
					SecurityId = subscription.SecurityId,
					ServerTime = serverTime,
					TradePrice = tick.Item2,
				}, cancellationToken);
			}
		}
	}

	private ValueTask ProcessDepth(MarketSubscription subscription, StocksMarketDepthProto depth, CancellationToken cancellationToken)
	{
		if (subscription.DepthId == 0)
			return default;
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscription.DepthId,
			SecurityId = subscription.SecurityId,
			ServerTime = GrowwNativeExtensions.FromFeedTimestamp(depth.TsInMillis),
			Bids = [.. depth.BuyBook.OrderBy(pair => pair.Key).Where(pair => pair.Value.Price > 0).Select(pair => new QuoteChange((decimal)pair.Value.Price, (decimal)pair.Value.Qty))],
			Asks = [.. depth.SellBook.OrderBy(pair => pair.Key).Where(pair => pair.Value.Price > 0).Select(pair => new QuoteChange((decimal)pair.Value.Price, (decimal)pair.Value.Qty))],
		}, cancellationToken);
	}
}
