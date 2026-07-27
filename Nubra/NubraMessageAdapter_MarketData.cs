namespace StockSharp.Nubra;

public partial class NubraMessageAdapter
{
	private readonly SynchronizedDictionary<
		long,
		SynchronizedDictionary<DataType, long>> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<long, int> _depths = [];
	private readonly SynchronizedDictionary<long, SecurityId> _securityIds = [];
	private readonly SynchronizedDictionary<long, NubraInstrument> _instruments = [];
	private readonly SynchronizedDictionary<
		long,
		(DateTime time, long price, long quantity)> _lastTicks = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(
			_referenceDate,
			cancellationToken))
		{
			SecurityId securityId;
			try
			{
				securityId = instrument.ToSecurityId();
			}
			catch (ArgumentException)
			{
				continue;
			}

			var securityType = instrument.ToSecurityType();
			var lotSize = instrument.LotSize > 0
				? instrument.LotSize
				: 1m;
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = securityType,
				Name = instrument.Asset
					.IsEmpty(instrument.StockName)
					.IsEmpty(instrument.NubraName),
				ShortName = instrument.StockName
					.IsEmpty(instrument.Asset),
				Class = instrument.AssetType
					.IsEmpty(instrument.DerivativeType)
					.IsEmpty(instrument.Series),
				Currency = CurrencyTypes.INR,
				PriceStep = instrument.TickSize > 0
					? instrument.TickSize.ToPrice()
					: null,
				VolumeStep = lotSize,
				Multiplier = lotSize,
				ExpiryDate = instrument.Expiry.ToExpiry(),
				Strike = instrument.StrikePrice > 0
					? instrument.StrikePrice.ToPrice()
					: null,
				OptionType = instrument.OptionType.ToOptionType(),
			};
			if (securityType is SecurityTypes.Future or
				SecurityTypes.Option &&
				!instrument.Asset.IsEmpty())
			{
				security.UnderlyingSecurityId = new()
				{
					SecurityCode = instrument.Asset,
				};
			}
			if (!instrument.Isin.IsEmpty())
				security.SecurityId = security.SecurityId with
				{
					Isin = instrument.Isin,
				};
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			RememberInstrument(instrument, security.SecurityId);
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(
			mdMsg,
			DataType.Level1,
			null,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(
			mdMsg,
			DataType.Ticks,
			null,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 20;
		if (depth is < 1 or > 20)
		{
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.MaxDepth),
				depth,
				"Nubra provides from one to twenty market-depth levels.");
		}

		return ProcessRealtimeSubscription(
			mdMsg,
			DataType.MarketDepth,
			depth,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (!mdMsg.IsHistoryOnly())
		{
			throw new NotSupportedException(
				"Nubra candle subscriptions are historical; use Level1 or market depth for realtime data.");
		}

		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = (mdMsg.From ??
			(timeFrame < TimeSpan.FromDays(1)
				? to.AddDays(-1)
				: to.AddYears(-1))).ToUniversalTime();
		var candles = await _restClient.GetCandles(
			instrument,
			timeFrame,
			from,
			to,
			cancellationToken);
		IEnumerable<NubraCandle> selected = candles;
		if (mdMsg.Count is > 0 and var count)
			selected = selected.TakeLast((int)Math.Min(count, int.MaxValue));

		foreach (var candle in selected)
		{
			await SendOutMessageAsync(
				new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = mdMsg.SecurityId,
					TypedArg = timeFrame,
					OpenTime = candle.Timestamp.ToNubraTime(CurrentTime),
					OpenPrice = candle.Open.ToPrice(),
					HighPrice = candle.High.ToPrice(),
					LowPrice = candle.Low.ToPrice(),
					ClosePrice = candle.Close.ToPrice(),
					TotalVolume = Math.Max(0, candle.Volume),
					State = CandleStates.Finished,
				},
				cancellationToken);
		}

		await SendSubscriptionFinishedAsync(
			mdMsg.TransactionId,
			cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(
		MarketDataMessage mdMsg,
		DataType dataType,
		int? depth,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveRealtimeSubscription(
				mdMsg,
				dataType,
				cancellationToken);
			return;
		}

		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		RememberInstrument(instrument, mdMsg.SecurityId);
		var snapshot = await _restClient.GetMarketUpdate(
			instrument.RefId,
			depth ?? 20,
			cancellationToken);

		var subscriptions = _marketSubscriptions.SafeAdd(instrument.RefId);
		var first = subscriptions.Count == 0;
		subscriptions[dataType] = mdMsg.TransactionId;
		if (depth != null)
			_depths[instrument.RefId] = depth.Value;
		await ProcessMarketUpdate(
			snapshot,
			dataType,
			mdMsg.TransactionId,
			cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			subscriptions.Remove(dataType);
			if (subscriptions.Count == 0)
				_marketSubscriptions.Remove(instrument.RefId);
			await SendSubscriptionFinishedAsync(
				mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		if (_marketClient == null)
		{
			throw new InvalidOperationException(
				"Nubra market-data WebSocket is not connected.");
		}
		if (first)
			await _marketClient.Subscribe(instrument.RefId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RemoveRealtimeSubscription(
		MarketDataMessage mdMsg,
		DataType dataType,
		CancellationToken cancellationToken)
	{
		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		if (!_marketSubscriptions.TryGetValue(
			instrument.RefId,
			out var subscriptions))
			return;
		if (subscriptions.TryGetValue(dataType, out var transactionId) &&
			transactionId == mdMsg.OriginalTransactionId)
			subscriptions.Remove(dataType);
		if (dataType == DataType.MarketDepth)
			_depths.Remove(instrument.RefId);
		if (subscriptions.Count != 0)
			return;

		_marketSubscriptions.Remove(instrument.RefId);
		_lastTicks.Remove(instrument.RefId);
		if (_marketClient != null)
		{
			await _marketClient.Unsubscribe(
				instrument.RefId,
				cancellationToken);
		}
	}

	private async ValueTask OnMarketDataReceived(
		NubraMarketUpdate update,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(
			update.RefId,
			out var subscriptions))
			return;

		foreach (var subscription in subscriptions.ToArray())
		{
			await ProcessMarketUpdate(
				update,
				subscription.Key,
				subscription.Value,
				cancellationToken);
		}
	}

	private async ValueTask ProcessMarketUpdate(
		NubraMarketUpdate update,
		DataType dataType,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (update == null)
			return;
		var securityId = _securityIds.TryGetValue2(update.RefId) ??
			new SecurityId
			{
				SecurityCode = update.RefId.ToString(
					CultureInfo.InvariantCulture),
				Native = update.RefId.ToString(
					CultureInfo.InvariantCulture),
			};
		var serverTime = update.Timestamp.ToNubraTime(CurrentTime);

		if (dataType == DataType.Level1)
		{
			var bestBid = update.Bids.FirstOrDefault();
			var bestAsk = update.Asks.FirstOrDefault();
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(
				Level1Fields.LastTradePrice,
				update.LastPrice > 0
					? update.LastPrice.ToPrice()
					: null)
			.TryAdd(
				Level1Fields.LastTradeVolume,
				update.LastQuantity > 0
					? update.LastQuantity
					: null)
			.TryAdd(
				Level1Fields.LastTradeTime,
				update.LastPrice > 0 ? serverTime : null)
			.TryAdd(
				Level1Fields.Volume,
				update.Volume > 0 ? update.Volume : null)
			.TryAdd(
				Level1Fields.BestBidPrice,
				bestBid?.Price > 0
					? bestBid.Price.ToPrice()
					: null)
			.TryAdd(
				Level1Fields.BestBidVolume,
				bestBid?.Quantity > 0
					? bestBid.Quantity
					: null)
			.TryAdd(
				Level1Fields.BestAskPrice,
				bestAsk?.Price > 0
					? bestAsk.Price.ToPrice()
					: null)
			.TryAdd(
				Level1Fields.BestAskVolume,
				bestAsk?.Quantity > 0
					? bestAsk.Quantity
					: null);
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
			return;
		}

		if (dataType == DataType.Ticks)
		{
			if (update.LastPrice <= 0)
				return;
			var trade = (
				serverTime,
				update.LastPrice,
				update.LastQuantity);
			if (_lastTicks.TryGetValue(update.RefId, out var previous) &&
				previous == trade)
				return;
			_lastTicks[update.RefId] = trade;
			await SendOutMessageAsync(
				new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					TradeStringId =
						$"{update.RefId}:{update.Timestamp}:{update.LastPrice}:{update.LastQuantity}",
					TradePrice = update.LastPrice.ToPrice(),
					TradeVolume = update.LastQuantity > 0
						? update.LastQuantity
						: null,
					ServerTime = serverTime,
				},
				cancellationToken);
			return;
		}

		if (dataType != DataType.MarketDepth)
			return;

		var depth = _depths.TryGetValue2(update.RefId) ?? 20;
		if (depth <= 0)
			depth = 20;
		await SendOutMessageAsync(
			new QuoteChangeMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = securityId,
				ServerTime = serverTime,
				Bids =
				[
					..
						update.Bids
							.Where(level => level.Price > 0)
							.Take(depth)
							.Select(level => new QuoteChange(
								level.Price.ToPrice(),
								level.Quantity)
							{
								OrdersCount = checked((int)Math.Min(
									level.Orders,
									int.MaxValue)),
							})
				],
				Asks =
				[
					..
						update.Asks
							.Where(level => level.Price > 0)
							.Take(depth)
							.Select(level => new QuoteChange(
								level.Price.ToPrice(),
								level.Quantity)
							{
								OrdersCount = checked((int)Math.Min(
									level.Orders,
									int.MaxValue)),
							})
				],
			},
			cancellationToken);
	}

	private async Task<NubraInstrument> ResolveInstrument(
		SecurityId securityId,
		CancellationToken cancellationToken)
	{
		if (long.TryParse(
			securityId.Native?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var refId))
		{
			if (_instruments.TryGetValue(refId, out var cached))
				return cached;
			var byId = await _restClient.GetInstrument(
				refId,
				_referenceDate,
				cancellationToken);
			if (byId != null)
				return byId;
		}

		var instrument = await _restClient.FindInstrument(
			securityId.BoardCode,
			securityId.SecurityCode,
			_referenceDate,
			cancellationToken);
		return instrument ??
			throw new InvalidOperationException(
				$"Nubra instrument '{securityId}' was not found in the authenticated reference master.");
	}

	private void RememberInstrument(
		NubraInstrument instrument,
		SecurityId securityId)
	{
		_instruments[instrument.RefId] = instrument;
		_securityIds[instrument.RefId] = securityId;
	}
}
