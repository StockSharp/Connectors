namespace StockSharp.Synthetix;

public partial class SynthetixMessageAdapter
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
					BoardCodes.Synthetix))
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
				"Synthetix does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await RefreshPricesAsync(cancellationToken);
		SynthetixMarketPrice price;
		using (_sync.EnterScope())
			_prices.TryGetValue(market.Symbol, out price);
		if (price is not null)
			await SendLevel1Async(market, price, mdMsg.TransactionId,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var key = StreamKey(market.Symbol, "prices");
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
				await SocketClient.SubscribePricesAsync(market.Symbol,
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
				"Synthetix does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var snapshot = await ApiClient.GetOrderBookAsync(market.Symbol,
			NormalizeRestDepth(depth), cancellationToken) ??
			throw new InvalidDataException(
				"Synthetix returned no order-book snapshot.");
		await SendDepthAsync(market.Symbol, snapshot.Bids, snapshot.Asks,
			mdMsg.TransactionId, depth, ApiClient.ServerTime, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var key = StreamKey(market.Symbol, "book");
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
				await SocketClient.SubscribeBookAsync(market.Symbol, MarketDepth,
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
				"Synthetix trade start time cannot be later than end time.");
		var count = (mdMsg.Count ?? 100).Min(100).Max(1).To<int>();
		var trades = (await ApiClient.GetLastTradesAsync(market.Symbol, count,
			cancellationToken))?.Trades ?? [];
		foreach (var trade in trades
			.Where(static trade => trade is not null && trade.Timestamp > 0)
			.Where(trade => from is null ||
				trade.Timestamp.FromSynthetixMilliseconds() >= from.Value)
			.Where(trade => trade.Timestamp.FromSynthetixMilliseconds() <= to)
			.OrderBy(static trade => trade.Timestamp))
		{
			MarkPublicTrade(trade.Symbol, trade.TradeId);
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var key = StreamKey(market.Symbol, "trades");
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
				await SocketClient.SubscribeTradesAsync(market.Symbol,
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
		var interval = timeFrame.ToSynthetixInterval();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Synthetix candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var response = await ApiClient.GetCandlesAsync(market.Symbol, interval,
			count, from, to, cancellationToken);
		foreach (var candle in (response?.Candles ?? [])
			.Where(static candle => candle is not null && candle.OpenTime > 0)
			.OrderBy(static candle => candle.OpenTime))
			await SendCandleAsync(market.Symbol, candle, timeFrame,
				mdMsg.TransactionId, candle.CloseTime > 0 &&
					candle.CloseTime.FromSynthetixMilliseconds() <= ServerTime
						? CandleStates.Finished
						: CandleStates.Active,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var key = StreamKey(market.Symbol, "candles:" + interval);
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
				await SocketClient.SubscribeCandlesAsync(market.Symbol, interval,
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

	private ValueTask SendLevel1Async(SynthetixMarket market,
		SynthetixMarketPrice price, long transactionId,
		CancellationToken cancellationToken)
	{
		if (market is null || price is null)
			return default;
		var time = price.Timestamp > 0
			? price.Timestamp.FromSynthetixMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToSynthetixSecurityId(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep,
			market.PriceIncrement.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.VolumeStep,
			market.OrderSizeIncrement.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.MinVolume,
			market.MinimumOrderSize.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.MaxVolume,
			market.MaximumLimitOrderSize.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.State, market.IsOpen
			? SecurityStates.Trading
			: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice,
			price.LastPrice.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.SettlementPrice,
			price.MarkPrice.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.Index,
			price.IndexPrice.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.BestBidPrice,
			price.BestBid.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.BestAskPrice,
			price.BestAsk.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.ClosePrice,
			price.PreviousDayPrice.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.Volume,
			price.Volume24Hours.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.Turnover,
			price.QuoteVolume24Hours.TryParseSynthetixDecimal())
		.TryAdd(Level1Fields.OpenInterest,
			price.OpenInterest.TryParseSynthetixDecimal()), cancellationToken);
	}

	private ValueTask SendDepthAsync(string symbol,
		SynthetixRestPriceLevel[] bids, SynthetixRestPriceLevel[] asks,
		long transactionId, int depth, DateTime time,
		CancellationToken cancellationToken)
		=> SendDepthAsync(symbol,
			(bids ?? []).Select(static level => new SynthetixBookLevel
			{
				Price = level.Price,
				Quantity = level.Quantity,
			}).ToArray(),
			(asks ?? []).Select(static level => new SynthetixBookLevel
			{
				Price = level.Price,
				Quantity = level.Quantity,
			}).ToArray(), transactionId, depth, time, cancellationToken);

	private ValueTask SendDepthAsync(string symbol, SynthetixBookLevel[] bids,
		SynthetixBookLevel[] asks, long transactionId, int depth, DateTime time,
		CancellationToken cancellationToken)
	{
		if (time == default)
			time = ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToSynthetixSecurityId(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(bids, depth, "bid", true),
			Asks = ToQuotes(asks, depth, "ask", false),
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(SynthetixPublicTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.Timestamp <= 0)
			return default;
		return SendTradeAsync(trade.Symbol, trade.TradeId, trade.Side,
			trade.Price, trade.Quantity,
			trade.Timestamp.FromSynthetixMilliseconds(), transactionId,
			cancellationToken);
	}

	private ValueTask SendTradeAsync(SynthetixTradeUpdate trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.Timestamp.IsEmpty())
			return default;
		return SendTradeAsync(trade.Symbol, trade.TradeId, trade.Side,
			trade.Price, trade.Quantity,
			trade.Timestamp.ParseSynthetixTime("trade time"), transactionId,
			cancellationToken);
	}

	private ValueTask SendTradeAsync(string symbol, string tradeId, string side,
		string price, string quantity, DateTime time, long transactionId,
		CancellationToken cancellationToken)
	{
		UpdateServerTime(time);
		long? id = long.TryParse(tradeId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numericId) ? numericId : null;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToSynthetixSecurityId(),
			ServerTime = time,
			TradeId = id,
			TradeStringId = tradeId,
			TradePrice = price.ParseSynthetixDecimal("trade price"),
			TradeVolume = quantity.ParseSynthetixDecimal("trade volume"),
			OriginSide = side.ToStockSharpSide(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, SynthetixCandle candle,
		TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
	{
		var openTime = candle.OpenTime.FromSynthetixMilliseconds();
		var closeTime = candle.CloseTime > 0
			? candle.CloseTime.FromSynthetixMilliseconds()
			: openTime + timeFrame;
		UpdateServerTime(closeTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToSynthetixSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.OpenPrice.ParseSynthetixDecimal("candle open"),
			HighPrice = candle.HighPrice.ParseSynthetixDecimal("candle high"),
			LowPrice = candle.LowPrice.ParseSynthetixDecimal("candle low"),
			ClosePrice = candle.ClosePrice.ParseSynthetixDecimal("candle close"),
			TotalVolume = candle.Volume.ParseSynthetixDecimal("candle volume"),
			TotalPrice = candle.QuoteVolume.TryParseSynthetixDecimal() ?? 0m,
			TotalTicks = candle.TradeCount.Min(int.MaxValue).Max(0).To<int>(),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(SynthetixCandleUpdate candle,
		TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
	{
		var openTime = candle.OpenTime.ParseSynthetixTime("candle open time");
		var closeTime = candle.CloseTime.IsEmpty()
			? openTime + timeFrame
			: candle.CloseTime.ParseSynthetixTime("candle close time");
		UpdateServerTime(closeTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Symbol.ToSynthetixSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.OpenPrice.ParseSynthetixDecimal("candle open"),
			HighPrice = candle.HighPrice.ParseSynthetixDecimal("candle high"),
			LowPrice = candle.LowPrice.ParseSynthetixDecimal("candle low"),
			ClosePrice = candle.ClosePrice.ParseSynthetixDecimal("candle close"),
			TotalVolume = candle.Volume.ParseSynthetixDecimal("candle volume"),
			TotalPrice = candle.QuoteVolume.TryParseSynthetixDecimal() ?? 0m,
			TotalTicks = candle.TradeCount.Min(int.MaxValue).Max(0).To<int>(),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);
	}

	private async ValueTask OnPriceUpdateAsync(
		SynthetixSocketNotification<SynthetixPriceUpdate> message,
		CancellationToken cancellationToken)
	{
		var update = message?.Data;
		if (update?.Symbol.IsEmpty() != false || update.Price.IsEmpty())
			return;
		var time = !update.LastUpdateTime.IsEmpty()
			? update.LastUpdateTime.ParseSynthetixTime("price update time")
			: message.Timestamp > 0
				? message.Timestamp.FromSynthetixMilliseconds()
				: ServerTime;
		UpdateServerTime(time);
		long[] transactions;
		using (_sync.EnterScope())
			transactions = [.. _level1Subscriptions.Values
				.Where(subscription => subscription.Symbol.Equals(update.Symbol,
					StringComparison.Ordinal))
				.Select(static subscription => subscription.TransactionId)];
		foreach (var transactionId in transactions)
		{
			var output = new Level1ChangeMessage
			{
				SecurityId = update.Symbol.ToSynthetixSecurityId(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			};
			var value = update.Price.ParseSynthetixDecimal("price update");
			switch (update.UpdateType?.ToLowerInvariant())
			{
				case "mark":
					output.TryAdd(Level1Fields.SettlementPrice, value);
					break;
				case "index":
					output.TryAdd(Level1Fields.Index, value);
					break;
				case "last":
					output.TryAdd(Level1Fields.LastTradePrice, value);
					output.TryAdd(Level1Fields.LastTradeTime, time);
					break;
				case "mid":
					output.TryAdd(Level1Fields.AveragePrice, value);
					break;
				default:
					continue;
			}
			await SendOutMessageAsync(output, cancellationToken);
		}
	}

	private async ValueTask OnTradeUpdateAsync(
		SynthetixSocketNotification<SynthetixTradeUpdate> message,
		CancellationToken cancellationToken)
	{
		var trade = message?.Data;
		if (trade?.Symbol.IsEmpty() != false || trade.TradeId.IsEmpty() ||
			!MarkPublicTrade(trade.Symbol, trade.TradeId))
			return;
		long[] transactions;
		using (_sync.EnterScope())
			transactions = [.. _tickSubscriptions.Values
				.Where(subscription => subscription.Symbol.Equals(trade.Symbol,
					StringComparison.Ordinal))
				.Select(static subscription => subscription.TransactionId)];
		foreach (var transactionId in transactions)
			await SendTradeAsync(trade, transactionId, cancellationToken);
	}

	private async ValueTask OnBookUpdateAsync(
		SynthetixSocketNotification<SynthetixBookUpdate> message,
		CancellationToken cancellationToken)
	{
		var book = message?.Data;
		if (book?.Symbol.IsEmpty() != false)
			return;
		var time = !book.Timestamp.IsEmpty()
			? book.Timestamp.ParseSynthetixTime("order-book time")
			: message.Timestamp > 0
				? message.Timestamp.FromSynthetixMilliseconds()
				: ServerTime;
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Values.Where(subscription =>
				subscription.Symbol.Equals(book.Symbol, StringComparison.Ordinal))];
		foreach (var subscription in subscriptions)
			await SendDepthAsync(book.Symbol, book.Bids, book.Asks,
				subscription.TransactionId, subscription.Depth, time,
				cancellationToken);
	}

	private async ValueTask OnCandleUpdateAsync(
		SynthetixSocketNotification<SynthetixCandleUpdate> message,
		CancellationToken cancellationToken)
	{
		var candle = message?.Data;
		if (candle?.Symbol.IsEmpty() != false || candle.TimeFrame.IsEmpty() ||
			candle.OpenTime.IsEmpty())
			return;
		var openTime = candle.OpenTime.ParseSynthetixTime("candle open time");
		var outputs = new List<(long TransactionId, TimeSpan TimeFrame,
			SynthetixCandleUpdate Candle, CandleStates State)>();
		using (_sync.EnterScope())
			foreach (var subscription in _candleSubscriptions.Values.Where(
				subscription => subscription.Symbol.Equals(candle.Symbol,
					StringComparison.Ordinal) && subscription.Interval.Equals(
						candle.TimeFrame, StringComparison.Ordinal)))
			{
				var previous = subscription.LastCandle;
				if (previous is not null && openTime >
					previous.OpenTime.ParseSynthetixTime("candle open time"))
					outputs.Add((subscription.TransactionId,
						subscription.TimeFrame, previous, CandleStates.Finished));
				subscription.LastCandle = candle;
				outputs.Add((subscription.TransactionId, subscription.TimeFrame,
					candle, CandleStates.Active));
			}
		foreach (var output in outputs)
			await SendCandleAsync(output.Candle, output.TimeFrame,
				output.TransactionId, output.State, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, "prices")))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribePricesAsync(removed.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription removed = null;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, "book")))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribeBookAsync(removed.Symbol, MarketDepth,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences,
					StreamKey(subscription.Symbol, "trades")))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribeTradesAsync(removed.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandleAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription removed = null;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_streamReferences, StreamKey(subscription.Symbol,
					"candles:" + subscription.Interval)))
				removed = subscription;
		if (removed is not null)
			await SocketClient.UnsubscribeCandlesAsync(removed.Symbol,
				removed.Interval, cancellationToken);
	}

	private bool MarkPublicTrade(string symbol, string tradeId)
	{
		if (symbol.IsEmpty() || tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			if (_seenTrades.Count >= 50000)
				_seenTrades.Clear();
			return _seenTrades.Add(symbol + ":" + tradeId);
		}
	}

	private static QuoteChange[] ToQuotes(SynthetixBookLevel[] levels,
		int depth, string side, bool isDescending)
	{
		var parsed = (levels ?? []).Select(level =>
		{
			if (level is null)
				throw new InvalidDataException(
					$"Synthetix returned a null {side} level.");
			var price = level.Price.ParseSynthetixDecimal(side + " price");
			var volume = level.Quantity.ParseSynthetixDecimal(side + " quantity");
			if (price <= 0 || volume < 0)
				throw new InvalidDataException(
					$"Synthetix returned an invalid {side} level.");
			return new QuoteChange(price, volume);
		});
		return [.. (isDescending
			? parsed.OrderByDescending(static quote => quote.Price)
			: parsed.OrderBy(static quote => quote.Price)).Take(depth)];
	}

	private static SecurityMessage CreateSecurity(SynthetixMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.Symbol.ToSynthetixSecurityId(),
			Name = market.Description.IsEmpty()
				? market.Symbol + " perpetual"
				: market.Description + " perpetual",
			ShortName = market.Symbol,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = CurrencyTypes.USD,
			PriceStep = market.PriceIncrement.ParseSynthetixDecimal("price step"),
			VolumeStep = market.OrderSizeIncrement.ParseSynthetixDecimal(
				"volume step"),
			MinVolume = market.MinimumOrderSize.TryParseSynthetixDecimal(),
			MaxVolume = market.MaximumLimitOrderSize.TryParseSynthetixDecimal(),
			Multiplier = market.ContractSize > 0 ? market.ContractSize : 1m,
			OriginalTransactionId = transactionId,
		};
		if (!market.BaseAsset.IsEmpty())
			message.TryFillUnderlyingId(market.BaseAsset.ToUpperInvariant());
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

	private static int NormalizeRestDepth(int depth)
		=> depth <= 5 ? 5 : depth <= 10 ? 10 : depth <= 20 ? 20 :
			depth <= 50 ? 50 : depth <= 100 ? 100 : depth <= 500 ? 500 : 1000;
}
