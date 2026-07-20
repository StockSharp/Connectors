namespace StockSharp.Extended;

using Native;

public partial class ExtendedMessageAdapter
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

		foreach (var market in GetMarkets().OrderBy(static item => item.Name,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Extended))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Name,
					StringComparison.Ordinal))
				continue;
			var securityType = market.Type.ToStockSharp();
			if (securityTypes.Count > 0 && !securityTypes.Contains(securityType))
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
				"Extended does not publish historical Level1 changes.");

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

		var keys = GetLevel1Streams(market.Name);
		ExtendedSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Name,
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
				"Extended does not publish historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var snapshot = await RestClient.GetOrderBookAsync(market.Name,
			cancellationToken);
		if (snapshot is not null)
			await SendBookAsync(market.Name, snapshot, mdMsg.TransactionId, depth,
				true, DateTime.UtcNow, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new ExtendedSubscriptionKey(ExtendedStreamScopes.OrderBooks,
			market.Name, "full", null, null);
		ExtendedSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Name,
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
		var from = mdMsg.From?.EnsureExtendedUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureExtendedUtc();
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1).To<int>();
		var trades = (await RestClient.GetPublicTradesAsync(market.Name,
			cancellationToken) ?? [])
			.Where(static trade => trade is not null && trade.Timestamp > 0)
			.Where(trade => trade.Timestamp.ToExtendedTime() <= to &&
				(from is null || trade.Timestamp.ToExtendedTime() >= from.Value))
			.OrderBy(static trade => trade.Timestamp)
			.TakeLast(count)
			.ToArray();
		foreach (var trade in trades)
			await SendPublicTradeAsync(trade, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new ExtendedSubscriptionKey(ExtendedStreamScopes.Trades,
			market.Name, null, null, null);
		ExtendedSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Name,
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
		var interval = timeFrame.ToExtendedInterval();
		var to = (mdMsg.To ?? ServerTime).EnsureExtendedUtc();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.EnsureExtendedUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Extended candle start time cannot be later than end time.");

		var candles = await RestClient.GetCandlesAsync(market.Name, interval,
			count, to, cancellationToken);
		foreach (var candle in (candles ?? [])
			.Where(static candle => candle is not null && candle.Timestamp > 0)
			.Where(candle => from is null ||
				candle.Timestamp.ToExtendedTime() >= from.Value)
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(count))
			await SendCandleAsync(market.Name, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new ExtendedSubscriptionKey(ExtendedStreamScopes.Candles,
			market.Name, ExtendedCandleTypes.Last.ToWire(), interval, null);
		ExtendedSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Name,
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
		ExtendedSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					GetLevel1Streams(subscription.Symbol));
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		ExtendedSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
				[
					new(ExtendedStreamScopes.OrderBooks, subscription.Symbol,
						"full", null, null),
				]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		ExtendedSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
				[
					new(ExtendedStreamScopes.Trades, subscription.Symbol,
						null, null, null),
				]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		ExtendedSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
				[
					new(ExtendedStreamScopes.Candles, subscription.Symbol,
						ExtendedCandleTypes.Last.ToWire(), subscription.Interval,
						null),
				]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask SubscribeStreamsAsync(
		IEnumerable<ExtendedSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		var subscribed = new List<ExtendedSubscriptionKey>();
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
		IEnumerable<ExtendedSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		foreach (var key in keys)
			await Socket.UnsubscribeAsync(key, cancellationToken);
	}

	private ExtendedSubscriptionKey[] AddStreamReferences(
		IEnumerable<ExtendedSubscriptionKey> keys)
	{
		var added = new List<ExtendedSubscriptionKey>();
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

	private ExtendedSubscriptionKey[] ReleaseStreamReferences(
		IEnumerable<ExtendedSubscriptionKey> keys)
	{
		var removed = new List<ExtendedSubscriptionKey>();
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

	private static ExtendedSubscriptionKey[] GetLevel1Streams(string symbol)
		=>
		[
			new(ExtendedStreamScopes.OrderBooks, symbol, "1", null, null),
			new(ExtendedStreamScopes.Trades, symbol, null, null, null),
			new(ExtendedStreamScopes.Prices, symbol,
				ExtendedPriceTypes.Mark.ToWire(), null, null),
			new(ExtendedStreamScopes.Prices, symbol,
				ExtendedPriceTypes.Index.ToWire(), null, null),
		];

	private async ValueTask OnOrderBookAsync(ExtendedOrderBook book,
		string detail, long timestamp, long sequence, bool isSnapshot,
		CancellationToken cancellationToken)
	{
		if (book?.Market.IsEmpty() != false || !TryGetMarket(book.Market, out _))
			return;
		var time = timestamp.ToExtendedTimeOrNow();
		UpdateServerTime(time);

		if (detail == "1")
		{
			var bid = GetBestQuote(book.Bids, true, isSnapshot);
			var ask = GetBestQuote(book.Asks, false, isSnapshot);
			using (_sync.EnterScope())
			{
				if (!_prices.TryGetValue(book.Market, out var state))
					_prices[book.Market] = state = new();
				state.BestBidPrice = bid?.Price;
				state.BestAskPrice = ask?.Price;
			}

			long[] ids;
			using (_sync.EnterScope())
				ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Symbol.Equals(book.Market,
						StringComparison.Ordinal))
					.Select(static pair => pair.Key)];
			foreach (var id in ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = book.Market.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = id,
					SeqNum = sequence,
				}
				.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
				.TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
				.TryAdd(Level1Fields.BestBidTime, bid is null ? null : time)
				.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
				.TryAdd(Level1Fields.BestAskVolume, ask?.Volume)
				.TryAdd(Level1Fields.BestAskTime, ask is null ? null : time),
					cancellationToken);
			return;
		}

		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Symbol.Equals(book.Market,
					StringComparison.Ordinal))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(book.Market, book, subscription.Id,
				subscription.Depth, isSnapshot, time, cancellationToken, sequence);
	}

	private async ValueTask OnPublicTradesAsync(ExtendedPublicTrade[] trades,
		long timestamp, long sequence, CancellationToken cancellationToken)
	{
		_ = timestamp;
		foreach (var trade in trades ?? [])
		{
			if (trade?.Market.IsEmpty() != false ||
				!TryGetMarket(trade.Market, out _))
				continue;
			var time = trade.Timestamp.ToExtendedTimeOrNow();
			UpdateServerTime(time);
			long[] tickIds;
			long[] level1Ids;
			using (_sync.EnterScope())
			{
				tickIds = [.. _tickSubscriptions
					.Where(pair => pair.Value.Symbol.Equals(trade.Market,
						StringComparison.Ordinal))
					.Select(static pair => pair.Key)];
				level1Ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Symbol.Equals(trade.Market,
						StringComparison.Ordinal))
					.Select(static pair => pair.Key)];
				if (!_prices.TryGetValue(trade.Market, out var state))
					_prices[trade.Market] = state = new();
				state.LastPrice = trade.Price.TryParseExtendedDecimal();
			}
			foreach (var id in tickIds)
				await SendPublicTradeAsync(trade, id, cancellationToken, sequence);
			foreach (var id in level1Ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = trade.Market.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = id,
					SeqNum = sequence,
				}
				.TryAdd(Level1Fields.LastTradePrice,
					trade.Price.TryParseExtendedDecimal())
				.TryAdd(Level1Fields.LastTradeVolume,
					trade.Quantity.TryParseExtendedDecimal())
				.TryAdd(Level1Fields.LastTradeTime, time)
				.TryAdd(Level1Fields.LastTradeOrigin, trade.Side.ToStockSharp()),
					cancellationToken);
		}
	}

	private ValueTask OnFundingRateAsync(ExtendedFundingRate fundingRate,
		long timestamp, long sequence, CancellationToken cancellationToken)
	{
		_ = fundingRate;
		_ = timestamp;
		_ = sequence;
		_ = cancellationToken;
		return default;
	}

	private async ValueTask OnPriceAsync(ExtendedPriceUpdate update,
		ExtendedPriceTypes priceType, long timestamp, long sequence,
		CancellationToken cancellationToken)
	{
		if (update?.Market.IsEmpty() != false ||
			!TryGetMarket(update.Market, out _))
			return;
		var price = update.Price.ParseExtendedDecimal("price");
		var time = (update.Timestamp > 0 ? update.Timestamp : timestamp)
			.ToExtendedTimeOrNow();
		UpdateServerTime(time);
		using (_sync.EnterScope())
		{
			if (!_prices.TryGetValue(update.Market, out var state))
				_prices[update.Market] = state = new();
			if (priceType == ExtendedPriceTypes.Mark)
				state.MarkPrice = price;
			else
				state.IndexPrice = price;
		}

		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.Equals(update.Market,
					StringComparison.Ordinal))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
		{
			var message = new Level1ChangeMessage
			{
				SecurityId = update.Market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
				SeqNum = sequence,
			};
			message.TryAdd(priceType == ExtendedPriceTypes.Mark
				? Level1Fields.TheorPrice
				: Level1Fields.Index, price);
			await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask OnCandlesAsync(string market, string interval,
		ExtendedCandle[] candles, long timestamp, long sequence,
		CancellationToken cancellationToken)
	{
		_ = timestamp;
		_ = sequence;
		(long Id, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions
				.Where(pair => pair.Value.Symbol.Equals(market,
					StringComparison.Ordinal) && pair.Value.Interval == interval)
				.Select(static pair => (pair.Key, pair.Value.TimeFrame))];
		foreach (var candle in candles ?? [])
			foreach (var subscription in subscriptions)
				await SendCandleAsync(market, candle, subscription.TimeFrame,
					subscription.Id, cancellationToken);
	}

	private async ValueTask SendLevel1SnapshotAsync(ExtendedMarket market,
		long transactionId, CancellationToken cancellationToken)
	{
		var statistics = market.Statistics;
		var price = GetPriceState(market.Name);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Name.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep,
			market.TradingConfig.MinimumPriceChange.TryParseExtendedDecimal())
		.TryAdd(Level1Fields.VolumeStep,
			market.TradingConfig.MinimumOrderSizeChange.TryParseExtendedDecimal())
		.TryAdd(Level1Fields.State,
			market.IsActive ? SecurityStates.Trading : SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice, price?.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, price?.BestBidPrice)
		.TryAdd(Level1Fields.BestAskPrice, price?.BestAskPrice)
		.TryAdd(Level1Fields.TheorPrice, price?.MarkPrice)
		.TryAdd(Level1Fields.Index, price?.IndexPrice)
		.TryAdd(Level1Fields.HighPrice,
			statistics?.DailyHigh.TryParseExtendedDecimal())
		.TryAdd(Level1Fields.LowPrice,
			statistics?.DailyLow.TryParseExtendedDecimal())
		.TryAdd(Level1Fields.Volume,
			statistics?.DailyVolumeBase.TryParseExtendedDecimal())
		.TryAdd(Level1Fields.Change,
			statistics?.DailyPriceChangePercentage.TryParseExtendedDecimal())
		.TryAdd(Level1Fields.OpenInterest,
			statistics?.OpenInterestBase.TryParseExtendedDecimal()),
			cancellationToken);
	}

	private ValueTask SendBookAsync(string symbol, ExtendedOrderBook book,
		long transactionId, int depth, bool isSnapshot, DateTime time,
		CancellationToken cancellationToken, long sequence = 0)
	{
		if (book is null)
			throw new ArgumentNullException(nameof(book));
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = isSnapshot
				? QuoteChangeStates.SnapshotComplete
				: QuoteChangeStates.Increment,
			SeqNum = sequence,
			Bids = ToQuotes(book.Bids, isSnapshot ? depth : int.MaxValue, true,
				isSnapshot),
			Asks = ToQuotes(book.Asks, isSnapshot ? depth : int.MaxValue, false,
				isSnapshot),
		}, cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(ExtendedPublicTrade trade,
		long transactionId, CancellationToken cancellationToken,
		long sequence = 0)
	{
		var time = trade.Timestamp.ToExtendedTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			SeqNum = sequence,
			TradeId = trade.Id > 0 ? trade.Id : null,
			TradePrice = trade.Price.ParseExtendedDecimal("trade price"),
			TradeVolume = trade.Quantity.ParseExtendedDecimal("trade quantity"),
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string market, ExtendedCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		if (candle is null)
			throw new ArgumentNullException(nameof(candle));
		var openTime = candle.Timestamp.ToExtendedTime("candle timestamp");
		var closeTime = openTime + timeFrame;
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open.ParseExtendedDecimal("candle open"),
			HighPrice = candle.High.ParseExtendedDecimal("candle high"),
			LowPrice = candle.Low.ParseExtendedDecimal("candle low"),
			ClosePrice = candle.Close.ParseExtendedDecimal("candle close"),
			TotalVolume = candle.Volume.TryParseExtendedDecimal() ?? 0m,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(ExtendedMarket market,
		long transactionId)
	{
		var name = market.UiName.IsEmpty() ? market.Name : market.UiName;
		var message = new SecurityMessage
		{
			SecurityId = market.Name.ToStockSharp(),
			Name = name,
			ShortName = market.Name,
			Class = market.Type == ExtendedMarketTypes.Perpetual
				? "PERPETUAL"
				: "SPOT",
			SecurityType = market.Type.ToStockSharp(),
			PriceStep = market.TradingConfig.MinimumPriceChange
				.ParseExtendedDecimal("minimum price change"),
			VolumeStep = market.TradingConfig.MinimumOrderSizeChange
				.ParseExtendedDecimal("minimum order size change"),
			MinVolume = market.TradingConfig.MinimumOrderSize
				.TryParseExtendedDecimal(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		if (market.Type == ExtendedMarketTypes.Perpetual)
			message.TryFillUnderlyingId(market.AssetName);
		return message;
	}

	private static QuoteChange[] ToQuotes(ExtendedPriceLevel[] levels,
		int depth, bool isBids, bool isSnapshot)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParseExtendedDecimal("book price");
			var volume = (isSnapshot ? level.Quantity :
				level.CurrentQuantity ?? level.Quantity)
				.ParseExtendedDecimal("book quantity");
			if (price <= 0 || volume < 0)
				throw new InvalidDataException(
					"Extended returned an invalid order-book level.");
			if (!isSnapshot || volume > 0)
				quotes.Add(new(price, volume));
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private static QuoteChange? GetBestQuote(ExtendedPriceLevel[] levels,
		bool isBids, bool isSnapshot)
	{
		var quotes = ToQuotes(levels, int.MaxValue, isBids, isSnapshot);
		foreach (var quote in quotes)
			if (quote.Volume > 0)
				return quote;
		return null;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.EnsureExtendedUtc())
			return ((to - from.EnsureExtendedUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit.Min(500);
	}
}
