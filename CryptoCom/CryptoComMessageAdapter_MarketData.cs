namespace StockSharp.CryptoCom;

public partial class CryptoComMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();

		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in await RestClient.GetInstrumentsAsync(cancellationToken))
		{
			if (instrument?.Symbol.IsEmpty() != false || !instrument.IsTradable)
				continue;

			CryptoComSections section;
			SecurityTypes securityType;
			if (instrument.InstrumentType.EqualsIgnoreCase("CCY_PAIR"))
			{
				section = CryptoComSections.Spot;
				securityType = SecurityTypes.CryptoCurrency;
			}
			else if (instrument.InstrumentType.EqualsIgnoreCase("PERPETUAL_SWAP") ||
				instrument.InstrumentType.EqualsIgnoreCase("FUTURE"))
			{
				section = CryptoComSections.Derivatives;
				securityType = SecurityTypes.Future;
			}
			else
				continue;

			if (!IsSectionEnabled(section) || (securityTypes.Count > 0 && !securityTypes.Contains(securityType)))
				continue;

			var boardCode = section == CryptoComSections.Spot
				? BoardCodes.CryptoCom
				: BoardCodes.CryptoComDerivatives;
			var priceStep = instrument.PriceTickSize.ToDecimal();
			var security = new SecurityMessage
			{
				SecurityId = instrument.Symbol.ToStockSharp(boardCode),
				Name = instrument.DisplayName.IsEmpty(instrument.Symbol),
				SecurityType = securityType,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = priceStep,
				Decimals = instrument.QuoteDecimals ?? priceStep?.GetCachedDecimals(),
				VolumeStep = instrument.QuantityTickSize.ToDecimal(),
				Multiplier = section == CryptoComSections.Derivatives
					? instrument.ContractSize.ToDecimal()
					: null,
				ExpiryDate = instrument.InstrumentType.EqualsIgnoreCase("FUTURE") && instrument.ExpiryTimestamp > 0
					? instrument.ExpiryTimestamp.Value.FromUnix(false)
					: null,
			}.TryFillUnderlyingId(instrument.BaseCurrency?.ToUpperInvariant());

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
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

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var ticker = (await RestClient.GetTickersAsync(symbol, cancellationToken)).FirstOrDefault();
		if (ticker is not null)
			await SendTickerAsync(ticker, symbol, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var channel = $"ticker.{symbol}";
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, symbol);
			subscribe = AddReference(_channelReferences, channel);
		}

		if (subscribe)
			await MarketWsClient.SubscribeAsync(channel, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();

		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		var book = await RestClient.GetBookAsync(symbol, depth, cancellationToken);
		await SendBookAsync(symbol, book.Bids, book.Asks, book.Time, null,
			QuoteChangeStates.SnapshotComplete, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var channel = $"book.{symbol}.{depth}";
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Channel = channel,
				Depth = depth,
			});
			subscribe = AddReference(_channelReferences, channel);
		}

		if (subscribe)
			await MarketWsClient.SubscribeAsync(channel, cancellationToken);
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

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var trades = await LoadTradesAsync(symbol, mdMsg.From, mdMsg.To ?? DateTime.UtcNow,
			mdMsg.Count, cancellationToken);
		string lastTradeId = null;
		var lastTime = mdMsg.From ?? default;
		foreach (var trade in trades)
		{
			await SendPublicTradeAsync(trade, symbol, mdMsg.TransactionId, cancellationToken);
			lastTradeId = trade.TradeId;
			lastTime = trade.Time.FromUnix(false);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var channel = $"trade.{symbol}";
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				LastTradeId = lastTradeId,
				LastTime = lastTime,
			});
			subscribe = AddReference(_channelReferences, channel);
		}

		if (subscribe)
			await MarketWsClient.SubscribeAsync(channel, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();

		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var timeFrame = mdMsg.GetTimeFrame();
		var candles = await LoadCandlesAsync(symbol, timeFrame, mdMsg.From,
			mdMsg.To ?? DateTime.UtcNow, mdMsg.Count, cancellationToken);
		var lastOpenTime = mdMsg.From ?? default;
		foreach (var candle in candles)
		{
			await SendCandleAsync(candle, symbol, timeFrame, mdMsg.TransactionId, cancellationToken);
			lastOpenTime = candle.OpenTime.FromUnix(false);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var channel = $"candlestick.{timeFrame.ToNative()}.{symbol}";
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
			});
			subscribe = AddReference(_channelReferences, channel);
		}

		if (subscribe)
			await MarketWsClient.SubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId, CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out var symbol))
			{
				var current = $"ticker.{symbol}";
				if (ReleaseReference(_channelReferences, current))
					channel = current;
			}
		}
		if (channel is not null)
			await MarketWsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId, CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out var subscription) &&
				ReleaseReference(_channelReferences, subscription.Channel))
				channel = subscription.Channel;
		}
		if (channel is not null)
			await MarketWsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId, CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out var subscription))
			{
				var current = $"trade.{subscription.Symbol}";
				if (ReleaseReference(_channelReferences, current))
					channel = current;
			}
		}
		if (channel is not null)
			await MarketWsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId, CancellationToken cancellationToken)
	{
		string channel = null;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out var subscription))
			{
				var current = $"candlestick.{subscription.TimeFrame.ToNative()}.{subscription.Symbol}";
				if (ReleaseReference(_channelReferences, current))
					channel = current;
			}
		}
		if (channel is not null)
			await MarketWsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask<CryptoComPublicTrade[]> LoadTradesAsync(string symbol, DateTime? from,
		DateTime to, long? requestedCount, CancellationToken cancellationToken)
	{
		var limit = requestedCount ?? long.MaxValue;
		long? start = from is { } fromTime ? ToNanoseconds(fromTime) : null;
		long? cursor = ToNanoseconds(to) + 1;
		var trades = new List<CryptoComPublicTrade>();
		var ids = new HashSet<string>(StringComparer.Ordinal);

		do
		{
			var batch = await RestClient.GetTradesAsync(symbol, (int)Math.Min(150, limit - trades.Count),
				start, cursor, cancellationToken);
			if (batch.Length == 0)
				break;

			var minimum = cursor;
			foreach (var trade in batch)
			{
				var time = trade.Time.FromUnix(false);
				if (time < from || time > to || !ids.Add(trade.TradeId))
					continue;
				trades.Add(trade);
				var nanoseconds = trade.TimeNanoseconds ?? trade.Time * 1_000_000L;
				if (minimum is null || nanoseconds < minimum)
					minimum = nanoseconds;
			}

			if (minimum is null || minimum >= cursor || minimum <= start || trades.Count >= limit)
				break;
			cursor = minimum;
		}
		while (from is not null);

		return [.. trades.OrderBy(static trade => trade.Time)
			.ThenBy(static trade => trade.TimeNanoseconds)
			.TakeLast((int)Math.Min(int.MaxValue, limit))];
	}

	private async ValueTask<CryptoComCandle[]> LoadCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime? from, DateTime to, long? requestedCount, CancellationToken cancellationToken)
	{
		var limit = requestedCount ?? long.MaxValue;
		var start = from?.ToUnixMilliseconds();
		long? cursor = to.ToUnixMilliseconds() + 1;
		var candles = new List<CryptoComCandle>();
		var openTimes = new HashSet<long>();

		do
		{
			var batch = await RestClient.GetCandlesAsync(symbol, timeFrame.ToNative(),
				(int)Math.Min(300, limit - candles.Count), start, cursor, cancellationToken);
			if (batch.Length == 0)
				break;

			var minimum = batch.Min(static candle => candle.OpenTime);
			foreach (var candle in batch)
			{
				var openTime = candle.OpenTime.FromUnix(false);
				if (openTime < from || openTime > to || !openTimes.Add(candle.OpenTime))
					continue;
				candles.Add(candle);
			}

			if (minimum >= cursor || minimum <= start || candles.Count >= limit)
				break;
			cursor = minimum;
		}
		while (from is not null);

		return [.. candles.OrderBy(static candle => candle.OpenTime)
			.TakeLast((int)Math.Min(int.MaxValue, limit))];
	}

	private async ValueTask OnTickerAsync(CryptoComWsEnvelope<CryptoComTicker> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var ticker in envelope.Result?.Data ?? [])
		{
			var symbol = ticker.InstrumentName.IsEmpty(envelope.Result.InstrumentName);
			KeyValuePair<long, string>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _level1Subscriptions.Where(pair => pair.Value.EqualsIgnoreCase(symbol))];

			foreach (var subscription in subscriptions)
				await SendTickerAsync(ticker, symbol, subscription.Key, cancellationToken);
		}
	}

	private async ValueTask OnBookAsync(CryptoComWsEnvelope<CryptoComWsBookItem> envelope,
		CancellationToken cancellationToken)
	{
		var result = envelope.Result;
		if (result?.Subscription.IsEmpty() != false)
			return;

		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair => pair.Value.Channel.EqualsIgnoreCase(result.Subscription))];

		foreach (var item in result.Data ?? [])
		{
			if (item.Update is null)
			{
				foreach (var subscription in subscriptions)
				{
					subscription.Value.LastSequence = item.Sequence ?? 0;
					await SendBookAsync(subscription.Value.Symbol, item.Bids, item.Asks, item.Time,
						item.Sequence, QuoteChangeStates.SnapshotComplete, subscription.Key, cancellationToken);
				}
				continue;
			}

			var gap = subscriptions.Any(subscription => subscription.Value.LastSequence > 0 &&
				item.PreviousSequence != subscription.Value.LastSequence);
			if (gap)
			{
				this.AddWarningLog("Crypto.com order-book sequence gap on {0}: previous={1}, expected={2}.",
					result.Subscription, item.PreviousSequence, subscriptions.First().Value.LastSequence);
				foreach (var subscription in subscriptions)
					subscription.Value.LastSequence = 0;
				await MarketWsClient.ResubscribeAsync(result.Subscription, cancellationToken);
				return;
			}

			foreach (var subscription in subscriptions)
			{
				subscription.Value.LastSequence = item.Sequence ?? subscription.Value.LastSequence;
				await SendBookAsync(subscription.Value.Symbol, item.Update.Bids, item.Update.Asks, item.Time,
					item.Sequence, QuoteChangeStates.Increment, subscription.Key, cancellationToken);
			}
		}
	}

	private async ValueTask OnTradeAsync(CryptoComWsEnvelope<CryptoComPublicTrade> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var trade in envelope.Result?.Data ?? [])
		{
			var symbol = trade.InstrumentName.IsEmpty(envelope.Result.InstrumentName);
			KeyValuePair<long, TickSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _tickSubscriptions.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(symbol))];

			var time = trade.Time.FromUnix(false);
			foreach (var subscription in subscriptions)
			{
				var state = subscription.Value;
				if (time < state.LastTime || (time == state.LastTime && trade.TradeId == state.LastTradeId))
					continue;

				state.LastTime = time;
				state.LastTradeId = trade.TradeId;
				await SendPublicTradeAsync(trade, symbol, subscription.Key, cancellationToken);
			}
		}
	}

	private async ValueTask OnCandleAsync(CryptoComWsEnvelope<CryptoComCandle> envelope,
		CancellationToken cancellationToken)
	{
		var result = envelope.Result;
		if (result is null || !CryptoComExtensions.TimeFrames.Values.Contains(result.Interval))
			return;
		var timeFrame = CryptoComExtensions.TimeFrames.First(pair => pair.Value == result.Interval).Key;

		foreach (var candle in result.Data ?? [])
		{
			var openTime = candle.OpenTime.FromUnix(false);
			KeyValuePair<long, CandleSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _candleSubscriptions.Where(pair =>
					pair.Value.Symbol.EqualsIgnoreCase(result.InstrumentName) && pair.Value.TimeFrame == timeFrame)];

			foreach (var subscription in subscriptions)
			{
				if (openTime < subscription.Value.LastOpenTime)
					continue;
				subscription.Value.LastOpenTime = openTime;
				await SendCandleAsync(candle, subscription.Value.Symbol, timeFrame,
					subscription.Key, cancellationToken);
			}
		}
	}

	private ValueTask SendTickerAsync(CryptoComTicker ticker, string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ToSecurityId(symbol),
			ServerTime = ticker.Time > 0 ? ticker.Time.FromUnix(false) : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.High.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest.ToDecimal()), cancellationToken);

	private ValueTask SendBookAsync(string symbol, CryptoComBookLevel[] bids,
		CryptoComBookLevel[] asks, long time, long? sequence, QuoteChangeStates state,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = ToSecurityId(symbol),
			ServerTime = time > 0 ? time.FromUnix(false) : CurrentTime,
			OriginalTransactionId = transactionId,
			State = state,
			SeqNum = sequence ?? 0,
			Bids = ToQuotes(bids),
			Asks = ToQuotes(asks),
		}, cancellationToken);

	private static QuoteChange[] ToQuotes(CryptoComBookLevel[] levels)
		=> [.. (levels ?? [])
			.Where(static level => level?.Price.ToDecimal() is not null && level.Quantity.ToDecimal() is not null)
			.Select(static level => new QuoteChange(level.Price.ToDecimal().Value, level.Quantity.ToDecimal().Value)
			{
				OrdersCount = level.OrderCount.ToInt(),
			})];

	private ValueTask SendPublicTradeAsync(CryptoComPublicTrade trade, string symbol,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = ToSecurityId(symbol),
			ServerTime = trade.Time.FromUnix(false),
			TradeId = ParseLong(trade.TradeId),
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			OriginSide = trade.Side.ToStockSharp(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);

	private ValueTask SendCandleAsync(CryptoComCandle candle, string symbol, TimeSpan timeFrame,
		long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.OpenTime.FromUnix(false);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = ToSecurityId(symbol),
			TypedArg = timeFrame,
			OpenTime = openTime,
			OpenPrice = candle.Open.ToDecimal() ?? 0m,
			HighPrice = candle.High.ToDecimal() ?? 0m,
			LowPrice = candle.Low.ToDecimal() ?? 0m,
			ClosePrice = candle.Close.ToDecimal() ?? 0m,
			TotalVolume = candle.Volume.ToDecimal() ?? 0m,
			State = openTime + timeFrame <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static long ToNanoseconds(DateTime time)
		=> checked(time.ToUnixMilliseconds() * 1_000_000L);
}
