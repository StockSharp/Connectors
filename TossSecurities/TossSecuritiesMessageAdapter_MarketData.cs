namespace StockSharp.TossSecurities;

public partial class TossSecuritiesMessageAdapter
{
    private static readonly (string code, string name,
        SecurityTypes type)[] _marketIndicators =
    [
        ("KOSPI", "Korea Composite Stock Price Index",
            SecurityTypes.Index),
        ("KOSDAQ", "Korean Securities Dealers Automated Quotations",
            SecurityTypes.Index),
        ("KR_BOND_2Y", "Korea Treasury yield 2Y",
            SecurityTypes.Bond),
        ("KR_BOND_3Y", "Korea Treasury yield 3Y",
            SecurityTypes.Bond),
        ("KR_BOND_5Y", "Korea Treasury yield 5Y",
            SecurityTypes.Bond),
        ("KR_BOND_10Y", "Korea Treasury yield 10Y",
            SecurityTypes.Bond),
        ("KR_BOND_20Y", "Korea Treasury yield 20Y",
            SecurityTypes.Bond),
        ("KR_BOND_30Y", "Korea Treasury yield 30Y",
            SecurityTypes.Bond),
    ];

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var securityTypes = lookupMsg.GetSecurityTypes();
        var left = lookupMsg.Count ?? long.MaxValue;
        var query = lookupMsg.SecurityId.SecurityCode;
        var requested = query.IsEmpty()
            ? []
            : query.Split(
                [',', ';', ' '],
                StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        var indicators = query.IsEmpty()
            ? _marketIndicators
            : [.. _marketIndicators.Where(indicator =>
                requested.Contains(
                    indicator.code,
                    StringComparer.OrdinalIgnoreCase))];
        foreach (var (code, name, type) in indicators)
        {
            var message = CreateIndicatorSecurity(
                code,
                name,
                type,
                lookupMsg.TransactionId);
            if (!message.IsMatch(lookupMsg, securityTypes))
                continue;
            await SendOutMessageAsync(message, cancellationToken);
            if (--left <= 0)
                break;
        }

        if (left > 0 && requested.Length > 0)
        {
            var stockSymbols = requested.Where(
                symbol => !IsMarketIndicator(symbol));
            foreach (var chunk in stockSymbols.Chunk(200))
            {
                foreach (var stock in await _restClient.GetStocks(
                    chunk, cancellationToken))
                {
                    var message = CreateSecurityMessage(
                        stock, lookupMsg.TransactionId);
                    if (!message.IsMatch(lookupMsg, securityTypes))
                        continue;
                    await SendOutMessageAsync(
                        message, cancellationToken);
                    if (--left <= 0)
                        break;
                }
                if (left <= 0)
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
        => ProcessSnapshotSubscription(
            mdMsg, DataType.Level1, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (IsMarketIndicator(mdMsg.SecurityId.SecurityCode))
        {
            throw new NotSupportedException(
                "Toss Securities does not provide order books for market indicators.");
        }
        return ProcessSnapshotSubscription(
            mdMsg, DataType.MarketDepth, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (IsMarketIndicator(mdMsg.SecurityId.SecurityCode))
        {
            throw new NotSupportedException(
                "Toss Securities does not provide trades for market indicators.");
        }
        return ProcessSnapshotSubscription(
            mdMsg, DataType.Ticks, cancellationToken);
    }

    private async ValueTask ProcessSnapshotSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            _marketSubscriptions.Remove(
                mdMsg.OriginalTransactionId);
            return;
        }

        await SendSnapshot(mdMsg, dataType, cancellationToken);
        if (!mdMsg.IsHistoryOnly())
        {
            _marketSubscriptions[mdMsg.TransactionId] =
                new MarketSubscription
                {
                    TransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    DataType = dataType,
                    MaxDepth = mdMsg.MaxDepth,
                };
        }

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
        }
    }

    private async ValueTask SendSnapshot(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        var symbol = RequireSymbol(mdMsg.SecurityId);
        if (dataType == DataType.Level1)
        {
            TossPrice price;
            TossPriceLimits limits = null;
            if (IsMarketIndicator(symbol))
            {
                price = (await _restClient.GetIndicatorPrices(
                    [symbol], cancellationToken)).FirstOrDefault();
            }
            else
            {
                price = (await _restClient.GetPrices(
                    [symbol], cancellationToken)).FirstOrDefault();
                limits = await _restClient.GetPriceLimits(
                    symbol, cancellationToken);
            }
            if (price is not null)
            {
                await SendLevel1(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    price,
                    limits,
                    cancellationToken);
            }
        }
        else if (dataType == DataType.MarketDepth)
        {
            var book = await _restClient.GetOrderBook(
                symbol, cancellationToken);
            if (book is not null)
            {
                await SendDepth(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    book,
                    mdMsg.MaxDepth,
                    cancellationToken);
            }
        }
        else if (dataType == DataType.Ticks)
        {
            var requested = mdMsg.Count is > 0 and <= 50
                ? (int)mdMsg.Count.Value
                : 50;
            IEnumerable<TossPublicTrade> trades =
                (await _restClient.GetTrades(
                    symbol, requested, cancellationToken))
                .Where(trade =>
                    (mdMsg.From is null ||
                        trade.Timestamp >= mdMsg.From) &&
                    (mdMsg.To is null ||
                        trade.Timestamp <= mdMsg.To))
                .OrderBy(trade => trade.Timestamp);
            if (mdMsg.Count is > 0 and <= int.MaxValue)
            {
                trades = trades.TakeLast(
                    (int)mdMsg.Count.Value);
            }
            foreach (var trade in trades)
            {
                await SendTrade(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    trade,
                    cancellationToken);
            }
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
            _marketSubscriptions.Remove(
                mdMsg.OriginalTransactionId);
            return;
        }

        var timeFrame = mdMsg.GetTimeFrame();
        ValidateTimeFrame(mdMsg.SecurityId, timeFrame);
        var candles = await LoadCandles(mdMsg, cancellationToken);
        for (var index = 0; index < candles.Length; index++)
        {
            var state = mdMsg.IsHistoryOnly() ||
                index < candles.Length - 1
                    ? CandleStates.Finished
                    : CandleStates.Active;
            await SendCandle(
                mdMsg.TransactionId,
                mdMsg.SecurityId,
                timeFrame,
                candles[index],
                state,
                cancellationToken);
            if (!mdMsg.IsHistoryOnly())
            {
                RememberCandle(
                    mdMsg.TransactionId,
                    candles[index],
                    state);
            }
        }

        if (!mdMsg.IsHistoryOnly())
        {
            _marketSubscriptions[mdMsg.TransactionId] =
                new MarketSubscription
                {
                    TransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    DataType = timeFrame.TimeFrame(),
                    TimeFrame = timeFrame,
                };
        }

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
        }
    }

