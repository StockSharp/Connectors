namespace StockSharp.Amberdata;

public partial class AmberdataMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		if (!message.SecurityId.BoardCode.IsEmpty() &&
			!message.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Amberdata))
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
		var exchangeFilter = ExchangeFilter;
		string instrumentFilter = null;
		if (!value.IsEmpty())
		{
			if (value.Contains(':'))
			{
				var key = AmberdataExtensions.ParseSecurityKey(value,
					exchangeFilter);
				exchangeFilter = key.Exchange;
				instrumentFilter = key.Instrument;
			}
			else
			{
				var normalized = value.Replace('/', '_');
				if (normalized.Contains('_') &&
					!normalized.Any(char.IsWhiteSpace))
					instrumentFilter = AmberdataExtensions.NormalizeInstrument(
						normalized);
			}
		}

		var references = await SafeRest().GetReferencesAsync(exchangeFilter,
			instrumentFilter, IsInactiveIncluded, MaximumItems, cancellationToken);
		CacheReferences(references);
		var skip = Math.Max(0L, message.Skip ?? 0);
		foreach (var reference in references.Where(IsValidReference)
			.OrderByDescending(static item => item.IsExchangeEnabled)
			.ThenBy(static item => item.Exchange,
				StringComparer.OrdinalIgnoreCase)
			.ThenBy(static item => item.Instrument,
				StringComparer.OrdinalIgnoreCase))
		{
			if (!Matches(reference, value))
				continue;
			var security = ToSecurityMessage(reference, message.TransactionId);
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

		var reference = await ResolveReferenceAsync(message.SecurityId,
			cancellationToken);
		var key = ToSecurityKey(reference);
		var securityId = ToSecurityId(key);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var trades = await SafeRest().GetTradesAsync(key, from, to,
				GetHistoryLimit(remaining), cancellationToken);
			var sent = 0;
			foreach (var trade in trades)
			{
				ValidateIdentity(key, trade?.Exchange, trade?.Instrument,
					"trade");
				await SendTradeAsync(trade, securityId, message.TransactionId,
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
			new(AmberdataSocketChannels.Trades, key), 0, remaining,
			cancellationToken);
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

		var reference = await ResolveReferenceAsync(message.SecurityId,
			cancellationToken);
		var key = ToSecurityKey(reference);
		var securityId = ToSecurityId(key);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var tickers = await SafeRest().GetTickersAsync(key, from, to,
				GetHistoryLimit(remaining), cancellationToken);
			var sent = 0;
			foreach (var ticker in tickers)
			{
				ValidateIdentity(key, ticker?.Exchange, ticker?.Instrument,
					"ticker");
				await SendLevel1Async(ticker, securityId, message.TransactionId,
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
			new(AmberdataSocketChannels.TickerSnapshots, key), 0, remaining,
			cancellationToken);
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

		var reference = await ResolveReferenceAsync(message.SecurityId,
			cancellationToken);
		var key = ToSecurityKey(reference);
		var securityId = ToSecurityId(key);
		var depth = Math.Min(Math.Max(1, message.MaxDepth ?? MarketDepth),
			MarketDepth);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var rows = await SafeRest().GetOrderBooksAsync(key, from, to,
				Math.Min(100000, GetHistoryLimit(remaining) * 2), depth,
				cancellationToken);
			foreach (var row in rows)
				ValidateIdentity(key, row?.Exchange, row?.Instrument,
					"order book");
			var sent = 0;
			foreach (var group in rows.Where(static row => row?.Timestamp is not null)
				.GroupBy(static row => row.Timestamp.Value)
				.OrderBy(static group => group.Key)
				.Take(GetHistoryLimit(remaining)))
			{
				var bids = ConvertLevels(group.SelectMany(static row =>
					row.Bids ?? []), true, depth);
				var asks = ConvertLevels(group.SelectMany(static row =>
					row.Asks ?? []), false, depth);
				var sequence = group.Select(static row => row.Sequence.ToSequence())
					.DefaultIfEmpty().Max();
				await SendOrderBookAsync(bids, asks,
					group.Key.ToAmberdataTime("order-book"), securityId,
					message.TransactionId, sequence, cancellationToken);
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
			new(AmberdataSocketChannels.OrderSnapshots, key), depth, remaining,
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
		var interval = timeFrame.ToTimeInterval();
		if (!message.IsHistoryOnly() && timeFrame != TimeSpan.FromMinutes(1))
			throw new NotSupportedException(
				"Amberdata WebSocket OHLCV supports one-minute candles; hourly and daily intervals are historical only.");
		var reference = await ResolveReferenceAsync(message.SecurityId,
			cancellationToken);
		var key = ToSecurityKey(reference);
		var securityId = ToSecurityId(key);
		var remaining = message.Count;
		DateTime? lastCandleOpenTime = null;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var candles = await SafeRest().GetOhlcvAsync(key, interval, from, to,
				GetHistoryLimit(remaining), cancellationToken);
			var sent = 0;
			foreach (var candle in candles)
			{
				ValidateIdentity(key, candle?.Exchange, candle?.Instrument,
					"OHLCV");
				var openTime = await SendCandleAsync(candle, securityId,
					message.TransactionId, timeFrame, cancellationToken);
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
			new(AmberdataSocketChannels.Ohlcv, key), 0, remaining,
			lastCandleOpenTime, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask<AmberdataReference> ResolveReferenceAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Amberdata))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Amberdata.");
		var identity = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		identity.ThrowIfEmpty(nameof(securityId.SecurityCode));
		using (_sync.EnterScope())
			if (_references.TryGetValue(identity, out var cached))
				return cached;

		var key = AmberdataExtensions.ParseSecurityKey(identity, ExchangeFilter);
		var references = await SafeRest().GetReferencesAsync(key.Exchange,
			key.Instrument, IsInactiveIncluded, 5000, cancellationToken);
		CacheReferences(references);
		var exact = references.Where(IsValidReference).Where(item =>
			item.Instrument.EqualsIgnoreCase(key.Instrument) &&
			(key.Exchange.IsEmpty() ||
				item.Exchange.EqualsIgnoreCase(key.Exchange))).Take(2).ToArray();
		if (exact.Length == 1)
			return exact[0];
		throw new InvalidOperationException(
			$"Amberdata instrument '{identity}' is unknown or ambiguous. Use security lookup and preserve exchange:instrument identity.");
	}

	private void CacheReferences(IEnumerable<AmberdataReference> references)
	{
		using (_sync.EnterScope())
			foreach (var reference in references.Where(IsValidReference))
				_references[ToSecurityKey(reference).NativeId] = reference;
	}

	private bool IsValidReference(AmberdataReference reference)
		=> reference?.Exchange.IsEmpty() == false &&
			!reference.Instrument.IsEmpty() && !reference.BaseSymbol.IsEmpty() &&
			!reference.QuoteSymbol.IsEmpty() &&
			(reference.Market.IsEmpty() || reference.Market.EqualsIgnoreCase("spot")) &&
			(IsInactiveIncluded || reference.IsExchangeEnabled);

	private static bool Matches(AmberdataReference reference, string value)
	{
		if (value.IsEmpty())
			return true;
		var normalized = value.Replace('/', '_');
		return ToSecurityKey(reference).NativeId.ContainsIgnoreCase(normalized) ||
			reference.Exchange.ContainsIgnoreCase(normalized) ||
			reference.Instrument.ContainsIgnoreCase(normalized) ||
			reference.BaseSymbol.ContainsIgnoreCase(normalized) ||
			reference.QuoteSymbol.ContainsIgnoreCase(normalized) ||
			(reference.BaseSymbol + "_" + reference.QuoteSymbol)
				.ContainsIgnoreCase(normalized);
	}

	private static AmberdataSecurityKey ToSecurityKey(
		AmberdataReference reference)
		=> new AmberdataSecurityKey(reference.Exchange,
			reference.Instrument).Normalize();

	private static SecurityId ToSecurityId(AmberdataSecurityKey key)
		=> new()
		{
			SecurityCode = key.NativeId.ToUpperInvariant(),
			BoardCode = BoardCodes.Amberdata,
			Native = key.NativeId,
		};

	private static SecurityMessage ToSecurityMessage(
		AmberdataReference reference, long originalTransactionId)
	{
		var key = ToSecurityKey(reference);
		var priceStep = Positive(reference.PricePrecision);
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(key),
			Name = reference.BaseSymbol.ToUpperInvariant() + "/" +
				reference.QuoteSymbol.ToUpperInvariant() + " @ " +
				reference.Exchange.ToUpperInvariant(),
			ShortName = reference.Instrument.ToUpperInvariant(),
			Class = reference.Exchange.ToUpperInvariant(),
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = ToCurrency(reference.QuoteSymbol),
			PriceStep = priceStep,
			Decimals = priceStep?.GetCachedDecimals(),
			VolumeStep = Positive(reference.VolumePrecision),
			MinVolume = Positive(reference.MinimumVolume),
			MaxVolume = Positive(reference.MaximumVolume),
			IssueDate = reference.ListingTimestamp?.ToAmberdataTime(
				"listing"),
		};
	}

	private static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	private ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		SecurityId securityId, AmberdataStreamKey key, int depth, long? remaining,
		CancellationToken cancellationToken)
		=> AddLiveSubscriptionAsync(message, securityId, key, depth, remaining,
			null, cancellationToken);

	private async ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		SecurityId securityId, AmberdataStreamKey key, int depth, long? remaining,
		DateTime? lastCountedCandle, CancellationToken cancellationToken)
	{
		key = new(key.Channel, key.Security.Normalize());
		var subscription = new LiveSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = securityId,
			Key = key,
			Depth = depth,
			Remaining = remaining,
			LastCountedCandle = lastCountedCandle,
		};
		bool isFirst;
		using (_sync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(message.TransactionId))
				throw new InvalidOperationException(
					$"Amberdata subscription {message.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item => item.Key == key);
			_liveSubscriptions.Add(message.TransactionId, subscription);
		}
		try
		{
			if (isFirst)
				await GetOrCreateSocket().SubscribeAsync(key, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_liveSubscriptions.Remove(message.TransactionId);
			throw;
		}
	}

	private async ValueTask RemoveLiveSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		bool isLast;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			isLast = !_liveSubscriptions.Values.Any(item =>
				item.Key == removed.Key);
			if (isLast)
				_books.Remove(removed.Key);
		}
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(removed.Key, cancellationToken);
	}

	private async ValueTask OnSocketMessageAsync(AmberdataSocketUpdate update,
		CancellationToken cancellationToken)
	{
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Key == update.Key)];
		if (subscriptions.Length == 0)
			return;

		switch (update.Key.Channel)
		{
			case AmberdataSocketChannels.Trades:
				foreach (var subscription in subscriptions)
				{
					foreach (var trade in update.Trades ?? [])
					{
						if (!IsLiveSubscriptionActive(subscription))
							break;
						await SendTradeAsync(trade, subscription.SecurityId,
							subscription.TransactionId, cancellationToken);
						if (await ConsumeLiveItemAsync(subscription, null,
							cancellationToken))
							break;
					}
				}
				break;
			case AmberdataSocketChannels.TickerSnapshots:
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendLevel1Async(update.Ticker,
						subscription.SecurityId, subscription.TransactionId,
						cancellationToken);
					await ConsumeLiveItemAsync(subscription, null,
						cancellationToken);
				}
				break;
			case AmberdataSocketChannels.OrderSnapshots:
				if (!TryUpdateBook(update.Key, update.BookSides, out var bids,
					out var asks, out var serverTime, out var sequence))
					return;
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendOrderBookAsync(ConvertLevels(bids, true,
							subscription.Depth), ConvertLevels(asks, false,
							subscription.Depth), serverTime,
						subscription.SecurityId, subscription.TransactionId,
						sequence, cancellationToken);
					await ConsumeLiveItemAsync(subscription, null,
						cancellationToken);
				}
				break;
			case AmberdataSocketChannels.Ohlcv:
				var candleOpenTime = ValidateCandle(update.Ohlcv);
				foreach (var subscription in subscriptions)
				{
					if (!IsLiveSubscriptionActive(subscription))
						continue;
					await SendCandleAsync(update.Ohlcv,
						subscription.SecurityId, subscription.TransactionId,
						cancellationToken);
					await ConsumeLiveItemAsync(subscription, candleOpenTime,
						cancellationToken);
				}
				break;
			default:
				throw new InvalidDataException(
					"Amberdata returned an unsupported stream update.");
		}
	}

	private bool TryUpdateBook(AmberdataStreamKey key,
		AmberdataSocketBookSide[] sides, out AmberdataSocketBookLevel[] bids,
		out AmberdataSocketBookLevel[] asks, out DateTime serverTime,
		out long sequence)
	{
		bids = null;
		asks = null;
		serverTime = default;
		sequence = 0;
		if (sides is not { Length: > 0 })
			return false;

		var isChanged = false;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(key, out var state))
			{
				state = new();
				_books.Add(key, state);
			}
			foreach (var side in sides)
			{
				if (side?.Levels is null || side.ExchangeTimestamp is null &&
					side.Timestamp is null)
					throw new InvalidDataException(
						"Amberdata order-book snapshot is incomplete.");
				var time = (side.ExchangeTimestamp ?? side.Timestamp).Value
					.ToAmberdataTime("order-book");
				if (side.IsBid)
				{
					if (state.Bids is not null && time < state.BidTime)
						continue;
					state.Bids = side.Levels;
					state.BidTime = time;
				}
				else
				{
					if (state.Asks is not null && time < state.AskTime)
						continue;
					state.Asks = side.Levels;
					state.AskTime = time;
				}
				state.ServerTime = state.ServerTime > time
					? state.ServerTime
					: time;
				state.Sequence = Math.Max(state.Sequence, side.Sequence ?? 0);
				isChanged = true;
			}
			if (!isChanged || state.Bids is null || state.Asks is null)
				return false;
			bids = state.Bids;
			asks = state.Asks;
			serverTime = state.ServerTime;
			sequence = state.Sequence;
			return true;
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
		var isLast = false;
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
			isLast = !_liveSubscriptions.Values.Any(item =>
				item.Key == current.Key);
			if (isLast)
				_books.Remove(current.Key);
			isFinished = true;
		}
		if (!isFinished)
			return false;
		await SendSubscriptionFinishedAsync(subscription.TransactionId,
			cancellationToken);
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(subscription.Key, cancellationToken);
		return true;
	}

	private ValueTask SendTradeAsync(AmberdataTrade trade,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.ExchangeTimestamp is null || trade.Price is not > 0 ||
			trade.Volume is null or < 0)
			throw new InvalidDataException(
				"Amberdata returned an invalid historical trade.");
		return SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = trade.ExchangeTimestamp.Value.ToAmberdataTime(
				trade.ExchangeTimestampNanoseconds, "trade"),
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.IsBuySide switch
			{
				true => Sides.Buy,
				false => Sides.Sell,
				_ => null,
			},
			SeqNum = trade.Sequence ?? 0,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(AmberdataSocketTrade trade,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.Timestamp is null || trade.Price is not > 0 ||
			trade.Volume is null or < 0)
			throw new InvalidDataException(
				"Amberdata returned an invalid live trade.");
		return SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = trade.Timestamp.Value.ToAmberdataTime(
				trade.TimestampNanoseconds, "trade"),
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.IsBuy switch
			{
				true => Sides.Buy,
				false => Sides.Sell,
				_ => null,
			},
		}, cancellationToken);
	}

	private ValueTask SendLevel1Async(AmberdataTicker ticker,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker?.ExchangeTimestamp is null || ticker.Bid is not > 0 &&
			ticker.Ask is not > 0 && ticker.Last is not > 0 ||
			ticker.BidVolume is < 0 || ticker.AskVolume is < 0 ||
			ticker.LastVolume is < 0)
			throw new InvalidDataException(
				"Amberdata returned an invalid historical ticker.");
		return SendLevel1Async(securityId, transactionId,
			ticker.ExchangeTimestamp.Value.ToAmberdataTime(
				ticker.ExchangeTimestampNanoseconds, "ticker"), ticker.Bid,
			ticker.BidVolume, ticker.Ask, ticker.AskVolume, ticker.Last,
			ticker.LastVolume, ticker.OpenOneDay, ticker.LowOneDay,
			ticker.HighOneDay, ticker.Sequence.ToSequence(), cancellationToken);
	}

	private ValueTask SendLevel1Async(AmberdataSocketTicker ticker,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null || ticker.ExchangeTimestamp is null &&
			ticker.Timestamp is null || ticker.Bid is not > 0 &&
			ticker.Ask is not > 0 && ticker.Last is not > 0 ||
			ticker.BidVolume is < 0 || ticker.AskVolume is < 0 ||
			ticker.LastVolume is < 0)
			throw new InvalidDataException(
				"Amberdata returned an invalid live ticker.");
		var time = ticker.ExchangeTimestamp is { } exchangeTimestamp
			? exchangeTimestamp.ToAmberdataTime(
				ticker.ExchangeTimestampNanoseconds, "ticker")
			: ticker.Timestamp.Value.ToAmberdataTime("ticker");
		return SendLevel1Async(securityId, transactionId, time, ticker.Bid,
			ticker.BidVolume, ticker.Ask, ticker.AskVolume, ticker.Last,
			ticker.LastVolume, ticker.OpenOneDay, ticker.LowOneDay,
			ticker.HighOneDay, ticker.Sequence ?? 0, cancellationToken);
	}

	private ValueTask SendLevel1Async(SecurityId securityId, long transactionId,
		DateTime serverTime, decimal? bid, decimal? bidVolume, decimal? ask,
		decimal? askVolume, decimal? last, decimal? lastVolume, decimal? open,
		decimal? low, decimal? high, long sequence,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			SeqNum = sequence,
		}
		.TryAdd(Level1Fields.BestBidPrice, Positive(bid))
		.TryAdd(Level1Fields.BestBidVolume, NonNegative(bidVolume))
		.TryAdd(Level1Fields.BestAskPrice, Positive(ask))
		.TryAdd(Level1Fields.BestAskVolume, NonNegative(askVolume))
		.TryAdd(Level1Fields.LastTradePrice, Positive(last))
		.TryAdd(Level1Fields.LastTradeVolume, NonNegative(lastVolume))
		.TryAdd(Level1Fields.OpenPrice, Positive(open))
		.TryAdd(Level1Fields.LowPrice, Positive(low))
		.TryAdd(Level1Fields.HighPrice, Positive(high)), cancellationToken);

	private ValueTask SendOrderBookAsync(QuoteChange[] bids, QuoteChange[] asks,
		DateTime serverTime, SecurityId securityId, long transactionId,
		long sequence, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = sequence,
		}, cancellationToken);

	private static QuoteChange[] ConvertLevels(
		IEnumerable<AmberdataBookLevel> levels, bool isBid, int depth)
	{
		var result = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Price is not > 0 || level.Volume is null or < 0)
				throw new InvalidDataException(
					"Amberdata returned an invalid historical order-book level.");
			if (level.Volume > 0)
				result.Add(new(level.Price.Value, level.Volume.Value));
		}
		return SortLevels(result, isBid, depth);
	}

	private static QuoteChange[] ConvertLevels(
		IEnumerable<AmberdataSocketBookLevel> levels, bool isBid, int depth)
	{
		var result = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Price is not > 0 || level.Volume is null or < 0)
				throw new InvalidDataException(
					"Amberdata returned an invalid live order-book level.");
			if (level.Volume > 0)
				result.Add(new(level.Price.Value, level.Volume.Value));
		}
		return SortLevels(result, isBid, depth);
	}

	private static QuoteChange[] SortLevels(IEnumerable<QuoteChange> levels,
		bool isBid, int depth)
		=> [.. (isBid
			? levels.OrderByDescending(static quote => quote.Price)
			: levels.OrderBy(static quote => quote.Price)).Take(depth)];

	private async ValueTask<DateTime> SendCandleAsync(AmberdataOhlcv candle,
		SecurityId securityId, long transactionId, TimeSpan timeFrame,
		CancellationToken cancellationToken)
	{
		if (candle?.ExchangeTimestamp is null || candle.Open is not >= 0 ||
			candle.High is not >= 0 || candle.Low is not >= 0 ||
			candle.Close is not >= 0 || candle.High < candle.Low ||
			candle.Volume is < 0)
			throw new InvalidDataException(
				"Amberdata returned an invalid historical OHLCV candle.");
		var openTime = candle.ExchangeTimestamp.Value.ToAmberdataTime("OHLCV");
		await SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.Value,
			HighPrice = candle.High.Value,
			LowPrice = candle.Low.Value,
			ClosePrice = candle.Close.Value,
			TotalVolume = candle.Volume ?? 0,
			State = CandleStates.Finished,
		}, cancellationToken);
		return openTime;
	}

	private ValueTask SendCandleAsync(AmberdataSocketOhlcv candle,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = ValidateCandle(candle);
		var closeTime = openTime + TimeSpan.FromMinutes(1);
		return SendOutMessageAsync(new TimeFrameCandleMessage
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
			State = closeTime <= CurrentTime.EnsureUtc()
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static DateTime ValidateCandle(AmberdataSocketOhlcv candle)
	{
		if (candle?.Timestamp is null || candle.Open is not >= 0 ||
			candle.High is not >= 0 || candle.Low is not >= 0 ||
			candle.Close is not >= 0 || candle.High < candle.Low ||
			candle.Volume is < 0)
			throw new InvalidDataException(
				"Amberdata returned an invalid live OHLCV candle.");
		return candle.Timestamp.Value.ToAmberdataTime("OHLCV");
	}

	private static void ValidateIdentity(AmberdataSecurityKey key,
		string exchange, string instrument, string field)
	{
		if (!key.Exchange.EqualsIgnoreCase(exchange) ||
			!key.Instrument.EqualsIgnoreCase(instrument))
			throw new InvalidDataException(
				$"Amberdata returned {field} data for a different instrument.");
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
				"Amberdata history start time must be earlier than end time.");
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
