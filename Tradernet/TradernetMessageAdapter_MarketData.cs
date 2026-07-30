namespace StockSharp.Tradernet;

public partial class TradernetMessageAdapter
{
    private sealed class BookState
    {
        private readonly Dictionary<string, TradernetBookRow>
            _rows = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public void Apply(TradernetBookBlock block)
        {
            lock (_sync)
            {
                foreach (var row in block.Deleted ?? [])
                {
                    if (!row.Side.IsEmpty())
                    {
                        _rows.Remove(GetKey(row));
                    }
                    else
                    {
                        foreach (var key in _rows
                            .Where(pair =>
                                pair.Value.Position ==
                                    row.Position)
                            .Select(pair => pair.Key)
                            .ToArray())
                        {
                            _rows.Remove(key);
                        }
                    }
                }

                foreach (var row in
                    (block.Inserted ?? [])
                    .Concat(block.Updated ?? []))
                {
                    if (!row.Side.IsEmpty())
                        _rows[GetKey(row)] = row;
                }
            }
        }

        public (
            QuoteChange[] Bids,
            QuoteChange[] Asks) Snapshot(int depth)
        {
            lock (_sync)
            {
                var rows = _rows.Values
                    .Select(row => new
                    {
                        Row = row,
                        Price = row.Price.ToDecimal(),
                        Volume = row.Quantity.ToDecimal(),
                    })
                    .Where(value =>
                        value.Price is > 0 &&
                        value.Volume is >= 0)
                    .ToArray();
                var bids = rows.Where(value =>
                        value.Row.Side
                            .EqualsIgnoreCase("B"))
                    .OrderByDescending(value =>
                        value.Price)
                    .Take(depth)
                    .Select(value => new QuoteChange(
                        value.Price.Value,
                        value.Volume.Value))
                    .ToArray();
                var asks = rows.Where(value =>
                        value.Row.Side
                            .EqualsIgnoreCase("S"))
                    .OrderBy(value => value.Price)
                    .Take(depth)
                    .Select(value => new QuoteChange(
                        value.Price.Value,
                        value.Volume.Value))
                    .ToArray();
                return (bids, asks);
            }
        }

        private static string GetKey(
            TradernetBookRow row)
            => $"{row.Side}:{row.Position}";
    }

    private readonly record struct PublicTrade(
        DateTime Time, decimal Price, decimal? Volume,
        string Id);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        var securityTypes = message.GetSecurityTypes();
        var left = message.Count ?? long.MaxValue;
        var query = message.SecurityId.SecurityCode;
        var native = message.SecurityId.Native as string;
        if (!native.IsEmpty())
            query = native;
        else if (!query.IsEmpty() &&
            !message.SecurityId.BoardCode.IsEmpty() &&
            !query.Contains('.'))
        {
            query += "." +
                message.SecurityId.BoardCode;
        }

        if (!query.IsEmpty())
        {
            var response = await Rest.FindSecurities(
                query, cancellationToken);

            foreach (var item in response?.Found ?? [])
            {
                if (left <= 0)
                    break;
                left = await SendSearchSecurity(
                    item, message, securityTypes,
                    left, cancellationToken);
            }
        }
        else
        {
            for (var skip = 0; left > 0;
                skip += SecuritiesPageSize)
            {
                var response = await Rest.GetSecurities(
                    SecuritiesPageSize, skip,
                    cancellationToken);
                var securities =
                    response?.Securities ?? [];

                foreach (var security in securities)
                {
                    left = await SendDirectorySecurity(
                        security, message, securityTypes,
                        left, cancellationToken);
                    if (left <= 0)
                        break;
                }

                if (securities.Length <
                        SecuritiesPageSize ||
                    response?.Total is long total &&
                    skip + securities.Length >= total)
                    break;
            }
        }

