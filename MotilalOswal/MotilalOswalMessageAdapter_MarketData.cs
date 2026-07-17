namespace StockSharp.MotilalOswal;

public partial class MotilalOswalMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, MotilalOswalInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (DateTime time, decimal price, decimal volume)> _lastTicks = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, MotilalOswalDepthLevel[]> _depthBooks = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(cancellationToken))
		{
			if (instrument == null || instrument.ScripCode <= 0 || instrument.ExchangeName.IsEmpty())
				continue;

			SecurityId securityId;
			try
			{
				securityId = instrument.ToSecurityId();
			}
			catch (ArgumentOutOfRangeException)
			{
				continue;
			}

			securityId.Isin = instrument.Isin;
			var lotSize = instrument.MarketLot > 0 ? instrument.MarketLot : 1;
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = instrument.ToSecurityType(),
				Name = instrument.FullName.IsEmpty(instrument.Name),
				ShortName = instrument.ShortName.IsEmpty(instrument.Name),
				Class = instrument.InstrumentName,
				Currency = CurrencyTypes.INR,
				PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
				VolumeStep = lotSize,
				Multiplier = lotSize,
				ExpiryDate = instrument.ExpirySeconds.ToExpiry(),
				Strike = instrument.StrikePrice > 0 ? instrument.StrikePrice : null,
				OptionType = instrument.OptionType.ToOptionType(),
				FaceValue = instrument.FaceValue > 0 ? instrument.FaceValue : null,
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			var instrumentKey = instrument.ExchangeName.ToInstrumentKey(instrument.ScripCode);
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
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth, "Motilal Oswal provides five market-depth levels.");
		return ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (_marketClient == null)
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
			var isFirst = subscriptions.Count == 0;
			subscriptions[dataType] = mdMsg.TransactionId;
			_securityIds[instrumentKey] = mdMsg.SecurityId;
			if (isFirst)
			{
				try
				{
					await _marketClient.Subscribe(instrumentKey, instrument.IsIndex, cancellationToken);
				}
				catch
				{
					subscriptions.Remove(dataType);
					if (subscriptions.Count == 0)
						_marketSubscriptions.Remove(instrumentKey);
					throw;
				}
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var existing))
			return;
		if (existing.TryGetValue(dataType, out var subscriptionId) && subscriptionId == mdMsg.OriginalTransactionId)
			existing.Remove(dataType);
		if (existing.Count != 0)
			return;

		_marketSubscriptions.Remove(instrumentKey);
		_securityIds.Remove(instrumentKey);
		_lastTicks.Remove(instrumentKey);
		_depthBooks.Remove(instrumentKey);
		await _marketClient.Unsubscribe(instrumentKey, cancellationToken);
	}

	private async Task<MotilalOswalInstrument> GetInstrument(string instrumentKey, CancellationToken cancellationToken)
	{
		if (_instruments.TryGetValue(instrumentKey, out var instrument))
			return instrument;

		instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
			?? throw new InvalidOperationException($"Motilal Oswal instrument '{instrumentKey}' was not found in the instrument master.");
		_instruments[instrumentKey] = instrument;
		return instrument;
	}

	private async ValueTask OnMarketUpdate(MotilalOswalMarketUpdate update, CancellationToken cancellationToken)
	{
		var instrumentKey = update.Exchange.ToInstrumentKey(update.ScripCode);
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(instrumentKey) ?? update.Exchange.ToSecurityId(update.ScripCode);
		if (update.MessageType == MotilalOswalMarketMessageTypes.Depth)
		{
			await ProcessDepthUpdate(instrumentKey, securityId, subscriptions, update, cancellationToken);
			return;
		}

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = update.ServerTime,
			};

			switch (update.MessageType)
			{
				case MotilalOswalMarketMessageTypes.LastTrade:
					level1
						.TryAdd(Level1Fields.LastTradePrice, Positive(update.LastPrice))
						.TryAdd(Level1Fields.LastTradeVolume, Positive(update.LastQuantity))
						.TryAdd(Level1Fields.LastTradeTime, update.LastPrice > 0 ? update.ServerTime : null)
						.TryAdd(Level1Fields.Volume, Positive(update.CumulativeQuantity))
						.TryAdd(Level1Fields.AveragePrice, Positive(update.AveragePrice))
						.TryAdd(Level1Fields.OpenInterest, Positive(update.OpenInterest));
					break;

				case MotilalOswalMarketMessageTypes.DayOhlc:
					level1
						.TryAdd(Level1Fields.OpenPrice, Positive(update.OpenPrice))
						.TryAdd(Level1Fields.HighPrice, Positive(update.HighPrice))
						.TryAdd(Level1Fields.LowPrice, Positive(update.LowPrice))
						.TryAdd(Level1Fields.ClosePrice, Positive(update.PreviousClose));
					break;

				case MotilalOswalMarketMessageTypes.CircuitLimits:
					level1
						.TryAdd(Level1Fields.MinPrice, Positive(update.LowerCircuit))
						.TryAdd(Level1Fields.MaxPrice, Positive(update.UpperCircuit));
					break;

				case MotilalOswalMarketMessageTypes.OpenInterest:
					level1.TryAdd(Level1Fields.OpenInterest, Positive(update.OpenInterest));
					break;

				case MotilalOswalMarketMessageTypes.Index:
					level1
						.TryAdd(Level1Fields.LastTradePrice, Positive(update.LastPrice))
						.TryAdd(Level1Fields.LastTradeTime, update.LastPrice > 0 ? update.ServerTime : null);
					break;
			}

			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}

		if (update.MessageType == MotilalOswalMarketMessageTypes.LastTrade &&
			update.LastPrice > 0 && update.LastQuantity > 0 &&
			subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (update.ServerTime, update.LastPrice, update.LastQuantity);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
			{
				_lastTicks[instrumentKey] = trade;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = ticksId,
					SecurityId = securityId,
					TradeStringId = $"{instrumentKey}:{update.ServerTime.Ticks}:{update.LastPrice.ToString(CultureInfo.InvariantCulture)}:{update.LastQuantity.ToString(CultureInfo.InvariantCulture)}",
					TradePrice = update.LastPrice,
					TradeVolume = update.LastQuantity,
					ServerTime = update.ServerTime,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessDepthUpdate(string instrumentKey, SecurityId securityId,
		SynchronizedDictionary<DataType, long> subscriptions, MotilalOswalMarketUpdate update,
		CancellationToken cancellationToken)
	{
		if (update.DepthLevel is < 1 or > 5)
			return;

		if (!_depthBooks.TryGetValue(instrumentKey, out var levels))
		{
			levels = Enumerable.Range(0, 5).Select(_ => new MotilalOswalDepthLevel()).ToArray();
			_depthBooks[instrumentKey] = levels;
		}

		var level = levels[update.DepthLevel - 1];
		level.BidPrice = update.BidPrice;
		level.BidQuantity = update.BidQuantity;
		level.BidOrders = update.BidOrders;
		level.AskPrice = update.AskPrice;
		level.AskQuantity = update.AskQuantity;
		level.AskOrders = update.AskOrders;

		if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = update.ServerTime,
				Bids = [.. levels.Where(l => l.BidPrice > 0).OrderByDescending(l => l.BidPrice)
					.Select(l => new QuoteChange(l.BidPrice, l.BidQuantity) { OrdersCount = l.BidOrders })],
				Asks = [.. levels.Where(l => l.AskPrice > 0).OrderBy(l => l.AskPrice)
					.Select(l => new QuoteChange(l.AskPrice, l.AskQuantity) { OrdersCount = l.AskOrders })],
			}, cancellationToken);
		}

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var bestBid = levels.Where(l => l.BidPrice > 0).OrderByDescending(l => l.BidPrice).FirstOrDefault();
			var bestAsk = levels.Where(l => l.AskPrice > 0).OrderBy(l => l.AskPrice).FirstOrDefault();
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = update.ServerTime,
			}
			.TryAdd(Level1Fields.BestBidPrice, bestBid?.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, bestBid?.BidQuantity)
			.TryAdd(Level1Fields.BestAskPrice, bestAsk?.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, bestAsk?.AskQuantity), cancellationToken);
		}
	}

	private static decimal? Positive(decimal value) => value > 0 ? value : null;
}