    private async Task<TossCandle[]> LoadCandles(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var symbol = RequireSymbol(mdMsg.SecurityId);
        var indicator = IsMarketIndicator(symbol);
        var timeFrame = mdMsg.GetTimeFrame();
        var interval = timeFrame == TimeSpan.FromMinutes(1)
            ? "1m" : "1d";
        var requested = mdMsg.Count is > 0
            ? mdMsg.Count.Value
            : mdMsg.From is not null
                ? long.MaxValue
                : mdMsg.IsHistoryOnly() ? 100 : 2;
        var before = new DateTimeOffset(
            (mdMsg.To ?? CurrentTime).ToUniversalTime());
        var cursors = new HashSet<DateTimeOffset>();
        var candles = new Dictionary<DateTimeOffset, TossCandle>();

        while (candles.Count < requested)
        {
            var pageSize = requested == long.MaxValue
                ? 200
                : (int)Math.Min(200, requested - candles.Count);
            var page = await _restClient.GetCandles(
                symbol,
                indicator,
                interval,
                Math.Max(1, pageSize),
                before,
                AdjustedCandles,
                cancellationToken);
            var rows = page?.Candles ?? [];
            foreach (var candle in rows)
            {
                if (candle.Timestamp > before ||
                    mdMsg.To is not null &&
                    candle.Timestamp > mdMsg.To ||
                    mdMsg.From is not null &&
                    candle.Timestamp < mdMsg.From)
                    continue;
                candles[candle.Timestamp] = candle;
            }

            var oldest = rows
                .Select(candle => candle.Timestamp)
                .DefaultIfEmpty()
                .Min();
            if (rows.Length == 0 ||
                mdMsg.From is not null &&
                    oldest.UtcDateTime <=
                        mdMsg.From.Value.ToUniversalTime() ||
                page?.NextBefore is not DateTimeOffset next ||
                !cursors.Add(next))
                break;
            before = next;
        }

        IEnumerable<TossCandle> result =
            candles.Values.OrderBy(candle => candle.Timestamp);
        if (requested != long.MaxValue)
        {
            result = result.TakeLast(
                (int)Math.Min(requested, int.MaxValue));
        }
        return [.. result];
    }

