namespace StockSharp.Korbit;

public partial class KorbitMessageAdapter
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
        MarketDefinition[] markets;
        using (_sync.EnterScope())
            markets = [.. _markets.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in markets.OrderBy(static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.Korbit))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(market.Symbol) &&
                !requestedSymbol.CompactSymbol().EqualsIgnoreCase(
                    market.Symbol.CompactSymbol()))
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
            }.TryAdd(Level1Fields.State, market.Status ==
                KorbitPairStatuses.Launched
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
                "Korbit does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var response = await RestClient.GetTickersAsync(new()
        {
            Symbols = market.Symbol,
        }, cancellationToken);
        var ticker = (response?.FirstOrDefault()) ?? throw new InvalidDataException(
                $"Korbit returned no ticker for '{market.Symbol}'.");
        await SendTickerAsync(market.Symbol, ticker, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Ticker, market.Symbol,
                cancellationToken);
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
                "Korbit does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 30).Min(30).Max(1);
        var book = await RestClient.GetOrderBookAsync(new()
        {
            Symbol = market.Symbol,
        }, cancellationToken);
        await SendBookAsync(market.Symbol, book, depth, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                Depth = depth,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.OrderBook, market.Symbol,
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
        var requestedCount = (mdMsg.Count ?? 500).Min(500).Max(1).To<int>();
        var response = await RestClient.GetTradesAsync(new()
        {
            Symbol = market.Symbol,
            Limit = requestedCount,
        }, cancellationToken);
        var from = mdMsg.From?.ToUniversalTime();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        foreach (var trade in (response ?? [])
            .Where(trade => trade is not null &&
                (from is null || trade.Timestamp.FromKorbitTimestamp(
                    DateTime.MinValue) >= from) &&
                trade.Timestamp.FromKorbitTimestamp(DateTime.MaxValue) <= to)
            .OrderBy(static trade => trade.Timestamp)
            .TakeLast(requestedCount))
            await SendPublicTradeAsync(market.Symbol, trade,
                mdMsg.TransactionId, cancellationToken, false);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Trade, market.Symbol,
                cancellationToken);
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
        var interval = timeFrame.ToKorbitInterval();
        var now = CurrentTime.Kind == DateTimeKind.Utc
            ? CurrentTime
            : CurrentTime.ToUniversalTime();
        var to = (mdMsg.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var count = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to - TimeSpan.FromTicks(timeFrame.Ticks * count);
        var candles = await DownloadCandlesAsync(market.Symbol, interval, from,
            to, count, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market.Symbol, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var current = candles.LastOrDefault(candle =>
            candle.Timestamp.FromKorbitTimestamp(DateTime.MinValue) +
                timeFrame > now);
        using (_sync.EnterScope())
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                TimeFrame = timeFrame,
                Current = current is null ? null : CopyCandle(current),
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Trade, market.Symbol,
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

    private static SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.Symbol.ToStockSharp(),
            Name = $"{market.BaseCurrency}/{market.QuoteCurrency}",
            ShortName = $"{market.BaseCurrency}/{market.QuoteCurrency}",
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteCurrency.ToCurrency(),
            PriceStep = 0.0001m,
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask<KorbitCandle[]> DownloadCandlesAsync(string symbol,
        string interval, DateTime from, DateTime to, int count,
        CancellationToken cancellationToken)
    {
        var values = new SortedDictionary<long, KorbitCandle>();
        var fromTimestamp = new DateTimeOffset(from).ToUnixTimeMilliseconds();
        var cursor = new DateTimeOffset(to).ToUnixTimeMilliseconds();
        while (values.Count < count && cursor >= fromTimestamp)
        {
            var pageSize = (count - values.Count).Min(200).Max(1);
            var page = await RestClient.GetCandlesAsync(new()
            {
                Symbol = symbol,
                Interval = interval,
                Start = fromTimestamp,
                End = cursor,
                Limit = pageSize,
            }, cancellationToken) ?? [];
            if (page.Length == 0)
                break;
            foreach (var candle in page)
                if (candle is not null && candle.Timestamp >= fromTimestamp &&
                    candle.Timestamp <= cursor)
                    values[candle.Timestamp] = candle;
            var earliest = page.Where(static candle => candle is not null)
                .Select(static candle => candle.Timestamp).DefaultIfEmpty().Min();
            if (earliest <= fromTimestamp || earliest <= 0 ||
                page.Length < pageSize)
                break;
            cursor = earliest - 1;
        }
        return [.. values.Values.TakeLast(count)];
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Ticker, subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.OrderBook, subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Trade, subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Trade, subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(KorbitSocketBookMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Symbol.IsEmpty() != false || message.Data is null)
            return;
        var symbol = GetMarket(message.Symbol).Symbol;
        KeyValuePair<long, DepthSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol))];
        foreach (var pair in subscriptions)
            await SendBookAsync(symbol, message.Data, pair.Value.Depth, pair.Key,
                cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(
        KorbitSocketTickerMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Symbol.IsEmpty() != false || message.Data is null)
            return;
        var symbol = GetMarket(message.Symbol).Symbol;
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol))];
        foreach (var pair in subscriptions)
            await SendTickerAsync(symbol, message.Data, pair.Key,
                cancellationToken, message.Timestamp);
    }

    private async ValueTask OnSocketTradeAsync(
        KorbitSocketTradeMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Symbol.IsEmpty() != false)
            return;
        var symbol = GetMarket(message.Symbol).Symbol;
        foreach (var trade in (message.Data ?? []).OrderBy(static value =>
            value.Timestamp))
        {
            if (trade is null || !AddPublicTrade(symbol, trade.TradeId))
                continue;
            KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
            using (_sync.EnterScope())
                tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
                    pair.Value.Symbol.EqualsIgnoreCase(symbol))];
            foreach (var pair in tickSubscriptions)
                await SendPublicTradeAsync(symbol, trade, pair.Key,
                    cancellationToken, false);
            if (message.IsSnapshot != true)
                await UpdateCandlesAsync(symbol, trade, cancellationToken);
        }
    }

    private ValueTask SendTickerAsync(string symbol, KorbitTicker ticker,
        long transactionId, CancellationToken cancellationToken,
        long messageTimestamp = 0)
    {
        var serverTime = (messageTimestamp > 0
            ? messageTimestamp
            : ticker.LastTradedAt).FromKorbitTimestamp(CurrentTime);
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.OpenPrice, ticker.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Close)
        .TryAdd(Level1Fields.LastTradeTime,
            ticker.LastTradedAt.FromKorbitTimestamp(serverTime))
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice)
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice)
        .TryAdd(Level1Fields.Volume, ticker.Volume)
        .TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
        .TryAdd(Level1Fields.Change, ticker.PriceChangePercent),
            cancellationToken);
    }

    private ValueTask SendBookAsync(string symbol, KorbitOrderBook book,
        int depth, long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = book.Timestamp.FromKorbitTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(book.Bids, false, depth),
            Asks = ToQuotes(book.Asks, true, depth),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(string symbol,
        KorbitPublicTrade trade, long transactionId,
        CancellationToken cancellationToken, bool addToDeduplication = true)
    {
        if (trade is null || trade.TradeId <= 0 || trade.Price <= 0 ||
            trade.Quantity <= 0)
            return default;
        if (addToDeduplication && !AddPublicTrade(symbol, trade.TradeId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = trade.Timestamp.FromKorbitTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            TradeId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            OriginSide = trade.IsBuyerTaker ? Sides.Buy : Sides.Sell,
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(string symbol, KorbitCandle candle,
        TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
    {
        var openTime = candle.Timestamp.FromKorbitTimestamp(CurrentTime);
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

    private async ValueTask UpdateCandlesAsync(string symbol,
        KorbitPublicTrade trade, CancellationToken cancellationToken)
    {
        KeyValuePair<long, CandleSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _candleSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol))];
        foreach (var pair in subscriptions)
        {
            KorbitCandle value;
            using (_sync.EnterScope())
            {
                var subscription = pair.Value;
                var tradeTime = trade.Timestamp.FromKorbitTimestamp(CurrentTime);
                var current = subscription.Current;
                if (current is null || tradeTime >=
                    current.Timestamp.FromKorbitTimestamp(DateTime.MinValue) +
                        subscription.TimeFrame)
                {
                    current = new()
                    {
                        Timestamp = AlignTime(tradeTime,
                            subscription.TimeFrame),
                        Open = trade.Price,
                        High = trade.Price,
                        Low = trade.Price,
                        Close = trade.Price,
                        Volume = trade.Quantity,
                    };
                    subscription.Current = current;
                }
                else if (trade.Timestamp < current.Timestamp)
                    continue;
                else
                {
                    current.High = current.High.Max(trade.Price);
                    current.Low = current.Low.Min(trade.Price);
                    current.Close = trade.Price;
                    current.Volume += trade.Quantity;
                }
                value = CopyCandle(current);
            }
            await SendCandleAsync(symbol, value, pair.Value.TimeFrame, pair.Key,
                cancellationToken);
        }
    }

    private static long AlignTime(DateTime time, TimeSpan timeFrame)
    {
        var milliseconds = new DateTimeOffset(time).ToUnixTimeMilliseconds();
        var interval = (long)timeFrame.TotalMilliseconds;
        return milliseconds / interval * interval;
    }

    private static KorbitCandle CopyCandle(KorbitCandle candle)
        => new()
        {
            Timestamp = candle.Timestamp,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
        };

    private static QuoteChange[] ToQuotes(IEnumerable<KorbitBookLevel> levels,
        bool isAsk, int depth)
    {
        var filtered = (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Quantity > 0);
        return [.. (isAsk
            ? filtered.OrderBy(static level => level.Price)
            : filtered.OrderByDescending(static level => level.Price))
            .Take(depth).Select(static level =>
                new QuoteChange(level.Price, level.Quantity))];
    }

    private static int GetCandleCount(MarketDataMessage message,
        TimeSpan timeFrame, DateTime to)
    {
        if (message.Count is long count)
            return count.Min(10000).Max(1).To<int>();
        if (message.From is DateTime from && to > from)
            return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
                .Min(10000L).Max(1L).To<int>();
        return 500;
    }

    private async ValueTask CompleteMarketSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
