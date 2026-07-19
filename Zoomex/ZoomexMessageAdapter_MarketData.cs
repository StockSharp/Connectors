namespace StockSharp.Zoomex;

public partial class ZoomexMessageAdapter
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
        ZoomexProduct[] products;
        using (_sync.EnterScope())
            products = [.. _products.Values];
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var product in products.OrderBy(
            static value => value.Category).ThenBy(
            static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    product.Category.ToBoardCode()))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(product.Symbol))
                continue;
            var security = CreateSecurity(product,
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
                "Zoomex does not expose historical Level1 events.");

        var product = GetProduct(mdMsg.SecurityId);
        var ticker = (await RestClient.GetTickersAsync(product.Category,
            product.Symbol, cancellationToken))?.Items?.FirstOrDefault();
        if (ticker is not null)
            await SendTickerAsync(product.Category, ticker,
                mdMsg.TransactionId, CurrentTime, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Category = product.Category,
                Symbol = product.Symbol,
            });
        try
        {
            await GetSocket(product.Category).SubscribeAsync(
                $"tickers.{product.Symbol}", cancellationToken);
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
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "Zoomex does not expose historical order-book events.");

        var product = GetProduct(mdMsg.SecurityId);
        var depth = NormalizeDepth(mdMsg.MaxDepth);
        var topic = $"orderbook.{depth}.{product.Symbol}";
        var snapshot = await RestClient.GetOrderBookAsync(product.Category,
            product.Symbol, depth, cancellationToken);
        if (snapshot is not null)
        {
            InitializeBook(product.Category, topic, snapshot);
            await SendBookStateAsync(product.Category, topic, depth,
                mdMsg.TransactionId, snapshot.Timestamp, cancellationToken);
        }
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Category = product.Category,
                Symbol = product.Symbol,
                Depth = depth,
                Topic = topic,
            });
        try
        {
            await GetSocket(product.Category).SubscribeAsync(topic,
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _depthSubscriptions.Remove(mdMsg.TransactionId);
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

        var product = GetProduct(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
        var result = await RestClient.GetPublicTradesAsync(product.Category,
            product.Symbol, maximum, cancellationToken);
        foreach (var trade in (result?.Items ?? [])
            .Where(trade => IsTradeInRange(trade, mdMsg.From, mdMsg.To))
            .OrderBy(static trade => trade.Time.ToLong()))
            await SendPublicTradeAsync(product.Category, trade,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Category = product.Category,
                Symbol = product.Symbol,
            });
        try
        {
            await GetSocket(product.Category).SubscribeAsync(
                $"publicTrade.{product.Symbol}", cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _tickSubscriptions.Remove(mdMsg.TransactionId);
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
        _ = timeFrame.ToInterval();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to - TimeSpan.FromTicks(timeFrame.Ticks * maximum);
        var candles = await LoadCandlesAsync(product.Category,
            product.Symbol, timeFrame, from, to, maximum,
            cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(product.Category, product.Symbol, candle,
                timeFrame, mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Category = product.Category,
                Symbol = product.Symbol,
                TimeFrame = timeFrame,
            });
        try
        {
            await GetSocket(product.Category).SubscribeAsync(
                $"kline.{timeFrame.ToInterval()}.{product.Symbol}",
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _candleSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(ZoomexProduct product,
        long originalTransactionId)
    {
        var lot = product.LotSizeFilter;
        var volumeStep = (product.Category == ZoomexCategories.Spot
            ? lot?.BasePrecision
            : lot?.QuantityStep).ToDecimal();
        return new SecurityMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.Category),
            Name = product.Symbol,
            ShortName = product.Symbol,
            SecurityType = product.Category == ZoomexCategories.Spot
                ? SecurityTypes.CryptoCurrency
                : SecurityTypes.Future,
            Currency = product.QuoteCoin.ToCurrency(),
            PriceStep = product.PriceFilter?.TickSize.ToDecimal(),
            VolumeStep = volumeStep,
            MinVolume = lot?.MinimumOrderQuantity.ToDecimal(),
            MaxVolume = lot?.MaximumOrderQuantity.ToDecimal(),
            IssueDate = product.LaunchTime.ToUtcTime(),
            OriginalTransactionId = originalTransactionId,
        }.TryFillUnderlyingId(product.BaseCoin?.ToUpperInvariant());
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription;
        using (_sync.EnterScope())
        {
            _level1Subscriptions.TryGetValue(transactionId,
                out subscription);
            _level1Subscriptions.Remove(transactionId);
        }
        if (subscription is not null)
            await GetSocket(subscription.Category).UnsubscribeAsync(
                $"tickers.{subscription.Symbol}", cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription;
        using (_sync.EnterScope())
        {
            _depthSubscriptions.TryGetValue(transactionId,
                out subscription);
            _depthSubscriptions.Remove(transactionId);
            if (subscription is not null && !_depthSubscriptions.Values.Any(
                value => value.Category == subscription.Category &&
                    value.Topic.EqualsIgnoreCase(subscription.Topic)))
            {
                _books.Remove(GetBookKey(subscription.Category,
                    subscription.Topic));
            }
        }
        if (subscription is not null)
            await GetSocket(subscription.Category).UnsubscribeAsync(
                subscription.Topic, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription;
        using (_sync.EnterScope())
        {
            _tickSubscriptions.TryGetValue(transactionId,
                out subscription);
            _tickSubscriptions.Remove(transactionId);
            _seenPublicTrades.RemoveWhere(key =>
                key.SubscriptionId == transactionId);
        }
        if (subscription is not null)
            await GetSocket(subscription.Category).UnsubscribeAsync(
                $"publicTrade.{subscription.Symbol}", cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription;
        using (_sync.EnterScope())
        {
            _candleSubscriptions.TryGetValue(transactionId,
                out subscription);
            _candleSubscriptions.Remove(transactionId);
        }
        if (subscription is not null)
            await GetSocket(subscription.Category).UnsubscribeAsync(
                $"kline.{subscription.TimeFrame.ToInterval()}." +
                subscription.Symbol, cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(ZoomexCategories category,
        ZoomexTicker ticker, long timestamp,
        CancellationToken cancellationToken)
    {
        if (ticker?.Symbol.IsEmpty() != false)
            return;
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Category == category &&
                pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol)).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendTickerAsync(category, ticker, target,
                GetTime(timestamp), cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(ZoomexCategories category,
        ZoomexOrderBook book, string topic, ZoomexWsUpdateTypes? type,
        long timestamp, CancellationToken cancellationToken)
    {
        if (book?.Symbol.IsEmpty() != false || topic.IsEmpty())
            return;
        (long Id, int Depth)[] targets;
        using (_sync.EnterScope())
        {
            var key = GetBookKey(category, topic);
            if (!_books.TryGetValue(key, out var state))
                _books[key] = state = new();
            if (type == ZoomexWsUpdateTypes.Snapshot || book.UpdateId == 1)
            {
                state.Bids.Clear();
                state.Asks.Clear();
            }
            else if (state.Sequence > 0 && book.Sequence > 0 &&
                book.Sequence <= state.Sequence)
            {
                return;
            }
            ApplyLevels(state.Bids, book.Bids);
            ApplyLevels(state.Asks, book.Asks);
            state.Sequence = book.Sequence;
            targets = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Category == category &&
                pair.Value.Topic.EqualsIgnoreCase(topic)).Select(
                static pair => (pair.Key, pair.Value.Depth))];
        }
        foreach (var target in targets)
            await SendBookStateAsync(category, topic, target.Depth,
                target.Id, timestamp, cancellationToken);
    }

    private async ValueTask OnSocketTradesAsync(ZoomexCategories category,
        ZoomexWsPublicTrade[] trades, long timestamp,
        CancellationToken cancellationToken)
    {
        _ = timestamp;
        foreach (var trade in trades.OrderBy(static value => value.Time))
        {
            if (trade?.Symbol.IsEmpty() != false)
                continue;
            long[] targets;
            using (_sync.EnterScope())
                targets = [.. _tickSubscriptions.Where(pair =>
                    pair.Value.Category == category &&
                    pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol)).Select(
                    static pair => pair.Key)];
            foreach (var target in targets)
                await SendPublicTradeAsync(category, trade, target,
                    cancellationToken);
        }
    }

    private async ValueTask OnSocketCandlesAsync(ZoomexCategories category,
        string topic, ZoomexWsCandle[] candles, long timestamp,
        CancellationToken cancellationToken)
    {
        _ = timestamp;
        foreach (var candle in candles)
        {
            if (candle?.Interval.IsEmpty() != false)
                continue;
            var timeFrame = candle.Interval.ToTimeFrame();
            var symbol = topic?.Split('.').LastOrDefault();
            if (symbol.IsEmpty())
                continue;
            long[] targets;
            using (_sync.EnterScope())
                targets = [.. _candleSubscriptions.Where(pair =>
                    pair.Value.Category == category &&
                    pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
                    pair.Value.TimeFrame == timeFrame).Select(
                    static pair => pair.Key)];
            foreach (var target in targets)
                await SendOutMessageAsync(new TimeFrameCandleMessage
                {
                    SecurityId = symbol.ToStockSharp(category),
                    OpenTime = candle.Start.ToUtcTime(),
                    CloseTime = candle.End.ToUtcTime(),
                    OpenPrice = candle.Open.ToDecimal() ?? 0m,
                    HighPrice = candle.High.ToDecimal() ?? 0m,
                    LowPrice = candle.Low.ToDecimal() ?? 0m,
                    ClosePrice = candle.Close.ToDecimal() ?? 0m,
                    TotalVolume = candle.Volume.ToDecimal() ?? 0m,
                    TypedArg = timeFrame,
                    OriginalTransactionId = target,
                    State = candle.IsFinished
                        ? CandleStates.Finished
                        : CandleStates.Active,
                }, cancellationToken);
        }
    }

    private ValueTask SendTickerAsync(ZoomexCategories category,
        ZoomexTicker ticker, long target, DateTime time,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.Symbol.ToStockSharp(category),
            ServerTime = time,
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.OpenPrice,
            ticker.PreviousPrice24Hours.ToDecimal())
        .TryAdd(Level1Fields.HighPrice, ticker.HighPrice24Hours.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.LowPrice24Hours.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.Volume24Hours.ToDecimal())
        .TryAdd(Level1Fields.Turnover, ticker.Turnover24Hours.ToDecimal())
        .TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice.ToDecimal())
        .TryAdd(Level1Fields.Index, ticker.IndexPrice.ToDecimal())
        .TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest.ToDecimal())
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice.ToDecimal())
        .TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize.ToDecimal()),
            cancellationToken);

    private ValueTask SendPublicTradeAsync(ZoomexCategories category,
        ZoomexPublicTrade trade, long target,
        CancellationToken cancellationToken)
    {
        if (trade?.ExecutionId.IsEmpty() != false)
            return default;
        using (_sync.EnterScope())
            if (!TryRemember(_seenPublicTrades, _publicTradeDeliveryOrder,
                new(target, trade.ExecutionId)))
                return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = trade.Symbol.ToStockSharp(category),
            ServerTime = trade.Time.ToUtcTime() ?? CurrentTime,
            OriginalTransactionId = target,
            TradeStringId = trade.ExecutionId,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Size.ToDecimal(),
            OriginSide = trade.Side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(ZoomexCategories category,
        ZoomexWsPublicTrade trade, long target,
        CancellationToken cancellationToken)
    {
        if (trade?.TradeId.IsEmpty() != false)
            return default;
        using (_sync.EnterScope())
            if (!TryRemember(_seenPublicTrades, _publicTradeDeliveryOrder,
                new(target, trade.TradeId)))
                return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = trade.Symbol.ToStockSharp(category),
            ServerTime = GetTime(trade.Time),
            OriginalTransactionId = target,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Volume.ToDecimal(),
            OriginSide = trade.Side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(ZoomexCategories category, string symbol,
        ZoomexCandle candle, TimeSpan timeFrame, long target,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime();
        var closeTime = openTime + timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = symbol.ToStockSharp(category),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open.ToDecimal() ?? 0m,
            HighPrice = candle.High.ToDecimal() ?? 0m,
            LowPrice = candle.Low.ToDecimal() ?? 0m,
            ClosePrice = candle.Close.ToDecimal() ?? 0m,
            TotalVolume = candle.Volume.ToDecimal() ?? 0m,
            TypedArg = timeFrame,
            OriginalTransactionId = target,
            State = closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);
    }

    private async ValueTask SendBookStateAsync(ZoomexCategories category,
        string topic, int depth, long target, long timestamp,
        CancellationToken cancellationToken)
    {
        QuoteChange[] bids;
        QuoteChange[] asks;
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(GetBookKey(category, topic),
                out var state))
                return;
            bids = [.. state.Bids.Take(depth).Select(static pair =>
                new QuoteChange(pair.Key, pair.Value))];
            asks = [.. state.Asks.Take(depth).Select(static pair =>
                new QuoteChange(pair.Key, pair.Value))];
        }
        var symbol = topic.Split('.').Last();
        await SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = symbol.ToStockSharp(category),
            ServerTime = GetTime(timestamp),
            OriginalTransactionId = target,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = bids,
            Asks = asks,
        }, cancellationToken);
    }

    private void InitializeBook(ZoomexCategories category, string topic,
        ZoomexOrderBook book)
    {
        using (_sync.EnterScope())
        {
            var state = new OrderBookState
            {
                Sequence = book.Sequence,
            };
            ApplyLevels(state.Bids, book.Bids);
            ApplyLevels(state.Asks, book.Asks);
            _books[GetBookKey(category, topic)] = state;
        }
    }

    private static void ApplyLevels(
        IDictionary<decimal, decimal> destination,
        IEnumerable<ZoomexLevel> levels)
    {
        foreach (var level in levels ?? [])
        {
            if (level is null || level.Price <= 0)
                continue;
            if (level.Volume <= 0)
                destination.Remove(level.Price);
            else
                destination[level.Price] = level.Volume;
        }
    }

    private async ValueTask<ZoomexCandle[]> LoadCandlesAsync(
        ZoomexCategories category, string symbol, TimeSpan timeFrame,
        DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<ZoomexCandle>();
        var lowerBound = from.ToUniversalTime();
        var cursor = to.ToUniversalTime();
        while (result.Count < maximum && cursor >= lowerBound)
        {
            var pageSize = (maximum - result.Count).Min(1000).Max(1);
            var page = await RestClient.GetCandlesAsync(category, symbol,
                timeFrame, lowerBound, cursor, pageSize, cancellationToken);
            var items = (page?.Items ?? []).Where(item => item is not null &&
                item.OpenTime > 0).ToArray();
            if (items.Length == 0)
                break;
            result.AddRange(items);
            var earliest = items.Min(static item => item.OpenTime).ToUtcTime();
            var next = earliest - TimeSpan.FromMilliseconds(1);
            if (next >= cursor)
                break;
            cursor = next;
        }
        return [.. result.Where(item => item.OpenTime.ToUtcTime() >=
                lowerBound && item.OpenTime.ToUtcTime() <= to)
            .GroupBy(static item => item.OpenTime)
            .Select(static group => group.First())
            .OrderBy(static item => item.OpenTime)
            .TakeLast(maximum)];
    }

    private DateTime GetTime(long timestamp)
        => timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime;

    private static string GetBookKey(ZoomexCategories category, string topic)
        => $"{category}:{topic}";

    private static bool IsTradeInRange(ZoomexPublicTrade trade,
        DateTime? from, DateTime? to)
    {
        if (trade?.Time.ToUtcTime() is not DateTime time)
            return false;
        return (from is null || time >= from.Value.ToUniversalTime()) &&
            (to is null || time <= to.Value.ToUniversalTime());
    }

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
