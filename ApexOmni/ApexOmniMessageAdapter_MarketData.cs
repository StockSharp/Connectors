namespace StockSharp.ApexOmni;

public partial class ApexOmniMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		ApexOmniContract[] instruments;
		using (_sync.EnterScope())
			instruments = [.. _instruments.Values];

		foreach (var instrument in instruments.OrderBy(static item =>
			item.Symbol, StringComparer.OrdinalIgnoreCase))
		{
			var securityType = instrument.Group == ApexOmniInstrumentGroups.Stock
				? SecurityTypes.Stock
				: SecurityTypes.Future;
			if (securityTypes.Count > 0 && !securityTypes.Contains(securityType))
				continue;
			var priceStep = instrument.TickSize.ParseRequiredDecimal("tick size");
			var volumeStep = instrument.StepSize.ParseRequiredDecimal("size step");
			var security = new SecurityMessage
			{
				SecurityId = instrument.ToStockSharp(),
				Name = instrument.TokenName.IsEmpty()
					? instrument.DisplayName
					: instrument.TokenName,
				SecurityType = securityType,
				Currency = instrument.SettleAssetId.ToCurrency(),
				PriceStep = priceStep,
				Decimals = priceStep.GetCachedDecimals(),
				VolumeStep = volumeStep,
				MinVolume = instrument.MinOrderSize.ToDecimal() ?? volumeStep,
				MaxVolume = instrument.MaxOrderSize.ToDecimal(),
				Multiplier = securityType == SecurityTypes.Future ? 1m : null,
				OriginalTransactionId = lookupMsg.TransactionId,
			};
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
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

		var instrument = GetInstrument(mdMsg.SecurityId);
		var tickers = await RestClient.GetTickerAsync(instrument.CrossSymbolName,
			cancellationToken);
		var ticker = tickers.FirstOrDefault(item =>
			item?.Symbol.EqualsIgnoreCase(instrument.CrossSymbolName) == true) ??
			tickers.FirstOrDefault();
		if (ticker is null)
			throw new InvalidDataException(
				$"ApeX Omni returned no ticker for '{instrument.Symbol}'.");
		await SendTickerAsync(instrument, ticker, mdMsg.TransactionId,
			ServerTime, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var topic = GetTickerTopic(instrument);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Instrument = instrument,
			});
			subscribe = AddTopicReference(topic);
		}
		try
		{
			if (subscribe)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseTopicReference(topic);
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

		var instrument = GetInstrument(mdMsg.SecurityId);
		var depth = GetRequestedDepth(mdMsg.MaxDepth);
		var book = await RestClient.GetOrderBookAsync(instrument.CrossSymbolName,
			MarketDepth, cancellationToken);
		await SendBookAsync(instrument, book, mdMsg.TransactionId, depth,
			ServerTime, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var topic = GetBookTopic(instrument);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Instrument = instrument,
				Depth = depth,
			});
			subscribe = AddTopicReference(topic);
		}
		try
		{
			if (subscribe)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseTopicReference(topic);
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

		var instrument = GetInstrument(mdMsg.SecurityId);
		var limit = GetHistoryLimit(mdMsg.Count, 100, 500);
		var trades = await RestClient.GetTradesAsync(instrument.CrossSymbolName,
			limit, cancellationToken);
		var subscription = new TickSubscription { Instrument = instrument };
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		foreach (var trade in trades
			.Where(item => item is not null)
			.OrderBy(static item => item.Time))
		{
			var time = trade.Time.ToApexOmniTime();
			if (from is DateTime fromTime && time < fromTime ||
				to is DateTime toTime && time > toTime)
				continue;
			var id = GetTradeId(trade);
			if (!subscription.TryAccept(id, time))
				continue;
			await SendTradeAsync(instrument, trade, id, mdMsg.TransactionId,
				cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var topic = GetTradeTopic(instrument);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddTopicReference(topic);
		}
		try
		{
			if (subscribe)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseTopicReference(topic);
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

		var instrument = GetInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToApexOmniInterval();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		var derivedCount = mdMsg.From is DateTime start
			? ((to - start.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Max(1)
			: 100;
		var limit = GetHistoryLimit(mdMsg.Count ?? derivedCount, 100, 200);
		var from = mdMsg.From?.ToUniversalTime() ??
			to.AddTicks(-timeFrame.Ticks * Math.Max(0, limit - 1));
		var candles = await RestClient.GetCandlesAsync(new()
		{
			Symbol = instrument.CrossSymbolName,
			Interval = interval,
			Start = from.ToUnixMilliseconds() / 1000,
			End = to.ToUnixMilliseconds() / 1000,
			Limit = limit,
		}, cancellationToken);
		var subscription = new CandleSubscription
		{
			Instrument = instrument,
			TimeFrame = timeFrame,
		};
		foreach (var candle in candles
			.Where(static item => item is not null)
			.OrderBy(static item => item.Start))
		{
			var openTime = candle.Start.ToApexOmniTime();
			subscription.LastOpenTime = openTime;
			await SendCandleAsync(instrument, candle, mdMsg.TransactionId,
				timeFrame, openTime + timeFrame <= ServerTime
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var topic = GetCandleTopic(instrument, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddTopicReference(topic);
		}
		try
		{
			if (subscribe)
				await PublicSocket.SubscribeAsync(topic, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseTopicReference(topic);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		bool unsubscribe = false;
		string topic = null;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				topic = GetTickerTopic(subscription.Instrument);
				unsubscribe = ReleaseTopicReference(topic);
			}
		if (unsubscribe)
			await PublicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		bool unsubscribe = false;
		string topic = null;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
			{
				topic = GetBookTopic(subscription.Instrument);
				unsubscribe = ReleaseTopicReference(topic);
			}
		if (unsubscribe)
			await PublicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		bool unsubscribe = false;
		string topic = null;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
			{
				topic = GetTradeTopic(subscription.Instrument);
				unsubscribe = ReleaseTopicReference(topic);
			}
		if (unsubscribe)
			await PublicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		bool unsubscribe = false;
		string topic = null;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
			{
				topic = GetCandleTopic(subscription.Instrument,
					subscription.TimeFrame);
				unsubscribe = ReleaseTopicReference(topic);
			}
		if (unsubscribe)
			await PublicSocket.UnsubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask OnTickerAsync(string topic, ApexOmniTicker ticker,
		long timestamp, CancellationToken cancellationToken)
	{
		_ = topic;
		var instrument = GetPublicInstrument(ticker?.Symbol);
		if (instrument is null)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions
				.Where(pair => ReferenceEquals(pair.Value.Instrument, instrument))
				.Select(static pair => pair.Key)];
		var time = timestamp > 0 ? timestamp.ToApexOmniTime() : ServerTime;
		foreach (var transactionId in subscriptions)
			await SendTickerAsync(instrument, ticker, transactionId, time,
				cancellationToken);
	}

	private async ValueTask OnBookAsync(string topic, ApexOmniOrderBook book,
		long timestamp, CancellationToken cancellationToken)
	{
		_ = topic;
		var instrument = GetPublicInstrument(book?.Symbol);
		if (instrument is null)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				ReferenceEquals(pair.Value.Instrument, instrument))];
		var time = timestamp > 0 ? timestamp.ToApexOmniTime() : ServerTime;
		foreach (var (transactionId, subscription) in subscriptions)
			await SendBookAsync(instrument, book, transactionId,
				subscription.Depth, time, cancellationToken);
	}

	private async ValueTask OnPublicTradeAsync(string topic,
		ApexOmniTrade trade, long timestamp,
		CancellationToken cancellationToken)
	{
		_ = timestamp;
		var publicSymbol = trade?.Symbol;
		if (publicSymbol.IsEmpty())
			publicSymbol = topic.Split('.').LastOrDefault();
		var instrument = GetPublicInstrument(publicSymbol);
		if (instrument is null || trade is null)
			return;
		var time = trade.Time.ToApexOmniTime();
		var id = GetTradeId(trade);
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			var accepted = new List<long>();
			foreach (var (transactionId, subscription) in _tickSubscriptions)
				if (ReferenceEquals(subscription.Instrument, instrument) &&
					subscription.TryAccept(id, time))
					accepted.Add(transactionId);
			subscriptions = [.. accepted];
		}
		foreach (var transactionId in subscriptions)
			await SendTradeAsync(instrument, trade, id, transactionId,
				cancellationToken);
	}

	private async ValueTask OnCandleAsync(string topic,
		ApexOmniWebSocketCandle candle, long timestamp,
		CancellationToken cancellationToken)
	{
		_ = timestamp;
		var publicSymbol = topic.Split('.').LastOrDefault();
		var instrument = GetPublicInstrument(publicSymbol);
		if (instrument is null || candle is null)
			return;
		var openTime = candle.Start.ToApexOmniTime();
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
		{
			var accepted = new List<KeyValuePair<long, CandleSubscription>>();
			foreach (var pair in _candleSubscriptions)
			{
				if (!ReferenceEquals(pair.Value.Instrument, instrument) ||
					!GetCandleTopic(instrument, pair.Value.TimeFrame)
						.EqualsIgnoreCase(topic) ||
					openTime < pair.Value.LastOpenTime)
					continue;
				pair.Value.LastOpenTime = openTime;
				accepted.Add(pair);
			}
			subscriptions = [.. accepted];
		}
		foreach (var (transactionId, subscription) in subscriptions)
			await SendCandleAsync(instrument, candle, transactionId,
				subscription.TimeFrame, candle.IsConfirmed
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken);
	}

	private ValueTask SendTickerAsync(ApexOmniContract instrument,
		ApexOmniTicker ticker, long transactionId, DateTime serverTime,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = instrument.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice24h.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice24h.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume24h.ToDecimal())
		.TryAdd(Level1Fields.Turnover, ticker.Turnover24h.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest.ToDecimal())
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.Index, ticker.IndexPrice.ToDecimal()),
			cancellationToken);

	private ValueTask SendBookAsync(ApexOmniContract instrument,
		ApexOmniOrderBook book, long transactionId, int depth,
		DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = instrument.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book?.Bids, depth),
			Asks = ToQuotes(book?.Asks, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(ApexOmniContract instrument,
		ApexOmniTrade trade, string id, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = instrument.ToStockSharp(),
			ServerTime = trade.Time.ToApexOmniTime(),
			OriginalTransactionId = transactionId,
			TradeStringId = id,
			TradePrice = trade.Price.ParseRequiredDecimal("trade price"),
			TradeVolume = trade.Size.ParseRequiredDecimal("trade size"),
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(ApexOmniContract instrument,
		ApexOmniCandle candle, long transactionId, TimeSpan timeFrame,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = instrument.ToStockSharp(),
			OpenTime = candle.Start.ToApexOmniTime(),
			CloseTime = candle.Start.ToApexOmniTime() + timeFrame,
			OpenPrice = candle.Open.ParseRequiredDecimal("candle open"),
			HighPrice = candle.High.ParseRequiredDecimal("candle high"),
			LowPrice = candle.Low.ParseRequiredDecimal("candle low"),
			ClosePrice = candle.Close.ParseRequiredDecimal("candle close"),
			TotalVolume = candle.Volume.ParseRequiredDecimal("candle volume"),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);

	private ValueTask SendCandleAsync(ApexOmniContract instrument,
		ApexOmniWebSocketCandle candle, long transactionId, TimeSpan timeFrame,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = instrument.ToStockSharp(),
			OpenTime = candle.Start.ToApexOmniTime(),
			CloseTime = candle.End > 0
				? candle.End.ToApexOmniTime()
				: candle.Start.ToApexOmniTime() + timeFrame,
			OpenPrice = candle.Open.ParseRequiredDecimal("candle open"),
			HighPrice = candle.High.ParseRequiredDecimal("candle high"),
			LowPrice = candle.Low.ParseRequiredDecimal("candle low"),
			ClosePrice = candle.Close.ParseRequiredDecimal("candle close"),
			TotalVolume = candle.Volume.ParseRequiredDecimal("candle volume"),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);

	private static QuoteChange[] ToQuotes(ApexOmniBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level => new QuoteChange(
			level.Price.ParseRequiredDecimal("book price"),
			level.Size.ParseRequiredDecimal("book size")))];

	private static string GetTradeId(ApexOmniTrade trade)
		=> trade.Id.IsEmpty()
			? $"{trade.Time}:{trade.Side}:{trade.Price}:{trade.Size}"
			: trade.Id;
}
