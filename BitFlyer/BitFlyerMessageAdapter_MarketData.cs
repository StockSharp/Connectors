namespace StockSharp.BitFlyer;

public partial class BitFlyerMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: lookupMsg.SecurityId.SecurityCode.NormalizeProductCode();
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static value => value.ProductCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitFlyer))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.ProductCode) &&
				!requestedCode.CompactProductCode().EqualsIgnoreCase(
					market.ProductCode.CompactProductCode()))
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
			}.TryAdd(Level1Fields.State, market.State.ToStockSharp()),
				cancellationToken);
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
				"bitFlyer does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		var ticker = await RestClient.GetTickerAsync(new()
		{
			ProductCode = market.ProductCode,
		}, cancellationToken);
		await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Ticker, market.ProductCode);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				ProductCode = market.ProductCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeTickerAsync(market.ProductCode,
					cancellationToken);
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
				"bitFlyer does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 50).Min(500).Max(1);
		var board = await RestClient.GetBoardAsync(new()
		{
			ProductCode = market.ProductCode,
		}, cancellationToken);
		ApplyBoard(market.ProductCode, board, true);
		await SendCurrentBoardAsync(market.ProductCode, depth,
			mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Board, market.ProductCode);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				ProductCode = market.ProductCode,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeBoardAsync(market.ProductCode,
					cancellationToken);
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
		var count = (mdMsg.Count ?? 100).Min(500).Max(1).To<int>();
		var executions = await RestClient.GetExecutionsAsync(new()
		{
			ProductCode = market.ProductCode,
			Count = count,
		}, cancellationToken);
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		foreach (var execution in (executions ?? [])
			.Where(value => value is not null &&
				value.ExecutionDate.ToUtcDateTime(DateTime.MinValue) >=
					(from ?? DateTime.MinValue) &&
				value.ExecutionDate.ToUtcDateTime(DateTime.MaxValue) <= to)
			.OrderBy(static value => value.Id))
			await SendPublicExecutionAsync(market.ProductCode, execution,
				mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Executions, market.ProductCode);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				ProductCode = market.ProductCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeExecutionsAsync(market.ProductCode,
					cancellationToken);
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
	{
		var name = $"{market.BaseAsset}/{market.QuoteAsset}" +
			(market.Type == BitFlyerMarketTypes.Fx ? " CFD" : string.Empty);
		var message = new SecurityMessage
		{
			SecurityId = market.ProductCode.ToStockSharp(),
			Name = name,
			ShortName = name,
			SecurityType = market.Type == BitFlyerMarketTypes.Fx
				? SecurityTypes.Future
				: SecurityTypes.CryptoCurrency,
			UnderlyingSecurityType = market.Type == BitFlyerMarketTypes.Fx
				? SecurityTypes.CryptoCurrency
				: null,
			Currency = market.QuoteAsset.ToCurrency(),
			OriginalTransactionId = originalTransactionId,
		};
		return market.Type == BitFlyerMarketTypes.Fx
			? message.TryFillUnderlyingId(
				$"{market.BaseAsset}_{market.QuoteAsset}")
			: message;
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Ticker, subscription.ProductCode));
		if (release)
			await SocketClient.UnsubscribeTickerAsync(subscription.ProductCode,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Board, subscription.ProductCode));
		if (release)
			await SocketClient.UnsubscribeBoardAsync(subscription.ProductCode,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Executions, subscription.ProductCode));
		if (release)
			await SocketClient.UnsubscribeExecutionsAsync(subscription.ProductCode,
				cancellationToken);
	}

	private async ValueTask OnSocketTickerAsync(BitFlyerTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.ProductCode.IsEmpty() != false)
			return;
		var market = GetMarket(ticker.ProductCode);
		using (_sync.EnterScope())
			market.State = ticker.State;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.ProductCode.EqualsIgnoreCase(market.ProductCode))];
		foreach (var pair in subscriptions)
			await SendTickerAsync(ticker, pair.Key, cancellationToken);
	}

	private async ValueTask OnSocketExecutionsAsync(string productCode,
		BitFlyerPublicExecution[] executions,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(productCode);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.ProductCode.EqualsIgnoreCase(market.ProductCode))];
		foreach (var execution in executions ?? [])
		{
			if (execution is null || !AddPublicTrade(market.ProductCode,
				execution.Id))
				continue;
			foreach (var pair in subscriptions)
				await SendPublicExecutionAsync(market.ProductCode, execution,
					pair.Key, cancellationToken, false);
		}
	}

	private async ValueTask OnSocketBoardAsync(string productCode,
		BitFlyerBoard board, bool isSnapshot,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(productCode);
		if (!ApplyBoard(market.ProductCode, board, isSnapshot))
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.ProductCode.EqualsIgnoreCase(market.ProductCode))];
		foreach (var pair in subscriptions)
			await SendCurrentBoardAsync(market.ProductCode, pair.Value.Depth,
				pair.Key, cancellationToken);
	}

	private ValueTask SendTickerAsync(BitFlyerTicker ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null || ticker.ProductCode.IsEmpty())
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.ProductCode.ToStockSharp(),
			ServerTime = ticker.Timestamp.ToUtcDateTime(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize)
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk)
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize)
		.TryAdd(Level1Fields.BidsVolume, ticker.TotalBidDepth)
		.TryAdd(Level1Fields.AsksVolume, ticker.TotalAskDepth)
		.TryAdd(Level1Fields.Volume, ticker.ProductVolume)
		.TryAdd(Level1Fields.State, ticker.State.ToStockSharp()),
			cancellationToken);
	}

	private ValueTask SendPublicExecutionAsync(string productCode,
		BitFlyerPublicExecution execution, long transactionId,
		CancellationToken cancellationToken, bool addToDeduplication = true)
	{
		if (execution is null || execution.Id <= 0 || execution.Price <= 0 ||
			execution.Size <= 0)
			return default;
		if (addToDeduplication && !AddPublicTrade(productCode, execution.Id))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = productCode.ToStockSharp(),
			ServerTime = execution.ExecutionDate.ToUtcDateTime(CurrentTime),
			OriginalTransactionId = transactionId,
			TradeId = execution.Id,
			TradePrice = execution.Price,
			TradeVolume = execution.Size,
			OriginSide = execution.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private bool ApplyBoard(string productCode, BitFlyerBoard board,
		bool isSnapshot)
	{
		if (board is null)
			return false;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(productCode, out var state))
				_books[productCode] = state = new();
			if (isSnapshot)
			{
				state.Bids.Clear();
				state.Asks.Clear();
				state.IsInitialized = true;
			}
			else if (!state.IsInitialized)
				return false;
			ApplyLevels(state.Bids, board.Bids);
			ApplyLevels(state.Asks, board.Asks);
			return true;
		}
	}

	private static void ApplyLevels(IDictionary<decimal, decimal> target,
		IEnumerable<BitFlyerBookLevel> levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level is null || level.Price <= 0)
				continue;
			if (level.Size <= 0)
				target.Remove(level.Price);
			else
				target[level.Price] = level.Size;
		}
	}

	private ValueTask SendCurrentBoardAsync(string productCode, int depth,
		long transactionId, CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(productCode, out var state) ||
				!state.IsInitialized)
				return default;
			bids = [.. state.Bids.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			asks = [.. state.Asks.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
		}
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = productCode.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
