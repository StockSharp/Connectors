namespace StockSharp.Kaiko;

public partial class KaikoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		var supportedTypes = new[]
		{
			SecurityTypes.CryptoCurrency,
			SecurityTypes.Future,
			SecurityTypes.Option,
		};
		if (securityTypes.Count > 0 &&
			!securityTypes.Any(supportedTypes.Contains))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (!message.SecurityId.BoardCode.IsEmpty() &&
			!message.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Kaiko))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var value = message.SecurityId.SecurityCode
			.IsEmpty(message.Name)?.Trim();
		var exchange = ExchangeFilter?.Trim().ToLowerInvariant();
		var instrumentClass = InstrumentClassFilter;
		string code = null;
		string baseAsset = null;
		if (KaikoSecurityKey.TryParse(message.SecurityId.Native as string,
			out var nativeKey))
		{
			exchange = nativeKey.Exchange;
			instrumentClass = nativeKey.InstrumentClass;
			code = nativeKey.Code;
		}
		else if (LooksLikeCode(value))
			code = value.Trim().ToLowerInvariant();
		else if (IsAssetCode(message.SecurityId.SecurityCode))
			baseAsset = message.SecurityId.SecurityCode.Trim().ToLowerInvariant();

		var instruments = await SafeRest().GetInstrumentsAsync(exchange,
			instrumentClass, code, baseAsset, null, MaximumItems,
			cancellationToken);
		var skip = Math.Max(0L, message.Skip ?? 0);
		var left = Math.Min(message.Count ?? MaximumItems, MaximumItems);
		foreach (var instrument in instruments.Where(IsSupportedInstrument))
		{
			if (left <= 0)
				break;
			if (!Matches(instrument, value))
				continue;
			var key = ToKey(instrument);
			Remember(key);
			var security = ToSecurityMessage(instrument, key,
				message.TransactionId);
			if (!security.IsMatch(message, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
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

		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		var remaining = message.Count;
		if (ShouldLoadHistory(message))
			remaining = await SendHistoricalTradesAsync(message, key, remaining,
				cancellationToken);
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, key,
			KaikoSubscriptionKinds.Ticks, default, remaining, cancellationToken);
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
		if (message.IsHistoryOnly())
			throw new NotSupportedException(
				"Kaiko top-of-book Level 1 is available as a live stream only.");

		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		await AddLiveSubscriptionAsync(message, key,
			KaikoSubscriptionKinds.Level1, default, message.Count,
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
		_ = timeFrame.ToAggregate();
		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		var remaining = message.Count;
		if (ShouldLoadHistory(message))
			remaining = await SendHistoricalCandlesAsync(message, key, timeFrame,
				remaining, cancellationToken);
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, key,
			KaikoSubscriptionKinds.Candles, timeFrame, remaining,
			cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask<long?> SendHistoricalTradesAsync(
		MarketDataMessage message, KaikoSecurityKey key, long? remaining,
		CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new NotSupportedException(
				"Kaiko historical trades require an API key.");
		var to = (message.To ?? CurrentTime).EnsureUtc();
		var from = message.From?.EnsureUtc() ?? Subtract(to, TimeSpan.FromDays(1));
		ValidateRange(from, to);
		var limit = Math.Min(remaining ?? HistoryLimit, HistoryLimit).To<int>();
		var sent = 0;
		string continuationToken = null;
		var tokens = new HashSet<string>(StringComparer.Ordinal);
		var trades = new HashSet<string>(StringComparer.Ordinal);
		do
		{
			var pageSize = Math.Min(100000, limit - sent);
			if (pageSize <= 0)
				break;
			var response = await SafeRest().GetTradesAsync(key, from, to,
				pageSize, continuationToken, cancellationToken);
			foreach (var trade in (response.Data ?? [])
				.OrderBy(static item => item?.Timestamp ?? long.MaxValue))
			{
				if (trade is null || sent >= limit)
					break;
				var time = trade.Timestamp.ToKaikoTime();
				if (time < from || time >= to)
					continue;
				var price = trade.Price.ParseKaikoDecimal("trade price");
				var amount = trade.Amount.ParseKaikoDecimal("trade amount");
				if (price <= 0 || amount < 0)
					continue;
				var tradeId = trade.TradeId.IsEmpty()
					? $"{trade.Timestamp}:{trade.Price}:{trade.Amount}"
					: trade.TradeId;
				if (!trades.Add(tradeId))
					continue;
				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = key.ToSecurityId(),
					DataTypeEx = DataType.Ticks,
					ServerTime = time,
					TradeStringId = tradeId,
					TradePrice = price,
					TradeVolume = amount,
					OriginSide = trade.IsTakerSideSell switch
					{
						true => Sides.Sell,
						false => Sides.Buy,
						_ => null,
					},
				}, cancellationToken);
				sent++;
				if (remaining is > 0)
					remaining--;
			}
			continuationToken = response.ContinuationToken;
		}
		while (!continuationToken.IsEmpty() &&
			tokens.Add(continuationToken) && remaining != 0);
		return remaining;
	}

	private async ValueTask<long?> SendHistoricalCandlesAsync(
		MarketDataMessage message, KaikoSecurityKey key, TimeSpan timeFrame,
		long? remaining, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new NotSupportedException(
				"Kaiko historical OHLCV requires an API key.");
		var to = (message.To ?? CurrentTime).EnsureUtc();
		var requested = Math.Min(remaining ?? HistoryLimit, HistoryLimit).To<int>();
		var from = message.From?.EnsureUtc() ?? EstimateFrom(to, timeFrame,
			requested);
		ValidateRange(from, to);
		var sent = 0;
		string continuationToken = null;
		var tokens = new HashSet<string>(StringComparer.Ordinal);
		var candles = new HashSet<DateTime>();
		do
		{
			var pageSize = Math.Min(100000, requested - sent);
			if (pageSize <= 0)
				break;
			var response = await SafeRest().GetOhlcvAsync(key, timeFrame, from,
				to, pageSize, continuationToken, cancellationToken);
			foreach (var candle in (response.Data ?? [])
				.OrderBy(static item => item?.Timestamp ?? long.MaxValue))
			{
				if (candle is null || sent >= requested)
					break;
				var openTime = candle.Timestamp.ToKaikoTime();
				if (openTime < from || openTime >= to || !candles.Add(openTime))
					continue;
				if (!TryReadCandle(candle.Open, candle.High, candle.Low,
					candle.Close, candle.Volume, out var open, out var high,
					out var low, out var close, out var volume))
					continue;
				await SendCandleAsync(key.ToSecurityId(), openTime, timeFrame,
					open, high, low, close, volume, CandleStates.Finished,
					message.TransactionId, cancellationToken);
				sent++;
				if (remaining is > 0)
					remaining--;
			}
			continuationToken = response.ContinuationToken;
		}
		while (!continuationToken.IsEmpty() &&
			tokens.Add(continuationToken) && remaining != 0);
		return remaining;
	}

	private async ValueTask<KaikoSecurityKey> ResolveKeyAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (KaikoSecurityKey.TryParse(securityId.Native as string, out var key))
			return key;
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToLowerInvariant();
		KaikoSecurityKey[] cached;
		using (_sync.EnterScope())
			cached = [.. _instruments.Values.Where(item =>
				item.Code.EqualsIgnoreCase(code) &&
				(ExchangeFilter.IsEmpty() ||
					item.Exchange.EqualsIgnoreCase(ExchangeFilter)) &&
				(InstrumentClassFilter == KaikoInstrumentClasses.Unknown ||
					item.InstrumentClass == InstrumentClassFilter)).Take(2)];
		if (cached.Length == 1)
			return cached[0];
		var instruments = await SafeRest().GetInstrumentsAsync(ExchangeFilter,
			InstrumentClassFilter, code, null, null, 10, cancellationToken);
		var matches = instruments.Where(IsSupportedInstrument)
			.Select(ToKey).Distinct().Take(2).ToArray();
		if (matches.Length != 1)
			throw new InvalidOperationException(
				$"Kaiko instrument '{code}' is unknown or ambiguous. Use security lookup to preserve its exchange and instrument class.");
		Remember(matches[0]);
		return matches[0];
	}

	private async ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		KaikoSecurityKey key, KaikoSubscriptionKinds kind, TimeSpan timeFrame,
		long? remaining, CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = key.ToSecurityId(),
			Key = key,
			Kind = kind,
			TimeFrame = timeFrame,
			Remaining = remaining,
		};
		KaikoStreamKey[] firstKeys;
		using (_sync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(message.TransactionId))
				throw new InvalidOperationException(
					$"Kaiko subscription {message.TransactionId} already exists.");
			var keys = GetStreamKeys(subscription);
			firstKeys = [.. keys.Where(key => !_liveSubscriptions.Values.Any(
				item => GetStreamKeys(item).Contains(key)))];
			_liveSubscriptions.Add(message.TransactionId, subscription);
		}
		var subscribed = new List<KaikoStreamKey>();
		try
		{
			var stream = GetOrCreateStream();
			foreach (var streamKey in firstKeys)
			{
				await stream.SubscribeAsync(streamKey, cancellationToken);
				subscribed.Add(streamKey);
			}
		}
		catch
		{
			using (_sync.EnterScope())
				_liveSubscriptions.Remove(message.TransactionId);
			if (_stream is not null)
				foreach (var streamKey in subscribed)
					await _stream.UnsubscribeAsync(streamKey, cancellationToken);
			throw;
		}
	}

	private async ValueTask RemoveLiveSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		KaikoStreamKey[] lastKeys;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.Remove(transactionId, out LiveSubscription removed))
				return;
			lastKeys = [.. GetStreamKeys(removed).Where(key =>
				!_liveSubscriptions.Values.Any(item =>
					GetStreamKeys(item).Contains(key)))];
		}
		if (_stream is not null)
			foreach (var key in lastKeys)
				await _stream.UnsubscribeAsync(key, cancellationToken);
	}

	private async ValueTask OnMarketUpdateAsync(
		StreamMarketUpdateResponseV1 update,
		CancellationToken cancellationToken)
	{
		if (update is null || update.Exchange.IsEmpty() || update.Code.IsEmpty() ||
			!KaikoExtensions.TryParseInstrumentClass(update.Class,
				out var instrumentClass))
			return;
		var isTrade = update.Commodity == StreamMarketUpdateCommodity.SmucTrade;
		var isTop = update.Commodity ==
			StreamMarketUpdateCommodity.SmucTopOfBook;
		if (!isTrade && !isTop)
			return;
		if (isTop && update.UpdateType is not
			StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.BestBid and not
			StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.UpdatedBid and not
			StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.BestAsk and not
			StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.UpdatedAsk)
			return;
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Key.Exchange.EqualsIgnoreCase(update.Exchange) &&
				item.Key.InstrumentClass == instrumentClass &&
				item.Key.Code.EqualsIgnoreCase(update.Code) &&
				(isTrade
					? item.Kind is KaikoSubscriptionKinds.Ticks or
						KaikoSubscriptionKinds.Level1
					: item.Kind == KaikoSubscriptionKinds.Level1))];
		if (subscriptions.Length == 0)
			return;
		var time = GetMarketTime(update);
		var price = update.Price.ToKaikoDecimal("stream price");
		var amount = update.Amount.ToKaikoDecimal("stream amount");
		if (price <= 0 || amount < 0)
			return;
		foreach (var subscription in subscriptions)
		{
			if (subscription.Kind == KaikoSubscriptionKinds.Ticks)
				await SendTradeAsync(subscription, update, time, price, amount,
					cancellationToken);
			else if (isTrade)
				await SendLastTradeAsync(subscription, update, time, price, amount,
					cancellationToken);
			else
				await SendTopOfBookAsync(subscription, update, time, price, amount,
					cancellationToken);
			await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private async ValueTask OnOhlcvUpdateAsync(
		StreamAggregatesOHLCVResponseV1 update,
		CancellationToken cancellationToken)
	{
		if (update is null || update.Exchange.IsEmpty() || update.Code.IsEmpty() ||
			!KaikoExtensions.TryParseInstrumentClass(update.Class,
				out var instrumentClass))
			return;
		TimeSpan timeFrame;
		try
		{
			timeFrame = ParseAggregate(update.Aggregate);
		}
		catch (InvalidDataException error)
		{
			await SendOutErrorAsync(error, cancellationToken);
			return;
		}
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Kind == KaikoSubscriptionKinds.Candles &&
				item.TimeFrame == timeFrame &&
				item.Key.Exchange.EqualsIgnoreCase(update.Exchange) &&
				item.Key.InstrumentClass == instrumentClass &&
				item.Key.Code.EqualsIgnoreCase(update.Code))];
		if (subscriptions.Length == 0 ||
			!TryReadCandle(update.Open, update.High, update.Low, update.Close,
				update.Volume, out var open, out var high, out var low,
				out var close, out var volume))
			return;
		var openTime = update.Uid.IsEmpty()
			? GetPreviousIntervalStart(
				update.Timestamp.ToKaikoTime("OHLCV"), timeFrame)
			: update.Uid.ParseKaikoTime();
		foreach (var subscription in subscriptions)
		{
			await SendCandleAsync(subscription.SecurityId, openTime, timeFrame,
				open, high, low, close, volume, CandleStates.Finished,
				subscription.TransactionId, cancellationToken);
			await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private ValueTask SendTradeAsync(LiveSubscription subscription,
		StreamMarketUpdateResponseV1 update, DateTime time, decimal price,
		decimal amount, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = time,
			TradeStringId = update.Id.IsEmpty(update.SequenceId),
			TradePrice = price,
			TradeVolume = amount,
			OriginSide = ToSide(update.UpdateType),
		}, cancellationToken);

	private ValueTask SendLastTradeAsync(LiveSubscription subscription,
		StreamMarketUpdateResponseV1 update, DateTime time, decimal price,
		decimal amount, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice, price)
		.TryAdd(Level1Fields.LastTradeVolume, amount)
		.TryAdd(Level1Fields.LastTradeTime, time)
		.TryAdd(Level1Fields.LastTradeOrigin, ToSide(update.UpdateType)),
			cancellationToken);

	private ValueTask SendTopOfBookAsync(LiveSubscription subscription,
		StreamMarketUpdateResponseV1 update, DateTime time, decimal price,
		decimal amount, CancellationToken cancellationToken)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		};
		switch (update.UpdateType)
		{
			case StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.BestBid:
			case StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.UpdatedBid:
				message.TryAdd(Level1Fields.BestBidPrice, price)
					.TryAdd(Level1Fields.BestBidVolume, amount);
				break;
			case StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.BestAsk:
			case StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.UpdatedAsk:
				message.TryAdd(Level1Fields.BestAskPrice, price)
					.TryAdd(Level1Fields.BestAskVolume, amount);
				break;
			default:
				return default;
		}
		return SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask ConsumeLiveItemAsync(LiveSubscription subscription,
		CancellationToken cancellationToken)
	{
		var isFinished = false;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) || !ReferenceEquals(current, subscription))
				return;
			if (current.Remaining is not > 0 || --current.Remaining != 0)
				return;
			isFinished = true;
		}
		if (!isFinished)
			return;
		await SendSubscriptionFinishedAsync(subscription.TransactionId,
			cancellationToken);
		await RemoveLiveSubscriptionAsync(subscription.TransactionId,
			cancellationToken);
	}

	private ValueTask SendCandleAsync(SecurityId securityId, DateTime openTime,
		TimeSpan timeFrame, decimal open, decimal high, decimal low,
		decimal close, decimal volume, CandleStates state, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = open,
			HighPrice = high,
			LowPrice = low,
			ClosePrice = close,
			TotalVolume = volume,
			State = state,
		}, cancellationToken);

	private async ValueTask FinishSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}

	private static KaikoStreamKey[] GetStreamKeys(LiveSubscription subscription)
		=> subscription.Kind switch
		{
			KaikoSubscriptionKinds.Ticks =>
				[new(KaikoStreamKinds.Trades, subscription.Key, default)],
			KaikoSubscriptionKinds.Level1 =>
			[
				new(KaikoStreamKinds.Trades, subscription.Key, default),
				new(KaikoStreamKinds.TopOfBook, subscription.Key, default),
			],
			KaikoSubscriptionKinds.Candles =>
				[new(KaikoStreamKinds.Ohlcv, subscription.Key,
					subscription.TimeFrame)],
			_ => throw new ArgumentOutOfRangeException(nameof(subscription),
				subscription.Kind, null),
		};

	private void Remember(KaikoSecurityKey key)
	{
		using (_sync.EnterScope())
			_instruments[key.ToNative()] = key;
	}

	private static KaikoSecurityKey ToKey(KaikoInstrument instrument)
		=> new(instrument.ExchangeCode.Trim().ToLowerInvariant(),
			instrument.InstrumentClass,
			instrument.Code.Trim().ToLowerInvariant(),
			instrument.BaseAsset?.Trim().ToLowerInvariant(),
			instrument.QuoteAsset?.Trim().ToLowerInvariant(),
			instrument.ExchangePairCode?.Trim());

	private static SecurityMessage ToSecurityMessage(KaikoInstrument instrument,
		KaikoSecurityKey key, long originalTransactionId)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = key.ToSecurityId(),
			Name = key.ExchangePairCode.IsEmpty(key.Code.ToUpperInvariant()),
			ShortName = key.Code.ToUpperInvariant(),
			Class = key.Exchange.ToUpperInvariant() + ":" +
				key.InstrumentClass.ToWire(),
			SecurityType = key.InstrumentClass.ToSecurityType(),
		};
		if (Enum.TryParse<CurrencyTypes>(key.QuoteAsset, true,
			out var currency))
			message.Currency = currency;
		if (instrument.TradeExpiryTimestamp is > 0)
			message.ExpiryDate = instrument.TradeExpiryTimestamp.Value.ToKaikoTime();
		return message;
	}

	private static bool IsSupportedInstrument(KaikoInstrument instrument)
		=> instrument is not null && !instrument.ExchangeCode.IsEmpty() &&
			!instrument.Code.IsEmpty() && instrument.InstrumentClass !=
			KaikoInstrumentClasses.Unknown;

	private static bool Matches(KaikoInstrument instrument, string value)
		=> value.IsEmpty() || instrument.Code.ContainsIgnoreCase(value) ||
			instrument.ExchangePairCode.ContainsIgnoreCase(value) ||
			instrument.BaseAsset.ContainsIgnoreCase(value) ||
			instrument.QuoteAsset.ContainsIgnoreCase(value) ||
			instrument.ExchangeCode.ContainsIgnoreCase(value);

	private static bool LooksLikeCode(string value)
		=> !value.IsEmpty() && value.Contains('-') &&
			value.Length <= 128 && !value.Any(char.IsWhiteSpace);

	private static bool IsAssetCode(string value)
		=> !value.IsEmpty() && value.Length <= 32 &&
			value.All(char.IsLetterOrDigit);

	private static bool ShouldLoadHistory(MarketDataMessage message)
		=> message.IsHistoryOnly() || message.From is not null ||
			message.To is not null;

	private static void ValidateRange(DateTime from, DateTime to)
	{
		if (from < DateTime.UnixEpoch || from >= to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"Kaiko history start time must be at or after the Unix epoch and earlier than end time.");
	}

	private static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame,
		int count)
	{
		try
		{
			var ticks = checked(timeFrame.Ticks * Math.Max(1, count));
			return Subtract(to, TimeSpan.FromTicks(ticks));
		}
		catch (OverflowException)
		{
			return DateTime.UnixEpoch;
		}
	}

	private static DateTime Subtract(DateTime value, TimeSpan interval)
		=> value - DateTime.UnixEpoch < interval
			? DateTime.UnixEpoch
			: value - interval;

	private static DateTime GetMarketTime(StreamMarketUpdateResponseV1 update)
	{
		if (update.TsExchange?.Value is not null)
			return update.TsExchange.ToKaikoTime("exchange");
		if (update.TsCollection?.Value is not null)
			return update.TsCollection.ToKaikoTime("collection");
		return update.TsEvent.ToKaikoTime("event");
	}

	private static DateTime GetPreviousIntervalStart(DateTime deliveryTime,
		TimeSpan timeFrame)
	{
		var boundary = deliveryTime.EnsureUtc().Truncate(timeFrame);
		return boundary - DateTime.UnixEpoch < timeFrame
			? DateTime.UnixEpoch
			: boundary - timeFrame;
	}

	private static Sides? ToSide(
		StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType value)
		=> value switch
		{
			StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.TradeBuy =>
				Sides.Buy,
			StreamMarketUpdateResponseV1.Types.StreamMarketUpdateType.TradeSell =>
				Sides.Sell,
			_ => null,
		};

	private static TimeSpan ParseAggregate(string value)
	{
		if (value.IsEmpty() || value.Length < 2 ||
			!int.TryParse(value[..^1], NumberStyles.None,
				CultureInfo.InvariantCulture, out var count) || count <= 0)
			throw new InvalidDataException(
				$"Invalid Kaiko aggregate '{value}'.");
		var result = char.ToLowerInvariant(value[^1]) switch
		{
			's' => TimeSpan.FromSeconds(count),
			'm' => TimeSpan.FromMinutes(count),
			'h' => TimeSpan.FromHours(count),
			'd' => TimeSpan.FromDays(count),
			_ => throw new InvalidDataException(
				$"Invalid Kaiko aggregate '{value}'."),
		};
		_ = result.ToAggregate();
		return result;
	}

	private static bool TryReadCandle(string openText, string highText,
		string lowText, string closeText, string volumeText, out decimal open,
		out decimal high, out decimal low, out decimal close, out decimal volume)
	{
		open = high = low = close = volume = default;
		if (!decimal.TryParse(openText, NumberStyles.Float,
			CultureInfo.InvariantCulture, out open) ||
			!decimal.TryParse(highText, NumberStyles.Float,
				CultureInfo.InvariantCulture, out high) ||
			!decimal.TryParse(lowText, NumberStyles.Float,
				CultureInfo.InvariantCulture, out low) ||
			!decimal.TryParse(closeText, NumberStyles.Float,
				CultureInfo.InvariantCulture, out close) ||
			!decimal.TryParse(volumeText, NumberStyles.Float,
				CultureInfo.InvariantCulture, out volume))
			return false;
		return open > 0 && high > 0 && low > 0 && close > 0 &&
			high >= low && volume >= 0;
	}
}
