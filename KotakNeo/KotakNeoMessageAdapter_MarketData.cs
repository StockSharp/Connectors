namespace StockSharp.KotakNeo;

public partial class KotakNeoMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _indexKeys = new(StringComparer.OrdinalIgnoreCase);
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
				PriceStep = instrument.TickSize is > 0 ? instrument.TickSize : null,
				VolumeStep = instrument.LotSize is > 0 ? instrument.LotSize : null,
				Multiplier = instrument.Multiplier is > 0 ? instrument.Multiplier : instrument.LotSize,
				ExpiryDate = instrument.ExpiryDate,
				Strike = instrument.StrikePrice is > 0 ? instrument.StrikePrice : null,
				OptionType = instrument.OptionType.ToOptionType(),
			};

			if (!instrument.AssetCode.IsEmpty())
				security.UnderlyingSecurityId = new() { SecurityCode = instrument.AssetCode };
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			var key = instrument.ExchangeSegment.ToInstrumentKey(instrument.Token);
			_securityIds[key] = securityId;
			if (security.SecurityType == SecurityTypes.Index)
				_indexKeys.Add(key);
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
		=> ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
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
				var hadNormal = subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.Ticks);
				var hadDepth = subscriptions.ContainsKey(DataType.MarketDepth);
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[instrumentKey] = mdMsg.SecurityId;

				var isIndex = await IsIndex(instrumentKey, cancellationToken);
				if (dataType == DataType.MarketDepth)
				{
					if (isIndex)
						throw new InvalidOperationException("Kotak Neo does not provide market depth for index instruments.");
					if (!hadDepth)
						await _marketClient.SetSubscription(instrumentKey, KotakNeoFeedKinds.Depth, true, cancellationToken);
				}
				else if (!hadNormal)
				{
					await _marketClient.SetSubscription(instrumentKey,
						isIndex ? KotakNeoFeedKinds.Index : KotakNeoFeedKinds.Scrip, true, cancellationToken);
				}
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
		{
			var wasDepth = subscriptions.ContainsKey(DataType.MarketDepth);
			var wasNormal = subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.Ticks);
			subscriptions.Remove(dataType);
			var hasNormal = subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.Ticks);
			var hasDepth = subscriptions.ContainsKey(DataType.MarketDepth);

			if (dataType == DataType.MarketDepth && wasDepth && !hasDepth)
				await _marketClient.SetSubscription(instrumentKey, KotakNeoFeedKinds.Depth, false, cancellationToken);
			else if (dataType != DataType.MarketDepth && wasNormal && !hasNormal)
				await _marketClient.SetSubscription(instrumentKey,
					await IsIndex(instrumentKey, cancellationToken) ? KotakNeoFeedKinds.Index : KotakNeoFeedKinds.Scrip,
					false, cancellationToken);

			if (subscriptions.Count == 0)
			{
				_marketSubscriptions.Remove(instrumentKey);
				_securityIds.Remove(instrumentKey);
				_indexKeys.Remove(instrumentKey);
				_lastTicks.Remove(instrumentKey);
			}
		}
	}

	private async Task<bool> IsIndex(string instrumentKey, CancellationToken cancellationToken)
	{
		if (_indexKeys.Contains(instrumentKey))
			return true;
		var instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken);
		if (instrument?.ToSecurityType() != SecurityTypes.Index)
			return false;
		_indexKeys.Add(instrumentKey);
		return true;
	}

	private async ValueTask OnMarketUpdate(KotakNeoMarketUpdate update, CancellationToken cancellationToken)
	{
		if (update == null || update.ExchangeSegment.IsEmpty() || update.Token.IsEmpty())
			return;
		var instrumentKey = update.ExchangeSegment.ToInstrumentKey(update.Token);
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(instrumentKey)
			?? KotakNeoExtensions.CreateSecurityId(update.ExchangeSegment, update.Token, update.TradingSymbol);
		var isDepth = update.FeedType.EqualsIgnoreCase(nameof(KotakNeoFeedKinds.Depth));

		if (!isDepth && subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = update.ServerTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, update.LastPrice is > 0 ? update.LastPrice : null)
			.TryAdd(Level1Fields.LastTradeVolume, update.LastVolume is > 0 ? update.LastVolume : null)
			.TryAdd(Level1Fields.LastTradeTime, update.LastTradeTime)
			.TryAdd(Level1Fields.AveragePrice, update.AveragePrice is > 0 ? update.AveragePrice : null)
			.TryAdd(Level1Fields.Volume, update.Volume is > 0 ? update.Volume : null)
			.TryAdd(Level1Fields.OpenPrice, update.OpenPrice is > 0 ? update.OpenPrice : null)
			.TryAdd(Level1Fields.HighPrice, update.HighPrice is > 0 ? update.HighPrice : null)
			.TryAdd(Level1Fields.LowPrice, update.LowPrice is > 0 ? update.LowPrice : null)
			.TryAdd(Level1Fields.ClosePrice, update.ClosePrice is > 0 ? update.ClosePrice : null)
			.TryAdd(Level1Fields.OpenInterest, update.OpenInterest is > 0 ? update.OpenInterest : null)
			.TryAdd(Level1Fields.BidsVolume, update.TotalBuyVolume is > 0 ? update.TotalBuyVolume : null)
			.TryAdd(Level1Fields.AsksVolume, update.TotalSellVolume is > 0 ? update.TotalSellVolume : null)
			.TryAdd(Level1Fields.MinPrice, update.LowerCircuit is > 0 ? update.LowerCircuit : null)
			.TryAdd(Level1Fields.MaxPrice, update.UpperCircuit is > 0 ? update.UpperCircuit : null)
			.TryAdd(Level1Fields.BestBidPrice, update.BestBidPrice)
			.TryAdd(Level1Fields.BestBidVolume, update.BestBidVolume)
			.TryAdd(Level1Fields.BestAskPrice, update.BestAskPrice)
			.TryAdd(Level1Fields.BestAskVolume, update.BestAskVolume), cancellationToken);
		}

		if (!isDepth && update.LastTradeTime is DateTime tradeTime && update.LastPrice is > 0 && update.LastVolume is > 0 &&
			subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (tradeTime, update.LastPrice.Value, update.LastVolume.Value);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
			{
				_lastTicks[instrumentKey] = trade;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = ticksId,
					SecurityId = securityId,
					ServerTime = tradeTime,
					TradePrice = update.LastPrice,
					TradeVolume = update.LastVolume,
				}, cancellationToken);
			}
		}

		if (isDepth && subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = update.ServerTime,
				Bids = [.. update.Bids.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
				Asks = [.. update.Asks.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
			}, cancellationToken);
		}
	}
}
