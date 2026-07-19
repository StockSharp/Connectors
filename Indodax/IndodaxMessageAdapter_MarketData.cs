namespace StockSharp.Indodax;

public partial class IndodaxMessageAdapter
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
            : lookupMsg.SecurityId.SecurityCode.NormalizePairId();
        MarketDefinition[] markets;
        using (_sync.EnterScope())
            markets = [.. _markets.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in markets.OrderBy(static value => value.PairId,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.Indodax))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(market.PairId) &&
                !requestedSymbol.EqualsIgnoreCase(
                    market.TapiPair.NormalizePairId()))
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
            }.TryAdd(Level1Fields.State,
                market.Pair.IsMaintenance == 0 &&
                market.Pair.IsMarketSuspended == 0
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
                "Indodax does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var ticker = await RestClient.GetTickerAsync(market.PairId,
            cancellationToken);
        await SendTickerAsync(market, ticker?.Ticker, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                PairId = market.PairId,
            });
        var bookAcquired = false;
        try
        {
            await AcquireStreamAsync(StreamTypes.Book, market.PairId,
                cancellationToken);
            bookAcquired = true;
            await AcquireStreamAsync(StreamTypes.Trades, market.PairId,
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            if (bookAcquired)
                await ReleaseStreamAsync(StreamTypes.Book, market.PairId,
                    CancellationToken.None);
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
                "Indodax does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 50).Min(5000).Max(1);
        var book = await RestClient.GetDepthAsync(market.PairId,
            cancellationToken);
        await SendBookAsync(market, book, depth, mdMsg.TransactionId,
            CurrentTime, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                PairId = market.PairId,
                Depth = depth,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Book, market.PairId,
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
        var trades = await RestClient.GetTradesAsync(market.PairId,
            cancellationToken);
        var from = mdMsg.From?.ToUniversalTime();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var maximum = (mdMsg.Count ?? long.MaxValue).Min(int.MaxValue).To<int>();
        foreach (var trade in (trades ?? [])
            .Where(trade => trade is not null &&
                (from is null || trade.Timestamp.FromIndodaxSeconds(
                    DateTime.MinValue) >= from) &&
                trade.Timestamp.FromIndodaxSeconds(DateTime.MaxValue) <= to)
            .OrderBy(static trade => trade.Timestamp)
            .TakeLast(maximum))
            await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
                cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                PairId = market.PairId,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Trades, market.PairId,
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
            return;
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var market = GetMarket(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        var nativeTimeFrame = timeFrame.ToIndodaxTimeFrame();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var requestedCount = (mdMsg.Count ?? 1000).Min(100000).Max(1).To<int>();
        var from = (mdMsg.From ?? to - TimeSpan.FromTicks(
            timeFrame.Ticks * requestedCount)).ToUniversalTime();
        if (from > to)
            (from, to) = (to, from);

        var candles = new Dictionary<long, IndodaxCandle>();
        var chunk = TimeSpan.FromTicks(timeFrame.Ticks * 5000L);
        for (var cursor = from; cursor <= to;)
        {
            var end = cursor + chunk;
            if (end > to)
                end = to;
            var values = await RestClient.GetCandlesAsync(market.Symbol,
                nativeTimeFrame,
                new DateTimeOffset(cursor).ToUnixTimeSeconds(),
                new DateTimeOffset(end).ToUnixTimeSeconds(),
                cancellationToken);
            foreach (var candle in values ?? [])
                if (candle is not null && candle.Time > 0)
                    candles[candle.Time] = candle;
            if (end >= to)
                break;
            cursor = end + timeFrame;
        }

        foreach (var candle in candles.Values
            .OrderBy(static value => value.Time).TakeLast(requestedCount))
            await SendOutMessageAsync(new TimeFrameCandleMessage
            {
                SecurityId = market.PairId.ToStockSharp(),
                TypedArg = timeFrame,
                OpenPrice = candle.Open,
                HighPrice = candle.High,
                LowPrice = candle.Low,
                ClosePrice = candle.Close,
                TotalVolume = candle.Volume,
                OpenTime = candle.Time.FromIndodaxSeconds(CurrentTime),
                State = CandleStates.Finished,
                OriginalTransactionId = mdMsg.TransactionId,
            }, cancellationToken);

        await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
    }

    private static SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.PairId.ToStockSharp(),
            Name = market.Pair.Description.IsEmpty()
                ? $"{market.BaseCurrency.ToUpperInvariant()}/{market.QuoteCurrency.ToUpperInvariant()}"
                : market.Pair.Description,
            ShortName =
                $"{market.BaseCurrency.ToUpperInvariant()}/{market.QuoteCurrency.ToUpperInvariant()}",
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteCurrency.ToCurrency(),
            PriceStep = market.Pair.PricePrecision > 0
                ? market.Pair.PricePrecision
                : null,
            VolumeStep = market.Pair.GetVolumeStep() > 0m
                ? market.Pair.GetVolumeStep()
                : null,
            MinVolume = market.Pair.MinimumBaseVolume > 0
                ? market.Pair.MinimumBaseVolume
                : null,
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
        await ReleaseStreamAsync(StreamTypes.Book, subscription.PairId,
            cancellationToken);
        await ReleaseStreamAsync(StreamTypes.Trades, subscription.PairId,
            cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Book, subscription.PairId,
                cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Trades, subscription.PairId,
                cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(IndodaxSocketBook book,
        CancellationToken cancellationToken)
    {
        if (book?.Pair.IsEmpty() != false)
            return;
        var market = GetMarket(book.Pair);
        KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
        KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
        using (_sync.EnterScope())
        {
            depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.PairId.EqualsIgnoreCase(market.PairId))];
            level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
                pair.Value.PairId.EqualsIgnoreCase(market.PairId))];
        }
        var serverTime = CurrentTime;
        foreach (var pair in depthSubscriptions)
            await SendBookAsync(market, book, pair.Value.Depth, pair.Key,
                serverTime, cancellationToken);
        foreach (var pair in level1Subscriptions)
            await SendLevel1BookAsync(market, book, pair.Key, serverTime,
                cancellationToken);
    }

    private async ValueTask OnSocketTradesAsync(IndodaxSocketTrade[] trades,
        CancellationToken cancellationToken)
    {
        foreach (var trade in (trades ?? [])
            .Where(static trade => trade is not null)
            .OrderBy(static trade => trade.Timestamp))
        {
            var market = GetMarket(trade.Pair);
            var identifier = trade.Sequence.IsEmpty()
                ? $"{market.PairId}:{trade.Timestamp}:{trade.Price}:{trade.BaseVolume}"
                : trade.Sequence;
            if (!AddPublicTrade(identifier))
                continue;
            KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
            KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
            using (_sync.EnterScope())
            {
                tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
                    pair.Value.PairId.EqualsIgnoreCase(market.PairId))];
                level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
                    pair.Value.PairId.EqualsIgnoreCase(market.PairId))];
            }
            foreach (var pair in tickSubscriptions)
                await SendSocketTradeAsync(market, trade, pair.Key, identifier,
                    cancellationToken);
            foreach (var pair in level1Subscriptions)
                await SendLevel1TradeAsync(market, trade, pair.Key,
                    cancellationToken);
        }
    }

    private ValueTask SendTickerAsync(MarketDefinition market,
        IndodaxTicker ticker, long transactionId,
        CancellationToken cancellationToken)
    {
        if (ticker is null)
            return default;
        var volume = ticker.Volumes?.FirstOrDefault(value =>
            value.Name.EqualsIgnoreCase(market.BaseCurrency))?.Amount;
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = ticker.ServerTime.FromIndodaxSeconds(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, ticker.Buy)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Sell)
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.Volume, volume), cancellationToken);
    }

    private ValueTask SendLevel1BookAsync(MarketDefinition market,
        IndodaxSocketBook book, long transactionId, DateTime serverTime,
        CancellationToken cancellationToken)
    {
        var bid = ToSocketQuotes(market, book?.Bids, false, 1)
            .FirstOrDefault();
        var ask = ToSocketQuotes(market, book?.Asks, true, 1)
            .FirstOrDefault();
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, bid.Price)
        .TryAdd(Level1Fields.BestBidVolume, bid.Volume)
        .TryAdd(Level1Fields.BestAskPrice, ask.Price)
        .TryAdd(Level1Fields.BestAskVolume, ask.Volume), cancellationToken);
    }

    private ValueTask SendLevel1TradeAsync(MarketDefinition market,
        IndodaxSocketTrade trade, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = trade.Timestamp.FromIndodaxSeconds(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.LastTradePrice, trade.Price)
        .TryAdd(Level1Fields.LastTradeVolume, trade.BaseVolume)
        .TryAdd(Level1Fields.LastTradeTime,
            trade.Timestamp.FromIndodaxSeconds(CurrentTime))
        .TryAdd(Level1Fields.LastTradeOrigin, trade.Side.ToStockSharp()),
            cancellationToken);

    private ValueTask SendBookAsync(MarketDefinition market,
        IndodaxDepth book, int depth, long transactionId, DateTime serverTime,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(book?.Bids, false, depth),
            Asks = ToQuotes(book?.Asks, true, depth),
        }, cancellationToken);

    private ValueTask SendBookAsync(MarketDefinition market,
        IndodaxSocketBook book, int depth, long transactionId,
        DateTime serverTime, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToSocketQuotes(market, book?.Bids, false, depth),
            Asks = ToSocketQuotes(market, book?.Asks, true, depth),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(MarketDefinition market,
        IndodaxPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || trade.TradeId.IsEmpty() || trade.Price <= 0 ||
            trade.Amount <= 0)
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = trade.Timestamp.FromIndodaxSeconds(CurrentTime),
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Amount,
            OriginSide = trade.Side.ToStockSharp(),
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private ValueTask SendSocketTradeAsync(MarketDefinition market,
        IndodaxSocketTrade trade, long transactionId, string identifier,
        CancellationToken cancellationToken)
    {
        if (trade.Price <= 0 || trade.BaseVolume <= 0)
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = trade.Timestamp.FromIndodaxSeconds(CurrentTime),
            TradeStringId = identifier,
            TradePrice = trade.Price,
            TradeVolume = trade.BaseVolume,
            OriginSide = trade.Side.ToStockSharp(),
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private static QuoteChange[] ToQuotes(IndodaxBookLevel[] levels,
        bool isAsk, int depth)
        => [.. (levels ?? [])
            .Where(static level => level is not null && level.Price > 0 &&
                level.Amount > 0)
            .OrderBy(level => isAsk ? level.Price : -level.Price)
            .Take(depth)
            .Select(static level => new QuoteChange(level.Price, level.Amount))];

    private static QuoteChange[] ToSocketQuotes(MarketDefinition market,
        IndodaxSocketBookEntry[] levels, bool isAsk, int depth)
        => [.. (levels ?? [])
            .Where(static level => level is not null && level.Price > 0)
            .Select(level => new QuoteChange(level.Price,
                level.Volumes?.FirstOrDefault(value =>
                    value.Name.EqualsIgnoreCase(market.BaseCurrency))?.Amount ?? 0m))
            .Where(static quote => quote.Volume > 0)
            .OrderBy(quote => isAsk ? quote.Price : -quote.Price)
            .Take(depth)
            ];

    private async ValueTask CompleteMarketSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
