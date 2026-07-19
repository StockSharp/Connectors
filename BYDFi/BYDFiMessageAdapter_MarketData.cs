namespace StockSharp.BYDFi;

public partial class BYDFiMessageAdapter
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
        BYDFiProduct[] products;
        using (_sync.EnterScope())
            products = [.. _products.Values];
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var product in products.OrderBy(static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.BYDFi))
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
                "BYDFi does not expose historical Level1 events.");

        var product = GetProduct(mdMsg.SecurityId);
        var ticker = (await RestClient.GetTickersAsync(product.Symbol,
            cancellationToken)).FirstOrDefault();
        if (ticker is not null)
            await SendTickerAsync(ticker, mdMsg.TransactionId,
                cancellationToken);
        var markPrice = await RestClient.GetMarkPriceAsync(product.Symbol,
            cancellationToken);
        if (markPrice is not null)
            await SendMarkPriceAsync(markPrice, mdMsg.TransactionId,
                cancellationToken);
        var book = await RestClient.GetOrderBookAsync(product.Symbol, 5,
            cancellationToken);
        if (book is not null)
            await SendBestQuotesAsync(product.Symbol, book,
                mdMsg.TransactionId, cancellationToken);
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
        try
        {
            await RefreshStreamsAsync(cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _level1Subscriptions.Remove(mdMsg.TransactionId);
            await RefreshStreamsAsync(cancellationToken);
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
                "BYDFi does not expose historical order-book events.");

        var product = GetProduct(mdMsg.SecurityId);
        var depth = NormalizeStreamDepth(mdMsg.MaxDepth);
        var snapshot = await RestClient.GetOrderBookAsync(product.Symbol,
            depth, cancellationToken);
        if (snapshot is not null)
            await SendBookAsync(product.Symbol, snapshot, depth,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
                Depth = depth,
            });
        try
        {
            await RefreshStreamsAsync(cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _depthSubscriptions.Remove(mdMsg.TransactionId);
            await RefreshStreamsAsync(cancellationToken);
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
            UnsubscribeTicks(mdMsg.OriginalTransactionId);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var product = GetProduct(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
        var trades = await RestClient.GetPublicTradesAsync(product.Symbol,
            maximum, cancellationToken);
        var ordered = (trades ?? [])
            .Where(trade => IsTradeInRange(trade, mdMsg.From, mdMsg.To))
            .OrderBy(static trade => trade.Time)
            .ToArray();
        foreach (var trade in ordered)
            await SendPublicTradeAsync(trade, mdMsg.TransactionId,
                cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
                LastTradeId = trades?.FirstOrDefault()?.Id,
            });
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
        var candles = await LoadCandlesAsync(product.Symbol, timeFrame, from,
            to, maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(candle, timeFrame, mdMsg.TransactionId,
                cancellationToken);
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
        try
        {
            await RefreshStreamsAsync(cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _candleSubscriptions.Remove(mdMsg.TransactionId);
            await RefreshStreamsAsync(cancellationToken);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(BYDFiProduct product,
        long originalTransactionId)
    {
        var factor = product.ContractFactor.ToDecimal() ?? 1m;
        var volumeStep = product.IsInverse
            ? 1m
            : product.VolumePrecision.PrecisionToStep();
        return new SecurityMessage
        {
            SecurityId = product.Symbol.ToStockSharp(),
            Name = product.Symbol,
            ShortName = product.Symbol,
            SecurityType = SecurityTypes.Future,
            Currency = product.QuoteAsset.ToCurrency(),
            PriceStep = product.OrderPricePrecision.PrecisionToStep(),
            Decimals = product.OrderPricePrecision,
            VolumeStep = volumeStep,
            MinVolume = product.LimitMinimumQuantity *
                (product.IsInverse ? 1m : factor),
            MaxVolume = product.LimitMaximumQuantity *
                (product.IsInverse ? 1m : factor),
            Multiplier = factor,
            OriginalTransactionId = originalTransactionId,
        }.TryFillUnderlyingId(product.BaseAsset?.ToUpperInvariant());
    }

    private async ValueTask RefreshStreamsAsync(
        CancellationToken cancellationToken)
    {
        string[] streams;
        using (_sync.EnterScope())
        {
            var result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in _level1Subscriptions.Values
                .Select(static value => value.Symbol).Distinct(
                    StringComparer.OrdinalIgnoreCase))
            {
                result.Add($"{symbol}@ticker");
                result.Add($"{symbol}@realTicker@1000ms");
            }
            foreach (var group in _depthSubscriptions.Values.GroupBy(
                static value => value.Symbol,
                StringComparer.OrdinalIgnoreCase))
                result.Add($"{group.Key}@depth{group.Max(
                    static value => value.Depth)}@100ms");
            foreach (var candle in _candleSubscriptions.Values)
                result.Add($"{candle.Symbol}@kline_" +
                    candle.TimeFrame.ToInterval());
            streams = [.. result];
        }
        await SocketClient.SetStreamsAsync(streams, cancellationToken);
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId);
        await RefreshStreamsAsync(cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId);
        await RefreshStreamsAsync(cancellationToken);
    }

    private void UnsubscribeTicks(long transactionId)
    {
        using (_sync.EnterScope())
        {
            _tickSubscriptions.Remove(transactionId);
            _seenPublicTrades.RemoveWhere(key =>
                key.SubscriptionId == transactionId);
        }
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId);
        await RefreshStreamsAsync(cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(BYDFiWsTicker ticker,
        CancellationToken cancellationToken)
    {
        if (ticker?.Symbol.IsEmpty() != false)
            return;
        var symbol = ticker.Symbol.NormalizeSymbol();
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendWsTickerAsync(ticker, target, cancellationToken);
    }

    private async ValueTask OnSocketRealTickerAsync(
        BYDFiWsRealTicker ticker, CancellationToken cancellationToken)
    {
        if (ticker?.Symbol.IsEmpty() != false)
            return;
        var symbol = ticker.Symbol.NormalizeSymbol();
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendOutMessageAsync(new Level1ChangeMessage
            {
                SecurityId = symbol.ToStockSharp(),
                ServerTime = GetTime(ticker.EventTime),
                OriginalTransactionId = target,
            }
            .TryAdd(Level1Fields.LastTradePrice,
                ticker.LastPrice.ToDecimal())
            .TryAdd(Level1Fields.SettlementPrice,
                ticker.MarkPrice.ToDecimal())
            .TryAdd(Level1Fields.Index, ticker.IndexPrice.ToDecimal()),
                cancellationToken);
    }

    private async ValueTask OnSocketDepthAsync(BYDFiWsDepth book,
        CancellationToken cancellationToken)
    {
        if (book?.Symbol.IsEmpty() != false)
            return;
        var symbol = book.Symbol.NormalizeSymbol();
        (long Id, int Depth)[] targets;
        using (_sync.EnterScope())
            targets = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => (pair.Key, pair.Value.Depth))];
        foreach (var target in targets)
            await SendWsBookAsync(book, target.Depth, target.Id,
                cancellationToken);
    }

    private async ValueTask OnSocketKlineAsync(BYDFiWsKline candle,
        CancellationToken cancellationToken)
    {
        if (candle?.Symbol.IsEmpty() != false || candle.Interval.IsEmpty())
            return;
        var symbol = candle.Symbol.NormalizeSymbol();
        var timeFrame = candle.Interval.ToTimeFrame();
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _candleSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
                pair.Value.TimeFrame == timeFrame).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendOutMessageAsync(new TimeFrameCandleMessage
            {
                SecurityId = symbol.ToStockSharp(),
                OpenTime = candle.OpenTime.ToUtcTime(),
                CloseTime = candle.CloseTime.ToUtcTime(),
                OpenPrice = candle.Open.ToDecimal() ?? 0m,
                HighPrice = candle.High.ToDecimal() ?? 0m,
                LowPrice = candle.Low.ToDecimal() ?? 0m,
                ClosePrice = candle.Close.ToDecimal() ?? 0m,
                TotalVolume = candle.Volume.ToDecimal() ?? 0m,
                TypedArg = timeFrame,
                OriginalTransactionId = target,
                State = candle.CloseTime.ToUtcTime() <= CurrentTime
                    ? CandleStates.Finished
                    : CandleStates.Active,
            }, cancellationToken);
    }

    private ValueTask SendTickerAsync(BYDFiTicker ticker, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.Symbol.ToStockSharp(),
            ServerTime = GetTime(ticker.Time),
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last.ToDecimal())
        .TryAdd(Level1Fields.OpenPrice, ticker.Open.ToDecimal())
        .TryAdd(Level1Fields.HighPrice, ticker.High.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.Low.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal()),
            cancellationToken);

    private ValueTask SendWsTickerAsync(BYDFiWsTicker ticker, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.Symbol.ToStockSharp(),
            ServerTime = GetTime(ticker.EventTime),
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice.ToDecimal())
        .TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal()),
            cancellationToken);

    private ValueTask SendMarkPriceAsync(BYDFiMarkPrice price, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = price.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.SettlementPrice, price.MarkPrice.ToDecimal())
        .TryAdd(Level1Fields.Index, price.IndexPrice.ToDecimal()),
            cancellationToken);

    private ValueTask SendBestQuotesAsync(string symbol, BYDFiOrderBook book,
        long target, CancellationToken cancellationToken)
    {
        var bid = (book.ObjectBids ?? [])
            .Select(ToLevel).Where(static value => value is not null)
            .OrderByDescending(static value => value.Price).FirstOrDefault();
        var ask = (book.ObjectAsks ?? [])
            .Select(ToLevel).Where(static value => value is not null)
            .OrderBy(static value => value.Price).FirstOrDefault();
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = GetTime(book.EventTime),
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.BestBidPrice, bid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
        .TryAdd(Level1Fields.BestAskPrice, ask?.Price)
        .TryAdd(Level1Fields.BestAskVolume, ask?.Volume), cancellationToken);
    }

    private ValueTask SendBookAsync(string symbol, BYDFiOrderBook book,
        int depth, long target, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = GetTime(book.EventTime),
            OriginalTransactionId = target,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes((book.ObjectBids ?? []).Select(ToLevel), depth,
                true),
            Asks = ToQuotes((book.ObjectAsks ?? []).Select(ToLevel), depth,
                false),
        }, cancellationToken);

    private ValueTask SendWsBookAsync(BYDFiWsDepth book, int depth,
        long target, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = book.Symbol.ToStockSharp(),
            ServerTime = GetTime(book.EventTime),
            OriginalTransactionId = target,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(book.Bids, depth, true),
            Asks = ToQuotes(book.Asks, depth, false),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(BYDFiPublicTrade trade,
        long target, CancellationToken cancellationToken)
    {
        if (trade?.Id.IsEmpty() != false)
            return default;
        using (_sync.EnterScope())
            if (!_seenPublicTrades.Add(new(target, trade.Id)))
                return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = trade.Symbol.ToStockSharp(),
            ServerTime = GetTime(trade.Time),
            OriginalTransactionId = target,
            TradeStringId = trade.Id,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = (trade.Quantity.IsEmpty()
                ? trade.LegacyVolume
                : trade.Quantity).ToDecimal(),
            OriginSide = trade.Side.ToStockSharpSide(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(BYDFiKline candle,
        TimeSpan timeFrame, long target,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime() ?? default;
        var closeTime = openTime + timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = candle.Symbol.ToStockSharp(),
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

    private async ValueTask PollPublicTradesAsync(
        CancellationToken cancellationToken)
    {
        (string Symbol, long[] Targets)[] groups;
        using (_sync.EnterScope())
            groups = [.. _tickSubscriptions.GroupBy(
                static pair => pair.Value.Symbol,
                StringComparer.OrdinalIgnoreCase).Select(group =>
                (group.Key, group.Select(static pair => pair.Key).ToArray()))];
        foreach (var group in groups)
        {
            var trades = await RestClient.GetPublicTradesAsync(group.Symbol,
                100, cancellationToken);
            if (trades is not { Length: > 0 })
                continue;

            var latest = trades[0].Id;
            if (latest.IsEmpty())
                continue;

            foreach (var target in group.Targets)
            {
                BYDFiPublicTrade[] pending;
                using (_sync.EnterScope())
                {
                    if (!_tickSubscriptions.TryGetValue(target,
                        out var subscription))
                        continue;

                    var previous = subscription.LastTradeId;
                    subscription.LastTradeId = latest;
                    if (previous.IsEmpty())
                    {
                        pending = [];
                    }
                    else
                    {
                        var previousIndex = Array.FindIndex(trades,
                            trade => trade.Id.EqualsIgnoreCase(previous));
                        pending = previousIndex >= 0
                            ? [.. trades.Take(previousIndex)]
                            : trades;
                    }
                }

                foreach (var trade in pending.OrderBy(
                    static value => value.Time))
                    await SendPublicTradeAsync(trade, target,
                        cancellationToken);
            }
        }
    }

    private async ValueTask<BYDFiKline[]> LoadCandlesAsync(string symbol,
        TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<BYDFiKline>();
        var cursor = from.ToUniversalTime();
        var upperBound = to.ToUniversalTime();
        while (result.Count < maximum && cursor <= upperBound)
        {
            var pageSize = (maximum - result.Count).Min(1500).Max(1);
            var windowEnd = (cursor + TimeSpan.FromTicks(
                timeFrame.Ticks * (pageSize - 1))).Min(upperBound);
            var items = await RestClient.GetCandlesAsync(symbol, timeFrame,
                cursor, windowEnd, pageSize, cancellationToken);
            if (items is not { Length: > 0 })
            {
                if (windowEnd >= upperBound)
                    break;
                cursor = windowEnd + timeFrame;
                continue;
            }
            result.AddRange(items);
            var last = items.Max(static item => item.OpenTime.ToLong()) ?? 0;
            var next = last.ToUtcTime() + timeFrame;
            if (next <= cursor)
                break;
            cursor = next;
        }
        return [.. result.Where(static item => item?.OpenTime.ToLong() > 0)
            .GroupBy(static item => item.OpenTime)
            .Select(static group => group.First())
            .OrderBy(static item => item.OpenTime.ToLong())
            .TakeLast(maximum)];
    }

    private static BYDFiLevel ToLevel(BYDFiObjectLevel value)
        => value?.Price.ToDecimal() is decimal price &&
            value.Amount.ToDecimal() is decimal volume
            ? new() { Price = price, Volume = volume }
            : null;

    private static QuoteChange[] ToQuotes(IEnumerable<BYDFiLevel> levels,
        int depth, bool isBids)
    {
        var filtered = (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Volume >= 0);
        filtered = isBids
            ? filtered.OrderByDescending(static level => level.Price)
            : filtered.OrderBy(static level => level.Price);
        return [.. filtered.Take(depth).Select(static level =>
            new QuoteChange(level.Price, level.Volume))];
    }

    private DateTime GetTime(long timestamp)
        => timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime;

    private static bool IsTradeInRange(BYDFiPublicTrade trade,
        DateTime? from, DateTime? to)
    {
        if (trade is null)
            return false;
        var time = trade.Time.ToUtcTime();
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