    private async ValueTask PollMarketData(
        CancellationToken cancellationToken)
    {
        var subscriptions = _marketSubscriptions.CachedValues;
        var level1 = subscriptions
            .Where(item => item.DataType == DataType.Level1)
            .ToArray();
        await PollLevel1(level1, false, cancellationToken);
        await PollLevel1(
            [.. level1.Where(item =>
                IsMarketIndicator(item.SecurityId.SecurityCode))],
            true,
            cancellationToken);

        foreach (var subscription in subscriptions.Where(
            item => item.DataType == DataType.MarketDepth))
        {
            var book = await _restClient.GetOrderBook(
                RequireSymbol(subscription.SecurityId),
                cancellationToken);
            if (book is not null &&
                IsChanged(
                    $"{subscription.TransactionId}:depth",
                    DepthSignature(book)))
            {
                await SendDepth(
                    subscription.TransactionId,
                    subscription.SecurityId,
                    book,
                    subscription.MaxDepth,
                    cancellationToken);
            }
        }

        foreach (var subscription in subscriptions.Where(
            item => item.DataType == DataType.Ticks))
        {
            foreach (var trade in (await _restClient.GetTrades(
                RequireSymbol(subscription.SecurityId),
                50,
                cancellationToken)).OrderBy(
                    trade => trade.Timestamp))
            {
                await SendTrade(
                    subscription.TransactionId,
                    subscription.SecurityId,
                    trade,
                    cancellationToken);
            }
        }

        foreach (var subscription in subscriptions.Where(
            item => item.TimeFrame is not null))
        {
            await PollCandles(subscription, cancellationToken);
        }
    }

