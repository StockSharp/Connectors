namespace StockSharp.WisdomCapital;

public partial class WisdomCapitalMessageAdapter
{
    private readonly SynchronizedDictionary<
        string,
        SynchronizedDictionary<DataType, long>> _marketSubscriptions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, WisdomInstrument>
        _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string,
        (DateTime time, decimal price, decimal volume)> _lastTicks =
            new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId,
            cancellationToken);
        var securityTypes = lookupMsg.GetSecurityTypes();
        var left = lookupMsg.Count ?? long.MaxValue;

        foreach (var instrument in await _restClient.GetInstruments(
            cancellationToken))
        {
            SecurityId securityId;
            try
            {
                securityId = instrument.ToSecurityId();
            }
            catch (ArgumentException)
            {
                continue;
            }
            var type = instrument.ToSecurityType();
            var lotSize = instrument.LotSize > 0
                ? instrument.LotSize
                : 1m;
            var security = new SecurityMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                SecurityId = securityId,
                SecurityType = type,
                Name = instrument.Name
                    .IsEmpty(instrument.DisplayName)
                    .IsEmpty(instrument.TradingSymbol),
                ShortName = instrument.TradingSymbol
                    .IsEmpty(instrument.DisplayName),
                Class = instrument.Series
                    .IsEmpty(instrument.InstrumentType),
                Currency = CurrencyTypes.INR,
                PriceStep = instrument.TickSize > 0
                    ? instrument.TickSize
                    : null,
                VolumeStep = lotSize,
                Multiplier = instrument.Multiplier > 0
                    ? instrument.Multiplier
                    : lotSize,
                ExpiryDate = instrument.ExpiryDate,
                Strike = instrument.StrikePrice > 0
                    ? instrument.StrikePrice
                    : null,
                OptionType = instrument.OptionTypeCode.ToOptionType(),
            };
            if (type is SecurityTypes.Future or SecurityTypes.Option &&
                !instrument.UnderlyingName.IsEmpty())
            {
                security.UnderlyingSecurityId = new()
                {
                    SecurityCode = instrument.UnderlyingName,
                    BoardCode =
                        instrument.ExchangeSegment.ToBoardCode(),
                    Native = instrument.UnderlyingInstrumentId is long id
                        ? WisdomCapitalExtensions.CreateInstrumentKey(
                            instrument.ExchangeSegment,
                            id)
                        : null,
                };
            }
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;

            RememberInstrument(instrument, securityId);
            await SendOutMessageAsync(security, cancellationToken);
            if (--left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(
            lookupMsg,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(
            mdMsg,
            DataType.Level1,
            1501,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(
            mdMsg,
            DataType.Ticks,
            1512,
            cancellationToken);

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
                "Wisdom Capital XTS provides five market-depth levels.");
        }
        return ProcessRealtimeSubscription(
            mdMsg,
            DataType.MarketDepth,
            1502,
            cancellationToken);
    }

    private async ValueTask ProcessRealtimeSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        int messageCode,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId,
            cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await RemoveRealtimeSubscription(
                mdMsg,
                dataType,
                messageCode,
                cancellationToken);
            return;
        }

        var instrument = await ResolveInstrument(
            mdMsg.SecurityId,
            cancellationToken);
        var reference = instrument.ToReference();
        var key = WisdomCapitalExtensions.CreateInstrumentKey(
            instrument.ExchangeSegment,
            instrument.ExchangeInstrumentId);
        RememberInstrument(instrument, mdMsg.SecurityId);

        var updates = mdMsg.IsHistoryOnly()
            ? await _restClient.GetQuotes(
                reference,
                messageCode,
                cancellationToken)
            : await _restClient.Subscribe(
                reference,
                messageCode,
                cancellationToken);
        foreach (var update in updates)
        {
            await SendMarketSnapshot(
                update,
                mdMsg.SecurityId,
                mdMsg.TransactionId,
                dataType,
                cancellationToken);
        }

        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId,
                cancellationToken);
            return;
        }

        _marketSubscriptions
            .SafeAdd(key)[dataType] = mdMsg.TransactionId;
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private async ValueTask RemoveRealtimeSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        int messageCode,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveInstrument(
            mdMsg.SecurityId,
            cancellationToken);
        var key = WisdomCapitalExtensions.CreateInstrumentKey(
            instrument.ExchangeSegment,
            instrument.ExchangeInstrumentId);
        if (!_marketSubscriptions.TryGetValue(
            key,
            out var subscriptions))
            return;
        if (!subscriptions.TryGetValue(
            dataType,
            out var subscriptionId) ||
            subscriptionId != mdMsg.OriginalTransactionId)
            return;

        subscriptions.Remove(dataType);
        await _restClient.Unsubscribe(
            instrument.ToReference(),
            messageCode,
            cancellationToken);
        if (subscriptions.Count > 0)
            return;
        _marketSubscriptions.Remove(key);
        _lastTicks.Remove(key);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId,
            cancellationToken);
        if (!mdMsg.IsSubscribe)
            return;
        if (!mdMsg.IsHistoryOnly())
        {
            throw new NotSupportedException(
                "Wisdom Capital XTS provides historical candles only; realtime candle subscriptions are not available.");
        }

        var instrument = await ResolveInstrument(
            mdMsg.SecurityId,
            cancellationToken);
        var timeFrame = mdMsg.GetTimeFrame();
        var to = mdMsg.To ?? CurrentTime;
        var defaultRange = timeFrame >= TimeSpan.FromDays(1)
            ? TimeSpan.FromDays(365)
            : TimeSpan.FromDays(7);
        if (mdMsg.Count is long requested && requested > 0)
        {
            var ticks = Math.Min(
                (double)timeFrame.Ticks * requested * 2,
                TimeSpan.FromDays(3650).Ticks);
            defaultRange = TimeSpan.FromTicks((long)ticks);
        }
        var from = mdMsg.From ?? to.Subtract(defaultRange);
        IEnumerable<WisdomCandle> candles =
            (await _restClient.GetCandles(
                instrument,
                timeFrame,
                from,
                to,
                cancellationToken))
            .OrderBy(candle => candle.OpenTime);
        if (mdMsg.Count is long count)
        {
            candles = candles
                .TakeLast((int)Math.Min(count, int.MaxValue))
                .OrderBy(candle => candle.OpenTime);
        }

        foreach (var candle in candles)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    TypedArg = timeFrame,
                    OpenTime = candle.OpenTime,
                    OpenPrice = candle.Open,
                    HighPrice = candle.High,
                    LowPrice = candle.Low,
                    ClosePrice = candle.Close,
                    TotalVolume = candle.Volume,
                    OpenInterest = candle.OpenInterest,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }
        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId,
            cancellationToken);
    }

    private async ValueTask OnMarketDataReceived(
        WisdomMarketUpdate update,
        CancellationToken cancellationToken)
    {
        if (update == null ||
            update.SegmentId <= 0 ||
            update.ExchangeInstrumentId <= 0)
            return;
        string key;
        try
        {
            key = WisdomCapitalExtensions.CreateInstrumentKey(
                update.SegmentId,
                update.ExchangeInstrumentId);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        if (!_marketSubscriptions.TryGetValue(
            key,
            out var subscriptions))
            return;
        if (!_securityIds.TryGetValue(key, out var securityId))
            return;

        if (subscriptions.TryGetValue(DataType.Level1, out var level1Id) &&
            update.MessageCode is 0 or 1501)
        {
            var message = CreateLevel1(update, securityId, level1Id);
            if (message.Changes.Count > 0)
                await SendOutMessageAsync(message, cancellationToken);
        }
        if (subscriptions.TryGetValue(DataType.Ticks, out var ticksId) &&
            update.MessageCode is 0 or 1512 &&
            update.LastPrice > 0)
        {
            var trade =
                (update.ServerTime, update.LastPrice, update.LastVolume);
            if (!_lastTicks.TryGetValue(key, out var previous) ||
                previous != trade)
            {
                _lastTicks[key] = trade;
                await SendOutMessageAsync(
                    new ExecutionMessage
                    {
                        DataTypeEx = DataType.Ticks,
                        OriginalTransactionId = ticksId,
                        SecurityId = securityId,
                        TradeStringId =
                            $"{key}:{update.ServerTime.Ticks}:{update.LastPrice.ToString(CultureInfo.InvariantCulture)}",
                        TradePrice = update.LastPrice,
                        TradeVolume = Positive(update.LastVolume),
                        ServerTime = update.ServerTime,
                    },
                    cancellationToken);
            }
        }
        if (subscriptions.TryGetValue(
                DataType.MarketDepth,
                out var depthId) &&
            update.MessageCode is 0 or 1502 &&
            (update.Bids.Length > 0 || update.Asks.Length > 0))
        {
            await SendOutMessageAsync(
                CreateDepth(update, securityId, depthId),
                cancellationToken);
        }
    }

    private async ValueTask SendMarketSnapshot(
        WisdomMarketUpdate update,
        SecurityId securityId,
        long transactionId,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        if (update == null)
            return;
        if (dataType == DataType.Level1)
        {
            var message = CreateLevel1(
                update,
                securityId,
                transactionId);
            if (message.Changes.Count > 0)
                await SendOutMessageAsync(message, cancellationToken);
        }
        else if (dataType == DataType.Ticks && update.LastPrice > 0)
        {
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    DataTypeEx = DataType.Ticks,
                    OriginalTransactionId = transactionId,
                    SecurityId = securityId,
                    TradeStringId =
                        $"LTP:{update.SegmentId}:{update.ExchangeInstrumentId}:{update.ServerTime.Ticks}",
                    TradePrice = update.LastPrice,
                    TradeVolume = Positive(update.LastVolume),
                    ServerTime = update.ServerTime,
                },
                cancellationToken);
        }
        else if (dataType == DataType.MarketDepth &&
            (update.Bids.Length > 0 || update.Asks.Length > 0))
        {
            await SendOutMessageAsync(
                CreateDepth(update, securityId, transactionId),
                cancellationToken);
        }
    }

    private static Level1ChangeMessage CreateLevel1(
        WisdomMarketUpdate update,
        SecurityId securityId,
        long transactionId)
    {
        var bestBid = update.Bids
            .OrderByDescending(level => level.Price)
            .FirstOrDefault();
        var bestAsk = update.Asks
            .OrderBy(level => level.Price)
            .FirstOrDefault();
        return new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = update.ServerTime,
        }
        .TryAdd(Level1Fields.LastTradePrice, Positive(update.LastPrice))
        .TryAdd(Level1Fields.LastTradeVolume, Positive(update.LastVolume))
        .TryAdd(
            Level1Fields.LastTradeTime,
            update.LastPrice > 0 ? update.ServerTime : null)
        .TryAdd(Level1Fields.OpenPrice, Positive(update.OpenPrice))
        .TryAdd(Level1Fields.HighPrice, Positive(update.HighPrice))
        .TryAdd(Level1Fields.LowPrice, Positive(update.LowPrice))
        .TryAdd(Level1Fields.ClosePrice, Positive(update.ClosePrice))
        .TryAdd(Level1Fields.Volume, Positive(update.Volume))
        .TryAdd(Level1Fields.AveragePrice, Positive(update.AveragePrice))
        .TryAdd(
            Level1Fields.BidsVolume,
            Positive(update.TotalBuyVolume))
        .TryAdd(
            Level1Fields.AsksVolume,
            Positive(update.TotalSellVolume))
        .TryAdd(
            Level1Fields.OpenInterest,
            Positive(update.OpenInterest))
        .TryAdd(Level1Fields.MaxPrice, Positive(update.UpperCircuit))
        .TryAdd(Level1Fields.MinPrice, Positive(update.LowerCircuit))
        .TryAdd(
            Level1Fields.BestBidPrice,
            Positive(bestBid?.Price ?? 0))
        .TryAdd(
            Level1Fields.BestBidVolume,
            Positive(bestBid?.Volume ?? 0))
        .TryAdd(
            Level1Fields.BestAskPrice,
            Positive(bestAsk?.Price ?? 0))
        .TryAdd(
            Level1Fields.BestAskVolume,
            Positive(bestAsk?.Volume ?? 0));
    }

    private static QuoteChangeMessage CreateDepth(
        WisdomMarketUpdate update,
        SecurityId securityId,
        long transactionId)
        => new()
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = update.ServerTime,
            Bids =
            [
                ..
                    update.Bids
                        .Where(level => level.Price > 0)
                        .OrderByDescending(level => level.Price)
                        .Take(5)
                        .Select(level => new QuoteChange(
                            level.Price,
                            Math.Max(0, level.Volume))
                        {
                            OrdersCount = (int)Math.Min(
                                Math.Max(0, level.Orders),
                                int.MaxValue),
                        }),
            ],
            Asks =
            [
                ..
                    update.Asks
                        .Where(level => level.Price > 0)
                        .OrderBy(level => level.Price)
                        .Take(5)
                        .Select(level => new QuoteChange(
                            level.Price,
                            Math.Max(0, level.Volume))
                        {
                            OrdersCount = (int)Math.Min(
                                Math.Max(0, level.Orders),
                                int.MaxValue),
                        }),
            ],
        };

    private async Task<WisdomInstrument> ResolveInstrument(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var native = securityId.Native?.ToString();
        if (!native.IsEmpty())
        {
            if (_instruments.TryGetValue(native, out var known))
                return known;
            var separator = native.IndexOf(':');
            if (separator > 0 &&
                long.TryParse(
                    native[(separator + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var instrumentId))
            {
                var segment = native[..separator];
                var instrument = await _restClient.GetInstrument(
                    segment,
                    instrumentId,
                    cancellationToken);
                if (instrument != null)
                {
                    RememberInstrument(instrument, securityId);
                    return instrument;
                }
            }
        }

        var exchangeSegment =
            securityId.BoardCode.ToExchangeSegmentFromBoard();
        var found = await _restClient.FindInstrument(
            exchangeSegment,
            securityId.SecurityCode,
            cancellationToken) ?? throw new InvalidOperationException(
                $"Wisdom Capital instrument '{securityId}' was not found in the XTS security master.");
        RememberInstrument(found, securityId);
        return found;
    }

    private void RememberInstrument(
        WisdomInstrument instrument,
        SecurityId securityId)
    {
        var key = WisdomCapitalExtensions.CreateInstrumentKey(
            instrument.ExchangeSegment,
            instrument.ExchangeInstrumentId);
        _instruments[key] = instrument;
        _securityIds[key] = securityId;
    }

    private static decimal? Positive(decimal value)
        => value > 0 ? value : null;
}
