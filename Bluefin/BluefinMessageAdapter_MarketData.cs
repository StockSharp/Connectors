namespace StockSharp.Bluefin;

public partial class BluefinMessageAdapter
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
		foreach (var market in GetMarkets().OrderBy(static market => market.Symbol,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Bluefin))
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
				"Bluefin does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		var ticker = await RestClient.GetTickerAsync(market.Symbol,
			cancellationToken);
		await SendLevel1Async(ticker, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var stream = "Ticker";
		var key = StreamKey(market.Symbol, stream);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.Symbol, stream,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, key);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
				"Bluefin does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		await RefreshDepthAsync(market.Symbol, depth, cancellationToken);
		OrderBookState state;
		using (_sync.EnterScope())
			state = _books[market.Symbol];
		await SendDepthAsync(market.Symbol, state, mdMsg.TransactionId, depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var stream = "Diff_Depth_200_ms";
		var key = StreamKey(market.Symbol, stream);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.Symbol, stream,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, key);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Bluefin trade start time cannot be later than end time.");
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var history = (await RestClient.GetTradesAsync(market.Symbol, from, to,
			count, cancellationToken) ?? [])
			.Where(static trade => trade is not null && trade.ExecutedAtMillis > 0)
			.Where(trade => from is null ||
				trade.ExecutedAtMillis.FromBluefinMilliseconds() >= from.Value)
			.Where(trade => trade.ExecutedAtMillis.FromBluefinMilliseconds() <= to)
			.OrderBy(static trade => trade.ExecutedAtMillis)
			.TakeLast(count)
			.ToArray();
		foreach (var trade in history)
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var stream = "Recent_Trade";
		var key = StreamKey(market.Symbol, stream);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.Symbol, stream,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, key);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandleAsync(mdMsg.OriginalTransactionId,
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
		var interval = timeFrame.ToBluefinInterval();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Bluefin candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var candles = await RestClient.GetCandlesAsync(market.Symbol, interval,
			from, to, count, cancellationToken) ?? [];
		foreach (var candle in candles
			.Where(static candle => candle is { Length: >= 9 })
			.OrderBy(static candle => candle[0], StringComparer.Ordinal))
			await SendCandleAsync(market.Symbol, candle, timeFrame,
				mdMsg.TransactionId, GetCandleOpenTime(candle) + timeFrame <=
					ServerTime ? CandleStates.Finished : CandleStates.Active,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var stream = "Candlestick_" + interval + "_Last";
		var key = StreamKey(market.Symbol, stream);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				TimeFrame = timeFrame,
				Interval = interval,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.Symbol, stream,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, key);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask SendLevel1Async(BluefinTicker ticker,
		long transactionId, CancellationToken cancellationToken)
	{
		if (ticker?.Symbol.IsEmpty() != false)
			return default;
		var market = GetMarket(ticker.Symbol);
		if (market is null)
			return default;
		var time = ticker.UpdatedAtMillis > 0
			? ticker.UpdatedAtMillis.FromBluefinMilliseconds()
			: ticker.LastTimeAtMillis > 0
				? ticker.LastTimeAtMillis.FromBluefinMilliseconds()
				: ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToBluefinSecurityId(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.TickSizeE9.TryParseE9())
		.TryAdd(Level1Fields.VolumeStep, market.StepSizeE9.TryParseE9())
		.TryAdd(Level1Fields.MinVolume,
			market.MinimumOrderQuantityE9.TryParseE9())
		.TryAdd(Level1Fields.MaxVolume,
			market.MaximumLimitOrderQuantityE9.TryParseE9())
		.TryAdd(Level1Fields.State,
			market.Status.EqualsIgnoreCase("ACTIVE") ||
				market.Status.EqualsIgnoreCase("BETA")
				? SecurityStates.Trading
				: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPriceE9.TryParseE9())
		.TryAdd(Level1Fields.LastTradeVolume,
			ticker.LastQuantityE9.TryParseE9())
		.TryAdd(Level1Fields.LastTradeTime,
			ticker.LastTimeAtMillis > 0
				? ticker.LastTimeAtMillis.FromBluefinMilliseconds()
				: null)
		.TryAdd(Level1Fields.SettlementPrice, ticker.MarkPriceE9.TryParseE9())
		.TryAdd(Level1Fields.Index, ticker.OraclePriceE9.TryParseE9())
		.TryAdd(Level1Fields.BestBidPrice,
			ticker.BestBidPriceE9.TryParseE9())
		.TryAdd(Level1Fields.BestBidVolume,
			ticker.BestBidQuantityE9.TryParseE9())
		.TryAdd(Level1Fields.BestAskPrice,
			ticker.BestAskPriceE9.TryParseE9())
		.TryAdd(Level1Fields.BestAskVolume,
			ticker.BestAskQuantityE9.TryParseE9())
		.TryAdd(Level1Fields.HighPrice,
			ticker.HighPrice24HoursE9.TryParseE9())
		.TryAdd(Level1Fields.LowPrice,
			ticker.LowPrice24HoursE9.TryParseE9())
		.TryAdd(Level1Fields.Volume, ticker.Volume24HoursE9.TryParseE9())
		.TryAdd(Level1Fields.OpenInterest,
			ticker.OpenInterestE9.TryParseE9())
		.TryAdd(Level1Fields.Change,
			ticker.PriceChange24HoursE9.TryParseE9()), cancellationToken);
	}

	private async ValueTask RefreshDepthAsync(string symbol, int depth,
		CancellationToken cancellationToken)
	{
		var snapshot = await RestClient.GetDepthAsync(symbol, depth,
			cancellationToken) ?? throw new InvalidDataException(
				"Bluefin returned no order-book snapshot.");
		if (!snapshot.Symbol.Equals(symbol, StringComparison.Ordinal))
			throw new InvalidDataException(
				"Bluefin returned an order book for a different market.");
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
				_books[symbol] = state = new();
			state.ApplySnapshot(snapshot);
		}
	}

	private ValueTask SendDepthAsync(string symbol, OrderBookState state,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		DateTime time;
		QuoteChange[] bids;
		QuoteChange[] asks;
		using (_sync.EnterScope())
		{
			time = state.ServerTime;
			bids = state.GetBids(depth);
			asks = state.GetAsks(depth);
		}
		if (time == default)
			time = ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToBluefinSecurityId(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(BluefinTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.ExecutedAtMillis <= 0)
			return default;
		var time = trade.ExecutedAtMillis.FromBluefinMilliseconds();
		UpdateServerTime(time);
		long? id = long.TryParse(trade.Id, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numericId) ? numericId : null;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToBluefinSecurityId(),
			ServerTime = time,
			TradeId = id,
			TradeStringId = trade.Id,
			TradePrice = trade.PriceE9.ParseE9("trade price"),
			TradeVolume = trade.QuantityE9.ParseE9("trade volume"),
			OriginSide = trade.Side.ToStockSharpSide(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, string[] candle,
		TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
	{
		if (candle is not { Length: >= 9 })
			throw new InvalidDataException(
				"Bluefin returned a malformed candlestick.");
		var openTime = GetCandleOpenTime(candle);
		var closeTime = ParseMilliseconds(candle[6], "candle close time");
		UpdateServerTime(closeTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToBluefinSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle[1].ParseE9("candle open price"),
			HighPrice = candle[2].ParseE9("candle high price"),
			LowPrice = candle[3].ParseE9("candle low price"),
			ClosePrice = candle[4].ParseE9("candle close price"),
			TotalVolume = candle[5].ParseE9("candle volume"),
			TotalPrice = candle[7].ParseE9("candle quote volume"),
			TotalTicks = ParseCount(candle[8], "candle trade count"),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(BluefinMarketStreamPayload candle,
		TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
	{
		if (candle?.Symbol.IsEmpty() != false || candle.StartTime <= 0)
			return default;
		var openTime = candle.StartTime.FromBluefinMilliseconds();
		var closeTime = candle.EndTime > 0
			? candle.EndTime.FromBluefinMilliseconds()
			: openTime + timeFrame;
		UpdateServerTime(closeTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Symbol.ToBluefinSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.OpenPriceE9.ParseE9("candle open price"),
			HighPrice = candle.HighPriceE9.ParseE9("candle high price"),
			LowPrice = candle.LowPriceE9.ParseE9("candle low price"),
			ClosePrice = candle.ClosePriceE9.ParseE9("candle close price"),
			TotalVolume = candle.VolumeE9.ParseE9("candle volume"),
			TotalPrice = candle.QuoteVolumeE9.ParseE9("candle quote volume"),
			TotalTicks = candle.NumberOfTrades.Min(int.MaxValue).Max(0).To<int>(),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);
	}

	private async ValueTask OnMarketMessageAsync(
		BluefinMarketStreamMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Payload is not { } payload)
			return;
		switch (message.Event)
		{
			case "TickerUpdate":
				await OnTickerAsync(payload, cancellationToken);
				break;
			case "RecentTradesUpdates":
				await OnTradesAsync(payload.Trades, cancellationToken);
				break;
			case "OrderbookDiffDepthUpdate":
				await OnDepthUpdateAsync(payload, cancellationToken);
				break;
			case "CandlestickUpdate":
				await OnCandleAsync(payload, cancellationToken);
				break;
		}
	}

	private async ValueTask OnTickerAsync(BluefinTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.Symbol.IsEmpty() != false)
			return;
		long[] transactions;
		using (_sync.EnterScope())
			transactions = [.. _level1Subscriptions.Values
				.Where(subscription => subscription.Symbol.Equals(ticker.Symbol,
					StringComparison.Ordinal))
				.Select(static subscription => subscription.TransactionId)];
		foreach (var transactionId in transactions)
			await SendLevel1Async(ticker, transactionId, cancellationToken);
	}

	private async ValueTask OnTradesAsync(BluefinTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			if (trade?.Symbol.IsEmpty() != false || trade.Id.IsEmpty())
				continue;
			long[] transactions;
			using (_sync.EnterScope())
			{
				if (!_seenTrades.Add(trade.Symbol + ":" + trade.Id))
					continue;
				transactions = [.. _tickSubscriptions.Values
					.Where(subscription => subscription.Symbol.Equals(trade.Symbol,
						StringComparison.Ordinal))
					.Select(static subscription => subscription.TransactionId)];
			}
			foreach (var transactionId in transactions)
				await SendTradeAsync(trade, transactionId, cancellationToken);
		}
	}

	private async ValueTask OnDepthUpdateAsync(
		BluefinMarketStreamPayload update,
		CancellationToken cancellationToken)
	{
		if (update?.Symbol.IsEmpty() != false)
			return;
		DepthSubscription[] subscriptions;
		var isApplied = false;
		using (_sync.EnterScope())
		{
			subscriptions = [.. _depthSubscriptions.Values.Where(subscription =>
				subscription.Symbol.Equals(update.Symbol,
					StringComparison.Ordinal))];
			if (subscriptions.Length > 0 &&
				_books.TryGetValue(update.Symbol, out var state))
				isApplied = state.TryApply(update);
		}
		if (subscriptions.Length == 0)
			return;
		if (!isApplied)
			await RefreshDepthAsync(update.Symbol,
				subscriptions.Max(static subscription => subscription.Depth),
				cancellationToken);
		OrderBookState book;
		using (_sync.EnterScope())
			book = _books[update.Symbol];
		foreach (var subscription in subscriptions)
			await SendDepthAsync(update.Symbol, book,
				subscription.TransactionId, subscription.Depth, cancellationToken);
	}

	private async ValueTask OnCandleAsync(BluefinMarketStreamPayload candle,
		CancellationToken cancellationToken)
	{
		if (candle?.Symbol.IsEmpty() != false || candle.Interval.IsEmpty() ||
			candle.StartTime <= 0)
			return;
		var messages = new List<(long TransactionId, TimeSpan TimeFrame,
			BluefinMarketStreamPayload Candle, CandleStates State)>();
		using (_sync.EnterScope())
			foreach (var subscription in _candleSubscriptions.Values.Where(
				subscription => subscription.Symbol.Equals(candle.Symbol,
					StringComparison.Ordinal) &&
					subscription.Interval.Equals(candle.Interval,
						StringComparison.Ordinal)))
			{
				var previous = subscription.LastCandle;
				if (previous is not null &&
					candle.StartTime > previous.StartTime)
					messages.Add((subscription.TransactionId,
						subscription.TimeFrame, previous, CandleStates.Finished));
				subscription.LastCandle = candle;
				messages.Add((subscription.TransactionId,
					subscription.TimeFrame, candle, CandleStates.Active));
			}
		foreach (var item in messages)
			await SendCandleAsync(item.Candle, item.TimeFrame,
				item.TransactionId, item.State, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, "Ticker")))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribeMarketAsync(removed.Symbol, "Ticker",
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription removed = null;
		const string stream = "Diff_Depth_200_ms";
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, stream)))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribeMarketAsync(removed.Symbol, stream,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		const string stream = "Recent_Trade";
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, stream)))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribeMarketAsync(removed.Symbol, stream,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandleAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription removed = null;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId,
				out var subscription))
			{
				var stream = "Candlestick_" + subscription.Interval + "_Last";
				if (ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, stream)))
					removed = subscription;
			}
		if (removed is not null)
			await SocketClient.UnsubscribeMarketAsync(removed.Symbol,
				"Candlestick_" + removed.Interval + "_Last", cancellationToken);
	}

	private static SecurityMessage CreateSecurity(BluefinMarket market,
		long transactionId)
	{
		var baseAsset = market.BaseAssetSymbol.IsEmpty()
			? market.Symbol.Replace("-PERP", string.Empty,
				StringComparison.Ordinal)
			: market.BaseAssetSymbol;
		var message = new SecurityMessage
		{
			SecurityId = market.ToBluefinSecurityId(),
			Name = $"{baseAsset}/USDC perpetual",
			ShortName = market.Symbol,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = CurrencyTypes.USD,
			PriceStep = market.TickSizeE9.ParseE9("price step"),
			VolumeStep = market.StepSizeE9.ParseE9("volume step"),
			MinVolume = market.MinimumOrderQuantityE9.TryParseE9(),
			MaxVolume = market.MaximumLimitOrderQuantityE9.TryParseE9(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(baseAsset.ToUpperInvariant());
		return message;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.ToUniversalTime())
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit;
	}

	private static DateTime GetCandleOpenTime(string[] candle)
		=> ParseMilliseconds(candle[0], "candle open time");

	private static DateTime ParseMilliseconds(string value, string field)
	{
		if (!long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed))
			throw new InvalidDataException(
				$"Bluefin returned invalid {field} '{value}'.");
		return parsed.FromBluefinMilliseconds();
	}

	private static int ParseCount(string value, string field)
	{
		if (!long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
			throw new InvalidDataException(
				$"Bluefin returned invalid {field} '{value}'.");
		return parsed.Min(int.MaxValue).To<int>();
	}
}
