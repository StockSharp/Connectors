namespace StockSharp.Tardis;

public partial class TardisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		var skip = Math.Max(0L, message.Skip ?? 0);
		var left = Math.Max(0L,
			Math.Min(message.Count ?? MaximumItems, MaximumItems));
		foreach (var instrument in GetInstruments()
			.Where(instrument => Matches(instrument, value))
			.OrderBy(static instrument => instrument.Id,
				StringComparer.OrdinalIgnoreCase)
			.ThenBy(static instrument => instrument.AvailableSince,
				StringComparer.Ordinal))
		{
			var security = ToSecurityMessage(instrument, message.TransactionId);
			if (!security.IsMatch(message, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, TardisStreamKinds.Trades,
			default, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, TardisStreamKinds.Level1,
			default, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, TardisStreamKinds.MarketDepth,
			default, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessSubscriptionAsync(message, TardisStreamKinds.Candles,
			message.GetTimeFrame(), cancellationToken);

	private async ValueTask ProcessSubscriptionAsync(MarketDataMessage message,
		TardisStreamKinds kind, TimeSpan timeFrame,
		CancellationToken cancellationToken)
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
		if (kind == TardisStreamKinds.Candles)
			_ = timeFrame.ToBarDataType();

		var instrument = ResolveInstrument(message.SecurityId);
		var securityId = ToSecurityId(instrument);
		var key = new TardisStreamKey(instrument.Id, kind, timeFrame);
		long? remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message, instrument);
			var historyLimit = checked((int)Math.Min(
				remaining ?? HistoryLimit, HistoryLimit));
			var sent = await ReplayAsync(key, securityId, from, to, historyLimit,
				message.TransactionId, cancellationToken);
			if (remaining is not null)
				remaining = Math.Max(0, remaining.Value - sent);
		}

		if (message.IsHistoryOnly() || message.To is not null || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		await AddLiveSubscriptionAsync(message.TransactionId, securityId, key,
			remaining, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask<int> ReplayAsync(TardisStreamKey key,
		SecurityId securityId, DateTime from, DateTime to, int limit,
		long transactionId, CancellationToken cancellationToken)
	{
		var requestedFrom = from;
		var replayBook = key.Kind == TardisStreamKinds.MarketDepth
			? new ReplayOrderBook()
			: null;
		var isReplayBookStarted = false;
		if (key.Kind is TardisStreamKinds.Level1 or
			TardisStreamKinds.MarketDepth)
			from = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0,
				DateTimeKind.Utc);
		var sent = 0;
		await foreach (var update in SafeMachine().ReplayAsync(Exchange, key,
			from, to, cancellationToken))
		{
			if (update is TardisDisconnect)
			{
				this.AddWarningLog(
					"Tardis replay contains an underlying {0} exchange disconnect.",
					Exchange);
				continue;
			}
			if (update is TardisMachineError error)
				throw new InvalidOperationException(
					"Tardis Machine replay error: " +
					error.Details.IsEmpty("unspecified error"));
			ValidateNormalizedMessage(update, key);
			var time = GetMessageTime(update);
			if (time > to)
				continue;
			if (replayBook is not null)
			{
				if (update is not TardisBookChange book)
					throw new InvalidDataException(
						"Tardis returned a non-book message for depth replay.");
				if (!replayBook.Apply(book) || time < requestedFrom)
					continue;
				if (!isReplayBookStarted)
				{
					await SendReplayBookSnapshotAsync(replayBook, time, securityId,
						transactionId, cancellationToken);
					isReplayBookStarted = true;
				}
				else
				{
					await SendBookAsync(book, securityId, transactionId,
						cancellationToken);
				}
				if (++sent >= limit)
					break;
				continue;
			}
			if (time < requestedFrom)
				continue;
			if (await SendNormalizedAsync(update, key, securityId, transactionId,
				cancellationToken) && ++sent >= limit)
				break;
		}
		return sent;
	}

	private ValueTask SendReplayBookSnapshotAsync(ReplayOrderBook book,
		DateTime time, SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (!book.IsInitialized)
			throw new InvalidOperationException(
				"Tardis replay order book is not initialized.");
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
			Bids = book.GetBids(),
			Asks = book.GetAsks(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private async ValueTask AddLiveSubscriptionAsync(long transactionId,
		SecurityId securityId, TardisStreamKey key, long? remaining,
		CancellationToken cancellationToken)
	{
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			var subscription = new LiveSubscription
			{
				TransactionId = transactionId,
				SecurityId = securityId,
				Key = key,
				Remaining = remaining,
			};
			TardisMachineStreamClient stream;
			bool isFirst;
			using (_sync.EnterScope())
			{
				if (_liveSubscriptions.ContainsKey(transactionId))
					throw new InvalidOperationException(
						$"Tardis subscription {transactionId} already exists.");
				isFirst = !_streams.TryGetValue(key, out stream);
				if (isFirst)
				{
					stream = new(MachineSocketEndpoint, Exchange, key,
						StreamTimeout, ReConnectionSettings.WorkingTime,
						Math.Max(1, ReConnectionSettings.ReAttemptCount))
					{
						Parent = this,
					};
					stream.MessageReceived += OnStreamMessageAsync;
					stream.Error += SendOutErrorAsync;
					_streams.Add(key, stream);
				}
				_liveSubscriptions.Add(transactionId, subscription);
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
					_liveSubscriptions.Remove(transactionId);
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
			TardisMachineStreamClient stream = null;
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

	private async ValueTask OnStreamMessageAsync(TardisStreamUpdate update,
		CancellationToken cancellationToken)
	{
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Key == update.Key)];
		foreach (var subscription in subscriptions)
		{
			if (!IsLiveSubscriptionActive(subscription))
				continue;
			if (await SendNormalizedAsync(update.Message, update.Key,
				subscription.SecurityId, subscription.TransactionId,
				cancellationToken))
				await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private bool IsLiveSubscriptionActive(LiveSubscription subscription)
	{
		using (_sync.EnterScope())
			return _liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) && ReferenceEquals(current, subscription);
	}

	private async ValueTask ConsumeLiveItemAsync(LiveSubscription subscription,
		CancellationToken cancellationToken)
	{
		TardisMachineStreamClient stream = null;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) || !ReferenceEquals(current, subscription) ||
				current.Remaining is not > 0 || --current.Remaining != 0)
				return;
			_liveSubscriptions.Remove(current.TransactionId);
			if (!_liveSubscriptions.Values.Any(item => item.Key == current.Key))
				_streams.Remove(current.Key, out stream);
		}

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
	}

	private async ValueTask<bool> SendNormalizedAsync(
		TardisNormalizedMessage update, TardisStreamKey key,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		ValidateNormalizedMessage(update, key);
		switch (key.Kind)
		{
			case TardisStreamKinds.Trades when update is TardisTrade trade:
				await SendTradeAsync(trade, securityId, transactionId,
					cancellationToken);
				return true;
			case TardisStreamKinds.MarketDepth
				when update is TardisBookChange book:
				await SendBookAsync(book, securityId, transactionId,
					cancellationToken);
				return true;
			case TardisStreamKinds.Level1
				when update is TardisBookSnapshot snapshot:
				return await SendLevel1Async(snapshot, securityId, transactionId,
					cancellationToken);
			case TardisStreamKinds.Level1
				when update is TardisBookTicker ticker:
				return await SendLevel1Async(ticker, securityId, transactionId,
					cancellationToken);
			case TardisStreamKinds.Level1
				when update is TardisDerivativeTicker derivative:
				return await SendLevel1Async(derivative, securityId, transactionId,
					cancellationToken);
			case TardisStreamKinds.Candles when update is TardisTradeBar candle:
				await SendCandleAsync(candle, key.TimeFrame, securityId,
					transactionId, cancellationToken);
				return true;
			default:
				throw new InvalidDataException(
					$"Tardis returned {update.Type} for a {key.Kind} subscription.");
		}
	}

	private ValueTask SendTradeAsync(TardisTrade trade, SecurityId securityId,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade.Price is not > 0 || trade.Amount is not > 0)
			throw new InvalidDataException("Tardis returned an invalid trade.");
		return SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = GetMessageTime(trade),
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			OriginSide = trade.Side switch
			{
				TardisSides.Buy => Sides.Buy,
				TardisSides.Sell => Sides.Sell,
				_ => null,
			},
		}, cancellationToken);
	}

	private ValueTask SendBookAsync(TardisBookChange book,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = GetMessageTime(book),
			Bids = ConvertLevels(book.Bids, "bid", !book.IsSnapshot),
			Asks = ConvertLevels(book.Asks, "ask", !book.IsSnapshot),
			State = book.IsSnapshot
				? QuoteChangeStates.SnapshotComplete
				: QuoteChangeStates.Increment,
		}, cancellationToken);

	private async ValueTask<bool> SendLevel1Async(TardisBookSnapshot snapshot,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (!snapshot.Name.EqualsIgnoreCase("quote"))
			throw new InvalidDataException(
				"Tardis returned an unexpected book snapshot for Level 1.");
		var bid = GetBestLevel(snapshot.Bids, true);
		var ask = GetBestLevel(snapshot.Asks, false);
		if (bid is null && ask is null)
			return false;
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = GetMessageTime(snapshot),
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Amount)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Amount), cancellationToken);
		return true;
	}

	private async ValueTask<bool> SendLevel1Async(TardisBookTicker ticker,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		ValidateQuote(ticker.BidPrice, ticker.BidAmount, "bid");
		ValidateQuote(ticker.AskPrice, ticker.AskAmount, "ask");
		if (ticker.BidPrice is null && ticker.AskPrice is null)
			return false;
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = GetMessageTime(ticker),
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidAmount)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskAmount), cancellationToken);
		return true;
	}

	private async ValueTask<bool> SendLevel1Async(
		TardisDerivativeTicker ticker, SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker.LastPrice is <= 0 || ticker.IndexPrice is <= 0 ||
			ticker.MarkPrice is <= 0 || ticker.OpenInterest is < 0)
			throw new InvalidDataException(
				"Tardis returned an invalid derivative ticker.");
		_ = ticker.FundingTimestamp.TryParseTardisTime("funding");
		if (ticker.LastPrice is null && ticker.IndexPrice is null &&
			ticker.MarkPrice is null && ticker.OpenInterest is null)
			return false;
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = GetMessageTime(ticker),
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest)
		.TryAdd(Level1Fields.Index, ticker.IndexPrice)
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice), cancellationToken);
		return true;
	}

	private ValueTask SendCandleAsync(TardisTradeBar candle,
		TimeSpan timeFrame, SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		var closeTime = GetMessageTime(candle);
		var openTradeTime = candle.OpenTimestamp.ParseTardisTime(
			"bar open-trade");
		var closeTradeTime = candle.CloseTimestamp.ParseTardisTime(
			"bar close-trade");
		var openTime = closeTime - timeFrame;
		if (candle.Kind != TardisBarKinds.Time ||
			candle.Interval != checked((long)timeFrame.TotalMilliseconds) ||
			candle.Open is not > 0 || candle.High is not > 0 ||
			candle.Low is not > 0 || candle.Close is not > 0 ||
			candle.Volume is < 0 || candle.BuyVolume is < 0 ||
			candle.SellVolume is < 0 || candle.Trades is < 0 ||
			candle.Low > candle.High || candle.High < candle.Open ||
			candle.High < candle.Close || candle.Low > candle.Open ||
			candle.Low > candle.Close || openTradeTime > closeTradeTime ||
			openTradeTime < openTime || closeTradeTime > closeTime)
			throw new InvalidDataException(
				"Tardis returned an invalid time-based trade bar.");
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataType = timeFrame.TimeFrame(),
			TypedArg = timeFrame,
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open.Value,
			HighPrice = candle.High.Value,
			LowPrice = candle.Low.Value,
			ClosePrice = candle.Close.Value,
			TotalVolume = candle.Volume ?? 0,
			BuyVolume = candle.BuyVolume,
			SellVolume = candle.SellVolume,
			TotalTicks = candle.Trades is null
				? null
				: (int)Math.Min(int.MaxValue, candle.Trades.Value),
			State = CandleStates.Finished,
		}, cancellationToken);
	}

	private static QuoteChange[] ConvertLevels(TardisBookLevel[] levels,
		string side, bool isIncrement)
	{
		var result = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Price is not > 0 || level.Amount is null or < 0)
				throw new InvalidDataException(
					$"Tardis returned an invalid {side} order-book level.");
			if (isIncrement || level.Amount > 0)
				result.Add(new(level.Price.Value, level.Amount.Value));
		}
		return [.. result];
	}

	private static TardisBookLevel GetBestLevel(TardisBookLevel[] levels,
		bool isBid)
	{
		var valid = new List<TardisBookLevel>();
		foreach (var level in levels ?? [])
		{
			if (level is null || level.Price is null && level.Amount is null)
				continue;
			if (level.Price is not > 0 || level.Amount is null or < 0)
				throw new InvalidDataException(
					"Tardis returned an invalid Level 1 book level.");
			if (level.Amount > 0)
				valid.Add(level);
		}
		return isBid
			? valid.OrderByDescending(static level => level.Price).FirstOrDefault()
			: valid.OrderBy(static level => level.Price).FirstOrDefault();
	}

	private static void ValidateQuote(decimal? price, decimal? amount,
		string side)
	{
		if (price is <= 0 || amount is < 0 || price is null && amount is not null)
			throw new InvalidDataException(
				$"Tardis returned an invalid best {side} quote.");
	}

	private void ValidateNormalizedMessage(TardisNormalizedMessage message,
		TardisStreamKey key)
	{
		if (message is null || !message.Exchange.EqualsIgnoreCase(Exchange) ||
			!message.Symbol.EqualsIgnoreCase(key.Symbol))
			throw new InvalidDataException(
				"Tardis returned data for a different exchange or symbol.");
	}

	private static DateTime GetMessageTime(TardisNormalizedMessage message)
	{
		var localTime = message.LocalTimestamp.ParseTardisTime(
			"local message");
		return message.Timestamp.IsEmpty()
			? localTime
			: message.Timestamp.ParseTardisTime("exchange message");
	}

	private (DateTime From, DateTime To) GetHistoryRange(
		MarketDataMessage message, TardisInstrument instrument)
	{
		var availableSince = instrument.AvailableSince.ParseTardisTime(
			"availableSince");
		var availableTo = instrument.AvailableTo.TryParseTardisTime("availableTo");
		var to = (message.To ?? CurrentTime).EnsureUtc();
		if (availableTo is { } last && last < to)
			to = last;
		var earliest = to - DateTime.UnixEpoch < HistoryLookback
			? DateTime.UnixEpoch
			: to - HistoryLookback;
		var from = (message.From ?? earliest).EnsureUtc();
		if (from < availableSince)
			from = availableSince;
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message),
				"Tardis replay start time must be earlier than its end time and inside the instrument availability range.");
		if (to - from > MaximumReplaySpan)
			throw new ArgumentOutOfRangeException(nameof(message),
				$"Tardis replay range exceeds the configured {MaximumReplaySpan} maximum span.");
		return (from, to);
	}

	private static bool ShouldDownloadHistory(MarketDataMessage message)
		=> message.IsHistoryOnly() || message.From is not null ||
			message.To is not null;

	private static bool Matches(TardisInstrument instrument, string value)
	{
		if (value.IsEmpty())
			return true;
		return instrument.Key.ContainsIgnoreCase(value) ||
			instrument.Id.ContainsIgnoreCase(value) ||
			instrument.DatasetId.ContainsIgnoreCase(value) ||
			instrument.BaseCurrency.ContainsIgnoreCase(value) ||
			instrument.QuoteCurrency.ContainsIgnoreCase(value) ||
			instrument.UnderlyingIndex.ContainsIgnoreCase(value) ||
			instrument.AliasFor.ContainsIgnoreCase(value) ||
			instrument.Type.ToString().ContainsIgnoreCase(value) ||
			instrument.ContractType.ToString().ContainsIgnoreCase(value);
	}

	private static SecurityId ToSecurityId(TardisInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Id,
			BoardCode = BoardCodes.Tardis,
			Native = instrument.Key,
		};

	private static SecurityMessage ToSecurityMessage(
		TardisInstrument instrument, long originalTransactionId)
	{
		var priceStep = instrument.PriceIncrement is > 0
			? instrument.PriceIncrement
			: null;
		var security = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(instrument),
			Name = instrument.Id,
			ShortName = instrument.BaseCurrency.IsEmpty() ||
				instrument.QuoteCurrency.IsEmpty()
					? instrument.Id
					: instrument.BaseCurrency + "/" + instrument.QuoteCurrency,
			Class = instrument.Exchange + ":" + instrument.Type.ToWire(),
			SecurityType = instrument.Type.ToSecurityType(),
			Currency = instrument.QuoteCurrency.ToCurrency(),
			PriceStep = priceStep,
			Decimals = priceStep?.GetCachedDecimals(),
			VolumeStep = instrument.AmountIncrement is > 0
				? instrument.AmountIncrement
				: null,
			MinVolume = instrument.MinimumTradeAmount is > 0
				? instrument.MinimumTradeAmount
				: null,
			Multiplier = instrument.ContractMultiplier is > 0
				? instrument.ContractMultiplier
				: null,
			IssueDate = instrument.Listing.TryParseTardisTime("listing"),
			ExpiryDate = instrument.Expiry.TryParseTardisTime("expiry"),
			Strike = instrument.StrikePrice is > 0
				? instrument.StrikePrice
				: null,
			OptionType = instrument.OptionType.ToOptionType(),
		};
		if (!instrument.UnderlyingIndex.IsEmpty())
			security.UnderlyingSecurityId = new()
			{
				SecurityCode = instrument.UnderlyingIndex,
				BoardCode = BoardCodes.Tardis,
			};
		return security;
	}

	private async ValueTask FinishSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
