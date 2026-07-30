namespace StockSharp.Buda;

public partial class BudaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requested = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in GetMarkets().OrderBy(
			static value => value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Buda))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.SecurityCode) &&
				!requested.EqualsIgnoreCase(market.Id))
				continue;
			var security = CreateSecurity(
				market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime = CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}.TryAdd(
					Level1Fields.State,
					SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Buda.com does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(
			market,
			await RestClient.GetTickerAsync(
				market.Id, cancellationToken),
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.Id,
				SecurityCode = market.SecurityCode,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			DepthSubscription subscription;
			var unsubscribe = false;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
				unsubscribe = ReleaseReference(
					_streamReferences,
					new("book", subscription.NativeSymbol));
			}
			if (unsubscribe)
				await WsClient.UnsubscribeAsync(
					CreateChannel(
						"book", subscription.NativeSymbol),
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Buda.com does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var maximumDepth = (mdMsg.MaxDepth ?? 100)
			.Max(1).Min(500).To<int>();
		var book = await RestClient.GetOrderBookAsync(
			market.Id, cancellationToken);
		UpdateBook(market.Id, book);
		await SendBookAsync(
			market,
			book,
			mdMsg.TransactionId,
			maximumDepth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.Id,
				SecurityCode = market.SecurityCode,
				Depth = maximumDepth,
			};
			subscribe = AddReference(
				_streamReferences,
				new("book", market.Id));
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					CreateChannel("book", market.Id),
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(
					_streamReferences,
					new("book", market.Id));
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			MarketSubscription subscription;
			var unsubscribe = false;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
				unsubscribe = ReleaseReference(
					_streamReferences,
					new("trades", subscription.NativeSymbol));
			}
			if (unsubscribe)
				await WsClient.UnsubscribeAsync(
					CreateChannel(
						"trades", subscription.NativeSymbol),
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 100)
			.Max(1).Min(100).To<int>();

		foreach (var trade in (await RestClient.GetTradesAsync(
			market.Id,
			mdMsg.From,
			maximum,
			cancellationToken) ?? [])
			.Where(trade => trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddPublicTrade(market.Id, trade.Id))
				continue;
			await SendTradeAsync(
				market,
				trade,
				mdMsg.TransactionId,
				cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.Id,
				SecurityCode = market.SecurityCode,
			};
			subscribe = AddReference(
				_streamReferences,
				new("trades", market.Id));
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					CreateChannel("trades", market.Id),
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(
					_streamReferences,
					new("trades", market.Id));
			}
			throw;
		}
	}

	private async ValueTask RefreshLevel1Async(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions];

		foreach (var group in subscriptions.GroupBy(
			static pair => pair.Value.NativeSymbol,
			StringComparer.OrdinalIgnoreCase))
		{
			var market = GetMarket(group.Key);
			if (market is null)
				continue;
			var ticker = await RestClient.GetTickerAsync(
				market.Id, cancellationToken);

			foreach (var pair in group)
				await SendLevel1Async(
					market,
					ticker,
					pair.Key,
					cancellationToken);
		}
	}

	private async ValueTask ProcessTradeMessageAsync(
		BudaWsMessage message,
		CancellationToken cancellationToken)
	{
		var trade = message?.Trade;
		var market = GetMarket(
			message?.MarketId ?? trade?.MarketId);
		if (market is null ||
			trade is null ||
			!AddPublicTrade(market.Id, trade.Id))
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(market.Id))];

		foreach (var pair in subscriptions)
			await SendTradeAsync(
				market,
				trade,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessBookMessageAsync(
		BudaWsMessage message,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(message?.MarketId);
		if (market is null)
			return;
		if (message.OrderBook is not null)
			UpdateBook(market.Id, message.OrderBook);
		else if (message.Change is not null)
			ApplyBookChange(market.Id, message.Change);
		else
			return;

		var book = GetBook(market.Id);
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(market.Id))];

		foreach (var pair in subscriptions)
			await SendBookAsync(
				market,
				book,
				pair.Key,
				pair.Value.Depth,
				cancellationToken);
	}

	private SecurityMessage CreateSecurity(
		BudaMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Name.IsEmpty()
				? market.SecurityCode
				: market.Name,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteCurrency.ToCurrency(),
			MinVolume = market.MinimumOrderAmount > 0
				? market.MinimumOrderAmount
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private ValueTask SendLevel1Async(
		BudaMarket market,
		BudaTicker ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice, ticker.LastPrice)
			.TryAdd(
				Level1Fields.BestBidPrice, ticker.BidPrice)
			.TryAdd(
				Level1Fields.BestAskPrice, ticker.AskPrice)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(
				Level1Fields.Change, ticker.PriceVariation24h)
			.TryAdd(
				Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private ValueTask SendBookAsync(
		BudaMarket market,
		BudaOrderBook book,
		long originalTransactionId,
		int maximumDepth,
		CancellationToken cancellationToken)
	{
		if (book is null)
			return default;
		return SendOutMessageAsync(
			new QuoteChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = [.. book.Bids
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume))],
				Asks = [.. book.Asks
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume))],
			},
			cancellationToken);
	}

	private ValueTask SendTradeAsync(
		BudaMarket market,
		BudaTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = market.ToStockSharp(),
				ServerTime = trade.Time,
				OriginalTransactionId =
					originalTransactionId,
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume.Abs(),
				OriginSide = trade.Side,
			},
			cancellationToken);

	private void UpdateBook(
		string marketId,
		BudaOrderBook book)
	{
		using (_sync.EnterScope())
		{
			var state = new BookState();

			foreach (var quote in book?.Bids ?? [])
				state.Bids[quote.Price] = quote.Volume;

			foreach (var quote in book?.Asks ?? [])
				state.Asks[quote.Price] = quote.Volume;

			_orderBooks[marketId] = state;
		}
	}

	private void ApplyBookChange(
		string marketId,
		BudaBookChange change)
	{
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(
				marketId, out var state))
			{
				state = new();
				_orderBooks[marketId] = state;
			}
			var side = change.Side == Sides.Buy
				? state.Bids
				: state.Asks;
			side.TryGetValue(change.Price, out var current);
			var volume = current + change.Delta;
			if (volume > 0)
				side[change.Price] = volume;
			else
				side.Remove(change.Price);
		}
	}

	private BudaOrderBook GetBook(string marketId)
	{
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(
				marketId, out var state))
				return new();
			return new()
			{
				Bids = [.. state.Bids.Select(static pair => new BudaQuote
				{
					Price = pair.Key,
					Volume = pair.Value,
				})],
				Asks = [.. state.Asks.Select(static pair => new BudaQuote
				{
					Price = pair.Key,
					Volume = pair.Value,
				})],
			};
		}
	}

	private static string CreateChannel(
		string type,
		string marketId)
		=> $"{type}@{marketId.ToBudaChannelMarket()}";

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
