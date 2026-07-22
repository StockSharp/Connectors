namespace StockSharp.CoinMetrics;

public partial class CoinMetricsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		if (!message.SecurityId.BoardCode.IsEmpty() &&
			!message.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinMetrics))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		var left = Math.Min(message.Count ?? MaximumItems, MaximumItems);
		if (left <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		var exactMarket = value.IsCanonicalMarketId() ? value : null;
		var markets = await SafeRest().GetMarketsAsync(exactMarket,
			ExchangeFilter, MaximumItems, cancellationToken);
		CacheMarkets(markets);
		var skip = Math.Max(0L, message.Skip ?? 0);
		foreach (var market in markets.Where(IsValidMarket)
			.OrderBy(static item => item.Status == CoinMetricsMarketStatuses.Offline)
			.ThenBy(static item => item.Exchange,
				StringComparer.OrdinalIgnoreCase)
			.ThenBy(static item => item.Market,
				StringComparer.OrdinalIgnoreCase))
		{
			if (!Matches(market, value))
				continue;
			var security = ToSecurityMessage(market, message.TransactionId);
			if (!security.IsMatch(message, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			await SendOutMessageAsync(security, cancellationToken);
			if (--left == 0)
				break;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var market = await ResolveMarketAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(market);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var trades = await SafeRest().GetTradesAsync(market.Market, from, to,
				GetHistoryLimit(remaining), cancellationToken);
			var sent = 0;
			foreach (var trade in trades)
			{
				ValidateMarket(market, trade?.Market, "trade");
				await SendTradeAsync(trade, securityId, message.TransactionId, 0,
					cancellationToken);
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinMetricsStreamKinds.Trades, market.Market, default,
				CoinMetricsBookDepthModes.Unknown), 0,
			remaining, null, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var market = await ResolveMarketAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(market);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var quotes = await SafeRest().GetQuotesAsync(market.Market, from, to,
				GetHistoryLimit(remaining), cancellationToken);
			var sent = 0;
			foreach (var quote in quotes)
			{
				ValidateMarket(market, quote?.Market, "quote");
				await SendLevel1Async(quote, securityId, message.TransactionId, 0,
					cancellationToken);
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinMetricsStreamKinds.Quotes, market.Market, default,
				CoinMetricsBookDepthModes.Unknown), 0,
			remaining, null, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var market = await ResolveMarketAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(market);
		var depth = Math.Min(Math.Max(1, message.MaxDepth ?? MarketDepth),
			MarketDepth);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var books = await SafeRest().GetOrderBooksAsync(market.Market, from,
				to, GetHistoryLimit(remaining), depth, cancellationToken);
			var sent = 0;
			foreach (var book in books)
			{
				ValidateMarket(market, book?.Market, "order book");
				await SendOrderBookAsync(book, securityId,
					message.TransactionId, depth, 0, cancellationToken);
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinMetricsStreamKinds.OrderBooks, market.Market, default,
				depth > 100
					? CoinMetricsBookDepthModes.FullBook
					: CoinMetricsBookDepthModes.Hundred), depth, remaining, null,
			cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var timeFrame = message.GetTimeFrame();
		_ = timeFrame.ToFrequency();
		var market = await ResolveMarketAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(market);
		var remaining = message.Count;
		DateTime? lastCandleOpenTime = null;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var candles = await SafeRest().GetCandlesAsync(market.Market,
				timeFrame, from, to, GetHistoryLimit(remaining), cancellationToken);
			var sent = 0;
			foreach (var candle in candles)
			{
				ValidateMarket(market, candle?.Market, "candle");
				var openTime = await SendCandleAsync(candle, securityId,
					message.TransactionId, timeFrame, 0, false, cancellationToken);
				if (lastCandleOpenTime is null || openTime > lastCandleOpenTime)
					lastCandleOpenTime = openTime;
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinMetricsStreamKinds.Candles, market.Market, timeFrame,
				CoinMetricsBookDepthModes.Unknown),
			0, remaining, lastCandleOpenTime, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask<CoinMetricsMarket> ResolveMarketAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinMetrics))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Coin Metrics.");
		var identity = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		identity.ThrowIfEmpty(nameof(securityId.SecurityCode));
		using (_sync.EnterScope())
			if (_markets.TryGetValue(identity, out var cached))
				return cached;

		identity = CoinMetricsExtensions.NormalizeMarket(identity);
		var markets = await SafeRest().GetMarketsAsync(identity, null, 2,
			cancellationToken);
		CacheMarkets(markets);
		var exact = markets.Where(IsValidMarket).Where(item =>
			item.Market.EqualsIgnoreCase(identity)).Take(2).ToArray();
		if (exact.Length == 1)
			return exact[0];
		throw new InvalidOperationException(
			$"Coin Metrics market '{identity}' is unknown or ambiguous. Use security lookup and preserve the canonical market identity.");
	}

	private void CacheMarkets(IEnumerable<CoinMetricsMarket> markets)
	{
		using (_sync.EnterScope())
			foreach (var market in markets.Where(IsValidMarket))
				_markets[market.Market] = market;
	}

	private bool IsValidMarket(CoinMetricsMarket market)
		=> market is not null && !market.Market.IsEmpty() &&
			!market.Exchange.IsEmpty() &&
			market.Type != CoinMetricsMarketTypes.Unknown &&
			(market.AssetClass is CoinMetricsAssetClasses.Unknown or
				CoinMetricsAssetClasses.Digital) &&
			(ExchangeFilter.IsEmpty() ||
				market.Exchange.EqualsIgnoreCase(ExchangeFilter)) &&
			(IsInactiveIncluded ||
				market.Status != CoinMetricsMarketStatuses.Offline) &&
			(IsExperimentalIncluded || market.IsExperimental != true);

	private static bool Matches(CoinMetricsMarket market, string value)
	{
		if (value.IsEmpty())
			return true;
		return market.Market.ContainsIgnoreCase(value) ||
			market.Exchange.ContainsIgnoreCase(value) ||
			market.Symbol.ContainsIgnoreCase(value) ||
			market.Pair.ContainsIgnoreCase(value) ||
			market.BaseAsset.ContainsIgnoreCase(value) ||
			market.QuoteAsset.ContainsIgnoreCase(value) ||
			market.Description.ContainsIgnoreCase(value);
	}

	private static SecurityId ToSecurityId(CoinMetricsMarket market)
		=> new()
		{
			SecurityCode = market.Market,
			BoardCode = BoardCodes.CoinMetrics,
			Native = market.Market,
		};

	private static SecurityMessage ToSecurityMessage(CoinMetricsMarket market,
		long originalTransactionId)
	{
		var priceStep = Positive(market.PriceIncrement) ??
			Positive(market.TickSize);
		var name = market.Symbol.IsEmpty(market.Pair).IsEmpty(market.Market);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(market),
			Name = name + " @ " + market.Exchange,
			ShortName = name,
			Class = market.Exchange,
			SecurityType = ToSecurityType(market.Type),
			Currency = ToCurrency(market.QuoteAsset),
			PriceStep = priceStep,
			Decimals = priceStep?.GetCachedDecimals(),
			VolumeStep = Positive(market.AmountIncrement),
			MinVolume = Positive(market.MinimumAmount) ??
				Positive(market.MinimumOrderSize),
			MaxVolume = Positive(market.MaximumAmount),
			Multiplier = Positive(market.ContractSize),
			IssueDate = ParseOptionalTime(
				market.ListingTime.IsEmpty(market.IssueDate), "listing"),
			ExpiryDate = ParseOptionalTime(
				market.ExpirationTime.IsEmpty(market.MaturityDate), "expiration"),
		};
		if (market.Type == CoinMetricsMarketTypes.Option)
		{
			message.OptionType = market.OptionType switch
			{
				CoinMetricsOptionTypes.Call => OptionTypes.Call,
				CoinMetricsOptionTypes.Put => OptionTypes.Put,
				_ => null,
			};
			message.OptionStyle = market.IsEuropean switch
			{
				true => OptionStyles.European,
				false => OptionStyles.American,
				_ => null,
			};
			message.Strike = Positive(market.Strike);
		}
		return message;
	}

	private static SecurityTypes ToSecurityType(CoinMetricsMarketTypes type)
		=> type switch
		{
			CoinMetricsMarketTypes.Spot => SecurityTypes.CryptoCurrency,
			CoinMetricsMarketTypes.Future => SecurityTypes.Future,
			CoinMetricsMarketTypes.Option => SecurityTypes.Option,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type,
				"Coin Metrics market type is unsupported."),
		};

	private static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	private static DateTime? ParseOptionalTime(string value, string field)
		=> value.IsEmpty()
			? null
			: value.ParseCoinMetricsReferenceTime(field);

	private async ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		SecurityId securityId, CoinMetricsStreamKey key, int depth,
		long? remaining, DateTime? lastCountedCandle,
		CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(
				"A Coin Metrics API key is required for WebSocket streams.");
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			var subscription = new LiveSubscription
			{
				TransactionId = message.TransactionId,
				SecurityId = securityId,
				Key = key,
				Depth = depth,
				Remaining = remaining,
				LastCountedCandle = lastCountedCandle,
			};
			CoinMetricsStreamClient stream;
			bool isFirst;
			using (_sync.EnterScope())
			{
				if (_liveSubscriptions.ContainsKey(message.TransactionId))
					throw new InvalidOperationException(
						$"Coin Metrics subscription {message.TransactionId} already exists.");
				isFirst = !_streams.TryGetValue(key, out stream);
				if (isFirst)
				{
					stream = new(SocketEndpoint, Token, key,
						ReConnectionSettings.WorkingTime,
						Math.Max(1, ReConnectionSettings.ReAttemptCount))
					{
						Parent = this,
					};
					stream.MessageReceived += OnStreamMessageAsync;
					stream.Error += SendOutErrorAsync;
					_streams.Add(key, stream);
				}
				_liveSubscriptions.Add(message.TransactionId, subscription);
			}
			try
			{
				if (isFirst)
					await stream.ConnectAsync(cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
				{
					_liveSubscriptions.Remove(message.TransactionId);
					if (isFirst)
						_streams.Remove(key);
				}
				if (isFirst)
				{
					stream.MessageReceived -= OnStreamMessageAsync;
					stream.Error -= SendOutErrorAsync;
					stream.Dispose();
				}
				throw;
			}
		}
		finally
		{
			_streamGate.Release();
		}
	}

	private async ValueTask RemoveLiveSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			LiveSubscription removed;
			CoinMetricsStreamClient stream = null;
			using (_sync.EnterScope())
			{
				if (!_liveSubscriptions.Remove(transactionId, out removed))
					return;
				if (!_liveSubscriptions.Values.Any(item => item.Key == removed.Key))
					_streams.Remove(removed.Key, out stream);
			}
			if (stream is null)
				return;
			stream.MessageReceived -= OnStreamMessageAsync;
			stream.Error -= SendOutErrorAsync;
			try
			{
				await stream.DisconnectAsync(cancellationToken);
			}
			finally
			{
				stream.Dispose();
			}
		}
		finally
		{
			_streamGate.Release();
		}
	}

	private async ValueTask OnStreamMessageAsync(CoinMetricsStreamUpdate update,
		CancellationToken cancellationToken)
	{
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Key == update.Key)];
		if (subscriptions.Length == 0)
			return;

		switch (update.Key.Kind)
		{
			case CoinMetricsStreamKinds.Trades:
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendTradeAsync(update.Trade,
						subscription.SecurityId, subscription.TransactionId,
						update.Trade.SequenceId.ParseCoinMetricsSequence("trade"),
						cancellationToken);
					await ConsumeLiveItemAsync(subscription, null,
						cancellationToken);
				}
				break;
			case CoinMetricsStreamKinds.Quotes:
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendLevel1Async(update.Quote,
						subscription.SecurityId, subscription.TransactionId,
						update.Quote.SequenceId.ParseCoinMetricsSequence("quote"),
						cancellationToken);
					await ConsumeLiveItemAsync(subscription, null,
						cancellationToken);
				}
				break;
			case CoinMetricsStreamKinds.OrderBooks:
				var bookSequence = update.OrderBook.SequenceId
					.ParseCoinMetricsSequence("order-book");
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendOrderBookAsync(update.OrderBook,
						subscription.SecurityId, subscription.TransactionId,
						subscription.Depth, bookSequence, cancellationToken);
					await ConsumeLiveItemAsync(subscription, null,
						cancellationToken);
				}
				break;
			case CoinMetricsStreamKinds.Candles:
				var candleOpenTime = ValidateCandle(update.Candle);
				var candleSequence = update.Candle.SequenceId
					.ParseCoinMetricsSequence("candle");
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendCandleAsync(update.Candle,
						subscription.SecurityId, subscription.TransactionId,
						subscription.Key.TimeFrame, candleSequence, true,
						cancellationToken);
					await ConsumeLiveItemAsync(subscription, candleOpenTime,
						cancellationToken);
				}
				break;
			default:
				throw new InvalidDataException(
					"Coin Metrics returned an unsupported stream update.");
		}
	}

	private bool IsLiveSubscriptionActive(LiveSubscription subscription)
	{
		using (_sync.EnterScope())
			return _liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) && ReferenceEquals(current, subscription);
	}

	private async ValueTask<bool> ConsumeLiveItemAsync(
		LiveSubscription subscription, DateTime? candleOpenTime,
		CancellationToken cancellationToken)
	{
		var isFinished = false;
		CoinMetricsStreamClient stream = null;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) || !ReferenceEquals(current, subscription))
				return true;
			if (candleOpenTime is { } openTime)
			{
				if (current.LastCountedCandle == openTime)
					return false;
				current.LastCountedCandle = openTime;
			}
			if (current.Remaining is not > 0 || --current.Remaining != 0)
				return false;
			_liveSubscriptions.Remove(current.TransactionId);
			if (!_liveSubscriptions.Values.Any(item => item.Key == current.Key))
				_streams.Remove(current.Key, out stream);
			isFinished = true;
		}
		if (!isFinished)
			return false;
		if (stream is not null)
		{
			stream.MessageReceived -= OnStreamMessageAsync;
			stream.Error -= SendOutErrorAsync;
		}
		await SendSubscriptionFinishedAsync(subscription.TransactionId,
			cancellationToken);
		if (stream is not null)
		{
			try
			{
				await stream.DisconnectAsync(default);
			}
			finally
			{
				stream.Dispose();
			}
		}
		return true;
	}

	private ValueTask SendTradeAsync(CoinMetricsTrade trade,
		SecurityId securityId, long transactionId, long sequence,
		CancellationToken cancellationToken)
	{
		if (trade?.Time.IsEmpty() != false || trade.Price is not > 0 ||
			trade.Amount is null or < 0 || trade.CoinMetricsId.IsEmpty())
			throw new InvalidDataException(
				"Coin Metrics returned an invalid trade.");
		return SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = trade.Time.ParseCoinMetricsTime("trade"),
			TradeStringId = trade.CoinMetricsId,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			OriginSide = trade.Side switch
			{
				CoinMetricsTradeSides.Buy => Sides.Buy,
				CoinMetricsTradeSides.Sell => Sides.Sell,
				_ => null,
			},
			SeqNum = sequence,
		}, cancellationToken);
	}

	private ValueTask SendLevel1Async(CoinMetricsQuote quote,
		SecurityId securityId, long transactionId, long sequence,
		CancellationToken cancellationToken)
	{
		if (quote?.Time.IsEmpty() != false || quote.BidPrice is not > 0 &&
			quote.AskPrice is not > 0 || quote.BidSize is < 0 ||
			quote.AskSize is < 0)
			throw new InvalidDataException(
				"Coin Metrics returned an invalid quote.");
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.Time.ParseCoinMetricsTime("quote"),
			SeqNum = sequence,
		}
		.TryAdd(Level1Fields.BestBidPrice, Positive(quote.BidPrice))
		.TryAdd(Level1Fields.BestBidVolume, NonNegative(quote.BidSize))
		.TryAdd(Level1Fields.BestAskPrice, Positive(quote.AskPrice))
		.TryAdd(Level1Fields.BestAskVolume, NonNegative(quote.AskSize)),
			cancellationToken);
	}

	private ValueTask SendOrderBookAsync(CoinMetricsOrderBook book,
		SecurityId securityId, long transactionId, int depth, long sequence,
		CancellationToken cancellationToken)
	{
		if (book?.Time.IsEmpty() != false || book.Asks is null ||
			book.Bids is null)
			throw new InvalidDataException(
				"Coin Metrics returned an incomplete order book.");
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = book.Time.ParseCoinMetricsTime("order-book"),
			Bids = ConvertLevels(book.Bids, true, depth),
			Asks = ConvertLevels(book.Asks, false, depth),
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = sequence,
		}, cancellationToken);
	}

	private static QuoteChange[] ConvertLevels(
		IEnumerable<CoinMetricsBookLevel> levels, bool isBid, int depth)
	{
		var result = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Price is not > 0 || level.Size is null or < 0)
				throw new InvalidDataException(
					"Coin Metrics returned an invalid order-book level.");
			if (level.Size > 0)
				result.Add(new(level.Price.Value, level.Size.Value));
		}
		return [.. (isBid
			? result.OrderByDescending(static quote => quote.Price)
			: result.OrderBy(static quote => quote.Price)).Take(depth)];
	}

	private async ValueTask<DateTime> SendCandleAsync(
		CoinMetricsCandle candle, SecurityId securityId, long transactionId,
		TimeSpan timeFrame, long sequence, bool isLive,
		CancellationToken cancellationToken)
	{
		var openTime = ValidateCandle(candle);
		var closeTime = openTime + timeFrame;
		await SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open.Value,
			HighPrice = candle.High.Value,
			LowPrice = candle.Low.Value,
			ClosePrice = candle.Close.Value,
			TotalVolume = candle.Volume ?? 0,
			TotalTicks = ToTradeCount(candle.TradesCount),
			SeqNum = sequence,
			State = !isLive || closeTime <= CurrentTime.EnsureUtc()
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
		return openTime;
	}

	private static DateTime ValidateCandle(CoinMetricsCandle candle)
	{
		if (candle?.Time.IsEmpty() != false || candle.Open is not >= 0 ||
			candle.High is not >= 0 || candle.Low is not >= 0 ||
			candle.Close is not >= 0 || candle.High < candle.Low ||
			candle.Volume is < 0 || candle.TradesCount is < 0)
			throw new InvalidDataException(
				"Coin Metrics returned an invalid candle.");
		return candle.Time.ParseCoinMetricsTime("candle");
	}

	private static int? ToTradeCount(long? value)
		=> value is >= 0
			? (int)Math.Min(int.MaxValue, value.Value)
			: null;

	private static void ValidateMarket(CoinMetricsMarket expected,
		string actual, string field)
	{
		if (!expected.Market.EqualsIgnoreCase(actual))
			throw new InvalidDataException(
				$"Coin Metrics returned {field} data for a different market.");
	}

	private (DateTime From, DateTime To) GetHistoryRange(
		MarketDataMessage message)
	{
		var to = (message.To ?? CurrentTime).EnsureUtc();
		var earliest = to - DateTime.UnixEpoch < HistoryLookback
			? DateTime.UnixEpoch
			: to - HistoryLookback;
		var from = (message.From ?? earliest).EnsureUtc();
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message),
				"Coin Metrics history start time must be earlier than end time.");
		return (from, to);
	}

	private int GetHistoryLimit(long? remaining)
		=> (int)Math.Min(HistoryLimit, remaining ?? HistoryLimit);

	private static bool ShouldDownloadHistory(MarketDataMessage message)
		=> message.IsHistoryOnly() || message.From is not null ||
			message.To is not null;

	private static long? SubtractCount(long? remaining, int sent)
		=> remaining is null ? null : Math.Max(0, remaining.Value - sent);

	private async ValueTask FinishSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;
}
