namespace StockSharp.Pacifica;

using Native;

public partial class PacificaMessageAdapter
{
	private static readonly PacificaSubscriptionKey _pricesStream =
		new(PacificaSources.Prices, null, null, null, null);

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
		foreach (var market in GetMarkets().OrderBy(static item => item.Symbol,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Pacifica))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.Ordinal))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Future))
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
				"Pacifica does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var keys = GetLevel1Streams(market.Symbol);
		PacificaSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			added = AddStreamReferences(keys);
		}
		try
		{
			await SubscribeStreamsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReferences(keys);
			}
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
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Pacifica does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var snapshot = await RestClient.GetBookAsync(market.Symbol, 1,
			cancellationToken);
		if (snapshot is not null)
			await SendBookAsync(market.Symbol, snapshot, mdMsg.TransactionId, depth,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new PacificaSubscriptionKey(PacificaSources.Book,
			market.Symbol, null, 1, null);
		PacificaSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				Depth = depth,
			});
			added = AddStreamReferences([key]);
		}
		try
		{
			await SubscribeStreamsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReferences([key]);
			}
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
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.EnsureUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1).To<int>();
		var trades = (await RestClient.GetTradesAsync(market.Symbol,
			cancellationToken) ?? [])
			.Where(static trade => trade is not null && trade.CreatedAt > 0)
			.Where(trade => trade.CreatedAt.ToPacificaTime() <= to &&
				(from is null || trade.CreatedAt.ToPacificaTime() >= from.Value))
			.OrderBy(static trade => trade.CreatedAt)
			.TakeLast(count)
			.ToArray();
		foreach (var trade in trades)
			await SendPublicTradeAsync(market.Symbol, trade, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new PacificaSubscriptionKey(PacificaSources.Trades,
			market.Symbol, null, null, null);
		PacificaSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			added = AddStreamReferences([key]);
		}
		try
		{
			await SubscribeStreamsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReferences([key]);
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId,
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
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToPacifica();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var earliest = to.AddTicks(-checked(timeFrame.Ticks * (long)count));
		var from = mdMsg.From?.EnsureUtc() ?? earliest;
		if (from < earliest)
			from = earliest;
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Pacifica candle start time cannot be later than end time.");
		var candles = await RestClient.GetCandlesAsync(market.Symbol, interval,
			from, to, count, cancellationToken);
		foreach (var candle in (candles ?? [])
			.Where(static candle => candle is not null)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(count))
			await SendCandleAsync(candle, timeFrame, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new PacificaSubscriptionKey(PacificaSources.Candle,
			market.Symbol, interval, null, null);
		PacificaSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				Interval = interval,
				TimeFrame = timeFrame,
			});
			added = AddStreamReferences([key]);
		}
		try
		{
			await SubscribeStreamsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReferences([key]);
			}
			throw;
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		PacificaSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(GetLevel1Streams(subscription.Symbol));
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		PacificaSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					[new(PacificaSources.Book, subscription.Symbol, null, 1, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		PacificaSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					[new(PacificaSources.Trades, subscription.Symbol, null, null, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		PacificaSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					[new(PacificaSources.Candle, subscription.Symbol,
						subscription.Interval, null, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask SubscribeStreamsAsync(
		IEnumerable<PacificaSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		var subscribed = new List<PacificaSubscriptionKey>();
		try
		{
			foreach (var key in keys)
			{
				await Socket.SubscribeAsync(key, cancellationToken);
				subscribed.Add(key);
			}
		}
		catch
		{
			foreach (var key in subscribed.AsEnumerable().Reverse())
				try
				{
					await Socket.UnsubscribeAsync(key, cancellationToken);
				}
				catch (Exception error)
				{
					await SendOutErrorAsync(error, cancellationToken);
				}
			throw;
		}
	}

	private async ValueTask UnsubscribeStreamsAsync(
		IEnumerable<PacificaSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		foreach (var key in keys)
			await Socket.UnsubscribeAsync(key, cancellationToken);
	}

	private PacificaSubscriptionKey[] AddStreamReferences(
		IEnumerable<PacificaSubscriptionKey> keys)
	{
		var added = new List<PacificaSubscriptionKey>();
		foreach (var key in keys)
		{
			if (_streamReferences.TryGetValue(key, out var count))
				_streamReferences[key] = checked(count + 1);
			else
			{
				_streamReferences.Add(key, 1);
				added.Add(key);
			}
		}
		return [.. added];
	}

	private PacificaSubscriptionKey[] ReleaseStreamReferences(
		IEnumerable<PacificaSubscriptionKey> keys)
	{
		var removed = new List<PacificaSubscriptionKey>();
		foreach (var key in keys)
		{
			if (!_streamReferences.TryGetValue(key, out var count))
				continue;
			if (count > 1)
				_streamReferences[key] = count - 1;
			else
			{
				_streamReferences.Remove(key);
				removed.Add(key);
			}
		}
		return [.. removed];
	}

	private static PacificaSubscriptionKey[] GetLevel1Streams(string symbol)
		=>
		[
			_pricesStream,
			new(PacificaSources.BestBidOffer, symbol, null, null, null),
			new(PacificaSources.Trades, symbol, null, null, null),
		];

	private async ValueTask OnPricesAsync(PacificaPrice[] prices,
		CancellationToken cancellationToken)
	{
		foreach (var price in prices ?? [])
		{
			if (price?.Symbol.IsEmpty() != false || !TryGetMarket(price.Symbol, out _))
				continue;
			using (_sync.EnterScope())
				_prices[price.Symbol] = price;
			var time = price.Timestamp.ToPacificaTimeOrNow();
			UpdateServerTime(time);
			long[] ids;
			using (_sync.EnterScope())
				ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Symbol.Equals(price.Symbol,
						StringComparison.Ordinal))
					.Select(static pair => pair.Key)];
			foreach (var id in ids)
				await SendPriceAsync(price, id, cancellationToken);
		}
	}

	private async ValueTask OnBestBidOfferAsync(PacificaBestBidOffer quote,
		CancellationToken cancellationToken)
	{
		if (quote?.Symbol.IsEmpty() != false)
			return;
		var time = quote.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.Equals(quote.Symbol,
					StringComparison.Ordinal))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = quote.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
				SeqNum = quote.LastOrderId,
			}
			.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice.TryParseDecimal())
			.TryAdd(Level1Fields.BestBidVolume, quote.BidAmount.TryParseDecimal())
			.TryAdd(Level1Fields.BestBidTime,
				quote.BidPrice.IsEmpty() ? null : time)
			.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice.TryParseDecimal())
			.TryAdd(Level1Fields.BestAskVolume, quote.AskAmount.TryParseDecimal())
			.TryAdd(Level1Fields.BestAskTime,
				quote.AskPrice.IsEmpty() ? null : time), cancellationToken);
	}

	private async ValueTask OnBookAsync(PacificaBook book,
		CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;
		var time = book.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Symbol.Equals(book.Symbol,
					StringComparison.Ordinal))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(book.Symbol, book, subscription.Id,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnPublicTradesAsync(PacificaPublicTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			if (trade?.Symbol.IsEmpty() != false)
				continue;
			var time = trade.CreatedAt.ToPacificaTimeOrNow();
			UpdateServerTime(time);
			long[] tickIds;
			long[] level1Ids;
			using (_sync.EnterScope())
			{
				tickIds = [.. _tickSubscriptions
					.Where(pair => pair.Value.Symbol.Equals(trade.Symbol,
						StringComparison.Ordinal))
					.Select(static pair => pair.Key)];
				level1Ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Symbol.Equals(trade.Symbol,
						StringComparison.Ordinal))
					.Select(static pair => pair.Key)];
			}
			foreach (var id in tickIds)
				await SendPublicTradeAsync(trade.Symbol, trade, id,
					cancellationToken);
			foreach (var id in level1Ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = trade.Symbol.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = id,
				}
				.TryAdd(Level1Fields.LastTradePrice,
					trade.Price.TryParseDecimal())
				.TryAdd(Level1Fields.LastTradeVolume,
					trade.Amount.TryParseDecimal())
				.TryAdd(Level1Fields.LastTradeTime, time)
				.TryAdd(Level1Fields.LastTradeOrigin,
					trade.Side.ToStockSharp()), cancellationToken);
		}
	}

	private async ValueTask OnCandleAsync(PacificaCandle candle,
		CancellationToken cancellationToken)
	{
		if (candle?.Symbol.IsEmpty() != false)
			return;
		(long Id, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions
				.Where(pair => pair.Value.Symbol.Equals(candle.Symbol,
					StringComparison.Ordinal) &&
					pair.Value.Interval == candle.Interval)
				.Select(static pair => (pair.Key, pair.Value.TimeFrame))];
		foreach (var subscription in subscriptions)
			await SendCandleAsync(candle, subscription.TimeFrame,
				subscription.Id, cancellationToken);
	}

	private async ValueTask SendLevel1SnapshotAsync(PacificaMarket market,
		long transactionId, CancellationToken cancellationToken)
	{
		var price = GetPrice(market.Symbol);
		if (price is not null)
			await SendPriceAsync(price, transactionId, cancellationToken);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.MinPrice, market.MinimumPrice.TryParseDecimal())
		.TryAdd(Level1Fields.MaxPrice, market.MaximumPrice.TryParseDecimal())
		.TryAdd(Level1Fields.PriceStep, market.TickSize.TryParseDecimal())
		.TryAdd(Level1Fields.VolumeStep, market.LotSize.TryParseDecimal())
		.TryAdd(Level1Fields.State, SecurityStates.Trading), cancellationToken);
	}

	private ValueTask SendPriceAsync(PacificaPrice price, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = price.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = price.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.TheorPrice, price.MarkPrice.TryParseDecimal())
		.TryAdd(Level1Fields.AveragePrice, price.MidPrice.TryParseDecimal())
		.TryAdd(Level1Fields.Index, price.OraclePrice.TryParseDecimal())
		.TryAdd(Level1Fields.OpenInterest, price.OpenInterest.TryParseDecimal())
		.TryAdd(Level1Fields.Volume, price.Volume24Hours.TryParseDecimal())
		.TryAdd(Level1Fields.ClosePrice, price.YesterdayPrice.TryParseDecimal()),
			cancellationToken);
	}

	private ValueTask SendBookAsync(string symbol, PacificaBook book,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		if (book.Levels is not { Length: >= 2 })
			throw new InvalidDataException(
				"Pacifica returned a malformed order book.");
		var time = book.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = book.LastOrderId ?? 0,
			Bids = ToQuotes(book.Levels[0], depth, true),
			Asks = ToQuotes(book.Levels[1], depth, false),
		}, cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(string symbol,
		PacificaPublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = trade.CreatedAt.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeId = trade.HistoryId > 0 ? trade.HistoryId : null,
			TradePrice = trade.Price.ParseDecimal("trade price"),
			TradeVolume = trade.Amount.ParseDecimal("trade amount"),
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(PacificaCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.OpenTime.ToPacificaTime("candle open time");
		var closeTime = candle.CloseTime > candle.OpenTime
			? candle.CloseTime.ToPacificaTime("candle close time")
			: openTime + timeFrame;
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.OpenPrice.ParseDecimal("candle open"),
			HighPrice = candle.HighPrice.ParseDecimal("candle high"),
			LowPrice = candle.LowPrice.ParseDecimal("candle low"),
			ClosePrice = candle.ClosePrice.ParseDecimal("candle close"),
			TotalVolume = candle.Volume.TryParseDecimal() ?? 0m,
			TotalTicks = candle.TradesCount,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(PacificaMarket market,
		long transactionId)
		=> new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = market.BaseAsset + "/USD perpetual",
			ShortName = market.Symbol,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = CurrencyTypes.USD,
			PriceStep = market.TickSize.ParseDecimal("tick size"),
			VolumeStep = market.LotSize.ParseDecimal("lot size"),
			MinVolume = market.LotSize.TryParseDecimal(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		}.TryFillUnderlyingId(market.BaseAsset);

	private static QuoteChange[] ToQuotes(PacificaBookLevel[] levels,
		int depth, bool isBids)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParseDecimal("book price");
			var volume = level.Amount.ParseDecimal("book amount");
			if (price <= 0 || volume < 0 || level.OrdersCount < 0)
				throw new InvalidDataException(
					"Pacifica returned an invalid order-book level.");
			if (volume > 0)
				quotes.Add(new(price, volume)
				{
					OrdersCount = level.OrdersCount,
				});
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.EnsureUtc())
			return ((to - from.EnsureUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit.Min(500);
	}
}
