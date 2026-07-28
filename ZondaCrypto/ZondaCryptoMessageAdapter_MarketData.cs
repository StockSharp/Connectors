namespace StockSharp.ZondaCrypto;

public partial class ZondaCryptoMessageAdapter
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
					BoardCodes.ZondaCrypto))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.SecurityCode) &&
				!requested.EqualsIgnoreCase(market.Code))
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
			MarketSubscription subscription;
			var unsubscribe = false;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
				unsubscribe = ReleaseReference(
					_streamReferences,
					new("ticker", subscription.NativeSymbol));
			}
			if (unsubscribe)
				await WsClient.UnsubscribePublicAsync(
					"trading",
					CreatePath("ticker", subscription.NativeSymbol),
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
				"zondacrypto does not expose historical Level1 " +
					"events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(
			market,
			await RestClient.GetTickerAsync(
				market.Code, cancellationToken),
			mdMsg.TransactionId,
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
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.Code,
				SecurityCode = market.SecurityCode,
			};
			subscribe = AddReference(
				_streamReferences,
				new("ticker", market.Code));
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribePublicAsync(
					"trading",
					CreatePath("ticker", market.Code),
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(
					_streamReferences,
					new("ticker", market.Code));
			}
			throw;
		}
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
					new("orderbook", subscription.NativeSymbol));
			}
			if (unsubscribe)
				await WsClient.UnsubscribePublicAsync(
					"trading",
					CreatePath(
						"orderbook", subscription.NativeSymbol),
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
				"zondacrypto does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var maximumDepth = (mdMsg.MaxDepth ?? 100)
			.Max(1).Min(500).To<int>();
		var book = await RestClient.GetOrderBookAsync(
			market.Code, cancellationToken);
		UpdateBook(market.Code, book);
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
				NativeSymbol = market.Code,
				SecurityCode = market.SecurityCode,
				Depth = maximumDepth,
			};
			subscribe = AddReference(
				_streamReferences,
				new("orderbook", market.Code));
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribePublicAsync(
					"trading",
					CreatePath("orderbook", market.Code),
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
					new("orderbook", market.Code));
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
					new("transactions", subscription.NativeSymbol));
			}
			if (unsubscribe)
				await WsClient.UnsubscribePublicAsync(
					"trading",
					CreatePath(
						"transactions", subscription.NativeSymbol),
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
			market.Code,
			maximum,
			cancellationToken) ?? [])
			.Where(trade =>
				(mdMsg.From is null ||
					trade.Time >=
						mdMsg.From.Value.ToUniversalTime()) &&
				trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddPublicTrade(market.Code, trade.Id))
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
				NativeSymbol = market.Code,
				SecurityCode = market.SecurityCode,
			};
			subscribe = AddReference(
				_streamReferences,
				new("transactions", market.Code));
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribePublicAsync(
					"trading",
					CreatePath("transactions", market.Code),
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
					new("transactions", market.Code));
			}
			throw;
		}
	}

	private async ValueTask ProcessTickerMessageAsync(
		ZondaCryptoTicker ticker,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(ticker?.Market?.Code);
		if (market is null)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(
					market.Code))];
		foreach (var pair in subscriptions)
			await SendLevel1Async(
				market,
				ticker,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessTradeMessageAsync(
		ZondaCryptoTrade trade,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(trade?.MarketCode);
		if (market is null ||
			trade is null ||
			!AddPublicTrade(market.Code, trade.Id))
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(
					market.Code))];
		foreach (var pair in subscriptions)
			await SendTradeAsync(
				market,
				trade,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessBookMessageAsync(
		ZondaCryptoWsMessage message,
		CancellationToken cancellationToken)
	{
		var marketCode = message.BookChanges
			.Select(static change => change.MarketCode)
			.FirstOrDefault(static code => !code.IsEmpty());
		var market = GetMarket(marketCode);
		if (market is null)
			return;

		var refresh = false;
		var stale = false;
		using (_sync.EnterScope())
		{
			if (_orderBooks.TryGetValue(
				market.Code, out var state) &&
				message.Sequence > 0 &&
				state.Sequence >= message.Sequence)
				stale = true;
			else if (state is null ||
				message.Sequence > 0 &&
				state.Sequence > 0 &&
				message.Sequence != state.Sequence + 1)
				refresh = true;
		}
		if (stale)
			return;
		if (refresh)
		{
			var snapshot = await RestClient.GetOrderBookAsync(
				market.Code, cancellationToken);
			UpdateBook(market.Code, snapshot);
		}
		else
		{
			ApplyBookChanges(
				market.Code,
				message.BookChanges,
				message.Sequence,
				message.Time);
		}

		var book = GetBook(market.Code);
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(
					market.Code))];
		foreach (var pair in subscriptions)
			await SendBookAsync(
				market,
				book,
				pair.Key,
				pair.Value.Depth,
				cancellationToken);
	}

	private SecurityMessage CreateSecurity(
		ZondaCryptoMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteCurrency.ToCurrency(),
			PriceStep = market.RatePrecision.ToStep(),
			VolumeStep = market.AmountPrecision.ToStep(),
			MinVolume = market.MinimumBaseAmount > 0
				? market.MinimumBaseAmount
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private ValueTask SendLevel1Async(
		ZondaCryptoMarket market,
		ZondaCryptoTicker ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = ticker.Time == default
					? CurrentTime
					: ticker.Time,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice, ticker.LastPrice)
			.TryAdd(
				Level1Fields.BestBidPrice, ticker.BidPrice)
			.TryAdd(
				Level1Fields.BestAskPrice, ticker.AskPrice)
			.TryAdd(
				Level1Fields.Change,
				ticker.LastPrice is decimal last &&
					ticker.PreviousPrice is decimal previous
					? last - previous
					: null)
			.TryAdd(
				Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private ValueTask SendBookAsync(
		ZondaCryptoMarket market,
		ZondaCryptoOrderBook book,
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
				ServerTime = book.Time == default
					? CurrentTime
					: book.Time,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = [.. book.Bids
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume)
					{
						OrdersCount = quote.OrderCount,
					})],
				Asks = [.. book.Asks
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume)
					{
						OrdersCount = quote.OrderCount,
					})],
			},
			cancellationToken);
	}

	private ValueTask SendTradeAsync(
		ZondaCryptoMarket market,
		ZondaCryptoTrade trade,
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
		string marketCode,
		ZondaCryptoOrderBook book)
	{
		using (_sync.EnterScope())
		{
			var state = new BookState
			{
				Sequence = book?.Sequence ?? 0,
				Time = book?.Time ?? CurrentTime,
			};
			foreach (var quote in book?.Bids ?? [])
				state.Bids[quote.Price] = quote.Volume;
			foreach (var quote in book?.Asks ?? [])
				state.Asks[quote.Price] = quote.Volume;
			_orderBooks[marketCode] = state;
		}
	}

	private void ApplyBookChanges(
		string marketCode,
		IEnumerable<ZondaCryptoBookChange> changes,
		long sequence,
		DateTime time)
	{
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(
				marketCode, out var state))
			{
				state = new();
				_orderBooks[marketCode] = state;
			}
			foreach (var change in changes ?? [])
			{
				var side = change.Side == Sides.Buy
					? state.Bids
					: state.Asks;
				if (change.IsRemove || change.Volume <= 0)
					side.Remove(change.Price);
				else
					side[change.Price] = change.Volume;
			}
			if (sequence > 0)
				state.Sequence = sequence;
			if (time != default)
				state.Time = time;
		}
	}

	private ZondaCryptoOrderBook GetBook(string marketCode)
	{
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(
				marketCode, out var state))
				return new();
			return new()
			{
				Bids = [.. state.Bids.Select(static pair =>
					new ZondaCryptoQuote
					{
						Price = pair.Key,
						Volume = pair.Value,
					})],
				Asks = [.. state.Asks.Select(static pair =>
					new ZondaCryptoQuote
					{
						Price = pair.Key,
						Volume = pair.Value,
					})],
				Sequence = state.Sequence,
				Time = state.Time,
			};
		}
	}

	private static string CreatePath(
		string type,
		string marketCode)
		=> $"{type}/{marketCode.ToLowerInvariant()}";

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
