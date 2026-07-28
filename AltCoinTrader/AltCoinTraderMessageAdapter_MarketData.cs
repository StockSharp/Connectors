namespace StockSharp.AltCoinTrader;

public partial class AltCoinTraderMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();

		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol =
			lookupMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetMarket(lookupMsg.SecurityId).SecurityCode;
		AltCoinTraderMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _marketsBySecurity.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(
			static value => value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.AltCoinTrader))
				continue;
			if (!requestedSymbol.IsEmpty() &&
				!requestedSymbol.EqualsIgnoreCase(
					market.SecurityCode))
				continue;

			var security = CreateSecurity(
				market,
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;

			await SendOutMessageAsync(
				security,
				cancellationToken);
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime = CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}.TryAdd(
					Level1Fields.State,
					market.IsActive
						? SecurityStates.Trading
						: SecurityStates.Stoped),
				cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(
			lookupMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();

		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(
				mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"AltCoinTrader does not expose " +
					"historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(
			market,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg,
				cancellationToken);
			return;
		}

		var key = new StreamKey(
			"ticker",
			market.Symbol,
			0);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(
				mdMsg.TransactionId,
				new()
				{
					NativeSymbol = market.Symbol,
					SecurityCode = market.SecurityCode,
				});
			subscribe = AddReference(
				_streamReferences, key);
		}

		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeTickerAsync(
					market.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg,
				cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(
				mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();

		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(
				mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"AltCoinTrader does not expose " +
					"historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = AltCoinTraderRestClient.NormalizeDepth(
			mdMsg.MaxDepth ?? 50);
		var snapshot = await RestClient.GetOrderBookAsync(
			market.Symbol,
			depth,
			cancellationToken);
		await SendDepthAsync(
			market.SecurityCode,
			snapshot,
			mdMsg.TransactionId,
			depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg,
				cancellationToken);
			return;
		}

		const int streamDepth = 200;
		var key = new StreamKey(
			"orderbook",
			market.Symbol,
			streamDepth);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(
				mdMsg.TransactionId,
				new()
				{
					NativeSymbol = market.Symbol,
					SecurityCode = market.SecurityCode,
					Depth = depth,
					StreamDepth = streamDepth,
				});
			subscribe = AddReference(
				_streamReferences, key);
		}

		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeOrderBookAsync(
					market.Symbol,
					streamDepth,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg,
				cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(
				mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();

		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(
				mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg,
				cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? 50)
			.Min(500).Max(1).To<int>();
		var from = mdMsg.From?.ToUtc();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var trades = await RestClient.GetPublicTradesAsync(
			market.Symbol,
			count,
			cancellationToken);
		foreach (var trade in (trades ?? [])
			.Where(trade =>
			{
				var time = trade.Timestamp > 0
					? trade.Timestamp
						.FromAltCoinTraderSeconds()
					: DateTime.MinValue;
				return time != DateTime.MinValue &&
					(from is null || time >= from.Value) &&
					time <= to;
			})
			.OrderBy(static trade => trade.Timestamp)
			.TakeLast(count))
		{
			if (!AddTrade(
				market.Symbol,
				trade.TradeId,
				false))
				continue;
			await SendPublicTradeAsync(
				market.SecurityCode,
				trade,
				mdMsg.TransactionId,
				cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg,
				cancellationToken);
			return;
		}

		var key = new StreamKey(
			"trades",
			market.Symbol,
			0);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(
				mdMsg.TransactionId,
				new()
				{
					NativeSymbol = market.Symbol,
					SecurityCode = market.SecurityCode,
				});
			subscribe = AddReference(
				_streamReferences, key);
		}

		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeTradesAsync(
					market.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg,
				cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(
				mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(
		AltCoinTraderMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.Base}/{market.Quote}",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Quote.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.QuantityStep,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(
		AltCoinTraderMarket market,
		long transactionId,
		CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(
			market.Symbol,
			cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"AltCoinTrader returned no ticker " +
					$"for '{market.Symbol}'.");
		await SendOutMessageAsync(
			CreateLevel1Message(
				market,
				ticker,
				transactionId),
			cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(
		AltCoinTraderMarket market,
		AltCoinTraderTicker ticker,
		long transactionId)
		=> new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromAltCoinTraderSeconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Change, ticker.PriceChange)
		.TryAdd(
			Level1Fields.State,
			market.IsActive
				? SecurityStates.Trading
				: SecurityStates.Stoped);

	private async ValueTask UnsubscribeLevel1Async(
		long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(
					_streamReferences,
					new(
						"ticker",
						subscription.NativeSymbol,
						0));
		if (release && _publicWsClient is not null)
			await _publicWsClient.UnsubscribeTickerAsync(
				subscription.NativeSymbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(
		long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(
					_streamReferences,
					new(
						"orderbook",
						subscription.NativeSymbol,
						subscription.StreamDepth));
		if (release && _publicWsClient is not null)
			await _publicWsClient.UnsubscribeOrderBookAsync(
				subscription.NativeSymbol,
				subscription.StreamDepth,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(
		long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(
					_streamReferences,
					new(
						"trades",
						subscription.NativeSymbol,
						0));
		if (release && _publicWsClient is not null)
			await _publicWsClient.UnsubscribeTradesAsync(
				subscription.NativeSymbol,
				cancellationToken);
	}

	private async ValueTask OnWebSocketTickerAsync(
		AltCoinTraderTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.Symbol.IsEmpty() != false)
			return;
		var market = GetMarket(ticker.Symbol);
		if (market is null)
			return;

		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions =
			[
				.. _level1Subscriptions.Where(pair =>
					pair.Value.NativeSymbol.EqualsIgnoreCase(
						ticker.Symbol)),
			];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(
				CreateLevel1Message(
					market,
					ticker,
					pair.Key),
				cancellationToken);
	}

	private async ValueTask OnWebSocketOrderBookAsync(
		AltCoinTraderOrderBook book,
		CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;

		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions =
			[
				.. _depthSubscriptions.Where(pair =>
					pair.Value.NativeSymbol.EqualsIgnoreCase(
						book.Symbol)),
			];
		foreach (var pair in subscriptions)
			await SendDepthAsync(
				pair.Value.SecurityCode,
				book,
				pair.Key,
				pair.Value.Depth,
				cancellationToken);
	}

	private async ValueTask OnWebSocketTradesAsync(
		AltCoinTraderTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			if (trade?.Market.IsEmpty() != false ||
				trade.TradeId.IsEmpty() ||
				!AddTrade(
					trade.Market,
					trade.TradeId,
					false))
				continue;

			KeyValuePair<long, MarketSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions =
				[
					.. _tickSubscriptions.Where(pair =>
						pair.Value.NativeSymbol.EqualsIgnoreCase(
							trade.Market)),
				];
			foreach (var pair in subscriptions)
				await SendPublicTradeAsync(
					pair.Value.SecurityCode,
					trade,
					pair.Key,
					cancellationToken);
		}
	}

	private ValueTask SendDepthAsync(
		string securityCode,
		AltCoinTraderOrderBook book,
		long transactionId,
		int depth,
		CancellationToken cancellationToken)
	{
		if (book is null)
			throw new InvalidDataException(
				"AltCoinTrader returned an empty order book.");
		return SendOutMessageAsync(
			new QuoteChangeMessage
			{
				SecurityId = securityCode
					.ToAltCoinTraderSecurityId(),
				ServerTime = book.Timestamp > 0
					? book.Timestamp
						.FromAltCoinTraderSeconds()
					: CurrentTime,
				OriginalTransactionId = transactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = ToQuotes(book.Bids, false, depth),
				Asks = ToQuotes(book.Asks, true, depth),
			},
			cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(
		string securityCode,
		AltCoinTraderTrade trade,
		long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = securityCode
					.ToAltCoinTraderSecurityId(),
				ServerTime = trade.Timestamp > 0
					? trade.Timestamp
						.FromAltCoinTraderSeconds()
					: CurrentTime,
				OriginalTransactionId = transactionId,
				TradeStringId = trade.TradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.Quantity.Abs(),
				OriginSide = trade.Side.ToSide(),
			},
			cancellationToken);

	private static QuoteChange[] ToQuotes(
		decimal[][] levels,
		bool isAsk,
		int depth)
	{
		var grouped = (levels ?? [])
			.Where(static level =>
				level is { Length: >= 2 } &&
				level[0] > 0 &&
				level[1] > 0)
			.GroupBy(static level => level[0])
			.Select(static group => new QuoteChange(
				group.Key,
				group.Sum(static level => level[1])));
		return
		[
			.. (isAsk
				? grouped.OrderBy(
					static quote => quote.Price)
				: grouped.OrderByDescending(
					static quote => quote.Price))
				.Take(depth),
		];
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId,
			cancellationToken);
	}
}
