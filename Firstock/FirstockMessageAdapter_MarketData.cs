namespace StockSharp.Firstock;

public partial class FirstockMessageAdapter
{
    private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>>
        _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, FirstockInstrument> _instruments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, (DateTime time, decimal price, decimal volume)>
        _lastTicks = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
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
                Name = instrument.CompanyName.IsEmpty(instrument.Symbol),
                ShortName = instrument.TradingSymbol,
                Class = instrument.Instrument,
                Currency = CurrencyTypes.INR,
                PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
                VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
                Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
                ExpiryDate = instrument.Expiry,
                Strike = instrument.StrikePrice > 0 ? instrument.StrikePrice : null,
                OptionType = instrument.OptionType.ToOptionType(),
            };
            if (security.SecurityType is SecurityTypes.Future or SecurityTypes.Option &&
                !instrument.Symbol.IsEmpty())
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
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(mdMsg, DataType.Level1, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(mdMsg, DataType.Ticks, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var depth = mdMsg.MaxDepth ?? 5;
        if (depth is < 1 or > 5)
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth), depth, "Firstock provides five market-depth levels.");
        return ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
    }

    private async ValueTask ProcessRealtimeSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
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
            subscriptions[dataType] = mdMsg.TransactionId;
            _securityIds[instrumentKey] = mdMsg.SecurityId;
            _instruments[instrumentKey] = instrument;
            if (wasEmpty)
                await _socketClient.Subscribe(instrumentKey, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            return;
        }

        if (!_marketSubscriptions.TryGetValue(instrumentKey, out var existing))
            return;
        if (existing.TryGetValue(dataType, out var subscriptionId) &&
            subscriptionId == mdMsg.OriginalTransactionId)
            existing.Remove(dataType);
        if (existing.Count > 0)
            return;

        _marketSubscriptions.Remove(instrumentKey);
        _securityIds.Remove(instrumentKey);
        _lastTicks.Remove(instrumentKey);
        await _socketClient.Unsubscribe(instrumentKey, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
            return;
        if (!mdMsg.IsHistoryOnly())
            throw new NotSupportedException(
                "Firstock provides historical candles only; realtime candle subscriptions are not available.");

        var instrument = await GetInstrument(
            mdMsg.SecurityId.ToInstrumentKey(), cancellationToken);
        var timeFrame = mdMsg.GetTimeFrame();
        var candles = await _restClient.GetCandles(
            instrument, timeFrame, mdMsg.From, mdMsg.To, cancellationToken);
        IEnumerable<FirstockCandle> ordered = candles
            .Where(candle => candle != null)
            .OrderBy(candle => candle.GetCandleTime());
        if (mdMsg.Count is long count)
            ordered = ordered
                .TakeLast((int)Math.Min(count, int.MaxValue))
                .OrderBy(candle => candle.GetCandleTime());

        foreach (var candle in ordered)
        {
            await SendOutMessageAsync(new TimeFrameCandleMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId,
                TypedArg = timeFrame,
                OpenTime = candle.GetCandleTime(),
                OpenPrice = candle.Open,
                HighPrice = candle.High,
                LowPrice = candle.Low,
                ClosePrice = candle.Close,
                TotalVolume = candle.Volume,
                OpenInterest = Positive(candle.OpenInterest),
                State = CandleStates.Finished,
            }, cancellationToken);
        }

        await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
    }

    private async Task<FirstockInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(instrumentKey, out var instrument))
            return instrument;
        instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Firstock instrument '{instrumentKey}' was not found in the official security master.");
        _instruments[instrumentKey] = instrument;
        return instrument;
    }

    private async ValueTask OnMarketDataReceived(
        FirstockMarketUpdate update,
        CancellationToken cancellationToken)
    {
        var instrumentKey = update.Exchange.ToInstrumentKey(update.Token);
        if (!_marketSubscriptions.TryGetValue(instrumentKey, out var subscriptions))
            return;

        var securityId = _securityIds.TryGetValue2(instrumentKey)
            ?? update.Exchange.ToSecurityId(update.Token, update.TradingSymbol);
        var serverTime = update.ServerTime;
        var bids = update.Bids
            .Select(level => new FirstockDepthLevel
            {
                Price = level.Price / PriceDivisor,
                Quantity = level.Quantity,
                Orders = level.Orders,
            })
            .Where(level => level.Price > 0)
            .OrderByDescending(level => level.Price)
            .ToArray();
        var asks = update.Asks
            .Select(level => new FirstockDepthLevel
            {
                Price = level.Price / PriceDivisor,
                Quantity = level.Quantity,
                Orders = level.Orders,
            })
            .Where(level => level.Price > 0)
            .OrderBy(level => level.Price)
            .ToArray();

        if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
        {
            var lastPrice = Positive(update.LastPrice.ToPrice(PriceDivisor));
            var level1 = new Level1ChangeMessage
            {
                OriginalTransactionId = level1Id,
                SecurityId = securityId,
                ServerTime = serverTime,
            }
            .TryAdd(Level1Fields.LastTradePrice, lastPrice)
            .TryAdd(Level1Fields.LastTradeVolume, Positive(update.LastQuantity))
            .TryAdd(Level1Fields.LastTradeTime, lastPrice != null
                ? update.LastTradeTime ?? serverTime
                : null)
            .TryAdd(Level1Fields.Volume, Positive(update.Volume))
            .TryAdd(Level1Fields.AveragePrice, Positive(update.AveragePrice.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.OpenPrice, Positive(update.Open.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.HighPrice, Positive(update.High.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.LowPrice, Positive(update.Low.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.ClosePrice, Positive(update.Close.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.OpenInterest, Positive(update.OpenInterest))
            .TryAdd(Level1Fields.BidsVolume, Positive(update.TotalBuyQuantity))
            .TryAdd(Level1Fields.AsksVolume, Positive(update.TotalSellQuantity))
            .TryAdd(Level1Fields.MinPrice, Positive(update.LowerCircuit.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.MaxPrice, Positive(update.UpperCircuit.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.HighPrice52Week, Positive(update.YearHigh.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.LowPrice52Week, Positive(update.YearLow.ToPrice(PriceDivisor)))
            .TryAdd(Level1Fields.BestBidPrice, bids.FirstOrDefault()?.Price)
            .TryAdd(Level1Fields.BestBidVolume, bids.FirstOrDefault()?.Quantity)
            .TryAdd(Level1Fields.BestAskPrice, asks.FirstOrDefault()?.Price)
            .TryAdd(Level1Fields.BestAskVolume, asks.FirstOrDefault()?.Quantity);
            if (level1.Changes.Count > 0)
                await SendOutMessageAsync(level1, cancellationToken);
        }

        if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
        {
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                OriginalTransactionId = depthId,
                SecurityId = securityId,
                ServerTime = serverTime,
                Bids =
                [
                    .. bids.Select(level => new QuoteChange(level.Price, level.Quantity)
                    {
                        OrdersCount = level.Orders,
                    }),
                ],
                Asks =
                [
                    .. asks.Select(level => new QuoteChange(level.Price, level.Quantity)
                    {
                        OrdersCount = level.Orders,
                    }),
                ],
            }, cancellationToken);
        }

        var tickPrice = update.LastPrice.ToPrice(PriceDivisor) ?? 0m;
        var tickVolume = update.LastQuantity ?? 0m;
        if (tickPrice > 0 && subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
        {
            var trade = (serverTime, tickPrice, tickVolume);
            if (!_lastTicks.TryGetValue(instrumentKey, out var previous) || previous != trade)
            {
                _lastTicks[instrumentKey] = trade;
                await SendOutMessageAsync(new ExecutionMessage
                {
                    DataTypeEx = DataType.Ticks,
                    OriginalTransactionId = ticksId,
                    SecurityId = securityId,
                    TradeStringId =
                        $"{instrumentKey}:{serverTime.Ticks}:{tickPrice.ToString(CultureInfo.InvariantCulture)}",
                    TradePrice = tickPrice,
                    TradeVolume = tickVolume > 0 ? tickVolume : null,
                    ServerTime = serverTime,
                }, cancellationToken);
            }
        }
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;
}
