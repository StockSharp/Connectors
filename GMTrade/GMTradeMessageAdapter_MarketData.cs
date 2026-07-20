namespace StockSharp.GMTrade;

using Native;

public partial class GMTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in GetMarkets().OrderBy(static item => item.Code,
			StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.GMTrade))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.Code))
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
				"GMTrade does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				MarketToken = market.MarketToken,
			});
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
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var from = mdMsg.From?.EnsureUtc();
		var limit = (mdMsg.Count ?? 500).Min(HistoryLimit).Max(1).To<int>();
		var trades = (await RestClient.GetTradesAsync(new()
		{
			MarketToken = market.MarketToken,
			From = from,
			To = to,
		}, limit, cancellationToken))
			.Where(trade => trade is not null &&
				trade.MarketToken == market.MarketToken)
			.OrderBy(static trade => trade.Timestamp.EnsureUtc())
			.TakeLast(limit)
			.ToArray();
		foreach (var trade in trades)
		{
			await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken);
			if (!mdMsg.IsHistoryOnly())
				TryAcceptTrade(_seenPublicTrades, trade.Id);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				MarketToken = market.MarketToken,
			});
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
		var resolution = timeFrame.ToResolution();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var maximumRange = TimeSpan.FromTicks(checked(
			timeFrame.Ticks * (long)count));
		var from = (mdMsg.From?.EnsureUtc() ?? to - maximumRange)
			.Max(to - maximumRange);
		var candles = (await RestClient.GetCandlesAsync(market.IndexToken,
			resolution, from, to, cancellationToken))
			.Where(candle => candle is not null &&
				candle.IndexToken == market.IndexToken &&
				candle.Resolution == resolution && candle.Timestamp > 0)
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(count)
			.ToArray();
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new CandleStreamKey(market.IndexToken, resolution);
		CandleStreamReference reference;
		var isNew = false;
		using (_sync.EnterScope())
		{
			if (!_candleStreams.TryGetValue(key, out reference))
			{
				reference = new() { Count = 1 };
				_candleStreams.Add(key, reference);
				isNew = true;
			}
			else
			{
				reference.Count++;
			}
		}
		try
		{
			if (isNew)
				reference.SocketId = await CandleSocket.SubscribeCandlesAsync(
					market.IndexToken, resolution, cancellationToken);
			using (_sync.EnterScope())
				_candleSubscriptions.Add(mdMsg.TransactionId, new()
				{
					TransactionId = mdMsg.TransactionId,
					MarketToken = market.MarketToken,
					IndexToken = market.IndexToken,
					Resolution = resolution,
					TimeFrame = timeFrame,
				});
		}
		catch
		{
			using (_sync.EnterScope())
			{
				reference.Count--;
				if (reference.Count == 0)
					_candleStreams.Remove(key);
			}
			throw;
		}
	}

	private async ValueTask UnsubscribeCandleAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription;
		string socketId = null;
		using (_sync.EnterScope())
		{
			if (!_candleSubscriptions.Remove(transactionId, out subscription))
				return;
			var key = new CandleStreamKey(subscription.IndexToken,
				subscription.Resolution);
			if (_candleStreams.TryGetValue(key, out var reference) &&
				--reference.Count == 0)
			{
				socketId = reference.SocketId;
				_candleStreams.Remove(key);
			}
		}
		if (!socketId.IsEmpty())
			await CandleSocket.UnsubscribeAsync(socketId, cancellationToken);
	}

	private async ValueTask PublishLevel1Async(GMTradeMarketInfo market,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.MarketToken == market.MarketToken)
				.Select(static pair => pair.Key)];
		foreach (var transactionId in subscriptions)
			await SendLevel1Async(market, transactionId, cancellationToken);
	}

	private ValueTask SendLevel1Async(GMTradeMarketInfo market,
		long transactionId, CancellationToken cancellationToken)
	{
		var feed = market.Market.Meta.IndexToken.Price ?? throw new
			InvalidDataException("GMTrade market has no oracle price.");
		var bid = feed.Minimum.TryFromOraclePrice(market.IndexDecimals);
		var ask = feed.Maximum.TryFromOraclePrice(market.IndexDecimals);
		var midpoint = bid is decimal minimum && ask is decimal maximum
			? (minimum + maximum) / 2m
			: bid ?? ask;
		var time = feed.Timestamp > 0 ? feed.Timestamp.ToUtcTime() : ServerTime;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.TheorPrice, midpoint)
		.TryAdd(Level1Fields.Index, midpoint)
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestBidTime, bid is null ? null : time)
		.TryAdd(Level1Fields.BestAskTime, ask is null ? null : time)
		.TryAdd(Level1Fields.State,
			market.Market.Meta.IsEnabled && feed.IsOpen
				? SecurityStates.Trading
				: SecurityStates.Stoped), cancellationToken);
	}

	private async ValueTask PollTradesAsync(
		CancellationToken cancellationToken)
	{
		MarketSubscription[] publicSubscriptions;
		var accountSubscription = _orderStatusSubscriptionId;
		using (_sync.EnterScope())
			publicSubscriptions = [.. _tickSubscriptions.Values];
		var serverTime = ServerTime;

		if (publicSubscriptions.Length > 0)
		{
			var marketTokens = publicSubscriptions.Select(static item =>
				item.MarketToken).Distinct(StringComparer.Ordinal).ToArray();
			var from = _lastPublicTradePoll == default
				? serverTime.AddSeconds(-5)
				: _lastPublicTradePoll.AddSeconds(-2);
			var trades = await RestClient.GetTradesAsync(new()
			{
				MarketTokens = marketTokens,
				From = from,
				To = serverTime.AddSeconds(2),
			}, HistoryLimit, cancellationToken);
			_lastPublicTradePoll = serverTime;
			foreach (var trade in trades.Where(static trade => trade is not null)
				.OrderBy(static trade => trade.Timestamp.EnsureUtc()))
			{
				if (!TryAcceptTrade(_seenPublicTrades, trade.Id) ||
					!TryGetMarketByToken(trade.MarketToken, out var market))
					continue;
				var targets = publicSubscriptions.Where(item =>
					item.MarketToken == trade.MarketToken).Select(static item =>
					item.TransactionId).ToArray();
				foreach (var target in targets)
					await SendPublicTradeAsync(market, trade, target,
						cancellationToken);
				await PublishLastTradeAsync(market, trade, cancellationToken);
			}
		}

		if (accountSubscription != 0 && !WalletAddress.IsEmpty())
		{
			var from = _lastAccountTradePoll == default
				? serverTime.AddSeconds(-5)
				: _lastAccountTradePoll.AddSeconds(-2);
			var trades = await RestClient.GetTradesAsync(new()
			{
				User = WalletAddress,
				From = from,
				To = serverTime.AddSeconds(2),
			}, HistoryLimit, cancellationToken);
			_lastAccountTradePoll = serverTime;
			foreach (var trade in trades.Where(static trade => trade is not null)
				.OrderBy(static trade => trade.Timestamp.EnsureUtc()))
				if (TryAcceptTrade(_seenAccountTrades, trade.Id))
					await SendAccountTradeAsync(trade, accountSubscription,
						cancellationToken);
		}
	}

	private ValueTask SendPublicTradeAsync(GMTradeMarketInfo market,
		GMTradeTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Timestamp.EnsureUtc(),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.Id,
			TradePrice = trade.ExecutionPrice.FromOraclePrice(
				market.IndexDecimals, "execution price"),
			TradeVolume = GetTradeVolume(trade, market.IndexDecimals),
			OriginSide = trade.GetExecutionSide(),
		}, cancellationToken);

	private async ValueTask PublishLastTradeAsync(GMTradeMarketInfo market,
		GMTradeTrade trade, CancellationToken cancellationToken)
	{
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _level1Subscriptions.Where(pair =>
				pair.Value.MarketToken == market.MarketToken)
				.Select(static pair => pair.Key)];
		if (targets.Length == 0)
			return;
		var time = trade.Timestamp.EnsureUtc();
		var price = trade.ExecutionPrice.FromOraclePrice(market.IndexDecimals,
			"execution price");
		var volume = GetTradeVolume(trade, market.IndexDecimals);
		foreach (var target in targets)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = target,
			}
			.TryAdd(Level1Fields.LastTradePrice, price)
			.TryAdd(Level1Fields.LastTradeVolume, volume)
			.TryAdd(Level1Fields.LastTradeTime, time)
			.TryAdd(Level1Fields.LastTradeOrigin, trade.GetExecutionSide()),
				cancellationToken);
	}

	private static decimal GetTradeVolume(GMTradeTrade trade, int decimals)
	{
		var before = trade.BeforeSizeInTokens.FromTokenAmount(decimals,
			"pre-trade token size");
		var after = trade.AfterSizeInTokens.FromTokenAmount(decimals,
			"post-trade token size");
		return (after - before).Abs();
	}

	private async ValueTask OnCandleAsync(GMTradeCandle candle,
		CancellationToken cancellationToken)
	{
		if (candle?.IndexToken.IsEmpty() != false || candle.Timestamp <= 0)
			return;
		CandleSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Values.Where(item =>
				item.IndexToken == candle.IndexToken &&
				item.Resolution == candle.Resolution)];
		foreach (var subscription in subscriptions)
			if (TryGetMarketByToken(subscription.MarketToken, out var market))
				await SendCandleAsync(market, candle, subscription.TimeFrame,
					subscription.TransactionId, cancellationToken);
	}

	private ValueTask SendCandleAsync(GMTradeMarketInfo market,
		GMTradeCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.ToUtcTime();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = openTime,
			OpenPrice = candle.Open.FromCandlePrice("candle open"),
			HighPrice = candle.High.FromCandlePrice("candle high"),
			LowPrice = candle.Low.FromCandlePrice("candle low"),
			ClosePrice = candle.Close.FromCandlePrice("candle close"),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = openTime + timeFrame <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(GMTradeMarketInfo market,
		long transactionId)
	{
		var priceStep = GMTradeExtensions.PriceStep(market.PricePrecision);
		var volumeStep = GMTradeExtensions.PriceStep(market.IndexDecimals);
		return new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Name + " perpetual",
			ShortName = market.Code,
			Class = market.Market.Meta.IndexToken.Meta.Category.IsEmpty()
				? "PERPETUAL"
				: market.Market.Meta.IndexToken.Meta.Category + "-PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = CurrencyTypes.USD,
			PriceStep = priceStep,
			Decimals = priceStep.GetCachedDecimals(),
			VolumeStep = volumeStep,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		}.TryFillUnderlyingId(market.IndexSymbol);
	}
}
