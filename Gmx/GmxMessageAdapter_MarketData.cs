namespace StockSharp.Gmx;

public partial class GmxMessageAdapter
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
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Gmx))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.OrdinalIgnoreCase))
				continue;
			var securityType = market.ToSecurityType();
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
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
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
				"GMX does not publish historical Level1 changes.");

		var market = GetMarket(mdMsg.SecurityId);
		await RefreshTickerDataAsync(cancellationToken);
		await SendLevel1Async(market, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				MarketAddress = market.MarketAddress,
			});
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
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.EnsureGmxUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureGmxUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"GMX execution start time cannot be later than end time.");
		var count = (mdMsg.Count ?? (mdMsg.IsHistoryOnly()
			? HistoryLimit
			: 100)).Min(HistoryLimit).Max(1).To<int>();
		var history = await ReadTradesAsync(market, from, to, count,
			cancellationToken);
		foreach (var trade in history)
		{
			await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken);
			using (_sync.EnterScope())
				_seenPublicTrades.Add(trade.Id);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				MarketAddress = market.MarketAddress,
			});
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
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
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
		var resolution = timeFrame.ToGmxTimeFrame();
		var from = mdMsg.From?.EnsureGmxUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureGmxUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"GMX candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var candles = await ApiClient.GetCandlesAsync(market.Symbol, resolution,
			count, from?.ToGmxMilliseconds(), cancellationToken);
		var selected = candles
			.Where(static candle => candle is not null && candle.Timestamp > 0)
			.Where(candle => from is null ||
				candle.Timestamp >= from.Value.ToGmxMilliseconds())
			.Where(candle => candle.Timestamp <= to.ToGmxMilliseconds())
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(count)
			.ToArray();
		foreach (var candle in selected)
			await SendCandleAsync(market, candle, timeFrame, mdMsg.TransactionId,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				MarketAddress = market.MarketAddress,
				TimeFrame = timeFrame,
				LastOpenTime = selected.LastOrDefault()?.Timestamp
					.FromGmxMilliseconds(),
			});
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RefreshLevel1SubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		await RefreshTickerDataAsync(cancellationToken);
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values];
		foreach (var subscription in subscriptions)
		{
			var market = GetMarketByAddress(subscription.MarketAddress);
			if (market is not null)
				await SendLevel1Async(market, subscription.TransactionId,
					cancellationToken);
		}
	}

	private async ValueTask RefreshTickerDataAsync(
		CancellationToken cancellationToken)
	{
		var tickers = await ApiClient.GetTickersAsync(cancellationToken);
		using (_sync.EnterScope())
			foreach (var ticker in tickers)
				if (ticker?.MarketTokenAddress.IsEmpty() == false &&
					_marketsByAddress.TryGetValue(ticker.MarketTokenAddress,
						out var market))
					market.Ticker = ticker;
		UpdateServerTime(DateTime.UtcNow);
	}

	private async ValueTask RefreshTradeSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Values];
		var addresses = subscriptions.Select(static item => item.MarketAddress)
			.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (addresses.Length == 0)
			return;
		var response = await ApiClient.SearchTradesAsync(new()
		{
			IsForAllAccounts = true,
			MarketsDirections = [.. addresses.Select(static address =>
				new GmxMarketDirectionFilter
				{
					MarketAddress = address,
					Direction = "any",
				})],
			OrderEventCombinations =
			[
				new() { EventName = GmxTradeEventNames.OrderExecuted },
			],
			Limit = 250,
		}, cancellationToken);

		foreach (var trade in (response?.Trades ?? [])
			.Where(static trade => trade?.Id.IsEmpty() == false &&
				trade.EventName == GmxTradeEventNames.OrderExecuted)
			.OrderBy(static trade => trade.Timestamp))
		{
			long[] transactionIds;
			using (_sync.EnterScope())
			{
				if (!_seenPublicTrades.Add(trade.Id))
					continue;
				transactionIds = [.. _tickSubscriptions.Values.Where(item =>
					item.MarketAddress.Equals(trade.MarketAddress,
						StringComparison.OrdinalIgnoreCase)).Select(
						static item => item.TransactionId)];
			}
			var market = GetMarketByAddress(trade.MarketAddress);
			if (market is null)
				continue;
			foreach (var transactionId in transactionIds)
				await SendPublicTradeAsync(market, trade, transactionId,
					cancellationToken);
		}
	}

	private async ValueTask RefreshCandleSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		CandleSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Values];
		foreach (var group in subscriptions.GroupBy(static item =>
			(item.MarketAddress, item.TimeFrame)))
		{
			var market = GetMarketByAddress(group.Key.MarketAddress);
			if (market is null)
				continue;
			var candles = await ApiClient.GetCandlesAsync(market.Symbol,
				group.Key.TimeFrame.ToGmxTimeFrame(), 2, null, cancellationToken);
			var ordered = candles.Where(static candle => candle is not null &&
				candle.Timestamp > 0).OrderBy(static candle => candle.Timestamp)
				.ToArray();
			if (ordered.Length == 0)
				continue;
			foreach (var subscription in group)
			{
				var latestTime = ordered[^1].Timestamp.FromGmxMilliseconds();
				if (subscription.LastOpenTime is DateTime previous &&
					latestTime > previous && ordered.Length > 1)
					await SendCandleAsync(market, ordered[^2],
						subscription.TimeFrame, subscription.TransactionId,
						cancellationToken, CandleStates.Finished);
				await SendCandleAsync(market, ordered[^1],
					subscription.TimeFrame, subscription.TransactionId,
					cancellationToken);
				subscription.LastOpenTime = latestTime;
			}
		}
	}

	private async ValueTask<GmxTradeAction[]> ReadTradesAsync(GmxMarket market,
		DateTime? from, DateTime to, int count,
		CancellationToken cancellationToken)
	{
		var result = new List<GmxTradeAction>();
		string cursor = null;
		while (result.Count < count)
		{
			var response = await ApiClient.SearchTradesAsync(new()
			{
				IsForAllAccounts = true,
				FromTimestamp = from?.ToGmxSeconds(),
				ToTimestamp = to.ToGmxSeconds(),
				MarketsDirections =
				[
					new()
					{
						MarketAddress = market.MarketAddress,
						Direction = "any",
					},
				],
				OrderEventCombinations =
				[
					new() { EventName = GmxTradeEventNames.OrderExecuted },
				],
				Limit = (count - result.Count).Min(250),
				Cursor = cursor,
			}, cancellationToken);
			result.AddRange((response?.Trades ?? []).Where(trade =>
				trade?.Id.IsEmpty() == false &&
				trade.EventName == GmxTradeEventNames.OrderExecuted &&
				trade.MarketAddress.Equals(market.MarketAddress,
					StringComparison.OrdinalIgnoreCase)));
			if (response?.IsMoreAvailable != true || response.NextCursor.IsEmpty() ||
				response.NextCursor.Equals(cursor, StringComparison.Ordinal))
				break;
			cursor = response.NextCursor;
		}
		return [.. result.OrderBy(static trade => trade.Timestamp).TakeLast(count)];
	}

	private ValueTask SendLevel1Async(GmxMarket market, long transactionId,
		CancellationToken cancellationToken)
	{
		var ticker = market.Ticker;
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		decimal? openInterest = ticker is null
			? null
			: (ticker.LongInterestInTokens.TryParseGmxScaled(
				market.IndexToken.Decimals) ?? 0m) +
				(ticker.ShortInterestInTokens.TryParseGmxScaled(
					market.IndexToken.Decimals) ?? 0m);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.PriceStep)
		.TryAdd(Level1Fields.VolumeStep, market.VolumeStep)
		.TryAdd(Level1Fields.State, market.IsListed
			? SecurityStates.Trading
			: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice,
			ticker?.MarkPrice.TryParseGmxUsd())
		.TryAdd(Level1Fields.LastTradeTime, ticker is null ? null : time)
		.TryAdd(Level1Fields.BestBidPrice,
			ticker?.MinimumPrice.TryParseGmxUsd())
		.TryAdd(Level1Fields.BestBidTime, ticker is null ? null : time)
		.TryAdd(Level1Fields.BestAskPrice,
			ticker?.MaximumPrice.TryParseGmxUsd())
		.TryAdd(Level1Fields.BestAskTime, ticker is null ? null : time)
		.TryAdd(Level1Fields.OpenPrice, ticker?.Open24Hours.TryParseGmxUsd())
		.TryAdd(Level1Fields.HighPrice, ticker?.High24Hours.TryParseGmxUsd())
		.TryAdd(Level1Fields.LowPrice, ticker?.Low24Hours.TryParseGmxUsd())
		.TryAdd(Level1Fields.OpenInterest, openInterest), cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(GmxMarket market,
		GmxTradeAction trade, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.Timestamp is not > 0 || trade.ExecutionPrice.IsEmpty() ||
			trade.SizeDeltaInTokens.IsEmpty() || trade.IsLong is null)
			return default;
		var price = trade.ExecutionPrice.ParseGmxContractPrice(
			market.IndexToken.Decimals, "execution price");
		var volume = trade.SizeDeltaInTokens.ParseGmxScaled(
			market.IndexToken.Decimals, "execution volume");
		if (price <= 0 || volume <= 0)
			return default;
		var time = trade.Timestamp.FromGmxSeconds();
		UpdateServerTime(time);
		var orderType = ParseOrderType(trade.OrderType);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			TradeStringId = trade.Id,
			TradePrice = price,
			TradeVolume = volume,
			OriginSide = orderType.ToStockSharpSide(trade.IsLong.Value),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(GmxMarket market, GmxCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken, CandleStates? forcedState = null)
	{
		var openTime = candle.Timestamp.FromGmxMilliseconds();
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = ParseCandlePrice(candle.Open, "open price"),
			HighPrice = ParseCandlePrice(candle.High, "high price"),
			LowPrice = ParseCandlePrice(candle.Low, "low price"),
			ClosePrice = ParseCandlePrice(candle.Close, "close price"),
			TotalVolume = 0m,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = forcedState ?? (ServerTime >= openTime + timeFrame
				? CandleStates.Finished
				: CandleStates.Active),
		}, cancellationToken);
	}

	private static decimal ParseCandlePrice(string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) || result <= 0)
			throw new InvalidDataException(
				"GMX returned an invalid candle " + field + ".");
		return result;
	}

	private int GetCandleCount(MarketDataMessage mdMsg, TimeSpan timeFrame,
		DateTime to)
	{
		if (mdMsg.Count is long requested)
			return requested.Max(1).Min(HistoryLimit).To<int>();
		if (mdMsg.From is DateTime from)
			return ((long)Math.Ceiling((to - from.EnsureGmxUtc()).TotalSeconds /
				timeFrame.TotalSeconds) + 1).Max(1).Min(HistoryLimit).To<int>();
		return mdMsg.IsHistoryOnly() ? HistoryLimit : 2;
	}

	private static SecurityMessage CreateSecurity(GmxMarket market,
		long transactionId)
	{
		var markPrice = market.Ticker?.MarkPrice.TryParseGmxUsd();
		var message = new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Symbol + " GMX " +
				(market.IsSpotOnly ? "swap" : "perpetual"),
			ShortName = market.Symbol,
			Class = market.IsSpotOnly ? "SWAP" : "PERPETUAL",
			SecurityType = market.ToSecurityType(),
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = !market.IsSpotOnly && market.MinimumPositionUsd > 0 &&
				markPrice is > 0
					? market.MinimumPositionUsd / markPrice.Value
					: null,
			Multiplier = 1m,
			IssueDate = market.ListingDate,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(market.BaseAsset);
		return message;
	}
}
