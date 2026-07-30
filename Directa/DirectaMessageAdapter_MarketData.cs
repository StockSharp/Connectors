namespace StockSharp.Directa;

public partial class DirectaMessageAdapter
{
    private sealed class BookState
    {
        private readonly Dictionary<int, DirectaBookLevel>
            _bids = [];
        private readonly Dictionary<int, DirectaBookLevel>
            _asks = [];
        private readonly object _sync = new();

        public void Apply(DirectaBookSlice slice)
        {
            lock (_sync)
            {
                for (var level = slice.FirstLevel;
                    level < slice.FirstLevel + 5;
                    level++)
                {
                    _bids.Remove(level);
                    _asks.Remove(level);
                }

                foreach (var item in slice.Levels ?? [])
                {
                    var target = item.Side == Sides.Buy
                        ? _bids : _asks;
                    target[item.Level] = item;
                }
            }
        }

        public (QuoteChange[] Bids,
            QuoteChange[] Asks) Snapshot(int depth)
        {
            lock (_sync)
            {
                return (
                    ToQuotes(_bids, depth),
                    ToQuotes(_asks, depth));
            }
        }

        private static QuoteChange[] ToQuotes(
            Dictionary<int, DirectaBookLevel> levels,
            int depth)
            => levels
                .OrderBy(pair => pair.Key)
                .Take(depth)
                .Select(pair => new QuoteChange(
                    pair.Value.Price,
                    pair.Value.Volume)
                {
                    OrdersCount = pair.Value.Orders,
                })
                .ToArray();
    }

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        var types = message.GetSecurityTypes();
        var left = message.Count ?? long.MaxValue;
        var query =
            (message.SecurityId.Native as string)
                .IsEmpty(message.SecurityId.SecurityCode);
        if (!query.IsEmpty())
        {
            await SendSecurity(
                new()
                {
                    Ticker = query.Trim(),
                    Name = query.Trim(),
                    Type = SecurityTypes.Stock,
                },
                message, types, cancellationToken);
        }
        else
        {
            var tables = await RequestBlock(
                "TABLELIST", "BEGIN TABLE", "END TABLE",
                null, cancellationToken);
            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables
                .Where(IsTableLine)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (left <= 0)
                    break;

                string[] rows;
                try
                {
                    rows = await RequestBlock(
                        "TABLE " + table,
                        "BEGIN LIST", "END LIST",
                        null, cancellationToken);
                }
                catch (InvalidOperationException error)
                {
                    this.AddWarningLog(
                        "Directa table '{0}' skipped: {1}",
                        table, error.Message);
                    continue;
                }

                var type =
                    DirectaProtocol.InferSecurityType(table);

                foreach (var row in rows)
                {
                    var parts =
                        DirectaProtocol.Split(row);
                    if (parts.Length < 2 ||
                        parts[0].IsEmpty() ||
                        !seen.Add(parts[0]))
                        continue;

                    var security = new DirectaSecurity
                    {
                        Ticker = parts[0],
                        Name = parts.Skip(1).Join(";"),
                        Table = table,
                        Type = type,
                    };
                    if (!await SendSecurity(
                        security, message, types,
                        cancellationToken))
                        continue;
                    if (--left <= 0)
                        break;
                }
            }
        }

        await SendSubscriptionResultAsync(
            message, cancellationToken);
    }

    private async ValueTask<bool> SendSecurity(
        DirectaSecurity item,
        SecurityLookupMessage lookup,
        HashSet<SecurityTypes> types,
        CancellationToken cancellationToken)
    {
        if (item?.Ticker.IsEmpty() != false)
            return false;

        var message = new SecurityMessage
        {
            OriginalTransactionId =
                lookup.TransactionId,
            SecurityId = DirectaProtocol.ToSecurityId(
                item.Ticker, item.Isin),
            Name = item.Name.IsEmpty(item.Ticker),
            ShortName = item.Ticker,
            SecurityType = item.Type,
            VolumeStep = 1,
            MinVolume = 1,
        };
        if (!message.IsMatch(lookup, types))
            return false;

        _securities[item.Ticker] = item;
        await SendOutMessageAsync(
            message, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    protected override async ValueTask
        OnLevel1SubscriptionAsync(
            MarketDataMessage message,
            CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        if (!message.IsSubscribe)
        {
            await RemoveDataSubscription(
                message.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId,
                cancellationToken);
            return;
        }

        var ticker = GetTicker(message.SecurityId);
        await AddDataSubscription(
            message, DataType.Level1,
            ticker, cancellationToken);
        await SendSubscriptionResultAsync(
            message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask
        OnMarketDepthSubscriptionAsync(
            MarketDataMessage message,
            CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        if (!message.IsSubscribe)
        {
            await RemoveDataSubscription(
                message.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId,
                cancellationToken);
            return;
        }

        var ticker = GetTicker(message.SecurityId);
        await AddDataSubscription(
            message, DataType.MarketDepth,
            ticker, cancellationToken);
        await SendSubscriptionResultAsync(
            message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask
        OnTicksSubscriptionAsync(
            MarketDataMessage message,
            CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        if (!message.IsSubscribe)
        {
            await RemoveDataSubscription(
                message.OriginalTransactionId,
                cancellationToken);
            return;
        }

        var ticker = GetTicker(message.SecurityId);
        if (message.From is not null ||
            message.To is not null ||
            message.Count is not null ||
            message.IsHistoryOnly())
        {
            var history = await EnsureHistory(
                cancellationToken);
            var days = GetHistoryDays(
                message.From, message.To,
                message.Count, null);
            var lines = await history.GetTicks(
                ticker, message.From, message.To,
                days, _timeZone, cancellationToken);
            var ticks = lines
                .Select(line =>
                    DirectaProtocol.ParseTick(
                        line, _timeZone))
                .OrderBy(tick => tick.Time)
                .ToArray();
            var normalized = new List<(
                DirectaHistoricalTick Tick,
                decimal? Volume)>(ticks.Length);
            long previousVolume = 0;
            DateTime previousDate = default;

            foreach (var tick in ticks)
            {
                var marketDate =
                    TimeZoneInfo.ConvertTimeFromUtc(
                        tick.Time, _timeZone).Date;
                if (marketDate != previousDate ||
                    tick.ProgressiveVolume < previousVolume)
                    previousVolume = 0;
                var volume = tick.ProgressiveVolume -
                    previousVolume;
                previousVolume = tick.ProgressiveVolume;
                previousDate = marketDate;
                normalized.Add((
                    tick, volume > 0 ? volume : null));
            }

            IEnumerable<(
                DirectaHistoricalTick Tick,
                decimal? Volume)> selected = normalized
                .Where(tick =>
                    message.From is not DateTime from ||
                    tick.Tick.Time >= from.ToUniversalTime())
                .Where(tick =>
                    message.To is not DateTime to ||
                    tick.Tick.Time <= to.ToUniversalTime());
            if (message.Count is > 0 and <= int.MaxValue)
            {
                selected = selected.TakeLast(
                    (int)message.Count.Value);
            }

            foreach (var (tick, volume) in selected)
            {
                await SendOutMessageAsync(
                    new ExecutionMessage
                    {
                        DataTypeEx = DataType.Ticks,
                        OriginalTransactionId =
                            message.TransactionId,
                        SecurityId = message.SecurityId,
                        ServerTime = tick.Time,
                        TradeStringId =
                            $"{ticker}:{tick.Time:O}:" +
                            tick.ProgressiveVolume,
                        TradePrice = tick.Price,
                        TradeVolume = volume,
                    }, cancellationToken);
            }
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId,
                cancellationToken);
            return;
        }

        await AddDataSubscription(
            message, DataType.Ticks,
            ticker, cancellationToken);
        await SendSubscriptionResultAsync(
            message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask
        OnTFCandlesSubscriptionAsync(
            MarketDataMessage message,
            CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        if (!message.IsSubscribe)
            return;
        if (!message.IsHistoryOnly())
        {
            throw new NotSupportedException(
                "Directa Darwin provides historical candles only.");
        }

        var ticker = GetTicker(message.SecurityId);
        var timeFrame = message.GetTimeFrame();
        var history = await EnsureHistory(
            cancellationToken);
        var lines = await history.GetCandles(
            ticker, timeFrame, message.From, message.To,
            GetHistoryDays(
                message.From, message.To,
                message.Count, timeFrame),
            _timeZone, cancellationToken);
        var candles = lines
            .Select(line =>
                DirectaProtocol.ParseCandle(
                    line, _timeZone))
            .Where(candle =>
                message.From is not DateTime from ||
                candle.Time >= from.ToUniversalTime())
            .Where(candle =>
                message.To is not DateTime to ||
                candle.Time <= to.ToUniversalTime())
            .OrderBy(candle => candle.Time)
            .ToArray();
        if (message.Count is > 0 and <= int.MaxValue)
        {
            candles = candles.TakeLast(
                (int)message.Count.Value).ToArray();
        }

        foreach (var candle in candles)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId =
                        message.TransactionId,
                    SecurityId = message.SecurityId,
                    TypedArg = timeFrame,
                    OpenTime = candle.Time,
                    CloseTime = candle.Time + timeFrame,
                    OpenPrice = candle.Open,
                    LowPrice = candle.Low,
                    HighPrice = candle.High,
                    ClosePrice = candle.Close,
                    TotalVolume = candle.Volume,
                    State = CandleStates.Finished,
                }, cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            message.TransactionId, cancellationToken);
    }

    private async ValueTask AddDataSubscription(
        MarketDataMessage message, DataType dataType,
        string ticker,
        CancellationToken cancellationToken)
    {
        _marketSubscriptions.Add(
            message.TransactionId, new()
            {
                TransactionId = message.TransactionId,
                SecurityId = message.SecurityId,
                Ticker = ticker,
                DataType = dataType,
                MaxDepth = message.MaxDepth,
            });
        try
        {
            await EnsureData(cancellationToken);
            await UpdateDataSubscription(
                ticker, cancellationToken);
        }
        catch
        {
            _marketSubscriptions.Remove(
                message.TransactionId);
            throw;
        }
    }

    private async ValueTask RemoveDataSubscription(
        long transactionId,
        CancellationToken cancellationToken)
    {
        if (!_marketSubscriptions.TryGetAndRemove(
            transactionId, out var subscription))
            return;
        if (_data is not null)
        {
            await UpdateDataSubscription(
                subscription.Ticker,
                cancellationToken);
        }
    }

    private async Task RestoreDataSubscriptions(
        CancellationToken cancellationToken)
    {
        foreach (var ticker in
            _marketSubscriptions.CachedValues
                .Select(value => value.Ticker)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase))
        {
            await UpdateDataSubscription(
                ticker, cancellationToken);
        }
    }

    private async Task UpdateDataSubscription(
        string ticker,
        CancellationToken cancellationToken)
    {
        await _subscriptionSync.WaitAsync(
            cancellationToken);
        try
        {
            ticker =
                DirectaProtocol.NormalizeTicker(ticker);
            var subscriptions =
                _marketSubscriptions.CachedValues
                    .Where(value =>
                        value.Ticker.EqualsIgnoreCase(ticker))
                    .ToArray();
            var newCode =
                CalculateSubscriptionCode(subscriptions);
            _sentDataCodes.TryGetValue(
                ticker, out var previousCode);
            if (previousCode == newCode)
                return;

            if (!previousCode.IsEmpty())
            {
                await Data.Send(
                    "UNS " + ticker, cancellationToken);
                _sentDataCodes.Remove(ticker);
                _books.Remove(ticker);
            }
            if (!newCode.IsEmpty())
            {
                await Data.Send(
                    $"{newCode} {ticker}",
                    cancellationToken);
                _sentDataCodes[ticker] = newCode;
            }
        }
        finally
        {
            _subscriptionSync.Release();
        }
    }

    private string CalculateSubscriptionCode(
        MarketSubscription[] subscriptions)
    {
        if (subscriptions.Length == 0)
            return null;

        var hasLevel1 = subscriptions.Any(value =>
            value.DataType == DataType.Level1);
        var hasTicks = subscriptions.Any(value =>
            value.DataType == DataType.Ticks);
        var depth = subscriptions
            .Where(value =>
                value.DataType == DataType.MarketDepth)
            .Select(value => Math.Clamp(
                value.MaxDepth ?? MaxMarketDepth,
                1, MaxMarketDepth))
            .DefaultIfEmpty()
            .Max();
        if (depth > 0)
        {
            if (depth <= 5)
                return hasLevel1 ? "SUBALL" : "SUB";
            if (depth <= 10)
                return "SUB10";
            if (depth <= 15)
                return "SUB15";
            return "SUB20";
        }
        if (hasLevel1)
            return "SUBPRZALL";
        return hasTicks ? "SUBPRZ" : null;
    }

    private async ValueTask ProcessDataLine(
        string line,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = DirectaProtocol.Split(line)
                .FirstOrDefault();
            switch (type)
            {
                case "ANAG":
                    await ProcessRegistry(
                        DirectaProtocol.ParseRegistry(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "PRICE":
                case "PRICE_AUCT":
                    await ProcessPrice(
                        DirectaProtocol.ParsePrice(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "BIDASK":
                    await ProcessBidAsk(
                        DirectaProtocol.ParseBidAsk(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case string value when
                    value.StartsWith(
                        "BOOK_",
                        StringComparison.OrdinalIgnoreCase):
                    await ProcessBook(
                        DirectaProtocol.ParseBook(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "ERR":
                    await SendOutErrorAsync(
                        CreateProtocolError(line),
                        cancellationToken);
                    break;
                case "DARWIN_STATUS":
                    break;
                default:
                    this.AddDebugLog(
                        "Directa ignored datafeed line '{0}'.",
                        line);
                    break;
            }
        }
        catch (Exception error)
            when (error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
        {
            await SendOutErrorAsync(
                error, cancellationToken);
        }
    }

    private async ValueTask ProcessRegistry(
        DirectaRegistry registry,
        CancellationToken cancellationToken)
    {
        var security = new DirectaSecurity
        {
            Ticker = registry.Ticker,
            Name = registry.Name,
            Type = _securities.TryGetValue(
                registry.Ticker, out var cached)
                    ? cached.Type : SecurityTypes.Stock,
            Isin = registry.Isin,
            Table = cached?.Table,
        };
        _securities[registry.Ticker] = security;

        foreach (var subscription in
            FindSubscriptions(registry.Ticker))
        {
            await SendOutMessageAsync(
                new SecurityMessage
                {
                    OriginalTransactionId =
                        subscription.TransactionId,
                    SecurityId =
                        DirectaProtocol.ToSecurityId(
                            registry.Ticker,
                            registry.Isin),
                    Name = registry.Name,
                    ShortName = registry.Ticker,
                    SecurityType = security.Type,
                    VolumeStep = 1,
                    MinVolume = 1,
                }, cancellationToken);
        }

        var level1 = new Level1ChangeMessage
        {
            SecurityId =
                DirectaProtocol.ToSecurityId(
                    registry.Ticker, registry.Isin),
            ServerTime = registry.Time,
        }
        .TryAdd(Level1Fields.ClosePrice,
            registry.ReferencePrice)
        .TryAdd(Level1Fields.OpenPrice,
            registry.OpenPrice);
        await BroadcastLevel1(
            registry.Ticker, level1,
            cancellationToken);
    }

    private async ValueTask ProcessPrice(
        DirectaPrice price,
        CancellationToken cancellationToken)
    {
        var level1 = new Level1ChangeMessage
        {
            SecurityId =
                DirectaProtocol.ToSecurityId(
                    price.Ticker),
            ServerTime = price.Time,
        }
        .TryAdd(Level1Fields.LastTradePrice,
            price.Price)
        .TryAdd(Level1Fields.LastTradeVolume,
            price.Volume)
        .TryAdd(Level1Fields.LastTradeTime,
            price.Time)
        .TryAdd(Level1Fields.LowPrice,
            price.LowPrice)
        .TryAdd(Level1Fields.HighPrice,
            price.HighPrice);
        await BroadcastLevel1(
            price.Ticker, level1,
            cancellationToken);

        foreach (var subscription in FindSubscriptions(
            price.Ticker, DataType.Ticks))
        {
            var tradeId =
                price.ExchangeTradeId ?? price.TradeId;
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    DataTypeEx = DataType.Ticks,
                    OriginalTransactionId =
                        subscription.TransactionId,
                    SecurityId =
                        subscription.SecurityId,
                    ServerTime = price.Time,
                    TradeId = tradeId,
                    TradeStringId = tradeId is null
                        ? $"{price.Ticker}:{price.Time:O}:" +
                            DirectaProtocol.FormatDecimal(
                                price.Price)
                        : null,
                    TradePrice = price.Price,
                    TradeVolume = price.Volume,
                }, cancellationToken);
        }
    }

    private async ValueTask ProcessBidAsk(
        DirectaBidAsk quote,
        CancellationToken cancellationToken)
    {
        var level1 = new Level1ChangeMessage
        {
            SecurityId =
                DirectaProtocol.ToSecurityId(
                    quote.Ticker),
            ServerTime = quote.Time,
        }
        .TryAdd(Level1Fields.BestBidPrice,
            quote.BidPrice)
        .TryAdd(Level1Fields.BestBidVolume,
            quote.BidVolume)
        .TryAdd(Level1Fields.BidsCount,
            quote.BidOrders)
        .TryAdd(Level1Fields.BestAskPrice,
            quote.AskPrice)
        .TryAdd(Level1Fields.BestAskVolume,
            quote.AskVolume)
        .TryAdd(Level1Fields.AsksCount,
            quote.AskOrders);
        await BroadcastLevel1(
            quote.Ticker, level1,
            cancellationToken);

        foreach (var subscription in FindSubscriptions(
            quote.Ticker, DataType.MarketDepth))
        {
            await SendOutMessageAsync(
                new QuoteChangeMessage
                {
                    OriginalTransactionId =
                        subscription.TransactionId,
                    SecurityId =
                        subscription.SecurityId,
                    ServerTime = quote.Time,
                    Bids = quote.BidPrice is > 0 &&
                        quote.BidVolume is >= 0
                            ? [new QuoteChange(
                                quote.BidPrice.Value,
                                quote.BidVolume.Value)
                            {
                                OrdersCount =
                                    quote.BidOrders,
                            }]
                            : [],
                    Asks = quote.AskPrice is > 0 &&
                        quote.AskVolume is >= 0
                            ? [new QuoteChange(
                                quote.AskPrice.Value,
                                quote.AskVolume.Value)
                            {
                                OrdersCount =
                                    quote.AskOrders,
                            }]
                            : [],
                }, cancellationToken);
        }
    }

    private async ValueTask ProcessBook(
        DirectaBookSlice slice,
        CancellationToken cancellationToken)
    {
        if (!_books.TryGetValue(
            slice.Ticker, out var book))
        {
            book = new();
            _books[slice.Ticker] = book;
        }
        book.Apply(slice);

        foreach (var subscription in FindSubscriptions(
            slice.Ticker, DataType.MarketDepth))
        {
            var depth = Math.Clamp(
                subscription.MaxDepth ??
                    MaxMarketDepth,
                1, MaxMarketDepth);
            var (bids, asks) = book.Snapshot(depth);
            await SendOutMessageAsync(
                new QuoteChangeMessage
                {
                    OriginalTransactionId =
                        subscription.TransactionId,
                    SecurityId =
                        subscription.SecurityId,
                    ServerTime = slice.Time,
                    Bids = bids,
                    Asks = asks,
                }, cancellationToken);
        }
    }

    private async ValueTask BroadcastLevel1(
        string ticker, Level1ChangeMessage template,
        CancellationToken cancellationToken)
    {
        if (template.Changes.Count == 0)
            return;

        foreach (var subscription in FindSubscriptions(
            ticker, DataType.Level1))
        {
            var message =
                (Level1ChangeMessage)template.Clone();
            message.OriginalTransactionId =
                subscription.TransactionId;
            message.SecurityId =
                subscription.SecurityId;
            await SendOutMessageAsync(
                message, cancellationToken);
        }
    }

    private MarketSubscription[] FindSubscriptions(
        string ticker, DataType dataType = null)
        => _marketSubscriptions.CachedValues
            .Where(value =>
                value.Ticker.EqualsIgnoreCase(ticker) &&
                (dataType is null ||
                    value.DataType == dataType))
            .ToArray();

    private string GetTicker(SecurityId securityId)
    {
        var ticker = DirectaProtocol.NormalizeTicker(
            securityId.ToTicker());
        if (!_securities.ContainsKey(ticker))
        {
            _securities[ticker] = new()
            {
                Ticker = ticker,
                Name = ticker,
                Type = SecurityTypes.Stock,
            };
        }
        return ticker;
    }

    private static bool IsTableLine(string line)
    {
        var parts = DirectaProtocol.Split(line);
        return parts.Length >= 2 &&
            !parts[0].IsEmpty() &&
            !parts[0].Contains(' ');
    }

    private static int GetHistoryDays(
        DateTime? from, DateTime? to, long? count,
        TimeSpan? timeFrame)
    {
        if (from is DateTime start)
        {
            var end = to ?? DateTime.UtcNow;
            var days = (end.ToUniversalTime() -
                start.ToUniversalTime()).TotalDays;
            if (days < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(to), to,
                    "Directa history end time precedes its start time.");
            }
            return ClampHistoryDays(days);
        }
        if (count is > 0 &&
            timeFrame is TimeSpan interval)
        {
            return ClampHistoryDays(
                count.Value * interval.TotalDays);
        }
        return 1;
    }

    private static int ClampHistoryDays(double days)
    {
        if (double.IsNaN(days) || days < 0)
            return 1;
        return (int)Math.Clamp(
            Math.Ceiling(days) + 1,
            1, int.MaxValue);
    }

    private static Exception CreateProtocolError(
        string line)
    {
        var parts = DirectaProtocol.Split(line);
        var code = parts.Length > 2 &&
            int.TryParse(
                parts[^1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
                    ? parsed : 0;
        var ticker = parts.Length > 1
            ? parts[1] : null;
        return new InvalidOperationException(
            ticker.IsEmpty() ||
            ticker.EqualsIgnoreCase("N/A")
                ? DirectaProtocol.GetError(code)
                : $"{ticker}: " +
                    DirectaProtocol.GetError(code));
    }
}
