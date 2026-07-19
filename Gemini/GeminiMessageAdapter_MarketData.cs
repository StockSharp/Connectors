namespace StockSharp.Gemini;

public partial class GeminiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		string[] symbols;
		using (_sync.EnterScope())
		{
			var requested = lookupMsg.SecurityId.SecurityCode;
			if (requested.IsEmpty())
				symbols = [.. _symbols.OrderBy(static value => value,
					StringComparer.OrdinalIgnoreCase)];
			else
			{
				requested = requested.Replace("_", string.Empty, StringComparison.Ordinal)
					.Replace("-", string.Empty, StringComparison.Ordinal)
					.Replace("/", string.Empty, StringComparison.Ordinal);
				symbols = [.. _symbols.Where(value => value.EqualsIgnoreCase(requested))];
			}
		}

		foreach (var symbol in symbols)
		{
			GeminiSymbolDetails details;
			try
			{
				details = await GetSymbolDetailsAsync(symbol, cancellationToken);
			}
			catch (HttpRequestException error) when (error.StatusCode ==
				HttpStatusCode.NotFound)
			{
				this.AddVerboseLog("Skip Gemini symbol {0} because its details are unavailable.",
					symbol);
				continue;
			}

			var security = new SecurityMessage
			{
				SecurityId = symbol.ToStockSharp(),
				Name = $"{details.BaseCurrency}/{details.QuoteCurrency}",
				SecurityType = details.ProductType == GeminiProductTypes.Swap
					? SecurityTypes.Future
					: SecurityTypes.CryptoCurrency,
				PriceStep = details.PriceStep,
				VolumeStep = details.VolumeStep,
				MinVolume = details.MinimumOrderSize,
				OriginalTransactionId = lookupMsg.TransactionId,
			};
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, details.Status.ToStockSharp()),
				cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(symbol, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var stream = "ticker:" + symbol;
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol });
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeTickerAsync(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(mdMsg.TransactionId, cancellationToken);
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
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		if (mdMsg.IsHistoryOnly())
		{
			var snapshot = await RestClient.GetOrderBookAsync(symbol, depth,
				cancellationToken);
			await SendRestDepthSnapshotAsync(symbol, snapshot, mdMsg.TransactionId,
				depth, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		bool subscribe;
		QuoteChange[] bids = null;
		QuoteChange[] asks = null;
		long sequence = 0;
		using (_sync.EnterScope())
		{
			if (!_depthStates.TryGetValue(symbol, out var state))
			{
				state = new() { Symbol = symbol };
				_depthStates.Add(symbol, state);
			}
			state.Subscribers.Add(mdMsg.TransactionId, depth);
			subscribe = state.Subscribers.Count == 1;
			if (state.IsSnapshotReady)
			{
				bids = CreateSnapshotQuotes(state.Bids, false, depth);
				asks = CreateSnapshotQuotes(state.Asks, true, depth);
				sequence = state.LastUpdateId;
			}
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeDepthAsync(symbol, cancellationToken);
			if (bids is not null)
				await SendDepthMessageAsync(symbol, mdMsg.TransactionId,
					QuoteChangeStates.SnapshotComplete, sequence, CurrentTime, bids, asks,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? 500).Min(500).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var isHistoryOnly = mdMsg.IsHistoryOnly();
		if (!isHistoryOnly)
		{
			EnsureRealtimeReady();
			var stream = "trade:" + symbol;
			bool subscribe;
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Add(mdMsg.TransactionId,
					new() { Symbol = symbol });
				subscribe = AddReference(_streamReferences, stream);
			}
			try
			{
				if (subscribe)
					await WsClient.SubscribeTradesAsync(symbol, cancellationToken);
			}
			catch
			{
				await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
				throw;
			}
		}

		GeminiWsTrade[] pending;
		try
		{
			var trades = await RestClient.GetTradesAsync(symbol, from?.ToMilliseconds(),
				null, count, cancellationToken);
			var selected = (trades ?? [])
			.Where(trade => !trade.IsBroken && GetTradeTime(trade) <= to &&
				(from is null || GetTradeTime(trade) >= from.Value))
			.OrderBy(GetTradeTime).TakeLast(count).ToArray();
			foreach (var trade in selected)
				await SendTradeAsync(symbol, trade, mdMsg.TransactionId,
					cancellationToken);

			if (isHistoryOnly)
			{
				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
					cancellationToken);
				return;
			}

			var seen = selected.Select(static trade => trade.TradeId).ToHashSet();
			while (true)
			{
				using (_sync.EnterScope())
				{
					if (!_tickSubscriptions.TryGetValue(mdMsg.TransactionId,
						out var subscription))
						return;
					if (subscription.Pending.Count == 0)
					{
						subscription.IsStreamReady = true;
						break;
					}
					pending = [.. subscription.Pending];
					subscription.Pending.Clear();
				}
				foreach (var trade in pending.OrderBy(static trade =>
					trade.EventTimeNanoseconds))
					if (seen.Add(trade.TradeId))
						await SendWsTradeAsync(trade, mdMsg.TransactionId,
							cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			if (!isHistoryOnly)
				await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
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
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!GeminiExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Gemini does not support the {timeFrame} candle interval.");
		var details = await GetSymbolDetailsAsync(symbol, cancellationToken);
		var isDerivative = details.ProductType == GeminiProductTypes.Swap;
		if (isDerivative && timeFrame != TimeSpan.FromMinutes(1))
			throw new NotSupportedException(
				"Gemini derivative candle history supports the one-minute interval only.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = mdMsg.From?.ToUniversalTime();
		var count = GetCandleCount(mdMsg);
		var isHistoryOnly = mdMsg.IsHistoryOnly();
		if (!isHistoryOnly)
		{
			EnsureRealtimeReady();
			var stream = "trade:" + symbol;
			bool subscribe;
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Add(mdMsg.TransactionId, new()
				{
					Symbol = symbol,
					TimeFrame = timeFrame,
				});
				subscribe = AddReference(_streamReferences, stream);
			}
			try
			{
				if (subscribe)
					await WsClient.SubscribeTradesAsync(symbol, cancellationToken);
			}
			catch
			{
				await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
				throw;
			}
		}

		try
		{
			var candles = await RestClient.GetCandlesAsync(symbol,
				timeFrame.ToGeminiInterval(), isDerivative, cancellationToken);
			var receivedAt = DateTime.UtcNow;
			var selected = (candles ?? [])
			.Where(candle =>
			{
				var time = candle.TimestampMilliseconds.FromMilliseconds();
				return time <= to && (from is null || time >= from.Value);
			})
			.OrderBy(static candle => candle.TimestampMilliseconds).TakeLast(count)
			.ToArray();
			foreach (var candle in selected)
				await SendCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);

			if (isHistoryOnly)
			{
				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
					cancellationToken);
				return;
			}

			DateTime? includedThrough = null;
			using (_sync.EnterScope())
			{
				if (!_candleSubscriptions.TryGetValue(mdMsg.TransactionId,
					out var subscription))
					return;
				var latest = selected.LastOrDefault();
				if (latest is not null)
				{
					var openTime = latest.TimestampMilliseconds.FromMilliseconds();
					if (openTime + timeFrame > receivedAt)
					{
						subscription.IsInitialized = true;
						subscription.OpenTime = openTime;
						subscription.Open = latest.Open;
						subscription.High = latest.High;
						subscription.Low = latest.Low;
						subscription.Close = latest.Close;
						subscription.Volume = latest.Volume;
						includedThrough = receivedAt;
					}
				}
			}

			while (true)
			{
				TimeFrameCandleMessage[] messages;
				using (_sync.EnterScope())
				{
					if (!_candleSubscriptions.TryGetValue(mdMsg.TransactionId,
						out var subscription))
						return;
					if (subscription.Pending.Count == 0)
					{
						subscription.IsStreamReady = true;
						break;
					}
					var output = new List<TimeFrameCandleMessage>();
					foreach (var trade in subscription.Pending
						.OrderBy(static trade => trade.EventTimeNanoseconds))
					{
						var serverTime = trade.EventTimeNanoseconds.FromNanoseconds();
						if (includedThrough is null || serverTime > includedThrough.Value)
							UpdateSyntheticCandle(mdMsg.TransactionId, subscription,
								trade, serverTime, output);
					}
					subscription.Pending.Clear();
					messages = [.. output];
				}
				foreach (var message in messages)
					await SendOutMessageAsync(message, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			if (!isHistoryOnly)
				await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private async ValueTask<GeminiSymbolDetails> GetSymbolDetailsAsync(string symbol,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_details.TryGetValue(symbol, out var cached))
				return cached;
		var details = await RestClient.GetSymbolDetailsAsync(symbol, cancellationToken);
		if (details is null || details.Symbol.IsEmpty())
			throw new InvalidDataException(
				$"Gemini returned no details for '{symbol}'.");
		details.Symbol = details.Symbol.ToUpperInvariant();
		using (_sync.EnterScope())
			_details[details.Symbol] = details;
		return details;
	}

	private async ValueTask SendLevel1SnapshotAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(symbol, cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"Gemini returned no ticker data for '{symbol}'.");
		GeminiSymbolDetails details;
		using (_sync.EnterScope())
			_details.TryGetValue(symbol, out details);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.State, details?.Status.ToStockSharp()), cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					"ticker:" + subscription.Symbol);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTickerAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		string symbol = null;
		using (_sync.EnterScope())
			foreach (var pair in _depthStates.ToArray())
				if (pair.Value.Subscribers.Remove(transactionId))
				{
					if (pair.Value.Subscribers.Count == 0)
					{
						symbol = pair.Key;
						_depthStates.Remove(pair.Key);
					}
					break;
				}
		if (!symbol.IsEmpty() && _wsClient is not null)
			await _wsClient.UnsubscribeDepthAsync(symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					"trade:" + subscription.Symbol);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					"trade:" + subscription.Symbol);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask OnTickerAsync(GeminiWsBookTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Symbol.ToStockSharp(),
				ServerTime = ticker.EventTimeNanoseconds.FromNanoseconds(),
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
			.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
			.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize)
			.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
			.TryAdd(Level1Fields.LastTradeVolume, ticker.LastSize), cancellationToken);
	}

	private async ValueTask OnTradeAsync(GeminiWsTrade trade,
		CancellationToken cancellationToken)
	{
		var serverTime = trade.EventTimeNanoseconds.FromNanoseconds();
		long[] tickIds;
		TimeFrameCandleMessage[] candleMessages;
		using (_sync.EnterScope())
		{
			var readyTickIds = new List<long>();
			foreach (var pair in _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol)))
				if (pair.Value.IsStreamReady)
					readyTickIds.Add(pair.Key);
				else
					pair.Value.Pending.Add(trade);
			tickIds = [.. readyTickIds];
			var messages = new List<TimeFrameCandleMessage>();
			foreach (var pair in _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol)))
				if (pair.Value.IsStreamReady)
					UpdateSyntheticCandle(pair.Key, pair.Value, trade, serverTime,
						messages);
				else
					pair.Value.Pending.Add(trade);
			candleMessages = [.. messages];
		}

		foreach (var id in tickIds)
			await SendWsTradeAsync(trade, id, cancellationToken);
		foreach (var candle in candleMessages)
			await SendOutMessageAsync(candle, cancellationToken);
	}

	private async ValueTask OnDepthAsync(GeminiWsDepthUpdate update,
		CancellationToken cancellationToken)
	{
		await _depthProcessing.WaitAsync(cancellationToken);
		try
		{
			(long Id, int Depth)[] subscribers;
			QuoteChangeStates state;
			QuoteChange[] bids;
			QuoteChange[] asks;
			var isGap = false;
			using (_sync.EnterScope())
			{
				if (!_depthStates.TryGetValue(update.Symbol, out var book))
					return;
				if (!book.IsSnapshotReady &&
					update.FirstUpdateId != update.LastUpdateId)
					return;
				if (book.IsSnapshotReady && update.LastUpdateId <= book.LastUpdateId)
					return;
				if (book.IsSnapshotReady && update.FirstUpdateId > book.LastUpdateId)
				{
					book.IsSnapshotReady = false;
					book.LastUpdateId = 0;
					book.Bids.Clear();
					book.Asks.Clear();
					isGap = true;
					subscribers = [];
					state = default;
					bids = null;
					asks = null;
				}
				else
				{
					var isSnapshot = !book.IsSnapshotReady;
					ApplyLevels(book.Bids, update.Bids);
					ApplyLevels(book.Asks, update.Asks);
					book.LastUpdateId = update.LastUpdateId;
					book.IsSnapshotReady = true;
					subscribers = [.. book.Subscribers.Select(static pair =>
						(pair.Key, pair.Value))];
					state = isSnapshot ? QuoteChangeStates.SnapshotComplete :
						QuoteChangeStates.Increment;
					bids = isSnapshot ? null : ToIncrementQuotes(update.Bids);
					asks = isSnapshot ? null : ToIncrementQuotes(update.Asks);
				}
			}

			if (isGap)
			{
				await WsClient.ResubscribeDepthAsync(update.Symbol, cancellationToken);
				return;
			}

			foreach (var subscriber in subscribers)
			{
				var outputBids = bids;
				var outputAsks = asks;
				if (state == QuoteChangeStates.SnapshotComplete)
					using (_sync.EnterScope())
					{
						if (!_depthStates.TryGetValue(update.Symbol, out var book))
							continue;
						outputBids = CreateSnapshotQuotes(book.Bids, false,
							subscriber.Depth);
						outputAsks = CreateSnapshotQuotes(book.Asks, true,
							subscriber.Depth);
					}
				await SendDepthMessageAsync(update.Symbol, subscriber.Id, state,
					update.LastUpdateId, update.EventTimeNanoseconds.FromNanoseconds(),
					outputBids, outputAsks, cancellationToken);
			}
		}
		finally
		{
			_depthProcessing.Release();
		}
	}

	private ValueTask SendRestDepthSnapshotAsync(string symbol, GeminiOrderBook snapshot,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendDepthMessageAsync(symbol, transactionId,
			QuoteChangeStates.SnapshotComplete, 0, CurrentTime,
			[.. (snapshot?.Bids ?? []).OrderByDescending(static level => level.Price)
				.Take(depth).Select(static level =>
					new QuoteChange(level.Price, level.Amount))],
			[.. (snapshot?.Asks ?? []).OrderBy(static level => level.Price)
				.Take(depth).Select(static level =>
					new QuoteChange(level.Price, level.Amount))], cancellationToken);

	private ValueTask SendDepthMessageAsync(string symbol, long transactionId,
		QuoteChangeStates state, long sequence, DateTime serverTime, QuoteChange[] bids,
		QuoteChange[] asks, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = state,
			SeqNum = sequence,
			Bids = bids ?? [],
			Asks = asks ?? [],
		}, cancellationToken);

	private ValueTask SendTradeAsync(string symbol, GeminiPublicTrade trade,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			OriginalTransactionId = transactionId,
			TradeId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);

	private ValueTask SendWsTradeAsync(GeminiWsTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = trade.EventTimeNanoseconds.FromNanoseconds(),
			OriginalTransactionId = transactionId,
			TradeId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);

	private ValueTask SendCandleAsync(string symbol, GeminiCandle candle,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.TimestampMilliseconds.FromMilliseconds();
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime ? CandleStates.Finished : CandleStates.Active,
		}, cancellationToken);
	}

	private static void ApplyLevels(IDictionary<decimal, decimal> book,
		IEnumerable<GeminiPriceLevel> levels)
	{
		foreach (var level in levels ?? [])
			if (level.Amount == 0)
				book.Remove(level.Price);
			else
				book[level.Price] = level.Amount;
	}

	private static QuoteChange[] CreateSnapshotQuotes(
		IEnumerable<KeyValuePair<decimal, decimal>> levels, bool isAscending, int depth)
	{
		var ordered = isAscending
			? levels.OrderBy(static pair => pair.Key)
			: levels.OrderByDescending(static pair => pair.Key);
		return [.. ordered.Take(depth).Select(static pair =>
			new QuoteChange(pair.Key, pair.Value))];
	}

	private static QuoteChange[] ToIncrementQuotes(IEnumerable<GeminiPriceLevel> levels)
		=> [.. (levels ?? []).Select(static level =>
			new QuoteChange(level.Price, level.Amount))];

	private static void UpdateSyntheticCandle(long transactionId,
		CandleSubscription subscription, GeminiWsTrade trade, DateTime serverTime,
		ICollection<TimeFrameCandleMessage> messages)
	{
		var openTicks = serverTime.Ticks - serverTime.Ticks %
			subscription.TimeFrame.Ticks;
		var openTime = new DateTime(openTicks, DateTimeKind.Utc);
		if (!subscription.IsInitialized || openTime > subscription.OpenTime)
		{
			if (subscription.IsInitialized)
				messages.Add(CreateSyntheticCandleMessage(transactionId, subscription,
					CandleStates.Finished));
			subscription.IsInitialized = true;
			subscription.OpenTime = openTime;
			subscription.Open = trade.Price;
			subscription.High = trade.Price;
			subscription.Low = trade.Price;
			subscription.Close = trade.Price;
			subscription.Volume = trade.Quantity;
		}
		else if (openTime == subscription.OpenTime)
		{
			subscription.High = Math.Max(subscription.High, trade.Price);
			subscription.Low = Math.Min(subscription.Low, trade.Price);
			subscription.Close = trade.Price;
			subscription.Volume += trade.Quantity;
		}
		else
			return;
		messages.Add(CreateSyntheticCandleMessage(transactionId, subscription,
			CandleStates.Active));
	}

	private static TimeFrameCandleMessage CreateSyntheticCandleMessage(long transactionId,
		CandleSubscription subscription, CandleStates state)
		=> new()
		{
			SecurityId = subscription.Symbol.ToStockSharp(),
			OpenTime = subscription.OpenTime,
			CloseTime = subscription.OpenTime + subscription.TimeFrame,
			OpenPrice = subscription.Open,
			HighPrice = subscription.High,
			LowPrice = subscription.Low,
			ClosePrice = subscription.Close,
			TotalVolume = subscription.Volume,
			TypedArg = subscription.TimeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		};

	private DateTime GetTradeTime(GeminiPublicTrade trade)
		=> trade.TimestampMilliseconds > 0
			? trade.TimestampMilliseconds.FromMilliseconds()
			: CurrentTime;

	private static int GetCandleCount(MarketDataMessage message)
		=> (message.Count ?? 1500).Min(1500).Max(1).To<int>();
}
