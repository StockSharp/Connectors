namespace StockSharp.Coincall;

public partial class CoincallMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requested = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in GetInstruments().OrderBy(
			static value => value.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					ProductType.ToBoardCode()))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(instrument.Symbol))
				continue;
			var security = CreateSecurity(
				instrument, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime = CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}.TryAdd(
					Level1Fields.State,
					instrument.IsActive
						? SecurityStates.Trading
						: SecurityStates.Stoped),
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			MarketSubscription subscription;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			var key = new StreamKey(
				"ticker", subscription.Symbol, null);
			if (ReleaseReference(_streamReferences, key))
				await WsClient.UnsubscribeTickerAsync(
					subscription.Symbol, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Coincall does not expose historical Level1 events.");

		var instrument = GetInstrument(mdMsg.SecurityId);
		var ticker = await RestClient.GetTickerAsync(
			instrument.Symbol, cancellationToken) ?? instrument;
		await SendLevel1Async(
			instrument,
			ticker,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		_ = WsClient;
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = instrument.Symbol,
			};
		var streamKey = new StreamKey(
			"ticker", instrument.Symbol, null);
		var subscribe = AddReference(
			_streamReferences, streamKey);
		try
		{
			if (subscribe)
				await WsClient.SubscribeTickerAsync(
					instrument.Symbol, cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(_streamReferences, streamKey);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			DepthSubscription subscription;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			var key = new StreamKey(
				"orderBook", subscription.Symbol, null);
			if (ReleaseReference(_streamReferences, key))
				await WsClient.UnsubscribeOrderBookAsync(
					subscription.Symbol, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Coincall does not expose historical order books.");

		var instrument = GetInstrument(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100)
			.Max(1).Min(100).To<int>();
		var book = await RestClient.GetOrderBookAsync(
			instrument.Symbol, depth, cancellationToken);
		await SendBookAsync(
			instrument,
			book,
			mdMsg.TransactionId,
			depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		_ = WsClient;
		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = instrument.Symbol,
				Depth = depth,
			};
		var streamKey = new StreamKey(
			"orderBook", instrument.Symbol, null);
		var subscribe = AddReference(
			_streamReferences, streamKey);
		try
		{
			if (subscribe)
				await WsClient.SubscribeOrderBookAsync(
					instrument.Symbol, cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(_streamReferences, streamKey);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			MarketSubscription subscription;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			var key = new StreamKey(
				"lastTradeV2", subscription.Symbol, null);
			if (ReleaseReference(_streamReferences, key))
				await WsClient.UnsubscribeTradesAsync(
					subscription.Symbol, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var instrument = GetInstrument(mdMsg.SecurityId);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 100)
			.Max(1).Min(1000).To<int>();
		foreach (var trade in
			(await RestClient.GetTradesAsync(
				instrument.Symbol,
				cancellationToken) ?? [])
			.Where(trade =>
				(mdMsg.From is null ||
					trade.Time >= mdMsg.From.Value
						.ToUniversalTime()) &&
				trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddTrade(instrument.Symbol, trade.Id))
				continue;
			await SendTradeAsync(
				instrument,
				trade,
				mdMsg.TransactionId,
				cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		_ = WsClient;
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = instrument.Symbol,
			};
		var streamKey = new StreamKey(
			"lastTradeV2", instrument.Symbol, null);
		var subscribe = AddReference(
			_streamReferences, streamKey);
		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(
					instrument.Symbol, cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(_streamReferences, streamKey);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			CandleSubscription subscription;
			using (_sync.EnterScope())
			{
				if (!_candleSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			var unsubscribePeriod =
				subscription.TimeFrame.ToPeriod();
			var key = new StreamKey(
				"kline", subscription.Symbol, unsubscribePeriod);
			if (ReleaseReference(_streamReferences, key))
				await WsClient.UnsubscribeCandlesAsync(
					subscription.Symbol,
					unsubscribePeriod,
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var instrument = GetInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var period = timeFrame.ToPeriod();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = (mdMsg.From ??
			to - timeFrame * (mdMsg.Count ?? 500))
			.ToUniversalTime();
		var maximum = (mdMsg.Count ?? 1000)
			.Max(1).Min(5000).To<int>();
		foreach (var candle in
			(await RestClient.GetCandlesAsync(
				instrument.Symbol,
				timeFrame,
				from,
				to,
				cancellationToken) ?? [])
			.Where(candle =>
				candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendCandleAsync(
				instrument,
				candle,
				mdMsg.TransactionId,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		_ = WsClient;
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = instrument.Symbol,
				TimeFrame = timeFrame,
			};
		var streamKey = new StreamKey(
			"kline", instrument.Symbol, period);
		var subscribe = AddReference(
			_streamReferences, streamKey);
		try
		{
			if (subscribe)
				await WsClient.SubscribeCandlesAsync(
					instrument.Symbol,
					period,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(_streamReferences, streamKey);
			throw;
		}
	}

	private async ValueTask ProcessTickerAsync(
		CoincallInstrument ticker,
		CancellationToken cancellationToken)
	{
		var instrument = GetInstrument(ticker?.Symbol);
		if (instrument is null || ticker is null)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					instrument.Symbol))];
		foreach (var pair in subscriptions)
			await SendLevel1Async(
				instrument,
				ticker,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessBookAsync(
		CoincallBook book,
		CancellationToken cancellationToken)
	{
		var instrument = GetInstrument(book?.Symbol);
		if (instrument is null || book is null)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					instrument.Symbol))];
		foreach (var pair in subscriptions)
			await SendBookAsync(
				instrument,
				book,
				pair.Key,
				pair.Value.Depth,
				cancellationToken);
	}

	private async ValueTask ProcessTradeAsync(
		CoincallTrade trade,
		CancellationToken cancellationToken)
	{
		var instrument = GetInstrument(trade?.Symbol);
		if (instrument is null || trade is null ||
			!AddTrade(instrument.Symbol, trade.Id))
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					instrument.Symbol))];
		foreach (var pair in subscriptions)
			await SendTradeAsync(
				instrument,
				trade,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessCandleAsync(
		CoincallCandle candle,
		CancellationToken cancellationToken)
	{
		var instrument = GetInstrument(candle?.Symbol);
		if (instrument is null || candle is null)
			return;
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(
					instrument.Symbol) &&
				pair.Value.TimeFrame == candle.TimeFrame)];
		foreach (var pair in subscriptions)
			await SendCandleAsync(
				instrument,
				candle,
				pair.Key,
				cancellationToken);
	}

	private static SecurityMessage CreateSecurity(
		CoincallInstrument instrument,
		long originalTransactionId)
	{
		var security = new SecurityMessage
		{
			SecurityId = instrument.ToStockSharp(),
			Name = instrument.DisplayName.IsEmpty()
				? instrument.Symbol
				: instrument.DisplayName,
			ShortName = instrument.Symbol,
			SecurityType = instrument.SecurityType,
			Currency = Enum.TryParse<CurrencyTypes>(
				instrument.QuoteCurrency,
				true,
				out var currency)
					? currency
					: null,
			PriceStep = instrument.PriceStep > 0
				? instrument.PriceStep
				: null,
			VolumeStep = instrument.VolumeStep > 0
				? instrument.VolumeStep
				: null,
			MinVolume = instrument.MinVolume,
			ExpiryDate = instrument.Expiry,
			Strike = instrument.Strike,
			OptionType = instrument.OptionType,
			OriginalTransactionId = originalTransactionId,
		};
		if (!instrument.BaseCurrency.IsEmpty())
			security.TryFillUnderlyingId(
				instrument.BaseCurrency.ToUpperInvariant());
		return security;
	}

	private ValueTask SendLevel1Async(
		CoincallInstrument instrument,
		CoincallInstrument ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = instrument.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
			.TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice)
			.TryAdd(Level1Fields.Index, ticker.IndexPrice)
			.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid)
			.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(
				Level1Fields.OpenInterest, ticker.OpenInterest)
			.TryAdd(
				Level1Fields.State,
				instrument.IsActive
					? SecurityStates.Trading
					: SecurityStates.Stoped),
			cancellationToken);

	private ValueTask SendBookAsync(
		CoincallInstrument instrument,
		CoincallBook book,
		long originalTransactionId,
		int depth,
		CancellationToken cancellationToken)
	{
		if (book is null)
			return default;
		return SendOutMessageAsync(
			new QuoteChangeMessage
			{
				SecurityId = instrument.ToStockSharp(),
				ServerTime = book.Time == default
					? CurrentTime
					: book.Time,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = [.. book.Bids.Take(depth).Select(
					static quote => new QuoteChange(
						quote.Price, quote.Volume))],
				Asks = [.. book.Asks.Take(depth).Select(
					static quote => new QuoteChange(
						quote.Price, quote.Volume))],
			},
			cancellationToken);
	}

	private ValueTask SendTradeAsync(
		CoincallInstrument instrument,
		CoincallTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = instrument.ToStockSharp(),
				ServerTime = trade.Time,
				OriginalTransactionId =
					originalTransactionId,
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume.Abs(),
				OriginSide = trade.Side,
			},
			cancellationToken);

	private ValueTask SendCandleAsync(
		CoincallInstrument instrument,
		CoincallCandle candle,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var closeTime = candle.OpenTime + candle.TimeFrame;
		return SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = instrument.ToStockSharp(),
				TypedArg = candle.TimeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = closeTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = closeTime <= DateTime.UtcNow
					? CandleStates.Finished
					: CandleStates.Active,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