    private async ValueTask PollLevel1(
        MarketSubscription[] subscriptions,
        bool indicators,
        CancellationToken cancellationToken)
    {
        var selected = subscriptions
            .Where(item =>
                IsMarketIndicator(item.SecurityId.SecurityCode) ==
                    indicators)
            .ToArray();
        foreach (var chunk in selected
            .Select(item => item.SecurityId.SecurityCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Chunk(200))
        {
            var prices = indicators
                ? await _restClient.GetIndicatorPrices(
                    chunk, cancellationToken)
                : await _restClient.GetPrices(
                    chunk, cancellationToken);
            foreach (var price in prices)
            {
                foreach (var subscription in selected.Where(
                    item => item.SecurityId.SecurityCode
                        .EqualsIgnoreCase(price.Symbol)))
                {
                    if (!IsChanged(
                        $"{subscription.TransactionId}:level1",
                        $"{price.Timestamp:O}:{price.LastPrice}"))
                        continue;
                    await SendLevel1(
                        subscription.TransactionId,
                        subscription.SecurityId,
                        price,
                        null,
                        cancellationToken);
                }
            }
        }
    }

    private async ValueTask PollCandles(
        MarketSubscription subscription,
        CancellationToken cancellationToken)
    {
        var symbol = RequireSymbol(subscription.SecurityId);
        var interval = subscription.TimeFrame ==
            TimeSpan.FromMinutes(1) ? "1m" : "1d";
        var page = await _restClient.GetCandles(
            symbol,
            IsMarketIndicator(symbol),
            interval,
            2,
            CurrentTime,
            AdjustedCandles,
            cancellationToken);
        var candles = (page?.Candles ?? [])
            .OrderBy(candle => candle.Timestamp)
            .ToArray();
        for (var index = 0; index < candles.Length; index++)
        {
            var state = index < candles.Length - 1
                ? CandleStates.Finished
                : CandleStates.Active;
            if (!RememberCandle(
                subscription.TransactionId,
                candles[index],
                state))
                continue;
            await SendCandle(
                subscription.TransactionId,
                subscription.SecurityId,
                subscription.TimeFrame.Value,
                candles[index],
                state,
                cancellationToken);
        }
    }

    private async ValueTask SendLevel1(
        long transactionId,
        SecurityId securityId,
        TossPrice price,
        TossPriceLimits limits,
        CancellationToken cancellationToken)
    {
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = price.Timestamp.UtcDateTime,
        }
        .TryAdd(
            Level1Fields.LastTradePrice,
            price.LastPrice.ToDecimalValue())
        .TryAdd(
            Level1Fields.LastTradeTime,
            price.Timestamp.UtcDateTime)
        .TryAdd(
            Level1Fields.MaxPrice,
            limits?.UpperLimitPrice.ToDecimalValue())
        .TryAdd(
            Level1Fields.MinPrice,
            limits?.LowerLimitPrice.ToDecimalValue());
        await SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendDepth(
        long transactionId,
        SecurityId securityId,
        TossOrderBook book,
        int? maxDepth,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = book.Timestamp.UtcDateTime,
            Bids =
            [
                .. (book.Bids ?? [])
                    .Select(entry => (
                        price: entry.Price.ToDecimalValue(),
                        volume: entry.Volume.ToDecimalValue()))
                    .Where(entry =>
                        entry.price > 0 && entry.volume >= 0)
                    .OrderByDescending(entry => entry.price)
                    .Take(maxDepth ?? int.MaxValue)
                    .Select(entry => new QuoteChange(
                        entry.price.Value,
                        entry.volume.Value)),
            ],
            Asks =
            [
                .. (book.Asks ?? [])
                    .Select(entry => (
                        price: entry.Price.ToDecimalValue(),
                        volume: entry.Volume.ToDecimalValue()))
                    .Where(entry =>
                        entry.price > 0 && entry.volume >= 0)
                    .OrderBy(entry => entry.price)
                    .Take(maxDepth ?? int.MaxValue)
                    .Select(entry => new QuoteChange(
                        entry.price.Value,
                        entry.volume.Value)),
            ],
        }, cancellationToken);

