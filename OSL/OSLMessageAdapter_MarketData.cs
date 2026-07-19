namespace StockSharp.OSL;

public partial class OSLMessageAdapter
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
        OSLSymbol[] symbols;
        using (_sync.EnterScope())
            symbols = [.. _symbols.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var symbol in symbols.OrderBy(static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.OSL))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(symbol.Symbol))
                continue;
            var security = CreateSecurity(symbol, lookupMsg.TransactionId);
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
                symbol.Status.IsEmpty() ||
                symbol.Status.EqualsIgnoreCase("online")
                    ? SecurityStates.Trading
                    : SecurityStates.Stoped), cancellationToken);
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
                "OSL does not expose historical Level1 events.");

        var product = GetSymbol(mdMsg.SecurityId);
        var ticker = (await RestClient.GetTickersAsync(product.Symbol,
            cancellationToken)).FirstOrDefault();
        if (ticker is not null)
            await SendTickerAsync(product.Symbol, ticker,
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
            await AcquirePublicStreamAsync(OSLWsChannels.Ticker,
                product.Symbol, cancellationToken);
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
                "OSL does not expose historical order-book events.");

        var product = GetSymbol(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 15).Min(15).Max(1);
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
            await AcquirePublicStreamAsync(GetBookChannel(depth),
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

        var product = GetSymbol(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 100).Min(500).Max(1).To<int>();
        var trades = await RestClient.GetPublicTradesAsync(product.Symbol,
            maximum, cancellationToken);
        foreach (var trade in (trades ?? []).Where(trade =>
                IsTradeInRange(trade, mdMsg.From, mdMsg.To))
            .OrderBy(static trade => trade.Timestamp.ToLong()))
            await SendPublicTradeAsync(product.Symbol, trade,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = product.Symbol,
            });
        try
        {
            await AcquirePublicStreamAsync(OSLWsChannels.Trade,
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

        var product = GetSymbol(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToRestInterval();
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
        try
        {
            await AcquireCandleStreamAsync(product.Symbol, timeFrame,
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

    private SecurityMessage CreateSecurity(OSLSymbol product,
        long originalTransactionId)
        => new()
        {
            SecurityId = product.Symbol.ToStockSharp(),
            Name = $"{product.BaseCoin.ToUpperInvariant()}/" +
                product.QuoteCoin.ToUpperInvariant(),
            ShortName = product.Symbol.NormalizeSymbol(),
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = product.QuoteCoin.ToCurrency(),
            PriceStep = product.PricePrecision.ToStep(),
            VolumeStep = product.QuantityPrecision.ToStep(),
            MinVolume = product.MinimumTradeAmount.ToDecimal(),
            MaxVolume = product.MaximumTradeAmount.ToDecimal(),
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleasePublicStreamAsync(OSLWsChannels.Ticker,
                subscription.Symbol, cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleasePublicStreamAsync(GetBookChannel(subscription.Depth),
                subscription.Symbol, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleasePublicStreamAsync(OSLWsChannels.Trade,
                subscription.Symbol, cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseCandleStreamAsync(subscription.Symbol,
                subscription.TimeFrame, cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(OSLTicker ticker,
        CancellationToken cancellationToken)
    {
        var symbol = ticker?.InstrumentId;
        if (symbol.IsEmpty())
            symbol = ticker?.Symbol;
        if (symbol.IsEmpty())
            return;
        symbol = symbol.NormalizeSymbol();
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendTickerAsync(symbol, ticker, target, cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(string symbol,
        OSLOrderBook book, CancellationToken cancellationToken)
    {
        if (symbol.IsEmpty() || book is null)
            return;
        symbol = symbol.NormalizeSymbol();
        (long Id, int Depth)[] targets;
        using (_sync.EnterScope())
            targets = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => (pair.Key, pair.Value.Depth))];
        foreach (var target in targets)
            await SendBookAsync(symbol, book, target.Depth, target.Id,
                cancellationToken);
    }

    private async ValueTask OnSocketTradeAsync(string symbol,
        OSLPublicTrade trade, CancellationToken cancellationToken)
    {
        if (symbol.IsEmpty() || trade is null)
            return;
        symbol = symbol.NormalizeSymbol();
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _tickSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendPublicTradeAsync(symbol, trade, target,
                cancellationToken);
    }

    private async ValueTask OnSocketCandleAsync(OSLLegacyCandle candle,
        CancellationToken cancellationToken)
    {
        if (candle?.Symbol.IsEmpty() != false)
            return;
        var symbol = candle.Symbol.NormalizeSymbol();
        (long Id, TimeSpan TimeFrame)[] targets;
        using (_sync.EnterScope())
            targets = [.. _candleSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
                pair.Value.TimeFrame.ToLegacyInterval().EqualsIgnoreCase(
                    candle.Interval)).Select(static pair =>
                (pair.Key, pair.Value.TimeFrame))];
        foreach (var target in targets)
            await SendLegacyCandleAsync(candle, target.TimeFrame, target.Id,
                cancellationToken);
    }

    private ValueTask SendTickerAsync(string symbol, OSLTicker ticker,
        long transactionId, CancellationToken cancellationToken)
    {
        var open = (ticker.Open24Hours.IsEmpty()
            ? ticker.Open
            : ticker.Open24Hours).ToDecimal();
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = GetTime(ticker.Timestamp),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidVolume, ticker.BidSize.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice.ToDecimal())
        .TryAdd(Level1Fields.BestAskVolume, ticker.AskSize.ToDecimal())
        .TryAdd(Level1Fields.OpenPrice, open)
        .TryAdd(Level1Fields.HighPrice, ticker.High24Hours.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.Low24Hours.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.BaseVolume.ToDecimal())
        .TryAdd(Level1Fields.Turnover, ticker.QuoteVolume.ToDecimal())
        .TryAdd(Level1Fields.Change, ticker.Change24Hours.ToDecimal()),
            cancellationToken);
    }

    private ValueTask SendBookAsync(string symbol, OSLOrderBook book,
        int depth, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = GetTime(book.Timestamp),
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes((book.Bids ?? []).Take(depth)),
            Asks = ToQuotes((book.Asks ?? []).Take(depth)),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(string symbol,
        OSLPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || !AddPublicTrade(trade.TradeId, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = transactionId,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Size.ToDecimal(),
            OriginSide = trade.Side.ToStockSharpSide(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(string symbol, OSLCandle candle,
        TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime();
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
            TotalVolume = candle.BaseVolume,
            TotalPrice = candle.QuoteVolume,
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);
    }

    private ValueTask SendLegacyCandleAsync(OSLLegacyCandle candle,
        TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = candle.Symbol.ToStockSharp(),
            OpenTime = candle.OpenTime.ToUtcTime(),
            CloseTime = candle.CloseTime.ToUtcTime(),
            OpenPrice = candle.Open.ToDecimal() ?? 0m,
            HighPrice = candle.High.ToDecimal() ?? 0m,
            LowPrice = candle.Low.ToDecimal() ?? 0m,
            ClosePrice = candle.Close.ToDecimal() ?? 0m,
            TotalVolume = candle.Volume.ToDecimal() ?? 0m,
            TotalPrice = candle.QuoteVolume.ToDecimal() ?? 0m,
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = candle.IsClosed
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);

    private async ValueTask<OSLCandle[]> LoadCandlesAsync(string symbol,
        TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<OSLCandle>();
        var cursor = from.ToUniversalTime();
        var upperBound = to.ToUniversalTime();
        while (result.Count < maximum && cursor <= upperBound)
        {
            var pageSize = (maximum - result.Count).Min(1000).Max(1);
            var windowEnd = cursor.AddPeriods(timeFrame, pageSize - 1)
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
                candle.OpenTime.ToUtcTime() >= from &&
                candle.OpenTime.ToUtcTime() <= to));
            var last = items.Max(static candle => candle.OpenTime);
            var next = last.ToUtcTime().AddPeriods(timeFrame, 1);
            if (next <= cursor)
                break;
            cursor = next;
        }
        return [.. result.GroupBy(static candle => candle.OpenTime)
            .Select(static group => group.First())
            .OrderBy(static candle => candle.OpenTime)
            .TakeLast(maximum)];
    }

    private static OSLWsChannels GetBookChannel(int depth)
        => depth <= 5 ? OSLWsChannels.Books5 : OSLWsChannels.Books15;

    private static QuoteChange[] ToQuotes(IEnumerable<OSLBookLevel> levels)
        => [.. (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Volume >= 0).Select(static level =>
            new QuoteChange(level.Price, level.Volume))];

    private DateTime GetTime(string timestamp)
        => timestamp.ToUtcTime() ?? CurrentTime;

    private static bool IsTradeInRange(OSLPublicTrade trade,
        DateTime? from, DateTime? to)
    {
        var time = trade?.Timestamp.ToUtcTime();
        return time is null ||
            (from is null || time >= from.Value.ToUniversalTime()) &&
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
