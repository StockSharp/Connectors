namespace StockSharp.QFEX;

using Native;

public partial class QFEXMessageAdapter
{
	private static readonly QFEXMarketChannels[] _level1Channels =
	[
		QFEXMarketChannels.BestBidOffer,
		QFEXMarketChannels.Trade,
		QFEXMarketChannels.Underlier,
		QFEXMarketChannels.MarkPrice,
		QFEXMarketChannels.OpenInterest,
		QFEXMarketChannels.MinimumMaximumPrice,
	];

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
			StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.QFEX))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.EqualsIgnoreCase(market.Symbol))
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
				"QFEX does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendReferenceLevel1Async(market, mdMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var keys = GetLevel1Keys(market.Symbol);
		QFEXMarketStreamKey[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
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
		if (mdMsg.From is not null || mdMsg.To is not null ||
			mdMsg.IsHistoryOnly())
			throw new NotSupportedException(
				"QFEX does not expose historical order-book snapshots.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		var key = new QFEXMarketStreamKey(QFEXMarketChannels.Level2,
			market.Symbol, null);
		QFEXMarketStreamKey[] added;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
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
		if (mdMsg.From is not null || mdMsg.To is not null ||
			mdMsg.IsHistoryOnly())
			throw new NotSupportedException(
				"QFEX public trade history is not exposed by the API.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		var key = new QFEXMarketStreamKey(QFEXMarketChannels.Trade,
			market.Symbol, null);
		QFEXMarketStreamKey[] added;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
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
		var interval = timeFrame.ToQFEX();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var earliest = to - TimeSpan.FromTicks(checked(timeFrame.Ticks *
			(long)count));
		var from = mdMsg.From?.EnsureUtc() ?? earliest;
		if (from < earliest)
			from = earliest;
		var candles = await LoadCandlesAsync(market.Symbol, interval, from, to,
			count, cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(market.Symbol, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new QFEXMarketStreamKey(QFEXMarketChannels.Candle,
			market.Symbol, interval);
		QFEXMarketStreamKey[] added;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = market.Symbol,
				Interval = interval,
				TimeFrame = timeFrame,
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
		QFEXMarketStreamKey[] removed = [];
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId,
				out var subscription))
				removed = ReleaseStreamReferences(
					GetLevel1Keys(subscription.Symbol));
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		QFEXMarketStreamKey[] removed = [];
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId,
				out var subscription))
				removed = ReleaseStreamReferences(
				[new(QFEXMarketChannels.Level2, subscription.Symbol, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		QFEXMarketStreamKey[] removed = [];
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId,
				out var subscription))
				removed = ReleaseStreamReferences(
					[new(QFEXMarketChannels.Trade, subscription.Symbol, null)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		QFEXMarketStreamKey[] removed = [];
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId,
				out var subscription))
				removed = ReleaseStreamReferences(
					[new(QFEXMarketChannels.Candle, subscription.Symbol,
						subscription.Interval)]);
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask SubscribeStreamsAsync(
		IEnumerable<QFEXMarketStreamKey> keys,
		CancellationToken cancellationToken)
	{
		var subscribed = new List<QFEXMarketStreamKey>();
		try
		{
			foreach (var key in keys)
			{
				await MarketSocket.SubscribeAsync(key.Channel, key.Symbol,
					key.Interval, cancellationToken);
				subscribed.Add(key);
			}
		}
		catch
		{
			foreach (var key in subscribed.AsEnumerable().Reverse())
				try
				{
					await MarketSocket.UnsubscribeAsync(key.Channel, key.Symbol,
						key.Interval, cancellationToken);
				}
				catch (Exception error)
				{
					await SendOutErrorAsync(error, cancellationToken);
				}
			throw;
		}
	}

	private async ValueTask UnsubscribeStreamsAsync(
		IEnumerable<QFEXMarketStreamKey> keys,
		CancellationToken cancellationToken)
	{
		foreach (var key in keys)
			await MarketSocket.UnsubscribeAsync(key.Channel, key.Symbol,
				key.Interval, cancellationToken);
	}

	private QFEXMarketStreamKey[] AddStreamReferences(
		IEnumerable<QFEXMarketStreamKey> keys)
	{
		var added = new List<QFEXMarketStreamKey>();
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

	private QFEXMarketStreamKey[] ReleaseStreamReferences(
		IEnumerable<QFEXMarketStreamKey> keys)
	{
		var removed = new List<QFEXMarketStreamKey>();
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

	private static QFEXMarketStreamKey[] GetLevel1Keys(string symbol)
		=> [.. _level1Channels.Select(channel =>
			new QFEXMarketStreamKey(channel, symbol, null))];

	private async ValueTask OnMarketDataAsync(QFEXMarketMessageTypes type,
		QFEXMarketDataMessage message, CancellationToken cancellationToken)
	{
		if (message?.Symbol.IsEmpty() != false)
			return;
		var time = message.Time.TryToQFEXTime() ??
			message.Start.TryToQFEXTime() ?? ServerTime;
		UpdateServerTime(time);
		switch (type)
		{
			case QFEXMarketMessageTypes.Level2:
				await OnDepthAsync(message, time, cancellationToken);
				break;
			case QFEXMarketMessageTypes.Trade:
				await OnPublicTradeAsync(message, time, cancellationToken);
				await OnTradeLevel1Async(message, time, cancellationToken);
				break;
			case QFEXMarketMessageTypes.BestBidOffer:
			case QFEXMarketMessageTypes.Underlier:
			case QFEXMarketMessageTypes.MarkPrice:
			case QFEXMarketMessageTypes.OpenInterest:
			case QFEXMarketMessageTypes.MinimumMaximumPrice:
				await OnLevel1Async(type, message, time, cancellationToken);
				break;
			case QFEXMarketMessageTypes.Candle:
				await OnCandleAsync(message, cancellationToken);
				break;
			case QFEXMarketMessageTypes.Funding:
			case QFEXMarketMessageTypes.MarketStatistics:
			case QFEXMarketMessageTypes.Subscribed:
			case QFEXMarketMessageTypes.Unsubscribed:
				break;
			default:
				throw new InvalidDataException(
					"Unsupported QFEX market-data message type '" + type + "'.");
		}
	}

	private async ValueTask OnLevel1Async(QFEXMarketMessageTypes type,
		QFEXMarketDataMessage update, DateTime time,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(update.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
		{
			var message = new Level1ChangeMessage
			{
				SecurityId = update.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			};
			switch (type)
			{
				case QFEXMarketMessageTypes.BestBidOffer:
				AddBestQuotes(message, update, time);
				break;
				case QFEXMarketMessageTypes.Underlier:
					message.TryAdd(Level1Fields.Index,
						update.Price.TryParseDecimal());
					break;
				case QFEXMarketMessageTypes.MarkPrice:
					message.TryAdd(Level1Fields.TheorPrice,
						update.Price.TryParseDecimal());
					break;
				case QFEXMarketMessageTypes.OpenInterest:
					message.TryAdd(Level1Fields.OpenInterest,
						update.OpenInterest.TryParseDecimal());
					break;
				case QFEXMarketMessageTypes.MinimumMaximumPrice:
					message
						.TryAdd(Level1Fields.MinPrice,
							update.MinimumPrice.TryParseDecimal())
						.TryAdd(Level1Fields.MaxPrice,
							update.MaximumPrice.TryParseDecimal());
					break;
			}
			if (message.Changes.Count > 0)
				await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask OnTradeLevel1Async(QFEXMarketDataMessage trade,
		DateTime time, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = trade.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.LastTradePrice,
				trade.Price.TryParseDecimal())
			.TryAdd(Level1Fields.LastTradeVolume,
				trade.Size.TryParseDecimal())
			.TryAdd(Level1Fields.LastTradeTime, time)
			.TryAdd(Level1Fields.LastTradeOrigin,
				trade.Side?.ToStockSharp()), cancellationToken);
	}

	private async ValueTask OnDepthAsync(QFEXMarketDataMessage book,
		DateTime time, CancellationToken cancellationToken)
	{
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(book.Symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = book.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = subscription.Id,
				State = QuoteChangeStates.SnapshotComplete,
				SeqNum = book.Sequence ?? 0,
				Bids = ToQuotes(book.Bids, subscription.Depth, true),
				Asks = ToQuotes(book.Asks, subscription.Depth, false),
			}, cancellationToken);
	}

	private async ValueTask OnPublicTradeAsync(QFEXMarketDataMessage trade,
		DateTime time, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = trade.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
				TradeStringId = trade.TradeId,
				TradePrice = trade.Price.ParseDecimal("trade price"),
				TradeVolume = trade.Size.ParseDecimal("trade size"),
				OriginSide = trade.Side?.ToStockSharp(),
			}, cancellationToken);
	}

	private async ValueTask OnCandleAsync(QFEXMarketDataMessage candle,
		CancellationToken cancellationToken)
	{
		if (candle.Resolution is not QFEXCandleIntervals interval)
			throw new InvalidDataException(
				"QFEX candle update has no resolution.");
		(long Id, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(candle.Symbol) &&
					pair.Value.Interval == interval)
				.Select(static pair => (pair.Key, pair.Value.TimeFrame))];
		foreach (var subscription in subscriptions)
			await SendCandleAsync(candle.Symbol, candle,
				subscription.TimeFrame, subscription.Id, cancellationToken);
	}

	private ValueTask SendReferenceLevel1Async(QFEXReferenceDataSymbol market,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.Index, market.UnderlierPrice.TryParseDecimal())
		.TryAdd(Level1Fields.MinPrice, market.MinimumPrice.TryParseDecimal())
		.TryAdd(Level1Fields.MaxPrice, market.MaximumPrice.TryParseDecimal())
		.TryAdd(Level1Fields.PriceStep, market.TickSize.TryParseDecimal())
		.TryAdd(Level1Fields.VolumeStep, market.LotSize.TryParseDecimal())
		.TryAdd(Level1Fields.State, market.Status == QFEXSymbolStatuses.Active
			? SecurityStates.Trading
			: SecurityStates.Stoped), cancellationToken);

	private async ValueTask<QFEXCandle[]> LoadCandlesAsync(string symbol,
		QFEXCandleIntervals interval, DateTime from, DateTime to, int count,
		CancellationToken cancellationToken)
	{
		var candles = await RestClient.GetCandlesAsync(symbol, interval, from,
			to, cancellationToken);
		return [.. candles
			.Where(static candle => candle is not null)
			.Where(candle => (candle.StartedAt.TryToQFEXTime() ??
				candle.Start.TryToQFEXTime()) is DateTime time &&
				time >= from && time <= to)
			.OrderBy(candle => candle.StartedAt.TryToQFEXTime() ??
				candle.Start.TryToQFEXTime())
			.TakeLast(count)];
	}

	private ValueTask SendCandleAsync(string symbol, QFEXCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.StartedAt.TryToQFEXTime() ??
			candle.Start.ToQFEXTime("candle start");
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.ParseDecimal("candle open"),
			HighPrice = candle.High.ParseDecimal("candle high"),
			LowPrice = candle.Low.ParseDecimal("candle low"),
			ClosePrice = candle.Close.ParseDecimal("candle close"),
			TotalVolume = candle.BaseTokenVolume.TryParseDecimal() ?? 0m,
			TotalPrice = candle.UsdVolume.TryParseDecimal() ?? 0m,
			TotalTicks = ParseTrades(candle.Trades),
			OpenInterest = candle.StartingOpenInterest.TryParseDecimal(),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = openTime + timeFrame <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol,
		QFEXMarketDataMessage candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Start.ToQFEXTime("candle start");
		UpdateServerTime(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.ParseDecimal("candle open"),
			HighPrice = candle.High.ParseDecimal("candle high"),
			LowPrice = candle.Low.ParseDecimal("candle low"),
			ClosePrice = candle.Close.ParseDecimal("candle close"),
			TotalPrice = candle.UsdVolume.TryParseDecimal() ?? 0m,
			TotalTicks = ParseTrades(candle.Trades),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = openTime + timeFrame <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(
		QFEXReferenceDataSymbol market, long transactionId)
		=> new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = market.BaseAsset + "/" + market.QuoteAsset + " perpetual",
			ShortName = market.Symbol,
			Class = market.ProductCategory.ToString().ToUpperInvariant() +
				"_PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.TickSize.ParseDecimal("tick size"),
			VolumeStep = market.LotSize.ParseDecimal("lot size"),
			MinVolume = market.MinimumQuantity.TryParseDecimal(),
			MaxVolume = market.MaximumQuantity.TryParseDecimal(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		}.TryFillUnderlyingId(market.BaseAsset);

	private static QuoteChange[] ToQuotes(string[][] levels, int depth,
		bool isBids)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is not { Length: >= 2 })
				throw new InvalidDataException(
					"QFEX returned a malformed order-book level.");
			var price = level[0].ParseDecimal("book price");
			var volume = level[1].ParseDecimal("book quantity");
			if (price <= 0 || volume < 0)
				throw new InvalidDataException(
					"QFEX returned a non-positive price or negative book quantity.");
			if (volume > 0)
				quotes.Add(new(price, volume));
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private static void AddBestQuotes(Level1ChangeMessage message,
		QFEXMarketDataMessage update, DateTime time)
	{
		var bid = GetBestQuote(update.Bids);
		var ask = GetBestQuote(update.Asks);
		message
			.TryAdd(Level1Fields.BestBidPrice, bid.Price)
			.TryAdd(Level1Fields.BestBidVolume, bid.Volume)
			.TryAdd(Level1Fields.BestBidTime, bid.Price is null ? null : time)
			.TryAdd(Level1Fields.BestAskPrice, ask.Price)
			.TryAdd(Level1Fields.BestAskVolume, ask.Volume)
			.TryAdd(Level1Fields.BestAskTime, ask.Price is null ? null : time);
	}

	private static (decimal? Price, decimal? Volume) GetBestQuote(
		string[][] levels)
	{
		var level = levels?.FirstOrDefault();
		if (level is null)
			return (null, null);
		if (level.Length < 2)
			throw new InvalidDataException(
				"QFEX returned a malformed best quote.");
		return (level[0].ParseDecimal("best quote price"),
			level[1].ParseDecimal("best quote quantity"));
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.EnsureUtc())
			return ((to - from.EnsureUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit.Min(500);
	}

	private static int? ParseTrades(string value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result) && result >= 0
			? result
			: null;
}
