namespace StockSharp.Reya;

public partial class ReyaMessageAdapter
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
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Reya))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.Ordinal))
				continue;
			var securityType = market.ToStockSharp();
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
				"Reya does not publish historical Level1 changes.");

		var market = GetMarket(mdMsg.SecurityId);
		await RefreshLevel1Async(market, cancellationToken);
		await SendLevel1SnapshotAsync(market.Symbol, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var channels = Level1Channels(market);
		string[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			added = AddChannelReferencesUnsafe(channels);
		}
		try
		{
			await SubscribeChannelsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseChannelReferencesUnsafe(channels);
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
				"Reya does not publish historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		if (!market.IsSpot)
			throw new NotSupportedException(
				"Reya publishes L2 order books for spot markets only.");
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		await SendDepthSnapshotAsync(market.Symbol, mdMsg.TransactionId, depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var channel = DepthChannel(market.Symbol);
		var isAdded = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				Depth = depth,
			});
			isAdded = AddReference(_streamReferences, channel);
		}
		try
		{
			if (isAdded)
				await Socket.SubscribeAsync(channel, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, channel);
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
		var from = mdMsg.From?.EnsureReyaUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureReyaUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Reya execution start time cannot be later than end time.");
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1).To<int>();
		if (market.IsSpot)
		{
			var page = await RestClient.GetSpotExecutionsAsync(market.Symbol, from,
				to, cancellationToken);
			foreach (var trade in (page?.Data ?? [])
				.Where(static trade => trade is not null && trade.Timestamp > 0)
				.OrderBy(static trade => trade.Timestamp)
				.TakeLast(count))
				await SendPublicTradeAsync(trade, mdMsg.TransactionId,
					cancellationToken);
		}
		else
		{
			var page = await RestClient.GetPerpetualExecutionsAsync(market.Symbol,
				from, to, cancellationToken);
			foreach (var trade in (page?.Data ?? [])
				.Where(static trade => trade is not null && trade.Timestamp > 0)
				.OrderBy(static trade => trade.Timestamp)
				.TakeLast(count))
				await SendPublicTradeAsync(trade, mdMsg.TransactionId,
					cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var channel = ExecutionChannel(market);
		var isAdded = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
			});
			isAdded = AddReference(_streamReferences, channel);
		}
		try
		{
			if (isAdded)
				await Socket.SubscribeAsync(channel, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, channel);
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
		var resolution = timeFrame.ToReyaInterval();
		var to = (mdMsg.To ?? ServerTime).EnsureReyaUtc();
		var from = mdMsg.From?.EnsureReyaUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Reya candle start time cannot be later than end time.");
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var history = await RestClient.GetCandlesAsync(market.Symbol, resolution,
			to, cancellationToken);
		var candles = ReadCandles(history)
			.Where(candle => from is null || candle.OpenTime >= from.Value)
			.Where(candle => candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(count)
			.ToArray();
		foreach (var candle in candles)
			await SendCandleAsync(market.Symbol, candle, timeFrame,
				mdMsg.TransactionId, candle.OpenTime + timeFrame <= ServerTime
					? CandleStates.Finished
					: CandleStates.Active, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var current = candles.LastOrDefault();
		if (current is not null && current.OpenTime + timeFrame <= ServerTime)
			current = null;
		var channel = ExecutionChannel(market);
		var isAdded = false;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				TimeFrame = timeFrame,
				Current = current,
			});
			isAdded = AddReference(_streamReferences, channel);
		}
		try
		{
			if (isAdded)
				await Socket.SubscribeAsync(channel, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_streamReferences, channel);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RefreshLevel1Async(ReyaMarket market,
		CancellationToken cancellationToken)
	{
		var priceTask = RestClient.GetPriceAsync(market.Symbol, cancellationToken)
			.AsTask();
		if (market.IsSpot)
		{
			var summaryTask = RestClient.GetSpotSummaryAsync(market.Symbol,
				cancellationToken).AsTask();
			await Task.WhenAll(priceTask, summaryTask);
			var price = await priceTask;
			var summary = await summaryTask;
			using (_sync.EnterScope())
			{
				ApplyPriceUnsafe(price);
				ApplySummaryUnsafe(summary);
			}
		}
		else
		{
			var summaryTask = RestClient.GetPerpetualSummaryAsync(market.Symbol,
				cancellationToken).AsTask();
			await Task.WhenAll(priceTask, summaryTask);
			var price = await priceTask;
			var summary = await summaryTask;
			using (_sync.EnterScope())
			{
				ApplyPriceUnsafe(price);
				ApplySummaryUnsafe(summary);
			}
		}
	}

	private ValueTask SendLevel1SnapshotAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
	{
		ReyaMarket market;
		ReyaPriceState state;
		using (_sync.EnterScope())
		{
			if (!_markets.TryGetValue(symbol, out market) ||
				!_prices.TryGetValue(symbol, out var source))
				return default;
			state = new()
			{
				OraclePrice = source.OraclePrice,
				PoolPrice = source.PoolPrice,
				Volume24Hours = source.Volume24Hours,
				PriceChange24Hours = source.PriceChange24Hours,
				OpenInterest = source.OpenInterest,
				FundingRate = source.FundingRate,
				UpdatedAt = source.UpdatedAt,
			};
		}
		var time = state.UpdatedAt == default ? ServerTime : state.UpdatedAt;
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.PriceStep)
		.TryAdd(Level1Fields.VolumeStep, market.QuantityStep)
		.TryAdd(Level1Fields.MinVolume, market.MinimumQuantity)
		.TryAdd(Level1Fields.State, SecurityStates.Trading)
		.TryAdd(Level1Fields.Index, state.OraclePrice)
		.TryAdd(Level1Fields.SettlementPrice, state.PoolPrice)
		.TryAdd(Level1Fields.LastTradePrice,
			state.PoolPrice ?? state.OraclePrice)
		.TryAdd(Level1Fields.Volume, state.Volume24Hours)
		.TryAdd(Level1Fields.Change, state.PriceChange24Hours)
		.TryAdd(Level1Fields.OpenInterest, state.OpenInterest), cancellationToken);
	}

	private async ValueTask SendDepthSnapshotAsync(string symbol,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		var snapshot = await RestClient.GetDepthAsync(symbol, cancellationToken) ??
			throw new InvalidDataException("Reya returned no order-book snapshot.");
		await SendDepthAsync(snapshot, transactionId, depth,
			QuoteChangeStates.SnapshotComplete, cancellationToken);
	}

	private ValueTask SendDepthAsync(ReyaDepth depth, long transactionId,
		int maximumDepth, QuoteChangeStates state,
		CancellationToken cancellationToken)
	{
		if (depth?.Symbol.IsEmpty() != false)
			return default;
		var time = depth.UpdatedAt.FromReyaMillisecondsOrNow();
		UpdateServerTime(time);
		var isSnapshot = state == QuoteChangeStates.SnapshotComplete;
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = depth.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = state,
			Bids = ToQuotes(depth.Bids, maximumDepth, true, isSnapshot),
			Asks = ToQuotes(depth.Asks, maximumDepth, false, isSnapshot),
		}, cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(ReyaPerpetualExecution trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0)
			return default;
		var time = trade.Timestamp.FromReyaMillisecondsOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			TradeId = trade.SequenceNumber,
			TradePrice = trade.Price.ParseReyaDecimal("execution price"),
			TradeVolume = trade.Quantity.ParseReyaDecimal("execution quantity"),
			OriginSide = trade.Side.ToStockSharp(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(ReyaSpotExecution trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0)
			return default;
		var time = trade.Timestamp.FromReyaMillisecondsOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			TradeId = trade.SequenceNumber,
			TradePrice = trade.Price.ParseReyaDecimal("execution price"),
			TradeVolume = trade.Quantity.ParseReyaDecimal("execution quantity"),
			OriginSide = trade.Side.ToStockSharp(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, CandleState candle,
		TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
		=> candle is null ? default : SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.OpenPrice,
			HighPrice = candle.HighPrice,
			LowPrice = candle.LowPrice,
			ClosePrice = candle.ClosePrice,
			TotalVolume = candle.TotalVolume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);

	private async ValueTask OnPerpetualSummaryAsync(
		ReyaSocketEnvelope<ReyaMarketSummary> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope?.Data?.Symbol.IsEmpty() != false)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			ApplySummaryUnsafe(envelope.Data);
			subscriptions = GetLevel1IdsUnsafe(envelope.Data.Symbol);
		}
		UpdateServerTime(envelope.Timestamp ?? envelope.Data.UpdatedAt);
		foreach (var transactionId in subscriptions)
			await SendLevel1SnapshotAsync(envelope.Data.Symbol, transactionId,
				cancellationToken);
	}

	private async ValueTask OnSpotSummaryAsync(
		ReyaSocketEnvelope<ReyaSpotMarketSummary> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope?.Data?.Symbol.IsEmpty() != false)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			ApplySummaryUnsafe(envelope.Data);
			subscriptions = GetLevel1IdsUnsafe(envelope.Data.Symbol);
		}
		UpdateServerTime(envelope.Timestamp ?? envelope.Data.UpdatedAt);
		foreach (var transactionId in subscriptions)
			await SendLevel1SnapshotAsync(envelope.Data.Symbol, transactionId,
				cancellationToken);
	}

	private async ValueTask OnPriceAsync(ReyaSocketEnvelope<ReyaPrice> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope?.Data?.Symbol.IsEmpty() != false)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			ApplyPriceUnsafe(envelope.Data);
			subscriptions = GetLevel1IdsUnsafe(envelope.Data.Symbol);
		}
		UpdateServerTime(envelope.Timestamp ?? envelope.Data.UpdatedAt);
		foreach (var transactionId in subscriptions)
			await SendLevel1SnapshotAsync(envelope.Data.Symbol, transactionId,
				cancellationToken);
	}

	private async ValueTask OnDepthAsync(ReyaSocketEnvelope<ReyaDepth> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope?.Data?.Symbol.IsEmpty() != false)
			return;
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Values.Where(subscription =>
				subscription.Symbol.Equals(envelope.Data.Symbol,
					StringComparison.Ordinal))];
		var state = envelope.Data.Type == ReyaDepthTypes.Snapshot
			? QuoteChangeStates.SnapshotComplete
			: QuoteChangeStates.Increment;
		foreach (var subscription in subscriptions)
			await SendDepthAsync(envelope.Data, subscription.TransactionId,
				subscription.Depth, state, cancellationToken);
	}

	private async ValueTask OnPerpetualExecutionsAsync(
		ReyaSocketEnvelope<ReyaPerpetualExecution[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		if (envelope.Channel.StartsWith("/v2/wallet/", StringComparison.Ordinal))
		{
			await OnWalletPerpetualExecutionsAsync(envelope, cancellationToken);
			return;
		}
		foreach (var trade in envelope.Data ?? [])
		{
			if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0 ||
				!TryAcceptTrade(_seenPublicTrades, "P:" + trade.Symbol + ":" +
					trade.SequenceNumber.ToString(CultureInfo.InvariantCulture)))
				continue;
			long[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = GetTickIdsUnsafe(trade.Symbol);
			foreach (var transactionId in subscriptions)
				await SendPublicTradeAsync(trade, transactionId, cancellationToken);
			await UpdateCandlesAsync(trade.Symbol, trade.Timestamp,
				trade.Price.ParseReyaDecimal("execution price"),
				trade.Quantity.ParseReyaDecimal("execution quantity"),
				cancellationToken);
		}
	}

	private async ValueTask OnSpotExecutionsAsync(
		ReyaSocketEnvelope<ReyaSpotExecution[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		if (envelope.Channel.StartsWith("/v2/wallet/", StringComparison.Ordinal))
		{
			await OnWalletSpotExecutionsAsync(envelope, cancellationToken);
			return;
		}
		foreach (var trade in envelope.Data ?? [])
		{
			if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0 ||
				!TryAcceptTrade(_seenPublicTrades, "S:" + trade.Symbol + ":" +
					trade.SequenceNumber.ToString(CultureInfo.InvariantCulture)))
				continue;
			long[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = GetTickIdsUnsafe(trade.Symbol);
			foreach (var transactionId in subscriptions)
				await SendPublicTradeAsync(trade, transactionId, cancellationToken);
			await UpdateCandlesAsync(trade.Symbol, trade.Timestamp,
				trade.Price.ParseReyaDecimal("execution price"),
				trade.Quantity.ParseReyaDecimal("execution quantity"),
				cancellationToken);
		}
	}

	private async ValueTask UpdateCandlesAsync(string symbol, long timestamp,
		decimal price, decimal volume, CancellationToken cancellationToken)
	{
		var tradeTime = timestamp.FromReyaMillisecondsOrNow();
		var messages = new List<(long TransactionId, TimeSpan TimeFrame,
			CandleState Candle, CandleStates State)>();
		using (_sync.EnterScope())
		{
			foreach (var subscription in _candleSubscriptions.Values.Where(
				subscription => subscription.Symbol.Equals(symbol,
					StringComparison.Ordinal)))
			{
				var openTime = tradeTime.Truncate(subscription.TimeFrame);
				var current = subscription.Current;
				if (current is not null && openTime < current.OpenTime)
					continue;
				if (current is null || openTime > current.OpenTime)
				{
					if (current is not null)
						messages.Add((subscription.TransactionId,
							subscription.TimeFrame, CopyCandle(current),
							CandleStates.Finished));
					current = new()
					{
						OpenTime = openTime,
						OpenPrice = price,
						HighPrice = price,
						LowPrice = price,
						ClosePrice = price,
						TotalVolume = volume,
					};
					subscription.Current = current;
				}
				else
				{
					current.HighPrice = current.HighPrice.Max(price);
					current.LowPrice = current.LowPrice.Min(price);
					current.ClosePrice = price;
					current.TotalVolume += volume;
				}
				messages.Add((subscription.TransactionId,
					subscription.TimeFrame, CopyCandle(current),
					CandleStates.Active));
			}
		}
		UpdateServerTime(tradeTime);
		foreach (var message in messages)
			await SendCandleAsync(symbol, message.Candle, message.TimeFrame,
				message.TransactionId, message.State, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		string[] removed = [];
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out var subscription))
			{
				var market = _markets[subscription.Symbol];
				removed = ReleaseChannelReferencesUnsafe(Level1Channels(market));
			}
		await UnsubscribeChannelsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out var subscription))
			{
				var value = DepthChannel(subscription.Symbol);
				if (ReleaseReference(_streamReferences, value))
					channel = value;
			}
		if (!channel.IsEmpty())
			await Socket.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
			{
				var value = ExecutionChannel(_markets[subscription.Symbol]);
				if (ReleaseReference(_streamReferences, value))
					channel = value;
			}
		if (!channel.IsEmpty())
			await Socket.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
			{
				var value = ExecutionChannel(_markets[subscription.Symbol]);
				if (ReleaseReference(_streamReferences, value))
					channel = value;
			}
		if (!channel.IsEmpty())
			await Socket.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask SubscribeChannelsAsync(IEnumerable<string> channels,
		CancellationToken cancellationToken)
	{
		var subscribed = new List<string>();
		try
		{
			foreach (var channel in channels)
			{
				await Socket.SubscribeAsync(channel, cancellationToken);
				subscribed.Add(channel);
			}
		}
		catch
		{
			foreach (var channel in subscribed)
				await Socket.UnsubscribeAsync(channel, cancellationToken);
			throw;
		}
	}

	private async ValueTask UnsubscribeChannelsAsync(
		IEnumerable<string> channels, CancellationToken cancellationToken)
	{
		foreach (var channel in channels)
			await Socket.UnsubscribeAsync(channel, cancellationToken);
	}

	private string[] AddChannelReferencesUnsafe(IEnumerable<string> channels)
		=> [.. channels.Where(channel => AddReference(_streamReferences, channel))];

	private string[] ReleaseChannelReferencesUnsafe(
		IEnumerable<string> channels)
		=> [.. channels.Where(channel => ReleaseReference(_streamReferences,
			channel))];

	private long[] GetLevel1IdsUnsafe(string symbol)
		=> [.. _level1Subscriptions.Values.Where(subscription =>
			subscription.Symbol.Equals(symbol, StringComparison.Ordinal))
			.Select(static subscription => subscription.TransactionId)];

	private long[] GetTickIdsUnsafe(string symbol)
		=> [.. _tickSubscriptions.Values.Where(subscription =>
			subscription.Symbol.Equals(symbol, StringComparison.Ordinal))
			.Select(static subscription => subscription.TransactionId)];

	private static string[] Level1Channels(ReyaMarket market)
		=> [SummaryChannel(market), PriceChannel(market.Symbol)];

	private static string SummaryChannel(ReyaMarket market)
		=> market.IsSpot
			? "/v2/spotMarket/" + market.Symbol + "/summary"
			: "/v2/market/" + market.Symbol + "/summary";

	private static string PriceChannel(string symbol)
		=> "/v2/prices/" + symbol;

	private static string DepthChannel(string symbol)
		=> "/v2/market/" + symbol + "/depth";

	private static string ExecutionChannel(ReyaMarket market)
		=> "/v2/market/" + market.Symbol +
			(market.IsSpot ? "/spotExecutions" : "/perpExecutions");

	private static SecurityMessage CreateSecurity(ReyaMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = market.IsSpot
				? market.BaseAsset + "/" + market.QuoteAsset
				: market.BaseAsset + "/" + market.QuoteAsset + " perpetual",
			ShortName = market.Symbol,
			Class = market.IsSpot ? "SPOT" : "PERPETUAL",
			SecurityType = market.ToStockSharp(),
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.QuantityStep,
			MinVolume = market.MinimumQuantity,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		if (!market.IsSpot)
			message.TryFillUnderlyingId(market.BaseAsset);
		return message;
	}

	private static QuoteChange[] ToQuotes(ReyaPriceLevel[] levels, int depth,
		bool isBids, bool isSnapshot)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParseReyaDecimal("order-book price");
			var volume = level.Quantity.ParseReyaDecimal("order-book quantity", true);
			if (!isSnapshot || volume > 0)
				quotes.Add(new(price, volume));
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private static CandleState[] ReadCandles(ReyaCandleHistory history)
	{
		if (history is null)
			return [];
		var timestamps = history.Timestamps ?? [];
		var open = history.OpenPrices ?? [];
		var high = history.HighPrices ?? [];
		var low = history.LowPrices ?? [];
		var close = history.ClosePrices ?? [];
		if (timestamps.Length != open.Length || timestamps.Length != high.Length ||
			timestamps.Length != low.Length || timestamps.Length != close.Length)
			throw new InvalidDataException(
				"Reya returned candle arrays with different lengths.");
		var result = new CandleState[timestamps.Length];
		for (var i = 0; i < result.Length; i++)
			result[i] = new()
			{
				OpenTime = timestamps[i].FromReyaSeconds(),
				OpenPrice = open[i].ParseReyaDecimal("candle open price"),
				HighPrice = high[i].ParseReyaDecimal("candle high price"),
				LowPrice = low[i].ParseReyaDecimal("candle low price"),
				ClosePrice = close[i].ParseReyaDecimal("candle close price"),
			};
		return result;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		const int maximum = 200;
		if (message.Count is long count)
			return count.Min(maximum).Max(1).To<int>();
		if (message.From is DateTime from && to > from.EnsureReyaUtc())
			return ((to - from.EnsureReyaUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(maximum).Max(1).To<int>();
		return maximum;
	}

	private static CandleState CopyCandle(CandleState value)
		=> new()
		{
			OpenTime = value.OpenTime,
			OpenPrice = value.OpenPrice,
			HighPrice = value.HighPrice,
			LowPrice = value.LowPrice,
			ClosePrice = value.ClosePrice,
			TotalVolume = value.TotalVolume,
		};
}
