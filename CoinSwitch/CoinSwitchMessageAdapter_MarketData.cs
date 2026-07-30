namespace StockSharp.CoinSwitch;

public partial class CoinSwitchMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requested = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in GetMarkets().OrderBy(
			static value => value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.CoinSwitch))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.SecurityCode) &&
				!requested.EqualsIgnoreCase(market.NativeSymbol))
				continue;
			var security = CreateSecurity(
				market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToSecurityId(),
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(
				Level1Fields.State,
				market.State),
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
			mdMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(
				mdMsg.OriginalTransactionId,
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
				"CoinSwitch does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(
			market, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var pair = GetSocketPair(market);
		var key = new StreamKey(_tickerEvent, pair);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
			});
			subscribe = _wsClient is not null &&
				AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					key.EventName,
					key.Pair,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(
				mdMsg.OriginalTransactionId,
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
				"CoinSwitch does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = CoinSwitchRestClient.NormalizeDepth(
			mdMsg.MaxDepth ?? 50);
		await SendDepthSnapshotAsync(
			market, depth, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var pair = GetSocketPair(market);
		var key = new StreamKey(_depthEvent, pair);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
				Depth = depth,
			});
			subscribe = _wsClient is not null &&
				AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					key.EventName,
					key.Pair,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(
				mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? 100)
			.Min(1000).Max(1).To<int>();
		await SendTradeSnapshotAsync(
			market,
			count,
			mdMsg.From,
			mdMsg.To,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var pair = GetSocketPair(market);
		var key = new StreamKey(_tradesEvent, pair);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
			});
			subscribe = _wsClient is not null &&
				AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					key.EventName,
					key.Pair,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(
				mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToCoinSwitchInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var count = mdMsg.Count?.Min(1000).Max(1).To<int>() ??
			GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUtc() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		await SendCandleHistoryAsync(
			market,
			timeFrame,
			from,
			to,
			count,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var stream = _wsClient is not null &&
			CanStreamCandle(timeFrame);
		var pair = GetSocketPair(market, timeFrame);
		var key = new StreamKey(_candlesEvent, pair);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
				TimeFrame = timeFrame,
				Pair = pair,
				UsesWebSocket = stream,
			});
			subscribe = stream &&
				AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					key.EventName,
					key.Pair,
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(
		CoinSwitchMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToSecurityId(),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = market.SecurityType,
			Currency = market.QuoteCurrency.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = market.MinimumVolume,
			MaxVolume = market.MaximumVolume,
			ExpiryDate = market.ExpiryDate,
			OptionType = market.OptionType,
			Strike = market.Strike,
			UnderlyingSecurityId =
				market.SecurityType == SecurityTypes.Option
					? new SecurityId
					{
						SecurityCode = market.BaseCurrency,
						BoardCode = BoardCodes.CoinSwitch,
					}
					: default,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(
		CoinSwitchMarket market,
		long transactionId,
		CancellationToken cancellationToken)
	{
		switch (ProductType)
		{
			case CoinSwitchProductTypes.Spot:
				await SendSpotTickerAsync(
					market,
					await RestClient.GetSpotTickerAsync(
						market.NativeSymbol, cancellationToken),
					transactionId,
					cancellationToken);
				break;

			case CoinSwitchProductTypes.Futures:
				await SendFuturesTickerAsync(
					market,
					await RestClient.GetFuturesTickerAsync(
						market.NativeSymbol, cancellationToken),
					transactionId,
					cancellationToken);
				break;

			case CoinSwitchProductTypes.Options:
				var tickers = await RestClient.GetHftTickersAsync(
					market.NativeSymbol, cancellationToken);
				var ticker = tickers.FirstOrDefault(value =>
					value.Symbol.EqualsIgnoreCase(
						market.NativeSymbol)) ??
					tickers.FirstOrDefault();
				if (ticker is null)
					throw new InvalidDataException(
						$"CoinSwitch returned no ticker for " +
							$"'{market.NativeSymbol}'.");
				await SendHftTickerAsync(
					market,
					ticker,
					transactionId,
					cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(ProductType),
					ProductType,
					LocalizedStrings.InvalidValue);
		}
	}

	private async ValueTask SendDepthSnapshotAsync(
		CoinSwitchMarket market,
		int depth,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (ProductType == CoinSwitchProductTypes.Options)
		{
			var book = await RestClient.GetHftDepthAsync(
				market.NativeSymbol, depth, cancellationToken);
			await SendDepthAsync(
				market,
				book?.Bids,
				book?.Asks,
				book?.Timestamp ?? 0,
				transactionId,
				depth,
				cancellationToken);
			return;
		}

		var snapshot = ProductType == CoinSwitchProductTypes.Spot
			? await RestClient.GetSpotDepthAsync(
				market.NativeSymbol, depth, cancellationToken)
			: await RestClient.GetFuturesDepthAsync(
				market.NativeSymbol, depth, cancellationToken);
		await SendDepthAsync(
			market,
			snapshot?.Bids,
			snapshot?.Asks,
			snapshot?.Timestamp ?? 0,
			transactionId,
			depth,
			cancellationToken);
	}

	private async ValueTask SendTradeSnapshotAsync(
		CoinSwitchMarket market,
		int count,
		DateTime? from,
		DateTime? to,
		long transactionId,
		CancellationToken cancellationToken)
	{
		from = from?.ToUtc();
		to = to?.ToUtc();
		if (ProductType == CoinSwitchProductTypes.Options)
		{
			var trades = await RestClient.GetHftTradesAsync(
				market.NativeSymbol, count, cancellationToken);

			foreach (var trade in trades
				.Where(value =>
					(from is null ||
						value.Timestamp.FromCoinSwitchMilliseconds() >=
							from.Value) &&
					(to is null ||
						value.Timestamp.FromCoinSwitchMilliseconds() <=
							to.Value))
				.OrderBy(static value => value.Timestamp))
				await SendHftTradeAsync(
					market,
					trade,
					transactionId,
					cancellationToken);

			return;
		}

		var values = ProductType == CoinSwitchProductTypes.Spot
			? await RestClient.GetSpotTradesAsync(
				market.NativeSymbol, count, cancellationToken)
			: await RestClient.GetFuturesTradesAsync(
				market.NativeSymbol, count, cancellationToken);

		foreach (var trade in (values ?? [])
			.Where(value =>
				(from is null ||
					value.Timestamp.FromCoinSwitchMilliseconds() >=
						from.Value) &&
				(to is null ||
					value.Timestamp.FromCoinSwitchMilliseconds() <=
						to.Value))
			.OrderBy(static value => value.Timestamp))
			await SendTradeAsync(
				market,
				trade,
				transactionId,
				cancellationToken);
	}

	private async ValueTask SendCandleHistoryAsync(
		CoinSwitchMarket market,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		int count,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (ProductType == CoinSwitchProductTypes.Options)
		{
			var candles = await RestClient.GetHftCandlesAsync(
				market.NativeSymbol,
				timeFrame,
				from,
				to,
				count,
				cancellationToken);

			foreach (var candle in candles
				.Where(value =>
					value.OpenTime
						.FromCoinSwitchMilliseconds() >= from &&
					value.OpenTime
						.FromCoinSwitchMilliseconds() <= to)
				.OrderBy(static value => value.OpenTime)
				.TakeLast(count))
				await SendHftCandleAsync(
					market,
					timeFrame,
					candle,
					transactionId,
					cancellationToken);

			return;
		}

		var values = ProductType == CoinSwitchProductTypes.Spot
			? await RestClient.GetSpotCandlesAsync(
				market.NativeSymbol,
				timeFrame,
				from,
				to,
				cancellationToken)
			: await RestClient.GetFuturesCandlesAsync(
				market.NativeSymbol,
				timeFrame,
				from,
				to,
				cancellationToken);

		foreach (var candle in (values ?? [])
			.Where(value =>
				value.StartTime
					.FromCoinSwitchMilliseconds() >= from &&
				value.StartTime
					.FromCoinSwitchMilliseconds() <= to)
			.OrderBy(static value => value.StartTime)
			.TakeLast(count))
			await SendCandleAsync(
				market,
				timeFrame,
				candle,
				transactionId,
				cancellationToken);
	}

	private ValueTask SendSpotTickerAsync(
		CoinSwitchMarket market,
		CoinSwitchTicker ticker,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToSecurityId(),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromCoinSwitchMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
		.TryAdd(Level1Fields.Change, ticker.PercentageChange),
			cancellationToken);
	}

	private ValueTask SendFuturesTickerAsync(
		CoinSwitchMarket market,
		CoinSwitchFuturesTicker ticker,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToSecurityId(),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromCoinSwitchMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
		.TryAdd(Level1Fields.Change, ticker.ChangePercent)
		.TryAdd(Level1Fields.Index, ticker.IndexPrice)
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice)
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest),
			cancellationToken);
	}

	private ValueTask SendHftTickerAsync(
		CoinSwitchMarket market,
		CoinSwitchHftTicker ticker,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToSecurityId(),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Turnover, ticker.Turnover)
		.TryAdd(Level1Fields.Index, ticker.IndexPrice)
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice)
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest),
			cancellationToken);
	}

	private ValueTask SendDepthAsync(
		CoinSwitchMarket market,
		decimal[][] bids,
		decimal[][] asks,
		long timestamp,
		long transactionId,
		int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToSecurityId(),
			ServerTime = timestamp > 0
				? timestamp.FromCoinSwitchMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(bids, false, depth),
			Asks = ToQuotes(asks, true, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(
		CoinSwitchMarket market,
		CoinSwitchTrade trade,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade is null ||
			!AddPublicTrade(
				market.NativeSymbol,
				trade.TradeId,
				trade.Timestamp,
				trade.Price,
				trade.Quantity))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToSecurityId(),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromCoinSwitchMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity.Abs(),
			OriginSide = trade.OriginSide,
		}, cancellationToken);
	}

	private ValueTask SendHftTradeAsync(
		CoinSwitchMarket market,
		CoinSwitchHftTrade trade,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade is null ||
			!AddPublicTrade(
				market.NativeSymbol,
				trade.TradeId,
				trade.Timestamp,
				trade.Price,
				trade.Volume))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToSecurityId(),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromCoinSwitchMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume.Abs(),
			OriginSide = trade.Side.ToSide(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(
		CoinSwitchMarket market,
		TimeSpan timeFrame,
		CoinSwitchCandle candle,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (candle is null || candle.StartTime <= 0)
			return default;
		var openTime =
			candle.StartTime.FromCoinSwitchMilliseconds();
		var closeTime = candle.CloseTime > 0
			? candle.CloseTime.FromCoinSwitchMilliseconds()
			: openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.QuoteVolume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = candle.IsClosed == true ||
				closeTime <= CurrentTime
					? CandleStates.Finished
					: CandleStates.Active,
		}, cancellationToken);
	}

	private ValueTask SendHftCandleAsync(
		CoinSwitchMarket market,
		TimeSpan timeFrame,
		CoinSwitchHftCandle candle,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (candle is null || candle.OpenTime <= 0)
			return default;
		var openTime =
			candle.OpenTime.FromCoinSwitchMilliseconds();
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(
		long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		StreamKey key = default;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(
				transactionId, out subscription) &&
				_wsClient is not null)
			{
				var market = _marketsByNative[
					subscription.NativeSymbol];
				key = new(
					_tickerEvent, GetSocketPair(market));
				release = ReleaseReference(
					_streamReferences, key);
			}
		}
		if (release)
			await WsClient.UnsubscribeAsync(
				key.EventName, key.Pair, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(
		long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		StreamKey key = default;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(
				transactionId, out subscription) &&
				_wsClient is not null)
			{
				var market = _marketsByNative[
					subscription.NativeSymbol];
				key = new(
					_depthEvent, GetSocketPair(market));
				release = ReleaseReference(
					_streamReferences, key);
			}
		}
		if (release)
			await WsClient.UnsubscribeAsync(
				key.EventName, key.Pair, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(
		long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		StreamKey key = default;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(
				transactionId, out subscription) &&
				_wsClient is not null)
			{
				var market = _marketsByNative[
					subscription.NativeSymbol];
				key = new(
					_tradesEvent, GetSocketPair(market));
				release = ReleaseReference(
					_streamReferences, key);
			}
		}
		if (release)
			await WsClient.UnsubscribeAsync(
				key.EventName, key.Pair, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(
		long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var release = false;
		StreamKey key = default;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(
				transactionId, out subscription) &&
				subscription.UsesWebSocket)
			{
				key = new(_candlesEvent, subscription.Pair);
				release = ReleaseReference(
					_streamReferences, key);
			}
		}
		if (release)
			await WsClient.UnsubscribeAsync(
				key.EventName, key.Pair, cancellationToken);
	}

	private async ValueTask OnWebSocketMarketDataAsync(
		string eventName,
		JToken payload,
		CancellationToken cancellationToken)
	{
		payload = UnwrapSocketPayload(payload);
		if (eventName.EqualsIgnoreCase(_tickerEvent))
			await ProcessSocketTickerAsync(
				payload, cancellationToken);
		else if (eventName.EqualsIgnoreCase(_depthEvent))
			await ProcessSocketDepthAsync(
				payload, cancellationToken);
		else if (eventName.EqualsIgnoreCase(_tradesEvent))
			await ProcessSocketTradesAsync(
				payload, cancellationToken);
		else if (eventName.EqualsIgnoreCase(_candlesEvent))
			await ProcessSocketCandleAsync(
				payload, cancellationToken);
	}

	private async ValueTask ProcessSocketTickerAsync(
		JToken payload,
		CancellationToken cancellationToken)
	{
		if (ProductType == CoinSwitchProductTypes.Spot)
		{
			foreach (var ticker in ToObjects<CoinSwitchTicker>(payload))
			{
				var market = GetMarket(
					NormalizeSocketSymbol(ticker.Symbol));
				if (market is null)
					continue;
				KeyValuePair<long, MarketSubscription>[] subscriptions;
				using (_sync.EnterScope())
					subscriptions = [.. _level1Subscriptions.Where(
						pair => pair.Value.NativeSymbol
							.EqualsIgnoreCase(market.NativeSymbol))];

				foreach (var subscription in subscriptions)
					await SendSpotTickerAsync(
						market,
						ticker,
						subscription.Key,
						cancellationToken);
			}

			return;
		}

		foreach (var ticker in
			ToObjects<CoinSwitchFuturesTicker>(payload))
		{
			var market = GetMarket(
				NormalizeSocketSymbol(ticker.Symbol));
			if (market is null)
				continue;
			KeyValuePair<long, MarketSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _level1Subscriptions.Where(
					pair => pair.Value.NativeSymbol
						.EqualsIgnoreCase(market.NativeSymbol))];

			foreach (var subscription in subscriptions)
				await SendFuturesTickerAsync(
					market,
					ticker,
					subscription.Key,
					cancellationToken);
		}
	}

	private async ValueTask ProcessSocketDepthAsync(
		JToken payload,
		CancellationToken cancellationToken)
	{
		foreach (var book in ToObjects<CoinSwitchDepth>(payload))
		{
			var market = GetMarket(
				NormalizeSocketSymbol(book.Symbol));
			if (market is null)
				continue;
			KeyValuePair<long, DepthSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions.Where(
					pair => pair.Value.NativeSymbol
						.EqualsIgnoreCase(market.NativeSymbol))];

			foreach (var subscription in subscriptions)
				await SendDepthAsync(
					market,
					book.Bids,
					book.Asks,
					book.Timestamp,
					subscription.Key,
					subscription.Value.Depth,
					cancellationToken);
		}
	}

	private async ValueTask ProcessSocketTradesAsync(
		JToken payload,
		CancellationToken cancellationToken)
	{
		foreach (var trade in ToObjects<CoinSwitchTrade>(payload))
		{
			var market = GetMarket(
				NormalizeSocketSymbol(trade.Symbol));
			if (market is null)
				continue;
			KeyValuePair<long, MarketSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _tickSubscriptions.Where(
					pair => pair.Value.NativeSymbol
						.EqualsIgnoreCase(market.NativeSymbol))];

			foreach (var subscription in subscriptions)
				await SendTradeAsync(
					market,
					trade,
					subscription.Key,
					cancellationToken);
		}
	}

	private async ValueTask ProcessSocketCandleAsync(
		JToken payload,
		CancellationToken cancellationToken)
	{
		foreach (var candle in ToObjects<CoinSwitchCandle>(payload))
		{
			var market = GetMarket(
				NormalizeSocketSymbol(candle.Symbol));
			if (market is null)
				continue;
			KeyValuePair<long, CandleSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _candleSubscriptions.Where(
					pair =>
						pair.Value.UsesWebSocket &&
						pair.Value.NativeSymbol.EqualsIgnoreCase(
							market.NativeSymbol) &&
						(candle.Interval.IsEmpty() ||
							MatchesInterval(
								candle.Interval,
								pair.Value.TimeFrame)))];

			foreach (var subscription in subscriptions)
				await SendCandleAsync(
					market,
					subscription.Value.TimeFrame,
					candle,
					subscription.Key,
					cancellationToken);
		}
	}

	private async ValueTask PollMarketDataAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] level1;
		KeyValuePair<long, DepthSubscription>[] depths;
		KeyValuePair<long, MarketSubscription>[] ticks;
		KeyValuePair<long, CandleSubscription>[] candles;
		using (_sync.EnterScope())
		{
			level1 = ProductType == CoinSwitchProductTypes.Options
				? [.. _level1Subscriptions]
				: [];
			depths = ProductType == CoinSwitchProductTypes.Options
				? [.. _depthSubscriptions]
				: [];
			ticks = ProductType == CoinSwitchProductTypes.Options
				? [.. _tickSubscriptions]
				: [];
			candles = [.. _candleSubscriptions.Where(
				static pair => !pair.Value.UsesWebSocket)];
		}

		foreach (var subscription in level1)
			await SendLevel1SnapshotAsync(
				GetMarket(subscription.Value.NativeSymbol),
				subscription.Key,
				cancellationToken);

		foreach (var subscription in depths)
			await SendDepthSnapshotAsync(
				GetMarket(subscription.Value.NativeSymbol),
				subscription.Value.Depth,
				subscription.Key,
				cancellationToken);

		foreach (var subscription in ticks)
			await SendTradeSnapshotAsync(
				GetMarket(subscription.Value.NativeSymbol),
				100,
				null,
				null,
				subscription.Key,
				cancellationToken);

		foreach (var subscription in candles)
		{
			var to = DateTime.UtcNow;
			await SendCandleHistoryAsync(
				GetMarket(subscription.Value.NativeSymbol),
				subscription.Value.TimeFrame,
				to - subscription.Value.TimeFrame * 2,
				to,
				2,
				subscription.Key,
				cancellationToken);
		}
	}

	private static QuoteChange[] ToQuotes(
		decimal[][] levels,
		bool isAsk,
		int depth)
	{
		var quotes = (levels ?? [])
			.Where(static level =>
				level is { Length: >= 2 } &&
				level[0] > 0 &&
				level[1] > 0)
			.GroupBy(static level => level[0])
			.Select(static group => new QuoteChange(
				group.Key,
				group.Sum(static level => level[1])));
		return [.. (isAsk
			? quotes.OrderBy(static quote => quote.Price)
			: quotes.OrderByDescending(static quote => quote.Price))
			.Take(depth)];
	}

	private static JToken UnwrapSocketPayload(JToken payload)
	{
		while (payload is JObject value)
		{
			var next = value["data"] ??
				value["result"] ??
				value["message"];
			if (next is null || ReferenceEquals(next, payload))
				break;
			payload = next;
		}

		return payload;
	}

	private static TData[] ToObjects<TData>(JToken payload)
		where TData : class
	{
		if (payload is null ||
			payload.Type == JTokenType.Null)
			return [];
		var serializer = JsonSerializer.Create(new()
		{
			DateParseHandling = DateParseHandling.None,
			FloatParseHandling = FloatParseHandling.Decimal,
		});
		if (payload is JArray array)
			return array.ToObject<TData[]>(serializer) ?? [];
		if (payload is JObject map &&
			map.Properties().All(static property =>
				property.Value is JObject) &&
			map.Properties().FirstOrDefault()?.Value is JObject)
			return [.. map.Properties()
				.Select(property =>
					property.Value.ToObject<TData>(serializer))
				.Where(static value => value is not null)];
		var single = payload.ToObject<TData>(serializer);
		return single is null ? [] : [single];
	}

	private string NormalizeSocketSymbol(string symbol)
	{
		if (symbol.IsEmpty())
			return symbol;
		symbol = symbol.Trim().ToUpperInvariant();
		if (ProductType == CoinSwitchProductTypes.Spot)
			return symbol.Replace(',', '/');
		var separator = symbol.LastIndexOf('_');
		return separator > 0 ? symbol[..separator] : symbol;
	}

	private static bool MatchesInterval(
		string interval,
		TimeSpan timeFrame)
	{
		if (int.TryParse(
			interval,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var minutes))
			return minutes == timeFrame.ToCoinSwitchInterval();
		return false;
	}

	private static int GetCandleCount(
		MarketDataMessage message,
		TimeSpan timeFrame,
		DateTime to)
	{
		if (message.From is not DateTime from)
			return 500;
		var count = (long)Math.Ceiling(
			(to - from.ToUtc()).Ticks /
			(double)timeFrame.Ticks) + 1;
		return count.Max(1).Min(1000).To<int>();
	}

	private static async ValueTask CompleteMarketSubscriptionAsync(
		CoinSwitchMessageAdapter adapter,
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await adapter.SendSubscriptionResultAsync(
			message, cancellationToken);
		await adapter.SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}

	private ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
		=> CompleteMarketSubscriptionAsync(
			this, message, cancellationToken);
}
