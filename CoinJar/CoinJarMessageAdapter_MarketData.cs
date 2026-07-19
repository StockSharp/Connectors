namespace StockSharp.CoinJar;

public partial class CoinJarMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        var securityTypes = lookupMsg.GetSecurityTypes();
        var requestedProduct = lookupMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : lookupMsg.SecurityId.SecurityCode.NormalizeProduct();
        CoinJarProduct[] products;
        using (_sync.EnterScope())
            products = [.. _products.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var product in products.OrderBy(static value => value.Id,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinJar))
                continue;
            if (!requestedProduct.IsEmpty() &&
                !requestedProduct.EqualsIgnoreCase(product.Id))
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
                "CoinJar does not expose historical Level1 events.");

        var product = GetProduct(mdMsg.SecurityId);
        var ticker = await RestClient.GetTickerAsync(product.Id,
            cancellationToken);
        await SendTickerAsync(product.Id, ticker, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductId = product.Id,
            });
        try
        {
            await AcquireStreamAsync(CoinJarSocketTopics.Ticker, product.Id,
                cancellationToken);
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
                "CoinJar does not expose historical order-book events.");

        var product = GetProduct(mdMsg.SecurityId);
        var requestedDepth = (mdMsg.MaxDepth ?? 40).Min(1000).Max(1);
        if (mdMsg.IsHistoryOnly())
        {
            var snapshot = await RestClient.GetOrderBookAsync(product.Id,
                requestedDepth == 1 ? 1 : requestedDepth <= 40 ? 2 : 3,
                cancellationToken);
            await SendRestBookAsync(product.Id, snapshot, requestedDepth,
                mdMsg.TransactionId, cancellationToken);
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var depth = requestedDepth.Min(40);
        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductId = product.Id,
                Depth = depth,
            });
            if (!_books.ContainsKey(product.Id))
                _books.Add(product.Id, new());
        }
        try
        {
            await AcquireStreamAsync(CoinJarSocketTopics.Book, product.Id,
                cancellationToken);
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

        var product = GetProduct(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 200).Min(10000).Max(1).To<int>();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var from = mdMsg.From?.ToUniversalTime() ?? to - TimeSpan.FromDays(1);
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(mdMsg.From),
                "CoinJar trade history start must not exceed its end.");
        var trades = await LoadPublicTradesAsync(product.Id, from, to, maximum,
            cancellationToken);
        foreach (var trade in trades)
            await SendPublicTradeAsync(product.Id, trade, mdMsg.TransactionId,
                cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductId = product.Id,
            });
        try
        {
            await AcquireStreamAsync(CoinJarSocketTopics.Trades, product.Id,
                cancellationToken);
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

        var product = GetProduct(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToCoinJarInterval();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to.SubtractPeriods(timeFrame, maximum);
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(mdMsg.From),
                "CoinJar candle history start must not exceed its end.");
        var candles = await LoadCandlesAsync(product.Id, timeFrame, from, to,
            maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(product.Id, candle, timeFrame,
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
                ProductId = product.Id,
                TimeFrame = timeFrame,
                Current = current,
            });
        try
        {
            await AcquireStreamAsync(CoinJarSocketTopics.Trades, product.Id,
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

    private SecurityMessage CreateSecurity(CoinJarProduct product,
        long originalTransactionId)
    {
        var priceStep = product.TickValue > 0 ? product.TickValue :
            GetSubunitStep(product.CounterCurrency);
        var volumeStep = GetSubunitStep(product.BaseCurrency);
        return new()
        {
            SecurityId = product.Id.ToStockSharp(),
            Name = product.Name.IsEmpty()
                ? $"{product.BaseCurrency.Code}/{product.CounterCurrency.Code}"
                : product.Name,
            ShortName = product.Id,
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = product.CounterCurrency.Code.ToCurrency(),
            PriceStep = priceStep > 0 ? priceStep : null,
            VolumeStep = volumeStep > 0 ? volumeStep : null,
            OriginalTransactionId = originalTransactionId,
        };
    }

    private static decimal GetSubunitStep(CoinJarCurrency currency)
        => currency?.SubunitToUnit > 0 ? 1m / currency.SubunitToUnit : 0m;

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(CoinJarSocketTopics.Ticker,
                subscription.ProductId, cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(CoinJarSocketTopics.Book,
                subscription.ProductId, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(CoinJarSocketTopics.Trades,
                subscription.ProductId, cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(CoinJarSocketTopics.Trades,
                subscription.ProductId, cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(string productId,
        CoinJarTicker ticker, CoinJarSocketEvents socketEvent,
        CancellationToken cancellationToken)
    {
        _ = socketEvent;
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _level1Subscriptions.Where(pair =>
                pair.Value.ProductId.EqualsIgnoreCase(productId)).Select(static pair =>
                    pair.Key)];
        foreach (var transactionId in transactionIds)
            await SendTickerAsync(productId, ticker, transactionId,
                cancellationToken);
    }

    private async ValueTask OnSocketTradesAsync(string productId,
        CoinJarTrade[] trades, CoinJarSocketEvents socketEvent,
        CancellationToken cancellationToken)
    {
        long[] tickIds;
        (long Id, CandleSubscription Subscription)[] candles;
        using (_sync.EnterScope())
        {
            tickIds = [.. _tickSubscriptions.Where(pair =>
                pair.Value.ProductId.EqualsIgnoreCase(productId)).Select(static pair =>
                    pair.Key)];
            candles = [.. _candleSubscriptions.Where(pair =>
                pair.Value.ProductId.EqualsIgnoreCase(productId)).Select(static pair =>
                    (pair.Key, pair.Value))];
        }
        foreach (var trade in (trades ?? []).Where(static value => value is not null)
            .OrderBy(static value => value.Timestamp))
        {
            foreach (var transactionId in tickIds)
                await SendPublicTradeAsync(productId, trade, transactionId,
                    cancellationToken);
            if (socketEvent != CoinJarSocketEvents.Init)
                foreach (var item in candles)
                    await UpdateLiveCandleAsync(item.Id, item.Subscription, trade,
                        cancellationToken);
        }
    }

    private async ValueTask OnSocketBookAsync(string productId,
        CoinJarOrderBook update, CoinJarSocketEvents socketEvent,
        CancellationToken cancellationToken)
    {
        var isSnapshot = socketEvent is CoinJarSocketEvents.Init or
            CoinJarSocketEvents.Snapshot;
        var requestSnapshot = false;
        QuoteChangeMessage[] messages;
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(productId, out var state))
                _books.Add(productId, state = new());
            if (isSnapshot)
            {
                state.Bids.Clear();
                state.Asks.Clear();
                ApplyLevels(state.Bids, update?.Bids);
                ApplyLevels(state.Asks, update?.Asks);
                state.IsInitialized = true;
                state.IsSnapshotRequested = false;
            }
            else if (!state.IsInitialized)
            {
                if (!state.IsSnapshotRequested)
                {
                    state.IsSnapshotRequested = true;
                    requestSnapshot = true;
                }
                messages = [];
                goto done;
            }
            else
            {
                ApplyLevels(state.Bids, update?.Bids);
                ApplyLevels(state.Asks, update?.Asks);
            }

            messages = [.. _depthSubscriptions.Where(pair =>
                pair.Value.ProductId.EqualsIgnoreCase(productId)).Select(pair =>
                    BuildDepthMessage(productId, pair.Key, pair.Value, state,
                        isSnapshot)).Where(static message => message is not null)];
        done:;
        }
        if (requestSnapshot)
        {
            await SocketClient.RequestSnapshotAsync(productId, cancellationToken);
            return;
        }
        foreach (var message in messages)
            await SendOutMessageAsync(message, cancellationToken);
    }

    private QuoteChangeMessage BuildDepthMessage(string productId,
        long transactionId, DepthSubscription subscription, BookState state,
        bool isSnapshot)
    {
        var bids = state.Bids.Take(subscription.Depth).ToDictionary(
            static pair => pair.Key, static pair => pair.Value);
        var asks = state.Asks.Take(subscription.Depth).ToDictionary(
            static pair => pair.Key, static pair => pair.Value);
        QuoteChange[] bidChanges;
        QuoteChange[] askChanges;
        if (isSnapshot || !subscription.IsInitialized)
        {
            bidChanges = ToQuotes(bids);
            askChanges = ToQuotes(asks);
            isSnapshot = true;
        }
        else
        {
            bidChanges = DiffQuotes(subscription.Bids, bids);
            askChanges = DiffQuotes(subscription.Asks, asks);
            if (bidChanges.Length == 0 && askChanges.Length == 0)
                return null;
        }
        Replace(subscription.Bids, bids);
        Replace(subscription.Asks, asks);
        subscription.IsInitialized = true;
        return new()
        {
            SecurityId = productId.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
            State = isSnapshot ? QuoteChangeStates.SnapshotComplete :
                QuoteChangeStates.Increment,
            Bids = bidChanges,
            Asks = askChanges,
        };
    }

    private static QuoteChange[] DiffQuotes(
        IReadOnlyDictionary<decimal, decimal> previous,
        IReadOnlyDictionary<decimal, decimal> current)
    {
        var result = new List<QuoteChange>();
        foreach (var pair in previous)
            if (!current.ContainsKey(pair.Key))
                result.Add(new(pair.Key, 0));
        foreach (var pair in current)
            if (!previous.TryGetValue(pair.Key, out var volume) ||
                volume != pair.Value)
                result.Add(new(pair.Key, pair.Value));
        return [.. result];
    }

    private static void Replace(IDictionary<decimal, decimal> target,
        IReadOnlyDictionary<decimal, decimal> source)
    {
        target.Clear();
        foreach (var pair in source)
            target.Add(pair.Key, pair.Value);
    }

    private static void ApplyLevels(IDictionary<decimal, decimal> target,
        IEnumerable<CoinJarBookLevel> levels)
    {
        foreach (var level in levels ?? [])
        {
            if (level is null || level.Price <= 0)
                continue;
            if (level.Volume <= 0)
                target.Remove(level.Price);
            else
                target[level.Price] = level.Volume;
        }
    }

    private ValueTask SendTickerAsync(string productId, CoinJarTicker ticker,
        long transactionId, CancellationToken cancellationToken)
    {
        if (ticker is null)
            throw new InvalidDataException("CoinJar returned an empty ticker.");
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = productId.ToStockSharp(),
            ServerTime = GetTime(ticker.CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.Volume, ticker.Volume24Hours ?? ticker.Volume)
        .TryAdd(Level1Fields.ClosePrice, ticker.PreviousClose)
        .TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice)
        .TryAdd(Level1Fields.State, ticker.Status.ToSecurityState()),
            cancellationToken);
    }

    private ValueTask SendRestBookAsync(string productId, CoinJarOrderBook book,
        int depth, long transactionId, CancellationToken cancellationToken)
    {
        if (book is null)
            throw new InvalidDataException("CoinJar returned an empty order book.");
        return SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = productId.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes((book.Bids ?? []).Where(static level => level is not null)
                .OrderByDescending(static level => level.Price).Take(depth)),
            Asks = ToQuotes((book.Asks ?? []).Where(static level => level is not null)
                .OrderBy(static level => level.Price).Take(depth)),
        }, cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(string productId, CoinJarTrade trade,
        long transactionId, CancellationToken cancellationToken)
    {
        if (trade is null || !AddPublicTrade(trade.TradeId, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = productId.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = transactionId,
            TradeId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Size,
            OriginSide = trade.TakerSide.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(string productId, CoinJarCandle candle,
        TimeSpan timeFrame, long transactionId, CandleStates? state,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime();
        var closeTime = openTime.GetCloseTime(timeFrame);
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = productId.ToStockSharp(),
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
        CandleSubscription subscription, CoinJarTrade trade,
        CancellationToken cancellationToken)
    {
        var openTime = GetTime(trade.Timestamp).Align(subscription.TimeFrame);
        CoinJarCandle finished = null;
        CoinJarCandle active;
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
                    Volume = trade.Size,
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
                    Volume = current.Volume + trade.Size,
                };
            subscription.Current = active;
        }
        if (finished is not null)
            await SendCandleAsync(subscription.ProductId, finished,
                subscription.TimeFrame, transactionId, CandleStates.Finished,
                cancellationToken);
        await SendCandleAsync(subscription.ProductId, active,
            subscription.TimeFrame, transactionId, CandleStates.Active,
            cancellationToken);
    }

    private async ValueTask<CoinJarTrade[]> LoadPublicTradesAsync(
        string productId, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, CoinJarTrade>();
        var after = new DateTimeOffset(from.ToUtcTime()).ToUnixTimeSeconds();
        var before = new DateTimeOffset(to.ToUtcTime()).ToUnixTimeSeconds() + 1;
        while (result.Count < maximum && before > after)
        {
            var requested = (maximum - result.Count).Min(1000).Max(1);
            var page = await RestClient.GetTradesAsync(productId, new()
            {
                After = after,
                Before = before,
                Limit = requested,
            }, cancellationToken);
            if (page is not { Length: > 0 })
                break;
            foreach (var trade in page.Where(trade => trade is not null &&
                GetTime(trade.Timestamp) >= from && GetTime(trade.Timestamp) <= to))
                result[trade.TradeId] = trade;
            var earliest = page.Where(static trade => trade is not null)
                .Select(trade => new DateTimeOffset(GetTime(trade.Timestamp))
                    .ToUnixTimeSeconds()).DefaultIfEmpty(before).Min();
            if (earliest <= after || earliest >= before ||
                page.Length < requested)
                break;
            before = earliest;
        }
        return [.. result.Values.OrderBy(static trade => trade.Timestamp)
            .TakeLast(maximum)];
    }

    private async ValueTask<CoinJarCandle[]> LoadCandlesAsync(string productId,
        TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<DateTime, CoinJarCandle>();
        var cursorEnd = to.ToUtcTime();
        var lowerBound = from.ToUtcTime();
        while (result.Count < maximum && cursorEnd >= lowerBound)
        {
            var pageSize = (maximum - result.Count).Min(1000).Max(1);
            var pageStart = cursorEnd.SubtractPeriods(timeFrame, pageSize);
            if (pageStart < lowerBound)
                pageStart = lowerBound;
            var page = await RestClient.GetCandlesAsync(productId, new()
            {
                Interval = timeFrame.ToCoinJarInterval(),
                After = new DateTimeOffset(pageStart).ToUnixTimeSeconds(),
                Before = new DateTimeOffset(cursorEnd).ToUnixTimeSeconds(),
            }, cancellationToken);
            if (page is not { Length: > 0 })
                break;
            foreach (var candle in page.Where(candle => candle is not null &&
                candle.OpenTime.ToUtcTime() >= lowerBound &&
                candle.OpenTime.ToUtcTime() <= to))
                result[candle.OpenTime.ToUtcTime()] = candle;
            var earliest = page.Min(static candle => candle.OpenTime.ToUtcTime());
            if (earliest <= lowerBound || earliest >= cursorEnd)
                break;
            cursorEnd = earliest.SubtractPeriods(timeFrame, 1);
        }
        return [.. result.Values.OrderBy(static candle => candle.OpenTime)
            .TakeLast(maximum)];
    }

    private static QuoteChange[] ToQuotes(
        IEnumerable<CoinJarBookLevel> levels)
        => [.. (levels ?? []).Where(static level => level is not null)
            .Select(static level => new QuoteChange(level.Price, level.Volume))];

    private static QuoteChange[] ToQuotes(
        IEnumerable<KeyValuePair<decimal, decimal>> levels)
        => [.. (levels ?? []).Select(static level =>
            new QuoteChange(level.Key, level.Value))];

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
            var calculated = (to - from).Ticks / timeFrame.Ticks + 1;
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
