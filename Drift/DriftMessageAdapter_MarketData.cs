namespace StockSharp.Drift;

public partial class DriftMessageAdapter
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
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Drift))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.OrdinalIgnoreCase))
				continue;
			var securityType = market.MarketType.ToStockSharp();
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
				"Drift does not publish historical Level1 changes.");
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
			subscribe = _level1Subscriptions.Count == 0;
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
		}
		try
		{
			if (subscribe)
				await DataSocket.SubscribeMarketsAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
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
				"Drift does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		if (market.MarketType != DriftMarketTypes.Perpetual)
			throw new NotSupportedException(
				"The current Drift DLOB exposes perpetual order books only.");
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		await SendDepthSnapshotAsync(market, mdMsg.TransactionId, depth,
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
				Symbol = market.Symbol,
				Depth = depth,
			});
			subscribe = AddReference(_depthReferences, market.Symbol);
		}
		try
		{
			if (subscribe)
				await DlobSocket.SubscribeBookAsync(market.Symbol,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_depthReferences, market.Symbol);
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
				"Drift trade start time cannot be later than end time.");
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var history = await RestClient.GetTradesAsync(market.Symbol, count,
			cancellationToken);
		foreach (var trade in history
			.Where(static trade => trade is not null && trade.Timestamp > 0)
			.Where(trade => from is null ||
				trade.Timestamp.FromDriftSeconds() >= from.Value)
			.Where(trade => trade.Timestamp.FromDriftSeconds() <= to)
			.OrderBy(static trade => trade.Timestamp))
			await SendTradeAsync(trade, market.Symbol, mdMsg.TransactionId,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (market.MarketType != DriftMarketTypes.Perpetual)
			throw new NotSupportedException(
				"The current Drift DLOB streams perpetual trades only.");
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			subscribe = AddReference(_tradeReferences, market.Symbol);
		}
		try
		{
			if (subscribe)
				await DlobSocket.SubscribeTradesAsync(market.Symbol,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_tradeReferences, market.Symbol);
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
		var resolution = timeFrame.ToDriftResolution();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Drift candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var response = await RestClient.GetCandlesAsync(market.Symbol,
			resolution, from, to, count, cancellationToken);
		var candles = (response?.Records ?? [])
			.Where(static candle => candle is not null && candle.Timestamp > 0)
			.OrderBy(static candle => candle.Timestamp)
			.ToArray();
		foreach (var candle in candles)
			await SendCandleAsync(market.Symbol, candle, timeFrame,
				mdMsg.TransactionId,
				candle.Timestamp.FromDriftSeconds() + timeFrame <= ServerTime
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var key = CandleKey(market.Symbol, resolution);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				TimeFrame = timeFrame,
				Resolution = resolution,
				LastCandle = candles.LastOrDefault(),
			});
			subscribe = AddReference(_candleReferences, key);
		}
		try
		{
			if (subscribe)
				await DataSocket.SubscribeCandleAsync(market.Symbol, resolution,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_candleReferences, key);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask SendLevel1Async(DriftMarket market, long transactionId,
		CancellationToken cancellationToken)
	{
		if (market is null)
			return default;
		var time = market.FundingRateUpdateTimestamp is long timestamp &&
			timestamp > 0 ? timestamp.FromDriftSeconds() : ServerTime;
		UpdateServerTime(time);
		var openInterest = (market.OpenInterest?.Long.TryParseDriftDecimal() ?? 0m)
			.Abs() + (market.OpenInterest?.Short.TryParseDriftDecimal() ?? 0m).Abs();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, 1m / DriftExtensions.PricePrecision)
		.TryAdd(Level1Fields.VolumeStep, market.GetVolumeStep())
		.TryAdd(Level1Fields.MinVolume, market.Limits?.Amount?.Minimum)
		.TryAdd(Level1Fields.MaxVolume, market.Limits?.Amount?.Maximum)
		.TryAdd(Level1Fields.State,
			market.Status.EqualsIgnoreCase("active")
				? SecurityStates.Trading
				: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice,
			market.Price.TryParseDriftDecimal() ??
			market.MarkPrice.TryParseDriftDecimal() ??
			market.OraclePrice.TryParseDriftDecimal())
		.TryAdd(Level1Fields.SettlementPrice,
			market.MarkPrice.TryParseDriftDecimal())
		.TryAdd(Level1Fields.Index, market.OraclePrice.TryParseDriftDecimal())
		.TryAdd(Level1Fields.HighPrice,
			market.PriceHigh?.Fill.TryParseDriftDecimal())
		.TryAdd(Level1Fields.LowPrice,
			market.PriceLow?.Fill.TryParseDriftDecimal())
		.TryAdd(Level1Fields.Volume, market.BaseVolume.TryParseDriftDecimal())
		.TryAdd(Level1Fields.OpenInterest, openInterest)
		.TryAdd(Level1Fields.Change,
			market.PriceChange24Hours.TryParseDriftDecimal()), cancellationToken);
	}

	private async ValueTask SendDepthSnapshotAsync(DriftMarket market,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		var book = await RestClient.GetOrderBookAsync(market.Symbol, depth,
			cancellationToken) ?? throw new InvalidDataException(
			"Drift returned no order-book snapshot.");
		await SendDepthAsync(book, market, transactionId, depth,
			cancellationToken);
	}

	private ValueTask SendDepthAsync(DriftDlobBook book, DriftMarket market,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		if (book is null || market is null)
			return default;
		var time = book.Timestamp > 0
			? book.Timestamp.FromDriftMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, market.Precision, depth, true),
			Asks = ToQuotes(book.Asks, market.Precision, depth, false),
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(DriftTrade trade, string symbol,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade is null || symbol.IsEmpty() || trade.Timestamp <= 0)
			return default;
		var time = trade.Timestamp.FromDriftSeconds();
		UpdateServerTime(time);
		long? id = long.TryParse(trade.FillRecordId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numericId) ? numericId : null;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToDriftSecurityId(),
			ServerTime = time,
			TradeId = id,
			TradeStringId = trade.FillRecordId,
			TradePrice = trade.GetTradePrice(),
			TradeVolume = trade.BaseAssetAmountFilled.ParseDriftDecimal(
				"trade volume"),
			OriginSide = trade.TakerOrderDirection.ToStockSharpDirection(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, DriftCandle candle,
		TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
	{
		if (candle is null)
			return default;
		var openTime = candle.Timestamp.FromDriftSeconds();
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToDriftSecurityId(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.FillOpen,
			HighPrice = candle.FillHigh,
			LowPrice = candle.FillLow,
			ClosePrice = candle.FillClose,
			TotalVolume = candle.BaseVolume,
			TotalPrice = candle.QuoteVolume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);
	}

	private async ValueTask OnMarketsAsync(DriftMarket[] markets,
		CancellationToken cancellationToken)
	{
		var changed = new List<(DriftMarket Market, long[] Transactions)>();
		using (_sync.EnterScope())
			foreach (var market in markets ?? [])
			{
				if (market?.Symbol.IsEmpty() != false)
					continue;
				market.Symbol = market.Symbol.Trim().ToUpperInvariant();
				if (!_markets.ContainsKey(market.Symbol))
					continue;
				_markets[market.Symbol] = market;
				var ids = _level1Subscriptions.Values
					.Where(subscription => subscription.Symbol.Equals(
						market.Symbol, StringComparison.Ordinal))
					.Select(static subscription => subscription.TransactionId)
					.ToArray();
				if (ids.Length > 0)
					changed.Add((market, ids));
			}
		foreach (var (market, transactions) in changed)
			foreach (var transactionId in transactions)
				await SendLevel1Async(market, transactionId, cancellationToken);
	}

	private async ValueTask OnBookAsync(DriftDlobBook book,
		CancellationToken cancellationToken)
	{
		if (book?.MarketName.IsEmpty() != false)
			return;
		var market = GetMarket(book.MarketName);
		if (market is null)
			return;
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Values.Where(subscription =>
				subscription.Symbol.Equals(book.MarketName,
					StringComparison.Ordinal))];
		foreach (var subscription in subscriptions)
			await SendDepthAsync(book, market, subscription.TransactionId,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnTradeAsync(DriftTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade is null || !AcceptTrade(trade))
			return;
		var symbol = trade.Symbol;
		if (symbol.IsEmpty())
			using (_sync.EnterScope())
				symbol = _markets.Values.FirstOrDefault(market =>
					market.MarketType == trade.MarketType &&
					market.MarketIndex == trade.MarketIndex)?.Symbol;
		if (symbol.IsEmpty())
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Values
				.Where(subscription => subscription.Symbol.Equals(symbol,
					StringComparison.Ordinal))
				.Select(static subscription => subscription.TransactionId)];
		foreach (var transactionId in subscriptions)
			await SendTradeAsync(trade, symbol, transactionId, cancellationToken);
	}

	private async ValueTask OnCandleAsync(DriftCandle candle,
		CancellationToken cancellationToken)
	{
		if (candle?.Symbol.IsEmpty() != false || candle.Resolution.IsEmpty())
			return;
		var messages = new List<(long TransactionId, TimeSpan TimeFrame,
			DriftCandle Candle, CandleStates State)>();
		using (_sync.EnterScope())
			foreach (var subscription in _candleSubscriptions.Values.Where(
				subscription => subscription.Symbol.Equals(candle.Symbol,
					StringComparison.Ordinal) &&
					subscription.Resolution.Equals(candle.Resolution,
					StringComparison.Ordinal)))
			{
				var previous = subscription.LastCandle;
				if (previous is not null &&
					candle.Timestamp > previous.Timestamp)
					messages.Add((subscription.TransactionId,
						subscription.TimeFrame, previous, CandleStates.Finished));
				subscription.LastCandle = candle;
				messages.Add((subscription.TransactionId,
					subscription.TimeFrame, candle, CandleStates.Active));
			}
		foreach (var message in messages)
			await SendCandleAsync(candle.Symbol, message.Candle,
				message.TimeFrame, message.TransactionId, message.State,
				cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Remove(transactionId);
			unsubscribe = _level1Subscriptions.Count == 0;
		}
		if (unsubscribe)
			await DataSocket.UnsubscribeMarketsAsync(cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string symbol = null;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_depthReferences, subscription.Symbol))
				symbol = subscription.Symbol;
		if (!symbol.IsEmpty())
			await DlobSocket.UnsubscribeBookAsync(symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string symbol = null;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_tradeReferences, subscription.Symbol))
				symbol = subscription.Symbol;
		if (!symbol.IsEmpty())
			await DlobSocket.UnsubscribeTradesAsync(symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandleAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription removed = null;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId,
				out var subscription) && ReleaseReference(_candleReferences,
					CandleKey(subscription.Symbol, subscription.Resolution)))
				removed = subscription;
		if (removed is not null)
			await DataSocket.UnsubscribeCandleAsync(removed.Symbol,
				removed.Resolution, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(DriftMarket market,
		long transactionId)
	{
		var isPerpetual = market.MarketType == DriftMarketTypes.Perpetual;
		var baseAsset = market.BaseAsset.IsEmpty()
			? market.Symbol.Replace("-PERP", string.Empty,
				StringComparison.Ordinal)
			: market.BaseAsset;
		var quoteAsset = market.QuoteAsset.IsEmpty() ? "USDT" : market.QuoteAsset;
		var message = new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = isPerpetual
				? $"{baseAsset}/{quoteAsset} perpetual"
				: $"{baseAsset}/{quoteAsset}",
			ShortName = market.Symbol,
			Class = isPerpetual ? "PERPETUAL" : "SPOT",
			SecurityType = market.MarketType.ToStockSharp(),
			Currency = quoteAsset.ToDriftCurrency(),
			PriceStep = 1m / DriftExtensions.PricePrecision,
			VolumeStep = market.GetVolumeStep(),
			MinVolume = market.Limits?.Amount?.Minimum,
			MaxVolume = market.Limits?.Amount?.Maximum,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		if (isPerpetual)
			message.TryFillUnderlyingId(baseAsset);
		return message;
	}

	private static QuoteChange[] ToQuotes(DriftDlobLevel[] levels,
		int precision, int depth, bool isBids)
	{
		var result = (levels ?? [])
			.Where(static level => level?.Price.IsEmpty() == false &&
				level.Size.IsEmpty() == false)
			.Select(level => new QuoteChange(level.Price.FromDlobPrice(),
				level.Size.FromDlobSize(precision)))
			.Where(static quote => quote.Price > 0 && quote.Volume > 0);
		result = isBids
			? result.OrderByDescending(static quote => quote.Price)
			: result.OrderBy(static quote => quote.Price);
		return [.. result.Take(depth)];
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

	private static string CandleKey(string symbol, string resolution)
		=> symbol + "|" + resolution;
}
