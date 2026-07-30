namespace StockSharp.Dexalot;

public partial class DexalotMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		DexalotPair[] pairs;
		using (_sync.EnterScope())
			pairs = [.. _pairs.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var pair in pairs.OrderBy(static item => item.Pair,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Dexalot))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(pair.Pair))
				continue;
			var security = CreateSecurity(pair, lookupMsg.TransactionId);
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
			}.TryAdd(Level1Fields.State,
				pair.AuctionMode == 0
					? SecurityStates.Trading
					: SecurityStates.Stoped),
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
			DexalotPair removedPair = null;
			using (_sync.EnterScope())
			{
				if (_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId, out var subscription))
					removedPair = subscription.Pair;
				RemoveDeliveriesNoLock(mdMsg.OriginalTransactionId);
			}
			if (removedPair is not null)
				await ReleasePairReferenceAsync(removedPair,
					cancellationToken);
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
				"Dexalot does not expose historical Level1 changes.");
		var pair = GetPair(mdMsg.SecurityId);
		var book = await EvmClient.GetBookAsync(_tradePairsAddress, pair, 1,
			cancellationToken);
		await SendLevel1Async(pair, book, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_level1Subscriptions.Add(mdMsg.TransactionId,
				new() { Pair = pair });
		try
		{
			await AddPairReferenceAsync(pair, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
			DexalotPair removedPair = null;
			using (_sync.EnterScope())
			{
				if (_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out var subscription))
					removedPair = subscription.Pair;
				RemoveDeliveriesNoLock(mdMsg.OriginalTransactionId);
			}
			if (removedPair is not null)
				await ReleasePairReferenceAsync(removedPair,
					cancellationToken);
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
				"Dexalot does not expose historical order-book changes.");
		var pair = GetPair(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? OrderBookDepth).Max(1)
			.Min(OrderBookDepth);
		var book = await EvmClient.GetBookAsync(_tradePairsAddress, pair,
			depth, cancellationToken);
		await SendDepthAsync(pair, book, depth, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Pair = pair,
				Depth = depth,
			});
		try
		{
			await AddPairReferenceAsync(pair, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
			DexalotPair removedPair = null;
			using (_sync.EnterScope())
			{
				if (_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out var subscription))
					removedPair = subscription.Pair;
				RemoveDeliveriesNoLock(mdMsg.OriginalTransactionId);
			}
			if (removedPair is not null)
				await ReleasePairReferenceAsync(removedPair,
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var pair = GetPair(mdMsg.SecurityId);
		var historyOnly = mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime requestedTo &&
			requestedTo.ToUniversalTime() <= DateTime.UtcNow;
		using (_sync.EnterScope())
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Pair = pair,
				From = mdMsg.From?.ToUniversalTime(),
				To = mdMsg.To?.ToUniversalTime(),
				Maximum = GetSubscriptionMaximum(mdMsg.Count),
				HistoryOnly = historyOnly,
			});
		try
		{
			await AddPairReferenceAsync(pair, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
			CandleSubscription subscription = null;
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out subscription);
				RemoveDeliveriesNoLock(mdMsg.OriginalTransactionId);
			}
			if (subscription is not null)
			{
				await ReleaseChartReferenceAsync(subscription.Pair,
					subscription.TimeFrame, cancellationToken);
				await ReleasePairReferenceAsync(subscription.Pair,
					cancellationToken);
			}
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var pair = GetPair(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToChartCode();
		var historyOnly = mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime requestedTo &&
			requestedTo.ToUniversalTime() <= DateTime.UtcNow;
		using (_sync.EnterScope())
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Pair = pair,
				TimeFrame = timeFrame,
				From = mdMsg.From?.ToUniversalTime(),
				To = mdMsg.To?.ToUniversalTime(),
				Maximum = GetSubscriptionMaximum(mdMsg.Count),
				HistoryOnly = historyOnly,
			});
		try
		{
			await AddPairReferenceAsync(pair, cancellationToken);
			try
			{
				await AddChartReferenceAsync(pair, timeFrame,
					cancellationToken);
			}
			catch
			{
				await ReleasePairReferenceAsync(pair, cancellationToken);
				throw;
			}
		}
		catch
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(DexalotPair pair,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = pair.ToStockSharp(),
			Name = pair.Pair,
			ShortName = pair.Pair,
			Class = "DEXALOT-L1-CLOB",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = pair.Quote.ToCurrency(),
			PriceStep = pair.QuoteDisplayDecimals.GetStep(),
			VolumeStep = pair.BaseDisplayDecimals.GetStep(),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(pair.Base);

	private async ValueTask DrainSocketMessagesAsync(
		CancellationToken cancellationToken)
	{
		JObject[] messages;
		using (_sync.EnterScope())
		{
			messages = [.. _socketMessages];
			_socketMessages.Clear();
		}

		foreach (var message in messages)
		{
			try
			{
				switch (message.Value<string>("type")?.Trim()
					.ToUpperInvariant())
				{
					case "ORDERBOOKS":
						await ProcessBookAsync(message, cancellationToken);
						break;
					case "LASTTRADE":
						await ProcessTradesAsync(message, cancellationToken);
						break;
					case "CHARTSNAPSHOT":
						await ProcessCandlesAsync(message, cancellationToken);
						break;
					case "ERROR":
						throw new InvalidOperationException(
							$"Dexalot WebSocket error: " +
								(message["data"]?.ToString() ??
									"request rejected"));
				}
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessBookAsync(JObject message,
		CancellationToken cancellationToken)
	{
		var pair = FindMessagePair(message);
		var book = ParseBook(pair, message["data"] as JObject);
		KeyValuePair<long, Level1Subscription>[] level1;
		KeyValuePair<long, DepthSubscription>[] depths;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions.Where(item =>
				item.Value.Pair.Pair.EqualsIgnoreCase(pair.Pair))];
			depths = [.. _depthSubscriptions.Where(item =>
				item.Value.Pair.Pair.EqualsIgnoreCase(pair.Pair))];
		}

		foreach (var target in level1)
			await SendLevel1Async(pair, book, target.Key,
				cancellationToken);

		foreach (var target in depths)
			await SendDepthAsync(pair, book, target.Value.Depth,
				target.Key, cancellationToken);
	}

	private async ValueTask ProcessTradesAsync(JObject message,
		CancellationToken cancellationToken)
	{
		var pair = FindMessagePair(message);
		var trades = ParseTrades(message["data"])
			.OrderBy(static trade => trade.Time).ToArray();
		await ProcessTradeCandlesAsync(pair, trades, cancellationToken);
		KeyValuePair<long, TickSubscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _tickSubscriptions.Where(item =>
				item.Value.Pair.Pair.EqualsIgnoreCase(pair.Pair))];
		var finished = new List<(long Id, DexalotPair Pair)>();

		foreach (var target in targets)
		{
			foreach (var trade in trades)
			{
				if (target.Value.From is DateTime from &&
					trade.Time < from ||
					target.Value.To is DateTime to && trade.Time > to)
					continue;
				if (await SendTradeAsync(pair, trade, target.Key,
					cancellationToken))
					target.Value.Delivered++;
				if (target.Value.Delivered >= target.Value.Maximum)
					break;
			}

			if (target.Value.HistoryOnly ||
				target.Value.Delivered >= target.Value.Maximum ||
				target.Value.To is DateTime end &&
				DateTime.UtcNow >= end)
				finished.Add((target.Key, target.Value.Pair));
		}

		foreach (var target in finished)
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(target.Id);
				RemoveDeliveriesNoLock(target.Id);
			}
			await ReleasePairReferenceAsync(target.Pair,
				cancellationToken);
			await SendSubscriptionFinishedAsync(target.Id,
				cancellationToken);
		}
	}

	private async ValueTask ProcessCandlesAsync(JObject message,
		CancellationToken cancellationToken)
	{
		var pair = FindMessagePair(message);
		var candles = ParseCandles(message["data"])
			.OrderBy(static candle => candle.OpenTime).ToArray();
		await DeliverCandlesAsync(pair, candles,
			InferTimeFrame(message, candles), cancellationToken);
	}

	private async ValueTask ProcessTradeCandlesAsync(DexalotPair pair,
		DexalotTrade[] trades, CancellationToken cancellationToken)
	{
		TimeSpan[] timeFrames;
		using (_sync.EnterScope())
			timeFrames = [.. _candleSubscriptions.Values
				.Where(item => item.Pair.Pair.EqualsIgnoreCase(pair.Pair))
				.Select(static item => item.TimeFrame)
				.Distinct()];

		foreach (var timeFrame in timeFrames)
			await DeliverCandlesAsync(pair,
				AggregateTrades(trades, timeFrame), timeFrame,
				cancellationToken);
	}

	private async ValueTask DeliverCandlesAsync(DexalotPair pair,
		DexalotCandle[] candles, TimeSpan? timeFrame,
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, CandleSubscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _candleSubscriptions.Where(item =>
				item.Value.Pair.Pair.EqualsIgnoreCase(pair.Pair) &&
				(timeFrame is null ||
					item.Value.TimeFrame == timeFrame.Value))];
		var finished = new List<(long Id, CandleSubscription Subscription)>();

		foreach (var target in targets)
		{
			foreach (var candle in candles)
			{
				if (target.Value.From is DateTime from &&
					candle.OpenTime < from ||
					target.Value.To is DateTime to &&
					candle.OpenTime > to)
					continue;
				if (!TryTrackDelivery(target.Key,
					GetCandleDeliveryIdentity(candle,
						target.Value.TimeFrame)))
					continue;
				await SendCandleAsync(pair, candle,
					target.Value.TimeFrame, target.Key, cancellationToken);
				target.Value.Delivered++;
				if (target.Value.Delivered >= target.Value.Maximum)
					break;
			}

			if (target.Value.HistoryOnly ||
				target.Value.Delivered >= target.Value.Maximum ||
				target.Value.To is DateTime end &&
				DateTime.UtcNow >= end)
				finished.Add((target.Key, target.Value));
		}

		foreach (var target in finished)
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(target.Id);
				RemoveDeliveriesNoLock(target.Id);
			}
			await ReleaseChartReferenceAsync(target.Subscription.Pair,
				target.Subscription.TimeFrame, cancellationToken);
			await ReleasePairReferenceAsync(target.Subscription.Pair,
				cancellationToken);
			await SendSubscriptionFinishedAsync(target.Id,
				cancellationToken);
		}
	}

	private DexalotPair FindMessagePair(JObject message)
	{
		var code = message.Value<string>("pair")
			.ThrowIfEmpty("Dexalot WebSocket pair");
		using (_sync.EnterScope())
			return _pairs.TryGetValue(code, out var pair)
				? pair
				: throw new InvalidDataException(
					$"Dexalot WebSocket returned unknown pair '{code}'.");
	}

	internal static DexalotBook ParseBook(DexalotPair pair, JObject data)
	{
		ArgumentNullException.ThrowIfNull(pair);
		ArgumentNullException.ThrowIfNull(data);
		return new()
		{
			Bids = ParseBookSide(pair, data["buyBook"]),
			Asks = ParseBookSide(pair, data["sellBook"]),
			Time = DateTime.UtcNow,
		};
	}

	private static DexalotBookLevel[] ParseBookSide(DexalotPair pair,
		JToken value)
	{
		var result = new List<DexalotBookLevel>();

		foreach (var chunk in value as JArray ?? [])
		{
			var prices = SplitWireNumbers(chunk.Value<string>("prices"));
			var volumes = SplitWireNumbers(
				chunk.Value<string>("quantities"));
			if (prices.Length != volumes.Length)
				throw new InvalidDataException(
					"Dexalot WebSocket returned mismatched book arrays.");

			for (var index = 0; index < prices.Length; index++)
			{
				var price = prices[index].ParseInteger()
					.FromBaseUnits(pair.QuoteDecimals);
				var volume = volumes[index].ParseInteger()
					.FromBaseUnits(pair.BaseDecimals);
				if (price > 0 && volume > 0)
					result.Add(new()
					{
						Price = price,
						Volume = volume,
					});
			}
		}

		return [.. result];
	}

	internal static DexalotTrade[] ParseTrades(JToken value)
	{
		var items = value switch
		{
			JArray array => array,
			JObject item => new JArray(item),
			_ => [],
		};
		var result = new List<DexalotTrade>();

		foreach (var item in items.OfType<JObject>())
		{
			var id = item["execId"]?.ToString()
				.ThrowIfEmpty("Dexalot execution identifier");
			if (!DateTime.TryParse(item.Value<string>("ts"),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal, out var time))
				throw new InvalidDataException(
					"Dexalot trade contains an invalid timestamp.");
			result.Add(new()
			{
				Id = id,
				Time = time,
				Price = item.Value<string>("price")
					.ParseDecimal("price"),
				Volume = item.Value<string>("quantity")
					.ParseDecimal("quantity"),
				Side = item["takerSide"].ToSide(),
			});
		}

		return [.. result.Where(static item =>
			item.Price > 0 && item.Volume > 0)];
	}

	internal static DexalotCandle[] AggregateTrades(
		IEnumerable<DexalotTrade> trades, TimeSpan timeFrame)
	{
		ArgumentNullException.ThrowIfNull(trades);
		if (timeFrame <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Time frame must be positive.");
		return [.. trades
			.OrderBy(static trade => trade.Time)
			.GroupBy(trade => new DateTime(
				trade.Time.Ticks -
					trade.Time.Ticks % timeFrame.Ticks,
				DateTimeKind.Utc))
			.Select(static group =>
			{
				var items = group.ToArray();
				return new DexalotCandle
				{
					OpenTime = group.Key,
					Open = items[0].Price,
					High = items.Max(static trade => trade.Price),
					Low = items.Min(static trade => trade.Price),
					Close = items[^1].Price,
					Volume = items.Sum(static trade => trade.Volume),
				};
			})
			.OrderBy(static candle => candle.OpenTime)];
	}

	internal static DexalotCandle[] ParseCandles(JToken value)
	{
		var items = value switch
		{
			JArray array => array,
			JObject item => new JArray(item),
			_ => [],
		};
		var result = new List<DexalotCandle>();

		foreach (var item in items.OfType<JObject>())
		{
			if (!DateTime.TryParse(item.Value<string>("date"),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal, out var time))
				throw new InvalidDataException(
					"Dexalot candle contains an invalid timestamp.");
			result.Add(new()
			{
				OpenTime = time,
				Open = item.Value<string>("open").ParseDecimal("open"),
				High = item.Value<string>("high").ParseDecimal("high"),
				Low = item.Value<string>("low").ParseDecimal("low"),
				Close = item.Value<string>("close").ParseDecimal("close"),
				Volume = item.Value<string>("volume")
					.ParseDecimal("volume"),
			});
		}

		return [.. result.Where(static item =>
			item.Open > 0 && item.High > 0 && item.Low > 0 &&
			item.Close > 0 && item.Volume >= 0)];
	}

	private ValueTask SendLevel1Async(DexalotPair pair, DexalotBook book,
		long target, CancellationToken cancellationToken)
	{
		var bid = book.Bids.OrderByDescending(static item => item.Price)
			.FirstOrDefault();
		var ask = book.Asks.OrderBy(static item => item.Price)
			.FirstOrDefault();
		var message = new Level1ChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
		}.TryAdd(Level1Fields.State,
			pair.AuctionMode == 0
				? SecurityStates.Trading
				: SecurityStates.Stoped);
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

	private ValueTask SendDepthAsync(DexalotPair pair, DexalotBook book,
		int depth, long target, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = book.Time,
			OriginalTransactionId = target,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = [.. book.Bids
				.OrderByDescending(static item => item.Price)
				.Take(depth)
				.Select(static item => new QuoteChange(
					item.Price, item.Volume))],
			Asks = [.. book.Asks
				.OrderBy(static item => item.Price)
				.Take(depth)
				.Select(static item => new QuoteChange(
					item.Price, item.Volume))],
		}, cancellationToken);

	private async ValueTask<bool> SendTradeAsync(DexalotPair pair,
		DexalotTrade trade, long target,
		CancellationToken cancellationToken)
	{
		if (!TryTrackDelivery(target, "T:" + trade.Id))
			return false;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = pair.ToStockSharp(),
			ServerTime = trade.Time,
			OriginalTransactionId = target,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side,
		}, cancellationToken);
		return true;
	}

	private ValueTask SendCandleAsync(DexalotPair pair,
		DexalotCandle candle, TimeSpan timeFrame, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = pair.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = target,
			State = candle.OpenTime + timeFrame <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);

	private async ValueTask AddPairReferenceAsync(DexalotPair pair,
		CancellationToken cancellationToken)
	{
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_pairReferenceCounts.TryGetValue(pair.Pair, out var count);
			_pairReferenceCounts[pair.Pair] = count + 1;
			subscribe = count == 0;
		}
		if (!subscribe)
			return;
		try
		{
			await SocketClient.SubscribePairAsync(pair, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_pairReferenceCounts.Remove(pair.Pair);
			throw;
		}
	}

	private async ValueTask ReleasePairReferenceAsync(DexalotPair pair,
		CancellationToken cancellationToken)
	{
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (!_pairReferenceCounts.TryGetValue(pair.Pair, out var count))
				return;
			if (count <= 1)
			{
				_pairReferenceCounts.Remove(pair.Pair);
				unsubscribe = true;
			}
			else
				_pairReferenceCounts[pair.Pair] = count - 1;
		}
		if (unsubscribe && _socketClient is not null)
			await SocketClient.SubscribePairAsync(pair, false,
				cancellationToken);
	}

	private async ValueTask AddChartReferenceAsync(DexalotPair pair,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var key = GetChartKey(pair, timeFrame);
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_chartReferenceCounts.TryGetValue(key, out var count);
			_chartReferenceCounts[key] = count + 1;
			subscribe = count == 0;
		}
		if (!subscribe)
			return;
		try
		{
			await SocketClient.SubscribeChartAsync(pair, timeFrame, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_chartReferenceCounts.Remove(key);
			throw;
		}
	}

	private async ValueTask ReleaseChartReferenceAsync(DexalotPair pair,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var key = GetChartKey(pair, timeFrame);
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (!_chartReferenceCounts.TryGetValue(key, out var count))
				return;
			if (count <= 1)
			{
				_chartReferenceCounts.Remove(key);
				unsubscribe = true;
			}
			else
				_chartReferenceCounts[key] = count - 1;
		}
		if (unsubscribe && _socketClient is not null)
			await SocketClient.SubscribeChartAsync(pair, timeFrame, false,
				cancellationToken);
	}

	private bool TryTrackDelivery(long target, string identity)
	{
		var key = new DeliveryKey(target, identity);
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

	private void RemoveDeliveriesNoLock(long target)
	{
		_seenMarketData.RemoveWhere(item =>
			item.SubscriptionId == target);
		var retained = _deliveryOrder.Where(_seenMarketData.Contains)
			.ToArray();
		_deliveryOrder.Clear();

		foreach (var item in retained)
			_deliveryOrder.Enqueue(item);
	}

	private static TimeSpan? InferTimeFrame(JObject message,
		DexalotCandle[] candles)
	{
		var code = message.Value<string>("chart");
		if (!code.IsEmpty())
			return code.ToUpperInvariant() switch
			{
				"M5" => TimeSpan.FromMinutes(5),
				"M15" => TimeSpan.FromMinutes(15),
				"M30" => TimeSpan.FromMinutes(30),
				"H1" => TimeSpan.FromHours(1),
				"H4" => TimeSpan.FromHours(4),
				"D1" => TimeSpan.FromDays(1),
				_ => null,
			};
		if (candles.Length < 2)
			return null;
		var difference = candles[1].OpenTime - candles[0].OpenTime;
		return AllTimeFrames.OrderBy(item =>
			Math.Abs((item - difference).Ticks)).FirstOrDefault();
	}

	private static string[] SplitWireNumbers(string value)
		=> value.IsEmpty()
			? []
			: value.Split(',', StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries);

	private static string GetChartKey(DexalotPair pair,
		TimeSpan timeFrame)
		=> $"{pair.Pair}:{timeFrame.ToChartCode()}";

	private string GetCandleDeliveryIdentity(DexalotCandle candle,
		TimeSpan timeFrame)
		=> $"C:{timeFrame.Ticks}:{candle.OpenTime.Ticks}:" +
			$"{candle.Open.ToString(CultureInfo.InvariantCulture)}:" +
			$"{candle.High.ToString(CultureInfo.InvariantCulture)}:" +
			$"{candle.Low.ToString(CultureInfo.InvariantCulture)}:" +
			$"{candle.Close.ToString(CultureInfo.InvariantCulture)}:" +
			$"{candle.Volume.ToString(CultureInfo.InvariantCulture)}:" +
			(candle.OpenTime + timeFrame <= CurrentTime ? "F" : "A");

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? int.MaxValue
			: count.Value.Min(int.MaxValue).Max(1).To<int>();

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
