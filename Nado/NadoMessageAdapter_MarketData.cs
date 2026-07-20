namespace StockSharp.Nado;

using Native;

public partial class NadoMessageAdapter
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
		foreach (var market in GetMarkets().OrderBy(static item => item.Symbol,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Nado))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
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
				"Nado does not publish historical Level1 changes.");

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

		var keys = GetLevel1Streams(market);
		NadoSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				ProductId = market.ProductId,
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
				"Nado does not publish historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		await SendDepthSnapshotAsync(market.ProductId, mdMsg.TransactionId,
			depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new NadoSubscriptionKey(NadoStreamTypes.BookDepth,
			market.ProductId, 0, null);
		NadoSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				ProductId = market.ProductId,
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
		var from = mdMsg.From?.EnsureNadoUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureNadoUtc();
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1).To<int>();
		var trades = await RestClient.GetPublicTradesAsync(market.TickerId,
			count, null, cancellationToken);
		foreach (var trade in trades
			.Where(static item => item is not null && item.Timestamp > 0)
			.Where(item => item.Timestamp.FromNadoSeconds() <= to &&
				(from is null || item.Timestamp.FromNadoSeconds() >= from.Value))
			.OrderBy(static item => item.Timestamp)
			.TakeLast(count))
			await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new NadoSubscriptionKey(NadoStreamTypes.Trade,
			market.ProductId, 0, null);
		NadoSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				ProductId = market.ProductId,
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
		var granularity = timeFrame.ToGranularity();
		var to = (mdMsg.To ?? ServerTime).EnsureNadoUtc();
		var from = mdMsg.From?.EnsureNadoUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Nado candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var candles = await RestClient.GetCandlesAsync(market.ProductId,
			granularity, count, to, cancellationToken);
		foreach (var candle in candles
			.Where(static item => item is not null && !item.Timestamp.IsEmpty())
			.Where(item => from is null ||
				item.Timestamp.FromNadoSeconds() >= from.Value)
			.OrderBy(static item => item.Timestamp)
			.TakeLast(count))
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new NadoSubscriptionKey(NadoStreamTypes.LatestCandlestick,
			market.ProductId, granularity, null);
		NadoSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				ProductId = market.ProductId,
				TimeFrame = timeFrame,
				Granularity = granularity,
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
		NadoSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					GetLevel1Streams(_marketsByProduct[subscription.ProductId]));
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		NadoSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					[new(NadoStreamTypes.BookDepth, subscription.ProductId, 0, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		NadoSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					[new(NadoStreamTypes.Trade, subscription.ProductId, 0, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		NadoSubscriptionKey[] removed = [];
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
				removed = ReleaseStreamReferences(
					[new(NadoStreamTypes.LatestCandlestick,
						subscription.ProductId, subscription.Granularity, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask SubscribeStreamsAsync(
		IEnumerable<NadoSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		var subscribed = new List<NadoSubscriptionKey>();
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
		IEnumerable<NadoSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		foreach (var key in keys)
			await Socket.UnsubscribeAsync(key, cancellationToken);
	}

	private NadoSubscriptionKey[] AddStreamReferences(
		IEnumerable<NadoSubscriptionKey> keys)
	{
		var added = new List<NadoSubscriptionKey>();
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

	private NadoSubscriptionKey[] ReleaseStreamReferences(
		IEnumerable<NadoSubscriptionKey> keys)
	{
		var removed = new List<NadoSubscriptionKey>();
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

	private static NadoSubscriptionKey[] GetLevel1Streams(NadoMarket market)
		=> market.Type == NadoProductTypes.Perpetual
			?
			[
				new(NadoStreamTypes.BestBidOffer, market.ProductId, 0, null),
				new(NadoStreamTypes.Trade, market.ProductId, 0, null),
			]
			:
			[
				new(NadoStreamTypes.BestBidOffer, market.ProductId, 0, null),
				new(NadoStreamTypes.Trade, market.ProductId, 0, null),
			];

	private async ValueTask OnTradeAsync(NadoTradeEvent trade,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(trade.ProductId);
		if (market is null)
			return;
		var time = trade.Timestamp.FromNadoNanoseconds();
		var price = trade.Price.ParseX18("trade price");
		var volume = trade.TakerQuantity.ParseAmount("trade quantity").Abs();
		UpdateServerTime(time);
		long[] tickIds;
		long[] level1Ids;
		using (_sync.EnterScope())
		{
			tickIds = [.. _tickSubscriptions.Where(pair =>
				pair.Value.ProductId == trade.ProductId).Select(static pair => pair.Key)];
			level1Ids = [.. _level1Subscriptions.Where(pair =>
				pair.Value.ProductId == trade.ProductId).Select(static pair => pair.Key)];
			if (_prices.TryGetValue(trade.ProductId, out var state))
				state.Last = price;
		}
		foreach (var id in tickIds)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
				TradePrice = price,
				TradeVolume = volume,
				OriginSide = trade.IsTakerBuyer ? Sides.Buy : Sides.Sell,
			}, cancellationToken);
		foreach (var id in level1Ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.LastTradePrice, price)
			.TryAdd(Level1Fields.LastTradeVolume, volume)
			.TryAdd(Level1Fields.LastTradeTime, time)
			.TryAdd(Level1Fields.LastTradeOrigin,
				trade.IsTakerBuyer ? Sides.Buy : Sides.Sell), cancellationToken);
	}

	private async ValueTask OnBestBidOfferAsync(NadoBestBidOfferEvent quote,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(quote.ProductId);
		if (market is null)
			return;
		var time = quote.Timestamp.FromNadoNanoseconds();
		var bid = quote.BidPrice.TryParseX18();
		var ask = quote.AskPrice.TryParseX18();
		var bidVolume = quote.BidQuantity.TryParseAmount();
		var askVolume = quote.AskQuantity.TryParseAmount();
		UpdateServerTime(time);
		long[] ids;
		using (_sync.EnterScope())
		{
			ids = [.. _level1Subscriptions.Where(pair =>
				pair.Value.ProductId == quote.ProductId).Select(static pair => pair.Key)];
			if (_prices.TryGetValue(quote.ProductId, out var state))
			{
				state.Bid = bid;
				state.Ask = ask;
			}
		}
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.BestBidPrice, bid)
			.TryAdd(Level1Fields.BestBidVolume, bidVolume)
			.TryAdd(Level1Fields.BestBidTime, bid is null ? null : time)
			.TryAdd(Level1Fields.BestAskPrice, ask)
			.TryAdd(Level1Fields.BestAskVolume, askVolume)
			.TryAdd(Level1Fields.BestAskTime, ask is null ? null : time),
				cancellationToken);
	}

	private async ValueTask OnBookDepthAsync(NadoBookDepthEvent depth,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(depth.ProductId);
		if (market is null)
			return;
		var time = depth.MaximumTimestamp.FromNadoNanoseconds();
		UpdateServerTime(time);
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.ProductId == depth.ProductId).Select(static pair =>
				(pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = subscription.Id,
				State = QuoteChangeStates.Increment,
				Bids = ToX18Quotes(depth.Bids, subscription.Depth, true, false),
				Asks = ToX18Quotes(depth.Asks, subscription.Depth, false, false),
			}, cancellationToken);
	}

	private async ValueTask OnCandleAsync(NadoCandleEvent candle,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(candle.ProductId);
		if (market is null)
			return;
		(long Id, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.ProductId == candle.ProductId &&
				pair.Value.Granularity == candle.Granularity).Select(static pair =>
				(pair.Key, pair.Value.TimeFrame))];
		var openTime = candle.Timestamp.FromNadoNanoseconds();
		UpdateServerTime(openTime);
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = market.Symbol.ToStockSharp(),
				OpenTime = openTime,
				CloseTime = openTime + subscription.TimeFrame,
				OpenPrice = candle.Open.ParseX18("candle open"),
				HighPrice = candle.High.ParseX18("candle high"),
				LowPrice = candle.Low.ParseX18("candle low"),
				ClosePrice = candle.Close.ParseX18("candle close"),
				TotalVolume = candle.Volume.ParseAmount("candle volume"),
				TypedArg = subscription.TimeFrame,
				OriginalTransactionId = subscription.Id,
				State = CandleStates.Active,
			}, cancellationToken);
	}

	private ValueTask OnFundingRateAsync(NadoFundingRateEvent funding,
		CancellationToken cancellationToken)
	{
		_ = funding;
		_ = cancellationToken;
		return default;
	}

	private async ValueTask SendDepthSnapshotAsync(int productId,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		var market = GetMarket(productId) ?? throw new InvalidOperationException(
			"Unknown Nado product " + productId.ToString(
				CultureInfo.InvariantCulture) + ".");
		var book = await RestClient.GetOrderBookAsync(market.TickerId, depth,
			cancellationToken) ?? throw new InvalidDataException(
				"Nado returned no order-book snapshot.");
		var time = DateTime.UnixEpoch.AddMilliseconds(book.Timestamp);
		UpdateServerTime(time);
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToDecimalQuotes(book.Bids, depth, true),
			Asks = ToDecimalQuotes(book.Asks, depth, false),
		}, cancellationToken);
	}

	private ValueTask SendLevel1SnapshotAsync(NadoMarket market,
		long transactionId, CancellationToken cancellationToken)
	{
		var price = GetPriceState(market.ProductId);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep,
			market.BookInfo.PriceIncrement.TryParseX18())
		.TryAdd(Level1Fields.VolumeStep,
			market.BookInfo.SizeIncrement.TryParseAmount())
		.TryAdd(Level1Fields.State, SecurityStates.Trading)
		.TryAdd(Level1Fields.LastTradePrice, price?.Last)
		.TryAdd(Level1Fields.BestBidPrice, price?.Bid)
		.TryAdd(Level1Fields.BestAskPrice, price?.Ask)
		.TryAdd(Level1Fields.Index, price?.Index)
		.TryAdd(Level1Fields.TheorPrice, price?.Oracle)
		.TryAdd(Level1Fields.OpenInterest,
			market.OpenInterest.TryParseAmount()), cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(NadoMarket market,
		NadoPublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = trade.Timestamp.FromNadoSeconds();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.BaseFilled.Abs(),
			OriginSide = trade.BaseFilled >= 0 ? Sides.Buy : Sides.Sell,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(NadoMarket market, NadoCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromNadoSeconds();
		var closeTime = openTime + timeFrame;
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open.ParseX18("candle open"),
			HighPrice = candle.High.ParseX18("candle high"),
			LowPrice = candle.Low.ParseX18("candle low"),
			ClosePrice = candle.Close.ParseX18("candle close"),
			TotalVolume = candle.Volume.ParseAmount("candle volume"),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(NadoMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = market.Symbol,
			ShortName = market.Symbol,
			Class = market.Type == NadoProductTypes.Perpetual
				? "PERPETUAL"
				: "SPOT",
			SecurityType = market.Type.ToStockSharp(),
			PriceStep = market.BookInfo.PriceIncrement.ParseX18(
				"price increment"),
			VolumeStep = market.BookInfo.SizeIncrement.ParseAmount(
				"size increment"),
			MinVolume = market.BookInfo.MinimumSize.TryParseAmount(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		if (market.Type == NadoProductTypes.Perpetual)
			message.TryFillUnderlyingId(market.BaseAsset.Replace("-PERP",
				string.Empty, StringComparison.Ordinal));
		return message;
	}

	private static QuoteChange[] ToDecimalQuotes(string[][] levels, int depth,
		bool isBids)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Length is not >= 2 ||
				!decimal.TryParse(level[0], NumberStyles.Number,
					CultureInfo.InvariantCulture, out var price) ||
				!decimal.TryParse(level[1], NumberStyles.Number,
					CultureInfo.InvariantCulture, out var volume) ||
				price <= 0 || volume < 0)
				throw new InvalidDataException(
					"Nado returned an invalid order-book level.");
			if (volume > 0)
				quotes.Add(new(price, volume));
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private static QuoteChange[] ToX18Quotes(string[][] levels, int depth,
		bool isBids, bool isSnapshot)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Length is not >= 2)
				throw new InvalidDataException(
					"Nado returned an invalid order-book update.");
			var price = level[0].ParseX18("book price");
			var volume = level[1].ParseAmount("book quantity");
			if (price <= 0 || volume < 0)
				throw new InvalidDataException(
					"Nado returned an invalid order-book update.");
			if (!isSnapshot || volume > 0)
				quotes.Add(new(price, volume));
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
		if (message.From is DateTime from && to > from.EnsureNadoUtc())
			return ((to - from.EnsureNadoUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit;
	}
}