    private ValueTask SendTrade(
        long transactionId,
        SecurityId securityId,
        TossPublicTrade trade,
        CancellationToken cancellationToken)
    {
        var price = trade.Price.ToDecimalValue();
        var volume = trade.Volume.ToDecimalValue();
        if (price is not > 0 || volume is not > 0)
            return default;

        var tradeId =
            $"{securityId.SecurityCode}:{trade.Timestamp:O}:{trade.Price}:{trade.Volume}";
        var seenKey = $"{transactionId}:{tradeId}";
        if (_seenPublicTrades.Contains(seenKey))
            return default;
        _seenPublicTrades.Add(seenKey);

        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            TradeStringId = tradeId,
            TradePrice = price,
            TradeVolume = volume,
            ServerTime = trade.Timestamp.UtcDateTime,
        }, cancellationToken);
    }

    private ValueTask SendCandle(
        long transactionId,
        SecurityId securityId,
        TimeSpan timeFrame,
        TossCandle candle,
        CandleStates state,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new TimeFrameCandleMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            TypedArg = timeFrame,
            OpenTime = candle.Timestamp.UtcDateTime,
            OpenPrice = candle.OpenPrice.ToDecimalValue() ?? 0,
            HighPrice = candle.HighPrice.ToDecimalValue() ?? 0,
            LowPrice = candle.LowPrice.ToDecimalValue() ?? 0,
            ClosePrice = candle.ClosePrice.ToDecimalValue() ?? 0,
            TotalVolume = candle.Volume.ToDecimalValue() ?? 0,
            State = state,
        }, cancellationToken);

    private bool RememberCandle(
        long transactionId,
        TossCandle candle,
        CandleStates state)
        => IsChanged(
            $"{transactionId}:candle:{candle.Timestamp.UtcTicks}",
            $"{state}:{candle.OpenPrice}:{candle.HighPrice}:" +
            $"{candle.LowPrice}:{candle.ClosePrice}:{candle.Volume}");

    private bool IsChanged(string key, string signature)
    {
        if (_marketSignatures.TryGetValue(key, out var previous) &&
            previous == signature)
            return false;
        _marketSignatures[key] = signature;
        return true;
    }

    private static string DepthSignature(TossOrderBook book)
        => $"{book.Timestamp:O}:" +
            (book.Bids ?? []).Select(
                entry => $"{entry.Price}@{entry.Volume}").Join(",") +
            ":" +
            (book.Asks ?? []).Select(
                entry => $"{entry.Price}@{entry.Volume}").Join(",");

    private static SecurityMessage CreateSecurityMessage(
        TossStock stock,
        long transactionId)
        => new()
        {
            OriginalTransactionId = transactionId,
            SecurityId = new()
            {
                SecurityCode = stock.Symbol,
                BoardCode = stock.Market.ToBoard(stock.Currency),
                Isin = stock.Isin,
            },
            Name = stock.EnglishName.IsEmpty(stock.Name),
            ShortName = stock.Symbol,
            SecurityType = stock.SecurityType.ToSecurityType(),
            Currency = stock.Currency.ToCurrency(),
            IssueSize = stock.SharesOutstanding.ToDecimalValue(),
            VolumeStep = 1,
            MinVolume = 1,
        };

    private static SecurityMessage CreateIndicatorSecurity(
        string code,
        string name,
        SecurityTypes type,
        long transactionId)
        => new()
        {
            OriginalTransactionId = transactionId,
            SecurityId = new()
            {
                SecurityCode = code,
                BoardCode = type == SecurityTypes.Index
                    ? "KRX" : "KR-BOND",
            },
            Name = name,
            ShortName = code,
            SecurityType = type,
            Currency = CurrencyTypes.KRW,
        };

    private static bool IsMarketIndicator(string symbol)
        => _marketIndicators.Any(indicator =>
            indicator.code.EqualsIgnoreCase(symbol));

    private static string RequireSymbol(SecurityId securityId)
        => securityId.SecurityCode.ThrowIfEmpty(
            nameof(securityId.SecurityCode));

    private static void ValidateTimeFrame(
        SecurityId securityId,
        TimeSpan timeFrame)
    {
        if (!AllTimeFrames.Contains(timeFrame))
        {
            throw new NotSupportedException(
                "Toss Securities supports only one-minute and daily candles.");
        }
        if (IsMarketIndicator(securityId.SecurityCode) &&
            securityId.SecurityCode.StartsWith(
                "KR_BOND_", StringComparison.OrdinalIgnoreCase) &&
            timeFrame != TimeSpan.FromDays(1))
        {
            throw new NotSupportedException(
                "Toss Securities provides daily candles only for Korean government-bond yields.");
        }
    }
}
