namespace StockSharp.PancakeSwap;

public partial class PancakeSwapMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        var securityTypes = lookupMsg.GetSecurityTypes();
        var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
        PancakeSwapMarket[] markets;
        using (_sync.EnterScope())
            markets = [.. _markets.Values];
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in markets.OrderByDescending(
            static item => item.TotalValueLockedUsd).ThenBy(
            static item => item.SecurityCode,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.PancakeSwap))
                continue;
            if (!requestedCode.IsEmpty() &&
                !requestedCode.EqualsIgnoreCase(market.SecurityCode))
                continue;
            var security = CreateSecurity(market,
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
            using (_sync.EnterScope())
                _level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "PancakeSwap quote probes do not expose historical Level1 " +
                "events.");
        var market = GetMarket(mdMsg.SecurityId);
        await SendLevel1Async(market, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _level1Subscriptions[mdMsg.TransactionId] = new()
            {
                Market = market,
            };
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
        var market = GetMarket(mdMsg.SecurityId);
        _ = GetGraphClient(market.PoolVersion);
        var now = DateTime.UtcNow;
        var from = (mdMsg.From ?? now - TimeSpan.FromHours(1))
            .ToUniversalTime();
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetSubscriptionMaximum(mdMsg.Count);
        var historyMaximum = maximum.Min(1000);
        var trades = await LoadTradesAsync(market, from, to, historyMaximum,
            cancellationToken);
        var delivered = 0;
        foreach (var trade in trades)
            if (await SendTradeAsync(market, trade, mdMsg.TransactionId,
                cancellationToken))
                delivered++;
        if (mdMsg.IsHistoryOnly() || mdMsg.To is DateTime requestedTo &&
            requestedTo.ToUniversalTime() <= now || delivered >= maximum)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _tickSubscriptions[mdMsg.TransactionId] = new()
            {
                Market = market,
                From = mdMsg.From?.ToUniversalTime(),
                To = mdMsg.To?.ToUniversalTime(),
                LastTime = trades.Length > 0 ? trades[^1].Time : from,
                Maximum = maximum,
                Delivered = delivered,
            };
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
        var market = GetMarket(mdMsg.SecurityId);
        _ = GetGraphClient(market.PoolVersion);
        var timeFrame = mdMsg.GetTimeFrame();
        if (!AllTimeFrames.Contains(timeFrame))
            throw new NotSupportedException(
                $"PancakeSwap does not support the {timeFrame} candle interval.");
        var now = DateTime.UtcNow;
        var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
        var maximum = GetSubscriptionMaximum(mdMsg.Count);
        var historyMaximum = GetCandleHistoryCount(mdMsg, timeFrame, to,
            maximum);
        var from = mdMsg.From?.ToUniversalTime() ??
            to - TimeSpan.FromTicks(timeFrame.Ticks * historyMaximum);
        var candles = await LoadCandlesAsync(market, timeFrame, from, to,
            historyMaximum, cancellationToken);
        foreach (var candle in candles)
            await SendCandleAsync(market, candle, timeFrame,
                mdMsg.TransactionId, cancellationToken);
        if (mdMsg.IsHistoryOnly() || mdMsg.To is DateTime requestedTo &&
            requestedTo.ToUniversalTime() <= now ||
            candles.Length >= maximum)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _candleSubscriptions[mdMsg.TransactionId] = new()
            {
                Market = market,
                TimeFrame = timeFrame,
                To = mdMsg.To?.ToUniversalTime(),
                LastTime = candles.Length > 0
                    ? candles[^1].OpenTime
                    : from,
                Maximum = maximum,
                Delivered = candles.Length,
            };
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private SecurityMessage CreateSecurity(PancakeSwapMarket market,
        long originalTransactionId)
        => new SecurityMessage
        {
            SecurityId = market.ToStockSharp(),
            Name = $"{market.BaseToken.Symbol}/{market.QuoteToken.Symbol}",
            ShortName = market.SecurityCode,
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteToken.Symbol.ToCurrency(),
            PriceStep = DecimalStep(market.QuoteToken.Decimals),
            VolumeStep = DecimalStep(market.BaseToken.Decimals),
            OriginalTransactionId = originalTransactionId,
        }.TryFillUnderlyingId(market.BaseToken.Symbol);

    private async ValueTask SendLevel1Async(PancakeSwapMarket market,
        long target, CancellationToken cancellationToken)
    {
        var snapshot = await LoadLevel1Async(market, cancellationToken);
        await SendLevel1Async(market, target, snapshot.Bid, snapshot.Ask,
            cancellationToken);
    }

    private async ValueTask<(decimal Bid, decimal Ask)> LoadLevel1Async(
        PancakeSwapMarket market, CancellationToken cancellationToken)
    {
        var units = ProbeVolume.ToBaseUnits(market.BaseToken.Decimals);
        if (units <= 0)
            throw new InvalidOperationException(
                "The configured quote probe volume rounds to zero base " +
                "units.");
        var bidQuote = await RpcClient.GetQuoteAsync(market,
            PancakeSwapTradeTypes.ExactInput, units, cancellationToken);
        var askQuote = await RpcClient.GetQuoteAsync(market,
            PancakeSwapTradeTypes.ExactOutput, units, cancellationToken);
        var bidOutput = bidQuote.OutputAmount
            .FromBaseUnits(market.QuoteToken.Decimals);
        var askInput = askQuote.InputAmount
            .FromBaseUnits(market.QuoteToken.Decimals);
        var bid = bidOutput / ProbeVolume;
        var ask = askInput / ProbeVolume;
        if (bid <= 0 || ask <= 0)
            throw new InvalidDataException(
                "PancakeSwap returned a non-positive quote price.");
        return (bid, ask);
    }

    private ValueTask SendLevel1Async(PancakeSwapMarket market, long target,
        decimal bid, decimal ask, CancellationToken cancellationToken)
        => SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = market.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = target,
        }
        .TryAdd(Level1Fields.BestBidPrice, bid)
        .TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
        .TryAdd(Level1Fields.BestAskPrice, ask)
        .TryAdd(Level1Fields.BestAskVolume, ProbeVolume),
            cancellationToken);

    private async ValueTask<PancakeSwapTrade[]> LoadTradesAsync(
        PancakeSwapMarket market, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var swaps = await GetGraphClient(market.PoolVersion)
            .GetSwapsAsync(market.PoolId, from,
                maximum.Min(1000).Max(1), cancellationToken);
        return [.. (swaps ?? []).Select(swap => ToTrade(market, swap))
            .Where(static trade => trade is not null)
            .Where(trade => trade.Time >= from.ToUniversalTime() &&
                trade.Time <= to.ToUniversalTime())
            .GroupBy(static trade => trade.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static trade => trade.Time)
            .TakeLast(maximum)];
    }

    private static PancakeSwapTrade ToTrade(PancakeSwapMarket market,
        PancakeSwapSwap swap)
    {
        if (swap?.Id.IsEmpty() != false || swap.Timestamp.IsEmpty())
            return null;
        decimal? amount0;
        decimal? amount1;
        if (market.PoolVersion == PancakeSwapPoolVersions.V3)
        {
            amount0 = swap.Amount0.ToDecimalInvariant();
            amount1 = swap.Amount1.ToDecimalInvariant();
        }
        else
        {
            var amount0In = swap.Amount0In.ToDecimalInvariant();
            var amount1In = swap.Amount1In.ToDecimalInvariant();
            var amount0Out = swap.Amount0Out.ToDecimalInvariant();
            var amount1Out = swap.Amount1Out.ToDecimalInvariant();
            if (amount0In is null || amount1In is null ||
                amount0Out is null || amount1Out is null)
                return null;
            amount0 = amount0In.Value - amount0Out.Value;
            amount1 = amount1In.Value - amount1Out.Value;
        }
        if (amount0 is null || amount1 is null)
            return null;
        var isBaseToken0 = market.BaseToken.Address.EqualsIgnoreCase(
            market.Token0.Address);
        var signedBase = isBaseToken0 ? amount0.Value : amount1.Value;
        var signedQuote = isBaseToken0 ? amount1.Value : amount0.Value;
        var volume = signedBase.Abs();
        var quote = signedQuote.Abs();
        if (volume <= 0 || quote <= 0)
            return null;
        DateTime time;
        try
        {
            time = swap.Timestamp.ToUtcTime();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return new()
        {
            Id = swap.Id,
            Time = time,
            Price = quote / volume,
            Volume = volume,
            Side = signedBase > 0 ? Sides.Sell : Sides.Buy,
            TransactionHash = swap.Transaction?.Id,
        };
    }

    private async ValueTask<bool> SendTradeAsync(PancakeSwapMarket market,
        PancakeSwapTrade trade, long target,
        CancellationToken cancellationToken)
    {
        var key = new DeliveryKey(target, trade.Id);
        using (_sync.EnterScope())
        {
            if (!_seenTrades.Add(key))
                return false;
            _tradeDeliveryOrder.Enqueue(key);
            while (_tradeDeliveryOrder.Count > _maximumDeliveryKeys)
                _seenTrades.Remove(_tradeDeliveryOrder.Dequeue());
        }
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = market.ToStockSharp(),
            ServerTime = trade.Time,
            OriginalTransactionId = target,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Volume,
            OriginSide = trade.Side,
        }, cancellationToken);
        return true;
    }

    private async ValueTask<PancakeSwapCandle[]> LoadCandlesAsync(
        PancakeSwapMarket market, TimeSpan timeFrame, DateTime from, DateTime to,
        int maximum, CancellationToken cancellationToken)
    {
        var trades = await LoadTradesAsync(market, from, to, 1000,
            cancellationToken);
        return [.. trades.GroupBy(trade => FloorTime(trade.Time, timeFrame))
            .OrderBy(static group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(static trade => trade.Time)
                    .ToArray();
                return new PancakeSwapCandle
                {
                    OpenTime = group.Key,
                    Open = ordered[0].Price,
                    High = ordered.Max(static trade => trade.Price),
                    Low = ordered.Min(static trade => trade.Price),
                    Close = ordered[^1].Price,
                    Volume = ordered.Sum(static trade => trade.Volume),
                    Turnover = ordered.Sum(static trade =>
                        trade.Price * trade.Volume),
                    TradeCount = ordered.Length,
                };
            }).TakeLast(maximum)];
    }

    private ValueTask SendCandleAsync(PancakeSwapMarket market,
        PancakeSwapCandle candle, TimeSpan timeFrame, long target,
        CancellationToken cancellationToken)
    {
        var fingerprint = new CandleFingerprint(candle.Open, candle.High,
            candle.Low, candle.Close, candle.Volume, candle.TradeCount);
        var key = $"{target}:{candle.OpenTime.Ticks}";
        using (_sync.EnterScope())
            _candleFingerprints[key] = fingerprint;
        var closeTime = candle.OpenTime + timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = market.ToStockSharp(),
            OpenTime = candle.OpenTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open,
            HighPrice = candle.High,
            LowPrice = candle.Low,
            ClosePrice = candle.Close,
            TotalVolume = candle.Volume,
            TotalPrice = candle.Turnover,
            TotalTicks = candle.TradeCount,
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
        await PollLevel1Async(cancellationToken);
        await PollTicksAsync(cancellationToken);
        await PollCandlesAsync(cancellationToken);
    }

    private async ValueTask PollLevel1Async(
        CancellationToken cancellationToken)
    {
        (PancakeSwapMarket Market, long[] Targets)[] groups;
        using (_sync.EnterScope())
            groups = [.. _level1Subscriptions.GroupBy(static pair =>
                    pair.Value.Market.SecurityCode,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => (group.First().Value.Market,
                    group.Select(static pair => pair.Key).ToArray()))];
        foreach (var group in groups)
        {
            try
            {
                var snapshot = await LoadLevel1Async(group.Market,
                    cancellationToken);
                foreach (var target in group.Targets)
                    await SendLevel1Async(group.Market, target,
                        snapshot.Bid, snapshot.Ask,
                        cancellationToken);
            }
            catch (Exception error) when (
                !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }
    }

    private async ValueTask PollTicksAsync(
        CancellationToken cancellationToken)
    {
        (long Id, TickSubscription Subscription)[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _tickSubscriptions.Select(static pair =>
                (pair.Key, pair.Value))];
        var finished = new List<long>();
        foreach (var item in subscriptions)
        {
            var from = item.Subscription.LastTime - TimeSpan.FromSeconds(1);
            var to = (item.Subscription.To ?? DateTime.UtcNow)
                .ToUniversalTime().Min(DateTime.UtcNow);
            var trades = await LoadTradesAsync(item.Subscription.Market,
                from, to, 1000, cancellationToken);
            foreach (var trade in trades)
            {
                if (item.Subscription.From is DateTime requestedFrom &&
                    trade.Time < requestedFrom ||
                    item.Subscription.To is DateTime requestedTo &&
                    trade.Time > requestedTo)
                    continue;
                if (await SendTradeAsync(item.Subscription.Market, trade,
                    item.Id, cancellationToken))
                    item.Subscription.Delivered++;
                if (trade.Time > item.Subscription.LastTime)
                    item.Subscription.LastTime = trade.Time;
                if (item.Subscription.Delivered >=
                    item.Subscription.Maximum)
                    break;
            }
            if (item.Subscription.Delivered >= item.Subscription.Maximum ||
                item.Subscription.To is DateTime end && CurrentTime >= end)
                finished.Add(item.Id);
        }
        foreach (var target in finished)
        {
            UnsubscribeTicks(target);
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
            var now = DateTime.UtcNow;
            var to = (item.Subscription.To ?? now).ToUniversalTime().Min(now);
            var from = item.Subscription.LastTime -
                item.Subscription.TimeFrame;
            var maximum = (item.Subscription.Maximum -
                item.Subscription.Delivered).Min(1000).Max(1);
            var candles = await LoadCandlesAsync(item.Subscription.Market,
                item.Subscription.TimeFrame, from, to, maximum,
                cancellationToken);
            foreach (var candle in candles)
            {
                var key = $"{item.Id}:{candle.OpenTime.Ticks}";
                var fingerprint = new CandleFingerprint(candle.Open,
                    candle.High, candle.Low, candle.Close, candle.Volume,
                    candle.TradeCount);
                var isChanged = false;
                var isNew = false;
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
                await SendCandleAsync(item.Subscription.Market, candle,
                    item.Subscription.TimeFrame, item.Id,
                    cancellationToken);
                if (isNew)
                    item.Subscription.Delivered++;
                item.Subscription.LastTime = candle.OpenTime;
                if (item.Subscription.Delivered >=
                    item.Subscription.Maximum)
                    break;
            }
            if (item.Subscription.Delivered >= item.Subscription.Maximum ||
                item.Subscription.To is DateTime end && CurrentTime >= end)
                finished.Add(item.Id);
        }
        foreach (var target in finished)
        {
            UnsubscribeCandles(target);
            await SendSubscriptionFinishedAsync(target, cancellationToken);
        }
    }

    private void UnsubscribeTicks(long target)
    {
        using (_sync.EnterScope())
        {
            _tickSubscriptions.Remove(target);
            _seenTrades.RemoveWhere(key => key.SubscriptionId == target);
            var retained = _tradeDeliveryOrder.Where(_seenTrades.Contains)
                .ToArray();
            _tradeDeliveryOrder.Clear();
            foreach (var key in retained)
                _tradeDeliveryOrder.Enqueue(key);
        }
    }

    private void UnsubscribeCandles(long target)
    {
        using (_sync.EnterScope())
        {
            _candleSubscriptions.Remove(target);
            RemoveFingerprintPrefix(_candleFingerprints, target);
        }
    }

    private static DateTime FloorTime(DateTime value, TimeSpan interval)
        => value.ToUniversalTime().Truncate(interval);

    private static decimal? DecimalStep(int decimals)
    {
        if (decimals is < 0 or > 28)
            return null;
        var result = 1m;
        for (var index = 0; index < decimals; index++)
            result /= 10m;
        return result;
    }

    private static int GetSubscriptionMaximum(long? count)
        => count is null
            ? int.MaxValue
            : count.Value.Min(1000).Max(1).To<int>();

    private static int GetCandleHistoryCount(MarketDataMessage message,
        TimeSpan timeFrame, DateTime to, int maximum)
    {
        if (message.Count is not null)
            return maximum;
        if (message.From is DateTime from && to > from)
            return ((to - from.ToUniversalTime()).Ticks /
                timeFrame.Ticks + 1).Min(1000L).Max(1L).To<int>();
        return 300;
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
