namespace StockSharp.Dhan;

public partial class DhanMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (long id, int depth)> _depthSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _instrumentTypes = new(StringComparer.OrdinalIgnoreCase);
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
				Name = instrument.SymbolName,
				ShortName = instrument.DisplayName,
				PriceStep = instrument.TickSize is > 0 ? instrument.TickSize : null,
				VolumeStep = instrument.LotSize is > 0 ? instrument.LotSize : null,
				Multiplier = instrument.LotSize is > 0 ? instrument.LotSize : null,
				ExpiryDate = instrument.ExpiryDate,
				Strike = instrument.StrikePrice is > 0 ? instrument.StrikePrice : null,
				OptionType = instrument.OptionType.ToOptionType(),
			};

			if (!instrument.UnderlyingSymbol.IsEmpty())
				security.UnderlyingSecurityId = new() { SecurityCode = instrument.UnderlyingSymbol };

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			var key = securityId.Native.ToString();
			_securityIds[key] = securityId;
			_instrumentTypes[key] = instrument.Instrument;
			await SendOutMessageAsync(security, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessNormalSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessNormalSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var requestedDepth = mdMsg.MaxDepth ?? 5;
		if (requestedDepth <= 5)
			return ProcessNormalSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
		if (requestedDepth <= 20)
			return ProcessExtendedDepthSubscription(mdMsg, 20, cancellationToken);
		if (requestedDepth <= 200)
			return ProcessExtendedDepthSubscription(mdMsg, 200, cancellationToken);
		throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), requestedDepth, "Dhan supports 5, 20, or 200 market-depth levels.");
	}

	private async ValueTask ProcessNormalSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (_marketClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[instrumentKey] = mdMsg.SecurityId;
				await UpdateNormalFeed(instrumentKey, subscriptions, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
		{
			subscriptions.Remove(dataType);
			if (subscriptions.Count == 0)
			{
				_marketSubscriptions.Remove(instrumentKey);
				_lastTicks.Remove(instrumentKey);
			}
			await UpdateNormalFeed(instrumentKey, subscriptions, cancellationToken);
		}
	}

	private ValueTask UpdateNormalFeed(string instrumentKey, SynchronizedDictionary<DataType, long> subscriptions, CancellationToken cancellationToken)
	{
		DhanFeedModes? mode = subscriptions.Count == 0
			? null
			: subscriptions.ContainsKey(DataType.MarketDepth) ? DhanFeedModes.Full : DhanFeedModes.Quote;
		return _marketClient.SetSubscription(instrumentKey, mode, cancellationToken);
	}

	private async ValueTask ProcessExtendedDepthSubscription(MarketDataMessage mdMsg, int depth, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				if (_depthSubscriptions.TryGetValue(instrumentKey, out var previous) && previous.depth != depth)
				{
					var previousClient = previous.depth == 20 ? _depth20Client : _depth200Client;
					if (previousClient != null)
						await previousClient.Unsubscribe(instrumentKey, cancellationToken);
				}

				var client = await GetDepthClient(depth, cancellationToken);
				_depthSubscriptions[instrumentKey] = (mdMsg.TransactionId, depth);
				_securityIds[instrumentKey] = mdMsg.SecurityId;
				await client.Subscribe(instrumentKey, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_depthSubscriptions.Remove(instrumentKey, out var subscription))
		{
			var client = subscription.depth == 20 ? _depth20Client : _depth200Client;
			if (client != null)
				await client.Unsubscribe(instrumentKey, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
		if (!_instrumentTypes.TryGetValue(instrumentKey, out var instrumentType) || instrumentType.IsEmpty())
		{
			var instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
				?? throw new InvalidOperationException($"Dhan instrument '{instrumentKey}' was not found in the instrument master.");
			instrumentType = instrument.Instrument;
			_instrumentTypes[instrumentKey] = instrumentType;
		}

		var candles = await _restClient.GetCandles(instrumentKey, instrumentType, mdMsg.GetTimeFrame(), mdMsg.From, mdMsg.To, cancellationToken);
		IEnumerable<DhanCandle> ordered = candles.OrderBy(c => c.Time);
		if (mdMsg.Count is long count)
			ordered = ordered.TakeLast((int)Math.Min(count, int.MaxValue)).OrderBy(c => c.Time);

		foreach (var candle in ordered)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = mdMsg.GetTimeFrame(),
				OpenTime = candle.Time,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				OpenInterest = candle.OpenInterest,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask OnTickReceived(DhanMarketTick tick, CancellationToken cancellationToken)
	{
		var instrumentKey = tick.ExchangeSegment.ToBoardCode().ToInstrumentKey(tick.SecurityId);
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(instrumentKey) ?? tick.ExchangeSegment.ToSecurityId(tick.SecurityId);
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
			.TryAdd(Level1Fields.AveragePrice, tick.AveragePrice)
			.TryAdd(Level1Fields.Volume, tick.Volume)
			.TryAdd(Level1Fields.OpenPrice, tick.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, tick.HighPrice)
			.TryAdd(Level1Fields.LowPrice, tick.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, tick.ClosePrice)
			.TryAdd(Level1Fields.OpenInterest, tick.OpenInterest)
			.TryAdd(Level1Fields.BidsVolume, tick.TotalBuyVolume)
			.TryAdd(Level1Fields.AsksVolume, tick.TotalSellVolume)
			.TryAdd(Level1Fields.BestBidPrice, tick.Bids.FirstOrDefault()?.Price)
			.TryAdd(Level1Fields.BestBidVolume, tick.Bids.FirstOrDefault()?.Volume)
			.TryAdd(Level1Fields.BestAskPrice, tick.Asks.FirstOrDefault()?.Price)
			.TryAdd(Level1Fields.BestAskVolume, tick.Asks.FirstOrDefault()?.Volume), cancellationToken);
		}

		if (tick.LastTradeTime is DateTime tradeTime && tick.LastPrice is decimal price && tick.LastVolume is decimal volume && volume > 0 &&
			subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (tradeTime, price, volume);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
			{
				_lastTicks[instrumentKey] = trade;
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

		if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId) && (tick.Bids.Length > 0 || tick.Asks.Length > 0))
			await SendDepth(depthId, securityId, tick.ServerTime, tick.Bids, tick.Asks, cancellationToken);
	}

	private ValueTask OnDepthReceived(DhanDepthUpdate update, CancellationToken cancellationToken)
	{
		var instrumentKey = update.ExchangeSegment.ToBoardCode().ToInstrumentKey(update.SecurityId);
		if (!_depthSubscriptions.TryGetValue(instrumentKey, out var subscription))
			return default;

		var securityId = _securityIds.TryGetValue2(instrumentKey) ?? update.ExchangeSegment.ToSecurityId(update.SecurityId);
		return SendDepth(subscription.id, securityId, CurrentTime, update.Bids, update.Asks, cancellationToken);
	}

	private ValueTask SendDepth(long subscriptionId, SecurityId securityId, DateTime serverTime,
		DhanDepthLevel[] bids, DhanDepthLevel[] asks, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			Bids = [.. bids.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
			Asks = [.. asks.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
		}, cancellationToken);
}
