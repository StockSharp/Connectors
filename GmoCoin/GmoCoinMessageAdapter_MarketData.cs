namespace StockSharp.GmoCoin;

public partial class GmoCoinMessageAdapter
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
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.GmoCoin))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(market.Symbol))
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
            }.TryAdd(Level1Fields.State, ToSecurityState()), cancellationToken);
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
                "GMO Coin does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var tickers = await RestClient.GetTickerAsync(new()
        {
            Symbol = market.Symbol,
        }, cancellationToken);
        var ticker = tickers?.FirstOrDefault();
        if (ticker is null)
            throw new InvalidDataException(
                $"GMO Coin returned no ticker for '{market.Symbol}'.");
        await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Ticker, market.Symbol);
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
                await PublicSocketClient.SubscribeTickerAsync(market.Symbol,
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
                "GMO Coin does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 50).Min(1000).Max(1);
        var book = await RestClient.GetOrderBookAsync(new()
        {
            Symbol = market.Symbol,
        }, cancellationToken);
        if (book is null)
            throw new InvalidDataException(
                $"GMO Coin returned no order book for '{market.Symbol}'.");
        await SendBookAsync(market, book.Timestamp, book.Bids, book.Asks, depth,
            mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.OrderBook, market.Symbol);
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
                await PublicSocketClient.SubscribeOrderBookAsync(market.Symbol,
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

        var market = GetMarket(mdMsg.SecurityId);
        var maximum = (mdMsg.Count ?? 100).Min(10000).Max(1).To<int>();
        var trades = await DownloadPublicTradesAsync(market, mdMsg, maximum,
            cancellationToken);
        foreach (var trade in trades)
            await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
                cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Trade, market.Symbol);
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
                await PublicSocketClient.SubscribeTradesAsync(market.Symbol,
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
            return;
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        if (!mdMsg.IsHistoryOnly())
            throw new NotSupportedException(
                "GMO Coin publishes candles through REST only; live candle subscriptions are not available.");

        var market = GetMarket(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        var interval = timeFrame.ToGmoCoinInterval();
        var now = CurrentTime.Kind == DateTimeKind.Utc
            ? CurrentTime
            : CurrentTime.ToUniversalTime();
        var to = (mdMsg.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var count = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to - TimeSpan.FromTicks(timeFrame.Ticks * count);
        var candles = await DownloadCandlesAsync(market, interval, timeFrame,
            from, to, count, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);
        await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
    }

    private SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
    {
        var name = $"{market.BaseAsset}/{market.QuoteAsset}" +
            (market.IsMargin ? " Margin" : " Spot");
        var message = new SecurityMessage
        {
            SecurityId = market.Symbol.ToStockSharp(),
            Name = name,
            ShortName = name,
            SecurityType = market.IsMargin
                ? SecurityTypes.Future
                : SecurityTypes.CryptoCurrency,
            UnderlyingSecurityType = market.IsMargin
                ? SecurityTypes.CryptoCurrency
                : null,
            Currency = market.QuoteAsset.ToCurrency(),
            PriceStep = market.TickSize > 0 ? market.TickSize : null,
            VolumeStep = market.SizeStep > 0 ? market.SizeStep : null,
            MinVolume = market.MinimumOrderSize > 0
                ? market.MinimumOrderSize
                : null,
            MaxVolume = market.MaximumOrderSize > 0
                ? market.MaximumOrderSize
                : null,
            OriginalTransactionId = originalTransactionId,
        };
        return market.IsMargin
            ? message.TryFillUnderlyingId(market.BaseAsset)
            : message;
    }

    private SecurityStates ToSecurityState()
        => _serviceStatus == GmoCoinServiceStatuses.Open
            ? SecurityStates.Trading
            : SecurityStates.Stoped;

    private async ValueTask<GmoCoinPublicTrade[]> DownloadPublicTradesAsync(
        MarketDefinition market, MarketDataMessage message, int maximum,
        CancellationToken cancellationToken)
    {
        var from = message.From?.ToUniversalTime();
        var to = (message.To ?? DateTime.UtcNow).ToUniversalTime();
        var values = new List<GmoCoinPublicTrade>();
        for (var page = 1; values.Count < maximum && page <= 100; page++)
        {
            var response = await RestClient.GetTradesAsync(new()
            {
                Symbol = market.Symbol,
                Page = page,
                Count = (maximum - values.Count).Min(100).Max(1),
            }, cancellationToken);
            var items = response?.Items ?? [];
            foreach (var trade in items)
            {
                if (trade is null)
                    continue;
                var time = trade.Timestamp.FromGmoCoinTime(DateTime.MinValue);
                if ((from is null || time >= from) && time <= to)
                    values.Add(trade);
            }
            if (items.Length < 100 || (from is DateTime lower &&
                items.Any(item => item?.Timestamp.FromGmoCoinTime(
                    DateTime.MaxValue) < lower)))
                break;
        }
        return [.. values.OrderBy(static trade => trade.Timestamp)
            .TakeLast(maximum)];
    }

    private async ValueTask<GmoCoinCandle[]> DownloadCandlesAsync(
        MarketDefinition market, string interval, TimeSpan timeFrame,
        DateTime from, DateTime to, int count,
        CancellationToken cancellationToken)
    {
        var values = new SortedDictionary<long, GmoCoinCandle>();
        if (timeFrame.UsesYearlyKlines())
        {
            for (var year = to.Year; year >= from.Year && values.Count < count;
                year--)
            {
                var page = await RestClient.GetKlinesAsync(new()
                {
                    Symbol = market.Symbol,
                    Interval = interval,
                    Date = year.ToString("0000", CultureInfo.InvariantCulture),
                }, cancellationToken);
                AddCandles(values, page, from, to);
            }
        }
        else
        {
            var date = to.AddHours(9).Date;
            var firstDate = from.AddHours(9).Date;
            for (; date >= firstDate && values.Count < count;
                date = date.AddDays(-1))
            {
                var page = await RestClient.GetKlinesAsync(new()
                {
                    Symbol = market.Symbol,
                    Interval = interval,
                    Date = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                }, cancellationToken);
                AddCandles(values, page, from, to);
            }
        }
        return [.. values.Values.TakeLast(count)];
    }

    private static void AddCandles(
        IDictionary<long, GmoCoinCandle> destination,
        IEnumerable<GmoCoinCandle> candles, DateTime from, DateTime to)
    {
        foreach (var candle in candles ?? [])
        {
            if (candle is null)
                continue;
            var time = candle.OpenTime.FromGmoCoinTime(DateTime.MinValue);
            if (time >= from && time <= to)
                destination[candle.OpenTime] = candle;
        }
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_level1Subscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.Ticker, subscription.Symbol));
        if (release)
            await PublicSocketClient.UnsubscribeTickerAsync(subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_depthSubscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.OrderBook, subscription.Symbol));
        if (release)
            await PublicSocketClient.UnsubscribeOrderBookAsync(subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_tickSubscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.Trade, subscription.Symbol));
        if (release)
            await PublicSocketClient.UnsubscribeTradesAsync(subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(GmoCoinSocketTicker ticker,
        CancellationToken cancellationToken)
    {
        if (ticker?.Symbol.IsEmpty() != false)
            return;
        var market = GetMarket(ticker.Symbol);
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendTickerAsync(ticker, pair.Key, cancellationToken);
    }

    private async ValueTask OnSocketOrderBookAsync(
        GmoCoinSocketOrderBook book, CancellationToken cancellationToken)
    {
        if (book?.Symbol.IsEmpty() != false)
            return;
        var market = GetMarket(book.Symbol);
        KeyValuePair<long, DepthSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendBookAsync(market, book.Timestamp, book.Bids, book.Asks,
                pair.Value.Depth, pair.Key, cancellationToken);
    }

    private async ValueTask OnSocketTradeAsync(GmoCoinSocketTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade?.Symbol.IsEmpty() != false || trade.Price <= 0 || trade.Size <= 0)
            return;
        var market = GetMarket(trade.Symbol);
        var tradeId = CreatePublicTradeId(trade.Timestamp, trade.Price,
            trade.Size, trade.Side);
        if (!AddPublicTrade(market.Symbol, tradeId))
            return;
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _tickSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendPublicTradeAsync(market, tradeId, trade.Timestamp,
                trade.Price, trade.Size, trade.Side, pair.Key, cancellationToken,
                false);
    }

    private ValueTask SendTickerAsync(GmoCoinTicker ticker, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.Symbol.ToStockSharp(),
            ServerTime = ticker.Timestamp.FromGmoCoinTime(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
        .TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);

    private ValueTask SendTickerAsync(GmoCoinSocketTicker ticker,
        long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.Symbol.ToStockSharp(),
            ServerTime = ticker.Timestamp.FromGmoCoinTime(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
        .TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);

    private ValueTask SendBookAsync(MarketDefinition market, string timestamp,
        IEnumerable<GmoCoinBookLevel> bids,
        IEnumerable<GmoCoinBookLevel> asks, int depth, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = market.Symbol.ToStockSharp(),
            ServerTime = timestamp.FromGmoCoinTime(CurrentTime),
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(bids, false, depth),
            Asks = ToQuotes(asks, true, depth),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(MarketDefinition market,
        GmoCoinPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null)
            return default;
        var id = CreatePublicTradeId(trade.Timestamp, trade.Price, trade.Size,
            trade.Side);
        return SendPublicTradeAsync(market, id, trade.Timestamp, trade.Price,
            trade.Size, trade.Side, transactionId, cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(MarketDefinition market,
        string tradeId, string timestamp, decimal price, decimal size,
        GmoCoinSides side, long transactionId,
        CancellationToken cancellationToken, bool addToDeduplication = true)
    {
        if (tradeId.IsEmpty() || price <= 0 || size <= 0)
            return default;
        if (addToDeduplication && !AddPublicTrade(market.Symbol, tradeId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = market.Symbol.ToStockSharp(),
            ServerTime = timestamp.FromGmoCoinTime(CurrentTime),
            OriginalTransactionId = transactionId,
            TradeStringId = tradeId,
            TradePrice = price,
            TradeVolume = size,
            OriginSide = side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(MarketDefinition market,
        GmoCoinCandle candle, TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.FromGmoCoinTime(CurrentTime);
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = market.Symbol.ToStockSharp(),
            OpenTime = openTime,
            CloseTime = openTime + timeFrame,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            TypedArg = timeFrame,
            OriginalTransactionId = transactionId,
            State = CandleStates.Finished,
        }, cancellationToken);
    }

    private static QuoteChange[] ToQuotes(IEnumerable<GmoCoinBookLevel> levels,
        bool isAsk, int depth)
    {
        var filtered = (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Size > 0);
        return [.. (isAsk
            ? filtered.OrderBy(static level => level.Price)
            : filtered.OrderByDescending(static level => level.Price))
            .Take(depth).Select(static level =>
                new QuoteChange(level.Price, level.Size))];
    }

    private static string CreatePublicTradeId(string timestamp, decimal price,
        decimal size, GmoCoinSides side)
        => $"{timestamp}:{price.ToWire()}:{size.ToWire()}:{side}";

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
