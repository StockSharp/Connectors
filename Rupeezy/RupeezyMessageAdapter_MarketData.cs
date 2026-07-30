namespace StockSharp.Rupeezy;

public partial class RupeezyMessageAdapter
{
    private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>>
        _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, RupeezyInstrument> _instruments =
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

            var securityType = instrument.ToSecurityType();
            var security = new SecurityMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                SecurityId = securityId,
                SecurityType = securityType,
                Name = instrument.SecurityDescription.IsEmpty(instrument.Symbol),
                ShortName = instrument.Symbol,
                Class = instrument.InstrumentName.IsEmpty(instrument.Series),
                Currency = CurrencyTypes.INR,
                PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
                VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
                Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
                ExpiryDate = instrument.Expiry ?? instrument.LastTradingDate,
                Strike = instrument.StrikePrice > 0 ? instrument.StrikePrice : null,
                OptionType = instrument.OptionType.ToOptionType(),
            };
            if (securityType is SecurityTypes.Future or SecurityTypes.Option &&
                !instrument.Symbol.IsEmpty())
            {
                security.UnderlyingSecurityId = new()
                {
                    SecurityCode = instrument.Symbol,
                };
            }
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
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth),
                depth,
                "Rupeezy provides five market-depth levels.");
        }
        return ProcessRealtimeSubscription(
            mdMsg,
            DataType.MarketDepth,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
            return;

        var (exchange, token) = mdMsg.SecurityId
            .ToInstrumentKey()
            .ParseInstrumentKey();
        var candles = await _restClient.GetCandles(
            exchange,
            token,
            mdMsg.GetTimeFrame(),
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        IEnumerable<RupeezyCandle> ordered = candles.OrderBy(candle => candle.Time);
        if (mdMsg.Count is long count)
        {
            ordered = ordered
                .TakeLast((int)Math.Min(count, int.MaxValue))
                .OrderBy(candle => candle.Time);
        }

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
                await SendSubscriptionFinishedAsync(
                    mdMsg.TransactionId,
                    cancellationToken);
                return;
            }

            var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
            var first = subscriptions.Count == 0;
            subscriptions[dataType] = mdMsg.TransactionId;
            _securityIds[instrumentKey] = mdMsg.SecurityId;
            _instruments[instrumentKey] = await GetInstrument(
                instrumentKey,
                cancellationToken);
            if (first)
            {
                await _socketClient.SetSubscription(
                    instrumentKey,
                    true,
                    cancellationToken);
            }
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
        _instruments.Remove(instrumentKey);
        _lastTicks.Remove(instrumentKey);
        await _socketClient.SetSubscription(
            instrumentKey,
            false,
            cancellationToken);
    }

    private async Task<RupeezyInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(instrumentKey, out var instrument))
            return instrument;

        instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Rupeezy instrument '{instrumentKey}' was not found in the official security master.");
        _instruments[instrumentKey] = instrument;
        return instrument;
    }

    private async ValueTask OnMarketDataReceived(
        RupeezyMarketTick update,
        CancellationToken cancellationToken)
    {
        if (update == null ||
            update.InstrumentKey.IsEmpty() ||
            !_marketSubscriptions.TryGetValue(
                update.InstrumentKey,
                out var subscriptions))
            return;

        if (!_securityIds.TryGetValue(update.InstrumentKey, out var securityId))
        {
            var (exchange, token) = update.InstrumentKey.ParseInstrumentKey();
            securityId = exchange.ToSecurityId(token);
        }
        var serverTime = update.ServerTime == default
            ? CurrentTime
            : update.ServerTime;
        var bids = update.Bids
            .Where(level => level?.Price > 0)
            .OrderByDescending(level => level.Price)
            .ToArray();
        var asks = update.Asks
            .Where(level => level?.Price > 0)
            .OrderBy(level => level.Price)
            .ToArray();

        if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
        {
            var lastPrice = Positive(update.LastPrice);
            var level1 = new Level1ChangeMessage
            {
                OriginalTransactionId = level1Id,
                SecurityId = securityId,
                ServerTime = serverTime,
            }
            .TryAdd(Level1Fields.LastTradePrice, lastPrice)
            .TryAdd(Level1Fields.LastTradeVolume, Positive(update.LastVolume))
            .TryAdd(Level1Fields.LastTradeTime, update.LastTradeTime)
            .TryAdd(Level1Fields.Volume, Positive(update.Volume))
            .TryAdd(Level1Fields.AveragePrice, Positive(update.AveragePrice))
            .TryAdd(Level1Fields.OpenPrice, Positive(update.OpenPrice))
            .TryAdd(Level1Fields.HighPrice, Positive(update.HighPrice))
            .TryAdd(Level1Fields.LowPrice, Positive(update.LowPrice))
            .TryAdd(Level1Fields.ClosePrice, Positive(update.ClosePrice))
            .TryAdd(Level1Fields.OpenInterest, Positive(update.OpenInterest))
            .TryAdd(Level1Fields.BidsVolume, Positive(update.TotalBuyVolume))
            .TryAdd(Level1Fields.AsksVolume, Positive(update.TotalSellVolume))
            .TryAdd(Level1Fields.MinPrice, Positive(update.LowerCircuit))
            .TryAdd(Level1Fields.MaxPrice, Positive(update.UpperCircuit))
            .TryAdd(Level1Fields.BestBidPrice, bids.FirstOrDefault()?.Price)
            .TryAdd(Level1Fields.BestBidVolume, Positive(bids.FirstOrDefault()?.Volume))
            .TryAdd(Level1Fields.BestAskPrice, asks.FirstOrDefault()?.Price)
            .TryAdd(Level1Fields.BestAskVolume, Positive(asks.FirstOrDefault()?.Volume));
            if (level1.Changes.Count > 0)
                await SendOutMessageAsync(level1, cancellationToken);
        }

        if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId) &&
            (bids.Length > 0 || asks.Length > 0))
        {
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                OriginalTransactionId = depthId,
                SecurityId = securityId,
                ServerTime = serverTime,
                Bids =
                [
                    .. bids.Select(level => new QuoteChange(
                        level.Price,
                        level.Volume)
                    {
                        OrdersCount = level.OrdersCount,
                    }),
                ],
                Asks =
                [
                    .. asks.Select(level => new QuoteChange(
                        level.Price,
                        level.Volume)
                    {
                        OrdersCount = level.OrdersCount,
                    }),
                ],
            }, cancellationToken);
        }

        var tickPrice = update.LastPrice ?? 0;
        var tickVolume = update.LastVolume ?? 0;
        if (tickPrice > 0 &&
            subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
        {
            var tradeTime = update.LastTradeTime ?? serverTime;
            var trade = (tradeTime, tickPrice, tickVolume);
            if (!_lastTicks.TryGetValue(update.InstrumentKey, out var previous) ||
                previous != trade)
            {
                _lastTicks[update.InstrumentKey] = trade;
                await SendOutMessageAsync(new ExecutionMessage
                {
                    DataTypeEx = DataType.Ticks,
                    OriginalTransactionId = ticksId,
                    SecurityId = securityId,
                    TradeStringId =
                        $"{update.InstrumentKey}:{tradeTime.Ticks}:{tickPrice.ToString(CultureInfo.InvariantCulture)}",
                    TradePrice = tickPrice,
                    TradeVolume = tickVolume > 0 ? tickVolume : null,
                    ServerTime = tradeTime,
                }, cancellationToken);
            }
        }
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;
}
