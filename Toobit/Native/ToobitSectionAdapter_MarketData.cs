namespace StockSharp.Toobit.Native;

sealed partial class ToobitSectionAdapter
{
	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Count > 0 && !securityTypes.Contains(SecurityType))
			return;

		var exchangeInfo = await RestClient.GetExchangeInfoAsync(cancellationToken);
		var symbols = _isFutures ? exchangeInfo.Contracts ?? [] : exchangeInfo.Symbols ?? [];
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in symbols)
		{
			if (symbol?.Symbol.IsEmpty() != false ||
				(!symbol.Status.IsEmpty() && !symbol.Status.EqualsIgnoreCase("TRADING")))
				continue;

			var priceFilter = symbol.Filters?.FirstOrDefault(
				static filter => filter.FilterType.EqualsIgnoreCase("PRICE_FILTER"));
			var quantityFilter = symbol.Filters?.FirstOrDefault(
				static filter => filter.FilterType.EqualsIgnoreCase("LOT_SIZE"));
			var notionalFilter = symbol.Filters?.FirstOrDefault(
				static filter => filter.FilterType.EqualsIgnoreCase("MIN_NOTIONAL"));
			var priceStep = priceFilter?.TickSize.ToDecimal()
				?? symbol.QuotePrecision.ToDecimal()
				?? symbol.QuoteAssetPrecision.ToDecimal();
			var volumeStep = quantityFilter?.StepSize.ToDecimal()
				?? symbol.BaseAssetPrecision.ToDecimal();

			var security = new SecurityMessage
			{
				SecurityId = symbol.Symbol.ToStockSharp(BoardCode),
				Name = symbol.SymbolName.IsEmpty(symbol.Symbol),
				SecurityType = SecurityType,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = priceStep,
				Decimals = priceStep?.GetCachedDecimals(),
				VolumeStep = volumeStep,
				MinVolume = quantityFilter?.MinQuantity.ToDecimal(),
				MaxVolume = quantityFilter?.MaxQuantity.ToDecimal(),
				Multiplier = _isFutures ? symbol.ContractMultiplier.ToDecimal() : null,
				UnderlyingSecurityMinVolume = notionalFilter?.MinNotional.ToDecimal(),
			}.TryFillUnderlyingId((_isFutures ? symbol.Underlying : symbol.BaseAsset)
				.IsEmpty(symbol.BaseAssetName)?.ToUpperInvariant());

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
	}

	public override async ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		var tickers = await RestClient.GetTickerAsync(_isFutures, symbol, cancellationToken);
		var ticker = tickers?.FirstOrDefault(t => t.Symbol.IsEmpty() || t.Symbol.EqualsIgnoreCase(symbol));
		if (ticker is not null)
			await SendTickerAsync(symbol, ticker, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await SubscribeLevel1Async(mdMsg.TransactionId, symbol, cancellationToken);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		var requestedDepth = mdMsg.MaxDepth ?? 100;
		var depth = await RestClient.GetDepthAsync(symbol, requestedDepth.Max(1).Min(300), cancellationToken);
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = depth.Time > 0 ? depth.Time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = mdMsg.TransactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(depth.Bids, mdMsg.MaxDepth),
			Asks = ToQuotes(depth.Asks, mdMsg.MaxDepth),
		}, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await SubscribeDepthAsync(mdMsg.TransactionId, symbol, mdMsg.MaxDepth, cancellationToken);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		var from = mdMsg.From;
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastTime = default(DateTime);
		var trades = await RestClient.GetTradesAsync(symbol, left.Min(60).To<int>(), cancellationToken);

		foreach (var trade in (trades ?? []).OrderBy(static trade => trade.Time))
		{
			var time = trade.Time.ToUtcTime();
			if (from is DateTime fromTime && time < fromTime)
				continue;
			if (time > to)
				break;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = time,
				TradeStringId = $"{trade.Time}:{trade.Price}:{trade.Quantity}",
				TradePrice = trade.Price.ToDecimal(),
				TradeVolume = trade.Quantity.ToDecimal(),
				OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);

			lastTime = time;
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly() || left <= 0)
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await SubscribeTicksAsync(mdMsg.TransactionId, symbol, lastTime, cancellationToken);
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToNative();
		var from = mdMsg.From ?? DateTime.UtcNow - TimeSpan.FromDays(1);
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var cursor = from;
		var lastOpenTime = default(DateTime);

		while (cursor <= to && left > 0)
		{
			var limit = left.Min(1000).To<int>();
			var candles = await RestClient.GetCandlesAsync(symbol, interval, cursor, to, limit, cancellationToken);
			var pageLastTime = default(DateTime);

			foreach (var candle in (candles ?? []).OrderBy(static candle => candle.OpenTime))
			{
				var openTime = candle.OpenTime.ToUtcTime();
				if (openTime < cursor || openTime > to)
					continue;

				await SendCandleAsync(symbol, timeFrame, candle, mdMsg.TransactionId, cancellationToken);
				lastOpenTime = pageLastTime = openTime;
				if (--left <= 0)
					break;
			}

			if (candles is null || candles.Length < limit || pageLastTime == default)
				break;

			var next = pageLastTime + timeFrame;
			if (next <= cursor)
				break;
			cursor = next;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly() || left <= 0)
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await SubscribeCandlesAsync(mdMsg.TransactionId, symbol, timeFrame, lastOpenTime, cancellationToken);
	}

	private async ValueTask SubscribeLevel1Async(long transactionId, string symbol,
		CancellationToken cancellationToken)
	{
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions[transactionId] = symbol;
			subscribe = AddReference(_level1References, symbol);
		}
		if (subscribe)
			await WsClient.SubscribeTickerAsync(symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId, CancellationToken cancellationToken)
	{
		string symbol = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out symbol))
				unsubscribe = ReleaseReference(_level1References, symbol);
		}
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTickerAsync(symbol, cancellationToken);
	}

	private async ValueTask SubscribeDepthAsync(long transactionId, string symbol, int? maxDepth,
		CancellationToken cancellationToken)
	{
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions[transactionId] = new() { Symbol = symbol, MaxDepth = maxDepth };
			subscribe = AddReference(_depthReferences, symbol);
		}
		if (subscribe)
			await WsClient.SubscribeDepthAsync(symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId, CancellationToken cancellationToken)
	{
		string symbol = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out var subscription))
			{
				symbol = subscription.Symbol;
				unsubscribe = ReleaseReference(_depthReferences, symbol);
			}
		}
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeDepthAsync(symbol, cancellationToken);
	}

	private async ValueTask SubscribeTicksAsync(long transactionId, string symbol, DateTime lastTime,
		CancellationToken cancellationToken)
	{
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions[transactionId] = new()
			{
				Symbol = symbol,
				TransactionId = transactionId,
				LastTime = lastTime,
			};
			subscribe = AddReference(_tickReferences, symbol);
		}
		if (subscribe)
			await WsClient.SubscribeTradesAsync(symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId, CancellationToken cancellationToken)
	{
		string symbol = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
			{
				symbol = subscription.Symbol;
				unsubscribe = ReleaseReference(_tickReferences, symbol);
			}
		}
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(symbol, cancellationToken);
	}

	private async ValueTask SubscribeCandlesAsync(long transactionId, string symbol, TimeSpan timeFrame,
		DateTime lastOpenTime, CancellationToken cancellationToken)
	{
		var key = (symbol, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions[transactionId] = new()
			{
				Symbol = symbol,
				TransactionId = transactionId,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
			};
			subscribe = AddReference(_candleReferences, key);
		}
		if (subscribe)
			await WsClient.SubscribeCandlesAsync(symbol, timeFrame, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId, CancellationToken cancellationToken)
	{
		(string Symbol, TimeSpan TimeFrame)? key = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
			{
				key = (subscription.Symbol, subscription.TimeFrame);
				unsubscribe = ReleaseReference(_candleReferences, key.Value);
			}
		}
		if (unsubscribe && key is { } value && _wsClient is not null)
			await _wsClient.UnsubscribeCandlesAsync(value.Symbol, value.TimeFrame, cancellationToken);
	}

	private async ValueTask OnTickerAsync(ToobitWsEnvelope<ToobitWsTicker[]> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var ticker in envelope.Data ?? [])
		{
			var symbol = ticker.Symbol.IsEmpty(envelope.Symbol);
			long[] subscriptions;
			using (_sync.EnterScope())
			{
				subscriptions = [.. _level1Subscriptions
					.Where(pair => pair.Value.EqualsIgnoreCase(symbol))
					.Select(static pair => pair.Key)];
			}

			foreach (var transactionId in subscriptions)
				await SendTickerAsync(symbol, ticker, transactionId, cancellationToken);
		}
	}

	private async ValueTask OnDepthAsync(ToobitWsEnvelope<ToobitWsDepth[]> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var depth in envelope.Data ?? [])
		{
			var symbol = depth.Symbol.IsEmpty(envelope.Symbol);
			(long TransactionId, int? MaxDepth)[] subscriptions;
			using (_sync.EnterScope())
			{
				subscriptions = [.. _depthSubscriptions
					.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(symbol))
					.Select(static pair => (pair.Key, pair.Value.MaxDepth))];
			}

			foreach (var subscription in subscriptions)
			{
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					SecurityId = symbol.ToStockSharp(BoardCode),
					ServerTime = depth.Time > 0 ? depth.Time.ToUtcTime() : CurrentTime,
					OriginalTransactionId = subscription.TransactionId,
					State = QuoteChangeStates.SnapshotComplete,
					Bids = ToQuotes(depth.Bids, subscription.MaxDepth),
					Asks = ToQuotes(depth.Asks, subscription.MaxDepth),
					SeqNum = ToobitExtensions.ParseVersion(depth.Version),
				}, cancellationToken);
			}
		}
	}

	private async ValueTask OnTradeAsync(ToobitWsEnvelope<ToobitWsTrade[]> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var trade in (envelope.Data ?? []).OrderBy(static trade => trade.Time))
		{
			var symbol = envelope.Symbol;
			var time = trade.Time.ToUtcTime();
			long[] subscriptions;
			using (_sync.EnterScope())
			{
				var accepted = new List<long>();
				foreach (var pair in _tickSubscriptions)
				{
					var state = pair.Value;
					if (!state.Symbol.EqualsIgnoreCase(symbol) || !IsNewTrade(state, trade, time))
						continue;

					state.LastTime = time;
					state.LastTradeId = trade.TradeId;
					accepted.Add(pair.Key);
				}
				subscriptions = [.. accepted];
			}

			foreach (var transactionId in subscriptions)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					SecurityId = symbol.ToStockSharp(BoardCode),
					ServerTime = time,
					TradeId = trade.TradeId.ToLongId(),
					TradeStringId = trade.TradeId,
					TradePrice = trade.Price.ToDecimal(),
					TradeVolume = trade.Quantity.ToDecimal(),
					OriginSide = trade.IsBuy ? Sides.Buy : Sides.Sell,
					OriginalTransactionId = transactionId,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask OnCandleAsync(ToobitWsEnvelope<ToobitWsCandle[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope.Parameters?.KlineType.IsEmpty() != false)
			return;

		TimeSpan timeFrame;
		try
		{
			timeFrame = envelope.Parameters.KlineType.ToTimeFrame();
		}
		catch (ArgumentOutOfRangeException)
		{
			return;
		}

		foreach (var candle in envelope.Data ?? [])
		{
			var symbol = candle.Symbol.IsEmpty(envelope.Symbol);
			var openTime = candle.OpenTime.ToUtcTime();
			long[] subscriptions;
			using (_sync.EnterScope())
			{
				var accepted = new List<long>();
				foreach (var pair in _candleSubscriptions)
				{
					var state = pair.Value;
					if (!state.Symbol.EqualsIgnoreCase(symbol) || state.TimeFrame != timeFrame ||
						(state.LastOpenTime != default && openTime < state.LastOpenTime))
						continue;

					state.LastOpenTime = openTime;
					accepted.Add(pair.Key);
				}
				subscriptions = [.. accepted];
			}

			foreach (var transactionId in subscriptions)
			{
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					SecurityId = symbol.ToStockSharp(BoardCode),
					TypedArg = timeFrame,
					OpenTime = openTime,
					CloseTime = openTime + timeFrame,
					OpenPrice = candle.Open.ToDecimal() ?? 0m,
					HighPrice = candle.High.ToDecimal() ?? 0m,
					LowPrice = candle.Low.ToDecimal() ?? 0m,
					ClosePrice = candle.Close.ToDecimal() ?? 0m,
					TotalVolume = candle.Volume.ToDecimal() ?? 0m,
					State = openTime + timeFrame <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
					OriginalTransactionId = transactionId,
				}, cancellationToken);
			}
		}
	}

	private ValueTask SendTickerAsync(string symbol, ToobitTicker ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		var last = ticker.LastPrice.ToDecimal();
		var open = ticker.OpenPrice.ToDecimal();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = ticker.Time > 0 ? ticker.Time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, open)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice.ToDecimal())
		.TryAdd(Level1Fields.Change, ticker.PriceChange.ToDecimal()
			?? (last is decimal lastPrice && open is decimal openPrice ? lastPrice - openPrice : null)),
		cancellationToken);
	}

	private ValueTask SendTickerAsync(string symbol, ToobitWsTicker ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		var last = ticker.LastPrice.ToDecimal();
		var open = ticker.OpenPrice.ToDecimal();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = ticker.Time > 0 ? ticker.Time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, open)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
		.TryAdd(Level1Fields.Change,
			last is decimal lastPrice && open is decimal openPrice ? lastPrice - openPrice : null),
		cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, TimeSpan timeFrame, ToobitCandle candle,
		long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.OpenTime.ToUtcTime();
		var closeTime = candle.CloseTime > 0 ? candle.CloseTime.ToUtcTime() : openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			TypedArg = timeFrame,
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open.ToDecimal() ?? 0m,
			HighPrice = candle.High.ToDecimal() ?? 0m,
			LowPrice = candle.Low.ToDecimal() ?? 0m,
			ClosePrice = candle.Close.ToDecimal() ?? 0m,
			TotalVolume = candle.Volume.ToDecimal() ?? 0m,
			State = closeTime <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static bool IsNewTrade(TickSubscription state, ToobitWsTrade trade, DateTime time)
	{
		var currentId = trade.TradeId.ToLongId();
		var previousId = state.LastTradeId.ToLongId();
		if (currentId is long current && previousId is long previous)
			return current > previous;
		return time > state.LastTime;
	}

	private static QuoteChange[] ToQuotes(string[][] entries, int? maxDepth)
	{
		if (entries is null || entries.Length == 0)
			return [];

		var limit = (maxDepth ?? entries.Length).Max(1).Min(entries.Length);
		var result = new List<QuoteChange>(limit);
		foreach (var entry in entries.Take(limit))
		{
			if (entry is not { Length: >= 2 })
				continue;
			var price = entry[0].ToDecimal();
			var volume = entry[1].ToDecimal();
			if (price is decimal p && volume is decimal v)
				result.Add(new QuoteChange(p, v));
		}
		return [.. result];
	}
}
