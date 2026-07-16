namespace StockSharp.Ctp;

public partial class CtpMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (string tradingDay, int volume, double turnover)> _tickState = new(StringComparer.OrdinalIgnoreCase);
	private SecurityLookupMessage _securityLookup;
	private int _securityLookupRequestId;
	private long _securityLookupLeft;
	private int _nativeRequestId;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (_securityLookup != null)
			throw new InvalidOperationException("Only one CTP security lookup can be active at a time.");

		_securityLookup = lookupMsg.TypedClone();
		_securityLookupLeft = lookupMsg.Count ?? long.MaxValue;
		_securityLookupRequestId = NextNativeRequest();
		try
		{
			await SendQuery(() => _client.QueryInstruments(
				_securityLookupRequestId,
				lookupMsg.SecurityId.BoardCode,
				lookupMsg.SecurityId.SecurityCode,
				string.Empty), cancellationToken);
		}
		catch
		{
			_securityLookup = null;
			_securityLookupRequestId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketDataSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketDataSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketDataSubscription(mdMsg, DataType.Ticks, cancellationToken);

	private async ValueTask ProcessMarketDataSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var instrumentId = mdMsg.SecurityId.SecurityCode;
		if (instrumentId.IsEmpty())
			throw new ArgumentException("CTP instrument ID is not specified.", nameof(mdMsg));

		var key = CtpExtensions.GetKey(instrumentId);
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var subscriptions = _marketSubscriptions.SafeAdd(key);
				var requiresNativeSubscription = subscriptions.Count == 0;
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[key] = mdMsg.SecurityId;
				if (requiresNativeSubscription)
					_client.SubscribeMarketData(instrumentId, true);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(key, out var subscriptions))
		{
			subscriptions.Remove(dataType);
			if (subscriptions.Count == 0)
			{
				_client.SubscribeMarketData(instrumentId, false);
				_marketSubscriptions.Remove(key);
				_securityIds.Remove(key);
				_tickState.Remove(key);
			}
		}
	}

	private void OnNativeInstrument(CtpNativeInstrument? instrument, CtpNativeError? error, int requestId, bool isLast)
		=> Enqueue(cancellationToken => ProcessNativeInstrument(instrument, error, requestId, isLast, cancellationToken));

	private async ValueTask ProcessNativeInstrument(CtpNativeInstrument? native, CtpNativeError? error, int requestId, bool isLast, CancellationToken cancellationToken)
	{
		var lookup = _securityLookup;
		if (lookup == null || requestId != _securityLookupRequestId)
			return;

		if (error is { Id: not 0 } nativeError)
			await SendOutErrorAsync(nativeError.ToException("instrument query"), cancellationToken);

		if (native is { } instrument && _securityLookupLeft > 0)
		{
			var securityId = instrument.InstrumentId.ToSecurityId(instrument.ExchangeId);
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookup.TransactionId,
				SecurityId = securityId,
				Name = instrument.Name,
				ShortName = instrument.Name,
				SecurityType = instrument.ProductClass.ToSecurityType(),
				Currency = CurrencyTypes.CNY,
				PriceStep = instrument.PriceTick > 0 ? (decimal)instrument.PriceTick : null,
				VolumeStep = 1,
				MinVolume = 1,
				MaxVolume = Math.Max(instrument.MaxLimitOrderVolume, instrument.MaxMarketOrderVolume) is > 0 and var maxVolume ? maxVolume : null,
				Multiplier = instrument.VolumeMultiple > 0 ? instrument.VolumeMultiple : null,
				ExpiryDate = instrument.ExpireDate.ToCtpDate(),
				Strike = instrument.StrikePrice > 0 ? (decimal)instrument.StrikePrice : null,
				OptionType = instrument.OptionsType.ToOptionType(),
			};

			if (security.IsMatch(lookup, lookup.GetSecurityTypes()))
			{
				_securityIds[CtpExtensions.GetKey(instrument.InstrumentId)] = securityId;
				await SendOutMessageAsync(security, cancellationToken);
				_securityLookupLeft--;
			}
		}

		if (isLast)
		{
			_securityLookup = null;
			_securityLookupRequestId = 0;
			await SendSubscriptionResultAsync(lookup, cancellationToken);
		}
	}

	private void OnNativeDepth(CtpNativeDepth depth)
		=> Enqueue(cancellationToken => ProcessNativeDepth(depth, cancellationToken));

	private async ValueTask ProcessNativeDepth(CtpNativeDepth depth, CancellationToken cancellationToken)
	{
		var key = CtpExtensions.GetKey(depth.InstrumentId);
		if (!_marketSubscriptions.TryGetValue(key, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(key) ?? depth.InstrumentId.ToSecurityId(depth.ExchangeId);
		var serverTime = depth.ToCtpTime();
		var tickVolume = GetTickVolume(key, depth);

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var message = new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, Positive(depth.LastPrice))
			.TryAdd(Level1Fields.LastTradeVolume, tickVolume > 0 ? tickVolume : null)
			.TryAdd(Level1Fields.OpenPrice, Positive(depth.OpenPrice))
			.TryAdd(Level1Fields.HighPrice, Positive(depth.HighPrice))
			.TryAdd(Level1Fields.LowPrice, Positive(depth.LowPrice))
			.TryAdd(Level1Fields.ClosePrice, Positive(depth.ClosePrice))
			.TryAdd(Level1Fields.SettlementPrice, Positive(depth.SettlementPrice))
			.TryAdd(Level1Fields.MinPrice, Positive(depth.LowerLimitPrice))
			.TryAdd(Level1Fields.MaxPrice, Positive(depth.UpperLimitPrice))
			.TryAdd(Level1Fields.Volume, depth.Volume >= 0 ? depth.Volume : null)
			.TryAdd(Level1Fields.Turnover, depth.Turnover >= 0 ? (decimal?)depth.Turnover : null)
			.TryAdd(Level1Fields.OpenInterest, Positive(depth.OpenInterest))
			.TryAdd(Level1Fields.AveragePrice, Positive(depth.AveragePrice))
			.TryAdd(Level1Fields.BestBidPrice, Positive(depth.BidPrices?.FirstOrDefault() ?? 0))
			.TryAdd(Level1Fields.BestBidVolume, depth.BidVolumes?.FirstOrDefault() is > 0 and var bidVolume ? bidVolume : null)
			.TryAdd(Level1Fields.BestAskPrice, Positive(depth.AskPrices?.FirstOrDefault() ?? 0))
			.TryAdd(Level1Fields.BestAskVolume, depth.AskVolumes?.FirstOrDefault() is > 0 and var askVolume ? askVolume : null);

			await SendOutMessageAsync(message, cancellationToken);
		}

		if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = serverTime,
				Bids = [.. Enumerable.Range(0, 5).Where(index => depth.BidPrices?[index] > 0 && depth.BidVolumes?[index] > 0).Select(index => new QuoteChange((decimal)depth.BidPrices[index], depth.BidVolumes[index]))],
				Asks = [.. Enumerable.Range(0, 5).Where(index => depth.AskPrices?[index] > 0 && depth.AskVolumes?[index] > 0).Select(index => new QuoteChange((decimal)depth.AskPrices[index], depth.AskVolumes[index]))],
			}, cancellationToken);
		}

		if (tickVolume > 0 && depth.LastPrice > 0 && subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = ticksId,
				SecurityId = securityId,
				TradeStringId = $"{depth.ActionDay}{depth.UpdateTime.Replace(":", string.Empty)}{depth.UpdateMillisec:D3}-{depth.Volume}",
				TradePrice = (decimal)depth.LastPrice,
				TradeVolume = tickVolume,
				ServerTime = serverTime,
				OpenInterest = depth.OpenInterest > 0 ? (decimal)depth.OpenInterest : null,
			}, cancellationToken);
		}
	}

	private int GetTickVolume(string key, CtpNativeDepth depth)
	{
		var hasState = _tickState.TryGetValue(key, out var state);
		_tickState[key] = (depth.TradingDay, depth.Volume, depth.Turnover);
		if (!hasState || !state.tradingDay.Equals(depth.TradingDay, StringComparison.Ordinal) || depth.Volume < state.volume)
			return 0;
		return depth.Volume - state.volume;
	}

	private int NextNativeRequest() => Interlocked.Increment(ref _nativeRequestId);

	private static decimal? Positive(double value) => value > 0 ? (decimal)value : null;

	private void ClearMarketDataState()
	{
		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_tickState.Clear();
		_securityLookup = null;
		_securityLookupRequestId = 0;
		_securityLookupLeft = 0;
	}
}
