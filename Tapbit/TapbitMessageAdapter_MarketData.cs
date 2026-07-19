namespace StockSharp.Tapbit;

public partial class TapbitMessageAdapter
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
            : lookupMsg.SecurityId.SecurityCode.NormalizeTapbitSymbol();
        TapbitInstrument[] products;
        using (_sync.EnterScope())
            products = [.. _products.Values];
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var product in products.OrderBy(
            static value => value.ProductType).ThenBy(
            static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    product.ProductType.ToBoardCode()))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(product.Symbol))
                continue;
            var security = CreateSecurity(product,
                lookupMsg.TransactionId);
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
                "Tapbit does not expose historical Level1 events.");

        var product = GetProduct(mdMsg.SecurityId);
        await SendLevel1SnapshotAsync(product, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductType = product.ProductType,
                Symbol = product.Symbol,
                StreamSymbol = product.StreamSymbol,
            });
        try
        {
            await RefreshTopicsAsync(cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _level1Subscriptions.Remove(mdMsg.TransactionId);
            await RefreshTopicsAsync(cancellationToken);
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
                "Tapbit does not expose historical order-book events.");

        var product = GetProduct(mdMsg.SecurityId);
        var depth = NormalizeStreamDepth(mdMsg.MaxDepth);
        var snapshot = await RestClient.GetOrderBookAsync(product, depth,
            cancellationToken);
        if (snapshot is not null)
            await SendBookAsync(product, snapshot, depth,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var baseTopic = $"{product.ProductType.ToTopicPrefix()}/" +
            $"orderBook.{product.StreamSymbol}";
        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductType = product.ProductType,
                Symbol = product.Symbol,
                StreamSymbol = product.StreamSymbol,
                Depth = depth,
                Topic = baseTopic,
            });
            _books.Remove(GetBookKey(product.ProductType,
                product.StreamSymbol));
        }
        try
        {
            await RefreshTopicsAsync(cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _depthSubscriptions.Remove(mdMsg.TransactionId);
            await RefreshTopicsAsync(cancellationToken);
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
        var trades = PrepareTrades(await RestClient.GetPublicTradesAsync(
            product, cancellationToken));
        foreach (var item in trades.Where(item => IsTradeInRange(item.Trade,
            mdMsg.From, mdMsg.To)).TakeLast(maximum))
            _ = await SendPublicTradeAsync(item.Trade, item.Identity,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductType = product.ProductType,
                Symbol = product.Symbol,
                StreamSymbol = product.StreamSymbol,
                From = mdMsg.From?.ToUniversalTime(),
                To = mdMsg.To?.ToUniversalTime(),
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
            UnsubscribeCandles(mdMsg.OriginalTransactionId);
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
        var candles = await LoadCandlesAsync(product, timeFrame, from, to,
            maximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(product, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                ProductType = product.ProductType,
                Symbol = product.Symbol,
                StreamSymbol = product.StreamSymbol,
                TimeFrame = timeFrame,
                To = mdMsg.To?.ToUniversalTime(),
            });
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private SecurityMessage CreateSecurity(TapbitInstrument product,
        long originalTransactionId)
        => new SecurityMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.ProductType),
            Name = product.Symbol,
            ShortName = product.Symbol,
            SecurityType = product.ProductType == TapbitProductTypes.Spot
                ? SecurityTypes.CryptoCurrency
                : SecurityTypes.Future,
            Currency = product.QuoteAsset.ToCurrency(),
            PriceStep = product.PriceStep,
            Decimals = product.PricePrecision,
            VolumeStep = product.VolumeStep,
            MinVolume = product.MinimumVolume,
            MaxVolume = product.MaximumVolume,
            Multiplier = product.Multiplier,
            OriginalTransactionId = originalTransactionId,
        }.TryFillUnderlyingId(product.BaseAsset?.ToUpperInvariant());

    private async ValueTask SendLevel1SnapshotAsync(TapbitInstrument product,
        long target, CancellationToken cancellationToken)
    {
        if (product.ProductType == TapbitProductTypes.Spot)
        {
            var ticker = await RestClient.GetSpotTickerAsync(product.Symbol,
                cancellationToken);
            if (ticker is not null)
                await SendSpotTickerAsync(product, ticker, target,
                    cancellationToken);
            return;
        }

        var futuresTicker = await RestClient.GetFuturesTickerAsync(
            product.Symbol, cancellationToken);
        if (futuresTicker is not null)
            await SendFuturesTickerAsync(product, futuresTicker, target,
                cancellationToken);
    }

    private async ValueTask RefreshTopicsAsync(
        CancellationToken cancellationToken)
    {
        string[] topics;
        using (_sync.EnterScope())
        {
            var result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var subscription in _level1Subscriptions.Values)
                result.Add($"{subscription.ProductType.ToTopicPrefix()}/" +
                    $"ticker.{subscription.StreamSymbol}");
            foreach (var group in _depthSubscriptions.Values.GroupBy(
                static value => new ProductKey(value.ProductType,
                    value.StreamSymbol)))
            {
                var first = group.First();
                result.Add($"{first.Topic}.{group.Max(
                    static value => value.Depth)}");
            }
            topics = [.. result];
        }
        await SocketClient.SetTopicsAsync(topics, cancellationToken);
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId);
        await RefreshTopicsAsync(cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
        {
            if (_depthSubscriptions.Remove(transactionId,
                out var subscription))
                _books.Remove(GetBookKey(subscription.ProductType,
                    subscription.StreamSymbol));
        }
        await RefreshTopicsAsync(cancellationToken);
    }

    private void UnsubscribeTicks(long transactionId)
    {
        using (_sync.EnterScope())
        {
            _tickSubscriptions.Remove(transactionId);
            RemoveTradeKeys(transactionId);
        }
    }

    private void UnsubscribeCandles(long transactionId)
    {
        using (_sync.EnterScope())
        {
            _candleSubscriptions.Remove(transactionId);
            RemoveFingerprintPrefix(_candleFingerprints, transactionId);
        }
    }

    private async ValueTask OnSocketTickerAsync(
        TapbitProductTypes productType, TapbitWsTicker ticker,
        CancellationToken cancellationToken)
    {
        if (ticker?.Symbol.IsEmpty() != false)
            return;
        var product = GetStreamProduct(productType, ticker.Symbol);
        if (product is null)
            return;
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _level1Subscriptions.Where(pair =>
                pair.Value.ProductType == productType &&
                pair.Value.Symbol.EqualsIgnoreCase(product.Symbol)).Select(
                static pair => pair.Key)];
        foreach (var target in targets)
            await SendWsTickerAsync(product, ticker, target,
                cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(
        TapbitProductTypes productType, string topic,
        TapbitWsActions? action, TapbitWsBook update,
        CancellationToken cancellationToken)
    {
        if (topic.IsEmpty() || update is null)
            return;
        const string marker = "orderBook.";
        var markerIndex = topic.IndexOf(marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return;
        var streamSymbol = topic[(markerIndex + marker.Length)..];
        var separatorIndex = streamSymbol.IndexOf('.');
        if (separatorIndex >= 0)
            streamSymbol = streamSymbol[..separatorIndex];
        streamSymbol = streamSymbol.NormalizeTapbitSymbol();
        var product = GetStreamProduct(productType, streamSymbol);
        if (product is null)
            return;

        (long Id, int Depth)[] targets;
        QuoteChange[] bids;
        QuoteChange[] asks;
        var shouldSend = false;
        using (_sync.EnterScope())
        {
            targets = [.. _depthSubscriptions.Where(pair =>
                pair.Value.ProductType == productType &&
                pair.Value.StreamSymbol.EqualsIgnoreCase(streamSymbol))
                .Select(static pair => (pair.Key, pair.Value.Depth))];
            if (targets.Length == 0)
                return;

            var key = GetBookKey(productType, streamSymbol);
            if (!_books.TryGetValue(key, out var state))
                _books.Add(key, state = new());
            if (state.IsInitialized && update.Version > 0 &&
                update.Version <= state.Version)
                return;

            if (action == TapbitWsActions.Insert)
            {
                state.Bids.Clear();
                state.Asks.Clear();
                ApplyLevels(state.Bids, update.Bids);
                ApplyLevels(state.Asks, update.Asks);
                state.IsInitialized = true;
            }
            else if (state.IsInitialized)
            {
                ApplyLevels(state.Bids, update.Bids);
                ApplyLevels(state.Asks, update.Asks);
            }
            else
            {
                return;
            }
            state.Version = update.Version;
            var maximum = targets.Max(static value => value.Depth);
            bids = [.. state.Bids.Take(maximum).Select(static pair =>
                new QuoteChange(pair.Key, pair.Value))];
            asks = [.. state.Asks.Take(maximum).Select(static pair =>
                new QuoteChange(pair.Key, pair.Value))];
            shouldSend = true;
        }
        if (!shouldSend)
            return;
        foreach (var target in targets)
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                SecurityId = product.Symbol.ToStockSharp(productType),
                ServerTime = GetTime(update.Timestamp),
                OriginalTransactionId = target.Id,
                State = QuoteChangeStates.SnapshotComplete,
                Bids = [.. bids.Take(target.Depth)],
                Asks = [.. asks.Take(target.Depth)],
            }, cancellationToken);
    }

    private ValueTask SendSpotTickerAsync(TapbitInstrument product,
        TapbitSpotTicker ticker, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.ProductType),
            ServerTime = CurrentTime,
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice.ToDecimal())
        .TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
        .TryAdd(Level1Fields.Turnover, ticker.Turnover.ToDecimal())
        .TryAdd(Level1Fields.Change, ticker.Change.ToDecimal()),
            cancellationToken);

    private ValueTask SendFuturesTickerAsync(TapbitInstrument product,
        TapbitFuturesTicker ticker, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.ProductType),
            ServerTime = GetTime(ticker.Timestamp),
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidVolume, ticker.BestBidVolume.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice.ToDecimal())
        .TryAdd(Level1Fields.BestAskVolume, ticker.BestAskVolume.ToDecimal())
        .TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
        .TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice.ToDecimal())
        .TryAdd(Level1Fields.Change, ticker.Change.ToDecimal()),
            cancellationToken);

    private ValueTask SendWsTickerAsync(TapbitInstrument product,
        TapbitWsTicker ticker, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.ProductType),
            ServerTime = GetTime(ticker.Timestamp),
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidVolume,
            ticker.BestBidVolume.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice.ToDecimal())
        .TryAdd(Level1Fields.BestAskVolume,
            ticker.BestAskVolume.ToDecimal())
        .TryAdd(Level1Fields.OpenPrice,
            (ticker.OpenPrice24h.IsEmpty()
                ? ticker.OpenPrice
                : ticker.OpenPrice24h).ToDecimal())
        .TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
        .TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
        .TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
        .TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice.ToDecimal())
        .TryAdd(Level1Fields.Index, ticker.IndexPrice.ToDecimal())
        .TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest.ToDecimal()),
            cancellationToken);

    private ValueTask SendBookAsync(TapbitInstrument product,
        TapbitOrderBook book, int depth, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.ProductType),
            ServerTime = GetTime(book.Timestamp),
            OriginalTransactionId = target,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(book.Bids, depth, true),
            Asks = ToQuotes(book.Asks, depth, false),
        }, cancellationToken);

    private async ValueTask<bool> SendPublicTradeAsync(
        TapbitPublicTrade trade, string identity, long target,
        CancellationToken cancellationToken)
    {
        if (trade is null || identity.IsEmpty())
            return false;
        var key = new TradeDeliveryKey(target, identity);
        using (_sync.EnterScope())
        {
            if (!_seenPublicTrades.Add(key))
                return false;
            _publicTradeDeliveryOrder.Enqueue(key);
            while (_publicTradeDeliveryOrder.Count > _maximumDeliveryKeys)
                _seenPublicTrades.Remove(
                    _publicTradeDeliveryOrder.Dequeue());
        }
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = trade.Symbol.ToStockSharp(trade.ProductType),
            ServerTime = GetTime(trade.Timestamp),
            OriginalTransactionId = target,
            TradeStringId = identity,
            TradePrice = trade.Price,
            TradeVolume = trade.Volume,
            OriginSide = trade.Side,
        }, cancellationToken);
        return true;
    }

    private ValueTask SendCandleAsync(TapbitInstrument product,
        TapbitCandle candle, TimeSpan timeFrame, long target,
        CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.ToUtcTime();
        var closeTime = openTime + timeFrame;
        using (_sync.EnterScope())
            _candleFingerprints[$"{target}:{candle.OpenTime}"] = new(
                candle.Open, candle.High, candle.Low, candle.Close,
                candle.Volume);
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = product.Symbol.ToStockSharp(product.ProductType),
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            TotalPrice = candle.Turnover ?? 0m,
            TypedArg = timeFrame,
            OriginalTransactionId = target,
            State = closeTime <= CurrentTime
                ? CandleStates.Finished
                : CandleStates.Active,
        }, cancellationToken);
    }

    private async ValueTask PollMarketAsync(
        CancellationToken cancellationToken)
    {
        await PollPublicTradesAsync(cancellationToken);
        await PollCandlesAsync(cancellationToken);
    }

    private async ValueTask PollPublicTradesAsync(
        CancellationToken cancellationToken)
    {
        (TapbitInstrument Product, long[] Targets)[] groups;
        using (_sync.EnterScope())
            groups = [.. _tickSubscriptions.GroupBy(static pair =>
                new ProductKey(pair.Value.ProductType, pair.Value.Symbol))
                .Select(group => (_products[group.Key], group.Select(
                    static pair => pair.Key).ToArray()))];
        var finished = new List<long>();
        foreach (var group in groups)
        {
            var trades = PrepareTrades(await RestClient.GetPublicTradesAsync(
                group.Product, cancellationToken));
            foreach (var target in group.Targets)
            {
                TickSubscription subscription;
                using (_sync.EnterScope())
                    if (!_tickSubscriptions.TryGetValue(target,
                        out subscription))
                        continue;
                foreach (var item in trades.Where(item => IsTradeInRange(
                    item.Trade, subscription.From, subscription.To)))
                {
                    _ = await SendPublicTradeAsync(item.Trade, item.Identity,
                        target, cancellationToken);
                }
                if (subscription.To is DateTime to && CurrentTime >= to)
                    finished.Add(target);
            }
        }
        foreach (var target in finished.Distinct())
        {
            using (_sync.EnterScope())
            {
                _tickSubscriptions.Remove(target);
                RemoveTradeKeys(target);
            }
            await SendSubscriptionFinishedAsync(target, cancellationToken);
        }
    }

    private async ValueTask PollCandlesAsync(
        CancellationToken cancellationToken)
    {
        (long Id, CandleSubscription Subscription)[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _candleSubscriptions.Select(static pair =>
                (pair.Key, pair.Value))];
        var finished = new List<long>();
        foreach (var item in subscriptions)
        {
            TapbitInstrument product;
            using (_sync.EnterScope())
                if (!_products.TryGetValue(new(item.Subscription.ProductType,
                    item.Subscription.Symbol), out product))
                    continue;
            var now = DateTime.UtcNow;
            var upperBound = item.Subscription.To is DateTime requestedTo
                ? requestedTo.Min(now)
                : now;
            var from = upperBound - TimeSpan.FromTicks(
                item.Subscription.TimeFrame.Ticks * 2);
            var candles = await RestClient.GetCandlesAsync(product,
                item.Subscription.TimeFrame, from, upperBound,
                cancellationToken);
            foreach (var candle in (candles ?? []).Where(static candle =>
                candle is not null && candle.OpenTime > 0).OrderBy(
                static candle => candle.OpenTime))
            {
                var key = $"{item.Id}:{candle.OpenTime}";
                var fingerprint = new CandleFingerprint(candle.Open,
                    candle.High, candle.Low, candle.Close, candle.Volume);
                var isNew = false;
                var isChanged = false;
                using (_sync.EnterScope())
                {
                    isNew = !_candleFingerprints.TryGetValue(key,
                        out var previous);
                    isChanged = isNew || previous != fingerprint;
                    if (isChanged)
                        _candleFingerprints[key] = fingerprint;
                }
                if (!isChanged)
                    continue;
                await SendCandleAsync(product, candle,
                    item.Subscription.TimeFrame, item.Id,
                    cancellationToken);
            }
            if (item.Subscription.To is DateTime to && CurrentTime >= to)
                finished.Add(item.Id);
        }
        foreach (var target in finished)
        {
            UnsubscribeCandles(target);
            await SendSubscriptionFinishedAsync(target, cancellationToken);
        }
    }

    private async ValueTask<TapbitCandle[]> LoadCandlesAsync(
        TapbitInstrument product, TimeSpan timeFrame, DateTime from,
        DateTime to, int maximum, CancellationToken cancellationToken)
    {
        var result = new List<TapbitCandle>();
        var cursor = from.ToUniversalTime();
        var upperBound = to.ToUniversalTime();
        while (result.Count < maximum && cursor <= upperBound)
        {
            var pageSize = (maximum - result.Count).Min(200).Max(1);
            var windowEnd = (cursor + TimeSpan.FromTicks(
                timeFrame.Ticks * (pageSize - 1))).Min(upperBound);
            var items = await RestClient.GetCandlesAsync(product, timeFrame,
                cursor, windowEnd, cancellationToken);
            var valid = (items ?? []).Where(static item => item is not null &&
                item.OpenTime > 0).OrderBy(static item => item.OpenTime)
                .ToArray();
            if (valid.Length > 0)
                result.AddRange(valid);
            var next = valid.Length > 0
                ? valid[^1].OpenTime.ToUtcTime() + timeFrame
                : windowEnd + timeFrame;
            if (next <= cursor)
                break;
            cursor = next;
        }
        return [.. result.Where(item =>
                item.OpenTime.ToUtcTime() >= from.ToUniversalTime() &&
                item.OpenTime.ToUtcTime() <= to.ToUniversalTime())
            .GroupBy(static item => item.OpenTime)
            .Select(static group => group.First())
            .OrderBy(static item => item.OpenTime)
            .TakeLast(maximum)];
    }

    private static (TapbitPublicTrade Trade, string Identity)[]
        PrepareTrades(TapbitPublicTrade[] trades)
    {
        var occurrences = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var result = new List<(TapbitPublicTrade, string)>();
        foreach (var trade in (trades ?? []).Where(static trade =>
            trade is not null && trade.Timestamp > 0).Reverse())
        {
            var baseIdentity = string.Join(':',
                trade.Timestamp.ToString(CultureInfo.InvariantCulture),
                trade.Price.ToString(CultureInfo.InvariantCulture),
                trade.Volume.ToString(CultureInfo.InvariantCulture),
                trade.Side.ToString());
            occurrences.TryGetValue(baseIdentity, out var occurrence);
            occurrences[baseIdentity] = ++occurrence;
            result.Add((trade, GetTradeIdentity(trade, occurrence)));
        }
        return [.. result.OrderBy(static item => item.Item1.Timestamp)];
    }

    private static void ApplyLevels(
        IDictionary<decimal, decimal> destination,
        IEnumerable<TapbitLevel> levels)
    {
        foreach (var level in levels ?? [])
        {
            if (level is null || level.Price <= 0 || level.Volume < 0)
                continue;
            if (level.Volume == 0)
                destination.Remove(level.Price);
            else
                destination[level.Price] = level.Volume;
        }
    }

    private static QuoteChange[] ToQuotes(IEnumerable<TapbitLevel> levels,
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

    private static bool IsTradeInRange(TapbitPublicTrade trade,
        DateTime? from, DateTime? to)
    {
        if (trade is null)
            return false;
        var time = trade.Timestamp.ToUtcTime();
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

    private static string GetBookKey(TapbitProductTypes productType,
        string streamSymbol)
        => $"{productType}:{streamSymbol.NormalizeTapbitSymbol()}";

    private void RemoveTradeKeys(long target)
    {
        _seenPublicTrades.RemoveWhere(key => key.SubscriptionId == target);
        if (_publicTradeDeliveryOrder.Count == 0)
            return;
        var retained = _publicTradeDeliveryOrder.Where(
            _seenPublicTrades.Contains).ToArray();
        _publicTradeDeliveryOrder.Clear();
        foreach (var key in retained)
            _publicTradeDeliveryOrder.Enqueue(key);
    }

    private static void RemoveFingerprintPrefix<TValue>(
        IDictionary<string, TValue> values, long target)
    {
        var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (var key in values.Keys.Where(key =>
            key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            values.Remove(key);
    }

    private async ValueTask CompleteMarketSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
