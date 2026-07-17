namespace StockSharp.Usmart;

public partial class UsmartMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var requestedTypes = message.GetSecurityTypes();
		var explicitMarket = message.SecurityId.BoardCode.IsEmpty()
			? null : message.SecurityId.ToMarket(DefaultMarket);
		var markets = explicitMarket.IsEmpty() ? new[] { "hk", "us", "sh", "sz" }
			: new[] { explicitMarket };
		var skip = Math.Max(0, message.Skip ?? 0);
		var left = Math.Max(0, message.Count ?? long.MaxValue);
		foreach (var market in markets)
		{
			var response = await _rest.GetSecurities(market, cancellationToken);
			foreach (var item in response.Data?.Items ?? [])
			{
				if (item?.Symbol.IsEmpty() != false)
					continue;
				var security = new SecurityMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = new()
					{
						SecurityCode = item.Symbol,
						BoardCode = market.ToBoard(),
					},
					SecurityType = item.Type.ToSecurityType(),
					Name = item.EnglishName.IsEmpty(item.TraditionalName)
						.IsEmpty(item.SimplifiedName),
					ShortName = item.EnglishName.IsEmpty(item.Symbol),
					VolumeStep = item.LotSize > 0 ? item.LotSize : null,
					Multiplier = item.LotSize > 0 ? item.LotSize : null,
				};
				if (!security.IsMatch(message, requestedTypes))
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
			if (left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveSubscription(_level1Subscriptions, message.OriginalTransactionId,
				cancellationToken);
			return;
		}
		var securityId = NormalizeSecurityId(message.SecurityId);
		var response = await _rest.GetQuotes([securityId.ToSecuId(DefaultMarket)],
			cancellationToken);
		foreach (var quote in response.Data?.Items ?? [])
			await SendQuote(message.TransactionId, securityId, quote, cancellationToken);
		if (!message.IsHistoryOnly())
			await AddSubscription(_level1Subscriptions, message.TransactionId, securityId,
				"rt", cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveSubscription(_tickSubscriptions, message.OriginalTransactionId,
				cancellationToken);
			return;
		}
		var securityId = NormalizeSecurityId(message.SecurityId);
		var count = (int)Math.Clamp(message.Count ?? 100, 1, 500);
		var response = await _rest.GetTicks(securityId.ToSecuId(DefaultMarket), 0, 0,
			count, cancellationToken);
		foreach (var tick in (response.Data?.Items ?? []).OrderBy(item => item.Time))
		{
			var time = tick.Time.ToUtc(securityId.ToMarket(DefaultMarket), CurrentTime);
			if (message.From is DateTime from && time < EnsureUtc(from))
				continue;
			if (message.To is DateTime to && time > EnsureUtc(to))
				continue;
			await SendTick(message.TransactionId, securityId, tick, cancellationToken);
		}
		if (!message.IsHistoryOnly())
			await AddSubscription(_tickSubscriptions, message.TransactionId, securityId,
				"tk", cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveSubscription(_depthSubscriptions, message.OriginalTransactionId,
				cancellationToken);
			return;
		}
		var securityId = NormalizeSecurityId(message.SecurityId);
		var response = await _rest.GetDepth(securityId.ToSecuId(DefaultMarket),
			cancellationToken);
		await SendDepth(message.TransactionId, securityId, response.Data?.LatestTime ?? 0,
			response.Data?.Items ?? [], message.MaxDepth, cancellationToken);
		if (!message.IsHistoryOnly())
			await AddSubscription(_depthSubscriptions, message.TransactionId, securityId,
				"ob", cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;
		var securityId = NormalizeSecurityId(message.SecurityId);
		var timeFrame = message.GetTimeFrame();
		var type = timeFrame.ToKlineType();
		var count = (int)Math.Clamp(message.Count ?? 200, 1, 500);
		var response = await _rest.GetKlines(securityId.ToSecuId(DefaultMarket), type, 0,
			count, cancellationToken);
		var from = message.From is DateTime fromValue ? EnsureUtc(fromValue) : (DateTime?)null;
		var to = message.To is DateTime toValue ? EnsureUtc(toValue) : (DateTime?)null;
		foreach (var candle in (response.Data?.Items ?? []).OrderBy(item => item.Time))
		{
			var openTime = candle.Time.ToUtc(securityId.ToMarket(DefaultMarket), CurrentTime);
			if (from != null && openTime < from.Value)
				continue;
			if (to != null && openTime > to.Value)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				DataType = message.DataType2,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}

	private async Task AddSubscription(
		SynchronizedDictionary<long, MarketSubscription> subscriptions, long transactionId,
		SecurityId securityId, string type, CancellationToken cancellationToken)
	{
		var market = securityId.ToMarket(DefaultMarket);
		var topic = $"{type}.{market}.{securityId.SecurityCode}";
		var isExisting = IsTopicSubscribed(topic);
		subscriptions[transactionId] = new() { SecurityId = securityId, Topic = topic };
		try
		{
			if (!isExisting)
				await _stream.Subscribe(topic, cancellationToken);
		}
		catch
		{
			subscriptions.Remove(transactionId);
			throw;
		}
	}

	private async Task RemoveSubscription(
		SynchronizedDictionary<long, MarketSubscription> subscriptions, long transactionId,
		CancellationToken cancellationToken)
	{
		if (!subscriptions.TryGetValue(transactionId, out var subscription))
			return;
		subscriptions.Remove(transactionId);
		if (!IsTopicSubscribed(subscription.Topic))
			await _stream.Unsubscribe(subscription.Topic, cancellationToken);
	}

	private bool IsTopicSubscribed(string topic)
		=> _level1Subscriptions.Values.Concat(_tickSubscriptions.Values)
			.Concat(_depthSubscriptions.Values)
			.Any(subscription => subscription.Topic.EqualsIgnoreCase(topic));

	private async ValueTask OnQuote(string topic, UsmartQuote quote,
		CancellationToken cancellationToken)
	{
		foreach (var pair in _level1Subscriptions.ToArray()
			.Where(pair => pair.Value.Topic.EqualsIgnoreCase(topic)))
			await SendQuote(pair.Key, pair.Value.SecurityId, quote, cancellationToken);
	}

	private async ValueTask OnTick(string topic, UsmartTick tick,
		CancellationToken cancellationToken)
	{
		foreach (var pair in _tickSubscriptions.ToArray()
			.Where(pair => pair.Value.Topic.EqualsIgnoreCase(topic)))
			await SendTick(pair.Key, pair.Value.SecurityId, tick, cancellationToken);
	}

	private async ValueTask OnDepth(string topic, UsmartDepthLevel[] depth,
		CancellationToken cancellationToken)
	{
		foreach (var pair in _depthSubscriptions.ToArray()
			.Where(pair => pair.Value.Topic.EqualsIgnoreCase(topic)))
			await SendDepth(pair.Key, pair.Value.SecurityId, 0, depth, null, cancellationToken);
	}

	private ValueTask SendQuote(long originalTransactionId, SecurityId securityId,
		UsmartQuote quote, CancellationToken cancellationToken)
	{
		if (quote == null)
			return default;
		var market = quote.Market.IsEmpty(securityId.ToMarket(DefaultMarket));
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = quote.LatestTime.ToUtc(market, CurrentTime),
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, quote.Open)
		.TryAdd(Level1Fields.HighPrice, quote.High)
		.TryAdd(Level1Fields.LowPrice, quote.Low)
		.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose)
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskVolume)
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.Turnover, quote.Turnover)
		.TryAdd(Level1Fields.MinPrice, quote.LowerLimit)
		.TryAdd(Level1Fields.MaxPrice, quote.UpperLimit)
		.TryAdd(Level1Fields.PriceStep, quote.PriceStep)
		.TryAdd(Level1Fields.State, quote.TradingStatus == 6
			? SecurityStates.Trading : SecurityStates.Stoped), cancellationToken);
	}

	private ValueTask SendTick(long originalTransactionId, SecurityId securityId,
		UsmartTick tick, CancellationToken cancellationToken)
	{
		if (tick == null || tick.Price <= 0 || tick.Volume <= 0)
			return default;
		var market = tick.Market.IsEmpty(securityId.ToMarket(DefaultMarket));
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			TradeStringId = $"{market}.{securityId.SecurityCode}.{tick.Sequence}",
			TradePrice = tick.Price,
			TradeVolume = tick.Volume,
			OriginSide = tick.Direction switch
			{
				1 => Sides.Buy,
				2 => Sides.Sell,
				_ => null,
			},
			ServerTime = tick.Time.ToUtc(market, CurrentTime),
		}, cancellationToken);
	}

	private ValueTask SendDepth(long originalTransactionId, SecurityId securityId,
		long time, UsmartDepthLevel[] levels, int? requestedDepth,
		CancellationToken cancellationToken)
	{
		var depth = Math.Clamp(requestedDepth ?? 10, 1, 10);
		var bids = (levels ?? []).Where(item => item?.BidPrice is > 0 && item.BidVolume is > 0)
			.OrderByDescending(item => item.BidPrice).Take(depth)
			.Select(item => new QuoteChange(item.BidPrice.Value, item.BidVolume.Value)
			{
				OrdersCount = item.BidOrders,
			}).ToArray();
		var asks = (levels ?? []).Where(item => item?.AskPrice is > 0 && item.AskVolume is > 0)
			.OrderBy(item => item.AskPrice).Take(depth)
			.Select(item => new QuoteChange(item.AskPrice.Value, item.AskVolume.Value)
			{
				OrdersCount = item.AskOrders,
			}).ToArray();
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = time.ToUtc(securityId.ToMarket(DefaultMarket), CurrentTime),
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private SecurityId NormalizeSecurityId(SecurityId securityId)
		=> new()
		{
			SecurityCode = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)),
			BoardCode = securityId.BoardCode.IsEmpty(
				securityId.ToMarket(DefaultMarket).ToBoard()),
		};

	private static DateTime EnsureUtc(DateTime time)
		=> time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
}