        await SendSubscriptionResultAsync(
            message, cancellationToken);
    }

    private async ValueTask<long> SendSearchSecurity(
        TradernetSearchSecurity item,
        SecurityLookupMessage message,
        HashSet<SecurityTypes> securityTypes,
        long left,
        CancellationToken cancellationToken)
    {
        if (item?.Ticker.IsEmpty() != false)
            return left;

        var type = item.Type.ToSecurityType(item.Kind);
        var security = new SecurityMessage
        {
            OriginalTransactionId =
                message.TransactionId,
            SecurityId = item.ToSecurityId(),
            Name = item.Name.IsEmpty(item.LatinName),
            ShortName = item.ShortName
                .IsEmpty(item.ExchangeTicker),
            SecurityType = type,
            VolumeStep = 1,
            MinVolume = 1,
        };
        if (!security.IsMatch(message, securityTypes))
            return left;

        CacheSecurity(new()
        {
            Ticker = item.Ticker,
            InstrumentId = item.InstrumentId,
            ExchangeTicker = item.ExchangeTicker,
            Name = item.Name.IsEmpty(item.LatinName),
            MarketId = item.MarketId,
            MarketName = item.Market,
            Type = item.Type,
            Kind = item.Kind,
            IssueNumber = item.Isin,
        });
        await SendOutMessageAsync(
            security, cancellationToken);
        return left - 1;
    }

    private async ValueTask<long> SendDirectorySecurity(
        TradernetSecurity item,
        SecurityLookupMessage message,
        HashSet<SecurityTypes> securityTypes,
        long left,
        CancellationToken cancellationToken)
    {
        if (item?.Ticker.IsEmpty() != false)
            return left;

        CacheSecurity(item);
        var type = item.Type.ToSecurityType(item.Kind);
        var priceStep = item.PriceStep.ToDecimal();
        var lot = item.LotSize.ToDecimal();
        var security = new SecurityMessage
        {
            OriginalTransactionId =
                message.TransactionId,
            SecurityId = item.ToSecurityId(),
            Name = item.Name,
            ShortName = item.ExchangeTicker,
            SecurityType = type,
            Currency = item.Currency.ToCurrency(),
            PriceStep = priceStep,
            Decimals = priceStep?.GetCachedDecimals(),
            VolumeStep = 1,
            MinVolume = lot is > 0 ? lot : 1,
            FaceValue = item.FaceValue.ToDecimal(),
            ExpiryDate =
                item.MaturityDate.ParseTimestamp(),
        };
        if (!security.IsMatch(message, securityTypes))
            return left;

        await SendOutMessageAsync(
            security, cancellationToken);
        return left - 1;
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
            await RemoveMarketSubscription(
                message.OriginalTransactionId,
                cancellationToken);
            return;
        }

        var security = await GetSecurity(
            message.SecurityId, cancellationToken);
        var securityId = security.ToSecurityId();

        foreach (var quote in await Rest.GetQuotes(
            [security.Ticker], cancellationToken))
        {
            await SendLevel1(quote,
                message.TransactionId, securityId,
                cancellationToken);
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId,
                cancellationToken);
            return;
        }

        await AddMarketSubscription(
            message, DataType.Level1,
            security.Ticker, cancellationToken);
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
            await RemoveMarketSubscription(
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

        var security = await GetSecurity(
            message.SecurityId, cancellationToken);
        await AddMarketSubscription(
            message, DataType.MarketDepth,
            security.Ticker, cancellationToken);
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
            await RemoveMarketSubscription(
                message.OriginalTransactionId,
                cancellationToken);
            return;
        }

        var security = await GetSecurity(
            message.SecurityId, cancellationToken);
        var securityId = security.ToSecurityId();
        if (message.From is not null ||
            message.To is not null ||
            message.Count is not null ||
            message.IsHistoryOnly())
        {
            var token = await Rest.GetPublicTrades(
                security.Ticker, cancellationToken);
            IEnumerable<PublicTrade> trades =
                ParsePublicTrades(token, security.Ticker)
                    .OrderBy(trade => trade.Time);
            if (message.From is DateTime from)
            {
                trades = trades.Where(trade =>
                    trade.Time >= from.ToUniversalTime());
            }
            if (message.To is DateTime to)
            {
                trades = trades.Where(trade =>
                    trade.Time <= to.ToUniversalTime());
            }
            if (message.Count is > 0 and <= int.MaxValue)
            {
                trades = trades.TakeLast(
                    (int)message.Count.Value);
            }

            foreach (var trade in trades)
            {
                await SendOutMessageAsync(
                    new ExecutionMessage
                    {
                        DataTypeEx = DataType.Ticks,
                        OriginalTransactionId =
                            message.TransactionId,
                        SecurityId = securityId,
                        ServerTime = trade.Time,
                        TradeStringId = trade.Id,
                        TradePrice = trade.Price,
                        TradeVolume = trade.Volume,
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

        await AddMarketSubscription(
            message, DataType.Ticks,
            security.Ticker, cancellationToken);
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
                "Tradernet provides historical candles only. " +
                "Realtime candles can be built from quote trade updates.");
        }

        var security = await GetSecurity(
            message.SecurityId, cancellationToken);
        var securityId = security.ToSecurityId();
        var timeFrame = message.GetTimeFrame();
        var token = await Rest.GetHloc(
            security.Ticker, timeFrame,
            message.From, message.To,
            message.Count, cancellationToken);
        var candles = ParseCandles(
            token, security.Ticker);
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
                    SecurityId = securityId,
                    TypedArg = timeFrame,
                    OpenTime = candle.Time,
                    CloseTime = candle.Time + timeFrame,
                    OpenPrice = candle.Open,
                    HighPrice = candle.High,
                    LowPrice = candle.Low,
                    ClosePrice = candle.Close,
                    TotalVolume = candle.Volume,
                    State = CandleStates.Finished,
                }, cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            message.TransactionId, cancellationToken);
    }

    private async ValueTask ProcessQuote(
        TradernetQuote quote,
        CancellationToken cancellationToken)
    {
        if (quote?.Ticker.IsEmpty() != false)
            return;

        foreach (var subscription in FindSubscriptions(
            quote.Ticker, DataType.Level1))
        {
            await SendLevel1(
                quote, subscription.TransactionId,
                subscription.SecurityId,
                cancellationToken);
        }

        var price = quote.LastPrice.ToDecimal();
        if (price is null)
            return;
        var time = quote.LastTime.ParseTimestamp(
            CurrentTime);
        var fingerprint =
            $"{quote.LastTime}:{quote.LastPrice}:" +
            $"{quote.LastSize}";
        if (_lastPublicTrades.TryGetValue(
            quote.Ticker, out var previous) &&
            previous == fingerprint)
            return;
        _lastPublicTrades[quote.Ticker] = fingerprint;

        foreach (var subscription in FindSubscriptions(
            quote.Ticker, DataType.Ticks))
        {
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    DataTypeEx = DataType.Ticks,
                    OriginalTransactionId =
                        subscription.TransactionId,
                    SecurityId =
                        subscription.SecurityId,
                    ServerTime = time,
                    TradeStringId =
                        $"{quote.Ticker}:{quote.LastTime}:" +
                        $"{quote.LastPrice}:{quote.LastSize}",
                    TradePrice = price,
                    TradeVolume =
                        quote.LastSize.ToDecimal(),
                }, cancellationToken);
        }
    }

    private ValueTask SendLevel1(
        TradernetQuote quote,
        long originalTransactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (quote is null)
            return default;

        var message = new Level1ChangeMessage
        {
            OriginalTransactionId =
                originalTransactionId,
            SecurityId = securityId,
            ServerTime = quote.LastTime
                .ParseTimestamp(CurrentTime),
        }
        .TryAdd(Level1Fields.BestBidPrice,
            quote.BestBidPrice.ToDecimal())
        .TryAdd(Level1Fields.BestBidVolume,
            quote.BestBidSize.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice,
            quote.BestAskPrice.ToDecimal())
        .TryAdd(Level1Fields.BestAskVolume,
            quote.BestAskSize.ToDecimal())
        .TryAdd(Level1Fields.OpenPrice,
            quote.OpenPrice.ToDecimal())
        .TryAdd(Level1Fields.ClosePrice,
            quote.PreviousPrice.ToDecimal())
        .TryAdd(Level1Fields.HighPrice,
            quote.HighPrice.ToDecimal())
        .TryAdd(Level1Fields.LowPrice,
            quote.LowPrice.ToDecimal())
        .TryAdd(Level1Fields.LastTradePrice,
            quote.LastPrice.ToDecimal())
        .TryAdd(Level1Fields.LastTradeVolume,
            quote.LastSize.ToDecimal())
        .TryAdd(Level1Fields.LastTradeTime,
            quote.LastTime.ParseTimestamp())
        .TryAdd(Level1Fields.Change,
            quote.ChangePercent.ToDecimal())
        .TryAdd(Level1Fields.Volume,
            quote.Volume.ToDecimal())
        .TryAdd(Level1Fields.Turnover,
            quote.Turnover.ToDecimal())
        .TryAdd(Level1Fields.Yield,
            quote.Yield.ToDecimal())
        .TryAdd(Level1Fields.AccruedCouponIncome,
            quote.AccruedInterest.ToDecimal())
        .TryAdd(Level1Fields.TradesCount,
            quote.TradesCount.ToDecimal());
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(
                message, cancellationToken);
    }

    private async ValueTask ProcessBook(
        TradernetBookBlock block,
        CancellationToken cancellationToken)
    {
        if (block?.Ticker.IsEmpty() != false)
            return;

        if (!_bookStates.TryGetValue(
            block.Ticker, out var state))
        {
            state = new();
            _bookStates[block.Ticker] = state;
        }
        state.Apply(block);

        foreach (var subscription in FindSubscriptions(
            block.Ticker, DataType.MarketDepth))
        {
            var depth = Math.Clamp(
                subscription.MaxDepth ??
                    MaxMarketDepth,
                1, MaxMarketDepth);
            var (bids, asks) = state.Snapshot(depth);
            await SendOutMessageAsync(
                new QuoteChangeMessage
                {
                    OriginalTransactionId =
                        subscription.TransactionId,
                    SecurityId =
                        subscription.SecurityId,
                    ServerTime = CurrentTime,
                    Bids = bids,
                    Asks = asks,
                }, cancellationToken);
        }
    }

    private static (
        DateTime Time, decimal Open, decimal High,
        decimal Low, decimal Close, decimal Volume)[]
        ParseCandles(JToken token, string ticker)
    {
        if (token is JObject wrapper &&
            wrapper["result"] is JToken payload)
        {
            token = payload;
        }
        if (token is not JObject root)
            return [];

        var prices = (root["hloc"] as JObject)?
            [ticker] as JArray;
        var volumes = (root["vl"] as JObject)?
            [ticker] as JArray;
        var times = (root["xSeries"] as JObject)?
            [ticker] as JArray;
        if (prices is null || times is null)
            return [];

        var count = Math.Min(prices.Count, times.Count);
        var result = new List<(
            DateTime, decimal, decimal,
            decimal, decimal, decimal)>(count);

        for (var i = 0; i < count; i++)
        {
            if (prices[i] is not JArray values ||
                values.Count < 4)
                continue;
            var timestamp = times[i].Value<long>();
            var high = ReadDecimal(values[0]);
            var low = ReadDecimal(values[1]);
            var open = ReadDecimal(values[2]);
            var close = ReadDecimal(values[3]);
            if (timestamp <= 0 ||
                high is null || low is null ||
                open is null || close is null)
                continue;
            result.Add((
                DateTimeOffset.FromUnixTimeSeconds(
                    timestamp).UtcDateTime,
                open.Value, high.Value, low.Value,
                close.Value,
                volumes is not null && i < volumes.Count
                    ? ReadDecimal(volumes[i]) ?? 0m
                    : 0m));
        }

        return result.ToArray();
    }

    private static PublicTrade[] ParsePublicTrades(
        JToken token, string ticker)
    {
        if (token is JObject wrapper &&
            wrapper["result"] is JToken payload)
        {
            token = payload;
        }
        if (token is not JObject root ||
            root[ticker] is not JObject tickerData)
            return [];

        var series = tickerData["series"] as JArray;
        if (series is null)
            return [];

        var result = new List<PublicTrade>();
        var index = 0;

        foreach (var item in series)
        {
            long? timestamp = null;
            decimal? price = null;
            decimal? volume = null;
            string id = null;
            if (item is JObject obj)
            {
                timestamp =
                    ReadLong(obj["timestamp"] ??
                        obj["time"] ?? obj["x"] ??
                        obj["date"]);
                price = ReadDecimal(
                    obj["price"] ?? obj["p"] ??
                    obj["y"]);
                volume = ReadDecimal(
                    obj["volume"] ?? obj["q"] ??
                    obj["size"]);
                id = obj.Value<string>("id");
            }
            else if (item is JArray values &&
                values.Count >= 2)
            {
                var first = ReadLong(values[0]);
                var last = ReadLong(
                    values[values.Count - 1]);
                if (first is > 100000000)
                {
                    timestamp = first;
                    price = ReadDecimal(values[1]);
                    if (values.Count > 2)
                        volume = ReadDecimal(values[2]);
                }
                else if (last is > 100000000)
                {
                    timestamp = last;
                    price = ReadDecimal(values[0]);
                    if (values.Count > 2)
                        volume = ReadDecimal(values[1]);
                }
            }

            if (timestamp is null || price is null)
                continue;
            var time = timestamp > 100000000000
                ? DateTimeOffset.FromUnixTimeMilliseconds(
                    timestamp.Value).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(
                    timestamp.Value).UtcDateTime;
            id = id.IsEmpty(
                $"{ticker}:{timestamp}:{price}:{volume}:{index}");
            result.Add(new(time, price.Value, volume, id));
            index++;
        }

        return result.ToArray();
    }

    private static decimal? ReadDecimal(JToken token)
        => token is null ||
            token.Type == JTokenType.Null
                ? null
                : token.Type is JTokenType.Integer or
                    JTokenType.Float
                    ? token.Value<decimal>()
                    : token.Value<string>().ToDecimal();

    private static long? ReadLong(JToken token)
    {
        if (token is null ||
            token.Type == JTokenType.Null)
            return null;
        if (token.Type == JTokenType.Integer)
            return token.Value<long>();
        if (long.TryParse(
            token.Value<string>(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value))
            return value;
        if (DateTimeOffset.TryParse(
            token.Value<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var time))
        {
            return time.ToUnixTimeMilliseconds();
        }
        return null;
    }
}
