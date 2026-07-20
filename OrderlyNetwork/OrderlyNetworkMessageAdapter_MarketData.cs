namespace StockSharp.OrderlyNetwork;

public partial class OrderlyNetworkMessageAdapter
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
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.OrderlyNetwork))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.OrdinalIgnoreCase))
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
				"Orderly Network does not publish historical Level1 changes.");

		var symbol = GetSymbol(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(symbol, mdMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var topics = new[] { symbol + "@ticker", symbol + "@bbo" };
		string[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol });
			added = [.. topics.Where(topic =>
				AddReference(_publicStreamReferences, topic))];
		}
		try
		{
			await SubscribePublicTopicsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				foreach (var topic in topics)
					ReleaseReference(_publicStreamReferences, topic);
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
				"Orderly Network does not publish historical order books.");

		var symbol = GetSymbol(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100).Max(1).Min(500);
		await SendDepthSnapshotAsync(symbol, mdMsg.TransactionId, depth,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var topic = symbol + "@orderbookupdate";
		bool isAdded;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Depth = depth,
			});
			isAdded = AddReference(_publicStreamReferences, topic);
		}
		try
		{
			if (isAdded)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_publicStreamReferences, topic);
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

		var symbol = GetSymbol(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? HistoryLimit).Min(1000).Max(1).To<int>();
		var from = mdMsg.From?.EnsureOrderlyUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureOrderlyUtc();
		var trades = await RestClient.GetPublicTradesAsync(symbol, count,
			cancellationToken);
		foreach (var trade in trades
			.Where(static item => item is not null && item.Timestamp > 0)
			.Where(item => from is null ||
				item.Timestamp.FromOrderlyMilliseconds() >= from.Value)
			.Where(item => item.Timestamp.FromOrderlyMilliseconds() <= to)
			.OrderBy(static item => item.Timestamp)
			.TakeLast(count))
			await SendPublicTradeAsync(trade, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var topic = symbol + "@trade";
		bool isAdded;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol });
			isAdded = AddReference(_publicStreamReferences, topic);
		}
		try
		{
			if (isAdded)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_publicStreamReferences, topic);
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

		var symbol = GetSymbol(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToOrderlyInterval();
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1).To<int>();
		var to = (mdMsg.To ?? ServerTime).EnsureOrderlyUtc();
		var defaultSpan = TimeSpan.FromTicks(checked(timeFrame.Ticks * count));
		var from = (mdMsg.From ?? to.Subtract(defaultSpan)).EnsureOrderlyUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Orderly candle start time cannot be later than end time.");
		var candles = await RestClient.GetCandlesAsync(symbol, interval, from, to,
			count, cancellationToken);
		foreach (var candle in (candles?.Rows ?? [])
			.Where(static item => item is not null && item.Timestamp > 0)
			.OrderBy(static item => item.Timestamp)
			.TakeLast(count))
			await SendCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var topic = symbol + "@kline_" + interval;
		bool isAdded;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				TimeFrame = timeFrame,
				Interval = interval,
			});
			isAdded = AddReference(_publicStreamReferences, topic);
		}
		try
		{
			if (isAdded)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_publicStreamReferences, topic);
			}
			throw;
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		string[] topics = [];
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription))
				topics = new[] { subscription.Symbol + "@ticker",
					subscription.Symbol + "@bbo" }
					.Where(topic => ReleaseReference(_publicStreamReferences, topic))
					.ToArray();
		await UnsubscribePublicTopicsAsync(topics, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string topic = null;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription))
			{
				var candidate = subscription.Symbol + "@orderbookupdate";
				if (ReleaseReference(_publicStreamReferences, candidate))
					topic = candidate;
				if (!_depthSubscriptions.Values.Any(item =>
					item.Symbol.Equals(subscription.Symbol,
						StringComparison.OrdinalIgnoreCase)))
					_depthTimestamps.Remove(subscription.Symbol);
			}
		if (!topic.IsEmpty() && _publicSocket is not null)
			await _publicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string topic = null;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
			{
				var candidate = subscription.Symbol + "@trade";
				if (ReleaseReference(_publicStreamReferences, candidate))
					topic = candidate;
			}
		if (!topic.IsEmpty() && _publicSocket is not null)
			await _publicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string topic = null;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
			{
				var candidate = subscription.Symbol + "@kline_" +
					subscription.Interval;
				if (ReleaseReference(_publicStreamReferences, candidate))
					topic = candidate;
			}
		if (!topic.IsEmpty() && _publicSocket is not null)
			await _publicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask SubscribePublicTopicsAsync(IEnumerable<string> topics,
		CancellationToken cancellationToken)
	{
		var subscribed = new List<string>();
		try
		{
			foreach (var topic in topics)
			{
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
				subscribed.Add(topic);
			}
		}
		catch
		{
			foreach (var topic in subscribed.AsEnumerable().Reverse())
				try
				{
					await PublicSocket.UnsubscribeAsync(topic, cancellationToken);
				}
				catch (Exception error)
				{
					await SendOutErrorAsync(error, cancellationToken);
				}
			throw;
		}
	}

	private async ValueTask UnsubscribePublicTopicsAsync(
		IEnumerable<string> topics, CancellationToken cancellationToken)
	{
		if (_publicSocket is null)
			return;
		foreach (var topic in topics)
			await _publicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private SecurityMessage CreateSecurity(OrderlyNetworkSymbolInfo market,
		long originalTransactionId)
	{
		var (baseAsset, quoteAsset) = ParseAssets(market.Symbol);
		return new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = market.DisplayName.IsEmpty() ? market.Symbol : market.DisplayName,
			SecurityType = SecurityTypes.Future,
			Currency = quoteAsset.IsEmpty()
				? null
				: quoteAsset.To<CurrencyTypes?>(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = market.MinimumBase,
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(baseAsset);
	}

	private static (string BaseAsset, string QuoteAsset) ParseAssets(string symbol)
	{
		var parts = symbol.ThrowIfEmpty(nameof(symbol)).Split('_');
		return parts.Length >= 3 && parts[0].EqualsIgnoreCase("PERP")
			? (parts[1], parts[2])
			: parts.Length >= 2
				? (parts[^2], parts[^1])
				: (symbol, null);
	}

	private ValueTask SendLevel1SnapshotAsync(string symbol,
		long transactionId, CancellationToken cancellationToken)
	{
		var market = GetMarket(symbol);
		var future = GetFuture(symbol);
		var time = ServerTime;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.PriceStep)
		.TryAdd(Level1Fields.VolumeStep, market.VolumeStep)
		.TryAdd(Level1Fields.MinPrice, market.MinimumQuote)
		.TryAdd(Level1Fields.MaxPrice, market.MaximumQuote)
		.TryAdd(Level1Fields.MinVolume, market.MinimumBase)
		.TryAdd(Level1Fields.MaxVolume, market.MaximumBase)
		.TryAdd(Level1Fields.State,
			market.Status.EqualsIgnoreCase("ACTIVE") ||
				market.Status.EqualsIgnoreCase("TRADING")
				? SecurityStates.Trading
				: SecurityStates.Stoped)
		.TryAdd(Level1Fields.OpenPrice, future?.Open)
		.TryAdd(Level1Fields.HighPrice, future?.High)
		.TryAdd(Level1Fields.LowPrice, future?.Low)
		.TryAdd(Level1Fields.LastTradePrice, future?.Close)
		.TryAdd(Level1Fields.Volume, future?.Volume)
		.TryAdd(Level1Fields.OpenInterest, future?.OpenInterest)
		.TryAdd(Level1Fields.Index, future?.IndexPrice)
		.TryAdd(Level1Fields.SettlementPrice, future?.MarkPrice),
			cancellationToken);
	}

	private async ValueTask SendDepthSnapshotAsync(string symbol,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		var book = await RestClient.GetOrderbookAsync(symbol, depth,
			cancellationToken) ?? throw new InvalidDataException(
			"Orderly Network returned no order-book snapshot.");
		var timestamp = book.Timestamp > 0
			? book.Timestamp
			: RestClient.ServerTime.ToOrderlyMilliseconds();
		UpdateServerTime(timestamp);
		using (_sync.EnterScope())
			_depthTimestamps[symbol] = timestamp;
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = timestamp.FromOrderlyMilliseconds(),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, depth, true),
			Asks = ToQuotes(book.Asks, depth, false),
		}, cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(OrderlyNetworkMarketTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = trade.Timestamp.FromOrderlyMilliseconds();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeStringId = CreateTradeId(trade.Symbol, trade.Timestamp,
				trade.Price, trade.Quantity, trade.Side),
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, OrderlyNetworkCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromOrderlyMilliseconds();
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = ServerTime >= openTime + timeFrame
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private async ValueTask OnBboAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketBbo> envelope,
		CancellationToken cancellationToken)
	{
		var quote = envelope?.Data;
		if (quote?.Symbol.IsEmpty() != false)
			return;
		UpdateServerTime(envelope.Timestamp);
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.Equals(quote.Symbol,
					StringComparison.OrdinalIgnoreCase)).Select(static pair => pair.Key)];
		var time = envelope.Timestamp.FromOrderlyMilliseconds();
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = quote.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidQuantity)
			.TryAdd(Level1Fields.BestBidTime,
				quote.BidPrice is null ? null : time)
			.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskQuantity)
			.TryAdd(Level1Fields.BestAskTime,
				quote.AskPrice is null ? null : time), cancellationToken);
	}

	private async ValueTask OnTickerAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketTicker> envelope,
		CancellationToken cancellationToken)
	{
		var ticker = envelope?.Data;
		if (ticker?.Symbol.IsEmpty() != false)
			return;
		UpdateServerTime(envelope.Timestamp);
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.Equals(ticker.Symbol,
					StringComparison.OrdinalIgnoreCase)).Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Symbol.ToStockSharp(),
				ServerTime = envelope.Timestamp.FromOrderlyMilliseconds(),
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.OpenPrice, ticker.Open)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
			.TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);
	}

	private async ValueTask OnPublicTradeAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketTrade> envelope,
		CancellationToken cancellationToken)
	{
		var trade = envelope?.Data;
		if (trade?.Symbol.IsEmpty() != false)
			return;
		UpdateServerTime(envelope.Timestamp);
		long[] tickIds;
		long[] level1Ids;
		using (_sync.EnterScope())
		{
			tickIds = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.Equals(trade.Symbol,
					StringComparison.OrdinalIgnoreCase)).Select(static pair => pair.Key)];
			level1Ids = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.Equals(trade.Symbol,
					StringComparison.OrdinalIgnoreCase)).Select(static pair => pair.Key)];
		}
		var time = envelope.Timestamp.FromOrderlyMilliseconds();
		var tradeId = CreateTradeId(trade.Symbol, envelope.Timestamp, trade.Price,
			trade.Quantity, trade.Side);
		foreach (var id in tickIds)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = trade.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
				TradeStringId = tradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.Quantity,
				OriginSide = trade.Side.ToStockSharp(),
			}, cancellationToken);
		foreach (var id in level1Ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = trade.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.LastTradePrice, trade.Price)
			.TryAdd(Level1Fields.LastTradeVolume, trade.Quantity)
			.TryAdd(Level1Fields.LastTradeTime, time)
			.TryAdd(Level1Fields.LastTradeOrigin,
				trade.Side.ToStockSharp()), cancellationToken);
	}

	private async ValueTask OnCandleAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketCandle> envelope,
		CancellationToken cancellationToken)
	{
		var candle = envelope?.Data;
		if (candle?.Symbol.IsEmpty() != false || candle.Interval.IsEmpty())
			return;
		UpdateServerTime(envelope.Timestamp);
		(long Id, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.Equals(candle.Symbol,
					StringComparison.OrdinalIgnoreCase) &&
				pair.Value.Interval.Equals(candle.Interval,
					StringComparison.Ordinal)).Select(static pair =>
					(pair.Key, pair.Value.TimeFrame))];
		var openTime = candle.StartTime.FromOrderlyMilliseconds();
		var closeTime = candle.EndTime > 0
			? candle.EndTime.FromOrderlyMilliseconds()
			: openTime + candle.Interval.ToOrderlyTimeFrame();
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = candle.Symbol.ToStockSharp(),
				OpenTime = openTime,
				CloseTime = closeTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TypedArg = subscription.TimeFrame,
				OriginalTransactionId = subscription.Id,
				State = envelope.Timestamp >= candle.EndTime
					? CandleStates.Finished
					: CandleStates.Active,
			}, cancellationToken);
	}

	private async ValueTask OnDepthAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketDepth> envelope,
		CancellationToken cancellationToken)
	{
		var update = envelope?.Data;
		if (update?.Symbol.IsEmpty() != false || envelope.Timestamp <= 0)
			return;
		await _depthSync.WaitAsync(cancellationToken);
		try
		{
			(long Id, int Depth)[] subscriptions;
			long previous;
			using (_sync.EnterScope())
			{
				subscriptions = [.. _depthSubscriptions.Where(pair =>
					pair.Value.Symbol.Equals(update.Symbol,
						StringComparison.OrdinalIgnoreCase)).Select(static pair =>
						(pair.Key, pair.Value.Depth))];
				_depthTimestamps.TryGetValue(update.Symbol, out previous);
			}
			if (subscriptions.Length == 0 || envelope.Timestamp <= previous)
				return;
			if (previous > 0 && update.PreviousTimestamp > previous)
			{
				await ResynchronizeSymbolDepthsAsync(update.Symbol, subscriptions,
					cancellationToken);
				return;
			}
			using (_sync.EnterScope())
				_depthTimestamps[update.Symbol] = envelope.Timestamp;
			UpdateServerTime(envelope.Timestamp);
			foreach (var subscription in subscriptions)
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					SecurityId = update.Symbol.ToStockSharp(),
					ServerTime = envelope.Timestamp.FromOrderlyMilliseconds(),
					OriginalTransactionId = subscription.Id,
					State = QuoteChangeStates.Increment,
					Bids = ToQuotes(update.Bids, subscription.Depth, true),
					Asks = ToQuotes(update.Asks, subscription.Depth, false),
				}, cancellationToken);
		}
		finally
		{
			_depthSync.Release();
		}
	}

	private async ValueTask ResynchronizeAllDepthsAsync(
		CancellationToken cancellationToken)
	{
		string[] symbols;
		using (_sync.EnterScope())
			symbols = [.. _depthSubscriptions.Values.Select(static item => item.Symbol)
				.Distinct(StringComparer.OrdinalIgnoreCase)];
		foreach (var symbol in symbols)
		{
			(long Id, int Depth)[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions.Where(pair =>
					pair.Value.Symbol.Equals(symbol,
						StringComparison.OrdinalIgnoreCase)).Select(static pair =>
						(pair.Key, pair.Value.Depth))];
			await ResynchronizeSymbolDepthsAsync(symbol, subscriptions,
				cancellationToken);
		}
	}

	private async ValueTask ResynchronizeSymbolDepthsAsync(string symbol,
		(long Id, int Depth)[] subscriptions,
		CancellationToken cancellationToken)
	{
		if (subscriptions.Length == 0)
			return;
		var maximum = subscriptions.Max(static item => item.Depth);
		var book = await RestClient.GetOrderbookAsync(symbol, maximum,
			cancellationToken) ?? throw new InvalidDataException(
			"Orderly Network returned no order-book recovery snapshot.");
		var timestamp = book.Timestamp > 0
			? book.Timestamp
			: RestClient.ServerTime.ToOrderlyMilliseconds();
		using (_sync.EnterScope())
			_depthTimestamps[symbol] = timestamp;
		UpdateServerTime(timestamp);
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = timestamp.FromOrderlyMilliseconds(),
				OriginalTransactionId = subscription.Id,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = ToQuotes(book.Bids, subscription.Depth, true),
				Asks = ToQuotes(book.Asks, subscription.Depth, false),
			}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(OrderlyNetworkBookLevel[] levels,
		int depth, bool isBids)
		=> [.. (levels ?? [])
			.Where(static item => item is not null && item.Price > 0 &&
				item.Quantity >= 0)
			.OrderBy(item => isBids ? -item.Price : item.Price)
			.Take(depth)
			.Select(static item => new QuoteChange
			{
				Price = item.Price,
				Volume = item.Quantity,
			})];

	private static QuoteChange[] ToQuotes(OrderlyNetworkSocketLevel[] levels,
		int depth, bool isBids)
		=> [.. (levels ?? [])
			.Where(static item => item is not null && item.Price > 0 &&
				item.Quantity >= 0)
			.OrderBy(item => isBids ? -item.Price : item.Price)
			.Take(depth)
			.Select(static item => new QuoteChange
			{
				Price = item.Price,
				Volume = item.Quantity,
			})];

	private static string CreateTradeId(string symbol, long timestamp,
		decimal price, decimal quantity, OrderlyNetworkSides side)
		=> symbol + ":" + timestamp.ToString(CultureInfo.InvariantCulture) +
			":" + price.ToWire() + ":" + quantity.ToWire() + ":" + side;
}
