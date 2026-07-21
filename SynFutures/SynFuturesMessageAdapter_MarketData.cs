namespace StockSharp.SynFutures;

public partial class SynFuturesMessageAdapter
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
		foreach (var market in GetMarkets())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.SynFutures))
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
				"SynFutures does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
			});
			subscribe = AddReference(_channelReferences,
				MarketChannel(market));
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences, MarketChannel(market));
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
				"SynFutures does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var steps = await ApiClient.GetOrderBookAsync(market, cancellationToken);
		await SendDepthAsync(market, steps, mdMsg.TransactionId, depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Market = market,
				Depth = depth,
			});
			subscribe = AddReference(_channelReferences, DepthChannel(market));
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeDepthAsync(market,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences, DepthChannel(market));
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
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"SynFutures public trade history is delivered as the initial " +
				"WebSocket snapshot and cannot be time-filtered.");
		var market = GetMarket(mdMsg.SecurityId);
		var subscription = new TickSubscription
		{
			TransactionId = mdMsg.TransactionId,
			Market = market,
			Count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
			IsHistoryOnly = mdMsg.IsHistoryOnly(),
		};
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddReference(_channelReferences, TradesChannel(market));
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeTradesAsync(market,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences, TradesChannel(market));
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
		_ = timeFrame.ToApiInterval();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.EnsureUtc() ?? to - TimeSpan.FromTicks(
			checked(timeFrame.Ticks * Math.Max(1, count - 1)));
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"SynFutures candle start time cannot be later than end time.");
		var response = await ApiClient.GetCandlesAsync(market, timeFrame, to,
			count, cancellationToken) ?? [];
		var candles = response.Where(static candle => candle is not null &&
				(candle.OpenTimestamp > 0 || candle.Timestamp > 0))
			.Where(candle => GetCandleTime(candle) >= from &&
				GetCandleTime(candle) <= to)
			.OrderBy(GetCandleTime)
			.TakeLast(count)
			.ToArray();
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var subscription = new CandleSubscription
		{
			TransactionId = mdMsg.TransactionId,
			Market = market,
			TimeFrame = timeFrame,
			LastOpenTime = candles.LastOrDefault() is { } last
				? GetCandleTime(last)
				: default,
			NextPollTime = DateTime.UtcNow + GetCandlePollInterval(timeFrame),
		};
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddReference(_channelReferences,
				KlineChannel(market, timeFrame));
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeKlineAsync(market, timeFrame,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences,
					KlineChannel(market, timeFrame));
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnMarketChangedAsync(SynFuturesMarket update,
		CancellationToken cancellationToken)
	{
		var market = StoreMarket(update);
		if (market is null)
			return;
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values.Where(
				subscription => subscription.Market.InstrumentAddress.Equals(
					market.InstrumentAddress,
					StringComparison.OrdinalIgnoreCase) &&
					subscription.Market.Expiry == market.Expiry)];
		foreach (var subscription in subscriptions)
			await SendLevel1Async(market, subscription.TransactionId,
				cancellationToken);
	}

	private async ValueTask OnDepthAsync(string instrument, uint expiry,
		SynFuturesDepthSteps steps, CancellationToken cancellationToken)
	{
		var market = GetMarket(instrument, expiry);
		if (market is null || steps is null)
			return;
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Values.Where(
				subscription => subscription.Market.InstrumentAddress.Equals(
					market.InstrumentAddress,
					StringComparison.OrdinalIgnoreCase) &&
					subscription.Market.Expiry == expiry)];
		foreach (var subscription in subscriptions)
			await SendDepthAsync(market, steps, subscription.TransactionId,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnTradesAsync(string instrument, uint expiry,
		SynFuturesTrade[] trades, CancellationToken cancellationToken)
	{
		var market = GetMarket(instrument, expiry);
		if (market is null || trades is null)
			return;
		TickSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Values.Where(
				subscription => subscription.Market.InstrumentAddress.Equals(
					market.InstrumentAddress,
					StringComparison.OrdinalIgnoreCase) &&
					subscription.Market.Expiry == expiry)];
		foreach (var subscription in subscriptions)
		{
			SynFuturesTrade[] selected;
			using (_sync.EnterScope())
				selected = [.. trades.Where(static trade => trade is not null &&
					!trade.Id.IsEmpty() && trade.Timestamp > 0)
					.Where(trade => subscription.SeenTrades.Add(trade.Id))
					.OrderBy(static trade => trade.Timestamp)
					.TakeLast(subscription.Count)];
			foreach (var trade in selected)
				await SendTradeAsync(market, trade, subscription.TransactionId,
					cancellationToken);
			if (subscription.IsHistoryOnly)
			{
				await UnsubscribeTicksAsync(subscription.TransactionId,
					cancellationToken);
				await SendSubscriptionFinishedAsync(subscription.TransactionId,
					cancellationToken);
			}
		}
	}

	private async ValueTask OnKlineAsync(string instrument, uint expiry,
		SynFuturesCandle candle, CancellationToken cancellationToken)
	{
		var market = GetMarket(instrument, expiry);
		if (market is null || candle is null)
			return;
		CandleSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Values.Where(
				subscription => subscription.Market.InstrumentAddress.Equals(
					market.InstrumentAddress,
					StringComparison.OrdinalIgnoreCase) &&
					subscription.Market.Expiry == expiry)];
		foreach (var subscription in subscriptions)
		{
			var openTime = GetCandleTime(candle);
			if (subscription.LastOpenTime != default &&
				openTime < subscription.LastOpenTime)
				continue;
			await SendCandleAsync(market, candle, subscription.TimeFrame,
				subscription.TransactionId, cancellationToken);
			if (openTime > subscription.LastOpenTime)
				subscription.LastOpenTime = openTime;
		}
	}

	private ValueTask SendLevel1Async(SynFuturesMarket market,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = market.UpdateTime > 0
			? market.UpdateTime.ToUtc()
			: ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, 0.000001m)
		.TryAdd(Level1Fields.VolumeStep, 0.000000000000000001m)
		.TryAdd(Level1Fields.State, market.Condition == 0 &&
				market.AmmStatus == 1
			? SecurityStates.Trading
			: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice, market.FairPrice.TryParseDecimal())
		.TryAdd(Level1Fields.SettlementPrice,
			market.MarkPrice.TryParseDecimal())
		.TryAdd(Level1Fields.Index, market.SpotPrice.TryParseDecimal())
		.TryAdd(Level1Fields.Volume, market.BaseVolume24Hours.TryParseDecimal())
		.TryAdd(Level1Fields.OpenInterest,
			market.OpenInterest.TryParseDecimal()), cancellationToken);
	}

	private ValueTask SendDepthAsync(SynFuturesMarket market,
		SynFuturesDepthSteps steps, long transactionId, int depth,
		CancellationToken cancellationToken)
	{
		var book = steps?.SelectFinestCurrent();
		if (book is null)
			throw new InvalidDataException(
				"SynFutures returned no usable order-book step.");
		var bids = ToQuotes(book.Bids, depth);
		var asks = ToQuotes(book.Asks, depth);
		var time = book.BlockInfo?.Timestamp > 0
			? book.BlockInfo.Timestamp.ToUtc()
			: ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(SynFuturesMarket market,
		SynFuturesTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = trade.Timestamp.ToUtc();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ParseDecimal("trade price"),
			TradeVolume = trade.Size.ParseDecimal("trade size").Abs(),
			OriginSide = ParseSide(trade.Side),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask PollCandleAsync(CandleSubscription subscription,
		CancellationToken cancellationToken)
	{
		var response = await ApiClient.GetCandlesAsync(subscription.Market,
			subscription.TimeFrame, ServerTime, 3, cancellationToken) ?? [];
		foreach (var candle in response.Where(static candle => candle is not null &&
				(candle.OpenTimestamp > 0 || candle.Timestamp > 0))
			.OrderBy(GetCandleTime))
		{
			var openTime = GetCandleTime(candle);
			if (subscription.LastOpenTime != default &&
				openTime < subscription.LastOpenTime)
				continue;
			await SendCandleAsync(subscription.Market, candle,
				subscription.TimeFrame, subscription.TransactionId,
				cancellationToken);
			if (openTime > subscription.LastOpenTime)
				subscription.LastOpenTime = openTime;
		}
	}

	private ValueTask SendCandleAsync(SynFuturesMarket market,
		SynFuturesCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		if (candle.Open <= 0 || candle.High <= 0 || candle.Low <= 0 ||
			candle.Close <= 0 || candle.High < candle.Low ||
			candle.High < candle.Open || candle.High < candle.Close ||
			candle.Low > candle.Open || candle.Low > candle.Close)
			throw new InvalidDataException(
				"SynFutures returned an invalid OHLC candle.");
		var openTime = GetCandleTime(candle);
		var closeTime = candle.CloseTimestamp > 0
			? candle.CloseTimestamp.ToUtc() + TimeSpan.FromSeconds(1)
			: openTime + timeFrame;
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.BaseVolume,
			OriginalTransactionId = transactionId,
			State = closeTime <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(SynFuturesDepthLevel[] levels,
		int depth)
		=> [.. (levels ?? [])
			.Where(static level => level is not null)
			.Select(level => new QuoteChange(
				level.Price.ParseInteger("depth price").FromBaseUnits(18),
				level.BaseSize.ParseInteger("depth size").FromBaseUnits(18)))
			.Where(static quote => quote.Price > 0 && quote.Volume > 0)
			.Take(depth)];

	private static SecurityMessage CreateSecurity(SynFuturesMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.FullSymbol.IsEmpty()
				? market.Symbol + " SynFutures perpetual"
				: market.FullSymbol,
			ShortName = market.Symbol,
			Class = market.MarketType.IsEmpty()
				? "PERPETUAL"
				: market.MarketType.ToUpperInvariant(),
			SecurityType = SecurityTypes.Future,
			Currency = market.QuoteToken?.Symbol.ToCurrency(),
			PriceStep = 0.000001m,
			VolumeStep = 0.000000000000000001m,
			MinVolume = 0.000000000000000001m,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(market.BaseToken?.Symbol?.ToUpperInvariant());
		return message;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.EnsureUtc())
			return ((to - from.EnsureUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit;
	}

	private static DateTime GetCandleTime(SynFuturesCandle candle)
		=> (candle.OpenTimestamp > 0 ? candle.OpenTimestamp : candle.Timestamp)
			.ToUtc();

	private static Sides? ParseSide(string side)
		=> side?.ToLowerInvariant() switch
		{
			"long" or "buy" => Sides.Buy,
			"short" or "sell" => Sides.Sell,
			_ => null,
		};

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseReference(_channelReferences,
					MarketChannel(removed.Market));
		if (unsubscribe)
			await SocketClient.UnsubscribeMarketAsync(removed.Market,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseReference(_channelReferences,
					DepthChannel(removed.Market));
		if (unsubscribe)
			await SocketClient.UnsubscribeDepthAsync(removed.Market,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		TickSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseReference(_channelReferences,
					TradesChannel(removed.Market));
		if (unsubscribe)
			await SocketClient.UnsubscribeTradesAsync(removed.Market,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandleAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseReference(_channelReferences,
					KlineChannel(removed.Market, removed.TimeFrame));
		if (unsubscribe)
			await SocketClient.UnsubscribeKlineAsync(removed.Market,
				removed.TimeFrame, cancellationToken);
	}
}
