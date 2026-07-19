namespace StockSharp.NDAX;

public partial class NDAXMessageAdapter
{
    private readonly record struct BookLevelKey(Sides Side, decimal Price);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        var securityTypes = lookupMsg.GetSecurityTypes();
        var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : lookupMsg.SecurityId.SecurityCode.NormalizeSymbol();
        NdaxInstrument[] instruments;
        using (_sync.EnterScope())
            instruments = [.. _instrumentsById.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var instrument in instruments.OrderBy(static value =>
            value.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.NDAX))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(instrument.Symbol))
                continue;
            var security = CreateSecurity(instrument,
                lookupMsg.TransactionId);
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;
            if (skip-- > 0)
                continue;
            await SendOutMessageAsync(security, cancellationToken);
            await SendOutMessageAsync(new Level1ChangeMessage
            {
                SecurityId = security.SecurityId,
                ServerTime = CurrentTime,
                OriginalTransactionId = lookupMsg.TransactionId,
            }.TryAdd(Level1Fields.State,
                ToSecurityState(instrument.SessionStatus)), cancellationToken);
            if (--left <= 0)
                break;
        }
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var instrument = GetInstrument(mdMsg.SecurityId);
        if (mdMsg.IsHistoryOnly())
        {
            var level1 = await RestClient.GetLevel1Async(OmsId,
                instrument.InstrumentId, cancellationToken);
            await SendLevel1Async(level1, mdMsg.TransactionId,
                cancellationToken);
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                InstrumentId = instrument.InstrumentId,
                Symbol = instrument.Symbol,
            });
        var key = new StreamKey(NdaxSubscriptionKinds.Level1,
            instrument.InstrumentId, 0, 0);
        try
        {
            await AcquireStreamAsync(key, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _level1Subscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var instrument = GetInstrument(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 100).Min(500).Max(1);
        if (mdMsg.IsHistoryOnly())
        {
            var entries = await RestClient.GetLevel2Async(OmsId,
                instrument.InstrumentId, depth, cancellationToken);
            await SendBookSnapshotAsync(instrument, entries, depth,
                mdMsg.TransactionId, cancellationToken);
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                InstrumentId = instrument.InstrumentId,
                Symbol = instrument.Symbol,
                Depth = depth,
            });
            if (!_books.ContainsKey(instrument.InstrumentId))
                _books.Add(instrument.InstrumentId, new());
        }
        var key = new StreamKey(NdaxSubscriptionKinds.Level2,
            instrument.InstrumentId, _webSocketDepth, 0);
        try
        {
            await AcquireStreamAsync(key, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeDepthAsync(mdMsg.TransactionId,
                cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var instrument = GetInstrument(mdMsg.SecurityId);
        if (mdMsg.IsHistoryOnly() || mdMsg.From is not null ||
            mdMsg.To is not null)
        {
            var trades = await RestClient.GetTradesAsync(instrument.Symbol,
                cancellationToken);
            var maximum = (mdMsg.Count ?? 1000).Min(5000).Max(1).To<int>();
            foreach (var trade in (trades ?? [])
                .Where(trade => trade is not null &&
                    (mdMsg.From is null || trade.Timestamp >=
                        mdMsg.From.Value.ToUniversalTime()) &&
                    (mdMsg.To is null || trade.Timestamp <=
                        mdMsg.To.Value.ToUniversalTime()))
                .OrderBy(static trade => trade.Timestamp)
                .TakeLast(maximum))
                await SendRecentTradeAsync(instrument, trade,
                    mdMsg.TransactionId, cancellationToken);
        }

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                InstrumentId = instrument.InstrumentId,
                Symbol = instrument.Symbol,
            });
        var key = new StreamKey(NdaxSubscriptionKinds.Trades,
            instrument.InstrumentId, 0, 0);
        try
        {
            await AcquireStreamAsync(key, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeTicksAsync(mdMsg.TransactionId,
                cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var instrument = GetInstrument(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToInterval();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to.AddPeriods(timeFrame, -maximum);
        var candles = await LoadCandlesAsync(instrument, timeFrame, from, to,
            maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(instrument, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                InstrumentId = instrument.InstrumentId,
                Symbol = instrument.Symbol,
                TimeFrame = timeFrame,
            });
        var key = new StreamKey(NdaxSubscriptionKinds.Ticker,
            instrument.InstrumentId, timeFrame.ToInterval(), 0);
        try
        {
            await AcquireStreamAsync(key, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeCandlesAsync(mdMsg.TransactionId,
                cancellationToken);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(NdaxInstrument instrument,
        long originalTransactionId)
    {
        NdaxProduct quote;
        using (_sync.EnterScope())
            _products.TryGetValue(instrument.QuoteProductId, out quote);
        return new()
        {
            SecurityId = instrument.Symbol.ToStockSharp(),
            Name = $"{instrument.BaseSymbol}/{instrument.QuoteSymbol}",
            ShortName = instrument.Symbol.NormalizeSymbol(),
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = instrument.QuoteSymbol.ToCurrency(),
            PriceStep = instrument.PriceIncrement > 0
                ? instrument.PriceIncrement
                : quote?.TickSize > 0 ? quote.TickSize : null,
            VolumeStep = instrument.QuantityIncrement > 0
                ? instrument.QuantityIncrement
                : null,
            MinVolume = instrument.MinimumQuantity is > 0
                ? instrument.MinimumQuantity
                : null,
            OriginalTransactionId = originalTransactionId,
        };
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(new(NdaxSubscriptionKinds.Level1,
                subscription.InstrumentId, 0, 0), cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(new(NdaxSubscriptionKinds.Level2,
                subscription.InstrumentId, _webSocketDepth, 0),
                cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(new(NdaxSubscriptionKinds.Trades,
                subscription.InstrumentId, 0, 0), cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(new(NdaxSubscriptionKinds.Ticker,
                subscription.InstrumentId,
                subscription.TimeFrame.ToInterval(), 0), cancellationToken);
    }

    private async ValueTask OnSocketLevel1Async(NdaxLevel1 level1,
        bool isSnapshot, CancellationToken cancellationToken)
    {
        _ = isSnapshot;
        if (level1 is null)
            return;
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _level1Subscriptions.Where(pair =>
                pair.Value.InstrumentId == level1.InstrumentId).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendLevel1Async(level1, target, cancellationToken);
    }

    private async ValueTask OnSocketLevel2Async(NdaxLevel2Entry[] entries,
        bool isSnapshot, CancellationToken cancellationToken)
    {
        if (entries is not { Length: > 0 })
            return;
        var valid = entries.Where(static entry => entry is not null &&
            entry.InstrumentId > 0).ToArray();
        if (valid.Length == 0)
            return;

        foreach (var group in valid.GroupBy(static entry =>
            entry.InstrumentId))
        {
            var updates = group.ToArray();
            if (!isSnapshot)
            {
                var sequenceState = ValidateBookSequence(group.Key, updates,
                    out var previous, out var expected, out var received);
                if (sequenceState == BookSequenceStates.Duplicate)
                    continue;
                if (sequenceState == BookSequenceStates.Gap)
                {
                    this.AddWarningLog(
                        "NDAX {0} order-book sequence gap. Expected {1}, received {2}. Requesting a new snapshot.",
                        group.Key, expected, received);
                    if (TryBeginBookRefresh(group.Key, out var gapDepth))
                        await RefreshBookAsync(group.Key, gapDepth,
                            cancellationToken);
                    continue;
                }
                if (previous > 0)
                    updates = [.. updates.Where(entry =>
                        entry.UpdateId <= 0 || entry.UpdateId > previous)];
            }
            if (!isSnapshot && !IsBookReady(group.Key))
            {
                if (TryBeginBookRefresh(group.Key, out var depth))
                    await RefreshBookAsync(group.Key, depth,
                        cancellationToken);
                continue;
            }
            await ApplyBookAsync(group.Key, updates, isSnapshot,
                cancellationToken);
        }
    }

    private async ValueTask ApplyBookAsync(int instrumentId,
        NdaxLevel2Entry[] entries, bool isSnapshot,
        CancellationToken cancellationToken)
    {
        (long Id, int Depth)[] targets;
        QuoteChange[] bids;
        QuoteChange[] asks;
        var sequence = entries.Select(static entry => entry.UpdateId)
            .DefaultIfEmpty(0).Max();
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(instrumentId, out var state))
                _books.Add(instrumentId, state = new());
            var changed = new HashSet<BookLevelKey>();
            if (isSnapshot)
            {
                state.Levels.Clear();
                foreach (var group in entries.Where(static entry =>
                    entry.Price > 0 && entry.Quantity > 0 &&
                    entry.Side is NdaxSides.Buy or NdaxSides.Sell)
                    .GroupBy(static entry => new BookLevelKey(
                        entry.Side.ToStockSharp(), entry.Price)))
                    state.Levels[group.Key] = new(
                        group.Sum(static entry => entry.Quantity),
                        group.Sum(static entry => entry.OrderCount));
                state.IsSnapshotReady = true;
                state.IsRefreshPending = false;
                state.Sequence = sequence;
                bids = GetBookSide(state, Sides.Buy);
                asks = GetBookSide(state, Sides.Sell);
            }
            else
            {
                if (!state.IsSnapshotReady)
                    return;
                foreach (var entry in entries.OrderBy(static value =>
                    value.UpdateId))
                {
                    if (entry.Price <= 0 ||
                        entry.Side is not (NdaxSides.Buy or NdaxSides.Sell))
                        continue;
                    var key = new BookLevelKey(entry.Side.ToStockSharp(),
                        entry.Price);
                    changed.Add(key);
                    if (entry.Action == NdaxBookActions.Delete ||
                        entry.Quantity <= 0)
                        state.Levels.Remove(key);
                    else
                        state.Levels[key] = new(entry.Quantity,
                            entry.OrderCount);
                }
                bids = GetChangedBookSide(state, changed, Sides.Buy);
                asks = GetChangedBookSide(state, changed, Sides.Sell);
            }
            targets = [.. _depthSubscriptions.Where(pair =>
                pair.Value.InstrumentId == instrumentId).Select(static pair =>
                (pair.Key, pair.Value.Depth))];
        }
        var instrument = GetInstrument(instrumentId);
        if (instrument is null)
            return;
        var serverTime = entries.Select(static entry =>
            entry.Timestamp.FromNdaxTime()).Where(static value =>
            value != default).DefaultIfEmpty(CurrentTime).Max();
        foreach (var target in targets)
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                SecurityId = instrument.Symbol.ToStockSharp(),
                ServerTime = serverTime,
                OriginalTransactionId = target.Id,
                State = isSnapshot
                    ? QuoteChangeStates.SnapshotComplete
                    : QuoteChangeStates.Increment,
                SeqNum = sequence,
                Bids = isSnapshot ? [.. bids.Take(target.Depth)] : bids,
                Asks = isSnapshot ? [.. asks.Take(target.Depth)] : asks,
            }, cancellationToken);
    }

    private BookSequenceStates ValidateBookSequence(int instrumentId,
        NdaxLevel2Entry[] entries, out long previous, out long expected,
        out long received)
    {
        var sequences = entries.Select(static entry => entry.UpdateId)
            .Where(static value => value > 0).Distinct().Order().ToArray();
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(instrumentId, out var state))
                _books.Add(instrumentId, state = new());
            var last = state.Sequence;
            previous = last;
            expected = last + 1;
            var fresh = last > 0
                ? sequences.Where(value => value > last).ToArray()
                : sequences;
            if (sequences.Length > 0 && fresh.Length == 0)
            {
                received = sequences[^1];
                return BookSequenceStates.Duplicate;
            }
            received = fresh.Length > 0 ? fresh[0] : expected;
            var hasGap = last > 0 && received > expected;
            for (var i = 1; !hasGap && i < fresh.Length; i++)
                hasGap = fresh[i] > fresh[i - 1] + 1;
            if (hasGap)
            {
                state.IsSnapshotReady = false;
                state.IsRefreshPending = false;
                return BookSequenceStates.Gap;
            }
            if (fresh.Length > 0)
                state.Sequence = fresh[^1];
            return BookSequenceStates.Valid;
        }
    }

    private async ValueTask RefreshBookAsync(int instrumentId, int depth,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await RestClient.GetLevel2Async(OmsId,
                instrumentId, depth, cancellationToken);
            await ApplyBookAsync(instrumentId, snapshot ?? [], true,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                if (_books.TryGetValue(instrumentId, out var state))
                    state.IsRefreshPending = false;
            throw;
        }
    }

    private bool IsBookReady(int instrumentId)
    {
        using (_sync.EnterScope())
            return _books.TryGetValue(instrumentId, out var state) &&
                state.IsSnapshotReady;
    }

    private bool TryBeginBookRefresh(int instrumentId, out int depth)
    {
        using (_sync.EnterScope())
        {
            depth = _depthSubscriptions.Values.Where(value =>
                value.InstrumentId == instrumentId).Select(static value =>
                value.Depth).DefaultIfEmpty(0).Max();
            if (depth <= 0)
                return false;
            if (!_books.TryGetValue(instrumentId, out var state))
                _books.Add(instrumentId, state = new());
            if (state.IsSnapshotReady || state.IsRefreshPending)
                return false;
            state.IsRefreshPending = true;
            return true;
        }
    }

    private async ValueTask OnSocketTradesAsync(NdaxPublicTrade[] trades,
        bool isSnapshot, CancellationToken cancellationToken)
    {
        _ = isSnapshot;
        foreach (var trade in (trades ?? []).Where(static value =>
            value is not null).OrderBy(static value => value.Timestamp))
        {
            var instrument = GetInstrument(trade.InstrumentId);
            if (instrument is null)
                continue;
            long[] targets;
            using (_sync.EnterScope())
                targets = [.. _tickSubscriptions.Where(pair =>
                    pair.Value.InstrumentId == trade.InstrumentId).Select(
                    static pair => pair.Key)];
            foreach (var target in targets)
                await SendPublicTradeAsync(instrument, trade, target,
                    cancellationToken);
        }
    }

    private async ValueTask OnSocketCandlesAsync(NdaxCandle[] candles,
        bool isSnapshot, CancellationToken cancellationToken)
    {
        _ = isSnapshot;
        foreach (var candle in candles ?? [])
        {
            if (candle is null)
                continue;
            var instrument = GetInstrument(candle.InstrumentId);
            if (instrument is null)
                continue;
            TimeSpan? interval = candle.Timestamp > candle.PreviousTimestamp &&
                candle.PreviousTimestamp > 0
                ? TimeSpan.FromMilliseconds(candle.Timestamp -
                    candle.PreviousTimestamp)
                : null;
            (long Id, TimeSpan TimeFrame)[] targets;
            using (_sync.EnterScope())
                targets = [.. _candleSubscriptions.Where(pair =>
                    pair.Value.InstrumentId == candle.InstrumentId &&
                    (interval is null ||
                        pair.Value.TimeFrame == interval.Value)).Select(
                    static pair => (pair.Key, pair.Value.TimeFrame))];
            if (interval is null && targets.Select(static value =>
                value.TimeFrame).Distinct().Take(2).Count() > 1)
            {
                this.AddWarningLog(
                    "NDAX candle update for {0} has no interval and cannot be routed across multiple time frames.",
                    instrument.Symbol);
                continue;
            }
            foreach (var target in targets)
                await SendCandleAsync(instrument, candle, target.TimeFrame,
                    target.Id, cancellationToken);
        }
    }

    private ValueTask SendLevel1Async(NdaxLevel1 level1, long targetId,
        CancellationToken cancellationToken)
    {
        if (level1 is null)
            return default;
        var instrument = GetInstrument(level1.InstrumentId);
        if (instrument is null)
            return default;
        var serverTime = level1.Timestamp.FromNdaxTime();
        if (serverTime == default)
            serverTime = CurrentTime;
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = targetId,
        }
        .TryAdd(Level1Fields.BestBidPrice, level1.BestBid)
        .TryAdd(Level1Fields.BestBidVolume, level1.BidQuantity)
        .TryAdd(Level1Fields.BestAskPrice, level1.BestOffer)
        .TryAdd(Level1Fields.BestAskVolume, level1.AskQuantity)
        .TryAdd(Level1Fields.LastTradePrice, level1.LastPrice)
        .TryAdd(Level1Fields.LastTradeVolume, level1.LastQuantity)
        .TryAdd(Level1Fields.LastTradeTime,
            level1.LastTradeTime.FromNdaxTime())
        .TryAdd(Level1Fields.OpenPrice, level1.Open)
        .TryAdd(Level1Fields.HighPrice, level1.High)
        .TryAdd(Level1Fields.LowPrice, level1.Low)
        .TryAdd(Level1Fields.ClosePrice, level1.Close)
        .TryAdd(Level1Fields.Volume, level1.RollingVolume ??
            level1.DayVolume)
        .TryAdd(Level1Fields.Change, level1.RollingChangePercent ??
            level1.DayChange)
        .TryAdd(Level1Fields.State,
            ToSecurityState(instrument.SessionStatus)), cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(NdaxInstrument instrument,
        NdaxPublicTrade trade, long targetId,
        CancellationToken cancellationToken)
    {
        var key = $"{targetId}:{trade.TradeId}";
        using (_sync.EnterScope())
        {
            if (_publicTrades.Count > 100000)
                _publicTrades.Clear();
            if (!_publicTrades.Add(key))
                return default;
        }
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp.FromNdaxTime()),
            OriginalTransactionId = targetId,
            TradeId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            OriginSide = trade.TakerSide is NdaxSides.Buy or NdaxSides.Sell
                ? trade.TakerSide.ToStockSharp()
                : null,
        }, cancellationToken);
    }

    private ValueTask SendRecentTradeAsync(NdaxInstrument instrument,
        NdaxRecentTrade trade, long targetId,
        CancellationToken cancellationToken)
    {
        var key = $"{targetId}:{trade.TradeId}";
        using (_sync.EnterScope())
        {
            if (_publicTrades.Count > 100000)
                _publicTrades.Clear();
            if (!_publicTrades.Add(key))
                return default;
        }
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = targetId,
            TradeId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Volume,
            OriginSide = trade.Type.ToSide(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(NdaxInstrument instrument,
        NdaxCandle candle, TimeSpan timeFrame, long targetId,
        CancellationToken cancellationToken)
    {
        var closeTime = candle.Timestamp.FromNdaxTime();
        var openTime = candle.PreviousTimestamp.FromNdaxTime();
        if (closeTime == default)
            closeTime = CurrentTime;
        if (openTime == default || openTime >= closeTime)
            openTime = closeTime - timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = instrument.Symbol.ToStockSharp(),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            TypedArg = timeFrame,
            OriginalTransactionId = targetId,
            State = closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);
    }

    private async ValueTask<NdaxCandle[]> LoadCandlesAsync(
        NdaxInstrument instrument, TimeSpan timeFrame, DateTime from,
        DateTime to, int maximum, CancellationToken cancellationToken)
    {
        var result = new List<NdaxCandle>();
        var cursor = from.ToUtcTime();
        var upperBound = to.ToUtcTime();
        while (result.Count < maximum && cursor < upperBound)
        {
            var pageSize = (maximum - result.Count).Min(1000).Max(1);
            var windowEnd = cursor.AddPeriods(timeFrame, pageSize)
                .Min(upperBound);
            var page = await RestClient.GetCandlesAsync(OmsId,
                instrument.InstrumentId, timeFrame, cursor, windowEnd,
                cancellationToken);
            result.AddRange((page ?? []).Where(candle => candle is not null &&
                candle.Timestamp.FromNdaxTime() >= from &&
                candle.Timestamp.FromNdaxTime() <= to));
            if (windowEnd >= upperBound)
                break;
            cursor = windowEnd;
        }
        return [.. result.GroupBy(static candle =>
                candle.PreviousTimestamp > 0
                    ? candle.PreviousTimestamp
                    : candle.Timestamp)
            .Select(static group => group.First())
            .OrderBy(static candle => candle.Timestamp)
            .TakeLast(maximum)];
    }

    private async ValueTask SendBookSnapshotAsync(NdaxInstrument instrument,
        NdaxLevel2Entry[] entries, int depth, long targetId,
        CancellationToken cancellationToken)
    {
        var levels = (entries ?? []).Where(static entry => entry is not null &&
            entry.Price > 0 && entry.Quantity > 0 &&
            entry.Side is NdaxSides.Buy or NdaxSides.Sell).ToArray();
        await SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = (entries ?? []).Select(static entry =>
                entry.Timestamp.FromNdaxTime()).Where(static value =>
                value != default).DefaultIfEmpty(CurrentTime).Max(),
            OriginalTransactionId = targetId,
            State = QuoteChangeStates.SnapshotComplete,
            SeqNum = (entries ?? []).Select(static entry => entry.UpdateId)
                .DefaultIfEmpty(0).Max(),
            Bids = [.. levels.Where(static entry =>
                    entry.Side == NdaxSides.Buy)
                .GroupBy(static entry => entry.Price)
                .OrderByDescending(static group => group.Key).Take(depth)
                .Select(static group => new QuoteChange(group.Key,
                    group.Sum(static entry => entry.Quantity),
                    group.Sum(static entry => entry.OrderCount)))],
            Asks = [.. levels.Where(static entry =>
                    entry.Side == NdaxSides.Sell)
                .GroupBy(static entry => entry.Price)
                .OrderBy(static group => group.Key).Take(depth)
                .Select(static group => new QuoteChange(group.Key,
                    group.Sum(static entry => entry.Quantity),
                    group.Sum(static entry => entry.OrderCount)))],
        }, cancellationToken);
    }

    private static QuoteChange[] GetBookSide(BookState state, Sides side)
        => [.. state.Levels.Where(pair => pair.Key.Side == side)
            .Select(static pair => new QuoteChange(pair.Key.Price,
                pair.Value.Volume, pair.Value.OrderCount))
            .OrderBy(value => side == Sides.Buy
                ? -value.Price
                : value.Price)];

    private static QuoteChange[] GetChangedBookSide(BookState state,
        HashSet<BookLevelKey> changed, Sides side)
        => [.. changed.Where(value => value.Side == side)
            .Select(value => state.Levels.TryGetValue(value, out var level)
                ? new QuoteChange(value.Price, level.Volume,
                    level.OrderCount)
                : new QuoteChange(value.Price, 0m, 0))];

    private static SecurityStates ToSecurityState(string status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "running" or "open" => SecurityStates.Trading,
            _ => SecurityStates.Stoped,
        };

    private DateTime GetTime(DateTime value)
        => value == default ? CurrentTime : value.ToUtcTime();

    private static int GetCandleCount(MarketDataMessage message,
        TimeSpan timeFrame, DateTime to)
    {
        if (message.Count is long count)
            return count.Min(10000).Max(1).To<int>();
        if (message.From is DateTime from && to > from)
            return ((to - from.ToUniversalTime()).Ticks /
                timeFrame.Ticks + 1).Min(10000L).Max(1L).To<int>();
        return 300;
    }

    private async ValueTask CompleteMarketSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
