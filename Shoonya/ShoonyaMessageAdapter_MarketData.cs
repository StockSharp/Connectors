namespace StockSharp.Shoonya;

public partial class ShoonyaMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, ShoonyaInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, ShoonyaMarketUpdate> _marketStates = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (DateTime time, decimal price, decimal volume)> _lastTicks = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(cancellationToken))
		{
			SecurityId securityId;
			try
			{
				securityId = instrument.ToSecurityId();
			}
			catch (ArgumentOutOfRangeException)
			{
				continue;
			}

			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = instrument.ToSecurityType(),
				Name = instrument.Symbol,
				ShortName = instrument.TradingSymbol,
				Class = instrument.Instrument,
				Currency = CurrencyTypes.INR,
				PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
				VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
				Multiplier = instrument.Multiplier > 0 ? instrument.Multiplier : null,
				ExpiryDate = instrument.Expiry,
				Strike = instrument.StrikePrice > 0 ? instrument.StrikePrice : null,
				OptionType = instrument.OptionType.ToOptionType(),
			};
			if (security.SecurityType is SecurityTypes.Future or SecurityTypes.Option && !instrument.Symbol.IsEmpty())
				security.UnderlyingSecurityId = new() { SecurityCode = instrument.Symbol };
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			var instrumentKey = instrument.Exchange.ToInstrumentKey(instrument.Token);
			_securityIds[instrumentKey] = securityId;
			_instruments[instrumentKey] = instrument;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth, "Shoonya provides five market-depth levels.");
		return ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (_socketClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.IsHistoryOnly())
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			var instrument = await GetInstrument(instrumentKey, cancellationToken);
			var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
			var wasEmpty = subscriptions.Count == 0;
			var wasDepth = subscriptions.ContainsKey(DataType.MarketDepth);
			subscriptions[dataType] = mdMsg.TransactionId;
			var isDepth = subscriptions.ContainsKey(DataType.MarketDepth);
			_securityIds[instrumentKey] = mdMsg.SecurityId;
			_instruments[instrumentKey] = instrument;
			if (wasEmpty || wasDepth != isDepth)
				await _socketClient.Subscribe(instrumentKey, isDepth, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var existing))
			return;
		var hadDepth = existing.ContainsKey(DataType.MarketDepth);
		if (existing.TryGetValue(dataType, out var subscriptionId) && subscriptionId == mdMsg.OriginalTransactionId)
			existing.Remove(dataType);
		if (existing.Count == 0)
		{
			_marketSubscriptions.Remove(instrumentKey);
			_securityIds.Remove(instrumentKey);
			_marketStates.Remove(instrumentKey);
			_lastTicks.Remove(instrumentKey);
			await _socketClient.Unsubscribe(instrumentKey, cancellationToken);
			return;
		}

		var hasDepth = existing.ContainsKey(DataType.MarketDepth);
		if (hadDepth != hasDepth)
			await _socketClient.Subscribe(instrumentKey, hasDepth, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (!mdMsg.IsHistoryOnly())
			throw new NotSupportedException("Shoonya provides historical candles only; realtime candle subscriptions are not available.");

		var instrument = await GetInstrument(mdMsg.SecurityId.ToInstrumentKey(), cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		var candles = await _restClient.GetCandles(instrument, timeFrame, mdMsg.From, mdMsg.To, cancellationToken);
		IEnumerable<ShoonyaCandle> ordered = candles
			.Where(c => c?.GetCandleTime() != null)
			.OrderBy(c => c.GetCandleTime());
		if (mdMsg.Count is long count)
			ordered = ordered.TakeLast((int)Math.Min(count, int.MaxValue)).OrderBy(c => c.GetCandleTime());

		foreach (var candle in ordered)
		{
			if (candle.GetCandleTime() is not DateTime openTime)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = openTime,
				OpenPrice = candle.Open.ToDecimal(),
				HighPrice = candle.High.ToDecimal(),
				LowPrice = candle.Low.ToDecimal(),
				ClosePrice = candle.Close.ToDecimal(),
				TotalVolume = candle.Volume.ToDecimal(),
				OpenInterest = Positive(candle.OpenInterest),
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<ShoonyaInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		if (_instruments.TryGetValue(instrumentKey, out var instrument))
			return instrument;
		instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
			?? throw new InvalidOperationException($"Shoonya instrument '{instrumentKey}' was not found in the official security master.");
		_instruments[instrumentKey] = instrument;
		return instrument;
	}

	private async ValueTask OnMarketDataReceived(ShoonyaMarketUpdate update, CancellationToken cancellationToken)
	{
		var instrumentKey = update.Exchange.ToInstrumentKey(update.Token);
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		if (!_marketStates.TryGetValue(instrumentKey, out var state))
		{
			state = new();
			_marketStates[instrumentKey] = state;
		}
		var isLastTradeUpdate = update.LastPrice != null;
		state.Apply(update);

		var securityId = _securityIds.TryGetValue2(instrumentKey)
			?? update.Exchange.ToSecurityId(update.Token, state.TradingSymbol);
		var serverTime = update.GetMarketTime();

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var bids = state.GetBids().OrderByDescending(level => level.Price).ToArray();
			var asks = state.GetAsks().OrderBy(level => level.Price).ToArray();
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, Positive(state.LastPrice))
			.TryAdd(Level1Fields.LastTradeVolume, Positive(state.LastQuantity))
			.TryAdd(Level1Fields.LastTradeTime, Positive(state.LastPrice) != null ? serverTime : null)
			.TryAdd(Level1Fields.Volume, Positive(state.Volume))
			.TryAdd(Level1Fields.AveragePrice, Positive(state.AveragePrice))
			.TryAdd(Level1Fields.OpenPrice, Positive(state.Open))
			.TryAdd(Level1Fields.HighPrice, Positive(state.High))
			.TryAdd(Level1Fields.LowPrice, Positive(state.Low))
			.TryAdd(Level1Fields.ClosePrice, Positive(state.Close))
			.TryAdd(Level1Fields.OpenInterest, Positive(state.OpenInterest))
			.TryAdd(Level1Fields.BidsVolume, Positive(state.TotalBuyQuantity))
			.TryAdd(Level1Fields.AsksVolume, Positive(state.TotalSellQuantity))
			.TryAdd(Level1Fields.MinPrice, Positive(state.LowerCircuit))
			.TryAdd(Level1Fields.MaxPrice, Positive(state.UpperCircuit))
			.TryAdd(Level1Fields.HighPrice52Week, Positive(state.YearHigh))
			.TryAdd(Level1Fields.LowPrice52Week, Positive(state.YearLow))
			.TryAdd(Level1Fields.BestBidPrice, bids.FirstOrDefault()?.Price)
			.TryAdd(Level1Fields.BestBidVolume, bids.FirstOrDefault()?.Volume)
			.TryAdd(Level1Fields.BestAskPrice, asks.FirstOrDefault()?.Price)
			.TryAdd(Level1Fields.BestAskVolume, asks.FirstOrDefault()?.Volume);
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}

		if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			var bids = state.GetBids().OrderByDescending(level => level.Price).ToArray();
			var asks = state.GetAsks().OrderBy(level => level.Price).ToArray();
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = serverTime,
				Bids = [.. bids.Select(level => new QuoteChange(level.Price, level.Volume)
				{
					OrdersCount = level.OrdersCount,
				})],
				Asks = [.. asks.Select(level => new QuoteChange(level.Price, level.Volume)
				{
					OrdersCount = level.OrdersCount,
				})],
			}, cancellationToken);
		}

		var lastPrice = state.LastPrice.ToDecimal();
		var lastVolume = state.LastQuantity.ToDecimal();
		if (isLastTradeUpdate && lastPrice > 0 && subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (serverTime, lastPrice, lastVolume);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
			{
				_lastTicks[instrumentKey] = trade;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = ticksId,
					SecurityId = securityId,
					TradeStringId = $"{instrumentKey}:{serverTime.Ticks}:{lastPrice.ToString(CultureInfo.InvariantCulture)}",
					TradePrice = lastPrice,
					TradeVolume = lastVolume > 0 ? lastVolume : null,
					ServerTime = serverTime,
				}, cancellationToken);
			}
		}
	}

	private static decimal? Positive(string value)
	{
		var parsed = value.ToDecimal();
		return parsed > 0 ? parsed : null;
	}
}
