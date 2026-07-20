namespace StockSharp.Aevo;

public partial class AevoMessageAdapter
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
		foreach (var market in GetMarkets().OrderBy(static market =>
			market.InstrumentName, StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Aevo))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.InstrumentName,
					StringComparison.OrdinalIgnoreCase))
				continue;
			var securityType = market.InstrumentType.ToStockSharp();
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
				"Aevo does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		var current = await RestClient.GetInstrumentAsync(market.InstrumentName,
			cancellationToken);
		await SendLevel1Async(current, ServerTime, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var channel = TickerChannel(market.InstrumentName);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.InstrumentName,
			});
			subscribe = AddReference(_channelReferences, channel);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeAsync(channel, false, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences, channel);
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
				"Aevo does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var book = await RestClient.GetOrderBookAsync(market.InstrumentName,
			cancellationToken);
		OrderBookState state;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(market.InstrumentName, out state))
				_books[market.InstrumentName] = state = new();
			state.Apply(book);
		}
		await SendDepthAsync(market.InstrumentName, state, mdMsg.TransactionId,
			depth, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var channel = DepthChannel(market.InstrumentName);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.InstrumentName,
				Depth = depth,
			});
			subscribe = AddReference(_channelReferences, channel);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeAsync(channel, false, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences, channel);
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
				"Aevo trade start time cannot be later than end time.");
		var count = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var response = await RestClient.GetTradesAsync(market.InstrumentName,
			from, to, cancellationToken);
		var history = (response?.Trades ?? [])
			.Where(static trade => trade?.CreatedTimestamp.IsEmpty() == false)
			.Where(trade => from is null ||
				trade.CreatedTimestamp.FromAevoNanoseconds() >= from.Value)
			.Where(trade => trade.CreatedTimestamp.FromAevoNanoseconds() <= to)
			.OrderBy(static trade => trade.CreatedTimestamp,
				StringComparer.Ordinal)
			.TakeLast(count)
			.ToArray();
		foreach (var trade in history)
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		var subscription = new TickSubscription
		{
			TransactionId = mdMsg.TransactionId,
			Symbol = market.InstrumentName,
		};
		foreach (var trade in history)
			if (!trade.TradeId.IsEmpty())
				subscription.SeenTrades.Add(trade.TradeId);
		var channel = TradeChannel(market.InstrumentName);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddReference(_channelReferences, channel);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeAsync(channel, false, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseReference(_channelReferences, channel);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnTickerAsync(AevoTickerData data,
		CancellationToken cancellationToken)
	{
		if (data is null)
			return;
		var time = data.Timestamp.IsEmpty()
			? ServerTime
			: data.Timestamp.FromAevoNanoseconds();
		UpdateServerTime(time);
		foreach (var ticker in data.Tickers ?? [])
		{
			if (ticker?.InstrumentName.IsEmpty() != false)
				continue;
			MarketSubscription[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _level1Subscriptions.Values.Where(
					subscription => subscription.Symbol.Equals(
						ticker.InstrumentName, StringComparison.Ordinal))];
			foreach (var subscription in subscriptions)
				await SendLevel1Async(ticker, time, subscription.TransactionId,
					cancellationToken);
		}
	}

	private async ValueTask OnOrderBookAsync(AevoOrderBook book,
		CancellationToken cancellationToken)
	{
		if (book?.InstrumentName.IsEmpty() != false)
			return;
		OrderBookState state;
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(book.InstrumentName, out state))
				_books[book.InstrumentName] = state = new();
			state.Apply(book);
			subscriptions = [.. _depthSubscriptions.Values.Where(
				subscription => subscription.Symbol.Equals(book.InstrumentName,
					StringComparison.Ordinal))];
		}
		foreach (var subscription in subscriptions)
			await SendDepthAsync(book.InstrumentName, state,
				subscription.TransactionId, subscription.Depth,
				cancellationToken);
	}

	private async ValueTask OnTradeAsync(AevoTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.InstrumentName.IsEmpty() != false)
			return;
		TickSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Values.Where(subscription =>
				subscription.Symbol.Equals(trade.InstrumentName,
					StringComparison.Ordinal) &&
				(trade.TradeId.IsEmpty() ||
					subscription.SeenTrades.Add(trade.TradeId)))];
		foreach (var subscription in subscriptions)
			await SendTradeAsync(trade, subscription.TransactionId,
				cancellationToken);
	}

	private ValueTask SendLevel1Async(AevoInstrument instrument, DateTime time,
		long transactionId, CancellationToken cancellationToken)
	{
		if (instrument is null)
			return default;
		var message = new Level1ChangeMessage
		{
			SecurityId = instrument.InstrumentName.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep,
			instrument.PriceStep.TryParseAevoDecimal())
		.TryAdd(Level1Fields.VolumeStep,
			instrument.AmountStep.TryParseAevoDecimal())
		.TryAdd(Level1Fields.State, instrument.IsActive
			? SecurityStates.Trading
			: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice,
			instrument.MarkPrice.TryParseAevoDecimal())
		.TryAdd(Level1Fields.SettlementPrice,
			instrument.MarkPrice.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Index,
			instrument.IndexPrice.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestBidPrice,
			instrument.BestBid?.Price.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestBidVolume,
			instrument.BestBid?.Amount.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestAskPrice,
			instrument.BestAsk?.Price.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestAskVolume,
			instrument.BestAsk?.Amount.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Volume,
			instrument.Statistics?.DailyVolumeContracts.TryParseAevoDecimal())
		.TryAdd(Level1Fields.OpenInterest,
			instrument.Statistics?.TotalOpenInterest.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Delta,
			instrument.Greeks?.Delta.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Gamma,
			instrument.Greeks?.Gamma.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Rho,
			instrument.Greeks?.Rho.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Theta,
			instrument.Greeks?.Theta.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Vega,
			instrument.Greeks?.Vega.TryParseAevoDecimal())
		.TryAdd(Level1Fields.ImpliedVolatility,
			instrument.Greeks?.ImpliedVolatility.TryParseAevoDecimal());
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendLevel1Async(AevoTicker ticker, DateTime time,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.InstrumentName.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.SettlementPrice,
			ticker.Mark?.Price.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Index, ticker.IndexPrice.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestBidPrice,
			ticker.Bid?.Price.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestBidVolume,
			ticker.Bid?.Amount.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestAskPrice,
			ticker.Ask?.Price.TryParseAevoDecimal())
		.TryAdd(Level1Fields.BestAskVolume,
			ticker.Ask?.Amount.TryParseAevoDecimal())
		.TryAdd(Level1Fields.OpenInterest,
			ticker.OpenInterest.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Delta,
			ticker.Mark?.Delta.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Gamma,
			ticker.Mark?.Gamma.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Rho,
			ticker.Mark?.Rho.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Theta,
			ticker.Mark?.Theta.TryParseAevoDecimal())
		.TryAdd(Level1Fields.Vega,
			ticker.Mark?.Vega.TryParseAevoDecimal())
		.TryAdd(Level1Fields.ImpliedVolatility,
			ticker.Mark?.ImpliedVolatility.TryParseAevoDecimal()),
			cancellationToken);

	private ValueTask SendDepthAsync(string symbol, OrderBookState state,
		long transactionId, int depth, CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		DateTime time;
		using (_sync.EnterScope())
		{
			bids = state.GetBids(depth);
			asks = state.GetAsks(depth);
			time = state.ServerTime == default ? ServerTime : state.ServerTime;
		}
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(AevoTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = trade.CreatedTimestamp.FromAevoNanoseconds();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.InstrumentName.ToStockSharp(),
			ServerTime = time,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ParseAevoDecimal("trade price"),
			TradeVolume = trade.Amount.ParseAevoDecimal("trade amount"),
			OriginSide = trade.Side.ToStockSharpSide(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(AevoInstrument market,
		long transactionId)
	{
		var baseAsset = market.BaseAsset.IsEmpty()
			? market.InstrumentName.Split('-')[0]
			: market.BaseAsset;
		var quoteAsset = market.QuoteAsset.IsEmpty() ? "USDC" : market.QuoteAsset;
		var message = new SecurityMessage
		{
			SecurityId = market.InstrumentName.ToStockSharp(),
			Name = market.InstrumentName,
			ShortName = market.InstrumentName,
			Class = market.InstrumentType.ToString().ToUpperInvariant(),
			SecurityType = market.InstrumentType.ToStockSharp(),
			Currency = quoteAsset.ToAevoCurrency(),
			PriceStep = market.PriceStep.TryParseAevoDecimal(),
			VolumeStep = market.AmountStep.TryParseAevoDecimal(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(baseAsset.ToUpperInvariant());
		if (market.InstrumentType == AevoInstrumentTypes.Option)
		{
			message.Strike = market.Strike.TryParseAevoDecimal();
			message.OptionType = market.OptionType?.ToStockSharp();
			if (!market.Expiry.IsEmpty())
				message.ExpiryDate = market.Expiry.FromAevoNanoseconds();
		}
		return message;
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseReference(_channelReferences,
					TickerChannel(removed.Symbol));
		if (unsubscribe)
			await SocketClient.UnsubscribeAsync(TickerChannel(removed.Symbol),
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
					DepthChannel(removed.Symbol));
		if (unsubscribe)
			await SocketClient.UnsubscribeAsync(DepthChannel(removed.Symbol),
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
					TradeChannel(removed.Symbol));
		if (unsubscribe)
			await SocketClient.UnsubscribeAsync(TradeChannel(removed.Symbol),
				cancellationToken);
	}

	private static string TickerChannel(string symbol)
		=> "ticker-500ms:" + symbol;

	private static string DepthChannel(string symbol)
		=> "orderbook-100ms:" + symbol;

	private static string TradeChannel(string symbol)
		=> "trades:" + symbol;
}
