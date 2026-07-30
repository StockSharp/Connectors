namespace StockSharp.LCX;

public partial class LcxMessageAdapter
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
			static value => value.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Lcx))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.Symbol))
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
					market.IsActive
						? SecurityStates.Trading
						: SecurityStates.Stoped),
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
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId))
					return;
			}
			if (ReleaseReference("ticker"))
				await WsClient.UnsubscribeAsync(
					"ticker", null, false, cancellationToken);
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
				"LCX does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		var ticker = GetTicker(market.Symbol);
		if (ticker is null)
		{
			var tickers = await RestClient.GetTickersAsync(
				cancellationToken);
			UpdateTickers(tickers);
			ticker = tickers.FirstOrDefault(item =>
				item.Symbol.EqualsIgnoreCase(market.Symbol));
		}
		await SendLevel1Async(
			market,
			ticker,
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
				Symbol = market.Symbol,
			};
		var subscribe = AddReference("ticker");
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					"ticker",
					null,
					false,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference("ticker");
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
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			var unsubscribeStream =
				"orderbook:" + subscription.Symbol;
			if (ReleaseReference(unsubscribeStream))
				await WsClient.UnsubscribeAsync(
					"orderbook",
					subscription.Symbol,
					false,
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
				"LCX does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100)
			.Max(1).Min(500).To<int>();
		var book = await RestClient.GetBookAsync(
			market.Symbol, cancellationToken);
		UpdateBook(book);
		await SendBookAsync(
			market,
			book,
			mdMsg.TransactionId,
			depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = market.Symbol,
				Depth = depth,
			};
		var stream = "orderbook:" + market.Symbol;
		var subscribe = AddReference(stream);
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					"orderbook",
					market.Symbol,
					false,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(stream);
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
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			var unsubscribeStream =
				"trade:" + subscription.Symbol;
			if (ReleaseReference(unsubscribeStream))
				await WsClient.UnsubscribeAsync(
					"trade",
					subscription.Symbol,
					false,
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

		foreach (var trade in
			(await RestClient.GetTradesAsync(
				market.Symbol, 1, cancellationToken) ?? [])
			.Where(trade =>
				(mdMsg.From is null ||
					trade.Time >=
						mdMsg.From.Value.ToUniversalTime()) &&
				trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddTrade(market.Symbol, trade.Id))
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

		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = market.Symbol,
			};
		var stream = "trade:" + market.Symbol;
		var subscribe = AddReference(stream);
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					"trade",
					market.Symbol,
					false,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(stream);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"LCX does not support the {timeFrame} candle " +
					"time frame.");
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 10000)
			.Max(1).Min(10000).To<int>();
		var from = (mdMsg.From ??
			to - timeFrame * maximum).ToUniversalTime();

		foreach (var candle in
			(await RestClient.GetCandlesAsync(
				market.Symbol,
				timeFrame,
				from,
				to,
				cancellationToken) ?? [])
			.Where(candle =>
				candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendCandleAsync(
				market,
				candle,
				mdMsg.TransactionId,
				cancellationToken);

		await CompleteMarketSubscriptionAsync(
			mdMsg, cancellationToken);
	}

	private async ValueTask ProcessTickerAsync(
		LcxTicker ticker,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(ticker?.Symbol);
		if (market is null || ticker is null)
			return;
		UpdateTickers([ticker]);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					market.Symbol))];

		foreach (var pair in subscriptions)
			await SendLevel1Async(
				market,
				ticker,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessTradeAsync(
		LcxPublicTrade trade,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(trade?.Symbol);
		if (market is null ||
			trade is null ||
			!AddTrade(market.Symbol, trade.Id))
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					market.Symbol))];

		foreach (var pair in subscriptions)
			await SendTradeAsync(
				market,
				trade,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessBookAsync(
		LcxBook book,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(book?.Symbol);
		if (market is null || book is null)
			return;
		if (book.IsSnapshot)
			UpdateBook(book);
		else
			ApplyBookUpdate(book);
		var current = GetBook(market.Symbol);
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					market.Symbol))];

		foreach (var pair in subscriptions)
			await SendBookAsync(
				market,
				current,
				pair.Key,
				pair.Value.Depth,
				cancellationToken);
	}

	private SecurityMessage CreateSecurity(
		LcxMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Symbol,
			ShortName = market.Symbol,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteCurrency.ToCurrency(),
			PriceStep = market.PricePrecision.ToStep(),
			VolumeStep = market.AmountPrecision.ToStep(),
			MinVolume = market.MinimumAmount > 0
				? market.MinimumAmount
				: null,
			MaxVolume = market.MaximumAmount > 0
				? market.MaximumAmount
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private ValueTask SendLevel1Async(
		LcxMarket market,
		LcxTicker ticker,
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
				Level1Fields.BestBidPrice, ticker.Bid)
			.TryAdd(
				Level1Fields.BestAskPrice, ticker.Ask)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(Level1Fields.Change, ticker.Change)
			.TryAdd(
				Level1Fields.State,
				market.IsActive
					? SecurityStates.Trading
					: SecurityStates.Stoped),
			cancellationToken);
	}

	private ValueTask SendBookAsync(
		LcxMarket market,
		LcxBook book,
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
		LcxMarket market,
		LcxPublicTrade trade,
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

	private ValueTask SendCandleAsync(
		LcxMarket market,
		LcxCandle candle,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var closeTime = candle.OpenTime + candle.TimeFrame;
		return SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = market.ToStockSharp(),
				TypedArg = candle.TimeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = closeTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = closeTime <= DateTime.UtcNow
					? CandleStates.Finished
					: CandleStates.Active,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);
	}

	private LcxTicker GetTicker(string symbol)
	{
		using (_sync.EnterScope())
			return _tickers.TryGetValue(symbol, out var ticker)
				? ticker
				: null;
	}

	private void UpdateTickers(IEnumerable<LcxTicker> tickers)
	{
		using (_sync.EnterScope())
		{
			foreach (var ticker in tickers ?? [])
			{
				if (ticker?.Symbol.IsEmpty() == false)
					_tickers[ticker.Symbol] = ticker;
			}
		}
	}

	private void UpdateBook(LcxBook book)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;
		using (_sync.EnterScope())
		{
			var state = new BookState();

			foreach (var quote in book.Bids)
			{
				if (quote.Volume > 0)
					state.Bids[quote.Price] = quote.Volume;
			}

			foreach (var quote in book.Asks)
			{
				if (quote.Volume > 0)
					state.Asks[quote.Price] = quote.Volume;
			}

			_books[book.Symbol] = state;
		}
	}

	private void ApplyBookUpdate(LcxBook book)
	{
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(
				book.Symbol, out var state))
			{
				state = new();
				_books[book.Symbol] = state;
			}

			foreach (var quote in book.Bids.Concat(book.Asks))
			{
				var side = quote.Side == Sides.Buy
					? state.Bids
					: state.Asks;
				if (quote.Volume > 0)
					side[quote.Price] = quote.Volume;
				else
					side.Remove(quote.Price);
			}
		}
	}

	private LcxBook GetBook(string symbol)
	{
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
				return null;
			return new()
			{
				Symbol = symbol,
				IsSnapshot = true,
				Bids = [.. state.Bids.Select(static item =>
					new LcxQuote
					{
						Price = item.Key,
						Volume = item.Value,
						Side = Sides.Buy,
					})],
				Asks = [.. state.Asks.Select(static item =>
					new LcxQuote
					{
						Price = item.Key,
						Volume = item.Value,
						Side = Sides.Sell,
					})],
			};
		}
	}

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
