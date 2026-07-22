namespace StockSharp.FXOpen;

public partial class FXOpenMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
        var securityTypes = lookupMsg.GetSecurityTypes();
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
        TickTraderSymbol[] symbols;
        using (_sync.EnterScope())
            symbols = [.. _symbols.Values.OrderBy(static symbol => symbol.Symbol)];

        foreach (var native in symbols)
        {
            var security = new SecurityMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                SecurityId = native.Symbol.ToSecurityId(),
                SecurityType = ToSecurityType(native),
                Name = native.Description.IsEmpty(native.ExtendedName).IsEmpty(native.Symbol),
                ShortName = native.Symbol,
                Class = native.SecurityName.IsEmpty(native.StatusGroupId),
                Currency = native.ProfitCurrency?.FromMicexCurrencyName(this.AddErrorLog),
                Decimals = native.Precision,
                PriceStep = native.Precision >= 0
                    ? 1m / (decimal)Math.Pow(10, native.Precision) : null,
                VolumeStep = native.TradeAmountStep > 0 ? native.TradeAmountStep : null,
                MinVolume = native.MinTradeAmount > 0 ? native.MinTradeAmount : null,
                MaxVolume = native.MaxTradeAmount > 0 ? native.MaxTradeAmount : null,
                Multiplier = native.ContractSize > 0 ? native.ContractSize : null,
            };

            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            if (left <= 0)
                break;
            await SendOutMessageAsync(security, cancellationToken);
            left--;
        }

        this.AddDebugLog("FXOpen security lookup {0} completed from {1} cached symbols.",
            lookupMsg.TransactionId, symbols.Length);
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessFeedSubscription(mdMsg, DataType.Level1, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessFeedSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

    private async ValueTask ProcessFeedSubscription(MarketDataMessage mdMsg, DataType dataType,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            this.AddDebugLog("FXOpen unsubscribing feed transaction {0}.",
                mdMsg.OriginalTransactionId);
            await RemoveFeedSubscription(mdMsg.OriginalTransactionId, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }
        var symbol = ResolveSymbol(mdMsg.SecurityId).Symbol;
        var depth = dataType == DataType.MarketDepth
            ? (mdMsg.MaxDepth ?? 20).Max(1).Min(100) : 1;
        var webSocket = mdMsg.IsHistoryOnly() ? null : WebSocketClient;
        this.AddDebugLog("FXOpen subscribing {0} for {1}, depth {2}, transaction {3}.",
            dataType, symbol, depth, mdMsg.TransactionId);
        if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.Count is not null ||
            mdMsg.Skip is not null)
        {
            var history = await LoadTicks(mdMsg, symbol, dataType == DataType.MarketDepth,
                cancellationToken);
            this.AddDebugLog("FXOpen loaded {0} historical {1} records for {2}.",
                history.Length, dataType, symbol);
            foreach (var tick in history)
                await SendFeed(mdMsg.TransactionId, dataType, depth, tick, cancellationToken);
        }
        else
        {
            var snapshot = dataType == DataType.MarketDepth
                ? (await RestClient.GetLevel2Async(symbol, cancellationToken)).FirstOrDefault()
                : (await RestClient.GetTicksAsync(symbol, cancellationToken)).FirstOrDefault();
            if (snapshot is not null)
                await SendFeed(mdMsg.TransactionId, dataType, depth, snapshot, cancellationToken);
        }

        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        var subscription = new FeedSubscription
        {
            Symbol = symbol,
            DataType = dataType,
            Depth = depth,
        };
        int oldDepth;
        int newDepth;
        using (_sync.EnterScope())
        {
            oldDepth = _feedSubscriptions.Values.Where(item => item.Symbol.EqualsIgnoreCase(symbol))
                .Select(static item => item.Depth).DefaultIfEmpty(0).Max();
            _feedSubscriptions[mdMsg.TransactionId] = subscription;
            newDepth = _feedSubscriptions.Values.Where(item => item.Symbol.EqualsIgnoreCase(symbol))
                .Select(static item => item.Depth).DefaultIfEmpty(0).Max();
        }
        try
        {
            if (newDepth != oldDepth)
                await webSocket.SubscribeFeedAsync(symbol, newDepth, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _feedSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private async ValueTask RemoveFeedSubscription(long transactionId,
        CancellationToken cancellationToken)
    {
        FeedSubscription removed;
        int depth;
        using (_sync.EnterScope())
        {
            if (!_feedSubscriptions.Remove(transactionId, out removed))
                return;
            depth = _feedSubscriptions.Values
                .Where(item => item.Symbol.EqualsIgnoreCase(removed.Symbol))
                .Select(static item => item.Depth).DefaultIfEmpty(0).Max();
        }
        if (_webSocketClient is null)
            return;
        if (depth == 0)
            await _webSocketClient.UnsubscribeFeedAsync(removed.Symbol, cancellationToken);
        else if (removed.Depth > depth)
            await _webSocketClient.SubscribeFeedAsync(removed.Symbol, depth, cancellationToken);
    }

    private async ValueTask<TickTraderFeedTick[]> LoadTicks(MarketDataMessage mdMsg,
        string symbol, bool isLevel2, CancellationToken cancellationToken)
    {
        var outputCount = Math.Min(100000,
            Math.Max(1, mdMsg.Count ?? (mdMsg.From is null ? 1000 : 10000)));
        var skip = Math.Min(100000, Math.Max(0, mdMsg.Skip ?? 0));
        var requested = Math.Min(100000, outputCount + skip);
        var to = (mdMsg.To ?? DateTime.UtcNow).EnsureUtc();
        var result = new SortedDictionary<DateTime, List<TickTraderFeedTick>>();
        long loaded = 0;

        void AddTicks(IEnumerable<TickTraderFeedTick> ticks, DateTime? from)
        {
            foreach (var tick in ticks)
            {
                if (tick.Symbol.IsEmpty())
                    tick.Symbol = symbol;
                var time = tick.Timestamp.EnsureUtc();
                if (time > to || from is not null && time < from.Value)
                    continue;
                if (!result.TryGetValue(time, out var sameTime))
                    result.Add(time, sameTime = []);
                sameTime.Add(tick);
                loaded++;
            }
        }

        if (mdMsg.From is DateTime requestedFrom)
        {
            var from = requestedFrom.EnsureUtc();
            var cursor = from;
            while (cursor <= to && loaded < requested)
            {
                var pageSize = (int)Math.Min(1000, requested - loaded);
                var page = await RestClient.GetTickHistoryAsync(symbol, isLevel2, cursor,
                    pageSize, cancellationToken);
                var ticks = page?.Ticks ?? [];
                AddTicks(ticks, from);
                if (ticks.Length < pageSize)
                    break;
                var next = ticks.Max(static tick => tick.Timestamp).EnsureUtc()
                    .AddMilliseconds(1);
                if (next <= cursor)
                    break;
                cursor = next;
            }
        }
        else
        {
            var cursor = to;
            while (loaded < requested)
            {
                var pageSize = (int)Math.Min(1000, requested - loaded);
                var page = await RestClient.GetTickHistoryAsync(symbol, isLevel2, cursor,
                    -pageSize, cancellationToken);
                var ticks = page?.Ticks ?? [];
                AddTicks(ticks, null);
                if (ticks.Length < pageSize)
                    break;
                var next = ticks.Min(static tick => tick.Timestamp).EnsureUtc()
                    .AddMilliseconds(-1);
                if (next >= cursor)
                    break;
                cursor = next;
            }
        }

        return [.. result.Values.SelectMany(static ticks => ticks)
            .Skip((int)Math.Min(skip, int.MaxValue)).Take((int)Math.Min(outputCount, int.MaxValue))];
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            this.AddDebugLog("FXOpen unsubscribing candle transaction {0}.",
                mdMsg.OriginalTransactionId);
            await RemoveCandleSubscription(mdMsg.OriginalTransactionId, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        var symbol = ResolveSymbol(mdMsg.SecurityId).Symbol;
        var timeFrame = mdMsg.GetTimeFrame();
        var periodicity = timeFrame.ToNative();
        var priceType = mdMsg.BuildField switch
        {
            null or Level1Fields.BestBidPrice => TickTraderPriceTypes.Bid,
            Level1Fields.BestAskPrice => TickTraderPriceTypes.Ask,
            _ => throw new NotSupportedException(
                $"FXOpen candles cannot be built from '{mdMsg.BuildField}'."),
        };
        var webSocket = mdMsg.IsHistoryOnly() ? null : WebSocketClient;
        this.AddDebugLog("FXOpen subscribing candles {0} {1} {2}, transaction {3}.",
            symbol, periodicity, priceType, mdMsg.TransactionId);
        if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.Count is not null ||
            mdMsg.Skip is not null || mdMsg.IsHistoryOnly())
        {
            var bars = await LoadBars(mdMsg, symbol, periodicity, priceType,
                cancellationToken);
            this.AddDebugLog("FXOpen loaded {0} historical bars for {1} {2} {3}.",
                bars.Length, symbol, periodicity, priceType);
            foreach (var bar in bars)
            {
                await SendOutMessageAsync(new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = symbol.ToSecurityId(),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = bar.Timestamp.EnsureUtc(),
                    CloseTime = bar.Timestamp.EnsureUtc() + timeFrame,
                    OpenPrice = bar.Open,
                    HighPrice = bar.High,
                    LowPrice = bar.Low,
                    ClosePrice = bar.Close,
                    TotalVolume = bar.Volume,
                    State = CandleStates.Finished,
                }, cancellationToken);
            }
        }

        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        var subscription = new CandleSubscription
        {
            Symbol = symbol,
            TimeFrame = timeFrame,
            PriceType = priceType,
        };
        bool shouldSubscribe;
        using (_sync.EnterScope())
        {
            shouldSubscribe = !_candleSubscriptions.Values.Any(item =>
                item.Symbol.EqualsIgnoreCase(symbol) && item.TimeFrame == timeFrame &&
                item.PriceType == priceType);
            _candleSubscriptions[mdMsg.TransactionId] = subscription;
        }
        try
        {
            if (shouldSubscribe)
                await webSocket.SubscribeBarAsync(symbol, periodicity, priceType,
                    cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _candleSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private async ValueTask RemoveCandleSubscription(long transactionId,
        CancellationToken cancellationToken)
    {
        CandleSubscription removed;
        CandleSubscription[] remaining;
        using (_sync.EnterScope())
        {
            if (!_candleSubscriptions.Remove(transactionId, out removed))
                return;
            if (_candleSubscriptions.Values.Any(item => item.Symbol.EqualsIgnoreCase(removed.Symbol) &&
                item.TimeFrame == removed.TimeFrame && item.PriceType == removed.PriceType))
                return;
            remaining = [.. _candleSubscriptions.Values
                .Where(item => item.Symbol.EqualsIgnoreCase(removed.Symbol))
                .GroupBy(static item => (item.TimeFrame, item.PriceType))
                .Select(static group => group.First())];
        }

        if (_webSocketClient is null)
            return;
        await _webSocketClient.UnsubscribeBarsAsync(removed.Symbol, cancellationToken);
        foreach (var item in remaining)
            await _webSocketClient.SubscribeBarAsync(item.Symbol, item.TimeFrame.ToNative(),
                item.PriceType, cancellationToken);
    }

    private async ValueTask<TickTraderBar[]> LoadBars(MarketDataMessage mdMsg, string symbol,
        string periodicity, TickTraderPriceTypes priceType, CancellationToken cancellationToken)
    {
        var outputCount = Math.Min(100000,
            Math.Max(1, mdMsg.Count ?? (mdMsg.From is null ? 1000 : 10000)));
        var skip = Math.Min(100000, Math.Max(0, mdMsg.Skip ?? 0));
        var requested = Math.Min(100000, outputCount + skip);
        var to = (mdMsg.To ?? DateTime.UtcNow).EnsureUtc();
        var result = new SortedDictionary<DateTime, TickTraderBar>();

        if (mdMsg.From is DateTime requestedFrom)
        {
            var from = requestedFrom.EnsureUtc();
            var cursor = from;
            while (cursor <= to && result.Count < requested)
            {
                var pageSize = (int)Math.Min(1000, requested - result.Count);
                var page = await RestClient.GetBarsAsync(symbol, periodicity, priceType, cursor,
                    pageSize, cancellationToken);
                var bars = page?.Bars ?? [];
                foreach (var bar in bars)
                {
                    var time = bar.Timestamp.EnsureUtc();
                    if (time >= from && time <= to)
                        result[time] = bar;
                }
                if (bars.Length < pageSize)
                    break;
                var next = bars.Max(static bar => bar.Timestamp).EnsureUtc().AddMilliseconds(1);
                if (next <= cursor)
                    break;
                cursor = next;
            }
        }
        else
        {
            var cursor = to;
            while (result.Count < requested)
            {
                var pageSize = (int)Math.Min(1000, requested - result.Count);
                var page = await RestClient.GetBarsAsync(symbol, periodicity, priceType, cursor,
                    -pageSize, cancellationToken);
                var bars = page?.Bars ?? [];
                foreach (var bar in bars)
                    result[bar.Timestamp.EnsureUtc()] = bar;
                if (bars.Length < pageSize)
                    break;
                var next = bars.Min(static bar => bar.Timestamp).EnsureUtc().AddMilliseconds(-1);
                if (next >= cursor)
                    break;
                cursor = next;
            }
        }

        return [.. result.Values.Skip((int)skip).Take((int)outputCount)];
    }

    private async ValueTask ProcessFeed(TickTraderFeedTick tick,
        CancellationToken cancellationToken)
    {
        if (tick?.Symbol.IsEmpty() != false)
            return;
        KeyValuePair<long, FeedSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _feedSubscriptions.Where(pair =>
                    pair.Value.Symbol.EqualsIgnoreCase(tick.Symbol))];
        foreach (var pair in subscriptions)
            await SendFeed(pair.Key, pair.Value.DataType, pair.Value.Depth, tick,
                cancellationToken);
    }

    private ValueTask SendFeed(long transactionId, DataType dataType, int depth,
        TickTraderFeedTick tick, CancellationToken cancellationToken)
    {
        var time = tick.Timestamp == default ? DateTime.UtcNow : tick.Timestamp.EnsureUtc();
        if (dataType == DataType.Level1)
        {
            return SendOutMessageAsync(new Level1ChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = tick.Symbol.ToSecurityId(),
                ServerTime = time,
            }
            .TryAdd(Level1Fields.BestBidPrice, tick.BestBid?.Price)
            .TryAdd(Level1Fields.BestBidVolume, tick.BestBid?.Volume)
            .TryAdd(Level1Fields.BestAskPrice, tick.BestAsk?.Price)
            .TryAdd(Level1Fields.BestAskVolume, tick.BestAsk?.Volume), cancellationToken);
        }

        return SendOutMessageAsync(new QuoteChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = tick.Symbol.ToSecurityId(),
            ServerTime = time,
            Bids = [.. (tick.Bids ?? []).Where(static level => level.Price > 0)
                .OrderByDescending(static level => level.Price).Take(depth)
                .Select(static level => new QuoteChange(level.Price, level.Volume))],
            Asks = [.. (tick.Asks ?? []).Where(static level => level.Price > 0)
                .OrderBy(static level => level.Price).Take(depth)
                .Select(static level => new QuoteChange(level.Price, level.Volume))],
        }, cancellationToken);
    }

    private async ValueTask ProcessBar(TickTraderBarUpdateResult result,
        CancellationToken cancellationToken)
    {
        if (result?.SymbolAlias.IsEmpty() != false)
            return;
        foreach (var update in result.Updates ?? [])
        {
            var timeFrame = update.Periodicity.ToTimeFrame();
            if (timeFrame is null)
                continue;
            KeyValuePair<long, CandleSubscription>[] subscriptions;
            using (_sync.EnterScope())
                subscriptions = [.. _candleSubscriptions.Where(pair =>
                    pair.Value.Symbol.EqualsIgnoreCase(result.SymbolAlias) &&
                    pair.Value.TimeFrame == timeFrame.Value &&
                    pair.Value.PriceType == update.PriceType)];
            var openTime = update.Timestamp.EnsureUtc();
            var close = update.Close ?? (update.PriceType == TickTraderPriceTypes.Bid
                ? result.BidClose : result.AskClose) ?? update.Open;
            foreach (var pair in subscriptions)
            {
                await SendOutMessageAsync(new TimeFrameCandleMessage
                {
                    OriginalTransactionId = pair.Key,
                    SecurityId = result.SymbolAlias.ToSecurityId(),
                    TypedArg = timeFrame.Value,
                    OpenTime = openTime,
                    CloseTime = openTime + timeFrame.Value,
                    OpenPrice = update.Open,
                    HighPrice = update.High,
                    LowPrice = update.Low,
                    ClosePrice = close,
                    TotalVolume = update.Volume ?? 0,
                    State = openTime + timeFrame.Value <= DateTime.UtcNow
                        ? CandleStates.Finished : CandleStates.Active,
                }, cancellationToken);
            }
        }
    }

    private static SecurityTypes ToSecurityType(TickTraderSymbol symbol)
    {
        var text = string.Join(" ", symbol.StatusGroupId, symbol.SecurityName,
            symbol.SecurityDescription, symbol.Description);
        if (text.ContainsIgnoreCase("crypto")) return SecurityTypes.CryptoCurrency;
        if (text.ContainsIgnoreCase("ETF")) return SecurityTypes.Etf;
        if (text.ContainsIgnoreCase("stock") || text.ContainsIgnoreCase("share"))
            return SecurityTypes.Stock;
        if (text.ContainsIgnoreCase("index")) return SecurityTypes.Index;
        if (text.ContainsIgnoreCase("future") || symbol.MarginMode == TickTraderMarginModes.Futures)
            return SecurityTypes.Future;
        if (text.ContainsIgnoreCase("metal") || text.ContainsIgnoreCase("commodity") ||
            text.ContainsIgnoreCase("oil") || text.ContainsIgnoreCase("gas"))
            return SecurityTypes.Commodity;
        return symbol.MarginMode == TickTraderMarginModes.Forex
            ? SecurityTypes.Currency : SecurityTypes.Stock;
    }
}
