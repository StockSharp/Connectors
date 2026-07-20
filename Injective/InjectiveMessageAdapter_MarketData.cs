namespace StockSharp.Injective;

public partial class InjectiveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in GetMarkets().OrderBy(static market => market.Code,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Injective))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Code,
					StringComparison.OrdinalIgnoreCase))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
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
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Injective does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
			});
		try
		{
			await AcquireStreamAsync(market, "depth", cancellationToken);
			await AcquireStreamAsync(market, "trades", cancellationToken);
			if (market.Kind == InjectiveMarketKinds.Derivative)
				await AcquireStreamAsync(market, "oracle", cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			await ReleaseStreamAsync(market, "oracle", cancellationToken);
			await ReleaseStreamAsync(market, "trades", cancellationToken);
			await ReleaseStreamAsync(market, "depth", cancellationToken);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Injective does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var snapshot = await RestClient.GetOrderBookAsync(market, depth,
			cancellationToken);
		await SendDepthAsync(market, snapshot?.Orderbook, mdMsg.TransactionId,
			depth, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
				Depth = depth,
			});
		try
		{
			await AcquireStreamAsync(market, "depth", cancellationToken);
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
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Injective trade start time cannot be later than end time.");
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var trades = await RestClient.GetTradesAsync(market, null, from, to,
			count, cancellationToken);
		foreach (var trade in trades.Where(static item => item is not null)
			.OrderBy(static item => item.ExecutedAt).TakeLast(count))
			await SendTradeAsync(market, trade, mdMsg.TransactionId, false,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
			});
		try
		{
			await AcquireStreamAsync(market, "trades", cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToChartResolution();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Injective candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var history = await RestClient.GetCandlesAsync(market, timeFrame, from,
			to, count, cancellationToken);
		var lastOpen = await SendCandlesAsync(market, history, timeFrame,
			mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpen,
				NextPollTime = DateTime.UtcNow + GetCandlePollInterval(timeFrame),
			});
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendLevel1SnapshotAsync(InjectiveMarket market,
		long transactionId, CancellationToken cancellationToken)
	{
		var summariesTask = RestClient.GetMarketSummariesAsync(market.Kind,
			cancellationToken).AsTask();
		var depthTask = RestClient.GetOrderBookAsync(market, 1,
			cancellationToken).AsTask();
		await Task.WhenAll(summariesTask, depthTask);
		var summary = (await summariesTask ?? []).FirstOrDefault(item =>
			item?.MarketId.Equals(market.MarketId,
				StringComparison.OrdinalIgnoreCase) == true);
		var book = (await depthTask)?.Orderbook;
		var bid = GetLevel(market, book?.Buys?.FirstOrDefault());
		var ask = GetLevel(market, book?.Sells?.FirstOrDefault());
		var time = GetBookTime(book);
		if (summary?.Price > 0)
			SetLastPrice(market.MarketId, summary.Price);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToInjectiveSecurityId(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.PriceStep)
		.TryAdd(Level1Fields.VolumeStep, market.VolumeStep)
		.TryAdd(Level1Fields.State, IsMarketActive(market.Status)
			? SecurityStates.Trading : SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice, summary?.Price)
		.TryAdd(Level1Fields.OpenPrice, summary?.Open)
		.TryAdd(Level1Fields.HighPrice, summary?.High)
		.TryAdd(Level1Fields.LowPrice, summary?.Low)
		.TryAdd(Level1Fields.Volume, summary?.Volume)
		.TryAdd(Level1Fields.Change, summary?.Change)
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Volume), cancellationToken);
	}

	private ValueTask SendDepthAsync(InjectiveMarket market,
		InjectiveOrderBook book, long transactionId, int depth,
		CancellationToken cancellationToken)
	{
		if (book is null)
			throw new InvalidDataException(
				"Injective returned no order-book snapshot.");
		var bids = (book.Buys ?? []).Take(depth)
			.Select(level => ToQuote(market, level)).ToArray();
		var asks = (book.Sells ?? []).Take(depth)
			.Select(level => ToQuote(market, level)).ToArray();
		var time = GetBookTime(book);
		UpdateServerTime(time, book.Height > 0 ? book.Height : null);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToInjectiveSecurityId(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(InjectiveMarket market,
		InjectiveTrade trade, long transactionId, bool isAccount,
		CancellationToken cancellationToken)
	{
		if (trade is null || trade.TradeId.IsEmpty())
			return default;
		var time = trade.ExecutedAt > 0
			? trade.ExecutedAt.FromInjectiveMilliseconds() : ServerTime;
		var price = market.Kind == InjectiveMarketKinds.Spot
			? market.ToPrice(trade.Price?.Price)
			: market.ToPrice(trade.PositionDelta?.ExecutionPrice);
		var volume = market.Kind == InjectiveMarketKinds.Spot
			? market.ToQuantity(trade.Price?.Quantity)
			: market.ToQuantity(trade.PositionDelta?.ExecutionQuantity);
		UpdateServerTime(time);
		SetLastPrice(market.MarketId, price);
		long? numericId = long.TryParse(trade.TradeId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var id) ? id : null;
		var message = new ExecutionMessage
		{
			DataTypeEx = isAccount ? DataType.Transactions : DataType.Ticks,
			SecurityId = market.ToInjectiveSecurityId(),
			ServerTime = time,
			TradeId = numericId,
			TradeStringId = trade.TradeId,
			TradePrice = price,
			TradeVolume = volume,
			OriginSide = (trade.TradeDirection ??
				trade.PositionDelta?.TradeDirection).ToStockSharpSide(),
			OriginalTransactionId = transactionId,
		};
		if (isAccount)
		{
			message.PortfolioName = PortfolioName;
			message.DepoName = _subaccountId;
			message.OrderStringId = trade.OrderHash;
			message.TransactionId = ParseTransactionId(trade.Cid);
			message.Commission = trade.Fee.IsEmpty()
				? null : market.ToQuote(trade.Fee);
			message.CommissionCurrency = market.QuoteSymbol;
		}
		return SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<DateTime> SendCandlesAsync(InjectiveMarket market,
		InjectiveChartHistory history, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		if (history is null)
			throw new InvalidDataException("Injective returned no candle history.");
		if (!history.ErrorMessage.IsEmpty() ||
			history.Status?.Equals("error",
				StringComparison.OrdinalIgnoreCase) == true)
			throw new InvalidDataException(
				"Injective candle request failed: " + history.ErrorMessage);
		var count = new[]
		{
			history.Times?.Length ?? 0, history.Opens?.Length ?? 0,
			history.Highs?.Length ?? 0, history.Lows?.Length ?? 0,
			history.Closes?.Length ?? 0, history.Volumes?.Length ?? 0,
		}.Min();
		var lastOpen = default(DateTime);
		for (var index = 0; index < count; index++)
		{
			var openTime = history.Times[index].FromInjectiveSeconds();
			lastOpen = openTime;
			var closeTime = openTime + timeFrame;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = market.ToInjectiveSecurityId(),
				OpenTime = openTime,
				CloseTime = closeTime,
				OpenPrice = history.Opens[index],
				HighPrice = history.Highs[index],
				LowPrice = history.Lows[index],
				ClosePrice = history.Closes[index],
				TotalVolume = history.Volumes[index],
				TypedArg = timeFrame,
				OriginalTransactionId = transactionId,
				State = closeTime <= ServerTime
					? CandleStates.Finished : CandleStates.Active,
			}, cancellationToken);
		}
		return lastOpen;
	}

	private async ValueTask PollCandleAsync(CandleSubscription subscription,
		CancellationToken cancellationToken)
	{
		var history = await RestClient.GetCandlesAsync(subscription.Market,
			subscription.TimeFrame, null, ServerTime, 2, cancellationToken);
		var times = history?.Times ?? [];
		for (var index = 0; index < times.Length; index++)
		{
			var openTime = times[index].FromInjectiveSeconds();
			if (openTime < subscription.LastOpenTime)
				continue;
			await SendOutMessageAsync(CreateCandle(subscription.Market, history,
				subscription.TimeFrame, subscription.TransactionId, index),
				cancellationToken);
			if (openTime > subscription.LastOpenTime)
				subscription.LastOpenTime = openTime;
		}
	}

	private static TimeFrameCandleMessage CreateCandle(InjectiveMarket market,
		InjectiveChartHistory history, TimeSpan timeFrame, long transactionId,
		int index)
	{
		if (history.Times is null || history.Opens is null ||
			history.Highs is null || history.Lows is null ||
			history.Closes is null || history.Volumes is null || index < 0 ||
			index >= history.Times.Length || index >= history.Opens.Length ||
			index >= history.Highs.Length || index >= history.Lows.Length ||
			index >= history.Closes.Length || index >= history.Volumes.Length)
			throw new InvalidDataException(
				"Injective returned misaligned candle arrays.");
		var openTime = history.Times[index].FromInjectiveSeconds();
		var closeTime = openTime + timeFrame;
		return new()
		{
			SecurityId = market.ToInjectiveSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = history.Opens[index],
			HighPrice = history.Highs[index],
			LowPrice = history.Lows[index],
			ClosePrice = history.Closes[index],
			TotalVolume = history.Volumes[index],
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= DateTime.UtcNow
				? CandleStates.Finished : CandleStates.Active,
		};
	}

	private async ValueTask OnDepthAsync(InjectiveDepthUpdate update,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(update?.MarketId);
		if (market is null || update.Orderbook is null)
			return;
		DepthSubscription[] depthSubscriptions;
		MarketSubscription[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			depthSubscriptions = [.. _depthSubscriptions.Values.Where(item =>
				item.Market.MarketId.Equals(market.MarketId,
					StringComparison.OrdinalIgnoreCase))];
			level1Subscriptions = [.. _level1Subscriptions.Values.Where(item =>
				item.Market.MarketId.Equals(market.MarketId,
					StringComparison.OrdinalIgnoreCase))];
		}
		foreach (var subscription in depthSubscriptions)
			await SendDepthAsync(market, update.Orderbook,
				subscription.TransactionId, subscription.Depth, cancellationToken);
		var bid = GetLevel(market, update.Orderbook.Buys?.FirstOrDefault());
		var ask = GetLevel(market, update.Orderbook.Sells?.FirstOrDefault());
		var time = GetBookTime(update.Orderbook);
		foreach (var subscription in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToInjectiveSecurityId(),
				ServerTime = time,
				OriginalTransactionId = subscription.TransactionId,
			}
			.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
			.TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
			.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
			.TryAdd(Level1Fields.BestAskVolume, ask?.Volume), cancellationToken);
	}

	private async ValueTask OnTradeAsync(InjectiveTradeUpdate update,
		CancellationToken cancellationToken)
	{
		var trade = update?.Trade;
		var market = GetMarket(trade?.MarketId);
		if (market is null || trade?.TradeId.IsEmpty() != false)
			return;
		MarketSubscription[] tickSubscriptions;
		MarketSubscription[] level1Subscriptions;
		long[] orderSubscriptions;
		var isAccount = !trade.SubaccountId.IsEmpty() &&
			trade.SubaccountId.Equals(_subaccountId,
				StringComparison.OrdinalIgnoreCase);
		using (_sync.EnterScope())
		{
			var isMarketNew = _seenTrades.Add("market:" + update.Kind + ':' +
				trade.TradeId);
			var isAccountNew = isAccount && _seenTrades.Add("account:" +
				update.Kind + ':' + trade.TradeId);
			if (!isMarketNew && !isAccountNew)
				return;
			tickSubscriptions = isMarketNew
				? [.. _tickSubscriptions.Values.Where(item =>
					item.Market.MarketId.Equals(market.MarketId,
						StringComparison.OrdinalIgnoreCase))] : [];
			level1Subscriptions = isMarketNew
				? [.. _level1Subscriptions.Values.Where(item =>
					item.Market.MarketId.Equals(market.MarketId,
						StringComparison.OrdinalIgnoreCase))] : [];
			orderSubscriptions = isAccountNew
				? [.. _orderSubscriptions.Keys] : [];
			if (_seenTrades.Count > 32768)
				_seenTrades.Clear();
		}
		foreach (var subscription in tickSubscriptions)
			await SendTradeAsync(market, trade, subscription.TransactionId,
				false, cancellationToken);
		var price = market.Kind == InjectiveMarketKinds.Spot
			? market.ToPrice(trade.Price?.Price)
			: market.ToPrice(trade.PositionDelta?.ExecutionPrice);
		var volume = market.Kind == InjectiveMarketKinds.Spot
			? market.ToQuantity(trade.Price?.Quantity)
			: market.ToQuantity(trade.PositionDelta?.ExecutionQuantity);
		var time = trade.ExecutedAt > 0
			? trade.ExecutedAt.FromInjectiveMilliseconds() : ServerTime;
		foreach (var subscription in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToInjectiveSecurityId(),
				ServerTime = time,
				OriginalTransactionId = subscription.TransactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice, price)
			.TryAdd(Level1Fields.LastTradeVolume, volume)
			.TryAdd(Level1Fields.LastTradeTime, time), cancellationToken);
		foreach (var transactionId in orderSubscriptions)
			await SendTradeAsync(market, trade, transactionId, true,
				cancellationToken);
	}

	private async ValueTask OnOraclePriceAsync(InjectiveOraclePrice update,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(update?.MarketId);
		if (market is null || update.Price.IsEmpty())
			return;
		var price = update.Price.ParseInjectiveDecimal("oracle price");
		SetLastPrice(market.MarketId, price);
		var time = update.Timestamp > 0
			? update.Timestamp.FromInjectiveMilliseconds() : ServerTime;
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values.Where(item =>
				item.Market.MarketId.Equals(market.MarketId,
					StringComparison.OrdinalIgnoreCase))];
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToInjectiveSecurityId(),
				ServerTime = time,
				OriginalTransactionId = subscription.TransactionId,
			}.TryAdd(Level1Fields.Index, price), cancellationToken);
	}

	private async ValueTask AcquireStreamAsync(InjectiveMarket market,
		string stream, CancellationToken cancellationToken)
	{
		var key = StreamKey(market, stream);
		var subscribe = false;
		using (_sync.EnterScope())
			subscribe = AddReference(_streamReferences, key);
		if (!subscribe)
			return;
		try
		{
			switch (stream)
			{
				case "depth":
					await GrpcClient.SubscribeDepthAsync(market,
						cancellationToken);
					break;
				case "trades":
					await GrpcClient.SubscribeTradesAsync(market,
						cancellationToken);
					break;
				case "oracle":
					await GrpcClient.SubscribeOracleAsync(market,
						cancellationToken);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(stream), stream,
						null);
			}
		}
		catch
		{
			using (_sync.EnterScope())
				ReleaseReference(_streamReferences, key);
			throw;
		}
	}

	private async ValueTask ReleaseStreamAsync(InjectiveMarket market,
		string stream, CancellationToken cancellationToken)
	{
		if (market is null)
			return;
		var unsubscribe = false;
		using (_sync.EnterScope())
			unsubscribe = ReleaseReference(_streamReferences,
				StreamKey(market, stream));
		if (!unsubscribe)
			return;
		switch (stream)
		{
			case "depth":
				await GrpcClient.UnsubscribeDepthAsync(market, cancellationToken);
				break;
			case "trades":
				await GrpcClient.UnsubscribeTradesAsync(market, cancellationToken);
				break;
			case "oracle":
				await GrpcClient.UnsubscribeOracleAsync(market, cancellationToken);
				break;
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription;
		using (_sync.EnterScope())
			_level1Subscriptions.Remove(transactionId, out subscription);
		if (subscription is null)
			return;
		if (subscription.Market.Kind == InjectiveMarketKinds.Derivative)
			await ReleaseStreamAsync(subscription.Market, "oracle",
				cancellationToken);
		await ReleaseStreamAsync(subscription.Market, "trades",
			cancellationToken);
		await ReleaseStreamAsync(subscription.Market, "depth",
			cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription;
		using (_sync.EnterScope())
			_depthSubscriptions.Remove(transactionId, out subscription);
		if (subscription is not null)
			await ReleaseStreamAsync(subscription.Market, "depth",
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription;
		using (_sync.EnterScope())
			_tickSubscriptions.Remove(transactionId, out subscription);
		if (subscription is not null)
			await ReleaseStreamAsync(subscription.Market, "trades",
				cancellationToken);
	}

	private void SetLastPrice(string marketId, decimal price)
	{
		if (price <= 0)
			return;
		using (_sync.EnterScope())
			_lastPrices[marketId] = price;
	}

	private decimal? GetLastPrice(string marketId)
	{
		using (_sync.EnterScope())
			return _lastPrices.TryGetValue(marketId, out var price)
				? price : null;
	}

	private static (decimal Price, decimal Volume)? GetLevel(
		InjectiveMarket market, InjectivePriceLevel level)
		=> level is null ? null :
			(market.ToPrice(level.Price), market.ToQuantity(level.Quantity));

	private static QuoteChange ToQuote(InjectiveMarket market,
		InjectivePriceLevel level)
	{
		var value = GetLevel(market, level) ?? throw new InvalidDataException(
			"Injective returned an empty order-book level.");
		if (value.Price <= 0 || value.Volume < 0)
			throw new InvalidDataException(
				"Injective returned an invalid order-book level.");
		return new(value.Price, value.Volume);
	}

	private DateTime GetBookTime(InjectiveOrderBook book)
	{
		var time = book?.Timestamp > 0
			? book.Timestamp.FromInjectiveMilliseconds() : ServerTime;
		UpdateServerTime(time, book?.Height > 0 ? book.Height : null);
		return time;
	}

	private static bool IsMarketActive(string status)
		=> status.EqualsIgnoreCase("active") ||
			status.EqualsIgnoreCase("launched");

	private static SecurityMessage CreateSecurity(InjectiveMarket market,
		long transactionId)
	{
		var isSpot = market.Kind == InjectiveMarketKinds.Spot;
		var message = new SecurityMessage
		{
			SecurityId = market.ToInjectiveSecurityId(),
			Name = market.Ticker,
			ShortName = market.Code,
			Class = isSpot ? "SPOT" : market.IsPerpetual
				? "PERPETUAL" : "FUTURE",
			SecurityType = isSpot
				? SecurityTypes.CryptoCurrency : SecurityTypes.Future,
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = market.VolumeStep,
			Multiplier = 1m,
			ExpiryDate = market.ExpiryDate,
			OriginalTransactionId = transactionId,
		};
		if (market.QuoteSymbol.EqualsIgnoreCase("USD") ||
			market.QuoteSymbol.EqualsIgnoreCase("USDT") ||
			market.QuoteSymbol.EqualsIgnoreCase("USDC"))
			message.Currency = CurrencyTypes.USD;
		if (!isSpot)
			message.TryFillUnderlyingId(market.BaseSymbol.ToUpperInvariant());
		return message;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.ToUniversalTime())
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit;
	}
}
