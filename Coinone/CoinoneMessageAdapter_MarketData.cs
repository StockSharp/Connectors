namespace StockSharp.Coinone;

public partial class CoinoneMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
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
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Coinone))
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
            }.TryAdd(Level1Fields.State, ToSecurityState(market)),
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
                "Coinone does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var response = await RestClient.GetTickerAsync(new()
        {
            QuoteCurrency = market.QuoteCurrency,
            TargetCurrency = market.TargetCurrency,
        }, cancellationToken);
        var ticker = response.Tickers?.FirstOrDefault();
        if (ticker is null)
            throw new InvalidDataException(
                $"Coinone returned no ticker for '{market.Symbol}'.");
        await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Ticker, market.Symbol, default);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await PublicSocketClient.SubscribeTickerAsync(market.QuoteCurrency,
                    market.TargetCurrency, cancellationToken);
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
                "Coinone does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 16).Min(16).Max(1);
        var book = await RestClient.GetOrderBookAsync(new()
        {
            QuoteCurrency = market.QuoteCurrency,
            TargetCurrency = market.TargetCurrency,
            Size = GetNativeDepth(depth),
        }, cancellationToken);
        await SendBookAsync(market, book.Timestamp, book.Bids, book.Asks, depth,
            mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.OrderBook, market.Symbol, default);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                Depth = depth,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await PublicSocketClient.SubscribeOrderBookAsync(
                    market.QuoteCurrency, market.TargetCurrency, cancellationToken);
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
        var requestedCount = (mdMsg.Count ?? 200).Min(200).Max(1).To<int>();
        var response = await RestClient.GetTradesAsync(new()
        {
            QuoteCurrency = market.QuoteCurrency,
            TargetCurrency = market.TargetCurrency,
            Size = GetNativeTradeCount(requestedCount),
        }, cancellationToken);
        var from = mdMsg.From?.ToUniversalTime();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        foreach (var trade in (response.Transactions ?? [])
            .Where(trade => trade is not null &&
                (from is null || trade.Timestamp.FromCoinoneTimestamp(
                    DateTime.MinValue) >= from) &&
                trade.Timestamp.FromCoinoneTimestamp(DateTime.MaxValue) <= to)
            .OrderBy(static trade => trade.Timestamp)
            .TakeLast(requestedCount))
            await SendPublicTradeAsync(market, trade.Id, trade.Timestamp,
                trade.Price, trade.Quantity, trade.IsSellerMaker,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Trade, market.Symbol, default);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await PublicSocketClient.SubscribeTradesAsync(market.QuoteCurrency,
                    market.TargetCurrency, cancellationToken);
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
        var interval = timeFrame.ToCoinoneInterval();
        if (!mdMsg.IsHistoryOnly() && !timeFrame.IsStreamingTimeFrame())
            throw new NotSupportedException(
                $"Coinone WebSocket does not stream '{interval}' candles; " +
                "this interval is available for history only.");

        var now = CurrentTime.Kind == DateTimeKind.Utc
            ? CurrentTime
            : CurrentTime.ToUniversalTime();
        var to = (mdMsg.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var count = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to - TimeSpan.FromTicks(timeFrame.Ticks * count);
        var candles = await DownloadCandlesAsync(market, interval, from, to,
            count, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Candle, market.Symbol, timeFrame);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                TimeFrame = timeFrame,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await PublicSocketClient.SubscribeCandlesAsync(
                    market.QuoteCurrency, market.TargetCurrency, interval,
                    cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
            throw;
        }
    }

    private SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
        => new()
        {
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            Name = $"{market.TargetCurrency}/{market.QuoteCurrency}",
            ShortName = $"{market.TargetCurrency}/{market.QuoteCurrency}",
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteCurrency.ToCurrency(),
            PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
            VolumeStep = market.QuantityStep > 0 ? market.QuantityStep : null,
            MinVolume = market.MinimumQuantity > 0
                ? market.MinimumQuantity
                : null,
            MaxVolume = market.MaximumQuantity > 0
                ? market.MaximumQuantity
                : null,
            OriginalTransactionId = originalTransactionId,
        };

    private static SecurityStates ToSecurityState(MarketDefinition market)
        => market.MaintenanceStatus == CoinoneMaintenanceStatuses.Normal &&
            market.TradeStatus != CoinoneTradeStatuses.Disabled
                ? SecurityStates.Trading
                : SecurityStates.Stoped;

    private async ValueTask<CoinoneCandle[]> DownloadCandlesAsync(
        MarketDefinition market, string interval, DateTime from, DateTime to,
        int count, CancellationToken cancellationToken)
    {
        var values = new SortedDictionary<long, CoinoneCandle>();
        var cursor = new DateTimeOffset(to).ToUnixTimeMilliseconds();
        while (values.Count < count)
        {
            var pageSize = (count - values.Count).Min(500).Max(1);
            var response = await RestClient.GetChartAsync(new()
            {
                QuoteCurrency = market.QuoteCurrency,
                TargetCurrency = market.TargetCurrency,
                Interval = interval,
                Timestamp = cursor,
                Size = pageSize,
            }, cancellationToken);
            var page = response.Chart ?? [];
            if (page.Length == 0)
                break;
            foreach (var candle in page)
            {
                if (candle is null)
                    continue;
                var time = candle.Timestamp.FromCoinoneTimestamp(DateTime.MinValue);
                if (time >= from && time <= to)
                    values[candle.Timestamp] = candle;
            }
            var earliest = page.Where(static candle => candle is not null)
                .Select(static candle => candle.Timestamp).DefaultIfEmpty().Min();
            if (earliest <= 0 || earliest.FromCoinoneTimestamp(DateTime.MinValue) <= from ||
                response.IsLast || page.Length < pageSize)
                break;
            cursor = earliest - 1;
        }
        return [.. values.Values.TakeLast(count)];
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_level1Subscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.Ticker, subscription.Symbol, default));
        if (release)
        {
            var market = GetMarket(subscription.Symbol);
            await PublicSocketClient.UnsubscribeTickerAsync(market.QuoteCurrency,
                market.TargetCurrency, cancellationToken);
        }
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_depthSubscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.OrderBook, subscription.Symbol, default));
        if (release)
        {
            var market = GetMarket(subscription.Symbol);
            await PublicSocketClient.UnsubscribeOrderBookAsync(
                market.QuoteCurrency, market.TargetCurrency, cancellationToken);
        }
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_tickSubscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.Trade, subscription.Symbol, default));
        if (release)
        {
            var market = GetMarket(subscription.Symbol);
            await PublicSocketClient.UnsubscribeTradesAsync(market.QuoteCurrency,
                market.TargetCurrency, cancellationToken);
        }
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_candleSubscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.Candle, subscription.Symbol,
                        subscription.TimeFrame));
        if (release)
        {
            var market = GetMarket(subscription.Symbol);
            await PublicSocketClient.UnsubscribeCandlesAsync(
                market.QuoteCurrency, market.TargetCurrency,
                subscription.TimeFrame.ToCoinoneInterval(), cancellationToken);
        }
    }

    private async ValueTask OnSocketBookAsync(CoinoneSocketBook book,
        CancellationToken cancellationToken)
    {
        if (book?.QuoteCurrency.IsEmpty() != false || book.TargetCurrency.IsEmpty())
            return;
        var market = GetMarket(book.QuoteCurrency, book.TargetCurrency);
        KeyValuePair<long, DepthSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendBookAsync(market, book.Timestamp, book.Bids, book.Asks,
                pair.Value.Depth, pair.Key, cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(CoinoneSocketTicker ticker,
        CancellationToken cancellationToken)
    {
        if (ticker?.QuoteCurrency.IsEmpty() != false || ticker.TargetCurrency.IsEmpty())
            return;
        var market = GetMarket(ticker.QuoteCurrency, ticker.TargetCurrency);
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendTickerAsync(ticker, pair.Key, cancellationToken);
    }

    private async ValueTask OnSocketTradeAsync(CoinoneSocketTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade?.QuoteCurrency.IsEmpty() != false || trade.TargetCurrency.IsEmpty() ||
            trade.Id.IsEmpty() || trade.Price <= 0 || trade.Quantity <= 0)
            return;
        var market = GetMarket(trade.QuoteCurrency, trade.TargetCurrency);
        if (!AddPublicTrade(market.Symbol, trade.Id))
            return;
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _tickSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendPublicTradeAsync(market, trade.Id, trade.Timestamp,
                trade.Price, trade.Quantity, trade.IsSellerMaker, pair.Key,
                cancellationToken, false);
    }

    private async ValueTask OnSocketCandleAsync(CoinoneSocketCandle candle,
        CancellationToken cancellationToken)
    {
        if (candle?.QuoteCurrency.IsEmpty() != false ||
            candle.TargetCurrency.IsEmpty() || candle.Interval.IsEmpty())
            return;
        var market = GetMarket(candle.QuoteCurrency, candle.TargetCurrency);
        var timeFrame = candle.Interval.ToTimeFrame();
        KeyValuePair<long, CandleSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _candleSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol) &&
                pair.Value.TimeFrame == timeFrame)];
        var value = new CoinoneCandle
        {
            Timestamp = candle.CandleTimestamp,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            TargetVolume = candle.TargetVolume,
            QuoteVolume = candle.QuoteVolume,
        };
        foreach (var pair in subscriptions)
            await SendCandleAsync(market, value, timeFrame, pair.Key,
                cancellationToken);
    }

    private ValueTask SendTickerAsync(CoinoneTicker ticker, long transactionId,
        CancellationToken cancellationToken)
    {
        var market = GetMarket(ticker.QuoteCurrency, ticker.TargetCurrency);
        var bestBid = ticker.BestBids?.FirstOrDefault();
        var bestAsk = ticker.BestAsks?.FirstOrDefault();
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = ticker.Timestamp.FromCoinoneTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.OpenPrice, ticker.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.BestBidPrice, bestBid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bestBid?.Quantity)
        .TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price)
        .TryAdd(Level1Fields.BestAskVolume, bestAsk?.Quantity)
        .TryAdd(Level1Fields.Volume, ticker.TargetVolume)
        .TryAdd(Level1Fields.Turnover, ticker.QuoteVolume), cancellationToken);
    }

    private ValueTask SendTickerAsync(CoinoneSocketTicker ticker,
        long transactionId, CancellationToken cancellationToken)
    {
        var market = GetMarket(ticker.QuoteCurrency, ticker.TargetCurrency);
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = ticker.Timestamp.FromCoinoneTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.OpenPrice, ticker.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice)
        .TryAdd(Level1Fields.BestBidVolume, ticker.BestBidQuantity)
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice)
        .TryAdd(Level1Fields.BestAskVolume, ticker.BestAskQuantity)
        .TryAdd(Level1Fields.Volume, ticker.TargetVolume)
        .TryAdd(Level1Fields.Turnover, ticker.QuoteVolume), cancellationToken);
    }

    private ValueTask SendBookAsync(MarketDefinition market, long timestamp,
        IEnumerable<CoinoneBookLevel> bids, IEnumerable<CoinoneBookLevel> asks,
        int depth, long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = timestamp.FromCoinoneTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(bids, false, depth),
            Asks = ToQuotes(asks, true, depth),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(MarketDefinition market,
        string tradeId, long timestamp, decimal price, decimal quantity,
        bool isSellerMaker, long transactionId,
        CancellationToken cancellationToken, bool addToDeduplication = true)
    {
        if (tradeId.IsEmpty() || price <= 0 || quantity <= 0)
            return default;
        if (addToDeduplication && !AddPublicTrade(market.Symbol, tradeId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = timestamp.FromCoinoneTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            TradeStringId = tradeId,
            TradePrice = price,
            TradeVolume = quantity,
            OriginSide = isSellerMaker ? Sides.Sell : Sides.Buy,
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(MarketDefinition market,
        CoinoneCandle candle, TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
    {
        var openTime = candle.Timestamp.FromCoinoneTimestamp(CurrentTime);
        var closeTime = openTime + timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.TargetVolume,
            TotalPrice = candle.QuoteVolume,
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);
    }

    private static QuoteChange[] ToQuotes(IEnumerable<CoinoneBookLevel> levels,
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

    private static int GetNativeDepth(int requestedDepth)
        => requestedDepth <= 5 ? 5 : requestedDepth <= 10 ? 10 :
            requestedDepth <= 15 ? 15 : 16;

    private static int GetNativeTradeCount(int requestedCount)
        => requestedCount <= 10 ? 10 : requestedCount <= 50 ? 50 :
            requestedCount <= 100 ? 100 : requestedCount <= 150 ? 150 : 200;

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
