namespace StockSharp.Chainflip;

public partial class ChainflipMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		ChainflipMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Chainflip))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode) &&
				!requestedCode.EqualsIgnoreCase(market.Key) &&
				!requestedCode.EqualsIgnoreCase(market.BaseAsset.Key))
				continue;
			var security = CreateSecurity(market,
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, SecurityStates.Trading),
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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Chainflip State Chain does not expose historical Level1 " +
					"changes.");
		var market = GetMarket(mdMsg.SecurityId);
		var prices = await StateClient.GetPricesAsync(market, null,
			cancellationToken);
		await SendLevel1Async(market, prices, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
			};
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
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Chainflip State Chain does not expose historical order " +
					"books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? OrderBookDepth).Max(1)
			.Min(OrderBookDepth);
		var book = await StateClient.GetOrderBookAsync(market, depth,
			cancellationToken);
		await SendDepthAsync(market, book, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				Depth = depth,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveMarketSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.IsHistoryOnly())
			throw new NotSupportedException(
				"Chainflip public RPC exposes real-time block fills but not " +
					"a historical trade index.");
		var market = GetMarket(mdMsg.SecurityId);
		var best = await StateClient.GetBestBlockNumberAsync(
			cancellationToken);
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Count == 0)
				_lastFillBlock = Math.Max(0, best - InitialTickBlocks);
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				To = mdMsg.To?.ToUniversalTime(),
				Maximum = GetSubscriptionMaximum(mdMsg.Count),
			};
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(ChainflipMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.BaseAsset.Symbol} ({market.BaseAsset.Chain})/" +
				$"{market.QuoteAsset.Symbol} ({market.QuoteAsset.Chain})",
			ShortName = $"{market.BaseAsset.Symbol}/" +
				market.QuoteAsset.Symbol,
			Class = "CROSS-CHAIN-JIT-AMM",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteAsset.Symbol.ToCurrency(),
			PriceStep = 0.00000001m,
			VolumeStep = market.BaseAsset.Decimals.GetUnitStep(),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.BaseAsset.Symbol);

	private async ValueTask SendLevel1Async(ChainflipMarket market,
		(decimal Bid, decimal Ask) prices, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var fingerprint = new Level1Fingerprint(prices.Bid, prices.Ask);
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(target,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[target] = fingerprint;
		}
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, prices.Bid)
		.TryAdd(Level1Fields.BestAskPrice, prices.Ask)
		.TryAdd(Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private async ValueTask SendDepthAsync(ChainflipMarket market,
		ChainflipOrderBook book, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var bids = book.Bids
			.OrderByDescending(static level => level.Price)
			.Select(static level => new QuoteChange(
				level.Price, level.Volume))
			.ToArray();
		var asks = book.Asks
			.OrderBy(static level => level.Price)
			.Select(static level => new QuoteChange(
				level.Price, level.Volume))
			.ToArray();
		var fingerprint = string.Join('|', bids.Select(static item =>
			$"{item.Price.ToString(CultureInfo.InvariantCulture)}:" +
				item.Volume.ToString(CultureInfo.InvariantCulture))) + "/" +
			string.Join('|', asks.Select(static item =>
				$"{item.Price.ToString(CultureInfo.InvariantCulture)}:" +
					item.Volume.ToString(CultureInfo.InvariantCulture)));
		using (_sync.EnterScope())
		{
			if (!isForced && _depthFingerprints.TryGetValue(target,
				out var previous) && previous == fingerprint)
				return;
			_depthFingerprints[target] = fingerprint;
		}
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private async ValueTask<bool> SendTradeAsync(ChainflipTrade trade,
		long target, CancellationToken cancellationToken)
	{
		if (!TryTrackDelivery(target, "T:" + trade.Id))
			return false;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Market.ToStockSharp(),
			ServerTime = trade.Time,
			OriginalTransactionId = target,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side,
		}, cancellationToken);
		return true;
	}

	private async ValueTask PollMarketDataAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, Level1Subscription>[] level1;
		KeyValuePair<long, DepthSubscription>[] depths;
		var hasTicks = false;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions];
			depths = [.. _depthSubscriptions];
			hasTicks = _tickSubscriptions.Count > 0;
		}

		foreach (var item in level1)
			await PollOneAsync(async token =>
			{
				var prices = await StateClient.GetPricesAsync(
					item.Value.Market, null, token);
				await SendLevel1Async(item.Value.Market, prices, item.Key,
					false, token);
			}, cancellationToken);

		foreach (var item in depths)
			await PollOneAsync(async token =>
			{
				var book = await StateClient.GetOrderBookAsync(
					item.Value.Market, item.Value.Depth, token);
				await SendDepthAsync(item.Value.Market, book, item.Key,
					false, token);
			}, cancellationToken);

		if (hasTicks)
			await PollOneAsync(PollTradesAsync, cancellationToken);
	}

	private async ValueTask PollTradesAsync(
		CancellationToken cancellationToken)
	{
		var best = await StateClient.GetBestBlockNumberAsync(
			cancellationToken);
		long first;
		IReadOnlyDictionary<string, ChainflipMarket> markets;
		using (_sync.EnterScope())
		{
			first = _lastFillBlock + 1;
			markets = new Dictionary<string, ChainflipMarket>(
				_marketsByKey, StringComparer.OrdinalIgnoreCase);
		}
		if (first > best)
			return;
		var last = Math.Min(best, first + MaxBlocksPerPoll - 1);

		for (var blockNumber = first; blockNumber <= last; blockNumber++)
		{
			var block = await StateClient.GetBlockTradesAsync(blockNumber,
				markets, cancellationToken);

			foreach (var trade in block.Trades)
				await DistributeTradeAsync(trade, cancellationToken);

			using (_sync.EnterScope())
				_lastFillBlock = blockNumber;
		}
	}

	private async ValueTask DistributeTradeAsync(ChainflipTrade trade,
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, TickSubscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Market.Key.EqualsIgnoreCase(trade.Market.Key))];

		foreach (var target in targets)
		{
			if (target.Value.To is DateTime end && trade.Time > end)
				continue;
			var sent = await SendTradeAsync(trade, target.Key,
				cancellationToken);
			var finished = false;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.TryGetValue(target.Key,
					out var active))
					continue;
				if (sent)
					active.Delivered++;
				finished = active.Delivered >= active.Maximum;
				if (finished)
					RemoveMarketSubscriptionNoLock(target.Key);
			}
			if (finished)
				await SendSubscriptionFinishedAsync(target.Key,
					cancellationToken);
		}
	}

	private async ValueTask PollOneAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private bool TryTrackDelivery(long target, string identity)
	{
		var key = new DeliveryKey(target, identity);
		using (_sync.EnterScope())
		{
			if (!_seenMarketData.Add(key))
				return false;
			_marketDataDeliveryOrder.Enqueue(key);

			while (_marketDataDeliveryOrder.Count > _maximumDeliveryKeys)
				_seenMarketData.Remove(_marketDataDeliveryOrder.Dequeue());

			return true;
		}
	}

	private void RemoveMarketSubscriptionNoLock(long target)
	{
		_level1Subscriptions.Remove(target);
		_depthSubscriptions.Remove(target);
		_tickSubscriptions.Remove(target);
		_level1Fingerprints.Remove(target);
		_depthFingerprints.Remove(target);
		_seenMarketData.RemoveWhere(key => key.SubscriptionId == target);
		var retained = _marketDataDeliveryOrder.Where(
			_seenMarketData.Contains).ToArray();
		_marketDataDeliveryOrder.Clear();

		foreach (var key in retained)
			_marketDataDeliveryOrder.Enqueue(key);
	}

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? int.MaxValue
			: count.Value.Min(int.MaxValue).Max(1).To<int>();

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
