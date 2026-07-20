namespace StockSharp.DydxChain;

public partial class DydxChainMessageAdapter
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
		foreach (var market in GetMarkets().OrderBy(static item => item.Ticker,
			StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.DydxChain))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Ticker,
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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
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
				"dYdX does not publish historical Level1 changes.");

		var market = GetMarket(mdMsg.SecurityId);
		var book = await ApiClient.GetOrderbookAsync(market.Ticker,
			cancellationToken);
		ApplyBookSnapshot(market.Ticker, book);
		var trades = await ApiClient.GetTradesAsync(market.Ticker, 1,
			cancellationToken);
		await SendLevel1SnapshotAsync(market, book,
			trades.FirstOrDefault(), mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var keys = new[]
		{
			new DydxChainSocketSubscriptionKey(
				DydxChainSocketChannels.Orderbook, market.Ticker),
			new DydxChainSocketSubscriptionKey(
				DydxChainSocketChannels.Trades, market.Ticker),
		};
		DydxChainSocketSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, market.Ticker);
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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
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
				"dYdX does not publish historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 1000).Min(1000).Max(1).To<int>();
		var snapshot = await ApiClient.GetOrderbookAsync(market.Ticker,
			cancellationToken);
		ApplyBookSnapshot(market.Ticker, snapshot);
		await SendBookSnapshotAsync(market.Ticker, snapshot,
			mdMsg.TransactionId, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new DydxChainSocketSubscriptionKey(
			DydxChainSocketChannels.Orderbook, market.Ticker);
		DydxChainSocketSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Ticker = market.Ticker,
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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
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
		var from = mdMsg.From?.EnsureUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var limit = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var trades = (await ApiClient.GetTradesAsync(market.Ticker, limit,
			cancellationToken))
			.Where(static trade => trade is not null && !trade.CreatedAt.IsEmpty())
			.Where(trade =>
			{
				var time = trade.CreatedAt.ParseUtcTime("trade time");
				return time <= to && (from is null || time >= from.Value);
			})
			.OrderBy(static trade => trade.CreatedAt,
				StringComparer.Ordinal)
			.ToArray();
		foreach (var trade in trades)
			await SendPublicTradeAsync(market.Ticker, trade,
				mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new DydxChainSocketSubscriptionKey(
			DydxChainSocketChannels.Trades, market.Ticker);
		DydxChainSocketSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, market.Ticker);
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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
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
		var resolution = timeFrame.ToDydxChain();
		var from = mdMsg.From?.EnsureUtc();
		var to = (mdMsg.To ?? ServerTime).EnsureUtc();
		var limit = GetCandleCount(mdMsg, timeFrame, to);
		var candles = await ApiClient.GetCandlesAsync(market.Ticker,
			resolution, from, to, limit, cancellationToken);
		foreach (var candle in candles.Where(static candle => candle is not null)
			.OrderBy(static candle => candle.StartedAt,
				StringComparer.Ordinal))
			await SendCandleAsync(candle, mdMsg.TransactionId,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new DydxChainSocketSubscriptionKey(
			DydxChainSocketChannels.Candles,
			market.Ticker + "/" + resolution.ToWire());
		DydxChainSocketSubscriptionKey[] added;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Ticker = market.Ticker,
				Resolution = resolution,
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

	private async ValueTask OnMarketsSnapshotAsync(
		DydxChainMarketsResponse snapshot,
		CancellationToken cancellationToken)
	{
		foreach (var market in snapshot?.Markets ?? [])
		{
			if (market?.Ticker.IsEmpty() != false)
				continue;
			market.Ticker = market.Ticker.NormalizeTicker();
			long[] ids;
			using (_sync.EnterScope())
			{
				if (_markets.TryGetValue(market.Ticker, out var existing))
					CopyMarket(existing, market);
				if (market.OraclePrice.TryParseDecimal() is decimal oracle &&
					oracle > 0)
					_oraclePrices[market.Ticker] = oracle;
				ids = GetLevel1IdsUnsafe(market.Ticker);
			}
			foreach (var id in ids)
				await SendMarketLevel1Async(market.Ticker, id,
					cancellationToken);
		}
	}

	private async ValueTask OnMarketsUpdateAsync(DydxChainMarketUpdate update,
		CancellationToken cancellationToken)
	{
		var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using (_sync.EnterScope())
		{
			foreach (var price in update?.OraclePrices ?? [])
			{
				if (price?.Ticker.IsEmpty() != false ||
					price.OraclePrice.TryParseDecimal() is not decimal value ||
					value <= 0)
					continue;
				var ticker = price.Ticker.NormalizeTicker();
				_oraclePrices[ticker] = value;
				if (_markets.TryGetValue(ticker, out var market))
					market.OraclePrice = price.OraclePrice;
				changed.Add(ticker);
			}
			foreach (var item in update?.Trading ?? [])
			{
				var ticker = (item?.Ticker.IsEmpty() == false
					? item.Ticker
					: item?.Key)?.NormalizeTicker();
				if (ticker.IsEmpty() ||
					!_markets.TryGetValue(ticker, out var market))
					continue;
				ApplyMarketUpdate(market, item);
				changed.Add(ticker);
			}
		}
		foreach (var ticker in changed)
		{
			long[] ids;
			using (_sync.EnterScope())
				ids = GetLevel1IdsUnsafe(ticker);
			foreach (var id in ids)
				await SendMarketLevel1Async(ticker, id, cancellationToken);
		}
	}

	private async ValueTask OnOrderbookSnapshotAsync(string ticker,
		DydxChainOrderbookResponse snapshot,
		CancellationToken cancellationToken)
	{
		ticker = ticker.NormalizeTicker();
		ApplyBookSnapshot(ticker, snapshot);
		UpdateServer(DateTime.UtcNow);
		(long Id, int Depth)[] depthIds;
		long[] level1Ids;
		using (_sync.EnterScope())
		{
			depthIds = [.. _depthSubscriptions
				.Where(pair => pair.Value.Ticker.Equals(ticker,
					StringComparison.OrdinalIgnoreCase))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
			level1Ids = GetLevel1IdsUnsafe(ticker);
		}
		foreach (var subscription in depthIds)
			await SendBookSnapshotAsync(ticker, snapshot, subscription.Id,
				subscription.Depth, cancellationToken);
		foreach (var id in level1Ids)
			await SendBestQuotesAsync(ticker, id, cancellationToken);
	}

	private async ValueTask OnOrderbookUpdateAsync(string ticker,
		DydxChainOrderbookUpdate update, CancellationToken cancellationToken)
	{
		ticker = ticker.NormalizeTicker();
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(ticker, out var book))
				return;
			book.Apply(update);
		}
		UpdateServer(DateTime.UtcNow);
		long[] depthIds;
		long[] level1Ids;
		using (_sync.EnterScope())
		{
			depthIds = [.. _depthSubscriptions
				.Where(pair => pair.Value.Ticker.Equals(ticker,
					StringComparison.OrdinalIgnoreCase))
				.Select(static pair => pair.Key)];
			level1Ids = GetLevel1IdsUnsafe(ticker);
		}
		foreach (var id in depthIds)
			await SendBookIncrementAsync(ticker, update, id,
				cancellationToken);
		foreach (var id in level1Ids)
			await SendBestQuotesAsync(ticker, id, cancellationToken);
	}

	private async ValueTask OnTradesAsync(string ticker,
		DydxChainTrade[] trades, CancellationToken cancellationToken)
	{
		ticker = ticker.NormalizeTicker();
		foreach (var trade in trades ?? [])
		{
			if (trade is null)
				continue;
			long[] tickIds;
			long[] level1Ids;
			using (_sync.EnterScope())
			{
				tickIds = [.. _tickSubscriptions
					.Where(pair => pair.Value.Equals(ticker,
						StringComparison.OrdinalIgnoreCase))
					.Select(static pair => pair.Key)];
				level1Ids = GetLevel1IdsUnsafe(ticker);
			}
			foreach (var id in tickIds)
				await SendPublicTradeAsync(ticker, trade, id,
					cancellationToken);
			foreach (var id in level1Ids)
				await SendLastTradeAsync(ticker, trade, id,
					cancellationToken);
		}
	}

	private async ValueTask OnCandlesAsync(string streamId,
		DydxChainCandle[] candles, CancellationToken cancellationToken)
	{
		var separator = streamId.LastIndexOf('/');
		if (separator <= 0)
			return;
		var ticker = streamId[..separator].NormalizeTicker();
		foreach (var candle in candles ?? [])
		{
			if (candle is null)
				continue;
			long[] ids;
			using (_sync.EnterScope())
				ids = [.. _candleSubscriptions
					.Where(pair => pair.Value.Ticker.Equals(ticker,
						StringComparison.OrdinalIgnoreCase) &&
						pair.Value.Resolution == candle.Resolution)
					.Select(static pair => pair.Key)];
			foreach (var id in ids)
				await SendCandleAsync(candle, id, cancellationToken);
		}
	}

	private async ValueTask SendLevel1SnapshotAsync(DydxChainMarket market,
		DydxChainOrderbookResponse book, DydxChainTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		await SendMarketLevel1Async(market.Ticker, transactionId,
			cancellationToken);
		if (book is not null)
			await SendBestQuotesAsync(market.Ticker, transactionId,
				cancellationToken);
		if (trade is not null)
			await SendLastTradeAsync(market.Ticker, trade, transactionId,
				cancellationToken);
	}

	private ValueTask SendMarketLevel1Async(string ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		DydxChainMarket market;
		decimal? oracle;
		using (_sync.EnterScope())
		{
			if (!_markets.TryGetValue(ticker, out market))
				return default;
			oracle = _oraclePrices.TryGetValue(ticker, out var value)
				? value
				: null;
		}
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.Index, oracle)
		.TryAdd(Level1Fields.OpenInterest,
			market.OpenInterest.TryParseDecimal())
		.TryAdd(Level1Fields.Volume, market.Volume24Hours.TryParseDecimal())
		.TryAdd(Level1Fields.Change,
			market.PriceChange24Hours.TryParseDecimal())
		.TryAdd(Level1Fields.PriceStep, market.TickSize.TryParseDecimal())
		.TryAdd(Level1Fields.VolumeStep, market.StepSize.TryParseDecimal())
		.TryAdd(Level1Fields.State, ToSecurityState(market.Status)),
			cancellationToken);
	}

	private ValueTask SendBestQuotesAsync(string ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		KeyValuePair<decimal, decimal>? bid = null;
		KeyValuePair<decimal, decimal>? ask = null;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(ticker, out var book))
				return default;
			if (book.Bids.Count > 0)
				bid = book.Bids.First();
			if (book.Asks.Count > 0)
				ask = book.Asks.First();
		}
		var time = ServerTime;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Key)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Value)
		.TryAdd(Level1Fields.BestBidTime, bid is null ? null : time)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Key)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Value)
		.TryAdd(Level1Fields.BestAskTime, ask is null ? null : time),
			cancellationToken);
	}

	private ValueTask SendLastTradeAsync(string ticker, DydxChainTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = trade.CreatedAt.ParseUtcTime("trade time");
		UpdateServer(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice,
			trade.Price.TryParseDecimal())
		.TryAdd(Level1Fields.LastTradeVolume,
			trade.Size.TryParseDecimal())
		.TryAdd(Level1Fields.LastTradeTime, time)
		.TryAdd(Level1Fields.LastTradeOrigin, trade.Side.ToStockSharp()),
			cancellationToken);
	}

	private ValueTask SendBookSnapshotAsync(string ticker,
		DydxChainOrderbookResponse snapshot, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = ticker.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(snapshot?.Bids, depth, true, false),
			Asks = ToQuotes(snapshot?.Asks, depth, false, false),
		}, cancellationToken);

	private ValueTask SendBookIncrementAsync(string ticker,
		DydxChainOrderbookUpdate update, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = ticker.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.Increment,
			Bids = ToQuotes(update?.Bids, int.MaxValue, true, true),
			Asks = ToQuotes(update?.Asks, int.MaxValue, false, true),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string ticker, DydxChainTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = trade.CreatedAt.ParseUtcTime("trade time");
		UpdateServer(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = ticker.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ParseDecimal("trade price"),
			TradeVolume = trade.Size.ParseDecimal("trade size", true),
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(DydxChainCandle candle,
		long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.StartedAt.ParseUtcTime("candle start time");
		var timeFrame = candle.Resolution.ToStockSharp();
		var closeTime = openTime + timeFrame;
		UpdateServer(openTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Ticker.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open.ParseDecimal("candle open"),
			HighPrice = candle.High.ParseDecimal("candle high"),
			LowPrice = candle.Low.ParseDecimal("candle low"),
			ClosePrice = candle.Close.ParseDecimal("candle close"),
			TotalVolume = candle.BaseTokenVolume.ParseDecimal(
				"candle volume", true),
			TotalTicks = candle.Trades,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private void ApplyBookSnapshot(string ticker,
		DydxChainOrderbookResponse snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(ticker, out var book))
				_books.Add(ticker, book = new());
			book.Reset(snapshot);
		}
	}

	private DydxChainSocketSubscriptionKey[] AddStreamReferences(
		IEnumerable<DydxChainSocketSubscriptionKey> keys)
	{
		var added = new List<DydxChainSocketSubscriptionKey>();
		foreach (var key in keys)
		{
			if (_streamReferences.TryGetValue(key, out var count))
				_streamReferences[key] = count + 1;
			else
			{
				_streamReferences.Add(key, 1);
				added.Add(key);
			}
		}
		return [.. added];
	}

	private DydxChainSocketSubscriptionKey[] ReleaseStreamReferences(
		IEnumerable<DydxChainSocketSubscriptionKey> keys)
	{
		var removed = new List<DydxChainSocketSubscriptionKey>();
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

	private async ValueTask SubscribeStreamsAsync(
		IEnumerable<DydxChainSocketSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		foreach (var key in keys)
			await SocketClient.SubscribeAsync(key, cancellationToken);
	}

	private async ValueTask UnsubscribeStreamsAsync(
		IEnumerable<DydxChainSocketSubscriptionKey> keys,
		CancellationToken cancellationToken)
	{
		foreach (var key in keys)
			await SocketClient.UnsubscribeAsync(key, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		DydxChainSocketSubscriptionKey[] removed;
		using (_sync.EnterScope())
		{
			if (!_level1Subscriptions.Remove(transactionId, out var ticker))
				return;
			removed = ReleaseStreamReferences(
			[
				new(DydxChainSocketChannels.Orderbook, ticker),
				new(DydxChainSocketChannels.Trades, ticker),
			]);
		}
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DydxChainSocketSubscriptionKey[] removed;
		using (_sync.EnterScope())
		{
			if (!_depthSubscriptions.Remove(transactionId, out var subscription))
				return;
			removed = ReleaseStreamReferences(
			[
				new(DydxChainSocketChannels.Orderbook, subscription.Ticker),
			]);
		}
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DydxChainSocketSubscriptionKey[] removed;
		using (_sync.EnterScope())
		{
			if (!_tickSubscriptions.Remove(transactionId, out var ticker))
				return;
			removed = ReleaseStreamReferences(
			[
				new(DydxChainSocketChannels.Trades, ticker),
			]);
		}
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DydxChainSocketSubscriptionKey[] removed;
		using (_sync.EnterScope())
		{
			if (!_candleSubscriptions.Remove(transactionId,
				out var subscription))
				return;
			removed = ReleaseStreamReferences(
			[
				new(DydxChainSocketChannels.Candles,
					subscription.Ticker + "/" +
					subscription.Resolution.ToWire()),
			]);
		}
		await UnsubscribeStreamsAsync(removed, cancellationToken);
	}

	private long[] GetLevel1IdsUnsafe(string ticker)
		=> [.. _level1Subscriptions
			.Where(pair => pair.Value.Equals(ticker,
				StringComparison.OrdinalIgnoreCase))
			.Select(static pair => pair.Key)];

	private static SecurityMessage CreateSecurity(DydxChainMarket market,
		long transactionId)
	{
		var parts = market.Ticker.Split('-');
		var baseAsset = parts.FirstOrDefault() ?? market.Ticker;
		var quoteAsset = parts.Length > 1 ? parts[^1] : "USD";
		return new SecurityMessage
		{
			SecurityId = market.Ticker.ToStockSharp(),
			Name = baseAsset + "/" + quoteAsset + " perpetual",
			ShortName = market.Ticker,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = market.Ticker.ToCurrency(),
			PriceStep = market.TickSize.ParseDecimal("tick size"),
			VolumeStep = market.StepSize.ParseDecimal("step size"),
			MinVolume = market.StepSize.TryParseDecimal(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		}.TryFillUnderlyingId(baseAsset);
	}

	private static QuoteChange[] ToQuotes(DydxChainPriceLevel[] levels,
		int depth, bool isBids, bool isIncrement)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParseDecimal("order-book price");
			var size = level.Size.ParseDecimal("order-book size", true);
			if (price <= 0 || !isIncrement && size == 0)
				continue;
			quotes.Add(new(price, size));
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private static SecurityStates ToSecurityState(DydxChainMarketStatuses status)
		=> status == DydxChainMarketStatuses.Active
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	private static void CopyMarket(DydxChainMarket target,
		DydxChainMarket source)
	{
		target.Status = source.Status;
		target.OraclePrice = source.OraclePrice;
		target.PriceChange24Hours = source.PriceChange24Hours;
		target.Volume24Hours = source.Volume24Hours;
		target.Trades24Hours = source.Trades24Hours;
		target.NextFundingRate = source.NextFundingRate;
		target.InitialMarginFraction = source.InitialMarginFraction;
		target.MaintenanceMarginFraction = source.MaintenanceMarginFraction;
		target.OpenInterest = source.OpenInterest;
		target.OpenInterestLowerCap = source.OpenInterestLowerCap;
		target.OpenInterestUpperCap = source.OpenInterestUpperCap;
		target.BaseOpenInterest = source.BaseOpenInterest;
		target.DefaultFundingRateOneHour = source.DefaultFundingRateOneHour;
	}

	private static void ApplyMarketUpdate(DydxChainMarket market,
		DydxChainTradingMarketUpdate update)
	{
		if (update.Status is DydxChainMarketStatuses status)
			market.Status = status;
		if (!update.InitialMarginFraction.IsEmpty())
			market.InitialMarginFraction = update.InitialMarginFraction;
		if (!update.MaintenanceMarginFraction.IsEmpty())
			market.MaintenanceMarginFraction = update.MaintenanceMarginFraction;
		if (!update.OpenInterest.IsEmpty())
			market.OpenInterest = update.OpenInterest;
		if (!update.OpenInterestLowerCap.IsEmpty())
			market.OpenInterestLowerCap = update.OpenInterestLowerCap;
		if (!update.OpenInterestUpperCap.IsEmpty())
			market.OpenInterestUpperCap = update.OpenInterestUpperCap;
		if (!update.BaseOpenInterest.IsEmpty())
			market.BaseOpenInterest = update.BaseOpenInterest;
		if (!update.DefaultFundingRateOneHour.IsEmpty())
			market.DefaultFundingRateOneHour =
				update.DefaultFundingRateOneHour;
		if (!update.PriceChange24Hours.IsEmpty())
			market.PriceChange24Hours = update.PriceChange24Hours;
		if (!update.Volume24Hours.IsEmpty())
			market.Volume24Hours = update.Volume24Hours;
		if (update.Trades24Hours is int trades)
			market.Trades24Hours = trades;
		if (!update.NextFundingRate.IsEmpty())
			market.NextFundingRate = update.NextFundingRate;
	}

	private int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(HistoryLimit).Max(1).To<int>();
		if (message.From is DateTime from && to > from.EnsureUtc())
			return ((to - from.EnsureUtc()).Ticks / timeFrame.Ticks + 1)
				.Min(HistoryLimit).Max(1).To<int>();
		return HistoryLimit.Min(100);
	}
}
