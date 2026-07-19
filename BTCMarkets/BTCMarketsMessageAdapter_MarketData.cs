namespace StockSharp.BTCMarkets;

public partial class BTCMarketsMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        var securityTypes = lookupMsg.GetSecurityTypes();
        var requestedMarket = lookupMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : lookupMsg.SecurityId.SecurityCode.NormalizeMarket();
        BTCMarketsMarket[] markets;
        using (_sync.EnterScope())
            markets = [.. _markets.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in markets.OrderBy(static value => value.MarketId,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.BTCMarkets))
                continue;
            if (!requestedMarket.IsEmpty() &&
                !requestedMarket.EqualsIgnoreCase(market.MarketId))
                continue;
            var security = CreateSecurity(market, lookupMsg.TransactionId);
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
            }.TryAdd(Level1Fields.State, market.Status.ToStockSharp()),
                cancellationToken);
            if (--left <= 0)
                break;
        }
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "BTC Markets does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var ticker = await RestClient.GetTickerAsync(market.MarketId,
            cancellationToken);
        await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                MarketId = market.MarketId,
            });
        try
        {
            await AcquireStreamAsync(BTCMarketsSocketChannels.Tick,
                market.MarketId, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeLevel1Async(mdMsg.TransactionId, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "BTC Markets does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? int.MaxValue).Max(1);
        if (mdMsg.IsHistoryOnly())
        {
            var snapshot = await RestClient.GetOrderBookAsync(market.MarketId,
                depth <= 50 ? 1 : 2, cancellationToken);
            await SendRestBookAsync(snapshot, depth, mdMsg.TransactionId,
                cancellationToken);
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                MarketId = market.MarketId,
                Depth = depth,
            });
            if (!_books.ContainsKey(market.MarketId))
                _books.Add(market.MarketId, new());
        }
        try
        {
            await AcquireStreamAsync(BTCMarketsSocketChannels.OrderBookUpdate,
                market.MarketId, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeDepthAsync(mdMsg.TransactionId, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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

        var market = GetMarket(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 200).Min(10000).Max(1).To<int>();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var from = mdMsg.From?.ToUniversalTime() ?? to - TimeSpan.FromDays(1);
        var trades = await LoadPublicTradesAsync(market.MarketId, from, to,
            maximum, cancellationToken);
        foreach (var trade in trades)
            await SendPublicTradeAsync(market.MarketId, trade,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                MarketId = market.MarketId,
            });
        try
        {
            await AcquireStreamAsync(BTCMarketsSocketChannels.Trade,
                market.MarketId, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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

        var market = GetMarket(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToWire();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to.SubtractPeriods(timeFrame, maximum);
        var candles = await LoadCandlesAsync(market.MarketId, timeFrame, from,
            to, maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market.MarketId, candle, timeFrame,
                mdMsg.TransactionId, null, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var current = candles.LastOrDefault(candle =>
            candle.OpenTime.GetCloseTime(timeFrame) > now);
        using (_sync.EnterScope())
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                MarketId = market.MarketId,
                TimeFrame = timeFrame,
                Current = current,
            });
        try
        {
            await AcquireStreamAsync(BTCMarketsSocketChannels.Trade,
                market.MarketId, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeCandlesAsync(mdMsg.TransactionId,
                cancellationToken);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(BTCMarketsMarket market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.MarketId.ToStockSharp(),
            Name = $"{market.BaseAsset}/{market.QuoteAsset}",
            ShortName = market.MarketId,
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteAsset.ToCurrency(),
            PriceStep = market.PriceDecimals.ToStep(),
            VolumeStep = market.AmountDecimals.ToStep(),
            MinVolume = market.MinimumOrderAmount > 0
                ? market.MinimumOrderAmount
                : null,
            MaxVolume = market.MaximumOrderAmount > 0
                ? market.MaximumOrderAmount
                : null,
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(BTCMarketsSocketChannels.Tick,
                subscription.MarketId, cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(BTCMarketsSocketChannels.OrderBookUpdate,
                subscription.MarketId, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(BTCMarketsSocketChannels.Trade,
                subscription.MarketId, cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(BTCMarketsSocketChannels.Trade,
                subscription.MarketId, cancellationToken);
    }

    private async ValueTask OnSocketTickAsync(BTCMarketsSocketTick ticker,
        CancellationToken cancellationToken)
    {
        if (ticker?.MarketId.IsEmpty() != false)
            return;
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _level1Subscriptions
                .Where(pair => pair.Value.MarketId.EqualsIgnoreCase(ticker.MarketId))
                .Select(static pair => pair.Key)];
        foreach (var transactionId in transactionIds)
            await SendSocketTickerAsync(ticker, transactionId,
                cancellationToken);
    }

    private async ValueTask OnSocketTradeAsync(BTCMarketsSocketTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade?.MarketId.IsEmpty() != false)
            return;
        long[] tickIds;
        (long Id, CandleSubscription Subscription)[] candleSubscriptions;
        using (_sync.EnterScope())
        {
            tickIds = [.. _tickSubscriptions
                .Where(pair => pair.Value.MarketId.EqualsIgnoreCase(trade.MarketId))
                .Select(static pair => pair.Key)];
            candleSubscriptions = [.. _candleSubscriptions
                .Where(pair => pair.Value.MarketId.EqualsIgnoreCase(trade.MarketId))
                .Select(static pair => (pair.Key, pair.Value))];
        }
        foreach (var transactionId in tickIds)
            await SendSocketTradeAsync(trade, transactionId, cancellationToken);
        foreach (var item in candleSubscriptions)
            await UpdateLiveCandleAsync(item.Id, item.Subscription, trade,
                cancellationToken);
    }

    private async ValueTask OnSocketOrderBookAsync(
        BTCMarketsSocketOrderBook update, CancellationToken cancellationToken)
    {
        if (update?.MarketId.IsEmpty() != false)
            return;
        var marketId = update.MarketId.NormalizeMarket();
        long[] transactionIds;
        QuoteChange[] bids = null;
        QuoteChange[] asks = null;
        var isSnapshot = update.IsSnapshot == true;
        var shouldRefresh = false;
        uint expectedChecksum = 0;
        uint actualChecksum = 0;
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(marketId, out var state))
                _books.Add(marketId, state = new());
            if (isSnapshot)
            {
                state.Bids.Clear();
                state.Asks.Clear();
                ApplyLevels(state.Bids, update.Bids);
                ApplyLevels(state.Asks, update.Asks);
                state.SnapshotId = update.SnapshotId;
                state.IsSnapshotReady = true;
                state.IsRefreshPending = false;
                bids = ToQuotes(state.Bids.Values);
                asks = ToQuotes(state.Asks.Values);
            }
            else
            {
                if (!state.IsSnapshotReady ||
                    update.SnapshotId < state.SnapshotId)
                    return;
                ApplyLevels(state.Bids, update.Bids);
                ApplyLevels(state.Asks, update.Asks);
                if (!update.Checksum.IsEmpty() &&
                    uint.TryParse(update.Checksum, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out expectedChecksum))
                {
                    actualChecksum = BTCMarketsChecksum.Calculate(
                        state.Bids.Values, state.Asks.Values);
                    if (actualChecksum != expectedChecksum)
                    {
                        state.IsSnapshotReady = false;
                        if (!state.IsRefreshPending)
                        {
                            state.IsRefreshPending = true;
                            shouldRefresh = true;
                        }
                    }
                }
                if (!state.IsSnapshotReady)
                {
                    bids = null;
                    asks = null;
                }
                else
                {
                    state.SnapshotId = update.SnapshotId;
                    bids = ToQuotes(update.Bids);
                    asks = ToQuotes(update.Asks);
                }
            }
            transactionIds = [.. _depthSubscriptions
                .Where(pair => pair.Value.MarketId.EqualsIgnoreCase(marketId))
                .Select(static pair => pair.Key)];
        }

        if (shouldRefresh)
        {
            this.AddWarningLog(
                "BTC Markets {0} order-book checksum mismatch. Expected {1}, actual {2}. Resubscribing.",
                marketId, expectedChecksum, actualChecksum);
            await SocketClient.RefreshOrderBookAsync(marketId, cancellationToken);
            return;
        }
        if (bids is null || asks is null)
            return;
        foreach (var transactionId in transactionIds)
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                SecurityId = marketId.ToStockSharp(),
                ServerTime = GetTime(update.Timestamp),
                OriginalTransactionId = transactionId,
                State = isSnapshot
                    ? QuoteChangeStates.SnapshotComplete
                    : QuoteChangeStates.Increment,
                SeqNum = update.SnapshotId,
                Bids = bids,
                Asks = asks,
            }, cancellationToken);
    }

    private static void ApplyLevels(
        IDictionary<decimal, BTCMarketsBookLevel> target,
        IEnumerable<BTCMarketsBookLevel> levels)
    {
        foreach (var level in levels ?? [])
        {
            if (level is null || level.Price <= 0)
                continue;
            if (level.Volume <= 0 || level.Count == 0)
                target.Remove(level.Price);
            else
                target[level.Price] = level;
        }
    }

    private ValueTask SendTickerAsync(BTCMarketsTicker ticker,
        long transactionId, CancellationToken cancellationToken)
    {
        if (ticker?.MarketId.IsEmpty() != false)
            throw new InvalidDataException(
                "BTC Markets returned a ticker without marketId.");
        BTCMarketsMarketStatuses? status;
        using (_sync.EnterScope())
            status = _markets.TryGetValue(ticker.MarketId, out var market)
                ? market.Status
                : null;
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.MarketId.ToStockSharp(),
            ServerTime = GetTime(ticker.Timestamp),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk)
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
        .TryAdd(Level1Fields.Volume, ticker.Volume24Hours)
        .TryAdd(Level1Fields.Turnover, ticker.QuoteVolume24Hours)
        .TryAdd(Level1Fields.HighPrice, ticker.High24Hours)
        .TryAdd(Level1Fields.LowPrice, ticker.Low24Hours)
        .TryAdd(Level1Fields.Change, ticker.PriceChange24Hours)
        .TryAdd(Level1Fields.State, status?.ToStockSharp()), cancellationToken);
    }

    private ValueTask SendSocketTickerAsync(BTCMarketsSocketTick ticker,
        long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.MarketId.ToStockSharp(),
            ServerTime = GetTime(ticker.Timestamp),
            OriginalTransactionId = transactionId,
            SeqNum = ticker.SnapshotId ?? 0,
        }
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk)
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
        .TryAdd(Level1Fields.Volume, ticker.Volume24Hours)
        .TryAdd(Level1Fields.Turnover, ticker.QuoteVolume24Hours)
        .TryAdd(Level1Fields.HighPrice, ticker.High24Hours)
        .TryAdd(Level1Fields.LowPrice, ticker.Low24Hours)
        .TryAdd(Level1Fields.Change, ticker.PriceChange24Hours), cancellationToken);

    private ValueTask SendRestBookAsync(BTCMarketsOrderBook book, int depth,
        long transactionId, CancellationToken cancellationToken)
    {
        if (book?.MarketId.IsEmpty() != false)
            throw new InvalidDataException(
                "BTC Markets returned an order book without marketId.");
        return SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = book.MarketId.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            SeqNum = book.SnapshotId,
            Bids = ToQuotes((book.Bids ?? [])
                .OrderByDescending(static level => level.Price).Take(depth)),
            Asks = ToQuotes((book.Asks ?? [])
                .OrderBy(static level => level.Price).Take(depth)),
        }, cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(string marketId,
        BTCMarketsPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || !AddPublicTrade(trade.Id, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = marketId.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = transactionId,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Amount,
            OriginSide = trade.Side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendSocketTradeAsync(BTCMarketsSocketTrade trade,
        long transactionId, CancellationToken cancellationToken)
    {
        if (!AddPublicTrade(trade.TradeId, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = trade.MarketId.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = transactionId,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Volume,
            OriginSide = trade.Side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(string marketId, BTCMarketsCandle candle,
        TimeSpan timeFrame, long transactionId, CandleStates? state,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime();
        var closeTime = openTime.GetCloseTime(timeFrame);
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = marketId.ToStockSharp(),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = state ?? (closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active),
        }, cancellationToken);
    }

    private async ValueTask UpdateLiveCandleAsync(long transactionId,
        CandleSubscription subscription, BTCMarketsSocketTrade trade,
        CancellationToken cancellationToken)
    {
        var openTime = GetTime(trade.Timestamp).Align(subscription.TimeFrame);
        BTCMarketsCandle finished = null;
        BTCMarketsCandle active;
        using (_sync.EnterScope())
        {
            var current = subscription.Current;
            if (current is not null && openTime < current.OpenTime)
                return;
            if (current is null || openTime > current.OpenTime)
            {
                finished = current;
                active = new()
                {
                    OpenTime = openTime,
                    Open = trade.Price,
                    High = trade.Price,
                    Low = trade.Price,
                    Close = trade.Price,
                    Volume = trade.Volume,
                };
            }
            else
                active = new()
                {
                    OpenTime = current.OpenTime,
                    Open = current.Open,
                    High = current.High.Max(trade.Price),
                    Low = current.Low.Min(trade.Price),
                    Close = trade.Price,
                    Volume = current.Volume + trade.Volume,
                };
            subscription.Current = active;
        }
        if (finished is not null)
            await SendCandleAsync(subscription.MarketId, finished,
                subscription.TimeFrame, transactionId, CandleStates.Finished,
                cancellationToken);
        await SendCandleAsync(subscription.MarketId, active,
            subscription.TimeFrame, transactionId, CandleStates.Active,
            cancellationToken);
    }

    private async ValueTask<BTCMarketsPublicTrade[]> LoadPublicTradesAsync(
        string marketId, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<BTCMarketsPublicTrade>();
        string before = null;
        while (result.Count < maximum)
        {
            var page = await RestClient.GetMarketTradesAsync(marketId, new()
            {
                Limit = (maximum - result.Count).Min(200).Max(1),
                Before = before,
            }, cancellationToken);
            var items = page?.Items ?? [];
            if (items.Length == 0)
                break;
            result.AddRange(items.Where(item => item is not null &&
                GetTime(item.Timestamp) >= from && GetTime(item.Timestamp) <= to));
            var earliest = items.Min(item => GetTime(item.Timestamp));
            if (earliest <= from || page.Before.IsEmpty() ||
                page.Before.EqualsIgnoreCase(before))
                break;
            before = page.Before;
        }
        return [.. result.Where(static item => !item.Id.IsEmpty())
            .GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item.Timestamp)
            .TakeLast(maximum)];
    }

    private async ValueTask<BTCMarketsCandle[]> LoadCandlesAsync(string marketId,
        TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<BTCMarketsCandle>();
        var cursorEnd = to.ToUtcTime();
        var lowerBound = from.ToUtcTime();
        while (result.Count < maximum && cursorEnd >= lowerBound)
        {
            var pageSize = (maximum - result.Count).Min(1000).Max(1);
            var pageStart = cursorEnd.SubtractPeriods(timeFrame, pageSize - 1);
            if (pageStart < lowerBound)
                pageStart = lowerBound;
            var page = await RestClient.GetCandlesAsync(marketId, new()
            {
                TimeWindow = timeFrame.ToWire(),
                From = pageStart,
                To = cursorEnd,
            }, cancellationToken);
            if (page is not { Length: > 0 })
                break;
            result.AddRange(page.Where(candle => candle is not null &&
                candle.OpenTime.ToUtcTime() >= lowerBound &&
                candle.OpenTime.ToUtcTime() <= to));
            var earliest = page.Min(static candle => candle.OpenTime.ToUtcTime());
            if (earliest <= lowerBound || page.Length < pageSize)
                break;
            cursorEnd = earliest.SubtractPeriods(timeFrame, 1);
        }
        return [.. result.GroupBy(static candle => candle.OpenTime)
            .Select(static group => group.First())
            .OrderBy(static candle => candle.OpenTime)
            .TakeLast(maximum)];
    }

    private static QuoteChange[] ToQuotes(
        IEnumerable<BTCMarketsBookLevel> levels)
        => [.. (levels ?? []).Where(static level => level is not null)
            .Select(static level => new QuoteChange(level.Price, level.Volume))];

    private DateTime GetTime(DateTime value)
        => value == default ? CurrentTime : value.ToUtcTime();

    private static int GetCandleCount(MarketDataMessage message,
        TimeSpan timeFrame, DateTime to)
    {
        if (message.Count is long count)
            return count.Min(10000).Max(1).To<int>();
        if (message.From is DateTime from && to > from)
        {
            from = from.ToUniversalTime();
            var calculated = timeFrame == TimeSpan.FromDays(30)
                ? (to.Year - from.Year) * 12L + to.Month - from.Month + 1
                : (to - from).Ticks / timeFrame.Ticks + 1;
            return calculated.Min(10000L).Max(1L).To<int>();
        }
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
