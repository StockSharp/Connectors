namespace StockSharp.ChoiceFinX;

public partial class ChoiceFinXMessageAdapter
{
    private readonly SynchronizedDictionary<
        string,
        SynchronizedDictionary<DataType, long>>
        _marketSubscriptions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, SecurityId> _securityIds =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, ChoiceFinXInstrument> _instruments =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string,
        (DateTime time, decimal price, decimal volume)>
        _lastTicks =
            new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        string key;
        try
        {
            key = lookupMsg.SecurityId.ToInstrumentKey();
        }
        catch (InvalidOperationException)
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var instrument = await ResolveInstrument(
            key, cancellationToken);
        var message = CreateSecurityMessage(
            instrument, lookupMsg.TransactionId);
        if (message.IsMatch(
            lookupMsg,
            lookupMsg.GetSecurityTypes()))
        {
            await SendOutMessageAsync(
                message, cancellationToken);
        }
        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg, DataType.Level1, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg, DataType.Ticks, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask
        OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var depth = mdMsg.MaxDepth ?? 5;
        if (depth > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth), depth,
                "Choice FinX touchline exposes up to five depth levels.");
        }
        return ProcessMarketSubscription(
            mdMsg,
            DataType.MarketDepth,
            cancellationToken);
    }

    private async ValueTask ProcessMarketSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        var key = mdMsg.SecurityId.ToInstrumentKey();

        if (!mdMsg.IsSubscribe)
        {
            if (_marketSubscriptions.TryGetValue(
                key, out var current))
            {
                current.Remove(dataType);
                if (current.Count == 0)
                {
                    _marketSubscriptions.Remove(key);
                    _lastTicks.Remove(key);
                }
            }
            return;
        }

        await ResolveInstrument(key, cancellationToken);
        _securityIds[key] = mdMsg.SecurityId;
        if (mdMsg.IsHistoryOnly())
        {
            await PollMarketData(
                [key], cancellationToken,
                dataType, mdMsg.TransactionId);
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
            return;
        }

        var subscriptions =
            _marketSubscriptions.SafeAdd(key);
        subscriptions[dataType] = mdMsg.TransactionId;
        await PollMarketData(
            [key], cancellationToken);
        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask
        OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
            return;

        var (segmentId, token) =
            mdMsg.SecurityId
                .ToInstrumentKey()
                .ParseInstrumentKey();
        var to = mdMsg.To ?? CurrentTime;
        var from = mdMsg.From ??
            to - TimeSpan.FromDays(30);
        var candles = await _restClient.GetCandles(
            segmentId,
            token,
            mdMsg.GetTimeFrame(),
            from,
            to,
            cancellationToken);
        IEnumerable<ChoiceFinXCandle> ordered =
            candles.OrderBy(item => item.Time);
        if (mdMsg.Count is long count)
        {
            ordered = ordered
                .TakeLast(
                    (int)Math.Min(count, int.MaxValue))
                .OrderBy(item => item.Time);
        }

        foreach (var candle in ordered)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId =
                        mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    TypedArg = mdMsg.GetTimeFrame(),
                    OpenTime = candle.Time,
                    OpenPrice = candle.Open,
                    HighPrice = candle.High,
                    LowPrice = candle.Low,
                    ClosePrice = candle.Close,
                    TotalVolume = candle.Volume,
                    OpenInterest =
                        candle.OpenInterest,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }
        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }

    private async ValueTask PollMarketData(
        CancellationToken cancellationToken)
    {
        var keys = _marketSubscriptions.Keys.ToArray();
        if (keys.Length == 0)
            return;
        await PollMarketData(keys, cancellationToken);
    }

    private async ValueTask PollMarketData(
        IEnumerable<string> keys,
        CancellationToken cancellationToken,
        DataType snapshotType = null,
        long snapshotId = 0)
    {
        foreach (var batch in keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => (
                key,
                native: key.ParseInstrumentKey()))
            .Chunk(50))
        {
            var ticks = await _restClient.GetTouchlines(
                batch.Select(item => item.native),
                cancellationToken);
            foreach (var tick in ticks)
            {
                var key =
                    ChoiceFinXExtensions.CreateInstrumentKey(
                        tick.SegmentId,
                        tick.Token);
                if (snapshotType != null)
                {
                    var securityId =
                        _securityIds.TryGetValue2(key) ??
                        tick.SegmentId.ToSecurityId(
                            tick.Token);
                    await SendTick(
                        key,
                        tick,
                        securityId,
                        snapshotType,
                        snapshotId,
                        cancellationToken);
                    continue;
                }

                if (!_marketSubscriptions.TryGetValue(
                    key, out var subscriptions))
                {
                    continue;
                }
                var stockSharpId =
                    _securityIds.TryGetValue2(key) ??
                    tick.SegmentId.ToSecurityId(
                        tick.Token);
                foreach (var subscription in
                    subscriptions.ToArray())
                {
                    await SendTick(
                        key,
                        tick,
                        stockSharpId,
                        subscription.Key,
                        subscription.Value,
                        cancellationToken);
                }
            }
        }
    }

    private async ValueTask SendTick(
        string key,
        ChoiceFinXTick tick,
        SecurityId securityId,
        DataType dataType,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        if (dataType == DataType.Level1)
        {
            await SendOutMessageAsync(
                new Level1ChangeMessage
                {
                    OriginalTransactionId =
                        subscriptionId,
                    SecurityId = securityId,
                    ServerTime = tick.ServerTime,
                }
                .TryAdd(
                    Level1Fields.LastTradePrice,
                    tick.LastPrice)
                .TryAdd(
                    Level1Fields.LastTradeVolume,
                    tick.LastQuantity)
                .TryAdd(
                    Level1Fields.LastTradeTime,
                    tick.LastTradeTime)
                .TryAdd(
                    Level1Fields.AveragePrice,
                    tick.AveragePrice)
                .TryAdd(
                    Level1Fields.Volume,
                    tick.Volume)
                .TryAdd(
                    Level1Fields.OpenPrice,
                    tick.Open)
                .TryAdd(
                    Level1Fields.HighPrice,
                    tick.High)
                .TryAdd(
                    Level1Fields.LowPrice,
                    tick.Low)
                .TryAdd(
                    Level1Fields.ClosePrice,
                    tick.Close)
                .TryAdd(
                    Level1Fields.OpenInterest,
                    tick.OpenInterest)
                .TryAdd(
                    Level1Fields.BidsVolume,
                    tick.TotalBuyQuantity)
                .TryAdd(
                    Level1Fields.AsksVolume,
                    tick.TotalSellQuantity)
                .TryAdd(
                    Level1Fields.BestBidPrice,
                    tick.Bids.FirstOrDefault()?.Price)
                .TryAdd(
                    Level1Fields.BestBidVolume,
                    tick.Bids.FirstOrDefault()?.Quantity)
                .TryAdd(
                    Level1Fields.BestAskPrice,
                    tick.Asks.FirstOrDefault()?.Price)
                .TryAdd(
                    Level1Fields.BestAskVolume,
                    tick.Asks.FirstOrDefault()?.Quantity),
                cancellationToken);
            return;
        }

        if (dataType == DataType.MarketDepth)
        {
            if (tick.Bids.Length == 0 &&
                tick.Asks.Length == 0)
            {
                return;
            }
            await SendOutMessageAsync(
                new QuoteChangeMessage
                {
                    OriginalTransactionId =
                        subscriptionId,
                    SecurityId = securityId,
                    ServerTime = tick.ServerTime,
                    Bids =
                    [
                        .. tick.Bids.Select(level =>
                            new QuoteChange(
                                level.Price,
                                level.Quantity)
                            {
                                OrdersCount =
                                    level.Orders,
                            })
                    ],
                    Asks =
                    [
                        .. tick.Asks.Select(level =>
                            new QuoteChange(
                                level.Price,
                                level.Quantity)
                            {
                                OrdersCount =
                                    level.Orders,
                            })
                    ],
                },
                cancellationToken);
            return;
        }

        if (dataType != DataType.Ticks ||
            tick.LastPrice is not decimal price)
        {
            return;
        }
        var time = tick.LastTradeTime ??
            tick.ServerTime;
        var volume = tick.LastQuantity ?? 0;
        var trade = (time, price, volume);
        if (subscriptionId != 0 &&
            _lastTicks.TryGetValue(
                key, out var previous) &&
            previous == trade)
        {
            return;
        }
        _lastTicks[key] = trade;
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Ticks,
                OriginalTransactionId =
                    subscriptionId,
                SecurityId = securityId,
                ServerTime = time,
                TradePrice = price,
                TradeVolume =
                    volume > 0 ? volume : null,
            },
            cancellationToken);
    }

    private async Task<ChoiceFinXInstrument>
        ResolveInstrument(
        string key,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(
            key, out var instrument))
        {
            return instrument;
        }
        var (segmentId, token) =
            key.ParseInstrumentKey();
        instrument = await _restClient.GetInstrument(
            segmentId, token, cancellationToken) ??
            new ChoiceFinXInstrument
            {
                SegmentId = segmentId,
                Token = token,
                Symbol = token.ToString(
                    CultureInfo.InvariantCulture),
                PriceDivisor = PriceDivisor,
                LotSize = 1,
            };
        _instruments[key] = instrument;
        _securityIds[key] = instrument.ToSecurityId();
        return instrument;
    }

    private static SecurityMessage CreateSecurityMessage(
        ChoiceFinXInstrument instrument,
        long originalTransactionId)
    {
        var message = new SecurityMessage
        {
            OriginalTransactionId =
                originalTransactionId,
            SecurityId = instrument.ToSecurityId(),
            SecurityType =
                instrument.ToSecurityType(),
            Name = instrument.Name,
            ShortName = instrument.Symbol,
            Class = instrument.Series,
            Currency = CurrencyTypes.INR,
            PriceStep = instrument.TickSize > 0
                ? instrument.TickSize
                : null,
            VolumeStep = instrument.LotSize > 0
                ? instrument.LotSize
                : null,
            Multiplier = instrument.LotSize > 0
                ? instrument.LotSize
                : null,
            ExpiryDate = instrument.ExpiryDate,
            Strike = instrument.StrikePrice > 0
                ? instrument.StrikePrice
                : null,
            OptionType =
                instrument.OptionType.ToOptionType(),
        };
        if (!instrument.Underlying.IsEmpty())
        {
            message.UnderlyingSecurityId = new()
            {
                SecurityCode = instrument.Underlying,
            };
        }
        return message;
    }

    private ValueTask OnMarketStatusReceived(
        JObject root,
        CancellationToken cancellationToken)
    {
        var payload =
            ChoiceFinXSocketClient.GetPayload(root);
        var eventType = payload.GetInt(
            "EventType", "eventType");
        var segmentId = payload.GetInt(
            "Segment", "SegmentId");
        if (segmentId <= 0 || eventType <= 0)
            return default;

        return SendOutMessageAsync(
            new BoardStateMessage
            {
                ServerTime = CurrentTime,
                BoardCode = segmentId.ToBoardCode(),
                State = eventType % 2 == 1
                    ? SessionStates.Active
                    : SessionStates.ForceStopped,
            },
            cancellationToken);
    }
}
