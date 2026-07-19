namespace StockSharp.MercadoBitcoin;

public partial class MercadoBitcoinMessageAdapter
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
                    BoardCodes.MercadoBitcoin))
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
                "Mercado Bitcoin does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var tickers = await RestClient.GetTickersAsync(new()
        {
            Symbols = [market.Symbol],
        }, cancellationToken);
        var ticker = tickers?.FirstOrDefault();
        if (ticker is null)
            throw new InvalidDataException(
                $"Mercado Bitcoin returned no ticker for '{market.Symbol}'.");
        await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Ticker, market.StreamId);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                StreamId = market.StreamId,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await SocketClient.SubscribeTickerAsync(market.StreamId,
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
                "Mercado Bitcoin does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var maximumDepth = mdMsg.IsHistoryOnly() ? 1000 : 200;
        var depth = (mdMsg.MaxDepth ?? 200).Min(maximumDepth).Max(1);
        var book = await RestClient.GetOrderBookAsync(market.Symbol, new()
        {
            Limit = depth,
        }, cancellationToken);
        if (book is null)
            throw new InvalidDataException(
                $"Mercado Bitcoin returned no order book for '{market.Symbol}'.");
        await SendBookAsync(market, book.Timestamp, book.Bids, book.Asks, depth,
            mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.OrderBook, market.StreamId);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                StreamId = market.StreamId,
                Depth = depth,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await SocketClient.SubscribeOrderBookAsync(market.StreamId,
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
        var maximum = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
        var from = mdMsg.From?.ToUniversalTime();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var queryFrom = from ?? (mdMsg.To is null ? null : DateTime.UnixEpoch);
        var trades = await RestClient.GetTradesAsync(market.Symbol, new()
        {
            From = queryFrom?.ToUnixSeconds(),
            To = queryFrom is null ? null : to.ToUnixSeconds(),
            Limit = maximum,
        }, cancellationToken);
        foreach (var trade in (trades ?? [])
            .Where(trade => trade is not null &&
                (from is null || trade.Timestamp.FromMercadoBitcoinTimestamp(
                    DateTime.MinValue) >= from) &&
                trade.Timestamp.FromMercadoBitcoinTimestamp(DateTime.MaxValue) <= to)
            .OrderBy(static trade => trade.Timestamp).TakeLast(maximum))
            await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
                cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var key = new StreamKey(StreamTypes.Trade, market.StreamId);
        bool subscribe;
        using (_sync.EnterScope())
        {
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                StreamId = market.StreamId,
            });
            subscribe = AddReference(_streamReferences, key);
        }
        try
        {
            if (subscribe)
                await SocketClient.SubscribeTradesAsync(market.StreamId,
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
                "Mercado Bitcoin publishes candles through REST only; live candle subscriptions are not available.");

        var market = GetMarket(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        var resolution = timeFrame.ToMercadoBitcoinResolution();
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var count = GetCandleCount(mdMsg, timeFrame, to);
        var from = mdMsg.From?.ToUniversalTime() ??
            to - TimeSpan.FromTicks(timeFrame.Ticks * count);
        var candles = await DownloadCandlesAsync(market, resolution, from, to,
            count, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);
        await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
    }

    private readonly record struct CandleValue(long OpenTime, decimal Open,
        decimal High, decimal Low, decimal Close, decimal Volume);

    private SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.Symbol.ToStockSharp(),
            Name = market.Description.IsEmpty()
                ? $"{market.BaseAsset}/{market.QuoteAsset}"
                : market.Description,
            ShortName = $"{market.BaseAsset}/{market.QuoteAsset}",
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteAsset.ToCurrency(),
            PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
            VolumeStep = market.VolumeStep > 0 ? market.VolumeStep : null,
            MinVolume = market.MinimumVolume > 0
                ? market.MinimumVolume
                : null,
            MaxVolume = market.MaximumVolume > 0
                ? market.MaximumVolume
                : null,
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask<CandleValue[]> DownloadCandlesAsync(
        MarketDefinition market, string resolution, DateTime from, DateTime to,
        int count, CancellationToken cancellationToken)
    {
        var values = new SortedDictionary<long, CandleValue>();
        var cursor = to;
        while (values.Count < count && cursor >= from)
        {
            var pageSize = (count - values.Count).Min(1000).Max(1);
            var page = await RestClient.GetCandlesAsync(new()
            {
                Symbol = market.Symbol,
                Resolution = resolution,
                To = cursor.ToUnixSeconds(),
                CountBack = pageSize,
            }, cancellationToken);
            var pageValues = ToCandles(page);
            foreach (var candle in pageValues)
            {
                var time = candle.OpenTime.FromMercadoBitcoinTimestamp(
                    DateTime.MinValue);
                if (time >= from && time <= to)
                    values[candle.OpenTime] = candle;
            }
            if (pageValues.Length < pageSize)
                break;
            var earliest = pageValues.Min(static value => value.OpenTime);
            var earliestTime = earliest.FromMercadoBitcoinTimestamp(
                DateTime.MinValue);
            if (earliestTime <= from)
                break;
            cursor = earliestTime.AddSeconds(-1);
        }
        return [.. values.Values.TakeLast(count)];
    }

    private static CandleValue[] ToCandles(MercadoBitcoinCandles candles)
    {
        if (candles is null)
            return [];
        var count = new[]
        {
            candles.OpenTimes?.Length ?? 0,
            candles.OpenPrices?.Length ?? 0,
            candles.HighPrices?.Length ?? 0,
            candles.LowPrices?.Length ?? 0,
            candles.ClosePrices?.Length ?? 0,
            candles.Volumes?.Length ?? 0,
        }.Min();
        var values = new CandleValue[count];
        for (var i = 0; i < count; i++)
            values[i] = new(candles.OpenTimes[i], candles.OpenPrices[i],
                candles.HighPrices[i], candles.LowPrices[i],
                candles.ClosePrices[i], candles.Volumes[i]);
        return values;
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        var release = false;
        using (_sync.EnterScope())
            if (_level1Subscriptions.Remove(transactionId, out subscription))
                release = ReleaseReference(_streamReferences,
                    new(StreamTypes.Ticker, subscription.StreamId));
        if (release)
            await SocketClient.UnsubscribeTickerAsync(subscription.StreamId,
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
                    new(StreamTypes.OrderBook, subscription.StreamId));
        if (release)
            await SocketClient.UnsubscribeOrderBookAsync(subscription.StreamId,
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
                    new(StreamTypes.Trade, subscription.StreamId));
        if (release)
            await SocketClient.UnsubscribeTradesAsync(subscription.StreamId,
                cancellationToken);
    }

    private async ValueTask OnSocketTickerAsync(
        MercadoBitcoinSocketTicker ticker,
        CancellationToken cancellationToken)
    {
        if (ticker?.Id.IsEmpty() != false || ticker.Data is null)
            return;
        var market = GetMarketByStreamId(ticker.Id);
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendTickerAsync(market, ticker.Data, pair.Key,
                cancellationToken);
    }

    private async ValueTask OnSocketOrderBookAsync(
        MercadoBitcoinSocketOrderBook book,
        CancellationToken cancellationToken)
    {
        if (book?.Id.IsEmpty() != false || book.Data is null)
            return;
        var market = GetMarketByStreamId(book.Id);
        KeyValuePair<long, DepthSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendBookAsync(market, book.Data.Timestamp, book.Data.Bids,
                book.Data.Asks, pair.Value.Depth, pair.Key, cancellationToken);
    }

    private async ValueTask OnSocketTradeAsync(
        MercadoBitcoinSocketTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade?.Id.IsEmpty() != false || trade.Data is null ||
            trade.Data.TradeId <= 0 || trade.Data.Price <= 0 ||
            trade.Data.Volume <= 0)
            return;
        var market = GetMarketByStreamId(trade.Id);
        KeyValuePair<long, MarketSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _tickSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
        foreach (var pair in subscriptions)
            await SendPublicTradeAsync(market, trade.Data.TradeId,
                trade.Data.Timestamp, trade.Data.Price, trade.Data.Volume,
                trade.Data.Side, pair.Key, cancellationToken);
    }

    private ValueTask SendTickerAsync(MercadoBitcoinTicker ticker,
        long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ticker.Symbol.ToStockSharp(),
            ServerTime = ticker.Timestamp.FromMercadoBitcoinTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.OpenPrice, ticker.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
        .TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);

    private ValueTask SendTickerAsync(MarketDefinition market,
        MercadoBitcoinSocketTickerData ticker, long transactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = market.Symbol.ToStockSharp(),
            ServerTime = ticker.Timestamp.FromMercadoBitcoinTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.OpenPrice, ticker.Open)
        .TryAdd(Level1Fields.HighPrice, ticker.High)
        .TryAdd(Level1Fields.LowPrice, ticker.Low)
        .TryAdd(Level1Fields.LastTradePrice, ticker.Last)
        .TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
        .TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
        .TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);

    private ValueTask SendBookAsync(MarketDefinition market, long timestamp,
        IEnumerable<decimal[]> bids, IEnumerable<decimal[]> asks, int depth,
        long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = market.Symbol.ToStockSharp(),
            ServerTime = timestamp.FromMercadoBitcoinTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(bids, false, depth),
            Asks = ToQuotes(asks, true, depth),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(MarketDefinition market,
        MercadoBitcoinTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null)
            return default;
        return SendPublicTradeAsync(market, trade.TradeId, trade.Timestamp,
            trade.Price, trade.Volume, trade.Side, transactionId,
            cancellationToken);
    }

    private ValueTask SendPublicTradeAsync(MarketDefinition market, long tradeId,
        long timestamp, decimal price, decimal volume,
        MercadoBitcoinOrderSides side, long transactionId,
        CancellationToken cancellationToken, bool addToDeduplication = true)
    {
        if (tradeId <= 0 || price <= 0 || volume <= 0)
            return default;
        if (addToDeduplication && !AddPublicTrade(market.Symbol, tradeId,
            transactionId))
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = market.Symbol.ToStockSharp(),
            ServerTime = timestamp.FromMercadoBitcoinTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            TradeId = tradeId,
            TradeStringId = tradeId.ToString(CultureInfo.InvariantCulture),
            TradePrice = price,
            TradeVolume = volume,
            OriginSide = side.ToStockSharp(),
        }, cancellationToken);
    }

    private ValueTask SendCandleAsync(MarketDefinition market,
        CandleValue candle, TimeSpan timeFrame, long transactionId,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.FromMercadoBitcoinTimestamp(CurrentTime);
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

    private static QuoteChange[] ToQuotes(IEnumerable<decimal[]> levels,
        bool isAsk, int depth)
    {
        var values = (levels ?? []).Where(static level =>
            level is { Length: >= 2 } && level[0] > 0 && level[1] > 0);
        return [.. (isAsk
            ? values.OrderBy(static level => level[0])
            : values.OrderByDescending(static level => level[0]))
            .Take(depth).Select(static level =>
                new QuoteChange(level[0], level[1]))];
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
