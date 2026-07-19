namespace StockSharp.Foxbit;

public partial class FoxbitMessageAdapter
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
        FoxbitMarket[] markets;
        using (_sync.EnterScope())
            markets = [.. _markets.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in markets.OrderBy(static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.Foxbit))
                continue;
            if (!requestedMarket.IsEmpty() &&
                !requestedMarket.EqualsIgnoreCase(market.Symbol))
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
                "Foxbit does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var ticker = await RestClient.GetTickerAsync(market.Symbol,
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
                MarketSymbol = market.Symbol,
            });
        try
        {
            await AcquireStreamAsync(FoxbitSocketChannels.Ticker,
                market.Symbol, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeLevel1Async(mdMsg.TransactionId,
                cancellationToken);
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
                "Foxbit does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 300).Min(300).Max(1);
        if (mdMsg.IsHistoryOnly())
        {
            var snapshot = await RestClient.GetOrderBookAsync(market.Symbol,
                depth, cancellationToken);
            await SendRestBookAsync(market.Symbol, snapshot, depth,
                mdMsg.TransactionId, cancellationToken);
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                MarketSymbol = market.Symbol,
                Depth = depth,
            });
            if (!_books.ContainsKey(market.Symbol))
                _books.Add(market.Symbol, new());
        }
        try
        {
            await AcquireStreamAsync(FoxbitSocketChannels.OrderBook100,
                market.Symbol, cancellationToken);
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

        var market = GetMarket(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 200).Min(10000).Max(1).To<int>();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var from = mdMsg.From?.ToUniversalTime() ?? to - TimeSpan.FromDays(1);
        var trades = await LoadPublicTradesAsync(market.Symbol, from, to,
            maximum, cancellationToken);
        foreach (var trade in trades)
            await SendPublicTradeAsync(market.Symbol, trade,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                MarketSymbol = market.Symbol,
            });
        try
        {
            await AcquireStreamAsync(FoxbitSocketChannels.Trades,
                market.Symbol, cancellationToken);
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

        var market = GetMarket(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToWire();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to.SubtractPeriods(timeFrame, maximum);
        var candles = await LoadCandlesAsync(market.Symbol, timeFrame, from,
            to, maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market.Symbol, candle, timeFrame,
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
                MarketSymbol = market.Symbol,
                TimeFrame = timeFrame,
                Current = current,
            });
        try
        {
            await AcquireStreamAsync(FoxbitSocketChannels.Trades,
                market.Symbol, cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeCandlesAsync(mdMsg.TransactionId,
                cancellationToken);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(FoxbitMarket market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.Symbol.ToStockSharp(),
            Name = $"{market.Base.Symbol.ToUpperInvariant()}/" +
                market.Quote.Symbol.ToUpperInvariant(),
            ShortName = market.Symbol.ToUpperInvariant(),
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.Quote.Symbol.ToCurrency(),
            PriceStep = market.PriceIncrement > 0
                ? market.PriceIncrement
                : market.PricePrecision.ToStep(),
            VolumeStep = market.QuantityIncrement > 0
                ? market.QuantityIncrement
                : market.QuantityPrecision.ToStep(),
            MinVolume = market.MinimumQuantity > 0
                ? market.MinimumQuantity
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
            await ReleaseStreamAsync(FoxbitSocketChannels.Ticker,
                subscription.MarketSymbol, cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(FoxbitSocketChannels.OrderBook100,
                subscription.MarketSymbol, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(FoxbitSocketChannels.Trades,
                subscription.MarketSymbol, cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        using (_sync.EnterScope())
            _candleSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(FoxbitSocketChannels.Trades,
                subscription.MarketSymbol, cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(string marketSymbol,
        FoxbitSocketTicker ticker, CancellationToken cancellationToken)
    {
        if (marketSymbol.IsEmpty() || ticker is null)
            return;
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _level1Subscriptions.Where(pair =>
                pair.Value.MarketSymbol.EqualsIgnoreCase(marketSymbol))
                .Select(static pair => pair.Key)];
        foreach (var transactionId in transactionIds)
            await SendSocketTickerAsync(marketSymbol, ticker, transactionId,
                cancellationToken);
    }

    private async ValueTask OnSocketTradesAsync(string marketSymbol,
        FoxbitSocketTrade[] trades, CancellationToken cancellationToken)
    {
        if (marketSymbol.IsEmpty() || trades is not { Length: > 0 })
            return;
        long[] tickIds;
        (long Id, CandleSubscription Subscription)[] candleSubscriptions;
        using (_sync.EnterScope())
        {
            tickIds = [.. _tickSubscriptions.Where(pair =>
                pair.Value.MarketSymbol.EqualsIgnoreCase(marketSymbol))
                .Select(static pair => pair.Key)];
            candleSubscriptions = [.. _candleSubscriptions.Where(pair =>
                pair.Value.MarketSymbol.EqualsIgnoreCase(marketSymbol))
                .Select(static pair => (pair.Key, pair.Value))];
        }
        foreach (var trade in trades.Where(static trade => trade is not null)
            .OrderBy(static trade => trade.Timestamp))
        {
            foreach (var transactionId in tickIds)
                await SendSocketTradeAsync(marketSymbol, trade, transactionId,
                    cancellationToken);
            foreach (var item in candleSubscriptions)
                await UpdateLiveCandleAsync(item.Id, item.Subscription, trade,
                    cancellationToken);
        }
    }

    private async ValueTask OnSocketBookSnapshotAsync(string marketSymbol,
        FoxbitSocketBookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (marketSymbol.IsEmpty() || snapshot is null)
            return;
        (long Id, int Depth)[] subscriptions;
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(marketSymbol, out var state))
                _books.Add(marketSymbol, state = new());
            state.SequenceId = snapshot.SequenceId;
            state.IsSnapshotReady = true;
            state.IsRefreshPending = false;
            subscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.MarketSymbol.EqualsIgnoreCase(marketSymbol))
                .Select(static pair => (pair.Key, pair.Value.Depth))];
        }
        foreach (var subscription in subscriptions)
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                SecurityId = marketSymbol.ToStockSharp(),
                ServerTime = CurrentTime,
                OriginalTransactionId = subscription.Id,
                State = QuoteChangeStates.SnapshotComplete,
                SeqNum = snapshot.SequenceId,
                Bids = ToQuotes((snapshot.Bids ?? []).Take(
                    subscription.Depth)),
                Asks = ToQuotes((snapshot.Asks ?? []).Take(
                    subscription.Depth)),
            }, cancellationToken);
    }

    private async ValueTask OnSocketBookUpdateAsync(string marketSymbol,
        FoxbitSocketBookUpdate update, CancellationToken cancellationToken)
    {
        if (marketSymbol.IsEmpty() || update is null)
            return;
        long[] transactionIds;
        var refresh = false;
        var valid = false;
        long expected = 0;
        using (_sync.EnterScope())
        {
            if (!_books.TryGetValue(marketSymbol, out var state))
                _books.Add(marketSymbol, state = new());
            expected = state.SequenceId + 1;
            valid = state.IsSnapshotReady &&
                (update.FirstSequenceId == expected ||
                    update.FirstSequenceId == 1) &&
                update.LastSequenceId >= update.FirstSequenceId;
            if (valid)
                state.SequenceId = update.LastSequenceId;
            else
            {
                state.IsSnapshotReady = false;
                if (!state.IsRefreshPending)
                {
                    state.IsRefreshPending = true;
                    refresh = true;
                }
            }
            transactionIds = [.. _depthSubscriptions.Where(pair =>
                pair.Value.MarketSymbol.EqualsIgnoreCase(marketSymbol))
                .Select(static pair => pair.Key)];
        }
        if (!valid)
        {
            if (refresh)
            {
                this.AddWarningLog(
                    "Foxbit {0} order-book sequence gap. Expected {1}, received {2}-{3}. Requesting a new snapshot.",
                    marketSymbol, expected, update.FirstSequenceId,
                    update.LastSequenceId);
                await SocketClient.RefreshOrderBookAsync(marketSymbol,
                    cancellationToken);
            }
            return;
        }
        var bids = ToQuotes(update.Bids);
        var asks = ToQuotes(update.Asks);
        foreach (var transactionId in transactionIds)
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                SecurityId = marketSymbol.ToStockSharp(),
                ServerTime = GetTime(update.Timestamp),
                OriginalTransactionId = transactionId,
                State = QuoteChangeStates.Increment,
                SeqNum = update.LastSequenceId,
                Bids = bids,
                Asks = asks,
            }, cancellationToken);
    }

    private ValueTask SendTickerAsync(FoxbitTicker ticker, long transactionId,
        CancellationToken cancellationToken)
    {
        if (ticker?.MarketSymbol.IsEmpty() != false)
            throw new InvalidDataException(
                "Foxbit returned a ticker without market_symbol.");
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.MarketSymbol.ToStockSharp(),
            ServerTime = GetTime(ticker.LastTrade?.Date),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, ticker.Best?.Bid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, ticker.Best?.Bid?.Volume)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Best?.Ask?.Price)
        .TryAdd(Level1Fields.BestAskVolume, ticker.Best?.Ask?.Volume)
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastTrade?.Price)
        .TryAdd(Level1Fields.LastTradeVolume, ticker.LastTrade?.Volume)
        .TryAdd(Level1Fields.Volume, ticker.RollingDay?.Volume)
        .TryAdd(Level1Fields.Turnover, ticker.RollingDay?.QuoteVolume)
        .TryAdd(Level1Fields.OpenPrice, ticker.RollingDay?.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.RollingDay?.High)
        .TryAdd(Level1Fields.LowPrice, ticker.RollingDay?.Low)
        .TryAdd(Level1Fields.Change, ticker.RollingDay?.PriceChange)
        .TryAdd(Level1Fields.TradesCount, ticker.RollingDay?.TradesCount),
            cancellationToken);
    }

    private ValueTask SendSocketTickerAsync(string marketSymbol,
        FoxbitSocketTicker ticker, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = marketSymbol.ToStockSharp(),
            ServerTime = GetTime(ticker.Timestamp),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, ticker.Best?.Bid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Best?.Ask)
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastTrade?.Price)
        .TryAdd(Level1Fields.LastTradeVolume, ticker.LastTrade?.Quantity)
        .TryAdd(Level1Fields.Volume, ticker.RollingDay?.Volume)
        .TryAdd(Level1Fields.OpenPrice, ticker.RollingDay?.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.RollingDay?.High)
        .TryAdd(Level1Fields.LowPrice, ticker.RollingDay?.Low)
        .TryAdd(Level1Fields.Change, ticker.RollingDay?.PriceChange)
        .TryAdd(Level1Fields.TradesCount, ticker.RollingDay?.TradesCount),
            cancellationToken);

    private ValueTask SendRestBookAsync(string marketSymbol,
        FoxbitOrderBook book, int depth, long transactionId,
        CancellationToken cancellationToken)
    {
        if (book is null)
            throw new InvalidDataException(
                "Foxbit returned an empty order book.");
        return SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = marketSymbol.ToStockSharp(),
            ServerTime = GetTime(book.Timestamp),
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            SeqNum = book.SequenceId,
            Bids = ToQuotes((book.Bids ?? []).Take(depth)),
            Asks = ToQuotes((book.Asks ?? []).Take(depth)),
        }, cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(string marketSymbol,
        FoxbitPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || !AddPublicTrade(trade.Id, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = marketSymbol.ToStockSharp(),
            ServerTime = GetTime(trade.CreatedAt),
            OriginalTransactionId = transactionId,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Volume,
            OriginSide = trade.TakerSide.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendSocketTradeAsync(string marketSymbol,
        FoxbitSocketTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (!AddPublicTrade(trade.Id, transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = marketSymbol.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = transactionId,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            OriginSide = trade.TakerSide.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(string marketSymbol,
        FoxbitCandle candle, TimeSpan timeFrame, long transactionId,
        CandleStates? state, CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime();
        var closeTime = candle.CloseTime > openTime
            ? candle.CloseTime.ToUtcTime()
            : openTime.GetCloseTime(timeFrame);
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = marketSymbol.ToStockSharp(),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.BaseVolume,
            TotalPrice = candle.QuoteVolume,
            TotalTicks = candle.TradesCount.Min(int.MaxValue).Max(0).To<int>(),
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = state ?? (closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active),
        }, cancellationToken);
    }

    private async ValueTask UpdateLiveCandleAsync(long transactionId,
        CandleSubscription subscription, FoxbitSocketTrade trade,
        CancellationToken cancellationToken)
    {
        var tradeTime = GetTime(trade.Timestamp);
        var openTime = tradeTime.Align(subscription.TimeFrame);
        FoxbitCandle finished = null;
        FoxbitCandle active;
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
                    CloseTime = openTime.GetCloseTime(subscription.TimeFrame),
                    Open = trade.Price,
                    High = trade.Price,
                    Low = trade.Price,
                    Close = trade.Price,
                    BaseVolume = trade.Quantity,
                    QuoteVolume = trade.Price * trade.Quantity,
                    TradesCount = 1,
                    TakerBuyBaseVolume = trade.TakerSide == FoxbitSides.Buy
                        ? trade.Quantity
                        : 0m,
                    TakerBuyQuoteVolume = trade.TakerSide == FoxbitSides.Buy
                        ? trade.Price * trade.Quantity
                        : 0m,
                };
            }
            else
                active = new()
                {
                    OpenTime = current.OpenTime,
                    CloseTime = current.CloseTime,
                    Open = current.Open,
                    High = current.High.Max(trade.Price),
                    Low = current.Low.Min(trade.Price),
                    Close = trade.Price,
                    BaseVolume = current.BaseVolume + trade.Quantity,
                    QuoteVolume = current.QuoteVolume +
                        trade.Price * trade.Quantity,
                    TradesCount = current.TradesCount + 1,
                    TakerBuyBaseVolume = current.TakerBuyBaseVolume +
                        (trade.TakerSide == FoxbitSides.Buy
                            ? trade.Quantity
                            : 0m),
                    TakerBuyQuoteVolume = current.TakerBuyQuoteVolume +
                        (trade.TakerSide == FoxbitSides.Buy
                            ? trade.Price * trade.Quantity
                            : 0m),
                };
            subscription.Current = active;
        }
        if (finished is not null)
            await SendCandleAsync(subscription.MarketSymbol, finished,
                subscription.TimeFrame, transactionId, CandleStates.Finished,
                cancellationToken);
        await SendCandleAsync(subscription.MarketSymbol, active,
            subscription.TimeFrame, transactionId, CandleStates.Active,
            cancellationToken);
    }

    private async ValueTask<FoxbitPublicTrade[]> LoadPublicTradesAsync(
        string marketSymbol, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<FoxbitPublicTrade>();
        for (var page = 1; result.Count < maximum; page++)
        {
            var pageSize = (maximum - result.Count).Min(200).Max(1);
            var items = await RestClient.GetPublicTradesAsync(marketSymbol,
                new()
                {
                    From = from,
                    To = to,
                    Page = page,
                    PageSize = pageSize,
                }, cancellationToken);
            result.AddRange(items.Where(item => item is not null &&
                GetTime(item.CreatedAt) >= from &&
                GetTime(item.CreatedAt) <= to));
            if (items.Length < pageSize)
                break;
        }
        return [.. result.Where(static trade => !trade.Id.IsEmpty())
            .GroupBy(static trade => trade.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static trade => trade.CreatedAt)
            .TakeLast(maximum)];
    }

    private async ValueTask<FoxbitCandle[]> LoadCandlesAsync(
        string marketSymbol, TimeSpan timeFrame, DateTime from, DateTime to,
        int maximum, CancellationToken cancellationToken)
    {
        var result = new List<FoxbitCandle>();
        var cursor = from.ToUtcTime();
        var upperBound = to.ToUtcTime();
        while (result.Count < maximum && cursor <= upperBound)
        {
            var pageSize = (maximum - result.Count).Min(500).Max(1);
            var items = await RestClient.GetCandlesAsync(marketSymbol, new()
            {
                Interval = timeFrame.ToWire(),
                From = cursor,
                To = upperBound,
                Limit = pageSize,
                Direction = "ASC",
            }, cancellationToken);
            if (items is not { Length: > 0 })
                break;
            result.AddRange(items.Where(candle => candle is not null &&
                candle.OpenTime >= from && candle.OpenTime <= to));
            var last = items.Max(static candle => candle.OpenTime.ToUtcTime());
            var next = last.AddPeriods(timeFrame, 1);
            if (items.Length < pageSize || next <= cursor)
                break;
            cursor = next;
        }
        return [.. result.GroupBy(static candle => candle.OpenTime)
            .Select(static group => group.First())
            .OrderBy(static candle => candle.OpenTime)
            .TakeLast(maximum)];
    }

    private static QuoteChange[] ToQuotes(
        IEnumerable<FoxbitBookLevel> levels)
        => [.. (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Volume >= 0)
            .Select(static level => new QuoteChange(level.Price,
                level.Volume))];

    private DateTime GetTime(DateTime? value)
        => value is not DateTime actual || actual == default
            ? CurrentTime
            : actual.ToUtcTime();

    private DateTime GetTime(long milliseconds)
        => milliseconds > 0 ? milliseconds.FromMilliseconds() : CurrentTime;

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
