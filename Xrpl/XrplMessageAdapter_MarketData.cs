namespace StockSharp.Xrpl;

public partial class XrplMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		XrplMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(
			static item => item.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Xrpl))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode))
				continue;
			var security = CreateSecurity(market,
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId);
				_level1Fingerprints.Remove(
					mdMsg.OriginalTransactionId);
			}
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"XRPL does not expose historical Level1 snapshots.");
		var market = GetMarket(mdMsg.SecurityId);
		var book = await RpcClient.GetBookAsync(market, 1,
			cancellationToken);
		await SendLevel1Async(market, book, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
			{
				_bookSubscriptions.Remove(
					mdMsg.OriginalTransactionId);
				_bookFingerprints.Remove(
					mdMsg.OriginalTransactionId);
			}
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"XRPL does not expose historical order-book snapshots.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? OrderBookDepth).Max(1)
			.Min(OrderBookDepth);
		var book = await RpcClient.GetBookAsync(market, depth,
			cancellationToken);
		await SendDepthAsync(market, book, depth, mdMsg.TransactionId,
			true, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_bookSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				Depth = depth,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveTickSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var subscription = new TickSubscription
		{
			Market = market,
			From = mdMsg.From?.ToUniversalTime(),
			To = mdMsg.To?.ToUniversalTime(),
			Maximum = GetSubscriptionMaximum(mdMsg.Count),
		};
		if (mdMsg.From is not null || mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime requestedTo &&
			requestedTo.ToUniversalTime() <= DateTime.UtcNow)
		{
			var bars = await LoadBarsAsync(market, subscription.From,
				subscription.To, subscription.Maximum, cancellationToken);
			foreach (var bar in bars)
			{
				if (!await SendTradeAsync(market, bar,
					mdMsg.TransactionId, cancellationToken))
					continue;
				subscription.Delivered++;
				if (subscription.Delivered >= subscription.Maximum)
					break;
			}
		}
		var historyOnly = mdMsg.IsHistoryOnly() ||
			subscription.Delivered >= subscription.Maximum ||
			subscription.To is DateTime end &&
			end <= DateTime.UtcNow;
		if (historyOnly)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				RemoveCandleSubscriptionNoLock(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!XrplExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"XRPL candle interval '{timeFrame}' is unsupported.");
		var subscription = new CandleSubscription
		{
			Market = market,
			TimeFrame = timeFrame,
			From = mdMsg.From?.ToUniversalTime(),
			To = mdMsg.To?.ToUniversalTime(),
			Maximum = GetSubscriptionMaximum(mdMsg.Count),
		};
		if (mdMsg.From is not null || mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime requestedTo &&
			requestedTo.ToUniversalTime() <= DateTime.UtcNow)
		{
			var bars = await LoadBarsAsync(market, subscription.From,
				subscription.To, int.MaxValue, cancellationToken);
			var candles = XrplExtensions.AggregateBars(bars, timeFrame);
			foreach (var candle in candles.Take(subscription.Maximum))
			{
				await SendCandleAsync(market, candle, timeFrame,
					mdMsg.TransactionId,
					candle.OpenTime + timeFrame <= DateTime.UtcNow
						? CandleStates.Finished
						: CandleStates.Active,
					cancellationToken);
				subscription.Delivered++;
				subscription.Current = candle.OpenTime + timeFrame >
					DateTime.UtcNow
						? candle
						: null;
			}
		}
		var historyOnly = mdMsg.IsHistoryOnly() ||
			subscription.Delivered >= subscription.Maximum ||
			subscription.To is DateTime end &&
			end <= DateTime.UtcNow;
		if (historyOnly)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(XrplMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.Base.Symbol}/{market.Quote.Symbol}",
			ShortName = market.SecurityCode,
			Class = "XRPL-DEX",
			SecurityType = SecurityTypes.CryptoCurrency,
			PriceStep = 0.000001m,
			VolumeStep = market.Base.IsXrp
				? 0.000001m
				: 0.000000000000001m,
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.Base.Symbol);

	private async ValueTask<XrplMarketBar[]> LoadBarsAsync(
		XrplMarket market, DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken)
	{
		var until = (to ?? DateTime.UtcNow).ToUniversalTime();
		var since = (from ?? until.AddHours(-1)).ToUniversalTime();
		if (since > until)
			return [];
		maximum = maximum.Max(1);
		var latest = await RpcClient.GetLedgerAsync(null,
			cancellationToken);
		var result = new List<XrplMarketBar>();
		var index = latest.Index;
		for (var scanned = 0;
			scanned < HistoryLedgerLimit && index > 0;
			scanned++, index--)
		{
			var response = await RpcClient.GetBookChangesAsync(index,
				cancellationToken);
			var ledgerIndex = response.Value<uint?>("ledger_index") ??
				index;
			var ledgerTime = response.Value<long?>("ledger_time") ??
				response.Value<long?>("close_time");
			if (ledgerTime is null)
			{
				var ledger = await RpcClient.GetLedgerAsync(index,
					cancellationToken);
				if (ledger.Time < since)
					break;
				if (ledger.Time > until)
					continue;
				ledgerTime = checked((long)(
					ledger.Time - XrplExtensions.RippleEpoch)
					.TotalSeconds);
			}
			var time = XrplExtensions.FromRippleTime(
				ledgerTime.Value);
			if (time < since)
				break;
			if (time > until)
				continue;
			foreach (var change in response["changes"]?
				.OfType<JObject>() ?? [])
			{
				var bar = XrplExtensions.ParseBookChange(market,
					change, ledgerIndex, ledgerTime.Value);
				if (bar is not null)
					result.Add(bar);
			}
			if (result.Count >= maximum)
				break;
		}
		return
		[
			.. result.OrderBy(static bar => bar.Time)
				.ThenBy(static bar => bar.LedgerIndex)
				.TakeLast(maximum)
		];
	}

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		XrplMarket[] markets;
		using (_sync.EnterScope())
			markets =
			[
				.. _bookSubscriptions.Values
					.Select(static item => item.Market)
					.Concat(_level1Subscriptions.Values.Select(
						static item => item.Market))
					.Distinct()
			];
		foreach (var market in markets)
		{
			var depth = 1;
			using (_sync.EnterScope())
				depth = _bookSubscriptions.Values.Where(item =>
						ReferenceEquals(item.Market, market))
					.Select(static item => item.Depth)
					.DefaultIfEmpty(1).Max();
			var book = await RpcClient.GetBookAsync(market, depth,
				cancellationToken);
			KeyValuePair<long, BookSubscription>[] books;
			KeyValuePair<long, Level1Subscription>[] level1;
			using (_sync.EnterScope())
			{
				books = [.. _bookSubscriptions.Where(item =>
					ReferenceEquals(item.Value.Market, market))];
				level1 = [.. _level1Subscriptions.Where(item =>
					ReferenceEquals(item.Value.Market, market))];
			}
			foreach (var target in books)
				await SendDepthAsync(market, book,
					target.Value.Depth, target.Key, false,
					cancellationToken);
			foreach (var target in level1)
				await SendLevel1Async(market, book, target.Key,
					false, cancellationToken);
		}
	}

	private async ValueTask ProcessBookChangesAsync(JArray changes,
		uint ledgerIndex, long ledgerTime,
		CancellationToken cancellationToken)
	{
		if (changes is null || ledgerIndex == 0 || ledgerTime <= 0)
			return;
		XrplMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		foreach (var market in markets)
		{
			foreach (var change in changes.OfType<JObject>())
			{
				var bar = XrplExtensions.ParseBookChange(market,
					change, ledgerIndex, ledgerTime);
				if (bar is null)
					continue;
				await DispatchBarAsync(market, bar, cancellationToken);
			}
		}
	}

	private async ValueTask DispatchBarAsync(XrplMarket market,
		XrplMarketBar bar, CancellationToken cancellationToken)
	{
		KeyValuePair<long, TickSubscription>[] ticks;
		KeyValuePair<long, CandleSubscription>[] candles;
		using (_sync.EnterScope())
		{
			ticks = [.. _tickSubscriptions.Where(item =>
				ReferenceEquals(item.Value.Market, market))];
			candles = [.. _candleSubscriptions.Where(item =>
				ReferenceEquals(item.Value.Market, market))];
		}
		foreach (var item in ticks)
		{
			var subscription = item.Value;
			if (subscription.From is DateTime from && bar.Time < from ||
				subscription.To is DateTime to && bar.Time > to)
				continue;
			var sent = await SendTradeAsync(market, bar, item.Key,
				cancellationToken);
			var finished = false;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.TryGetValue(item.Key,
					out var active))
					continue;
				if (sent)
					active.Delivered++;
				finished = active.Delivered >= active.Maximum ||
					active.To is DateTime until && bar.Time >= until;
				if (finished)
					RemoveTickSubscriptionNoLock(item.Key);
			}
			if (finished)
				await SendSubscriptionFinishedAsync(item.Key,
					cancellationToken);
		}
		foreach (var item in candles)
			await DispatchCandleBarAsync(item.Key, item.Value, bar,
				cancellationToken);
	}

	private async ValueTask DispatchCandleBarAsync(long target,
		CandleSubscription subscription, XrplMarketBar bar,
		CancellationToken cancellationToken)
	{
		if (subscription.From is DateTime from && bar.Time < from ||
			subscription.To is DateTime to && bar.Time > to)
			return;
		var openTime = new DateTime(
			bar.Time.ToUniversalTime().Ticks /
				subscription.TimeFrame.Ticks *
				subscription.TimeFrame.Ticks,
			DateTimeKind.Utc);
		var incoming = new XrplCandle
		{
			OpenTime = openTime,
			Open = bar.Open,
			High = bar.High,
			Low = bar.Low,
			Close = bar.Close,
			Volume = bar.Volume,
			Turnover = bar.Turnover,
			LedgerCount = 1,
		};
		var isNew = subscription.Current is null ||
			subscription.Current.OpenTime != openTime;
		if (subscription.Current is not null &&
			subscription.Current.OpenTime < openTime)
			await SendCandleAsync(subscription.Market,
				subscription.Current, subscription.TimeFrame, target,
				CandleStates.Finished, cancellationToken);
		if (subscription.Current is null ||
			subscription.Current.OpenTime < openTime)
			subscription.Current = incoming;
		else if (subscription.Current.OpenTime == openTime)
			subscription.Current = MergeCandle(subscription.Current,
				incoming);
		else
			return;
		await SendCandleAsync(subscription.Market,
			subscription.Current, subscription.TimeFrame, target,
			CandleStates.Active, cancellationToken);
		var finished = false;
		using (_sync.EnterScope())
		{
			if (!_candleSubscriptions.TryGetValue(target,
				out var active))
				return;
			if (isNew)
				active.Delivered++;
			finished = active.Delivered >= active.Maximum ||
				active.To is DateTime until && openTime >= until;
			if (finished)
				RemoveCandleSubscriptionNoLock(target);
		}
		if (finished)
			await SendSubscriptionFinishedAsync(target,
				cancellationToken);
	}

	private static XrplCandle MergeCandle(XrplCandle current,
		XrplCandle incoming)
		=> new()
		{
			OpenTime = current.OpenTime,
			Open = current.Open,
			High = Math.Max(current.High, incoming.High),
			Low = Math.Min(current.Low, incoming.Low),
			Close = incoming.Close,
			Volume = current.Volume + incoming.Volume,
			Turnover = current.Turnover + incoming.Turnover,
			LedgerCount = current.LedgerCount +
				incoming.LedgerCount,
		};

	private ValueTask SendLevel1Async(XrplMarket market, XrplBook book,
		long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var bid = book.Bids.FirstOrDefault();
		var ask = book.Asks.FirstOrDefault();
		var fingerprint = new Level1Fingerprint(
			bid?.Price ?? 0, ask?.Price ?? 0, book.LedgerIndex);
		using (_sync.EnterScope())
		{
			if (!isForced &&
				_level1Fingerprints.TryGetValue(target,
					out var previous) &&
				previous == fingerprint)
				return ValueTask.CompletedTask;
			_level1Fingerprints[target] = fingerprint;
		}
		var message = new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
		}.TryAdd(Level1Fields.State, SecurityStates.Trading);
		if (bid is not null)
			message
				.TryAdd(Level1Fields.BestBidPrice, bid.Price)
				.TryAdd(Level1Fields.BestBidVolume, bid.Volume);
		if (ask is not null)
			message
				.TryAdd(Level1Fields.BestAskPrice, ask.Price)
				.TryAdd(Level1Fields.BestAskVolume, ask.Volume);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendDepthAsync(XrplMarket market, XrplBook book,
		int depth, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var fingerprint = new BookFingerprint(book.LedgerIndex,
			book.Bids.FirstOrDefault()?.Price ?? 0,
			book.Asks.FirstOrDefault()?.Price ?? 0);
		using (_sync.EnterScope())
		{
			if (!isForced &&
				_bookFingerprints.TryGetValue(target,
					out var previous) &&
				previous == fingerprint)
				return ValueTask.CompletedTask;
			_bookFingerprints[target] = fingerprint;
		}
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
			State = QuoteChangeStates.SnapshotComplete,
			Bids =
			[
				.. book.Bids.Take(depth).Select(static item =>
					new QuoteChange(item.Price, item.Volume))
			],
			Asks =
			[
				.. book.Asks.Take(depth).Select(static item =>
					new QuoteChange(item.Price, item.Volume))
			],
		}, cancellationToken);
	}

	private async ValueTask<bool> SendTradeAsync(XrplMarket market,
		XrplMarketBar bar, long target,
		CancellationToken cancellationToken)
	{
		if (!TryTrackDelivery(target, "T:" + bar.Id))
			return false;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = bar.Time,
			OriginalTransactionId = target,
			TradeStringId = bar.Id,
			TradePrice = bar.Close,
			TradeVolume = bar.Volume,
		}, cancellationToken);
		return true;
	}

	private ValueTask SendCandleAsync(XrplMarket market,
		XrplCandle candle, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover,
			TotalTicks = candle.LedgerCount,
			TypedArg = timeFrame,
			OriginalTransactionId = target,
			State = state,
		}, cancellationToken);

	private bool TryTrackDelivery(long subscriptionId, string identity)
	{
		var key = new DeliveryKey(subscriptionId, identity);
		using (_sync.EnterScope())
		{
			if (!_seenMarketData.Add(key))
				return false;
			_deliveryOrder.Enqueue(key);
			while (_deliveryOrder.Count > _maximumDeliveryKeys)
				_seenMarketData.Remove(_deliveryOrder.Dequeue());
			return true;
		}
	}

	private void RemoveTickSubscriptionNoLock(long target)
	{
		_tickSubscriptions.Remove(target);
		RemoveDeliveriesNoLock(target);
	}

	private void RemoveCandleSubscriptionNoLock(long target)
	{
		_candleSubscriptions.Remove(target);
		RemoveDeliveriesNoLock(target);
	}

	private void RemoveDeliveriesNoLock(long target)
	{
		_seenMarketData.RemoveWhere(item =>
			item.SubscriptionId == target);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
