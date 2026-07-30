namespace StockSharp.MasterLink;

public partial class MasterLinkMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var requested = lookupMsg.Count ?? MaxLookupResults;
        var fetch = Math.Min(
            MaxLookupResults,
            Math.Max(1, requested + skip).Min(int.MaxValue).To<int>());
        var securities = await SafeClient().Lookup(
            lookupMsg.SecurityId.SecurityCode,
            lookupMsg.SecurityId.BoardCode,
            fetch,
            cancellationToken);
        var types = lookupMsg.GetSecurityTypes();
        var left = lookupMsg.Count ?? long.MaxValue;

        foreach (var security in securities ?? [])
        {
            if (security?.Symbol.IsEmpty() != false)
                continue;
            security.IsOddLot =
                lookupMsg.SecurityId.IsOddLotBoard();
            CacheSecurity(security);
            var message = security.ToSecurityMessage(
                lookupMsg.TransactionId);
            if (!message.IsMatch(lookupMsg, types))
                continue;
            if (skip-- > 0)
                continue;
            await SendOutMessageAsync(message, cancellationToken);
            if (--left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(
            mdMsg, DataType.Level1, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(
            mdMsg, DataType.Ticks, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var depth = mdMsg.MaxDepth ?? 5;
        if (depth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth),
                depth,
                "Taishin Nova API provides five order-book levels.");
        }
        return ProcessRealtimeSubscription(
            mdMsg, DataType.MarketDepth, cancellationToken);
    }

    private async ValueTask ProcessRealtimeSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
        {
            if (_marketSubscriptions.Remove(
                mdMsg.OriginalTransactionId))
            {
                _liveCandles.Remove(mdMsg.OriginalTransactionId);
                await SafeClient().Unsubscribe(
                    mdMsg.OriginalTransactionId,
                    cancellationToken);
            }
            return;
        }

        var security =
            mdMsg.SecurityId.ParseMasterLinkSecurity();
        CacheSecurity(security);

        if (dataType == DataType.Level1)
        {
            var quote = await SafeClient().GetQuote(
                security.Symbol,
                security.IsOddLot,
                cancellationToken);
            if (quote != null)
            {
                await SendLevel1(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    quote,
                    cancellationToken);
            }
        }
        else if (dataType == DataType.MarketDepth)
        {
            var quote = await SafeClient().GetQuote(
                security.Symbol,
                security.IsOddLot,
                cancellationToken);
            if (quote != null)
            {
                await SendDepth(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    quote.Bids,
                    quote.Asks,
                    quote.LastUpdated.ToMasterLinkTime() ??
                        quote.CloseTime.ToMasterLinkTime() ??
                        CurrentTime,
                    mdMsg.MaxDepth,
                    cancellationToken);
            }
        }
        else if (mdMsg.IsHistoryOnly() ||
            mdMsg.From != null ||
            mdMsg.To != null ||
            mdMsg.Count != null)
        {
            await SendTradeHistory(
                mdMsg, security, cancellationToken);
        }

        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
            return;
        }

        _marketSubscriptions[mdMsg.TransactionId] = new()
        {
            TransactionId = mdMsg.TransactionId,
            SecurityId = mdMsg.SecurityId,
            DataType = dataType,
            MaxDepth = mdMsg.MaxDepth,
        };
        try
        {
            await SafeClient().Subscribe(
                mdMsg.TransactionId,
                dataType == DataType.Level1
                    ? "level1"
                    : dataType == DataType.Ticks
                        ? "ticks"
                        : "depth",
                security.Symbol,
                security.IsOddLot,
                cancellationToken);
        }
        catch
        {
            _marketSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private async ValueTask SendTradeHistory(
        MarketDataMessage mdMsg,
        MasterLinkSecurity security,
        CancellationToken cancellationToken)
    {
        var limit = mdMsg.Count is > 0
            ? Math.Min(500, mdMsg.Count.Value).To<int>()
            : 500;
        IEnumerable<MasterLinkTrade> trades =
            (await SafeClient().GetTrades(
                security.Symbol,
                security.IsOddLot,
                limit,
                cancellationToken)) ?? [];

        trades = trades
            .Where(trade => trade?.Price is > 0 &&
                trade.IsTrial != true)
            .OrderBy(trade =>
                trade.Time.ToMasterLinkTime() ?? DateTime.MinValue);
        if (mdMsg.From is DateTime from)
        {
            var normalized = MasterLinkExtensions.NormalizeUtc(from);
            trades = trades.Where(trade =>
                (trade.Time.ToMasterLinkTime() ??
                    DateTime.MinValue) >= normalized);
        }
        if (mdMsg.To is DateTime to)
        {
            var normalized = MasterLinkExtensions.NormalizeUtc(to);
            trades = trades.Where(trade =>
                (trade.Time.ToMasterLinkTime() ??
                    DateTime.MaxValue) <= normalized);
        }
        if (mdMsg.Count is > 0 and <= int.MaxValue)
            trades = trades.TakeLast((int)mdMsg.Count.Value);

        var index = 0;

        foreach (var trade in trades)
        {
            await SendTick(
                mdMsg.TransactionId,
                mdMsg.SecurityId,
                trade,
                index++,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
        {
            if (_marketSubscriptions.Remove(
                mdMsg.OriginalTransactionId))
            {
                _liveCandles.Remove(mdMsg.OriginalTransactionId);
                await SafeClient().Unsubscribe(
                    mdMsg.OriginalTransactionId,
                    cancellationToken);
            }
            return;
        }

        var security =
            mdMsg.SecurityId.ParseMasterLinkSecurity();
        CacheSecurity(security);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToNativeTimeFrame();

        if (!mdMsg.IsHistoryOnly() &&
            timeFrame != TimeSpan.FromMinutes(1))
        {
            throw new NotSupportedException(
                "Taishin Nova streams one-minute candles only. Use StockSharp aggregation for larger realtime intervals.");
        }

        if (mdMsg.IsHistoryOnly() ||
            mdMsg.From != null ||
            mdMsg.To != null ||
            mdMsg.Count != null)
        {
            await SendCandleHistory(
                mdMsg, security, timeFrame, cancellationToken);
        }

        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
            return;
        }

        _marketSubscriptions[mdMsg.TransactionId] = new()
        {
            TransactionId = mdMsg.TransactionId,
            SecurityId = mdMsg.SecurityId,
            DataType = timeFrame.TimeFrame(),
            TimeFrame = timeFrame,
        };
        try
        {
            await SafeClient().Subscribe(
                mdMsg.TransactionId,
                "candles",
                security.Symbol,
                security.IsOddLot,
                cancellationToken);
        }
        catch
        {
            _marketSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private async ValueTask SendCandleHistory(
        MarketDataMessage mdMsg,
        MasterLinkSecurity security,
        TimeSpan timeFrame,
        CancellationToken cancellationToken)
    {
        var response = await SafeClient().GetCandles(
            security.Symbol,
            mdMsg.From,
            mdMsg.To,
            timeFrame.ToNativeTimeFrame(),
            AdjustedCandles,
            cancellationToken);
        IEnumerable<(MasterLinkCandle candle, DateTime time)> candles =
            (response?.Data ?? [])
            .Select(candle => (
                candle,
                time: GetCandleTime(candle) ?? DateTime.MinValue))
            .Where(item => item.candle != null &&
                item.time != DateTime.MinValue)
            .OrderBy(item => item.time);
        if (mdMsg.From is DateTime from)
        {
            var normalized = MasterLinkExtensions.NormalizeUtc(from);
            candles = candles.Where(item =>
                item.time >= normalized);
        }
        if (mdMsg.To is DateTime to)
        {
            var normalized = MasterLinkExtensions.NormalizeUtc(to);
            candles = candles.Where(item =>
                item.time <= normalized);
        }
        if (mdMsg.Count is > 0 and <= int.MaxValue)
            candles = candles.TakeLast((int)mdMsg.Count.Value);

        foreach (var (candle, openTime) in candles)
        {
            await SendCandle(
                mdMsg.TransactionId,
                mdMsg.SecurityId,
                timeFrame,
                candle,
                openTime,
                CandleStates.Finished,
                cancellationToken);
        }
    }

    private async ValueTask OnMarketData(
        long subscriptionId,
        string channel,
        JToken data,
        CancellationToken cancellationToken)
    {
        if (!_marketSubscriptions.TryGetValue(
            subscriptionId, out var subscription) ||
            data == null)
        {
            return;
        }

        switch (channel?.ToLowerInvariant())
        {
            case "trades":
                {
                    var trade = data.ToObject<MasterLinkTrade>();
                    if (trade?.Price is not > 0 ||
                        trade.IsTrial == true)
                    {
                        return;
                    }
                    if (subscription.DataType == DataType.Ticks)
                    {
                        await SendTick(
                            subscription.TransactionId,
                            subscription.SecurityId,
                            trade,
                            0,
                            cancellationToken);
                    }
                    else if (subscription.DataType == DataType.Level1)
                    {
                        await SendTradeLevel1(
                            subscription, trade, cancellationToken);
                    }
                    break;
                }

            case "books":
                {
                    var book = data.ToObject<MasterLinkBook>();
                    if (book == null)
                        return;
                    if (subscription.DataType == DataType.MarketDepth)
                    {
                        await SendDepth(
                            subscription.TransactionId,
                            subscription.SecurityId,
                            book.Bids,
                            book.Asks,
                            book.Time.ToMasterLinkTime() ?? CurrentTime,
                            subscription.MaxDepth,
                            cancellationToken);
                    }
                    else if (subscription.DataType == DataType.Level1)
                    {
                        await SendBookLevel1(
                            subscription, book, cancellationToken);
                    }
                    break;
                }

            case "aggregates":
                {
                    if (subscription.DataType == DataType.Level1)
                    {
                        await SendAggregateLevel1(
                            subscription,
                            data.ToObject<MasterLinkAggregate>(),
                            cancellationToken);
                    }
                    break;
                }

            case "candles":
                {
                    if (subscription.TimeFrame != null)
                    {
                        await ProcessLiveCandle(
                            subscription,
                            data.ToObject<MasterLinkCandle>(),
                            cancellationToken);
                    }
                    break;
                }
        }
    }

    private async ValueTask SendLevel1(
        long transactionId,
        SecurityId securityId,
        MasterLinkQuote quote,
        CancellationToken cancellationToken)
    {
        var bid = (quote.Bids ?? [])
            .Where(level => level.Price > 0)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault();
        var ask = (quote.Asks ?? [])
            .Where(level => level.Price > 0)
            .OrderBy(level => level.Price)
            .FirstOrDefault();
        var last = quote.LastTrade;
        var serverTime =
            quote.LastUpdated.ToMasterLinkTime() ??
            last?.Time.ToMasterLinkTime() ??
            quote.CloseTime.ToMasterLinkTime() ??
            CurrentTime;
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = serverTime,
        }
        .TryAdd(
            Level1Fields.LastTradePrice,
            quote.LastPrice ?? last?.Price)
        .TryAdd(
            Level1Fields.LastTradeVolume,
            quote.LastSize ?? last?.Size)
        .TryAdd(
            Level1Fields.LastTradeTime,
            (quote.LastPrice ?? last?.Price) != null
                ? serverTime
                : null)
        .TryAdd(Level1Fields.OpenPrice, quote.OpenPrice)
        .TryAdd(Level1Fields.HighPrice, quote.HighPrice)
        .TryAdd(Level1Fields.LowPrice, quote.LowPrice)
        .TryAdd(Level1Fields.ClosePrice, quote.ClosePrice)
        .TryAdd(Level1Fields.AveragePrice, quote.AvgPrice)
        .TryAdd(Level1Fields.Change, quote.Change)
        .TryAdd(Level1Fields.Volume, quote.Total?.TradeVolume)
        .TryAdd(Level1Fields.BestBidPrice, bid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bid?.Size)
        .TryAdd(Level1Fields.BestAskPrice, ask?.Price)
        .TryAdd(Level1Fields.BestAskVolume, ask?.Size)
        .TryAdd(
            Level1Fields.BidsVolume,
            quote.Bids?.Sum(level => level.Size))
        .TryAdd(
            Level1Fields.AsksVolume,
            quote.Asks?.Sum(level => level.Size));
        if (message.Changes.Count > 0)
            await SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendDepth(
        long transactionId,
        SecurityId securityId,
        IEnumerable<MasterLinkBookLevel> bids,
        IEnumerable<MasterLinkBookLevel> asks,
        DateTime serverTime,
        int? maxDepth,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = serverTime,
            Bids =
            [
                .. (bids ?? [])
                    .Where(level =>
                        level.Price > 0 && level.Size >= 0)
                    .OrderByDescending(level => level.Price)
                    .Take(maxDepth ?? 5)
                    .Select(level =>
                        new QuoteChange(level.Price, level.Size)),
            ],
            Asks =
            [
                .. (asks ?? [])
                    .Where(level =>
                        level.Price > 0 && level.Size >= 0)
                    .OrderBy(level => level.Price)
                    .Take(maxDepth ?? 5)
                    .Select(level =>
                        new QuoteChange(level.Price, level.Size)),
            ],
        }, cancellationToken);

    private ValueTask SendTick(
        long transactionId,
        SecurityId securityId,
        MasterLinkTrade trade,
        int index,
        CancellationToken cancellationToken)
    {
        var serverTime =
            trade.Time.ToMasterLinkTime() ?? CurrentTime;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            TradeStringId = trade.Serial is > 0
                ? trade.Serial.Value.ToString(
                    CultureInfo.InvariantCulture)
                : $"{securityId.SecurityCode}:{serverTime.Ticks}:{index}",
            TradePrice = trade.Price,
            TradeVolume = trade.Size ?? trade.Volume,
            ServerTime = serverTime,
        }, cancellationToken);
    }

    private ValueTask SendTradeLevel1(
        MarketSubscription subscription,
        MasterLinkTrade trade,
        CancellationToken cancellationToken)
    {
        var time = trade.Time.ToMasterLinkTime() ?? CurrentTime;
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = subscription.TransactionId,
            SecurityId = subscription.SecurityId,
            ServerTime = time,
        }
        .TryAdd(Level1Fields.LastTradePrice, trade.Price)
        .TryAdd(Level1Fields.LastTradeVolume, trade.Size)
        .TryAdd(Level1Fields.LastTradeTime, time)
        .TryAdd(Level1Fields.BestBidPrice, trade.Bid)
        .TryAdd(Level1Fields.BestAskPrice, trade.Ask)
        .TryAdd(Level1Fields.Volume, trade.Volume);
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendBookLevel1(
        MarketSubscription subscription,
        MasterLinkBook book,
        CancellationToken cancellationToken)
    {
        var bid = (book.Bids ?? [])
            .Where(level => level.Price > 0)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault();
        var ask = (book.Asks ?? [])
            .Where(level => level.Price > 0)
            .OrderBy(level => level.Price)
            .FirstOrDefault();
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = subscription.TransactionId,
            SecurityId = subscription.SecurityId,
            ServerTime =
                book.Time.ToMasterLinkTime() ?? CurrentTime,
        }
        .TryAdd(Level1Fields.BestBidPrice, bid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bid?.Size)
        .TryAdd(Level1Fields.BestAskPrice, ask?.Price)
        .TryAdd(Level1Fields.BestAskVolume, ask?.Size)
        .TryAdd(
            Level1Fields.BidsVolume,
            book.Bids?.Sum(level => level.Size))
        .TryAdd(
            Level1Fields.AsksVolume,
            book.Asks?.Sum(level => level.Size));
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendAggregateLevel1(
        MarketSubscription subscription,
        MasterLinkAggregate aggregate,
        CancellationToken cancellationToken)
    {
        if (aggregate == null)
            return default;
        var time =
            aggregate.Time.ToMasterLinkTime() ?? CurrentTime;
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = subscription.TransactionId,
            SecurityId = subscription.SecurityId,
            ServerTime = time,
        }
        .TryAdd(Level1Fields.LastTradePrice, aggregate.ClosePrice)
        .TryAdd(
            Level1Fields.LastTradeTime,
            aggregate.ClosePrice != null ? time : null)
        .TryAdd(Level1Fields.OpenPrice, aggregate.OpenPrice)
        .TryAdd(Level1Fields.HighPrice, aggregate.HighPrice)
        .TryAdd(Level1Fields.LowPrice, aggregate.LowPrice)
        .TryAdd(Level1Fields.Change, aggregate.Change)
        .TryAdd(Level1Fields.Volume, aggregate.TradeVolume);
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private async ValueTask ProcessLiveCandle(
        MarketSubscription subscription,
        MasterLinkCandle candle,
        CancellationToken cancellationToken)
    {
        if (candle == null ||
            GetCandleTime(candle) is not DateTime openTime)
        {
            return;
        }
        if (_liveCandles.TryGetValue(
            subscription.TransactionId, out var previous))
        {
            if (previous.OpenTime > openTime)
                return;
            if (previous.OpenTime < openTime)
            {
                await SendCandle(
                    previous.Subscription.TransactionId,
                    previous.Subscription.SecurityId,
                    previous.Subscription.TimeFrame ??
                        TimeSpan.FromMinutes(1),
                    previous.Candle,
                    previous.OpenTime,
                    CandleStates.Finished,
                    cancellationToken);
            }
        }
        _liveCandles[subscription.TransactionId] = new()
        {
            OpenTime = openTime,
            Candle = candle,
            Subscription = subscription,
        };
        await SendCandle(
            subscription.TransactionId,
            subscription.SecurityId,
            subscription.TimeFrame ?? TimeSpan.FromMinutes(1),
            candle,
            openTime,
            CandleStates.Active,
            cancellationToken);
    }

    private ValueTask SendCandle(
        long transactionId,
        SecurityId securityId,
        TimeSpan timeFrame,
        MasterLinkCandle candle,
        DateTime openTime,
        CandleStates state,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new TimeFrameCandleMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            TypedArg = timeFrame,
            OpenTime = openTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            State = state,
        }, cancellationToken);

    private static DateTime? GetCandleTime(
        MasterLinkCandle candle)
        => candle?.Time.ToMasterLinkTime() ??
            candle?.Date.ParseMasterLinkTime();
}
