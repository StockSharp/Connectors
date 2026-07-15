namespace StockSharp.Fyers;

public partial class FyersMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _depth50Subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FyersInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
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
				Name = instrument.Name,
				ShortName = instrument.ShortName,
				PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
				VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
				Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
				ExpiryDate = instrument.ExpiryDate,
				Strike = instrument.Strike,
				OptionType = instrument.OptionType.ToOptionType(),
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			_securityIds[instrument.Symbol] = securityId;
			_instruments[instrument.Symbol] = instrument;
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
		if (requestedDepth <= 50)
			return ProcessTbtSubscription(mdMsg, cancellationToken);
		throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), requestedDepth, "FYERS supports 5 or 50 market-depth levels.");
	}

	private async ValueTask ProcessNormalSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (_marketClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var symbol = mdMsg.SecurityId.ToFyersSymbol();
		var instrument = await GetInstrument(mdMsg.SecurityId, cancellationToken);
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				if (dataType == DataType.MarketDepth && _depth50Subscriptions.Remove(symbol) && _tbtClient != null)
					await _tbtClient.Unsubscribe(symbol, cancellationToken);

				var subscriptions = _marketSubscriptions.SafeAdd(symbol);
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[symbol] = mdMsg.SecurityId;
				await UpdateNormalFeed(instrument, subscriptions, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(symbol, out var subscriptions))
		{
			subscriptions.Remove(dataType);
			if (subscriptions.Count == 0)
			{
				_marketSubscriptions.Remove(symbol);
				_lastTicks.Remove(symbol);
			}
			await UpdateNormalFeed(instrument, subscriptions, cancellationToken);
		}
	}

	private ValueTask UpdateNormalFeed(FyersInstrument instrument, SynchronizedDictionary<DataType, long> subscriptions, CancellationToken cancellationToken)
	{
		var flags = FyersFeedSubscriptions.None;
		if (subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.Ticks))
			flags |= FyersFeedSubscriptions.Symbol;
		if (subscriptions.ContainsKey(DataType.MarketDepth))
			flags |= FyersFeedSubscriptions.Depth;
		return _marketClient.SetSubscription(instrument, flags, cancellationToken);
	}

	private async ValueTask ProcessTbtSubscription(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var symbol = mdMsg.SecurityId.ToFyersSymbol();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				if (_marketSubscriptions.TryGetValue(symbol, out var normal) && normal.Remove(DataType.MarketDepth))
				{
					var instrument = await GetInstrument(mdMsg.SecurityId, cancellationToken);
					await UpdateNormalFeed(instrument, normal, cancellationToken);
					if (normal.Count == 0)
						_marketSubscriptions.Remove(symbol);
				}

				var client = await GetTbtClient(cancellationToken);
				_depth50Subscriptions[symbol] = mdMsg.TransactionId;
				_securityIds[symbol] = mdMsg.SecurityId;
				await client.Subscribe(symbol, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_depth50Subscriptions.Remove(symbol) && _tbtClient != null)
		{
			await _tbtClient.Unsubscribe(symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var candles = await _restClient.GetCandles(mdMsg.SecurityId.ToFyersSymbol(), mdMsg.GetTimeFrame(), mdMsg.From, mdMsg.To, cancellationToken);
		IEnumerable<FyersCandle> ordered = candles.OrderBy(c => c.Time);
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
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<FyersInstrument> GetInstrument(SecurityId securityId, CancellationToken cancellationToken)
	{
		var symbol = securityId.ToFyersSymbol();
		if (_instruments.TryGetValue(symbol, out var instrument))
			return instrument;

		instrument = await _restClient.GetInstrument(symbol, cancellationToken);
		if (instrument == null)
		{
			var token = securityId.ToFyersToken();
			if (token.IsEmpty())
				throw new InvalidOperationException($"FYERS symbol '{symbol}' was not found in the symbol master and has no native token.");
			instrument = new FyersInstrument { Symbol = symbol, Token = token };
		}
		_instruments[symbol] = instrument;
		return instrument;
	}

	private async ValueTask OnTickReceived(FyersMarketTick tick, CancellationToken cancellationToken)
	{
		if (tick?.Symbol.IsEmpty() != false || !_marketSubscriptions.TryGetValue(tick.Symbol, out var subscriptions))
			return;

		var securityId = _securityIds.TryGetValue2(tick.Symbol) ?? tick.Symbol.ToFyersSecurityId();
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
			if (!_lastTicks.TryGetValue(tick.Symbol, out var previous) || previous != trade)
			{
				_lastTicks[tick.Symbol] = trade;
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

		if (tick.IsDepth && subscriptions.TryGetValue(DataType.MarketDepth, out var depthId) && (tick.Bids.Length > 0 || tick.Asks.Length > 0))
			await SendDepth(depthId, securityId, tick.ServerTime, tick.Bids, tick.Asks, cancellationToken);
	}

	private ValueTask OnDepthReceived(FyersDepthUpdate update, CancellationToken cancellationToken)
	{
		if (update == null || !_depth50Subscriptions.TryGetValue(update.Symbol, out var subscriptionId))
			return default;
		var securityId = _securityIds.TryGetValue2(update.Symbol) ?? update.Symbol.ToFyersSecurityId();
		return SendDepth(subscriptionId, securityId, update.ServerTime, update.Bids, update.Asks, cancellationToken);
	}

	private ValueTask SendDepth(long subscriptionId, SecurityId securityId, DateTime serverTime,
		FyersDepthLevel[] bids, FyersDepthLevel[] asks, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscriptionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			Bids = [.. bids.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
			Asks = [.. asks.Select(level => new QuoteChange(level.Price, level.Volume) { OrdersCount = level.OrdersCount })],
		}, cancellationToken);
}
