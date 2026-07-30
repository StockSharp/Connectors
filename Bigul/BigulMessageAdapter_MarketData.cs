namespace StockSharp.Bigul;

public partial class BigulMessageAdapter
{
    private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>>
        _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, BigulInstrument> _instruments =
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
                Name = instrument.Description.IsEmpty(instrument.Symbol),
                ShortName = instrument.TradingSymbol,
                Class = instrument.Series,
                Currency = CurrencyTypes.INR,
                PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
                VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
                Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
                ExpiryDate = instrument.Expiry,
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

            var instrumentKey = instrument.Segment.ToInstrumentKey(instrument.Token);
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
                "Bigul provides five market-depth levels.");
        }
        return ProcessRealtimeSubscription(
            mdMsg,
            DataType.MarketDepth,
            cancellationToken);
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

            var instrument = await GetInstrument(instrumentKey, cancellationToken);
            var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
            subscriptions[dataType] = mdMsg.TransactionId;
            _securityIds[instrumentKey] = mdMsg.SecurityId;
            _instruments[instrumentKey] = instrument;
            await _socketClient.SetSubscription(
                instrument,
                GetFeedSubscriptions(subscriptions),
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            return;
        }

        if (!_marketSubscriptions.TryGetValue(instrumentKey, out var existing))
            return;
        if (existing.TryGetValue(dataType, out var subscriptionId) &&
            subscriptionId == mdMsg.OriginalTransactionId)
            existing.Remove(dataType);

        if (_instruments.TryGetValue(instrumentKey, out var existingInstrument))
        {
            await _socketClient.SetSubscription(
                existingInstrument,
                GetFeedSubscriptions(existing),
                cancellationToken);
        }
        if (existing.Count > 0)
            return;

        _marketSubscriptions.Remove(instrumentKey);
        _securityIds.Remove(instrumentKey);
        _instruments.Remove(instrumentKey);
        _lastTicks.Remove(instrumentKey);
    }

    private async Task<BigulInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(instrumentKey, out var instrument))
            return instrument;
        instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Bigul instrument '{instrumentKey}' was not found in the official security master.");
        _instruments[instrumentKey] = instrument;
        return instrument;
    }

    private async ValueTask OnMarketDataReceived(
        BigulMarketTick update,
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
            var (segment, token) = update.InstrumentKey.ParseInstrumentKey();
            securityId = segment.ToSecurityId(token);
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
            .TryAdd(
                Level1Fields.LastTradeTime,
                lastPrice != null
                    ? update.LastTradeTime ?? serverTime
                    : null)
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
            .TryAdd(
                Level1Fields.BestBidPrice,
                bids.FirstOrDefault()?.Price ?? update.BidPrice)
            .TryAdd(
                Level1Fields.BestBidVolume,
                Positive(bids.FirstOrDefault()?.Volume ?? update.BidVolume))
            .TryAdd(
                Level1Fields.BestAskPrice,
                asks.FirstOrDefault()?.Price ?? update.AskPrice)
            .TryAdd(
                Level1Fields.BestAskVolume,
                Positive(asks.FirstOrDefault()?.Volume ?? update.AskVolume));
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
                        StartPosition = level.Position,
                        EndPosition = level.Position,
                    }),
                ],
                Asks =
                [
                    .. asks.Select(level => new QuoteChange(
                        level.Price,
                        level.Volume)
                    {
                        OrdersCount = level.OrdersCount,
                        StartPosition = level.Position,
                        EndPosition = level.Position,
                    }),
                ],
            }, cancellationToken);
        }

        var tickPrice = update.LastPrice ?? 0m;
        var tickVolume = update.LastVolume ?? 0m;
        if (tickPrice > 0 &&
            subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
        {
            var trade = (serverTime, tickPrice, tickVolume);
            if (!_lastTicks.TryGetValue(
                    update.InstrumentKey,
                    out var previous) ||
                previous != trade)
            {
                _lastTicks[update.InstrumentKey] = trade;
                await SendOutMessageAsync(new ExecutionMessage
                {
                    DataTypeEx = DataType.Ticks,
                    OriginalTransactionId = ticksId,
                    SecurityId = securityId,
                    TradeStringId =
                        $"{update.InstrumentKey}:{serverTime.Ticks}:{tickPrice.ToString(CultureInfo.InvariantCulture)}",
                    TradePrice = tickPrice,
                    TradeVolume = tickVolume > 0 ? tickVolume : null,
                    ServerTime = serverTime,
                }, cancellationToken);
            }
        }
    }

    private static BigulFeedSubscriptions GetFeedSubscriptions(
        SynchronizedDictionary<DataType, long> subscriptions)
    {
        var result = BigulFeedSubscriptions.None;
        if (subscriptions.ContainsKey(DataType.Level1) ||
            subscriptions.ContainsKey(DataType.Ticks))
            result |= BigulFeedSubscriptions.Symbol;
        if (subscriptions.ContainsKey(DataType.MarketDepth))
            result |= BigulFeedSubscriptions.Depth;
        return result;
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;
}
