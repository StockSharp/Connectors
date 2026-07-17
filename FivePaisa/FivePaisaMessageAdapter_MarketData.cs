namespace StockSharp.FivePaisa;

public partial class FivePaisaMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _depthSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FivePaisaInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
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
				Name = instrument.FullName,
				ShortName = instrument.Name,
				PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
				VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
				Multiplier = instrument.Multiplier > 0 ? instrument.Multiplier : instrument.LotSize > 0 ? instrument.LotSize : null,
				ExpiryDate = instrument.Expiry,
				Strike = instrument.StrikeRate > 0 ? instrument.StrikeRate : null,
				OptionType = instrument.ToOptionType(),
			};

			if (!instrument.Isin.IsEmpty())
			{
				securityId.Isin = instrument.Isin;
				security.SecurityId = securityId;
			}
			if (!instrument.SymbolRoot.IsEmpty() && security.SecurityType is SecurityTypes.Future or SecurityTypes.Option)
				security.UnderlyingSecurityId = new() { SecurityCode = instrument.SymbolRoot };

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			var key = securityId.Native.ToString();
			_securityIds[key] = securityId;
			_instruments[key] = instrument;
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

	private async ValueTask ProcessNormalSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (_feedClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
				var wasEmpty = subscriptions.Count == 0;
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[instrumentKey] = mdMsg.SecurityId;
				if (wasEmpty)
					await _feedClient.Subscribe(instrumentKey, cancellationToken);
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
				await _feedClient.Unsubscribe(instrumentKey, cancellationToken);
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var depth = mdMsg.MaxDepth ?? 20;
		if (depth > 20)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth, "5paisa supports at most 20 market-depth levels.");

		var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
		instrumentKey.ToDepthInstrument();
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var client = await GetDepthClient(cancellationToken);
				_depthSubscriptions[instrumentKey] = mdMsg.TransactionId;
				_securityIds[instrumentKey] = mdMsg.SecurityId;
				await client.Subscribe(instrumentKey, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_depthSubscriptions.Remove(instrumentKey) && _depthClient != null)
		{
			await _depthClient.Unsubscribe(instrumentKey, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (!mdMsg.IsHistoryOnly())
			throw new NotSupportedException("5paisa Xstream provides historical candles only; realtime candle subscriptions are not available.");

		var candles = await _restClient.GetCandles(mdMsg.SecurityId.ToInstrumentKey(), mdMsg.GetTimeFrame(), mdMsg.From, mdMsg.To, cancellationToken);
		IEnumerable<FivePaisaCandle> ordered = candles.OrderBy(c => c.OpenTime.ToFivePaisaTime());
		if (mdMsg.Count is long count)
			ordered = ordered.TakeLast((int)Math.Min(count, int.MaxValue)).OrderBy(c => c.OpenTime.ToFivePaisaTime());

		foreach (var candle in ordered)
		{
			if (candle.OpenTime.ToFivePaisaTime() is not DateTime openTime)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = mdMsg.GetTimeFrame(),
				OpenTime = openTime,
				OpenPrice = candle.OpenPrice,
				HighPrice = candle.HighPrice,
				LowPrice = candle.LowPrice,
				ClosePrice = candle.ClosePrice,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask<FivePaisaInstrument> GetInstrument(SecurityId securityId, CancellationToken cancellationToken)
	{
		var key = securityId.ToInstrumentKey();
		if (_instruments.TryGetValue(key, out var instrument))
			return instrument;
		instrument = await _restClient.GetInstrument(key, cancellationToken);
		if (instrument != null)
			_instruments[key] = instrument;
		return instrument;
	}

	private async ValueTask OnMarketDataReceived(FivePaisaMarketUpdate update, CancellationToken cancellationToken)
	{
		var instrumentKey = update.Exchange.ToInstrumentKey(update.ExchangeType, update.Token);
		if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(instrumentKey) ?? update.Exchange.ToSecurityId(update.ExchangeType, update.Token);
		var serverTime = GetServerTime(update);
		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, NullIfZero(update.LastPrice))
			.TryAdd(Level1Fields.LastTradeVolume, NullIfZero(update.LastVolume))
			.TryAdd(Level1Fields.LastTradeTime, serverTime)
			.TryAdd(Level1Fields.Volume, NullIfZero(update.TotalVolume))
			.TryAdd(Level1Fields.AveragePrice, NullIfZero(update.AveragePrice))
			.TryAdd(Level1Fields.OpenPrice, NullIfZero(update.OpenPrice))
			.TryAdd(Level1Fields.HighPrice, NullIfZero(update.HighPrice))
			.TryAdd(Level1Fields.LowPrice, NullIfZero(update.LowPrice))
			.TryAdd(Level1Fields.ClosePrice, NullIfZero(update.PreviousClose))
			.TryAdd(Level1Fields.BestBidPrice, NullIfZero(update.BestBidPrice))
			.TryAdd(Level1Fields.BestBidVolume, NullIfZero(update.BestBidVolume))
			.TryAdd(Level1Fields.BestAskPrice, NullIfZero(update.BestAskPrice))
			.TryAdd(Level1Fields.BestAskVolume, NullIfZero(update.BestAskVolume))
			.TryAdd(Level1Fields.BidsVolume, NullIfZero(update.TotalBidVolume))
			.TryAdd(Level1Fields.AsksVolume, NullIfZero(update.TotalAskVolume)), cancellationToken);
		}

		if (update.LastPrice > 0 && update.LastVolume > 0 && subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (serverTime, update.LastPrice, update.LastVolume);
			if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
			{
				_lastTicks[instrumentKey] = trade;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = ticksId,
					SecurityId = securityId,
					ServerTime = serverTime,
					TradePrice = update.LastPrice,
					TradeVolume = update.LastVolume,
				}, cancellationToken);
			}
		}
	}

	private ValueTask OnDepthReceived(FivePaisaDepthUpdate update, CancellationToken cancellationToken)
	{
		var scripCode = update.Token > 0 ? update.Token : update.ScripCode;
		var instrumentKey = update.Exchange.ToInstrumentKey(update.ExchangeType, scripCode);
		if (!_depthSubscriptions.TryGetValue(instrumentKey, out var subscriptionId))
			return default;

		var bids = update.Details
			.Where(level => level.BuySellFlag == 66 && level.Price > 0)
			.OrderByDescending(level => level.Price)
			.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })
			.Take(20)
			.ToArray();
		var asks = update.Details
			.Where(level => level.BuySellFlag == 83 && level.Price > 0)
			.OrderBy(level => level.Price)
			.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })
			.Take(20)
			.ToArray();
		var serverTime = update.Timestamp > 1_000_000_000_000
			? update.Timestamp.FromUnixMilliseconds()
			: update.Timestamp > 1_000_000_000 ? update.Timestamp.FromUnixSeconds() : update.Time.ToFivePaisaTime() ?? CurrentTime;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = _securityIds.TryGetValue2(instrumentKey) ?? update.Exchange.ToSecurityId(update.ExchangeType, scripCode),
			ServerTime = serverTime,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private DateTime GetServerTime(FivePaisaMarketUpdate update)
	{
		if (update.TickTime.ToFivePaisaTime() is DateTime tickTime)
			return tickTime;
		if (update.Time > 1_000_000_000_000)
			return update.Time.FromUnixMilliseconds();
		if (update.Time > 1_000_000_000)
			return update.Time.FromUnixSeconds();
		if (update.Time is > 0 and < 86400)
			return update.Time.FromIndiaSecondsOfDay(CurrentTime);
		return CurrentTime;
	}

	private static decimal? NullIfZero(decimal value) => value == 0 ? null : value;
}
