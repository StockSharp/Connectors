namespace StockSharp.Mastertrust;

public partial class MastertrustMessageAdapter
{
    private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>>
        _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, MastertrustInstrument> _instruments =
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
                Name = instrument.CompanyName.IsEmpty(instrument.TradingSymbol),
                ShortName = instrument.TradingSymbol,
                Class = instrument.InstrumentName.IsEmpty(instrument.Segment),
                Currency = CurrencyTypes.INR,
                PriceStep = instrument.TickSize > 0 ? instrument.TickSize : null,
                VolumeStep = instrument.LotSize > 0 ? instrument.LotSize : null,
                Multiplier = instrument.LotSize > 0 ? instrument.LotSize : null,
                ExpiryDate = instrument.Expiry,
                Strike = instrument.Strike > 0 ? instrument.Strike : null,
                OptionType = instrument.OptionType.ToOptionType(),
            };
            if (securityType is SecurityTypes.Future or SecurityTypes.Option &&
                !instrument.AssetCode.IsEmpty())
            {
                security.UnderlyingSecurityId = new()
                {
                    SecurityCode = instrument.AssetCode,
                };
            }
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;

            var instrumentKey = instrument.Exchange.ToInstrumentKey(
                instrument.Token);
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
                "Mastertrust full snapquote provides five market-depth levels.");
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

            var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
            var hadDetailed = HasDetailedSubscription(subscriptions);
            var hadDepth = subscriptions.ContainsKey(DataType.MarketDepth);
            subscriptions[dataType] = mdMsg.TransactionId;
            _securityIds[instrumentKey] = mdMsg.SecurityId;
            _instruments[instrumentKey] = await GetInstrument(
                instrumentKey,
                cancellationToken);

            if (!hadDetailed && HasDetailedSubscription(subscriptions))
            {
                await _socketClient.SetSubscription(
                    instrumentKey,
                    MastertrustStreamModes.Detailed,
                    true,
                    cancellationToken);
            }
            if (!hadDepth && subscriptions.ContainsKey(DataType.MarketDepth))
            {
                await _socketClient.SetSubscription(
                    instrumentKey,
                    MastertrustStreamModes.Depth,
                    true,
                    cancellationToken);
            }

            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            return;
        }

        if (!_marketSubscriptions.TryGetValue(instrumentKey, out var existing))
            return;
        var hadDetailedBefore = HasDetailedSubscription(existing);
        var hadDepthBefore = existing.ContainsKey(DataType.MarketDepth);
        if (existing.TryGetValue(dataType, out var subscriptionId) &&
            subscriptionId == mdMsg.OriginalTransactionId)
            existing.Remove(dataType);

        if (hadDetailedBefore && !HasDetailedSubscription(existing))
        {
            await _socketClient.SetSubscription(
                instrumentKey,
                MastertrustStreamModes.Detailed,
                false,
                cancellationToken);
        }
        if (hadDepthBefore && !existing.ContainsKey(DataType.MarketDepth))
        {
            await _socketClient.SetSubscription(
                instrumentKey,
                MastertrustStreamModes.Depth,
                false,
                cancellationToken);
        }
        if (existing.Count > 0)
            return;

        _marketSubscriptions.Remove(instrumentKey);
        _securityIds.Remove(instrumentKey);
        _instruments.Remove(instrumentKey);
        _lastTicks.Remove(instrumentKey);
    }

    private async Task<MastertrustInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(instrumentKey, out var instrument))
            return instrument;

        instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Mastertrust instrument '{instrumentKey}' was not found in the official security master.");
        _instruments[instrumentKey] = instrument;
        return instrument;
    }

    private async ValueTask OnMarketDataReceived(
        MastertrustMarketData update,
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

        if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
        {
            var level1 = new Level1ChangeMessage
            {
                OriginalTransactionId = level1Id,
                SecurityId = securityId,
                ServerTime = serverTime,
            }
            .TryAdd(Level1Fields.LastTradePrice, Positive(update.LastPrice))
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
            .TryAdd(Level1Fields.BestBidPrice, Positive(update.BestBidPrice))
            .TryAdd(Level1Fields.BestBidVolume, Positive(update.BestBidVolume))
            .TryAdd(Level1Fields.BestAskPrice, Positive(update.BestAskPrice))
            .TryAdd(Level1Fields.BestAskVolume, Positive(update.BestAskVolume));
            if (level1.Changes.Count > 0)
                await SendOutMessageAsync(level1, cancellationToken);
        }

        if (update.IsDepth &&
            subscriptions.TryGetValue(DataType.MarketDepth, out var depthId) &&
            (update.Bids.Length > 0 || update.Asks.Length > 0))
        {
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                OriginalTransactionId = depthId,
                SecurityId = securityId,
                ServerTime = serverTime,
                Bids =
                [
                    .. update.Bids.Select(level => new QuoteChange(
                        level.Price,
                        level.Volume)
                    {
                        OrdersCount = level.OrdersCount,
                    }),
                ],
                Asks =
                [
                    .. update.Asks.Select(level => new QuoteChange(
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
        if (!update.IsDepth &&
            tickPrice > 0 &&
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

    private static bool HasDetailedSubscription(
        IDictionary<DataType, long> subscriptions)
        => subscriptions.ContainsKey(DataType.Level1) ||
            subscriptions.ContainsKey(DataType.Ticks);

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;
}
