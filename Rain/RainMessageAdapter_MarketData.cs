namespace StockSharp.Rain;

public partial class RainMessageAdapter
{
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
        RainProduct[] products;
        using (_sync.EnterScope())
            products = [.. _products.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var product in products.OrderBy(static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.Rain))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(product.Symbol))
                continue;
            var security = CreateSecurity(product, lookupMsg.TransactionId);
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
            }.TryAdd(Level1Fields.State, SecurityStates.Trading),
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
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "Rain does not expose historical Level1 events.");

        var product = GetProduct(mdMsg.SecurityId);
        await SendProductAsync(product, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
            });
        var productAcquired = false;
        try
        {
            await AcquireStreamAsync(RainSocketChannels.ProductSummary,
                product.Symbol, cancellationToken);
            productAcquired = true;
            await AcquireStreamAsync(RainSocketChannels.MarketSummary,
                product.Symbol, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _level1Subscriptions.Remove(mdMsg.TransactionId);
            if (productAcquired)
                await ReleaseStreamAsync(RainSocketChannels.ProductSummary,
                    product.Symbol, cancellationToken);
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
        if (mdMsg.From is not null || mdMsg.IsHistoryOnly())
            throw new NotSupportedException(
                "Rain order books are available as a live WebSocket snapshot and incremental stream only.");

        var product = GetProduct(mdMsg.SecurityId);
        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
                Depth = (mdMsg.MaxDepth ?? 500).Min(500).Max(1),
            });
            if (!_books.ContainsKey(product.Symbol))
                _books.Add(product.Symbol, new());
        }
        try
        {
            await AcquireStreamAsync(RainSocketChannels.OrderBook,
                product.Symbol, cancellationToken);
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
        if (mdMsg.From is not null || mdMsg.To is not null ||
            mdMsg.IsHistoryOnly())
            throw new NotSupportedException(
                "Rain public trades are available through the live WebSocket stream only.");

        var product = GetProduct(mdMsg.SecurityId);
        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
            });
        try
        {
            await AcquireStreamAsync(RainSocketChannels.Trades,
                product.Symbol, cancellationToken);
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

        var product = GetProduct(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToWire();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to.AddPeriods(timeFrame, -maximum);
        var candles = await LoadCandlesAsync(product.Symbol, timeFrame, from,
            to, maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(product.Symbol, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
                TimeFrame = timeFrame,
            });
        var selector = $"{product.Symbol};{timeFrame.ToWire()}";
        try
        {
            await AcquireStreamAsync(RainSocketChannels.Candles, selector,
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeCandlesAsync(mdMsg.TransactionId,
                cancellationToken);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(RainProduct product,
        long originalTransactionId)
        => new()
        {
            SecurityId = product.Symbol.ToStockSharp(),
            Name = $"{product.BaseCurrency.Code.ToUpperInvariant()}/" +
                product.ReferenceCurrency.Code.ToUpperInvariant(),
            ShortName = product.Symbol.NormalizeSymbol(),
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = product.ReferenceCurrency.Code.ToCurrency(),
            PriceStep = product.ReferencePrecision > 0
                ? product.ReferencePrecision
                : null,
            VolumeStep = product.BasePrecision > 0
                ? product.BasePrecision
                : null,
            MinVolume = product.Minimum > 0 ? product.Minimum : null,
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is null)
            return;
        await ReleaseStreamAsync(RainSocketChannels.ProductSummary,
            subscription.Symbol, cancellationToken);
        await ReleaseStreamAsync(RainSocketChannels.MarketSummary,
            subscription.Symbol, cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(RainSocketChannels.OrderBook,
                subscription.Symbol, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(RainSocketChannels.Trades,
                subscription.Symbol, cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(RainSocketChannels.Candles,
                $"{subscription.Symbol};{subscription.TimeFrame.ToWire()}",
                cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(RainSocketBook book,
        CancellationToken cancellationToken)
    {
        if (book?.Symbol.IsEmpty() != false)
            return;
        var symbol = book.Symbol.NormalizeSymbol();
        (long Id, int Depth)[] subscriptions;
        var isSnapshot = book.Sequence == 0;
        var isValid = false;
        var refresh = false;
        long expected = 0;
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(symbol, out var state))
                _books.Add(symbol, state = new());
            if (isSnapshot)
            {
                state.Sequence = 0;
                state.IsSnapshotReady = true;
                state.IsRefreshPending = false;
                isValid = true;
            }
            else
            {
                expected = state.Sequence + 1;
                isValid = state.IsSnapshotReady && book.Sequence == expected;
                if (isValid)
                    state.Sequence = book.Sequence;
                else
                {
                    state.IsSnapshotReady = false;
                    if (!state.IsRefreshPending)
                    {
                        state.IsRefreshPending = true;
                        refresh = true;
                    }
                }
            }
            subscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => (pair.Key, pair.Value.Depth))];
        }

        if (!isValid)
        {
            if (refresh)
            {
                this.AddWarningLog(
                    "Rain {0} order-book sequence gap. Expected {1}, received {2}. Requesting a new snapshot.",
                    symbol, expected, book.Sequence);
                await SocketClient.RefreshAsync(RainSocketChannels.OrderBook,
                    symbol, cancellationToken);
            }
            return;
        }

        foreach (var subscription in subscriptions)
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                SecurityId = symbol.ToStockSharp(),
                ServerTime = CurrentTime,
                OriginalTransactionId = subscription.Id,
                State = isSnapshot
                    ? QuoteChangeStates.SnapshotComplete
                    : QuoteChangeStates.Increment,
                SeqNum = book.Sequence,
                Bids = ToQuotes(isSnapshot
                    ? (book.Bids ?? []).Take(subscription.Depth)
                    : book.Bids),
                Asks = ToQuotes(isSnapshot
                    ? (book.Asks ?? []).Take(subscription.Depth)
                    : book.Asks),
            }, cancellationToken);
    }

    private async ValueTask OnSocketTradesAsync(RainSocketTrades payload,
        CancellationToken cancellationToken)
    {
        if (payload?.Symbol.IsEmpty() != false ||
            payload.Trades is not { Length: > 0 })
            return;
        var symbol = payload.Symbol.NormalizeSymbol();
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _tickSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var trade in payload.Trades.Where(static value =>
            value is not null).OrderBy(static value => value.Date))
            foreach (var transactionId in transactionIds)
                await SendPublicTradeAsync(symbol, trade, transactionId,
                    cancellationToken);
    }

    private async ValueTask OnSocketCandleAsync(RainSocketCandle payload,
        CancellationToken cancellationToken)
    {
        if (payload?.Symbol.IsEmpty() != false || payload.Candle is null)
            return;
        var symbol = payload.Symbol.NormalizeSymbol();
        (long Id, TimeSpan TimeFrame)[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _candleSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
                (payload.Interval.IsEmpty() ||
                    pair.Value.TimeFrame.ToWire().EqualsIgnoreCase(
                        payload.Interval))).Select(static pair =>
                (pair.Key, pair.Value.TimeFrame))];
        if (payload.Interval.IsEmpty() && subscriptions.Select(
            static value => value.TimeFrame).Distinct().Take(2).Count() > 1)
        {
            this.AddWarningLog(
                "Rain candle update for {0} has no interval and cannot be routed across multiple time frames.",
                symbol);
            return;
        }
        foreach (var subscription in subscriptions)
            await SendCandleAsync(symbol, payload.Candle,
                subscription.TimeFrame, subscription.Id, cancellationToken);
    }

    private async ValueTask OnSocketProductSummaryAsync(
        RainSocketProductSummary summary,
        CancellationToken cancellationToken)
    {
        if (summary?.Symbol.IsEmpty() != false)
            return;
        var symbol = summary.Symbol.NormalizeSymbol();
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var transactionId in transactionIds)
            await SendOutMessageAsync(new Level1ChangeMessage
            {
                SecurityId = symbol.ToStockSharp(),
                ServerTime = CurrentTime,
                OriginalTransactionId = transactionId,
            }
            .TryAdd(Level1Fields.BestBidPrice, summary.BidPrice?.Amount)
            .TryAdd(Level1Fields.BestAskPrice, summary.AskPrice?.Amount)
            .TryAdd(Level1Fields.LastTradePrice, summary.LastPrice?.Amount),
                cancellationToken);
    }

    private async ValueTask OnSocketMarketSummaryAsync(
        RainSocketMarketSummary summary,
        CancellationToken cancellationToken)
    {
        if (summary?.Symbol.IsEmpty() != false)
            return;
        var symbol = summary.Symbol.NormalizeSymbol();
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var transactionId in transactionIds)
            await SendOutMessageAsync(new Level1ChangeMessage
            {
                SecurityId = symbol.ToStockSharp(),
                ServerTime = CurrentTime,
                OriginalTransactionId = transactionId,
            }
            .TryAdd(Level1Fields.Change, summary.PercentChange)
            .TryAdd(Level1Fields.Volume, summary.Volume?.Amount)
            .TryAdd(Level1Fields.LowPrice, summary.Low?.Amount)
            .TryAdd(Level1Fields.HighPrice, summary.High?.Amount),
                cancellationToken);
    }

    private ValueTask SendProductAsync(RainProduct product,
        long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = product.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, product.BidPrice?.Amount)
        .TryAdd(Level1Fields.BestAskPrice, product.AskPrice?.Amount)
        .TryAdd(Level1Fields.LastTradePrice, product.LastPrice?.Amount)
        .TryAdd(Level1Fields.Volume, product.Volume?.Amount)
        .TryAdd(Level1Fields.Change, product.Change), cancellationToken);

    private ValueTask SendPublicTradeAsync(string symbol,
        RainPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || !AddPublicTrade(trade.Id, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = GetTime(trade.Date),
            OriginalTransactionId = transactionId,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            OriginSide = trade.Side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(string symbol, RainCandle candle,
        TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
    {
        var openTime = candle.Time.ToUtcTime();
        var closeTime = openTime + timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = symbol.ToStockSharp(),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);
    }

    private async ValueTask<RainCandle[]> LoadCandlesAsync(string symbol,
        TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<RainCandle>();
        var cursor = from.ToUtcTime();
        var upperBound = to.ToUtcTime();
        while (result.Count < maximum && cursor <= upperBound)
        {
            var pageSize = (maximum - result.Count).Min(500).Max(1);
            var windowEnd = cursor.AddPeriods(timeFrame, pageSize)
                .Min(upperBound);
            var items = await RestClient.GetCandlesAsync(symbol, timeFrame,
                cursor, windowEnd, pageSize, cancellationToken);
            if (items is not { Length: > 0 })
            {
                if (windowEnd >= upperBound)
                    break;
                cursor = windowEnd.AddPeriods(timeFrame, 1);
                continue;
            }
            result.AddRange(items.Where(candle => candle is not null &&
                candle.Time.ToUtcTime() >= from &&
                candle.Time.ToUtcTime() <= to));
            var last = items.Max(static candle => candle.Time.ToUtcTime());
            var next = last.AddPeriods(timeFrame, 1);
            if (next <= cursor)
                break;
            cursor = next;
        }
        return [.. result.GroupBy(static candle => candle.Time.ToUtcTime())
            .Select(static group => group.First())
            .OrderBy(static candle => candle.Time)
            .TakeLast(maximum)];
    }

    private static QuoteChange[] ToQuotes(IEnumerable<RainBookLevel> levels)
        => [.. (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Quantity >= 0).Select(static level =>
            new QuoteChange(level.Price, level.Quantity))];

    private DateTime GetTime(DateTime? value)
        => value is not DateTime actual || actual == default
            ? CurrentTime
            : actual.ToUtcTime();

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
