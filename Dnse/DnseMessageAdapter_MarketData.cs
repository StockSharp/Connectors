namespace StockSharp.Dnse;

public partial class DnseMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var securityTypes = lookupMsg.GetSecurityTypes();
        var requested = lookupMsg.SecurityId.SecurityCode?
            .Split(
                [',', ';', ' '],
                StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
            .Select(value => value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var symbolQuery = requested.Length == 0
            ? null
            : requested.Join(",");
        var marketId = lookupMsg.SecurityId.BoardCode.IsEmpty()
            ? null
            : lookupMsg.SecurityId.BoardCode.ToDnseMarketId();
        var securityGroupId =
            securityTypes.ToSecurityGroupId();
        var remaining = Math.Min(
            lookupMsg.Count ?? LookupLimit,
            LookupLimit);
        const int pageSize = 100;

        for (var page = 1; remaining > 0; page++)
        {
            var response = await _rest.GetInstruments(
                symbolQuery,
                marketId,
                securityGroupId,
                pageSize,
                page,
                cancellationToken);
            var instruments = response?.Data ?? [];
            foreach (var instrument in instruments)
            {
                if (instrument?.Symbol.IsEmpty() != false)
                    continue;
                var native = instrument.ToNative(DefaultBoardId);
                CacheSecurity(native);
                var security = instrument.ToSecurityMessage(
                    lookupMsg.TransactionId,
                    DefaultBoardId);
                if (!security.IsMatch(lookupMsg, securityTypes))
                    continue;
                await SendOutMessageAsync(
                    security, cancellationToken);
                if (--remaining <= 0)
                    break;
            }

            if (instruments.Length < pageSize ||
                response?.Total is > 0 &&
                    page * pageSize >= response.Total)
            {
                break;
            }
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            DataType.Level1,
            null,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            DataType.MarketDepth,
            null,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            DataType.Ticks,
            null,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            mdMsg.GetTimeFrame().TimeFrame(),
            mdMsg.GetTimeFrame(),
            cancellationToken);

    private async ValueTask ProcessMarketSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        TimeSpan? timeFrame,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
        {
            if (_marketSubscriptions.TryGetValue(
                mdMsg.OriginalTransactionId,
                out var old))
            {
                _marketSubscriptions.Remove(
                    mdMsg.OriginalTransactionId);
                foreach (var channel in old.Channels ?? [])
                {
                    await RemoveNativeReference(
                        channel,
                        old.Native.Symbol,
                        cancellationToken);
                }
            }
            return;
        }

        if (timeFrame is not null &&
            !AllTimeFrames.Contains(timeFrame.Value))
        {
            throw new NotSupportedException(
                $"DNSE does not support {timeFrame} candles.");
        }

        var native = mdMsg.SecurityId.ToDnseNative(DefaultBoardId);
        CacheSecurity(native);
        await SendMarketSnapshot(
            mdMsg,
            dataType,
            timeFrame,
            native,
            cancellationToken);

        if (!mdMsg.IsHistoryOnly())
        {
            var channels = GetChannels(
                dataType, timeFrame, native.BoardId);
            var added = new List<string>();
            try
            {
                foreach (var channel in channels)
                {
                    await AddNativeReference(
                        channel,
                        native.Symbol,
                        cancellationToken);
                    added.Add(channel);
                }
            }
            catch
            {
                foreach (var channel in added)
                {
                    await RemoveNativeReference(
                        channel,
                        native.Symbol,
                        CancellationToken.None);
                }
                throw;
            }

            _marketSubscriptions[mdMsg.TransactionId] =
                new()
                {
                    TransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    Native = native,
                    DataType = dataType,
                    TimeFrame = timeFrame,
                    MaxDepth = mdMsg.MaxDepth,
                    Channels = channels,
                };
        }

        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
        }
    }

    private async ValueTask SendMarketSnapshot(
        MarketDataMessage mdMsg,
        DataType dataType,
        TimeSpan? timeFrame,
        DnseInstrumentKey native,
        CancellationToken cancellationToken)
    {
        if (dataType == DataType.Level1)
        {
            var definition = await _rest.GetSecurityDefinition(
                native.Symbol,
                native.BoardId,
                cancellationToken);
            if (definition is not null)
            {
                await SendSecurityDefinitionLevel1(
                    definition,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }

            var trade = (await _rest.GetTrades(
                native.Symbol,
                native.BoardId,
                null,
                null,
                1,
                true,
                cancellationToken)).LastOrDefault();
            if (trade is not null)
            {
                await SendTradeLevel1(
                    trade,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }

            var quote = (await _rest.GetQuotes(
                native.Symbol,
                native.BoardId,
                null,
                null,
                1,
                true,
                cancellationToken)).LastOrDefault();
            if (quote is not null)
            {
                await SendQuoteLevel1(
                    quote,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }
        }
        else if (dataType == DataType.MarketDepth)
        {
            var quote = (await _rest.GetQuotes(
                native.Symbol,
                native.BoardId,
                null,
                null,
                1,
                true,
                cancellationToken)).LastOrDefault();
            if (quote is not null)
            {
                await SendDepth(
                    quote,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    mdMsg.MaxDepth,
                    cancellationToken);
            }
        }
        else if (dataType == DataType.Ticks)
        {
            var to = mdMsg.To ?? DateTime.UtcNow;
            var from = mdMsg.From ?? to.AddDays(-1);
            var limit = mdMsg.Count is > 0
                ? (int)Math.Min(mdMsg.Count.Value, 1000)
                : 1000;
            var trades = await _rest.GetTrades(
                native.Symbol,
                native.BoardId,
                from,
                to,
                limit,
                false,
                cancellationToken);
            foreach (var trade in trades
                .OrderBy(item => item.Time.ToDnseTime()))
            {
                await SendPublicTrade(
                    trade,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }
        }
        else if (timeFrame is not null)
        {
            var candles = await LoadCandles(
                mdMsg, native, timeFrame.Value, cancellationToken);
            for (var index = 0; index < candles.Length; index++)
            {
                var candle = candles[index];
                var isActive =
                    !mdMsg.IsHistoryOnly() &&
                    index == candles.Length - 1 &&
                    candle.time + timeFrame > DateTime.UtcNow;
                await SendCandle(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    timeFrame.Value,
                    candle.time,
                    candle.open,
                    candle.high,
                    candle.low,
                    candle.close,
                    candle.volume,
                    isActive
                        ? CandleStates.Active
                        : CandleStates.Finished,
                    cancellationToken);
            }
        }
    }

    private async Task<(DateTime time, decimal open, decimal high,
        decimal low, decimal close, decimal volume)[]> LoadCandles(
        MarketDataMessage mdMsg,
        DnseInstrumentKey native,
        TimeSpan timeFrame,
        CancellationToken cancellationToken)
    {
        var to = NormalizeUtc(mdMsg.To ?? DateTime.UtcNow);
        var requested = mdMsg.Count is > 0
            ? Math.Min(mdMsg.Count.Value, 100000)
            : mdMsg.From is null
                ? mdMsg.IsHistoryOnly() ? 1000 : 2
                : long.MaxValue;
        var from = mdMsg.From is DateTime explicitFrom
            ? NormalizeUtc(explicitFrom)
            : SafeSubtract(
                to,
                timeFrame,
                requested == long.MaxValue ? 1000 : requested);

        var values = new SortedDictionary<DateTime,
            (decimal open, decimal high, decimal low,
                decimal close, decimal volume)>();
        var cursor = from;
        var visited = new HashSet<long>();
        for (var page = 0; page < 100; page++)
        {
            var response = await _rest.GetCandles(
                native.Symbol,
                timeFrame.ToResolution(),
                cursor,
                to,
                cancellationToken);
            var count = new[]
            {
                response?.Times?.Length ?? 0,
                response?.Opens?.Length ?? 0,
                response?.Highs?.Length ?? 0,
                response?.Lows?.Length ?? 0,
                response?.Closes?.Length ?? 0,
                response?.Volumes?.Length ?? 0,
            }.Min();
            for (var index = 0; index < count; index++)
            {
                DateTime time;
                try
                {
                    time = DateTimeOffset
                        .FromUnixTimeSeconds(response.Times[index])
                        .UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
                if (time < from || time > to)
                    continue;
                values[time] = (
                    response.Opens[index].ScalePrice(
                        MarketDataPriceMultiplier),
                    response.Highs[index].ScalePrice(
                        MarketDataPriceMultiplier),
                    response.Lows[index].ScalePrice(
                        MarketDataPriceMultiplier),
                    response.Closes[index].ScalePrice(
                        MarketDataPriceMultiplier),
                    response.Volumes[index]);
            }

            if (response?.NextTime is not > 0 ||
                !visited.Add(response.NextTime) ||
                response.NextTime <=
                    new DateTimeOffset(cursor).ToUnixTimeSeconds())
            {
                break;
            }
            cursor = DateTimeOffset
                .FromUnixTimeSeconds(response.NextTime)
                .UtcDateTime;
            if (cursor >= to || values.Count >= requested)
                break;
        }

        IEnumerable<KeyValuePair<DateTime,
            (decimal open, decimal high, decimal low,
                decimal close, decimal volume)>> result = values;
        if (requested != long.MaxValue)
            result = result.TakeLast((int)requested);
        return
        [
            .. result.Select(pair => (
                pair.Key,
                pair.Value.open,
                pair.Value.high,
                pair.Value.low,
                pair.Value.close,
                pair.Value.volume)),
        ];
    }

    private async ValueTask ProcessSecurityDefinition(
        DnseSecurityDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition?.Symbol.IsEmpty() != false)
            return;
        var native = definition.ToNative();
        CacheSecurity(native);
        foreach (var subscription in FindSubscriptions(
            definition.Symbol,
            definition.BoardId,
            DataType.Level1))
        {
            await SendSecurityDefinitionLevel1(
                definition,
                subscription.TransactionId,
                subscription.SecurityId,
                cancellationToken);
        }
    }

    private async ValueTask ProcessPublicTrade(
        DnseTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade?.Symbol.IsEmpty() != false)
            return;
        foreach (var subscription in FindSubscriptions(
            trade.Symbol, trade.BoardId))
        {
            if (subscription.DataType == DataType.Level1)
            {
                await SendTradeLevel1(
                    trade,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
            else if (subscription.DataType == DataType.Ticks)
            {
                await SendPublicTrade(
                    trade,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
        }
    }

    private async ValueTask ProcessQuote(
        DnseQuote quote,
        CancellationToken cancellationToken)
    {
        if (quote?.Symbol.IsEmpty() != false)
            return;
        foreach (var subscription in FindSubscriptions(
            quote.Symbol, quote.BoardId))
        {
            if (subscription.DataType == DataType.Level1)
            {
                await SendQuoteLevel1(
                    quote,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
            else if (subscription.DataType == DataType.MarketDepth)
            {
                await SendDepth(
                    quote,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    subscription.MaxDepth,
                    cancellationToken);
            }
        }
    }

    private async ValueTask ProcessLiveCandle(
        DnseCandle candle,
        bool isFinished,
        CancellationToken cancellationToken)
    {
        if (candle?.Symbol.IsEmpty() != false)
            return;
        var timeFrame = candle.Resolution.ToTimeFrame();
        if (timeFrame == default)
            return;
        foreach (var subscription in _marketSubscriptions.CachedValues
            .Where(item =>
                item.TimeFrame == timeFrame &&
                item.Native.Symbol.EqualsIgnoreCase(candle.Symbol)))
        {
            await SendCandle(
                subscription.TransactionId,
                subscription.SecurityId,
                timeFrame,
                candle.Time.ToDnseTime(),
                candle.Open.ScalePrice(MarketDataPriceMultiplier),
                candle.High.ScalePrice(MarketDataPriceMultiplier),
                candle.Low.ScalePrice(MarketDataPriceMultiplier),
                candle.Close.ScalePrice(MarketDataPriceMultiplier),
                candle.Volume,
                isFinished ||
                    candle.Type.EqualsIgnoreCase("closed")
                        ? CandleStates.Finished
                        : CandleStates.Active,
                cancellationToken);
        }
    }

    private ValueTask SendSecurityDefinitionLevel1(
        DnseSecurityDefinition definition,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(
            new Level1ChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime = definition.Time.ToDnseTime(),
            }
            .TryAdd(
                Level1Fields.SettlementPrice,
                definition.BasicPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.MaxPrice,
                definition.CeilingPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.MinPrice,
                definition.FloorPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.PriceStep,
                definition.GetPriceStep(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.State,
                definition.SecurityStatus.ToSecurityState()),
            cancellationToken);

    private ValueTask SendTradeLevel1(
        DnseTrade trade,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var time = trade.Time.ToDnseTime();
        return SendOutMessageAsync(
            new Level1ChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime = time,
            }
            .TryAdd(
                Level1Fields.LastTradePrice,
                trade.MatchPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.LastTradeVolume,
                trade.MatchQuantity)
            .TryAdd(Level1Fields.LastTradeTime, time)
            .TryAdd(
                Level1Fields.LastTradeOrigin,
                trade.Side.ToSide())
            .TryAdd(
                Level1Fields.AveragePrice,
                trade.AveragePrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.OpenPrice,
                trade.OpenPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.HighPrice,
                trade.HighPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.LowPrice,
                trade.LowPrice.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.Volume,
                trade.TotalVolume),
            cancellationToken);
    }

    private ValueTask SendQuoteLevel1(
        DnseQuote quote,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var bid = quote.Bids?
            .Where(level => level.Price > 0)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault();
        var ask = quote.Offers?
            .Where(level => level.Price > 0)
            .OrderBy(level => level.Price)
            .FirstOrDefault();
        return SendOutMessageAsync(
            new Level1ChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime = quote.Time.ToDnseTime(),
            }
            .TryAdd(
                Level1Fields.BestBidPrice,
                bid?.Price.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.BestBidVolume,
                bid?.Quantity)
            .TryAdd(
                Level1Fields.BestAskPrice,
                ask?.Price.ScalePrice(
                    MarketDataPriceMultiplier))
            .TryAdd(
                Level1Fields.BestAskVolume,
                ask?.Quantity)
            .TryAdd(
                Level1Fields.BidsVolume,
                quote.TotalBidQuantity)
            .TryAdd(
                Level1Fields.AsksVolume,
                quote.TotalOfferQuantity),
            cancellationToken);
    }

    private ValueTask SendDepth(
        DnseQuote quote,
        long transactionId,
        SecurityId securityId,
        int? maxDepth,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(
            new QuoteChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime = quote.Time.ToDnseTime(),
                Bids =
                [
                    .. (quote.Bids ?? [])
                        .Where(level =>
                            level.Price > 0 &&
                            level.Quantity >= 0)
                        .OrderByDescending(level => level.Price)
                        .Take(maxDepth ?? 3)
                        .Select(level => new QuoteChange(
                            level.Price.ScalePrice(
                                MarketDataPriceMultiplier),
                            level.Quantity)),
                ],
                Asks =
                [
                    .. (quote.Offers ?? [])
                        .Where(level =>
                            level.Price > 0 &&
                            level.Quantity >= 0)
                        .OrderBy(level => level.Price)
                        .Take(maxDepth ?? 3)
                        .Select(level => new QuoteChange(
                            level.Price.ScalePrice(
                                MarketDataPriceMultiplier),
                            level.Quantity)),
                ],
            },
            cancellationToken);

    private ValueTask SendPublicTrade(
        DnseTrade trade,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (trade.MatchPrice <= 0 || trade.MatchQuantity <= 0)
            return default;
        var time = trade.Time.ToDnseTime();
        var tradeId =
            $"{trade.Symbol}:{trade.BoardId}:{time:O}:" +
            $"{trade.MatchPrice}:{trade.MatchQuantity}:{trade.Side}";
        var seen = $"{transactionId}:{tradeId}";
        if (!_seenTrades.TryAdd(seen))
            return default;

        return SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Ticks,
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                TradeStringId = tradeId,
                TradePrice = trade.MatchPrice.ScalePrice(
                    MarketDataPriceMultiplier),
                TradeVolume = trade.MatchQuantity,
                OriginSide = trade.Side.ToSide(),
                ServerTime = time,
            },
            cancellationToken);
    }

    private ValueTask SendCandle(
        long transactionId,
        SecurityId securityId,
        TimeSpan timeFrame,
        DateTime openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        CandleStates state,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(
            new TimeFrameCandleMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                TypedArg = timeFrame,
                OpenTime = openTime,
                CloseTime = openTime + timeFrame,
                OpenPrice = open,
                HighPrice = high,
                LowPrice = low,
                ClosePrice = close,
                TotalVolume = volume,
                State = state,
            },
            cancellationToken);

    private MarketSubscription[] FindSubscriptions(
        string symbol,
        string boardId,
        DataType dataType = null)
        => _marketSubscriptions.CachedValues
            .Where(item =>
                item.Native.Symbol.EqualsIgnoreCase(symbol) &&
                (boardId.IsEmpty() ||
                    item.Native.BoardId.EqualsIgnoreCase(boardId)) &&
                (dataType is null || item.DataType == dataType))
            .ToArray();

    private static string[] GetChannels(
        DataType dataType,
        TimeSpan? timeFrame,
        string boardId)
    {
        if (dataType == DataType.Level1)
        {
            return
            [
                $"security_definition.{boardId}.json",
                $"tick.{boardId}.json",
                $"top_price.{boardId}.json",
            ];
        }
        if (dataType == DataType.MarketDepth)
            return [$"top_price.{boardId}.json"];
        if (dataType == DataType.Ticks)
            return [$"tick.{boardId}.json"];
        if (timeFrame is not null)
        {
            var resolution = timeFrame.Value.ToResolution();
            return
            [
                $"ohlc.{resolution}.json",
                $"ohlc_closed.{resolution}.json",
            ];
        }
        throw new NotSupportedException(
            $"Unsupported DNSE market-data type {dataType}.");
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static DateTime SafeSubtract(
        DateTime value,
        TimeSpan timeFrame,
        long count)
    {
        var safeCount = Math.Clamp(count, 1, 100000);
        try
        {
            return value -
                TimeSpan.FromTicks(
                    checked(timeFrame.Ticks * safeCount));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UnixEpoch;
        }
        catch (OverflowException)
        {
            return DateTime.UnixEpoch;
        }
    }
}
