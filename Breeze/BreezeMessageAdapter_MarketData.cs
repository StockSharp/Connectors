namespace StockSharp.Breeze;

public partial class BreezeMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<TimeSpan, long>> _candleSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, BreezeInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (DateTime time, decimal price, decimal volume)> _lastTicks = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(cancellationToken))
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.ToSecurityId(),
				SecurityType = instrument.ToSecurityType(),
				Name = instrument.Name,
				ShortName = instrument.StockCode,
				PriceStep = instrument.PriceStep > 0 ? instrument.PriceStep : null,
				VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
				Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
				ExpiryDate = instrument.ExpiryDate,
				Strike = instrument.StrikePrice,
				OptionType = instrument.OptionType,
			};
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			_securityIds[instrument.Token] = security.SecurityId;
			_instruments[instrument.Token] = instrument;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if ((mdMsg.MaxDepth ?? 5) > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), mdMsg.MaxDepth, "Breeze provides five market-depth levels.");
		return ProcessMarketSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (_marketClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var instrument = await _restClient.GetInstrument(mdMsg.SecurityId, cancellationToken);
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				EnsureSubscriptionLimit(instrument.Token, false);
				var subscriptions = _marketSubscriptions.SafeAdd(instrument.Token);
				subscriptions[dataType] = mdMsg.TransactionId;
				CacheInstrument(instrument, mdMsg.SecurityId);
				await UpdateMarketFeed(instrument, subscriptions, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(instrument.Token, out var subscriptions))
		{
			subscriptions.Remove(dataType);
			if (subscriptions.Count == 0)
			{
				_marketSubscriptions.Remove(instrument.Token);
				_lastTicks.Remove(instrument.Token);
			}
			await UpdateMarketFeed(instrument, subscriptions, cancellationToken);
		}
	}

	private ValueTask UpdateMarketFeed(BreezeInstrument instrument, SynchronizedDictionary<DataType, long> subscriptions, CancellationToken cancellationToken)
		=> _marketClient.SetSubscription(instrument,
			subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.Ticks),
			subscriptions.ContainsKey(DataType.MarketDepth), cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var instrument = await _restClient.GetInstrument(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!mdMsg.IsSubscribe)
		{
			if (_candleSubscriptions.TryGetValue(instrument.Token, out var existing))
			{
				existing.Remove(timeFrame);
				if (existing.Count == 0)
				{
					_candleSubscriptions.Remove(instrument.Token);
					if (_ohlcClient != null)
						await _ohlcClient.Unsubscribe(instrument, cancellationToken);
				}
			}
			return;
		}

		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			IEnumerable<BreezeCandle> candles = await _restClient.GetCandles(instrument, timeFrame, mdMsg.From, mdMsg.To, cancellationToken);
			if (mdMsg.Count is long count)
				candles = candles.TakeLast((int)Math.Min(count, int.MaxValue));
			foreach (var candle in candles)
			{
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = mdMsg.SecurityId,
					TypedArg = timeFrame,
					OpenTime = candle.Time,
					OpenPrice = candle.Open,
					HighPrice = candle.High,
					LowPrice = candle.Low,
					ClosePrice = candle.Close,
					TotalVolume = candle.Volume,
					State = CandleStates.Finished,
				}, cancellationToken);
			}
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (timeFrame == TimeSpan.FromDays(1))
			throw new NotSupportedException("Breeze provides daily candles through history only.");

		EnsureSubscriptionLimit(instrument.Token, true);
		var subscriptions = _candleSubscriptions.SafeAdd(instrument.Token);
		var first = subscriptions.Count == 0;
		subscriptions[timeFrame] = mdMsg.TransactionId;
		CacheInstrument(instrument, mdMsg.SecurityId);
		if (first)
			await (await GetOhlcClient(cancellationToken)).Subscribe(instrument, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnTickReceived(BreezeMarketTick tick, CancellationToken cancellationToken)
	{
		if (tick?.InstrumentToken.IsEmpty() != false || !_marketSubscriptions.TryGetValue(tick.InstrumentToken, out var subscriptions))
			return;
		var securityId = _securityIds.TryGetValue2(tick.InstrumentToken) ?? default;
		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = tick.ServerTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, tick.LastPrice)
			.TryAdd(Level1Fields.LastTradeVolume, tick.LastVolume)
			.TryAdd(Level1Fields.LastTradeTime, tick.LastTradeTime)
			.TryAdd(Level1Fields.Volume, tick.Volume)
			.TryAdd(Level1Fields.BestBidPrice, tick.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, tick.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, tick.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, tick.AskVolume)
			.TryAdd(Level1Fields.BidsVolume, tick.TotalBuyVolume)
			.TryAdd(Level1Fields.AsksVolume, tick.TotalSellVolume)
			.TryAdd(Level1Fields.AveragePrice, tick.AveragePrice)
			.TryAdd(Level1Fields.OpenInterest, tick.OpenInterest)
			.TryAdd(Level1Fields.OpenPrice, tick.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, tick.HighPrice)
			.TryAdd(Level1Fields.LowPrice, tick.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, tick.ClosePrice)
			.TryAdd(Level1Fields.MinPrice, tick.LowerCircuit)
			.TryAdd(Level1Fields.MaxPrice, tick.UpperCircuit), cancellationToken);
		}

		if (tick.LastTradeTime is DateTime tradeTime && tick.LastPrice is decimal price && tick.LastVolume is decimal volume && volume > 0 &&
			subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (tradeTime, price, volume);
			if (!_lastTicks.TryGetValue(tick.InstrumentToken, out var previous) || previous != trade)
			{
				_lastTicks[tick.InstrumentToken] = trade;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = ticksId,
					SecurityId = securityId,
					ServerTime = tradeTime,
					TradePrice = price,
					TradeVolume = volume,
				}, cancellationToken);
			}
		}
	}

	private ValueTask OnDepthReceived(BreezeDepthUpdate update, CancellationToken cancellationToken)
	{
		if (update == null || !_marketSubscriptions.TryGetValue(update.InstrumentToken, out var subscriptions) ||
			!subscriptions.TryGetValue(DataType.MarketDepth, out var subscriptionId))
			return default;
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = _securityIds.TryGetValue2(update.InstrumentToken) ?? default,
			ServerTime = update.ServerTime,
			Bids = [.. update.Bids.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
			Asks = [.. update.Asks.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
		}, cancellationToken);
	}

	private ValueTask OnCandleReceived(BreezeStreamCandle candle, CancellationToken cancellationToken)
	{
		if (candle == null)
			return default;
		var timeFrame = candle.Event switch
		{
			"1SEC" => TimeSpan.FromSeconds(1),
			"1MIN" => TimeSpan.FromMinutes(1),
			"5MIN" => TimeSpan.FromMinutes(5),
			"30MIN" => TimeSpan.FromMinutes(30),
			_ => default,
		};
		if (timeFrame == default)
			return default;
		foreach (var pair in _candleSubscriptions.ToArray())
		{
			if (!pair.Value.TryGetValue(timeFrame, out var subscriptionId) || !_instruments.TryGetValue(pair.Key, out var instrument) || !Matches(candle, instrument))
				continue;
			return SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = subscriptionId,
				SecurityId = _securityIds.TryGetValue2(pair.Key) ?? default,
				TypedArg = timeFrame,
				OpenTime = candle.Time,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Active,
			}, cancellationToken);
		}
		return default;
	}

	private static bool Matches(BreezeStreamCandle candle, BreezeInstrument instrument)
	{
		if (!candle.StockCode.EqualsIgnoreCase(instrument.StockCode))
			return false;
		if (instrument.Kind == BreezeInstrumentKinds.Equity)
			return true;
		if (candle.ExpiryDate.ParseBreezeTime()?.Date != instrument.ExpiryDate?.Date)
			return false;
		if (instrument.Kind == BreezeInstrumentKinds.Future)
			return true;
		return candle.StrikePrice == instrument.StrikePrice &&
			(candle.Right.EqualsIgnoreCase("call") || candle.Right.EqualsIgnoreCase("ce") ? OptionTypes.Call : OptionTypes.Put) == instrument.OptionType;
	}

	private void EnsureSubscriptionLimit(string token, bool candle)
	{
		var alreadyExists = candle ? _candleSubscriptions.ContainsKey(token) : _marketSubscriptions.ContainsKey(token);
		if (!alreadyExists && _marketSubscriptions.Keys.Concat(_candleSubscriptions.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2000)
			throw new InvalidOperationException("Breeze allows at most 2000 instruments across live and OHLC subscriptions.");
	}

	private void CacheInstrument(BreezeInstrument instrument, SecurityId securityId)
	{
		_instruments[instrument.Token] = instrument;
		_securityIds[instrument.Token] = securityId;
	}
}
