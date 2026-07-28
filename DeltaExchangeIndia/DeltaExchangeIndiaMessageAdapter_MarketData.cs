namespace StockSharp.DeltaExchangeIndia;

public partial class DeltaExchangeIndiaMessageAdapter
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
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var product in GetProducts()
			.OrderBy(static product => product.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.DeltaExchangeIndia))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.EqualsIgnoreCase(
					product.Symbol))
				continue;
			var security = CreateSecurity(
				product, lookupMsg.TransactionId);
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
				}
				.TryAdd(Level1Fields.State,
					product.IsActive
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
			if (ReleaseReference("ticker", subscription.Symbol))
				await PublicWsClient.UnsubscribeAsync(
					"ticker",
					subscription.Symbol,
					cancellationToken);
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
				"Delta Exchange India does not expose " +
					"historical Level1 events.");
		var product = GetProduct(mdMsg.SecurityId);
		var ticker = await RestClient.GetTickerAsync(
			product.Symbol, cancellationToken);
		await SendLevel1Async(
			product,
			ticker,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = product.Symbol,
			};
		var subscribe = AddReference("ticker", product.Symbol);
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeAsync(
					"ticker",
					product.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference("ticker", product.Symbol);
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
			if (ReleaseReference("ob_l2", subscription.Symbol))
				await PublicWsClient.UnsubscribeAsync(
					"ob_l2",
					subscription.Symbol,
					cancellationToken);
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
				"Delta Exchange India does not expose " +
					"historical order books.");
		var product = GetProduct(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 15)
			.Max(1).Min(15).To<int>();
		var book = await RestClient.GetOrderBookAsync(
			product.Symbol, depth, cancellationToken);
		await SendBookAsync(
			product,
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
		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = product.Symbol,
				Depth = depth,
			};
		var subscribe = AddReference("ob_l2", product.Symbol);
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeAsync(
					"ob_l2",
					product.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference("ob_l2", product.Symbol);
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
			if (ReleaseReference("trades", subscription.Symbol))
				await PublicWsClient.UnsubscribeAsync(
					"trades",
					subscription.Symbol,
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		var product = GetProduct(mdMsg.SecurityId);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 50).Max(1).Min(50).To<int>();
		foreach (var trade in
			(await RestClient.GetTradesAsync(
				product.Symbol, cancellationToken) ?? [])
			.Where(trade =>
				(mdMsg.From is null ||
					trade.Time >= mdMsg.From.Value.ToUniversalTime()) &&
				trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddTrade(product.Symbol, trade.Id))
				continue;
			await SendTradeAsync(
				product,
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
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = product.Symbol,
			};
		var subscribe = AddReference("trades", product.Symbol);
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeAsync(
					"trades",
					product.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference("trades", product.Symbol);
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
		var timeFrame = mdMsg.GetTimeFrame();
		var channel = "candlestick_" +
			DeltaExchangeIndiaExtensions.ToResolution(timeFrame);
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
			var unsubscribeChannel = "candlestick_" +
				DeltaExchangeIndiaExtensions.ToResolution(
					subscription.TimeFrame);
			if (ReleaseReference(
				unsubscribeChannel, subscription.Symbol))
				await PublicWsClient.UnsubscribeAsync(
					unsubscribeChannel,
					subscription.Symbol,
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		var product = GetProduct(mdMsg.SecurityId);
		var maximum = (mdMsg.Count ?? 1999)
			.Max(1).Min(1999).To<int>();
		foreach (var candle in
			(await RestClient.GetCandlesAsync(
				product.Symbol,
				timeFrame,
				mdMsg.From?.ToUniversalTime(),
				mdMsg.To?.ToUniversalTime(),
				cancellationToken) ?? [])
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendCandleAsync(
				product,
				candle,
				mdMsg.TransactionId,
				cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = product.Symbol,
				TimeFrame = timeFrame,
			};
		var subscribe = AddReference(channel, product.Symbol);
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeAsync(
					channel,
					product.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseReference(channel, product.Symbol);
			throw;
		}
	}

	private async ValueTask ProcessTickerAsync(
		DeltaTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.Symbol.IsEmpty() != false)
			return;
		var product = GetProduct(ticker.Symbol);
		if (product is null)
			return;
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values
				.Where(subscription =>
					subscription.Symbol.EqualsIgnoreCase(
						ticker.Symbol))];
		foreach (var subscription in subscriptions)
			await SendLevel1Async(
				product,
				ticker,
				FindTransactionId(
					_level1Subscriptions, subscription),
				cancellationToken);
	}

	private async ValueTask ProcessBookAsync(
		DeltaBook book,
		CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;
		var product = GetProduct(book.Symbol);
		if (product is null)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(
				pair => pair.Value.Symbol.EqualsIgnoreCase(
					book.Symbol))];
		foreach (var (transactionId, subscription) in subscriptions)
			await SendBookAsync(
				product,
				book,
				transactionId,
				subscription.Depth,
				cancellationToken);
	}

	private async ValueTask ProcessTradeAsync(
		DeltaTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false ||
			!AddTrade(trade.Symbol, trade.Id))
			return;
		var product = GetProduct(trade.Symbol);
		if (product is null)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(
				pair => pair.Value.Symbol.EqualsIgnoreCase(
					trade.Symbol))];
		foreach (var (transactionId, _) in subscriptions)
			await SendTradeAsync(
				product,
				trade,
				transactionId,
				cancellationToken);
	}

	private async ValueTask ProcessCandleAsync(
		DeltaCandle candle,
		CancellationToken cancellationToken)
	{
		if (candle?.Symbol.IsEmpty() != false)
			return;
		var product = GetProduct(candle.Symbol);
		if (product is null)
			return;
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(
				pair =>
					pair.Value.Symbol.EqualsIgnoreCase(
						candle.Symbol) &&
					pair.Value.TimeFrame == candle.TimeFrame)];
		foreach (var (transactionId, _) in subscriptions)
			await SendCandleAsync(
				product,
				candle,
				transactionId,
				cancellationToken);
	}

	private ValueTask SendLevel1Async(
		DeltaProduct product,
		DeltaTicker ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = product.ToStockSharp(),
				ServerTime = ticker.Time == default
					? CurrentTime
					: ticker.Time,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(Level1Fields.OpenPrice, ticker.Open)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
			.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid)
			.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk)
			.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
			.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest)
			.TryAdd(Level1Fields.State,
				product.IsActive
					? SecurityStates.Trading
					: SecurityStates.Stoped),
			cancellationToken);
	}

	private ValueTask SendBookAsync(
		DeltaProduct product,
		DeltaBook book,
		long originalTransactionId,
		int maximumDepth,
		CancellationToken cancellationToken)
	{
		if (book is null)
			return default;
		return SendOutMessageAsync(
			new QuoteChangeMessage
			{
				SecurityId = product.ToStockSharp(),
				ServerTime = book.Time == default
					? CurrentTime
					: book.Time,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = [.. book.Bids
					.Take(maximumDepth)
					.Select(static quote =>
						new QuoteChange(
							quote.Price, quote.Volume))],
				Asks = [.. book.Asks
					.Take(maximumDepth)
					.Select(static quote =>
						new QuoteChange(
							quote.Price, quote.Volume))],
			},
			cancellationToken);
	}

	private ValueTask SendTradeAsync(
		DeltaProduct product,
		DeltaTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = product.ToStockSharp(),
				ServerTime = trade.Time == default
					? CurrentTime
					: trade.Time,
				OriginalTransactionId =
					originalTransactionId,
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume.Abs(),
				OriginSide = trade.Side,
			},
			cancellationToken);

	private ValueTask SendCandleAsync(
		DeltaProduct product,
		DeltaCandle candle,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var closeTime = candle.OpenTime + candle.TimeFrame;
		return SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = product.ToStockSharp(),
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

	private static SecurityMessage CreateSecurity(
		DeltaProduct product,
		long originalTransactionId)
	{
		var security = new SecurityMessage
		{
			SecurityId = product.ToStockSharp(),
			Name = product.Description.IsEmpty()
				? product.Symbol
				: product.Description,
			ShortName = product.Symbol,
			Class = product.ContractType?.ToUpperInvariant(),
			SecurityType = product.SecurityType,
			Currency = Enum.TryParse<CurrencyTypes>(
				product.QuotingAsset ??
					product.SettlingAsset,
				true,
				out var currency)
					? currency
					: null,
			PriceStep = product.PriceStep > 0
				? product.PriceStep
				: null,
			VolumeStep = 1,
			MinVolume = 1,
			Multiplier = product.ContractValue > 0
				? product.ContractValue
				: null,
			ExpiryDate = product.Expiry,
			Strike = product.Strike,
			OptionType = product.OptionType,
			OriginalTransactionId = originalTransactionId,
		};
		if (!product.UnderlyingAsset.IsEmpty())
			security.TryFillUnderlyingId(
				product.UnderlyingAsset.ToUpperInvariant());
		return security;
	}

	private long FindTransactionId<T>(
		Dictionary<long, T> source,
		T subscription)
		where T : class
	{
		using (_sync.EnterScope())
			return source.FirstOrDefault(pair =>
				ReferenceEquals(pair.Value, subscription)).Key;
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
