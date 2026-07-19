namespace StockSharp.Bitkub;

public partial class BitkubMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: lookupMsg.SecurityId.SecurityCode.NormalizeSymbol();
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static market => market.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitkub))
				continue;
			if (!requestedSymbol.IsEmpty() &&
				!requestedSymbol.EqualsIgnoreCase(market.Symbol))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, market.IsTrading
				? SecurityStates.Trading
				: SecurityStates.Stoped), cancellationToken);
			if (--left <= 0)
				break;
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
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bitkub does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = market.Symbol });
			subscribe = AddStreamReference(market.Symbol);
		}
		try
		{
			if (subscribe)
				await PublicWebSocketClient.SubscribeAsync(market.Symbol,
					market.PairingId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bitkub does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100).Min(200).Max(1);
		var snapshot = await RestClient.GetDepthAsync(market.Symbol, depth,
			cancellationToken);
		await SendDepthAsync(market.Symbol, CurrentTime,
			ToQuotes(snapshot.Bids, false, depth),
			ToQuotes(snapshot.Asks, true, depth), mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				Depth = depth,
			});
			subscribe = AddStreamReference(market.Symbol);
		}
		try
		{
			if (subscribe)
				await PublicWebSocketClient.SubscribeAsync(market.Symbol,
					market.PairingId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var trades = await RestClient.GetTradesAsync(market.Symbol, count,
			cancellationToken);
		foreach (var trade in trades.Where(trade =>
		{
			var time = trade.Timestamp.FromBitkubTimestamp();
			return (from is null || time >= from.Value) && time <= to;
		}).OrderBy(static trade => trade.Timestamp))
			await SendPublicTradeAsync(market.Symbol, trade.Timestamp, trade.Price,
				trade.Amount, trade.Side, mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = market.Symbol });
			subscribe = AddStreamReference(market.Symbol);
		}
		try
		{
			if (subscribe)
				await PublicWebSocketClient.SubscribeAsync(market.Symbol,
					market.PairingId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MarketDefinition market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = $"{market.BaseAsset}/{market.QuoteAsset}",
			ShortName = $"{market.BaseAsset}/{market.QuoteAsset}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			VolumeStep = market.VolumeStep > 0 ? market.VolumeStep : null,
			MinVolume = market.VolumeStep > 0 ? market.VolumeStep : null,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(MarketDefinition market,
		long transactionId, CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(market.Symbol,
			cancellationToken);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.HighestBid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.LowestAsk)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.HighPrice, ticker.High24Hours)
		.TryAdd(Level1Fields.LowPrice, ticker.Low24Hours)
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		.TryAdd(Level1Fields.Change, ticker.Change)
		.TryAdd(Level1Fields.State, market.IsTrading
			? SecurityStates.Trading
			: SecurityStates.Stoped), cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				release = ReleaseStreamReference(subscription.Symbol);
		if (release)
			await PublicWebSocketClient.ReleaseAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseStreamReference(subscription.Symbol);
		if (release)
			await PublicWebSocketClient.ReleaseAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseStreamReference(subscription.Symbol);
		if (release)
			await PublicWebSocketClient.ReleaseAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask OnPublicTradesChangedAsync(string symbol,
		BitkubWebSocketChangedData data, CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
		KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
		using (_sync.EnterScope())
		{
			tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
			depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		}

		foreach (var trade in (data.Trades ?? [])
			.OrderBy(static trade => trade.Timestamp))
		{
			var tradeId = CreatePublicTradeId(trade.Timestamp, trade.Price,
				trade.Amount, trade.Side);
			if (!AddPublicTrade(symbol, tradeId))
				continue;
			foreach (var pair in tickSubscriptions)
				await SendPublicTradeAsync(symbol, trade.Timestamp, trade.Price,
					trade.Amount, trade.Side, pair.Key, cancellationToken);
		}

		if ((data.Bids?.Length ?? 0) == 0 && (data.Asks?.Length ?? 0) == 0)
			return;
		foreach (var pair in depthSubscriptions)
			await SendDepthAsync(symbol, CurrentTime,
				ToQuotes(data.Bids, false, pair.Value.Depth),
				ToQuotes(data.Asks, true, pair.Value.Depth), pair.Key,
				cancellationToken);
		await SendBookLevel1Async(symbol, ToQuotes(data.Bids, false, 1),
			ToQuotes(data.Asks, true, 1), cancellationToken);
	}

	private async ValueTask OnPublicDepthChangedAsync(string symbol,
		BitkubWebSocketDepth depth, CancellationToken cancellationToken)
	{
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		foreach (var pair in subscriptions)
			await SendDepthAsync(symbol, CurrentTime,
				ToQuotes(depth.Bids, false, pair.Value.Depth),
				ToQuotes(depth.Asks, true, pair.Value.Depth), pair.Key,
				cancellationToken);
		await SendBookLevel1Async(symbol, ToQuotes(depth.Bids, false, 1),
			ToQuotes(depth.Asks, true, 1), cancellationToken);
	}

	private async ValueTask OnPublicTickerChangedAsync(string symbol,
		BitkubTicker ticker, CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = pair.Key,
			}
			.TryAdd(Level1Fields.BestBidPrice, ticker.HighestBid)
			.TryAdd(Level1Fields.BestBidVolume, ticker.HighestBidSize)
			.TryAdd(Level1Fields.BestAskPrice, ticker.LowestAsk)
			.TryAdd(Level1Fields.BestAskVolume, ticker.LowestAskSize)
			.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
			.TryAdd(Level1Fields.HighPrice, ticker.High24Hours)
			.TryAdd(Level1Fields.LowPrice, ticker.Low24Hours)
			.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
			.TryAdd(Level1Fields.Change, ticker.Change), cancellationToken);
	}

	private async ValueTask SendBookLevel1Async(string symbol,
		QuoteChange[] bids, QuoteChange[] asks,
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = pair.Key,
			}
			.TryAdd(Level1Fields.BestBidPrice,
				bids.Length == 0 ? null : bids[0].Price)
			.TryAdd(Level1Fields.BestBidVolume,
				bids.Length == 0 ? null : bids[0].Volume)
			.TryAdd(Level1Fields.BestAskPrice,
				asks.Length == 0 ? null : asks[0].Price)
			.TryAdd(Level1Fields.BestAskVolume,
				asks.Length == 0 ? null : asks[0].Volume), cancellationToken);
	}

	private ValueTask SendDepthAsync(string symbol, DateTime serverTime,
		QuoteChange[] bids, QuoteChange[] asks, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string symbol, long timestamp,
		decimal price, decimal amount, BitkubSides side, long transactionId,
		CancellationToken cancellationToken)
	{
		var tradeId = CreatePublicTradeId(timestamp, price, amount, side);
		_ = AddPublicTrade(symbol, tradeId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = timestamp.FromBitkubTimestamp(),
			OriginalTransactionId = transactionId,
			TradeStringId = tradeId,
			TradePrice = price,
			TradeVolume = amount,
			OriginSide = side.ToStockSharp(),
		}, cancellationToken);
	}

	private static string CreatePublicTradeId(long timestamp, decimal price,
		decimal amount, BitkubSides side)
		=> string.Join('-',
			timestamp.ToString(CultureInfo.InvariantCulture),
			price.ToString(CultureInfo.InvariantCulture),
			amount.ToString(CultureInfo.InvariantCulture),
			side == BitkubSides.Buy ? "B" : "S");

	private static QuoteChange[] ToQuotes(IEnumerable<BitkubBookLevel> levels,
		bool isAsk, int depth)
	{
		var quotes = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Amount > 0)
			.Select(static level => new QuoteChange(level.Price, level.Amount));
		return [.. (isAsk
			? quotes.OrderBy(static quote => quote.Price)
			: quotes.OrderByDescending(static quote => quote.Price)).Take(depth)];
	}

	private static QuoteChange[] ToQuotes(
		IEnumerable<BitkubWebSocketDepthLevel> levels, bool isAsk, int depth)
	{
		var quotes = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.BaseVolume > 0)
			.Select(static level => new QuoteChange(level.Price, level.BaseVolume));
		return [.. (isAsk
			? quotes.OrderBy(static quote => quote.Price)
			: quotes.OrderByDescending(static quote => quote.Price)).Take(depth)];
	}

	private static QuoteChange[] ToQuotes(IEnumerable<BitkubWebSocketOrder> levels,
		bool isAsk, int depth)
	{
		var quotes = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Amount > 0)
			.Select(static level => new QuoteChange(level.Price, level.Amount));
		return [.. (isAsk
			? quotes.OrderBy(static quote => quote.Price)
			: quotes.OrderByDescending(static quote => quote.Price)).Take(depth)];
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
