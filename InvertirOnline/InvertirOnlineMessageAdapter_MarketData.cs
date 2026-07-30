namespace StockSharp.InvertirOnline;

public partial class InvertirOnlineMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public IolSecurityKey Native { get; init; }
        public DataType DataType { get; init; }
        public TimeSpan? TimeFrame { get; init; }
        public int? MaxDepth { get; init; }
        public string Signature { get; set; }
    }

    private readonly SynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, IolSecurityKey>
        _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId>
        _securityIds = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var requestedTypes = lookupMsg.GetSecurityTypes();
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = Math.Min(
            lookupMsg.Count ?? LookupLimit,
            LookupLimit);
        if (left <= 0)
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var symbol = lookupMsg.SecurityId.SecurityCode?.Trim();
        var board = lookupMsg.SecurityId.BoardCode?.Trim();
        if (!symbol.IsEmpty() && !board.IsEmpty())
        {
            var title = await _rest.GetTitle(
                board.ToApiMarket(), symbol, cancellationToken);
            if (title != null)
            {
                var security = title.ToSecurityMessage(
                    lookupMsg.TransactionId,
                    board.InferCountry(),
                    board,
                    DefaultInstrumentType,
                    DefaultSettlement.ToNative());
                if (security.IsMatch(lookupMsg, requestedTypes) &&
                    skip-- <= 0)
                {
                    RememberInstrument(
                        title.ToNative(
                            board.InferCountry(),
                            board,
                            DefaultInstrumentType,
                            DefaultSettlement.ToNative()),
                        security.SecurityId);
                    await SendOutMessageAsync(
                        security, cancellationToken);
                }
            }
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var countries = board.IsEmpty()
            ? new[]
            {
                InvertirOnlineCountries.Argentina.ToNative(),
                InvertirOnlineCountries.UnitedStates.ToNative(),
            }
            : [board.InferCountry()];
        var emitted = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var country in countries)
        {
            var groups = await _rest.GetInstrumentGroups(
                country, cancellationToken);
            var groupNames = (groups ?? [])
                .Where(item => item?.InstrumentType.IsEmpty() == false)
                .Select(item => item.InstrumentType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupNames.Count == 0)
                groupNames.Add(DefaultInstrumentType);

            foreach (var group in groupNames)
            {
                var securityType = group.ToSecurityType();
                if (requestedTypes.Count > 0 &&
                    !requestedTypes.Contains(securityType))
                {
                    continue;
                }

                foreach (var instrument in await _rest.GetInstruments(
                    group, country, cancellationToken) ?? [])
                {
                    if (instrument?.Symbol.IsEmpty() != false)
                        continue;

                    var security = instrument.ToSecurityMessage(
                        lookupMsg.TransactionId,
                        country,
                        group,
                        DefaultMarket,
                        DefaultSettlement.ToNative());
                    if (!security.IsMatch(lookupMsg, requestedTypes))
                        continue;
                    if (!emitted.Add(
                        security.SecurityId.Native?.ToString() ??
                            $"{security.SecurityId.SecurityCode}|" +
                            security.SecurityId.BoardCode))
                    {
                        continue;
                    }
                    if (skip-- > 0)
                        continue;

                    RememberInstrument(
                        instrument.ToNative(
                            country,
                            group,
                            DefaultMarket,
                            DefaultSettlement.ToNative()),
                        security.SecurityId);
                    await SendOutMessageAsync(
                        security, cancellationToken);
                    if (--left <= 0)
                    {
                        await SendSubscriptionResultAsync(
                            lookupMsg, cancellationToken);
                        return;
                    }
                }
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
            mdMsg, DataType.Level1, null, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg, DataType.MarketDepth, null, cancellationToken);

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
            _marketSubscriptions.Remove(mdMsg.OriginalTransactionId);
            return;
        }
        if (timeFrame is not null &&
            !AllTimeFrames.Contains(timeFrame.Value))
        {
            throw new NotSupportedException(
                $"IOL supports daily candles only, not {timeFrame}.");
        }

        var native = await ResolveNative(
            mdMsg.SecurityId, cancellationToken);
        RememberInstrument(native, mdMsg.SecurityId);
        await SendMarketSnapshot(
            mdMsg,
            dataType,
            timeFrame,
            native,
            cancellationToken);

        if (!mdMsg.IsHistoryOnly())
        {
            _marketSubscriptions[mdMsg.TransactionId] = new()
            {
                TransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId,
                Native = native,
                DataType = dataType,
                TimeFrame = timeFrame,
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

    private async ValueTask SendMarketSnapshot(
        MarketDataMessage mdMsg,
        DataType dataType,
        TimeSpan? timeFrame,
        IolSecurityKey native,
        CancellationToken cancellationToken)
    {
        if (dataType == DataType.Level1)
        {
            var quote = await _rest.GetQuote(native, cancellationToken);
            if (quote != null)
            {
                await SendLevel1(
                    quote,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }
            return;
        }

        if (dataType == DataType.MarketDepth)
        {
            var quote = await _rest.GetQuote(native, cancellationToken);
            if (quote != null)
            {
                await SendDepth(
                    quote.Date.ToUtc(CurrentTime),
                    quote.Book,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    mdMsg.MaxDepth,
                    cancellationToken);
            }
            return;
        }

        if (timeFrame == TimeSpan.FromDays(1))
        {
            var from = (mdMsg.From ??
                CurrentTime.AddYears(-1)).ToUniversalTime();
            var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
            if (from > to)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mdMsg.From),
                    mdMsg.From,
                    "IOL history start time cannot be after the end time.");
            }

            foreach (var quote in (await _rest.GetHistory(
                native,
                from,
                to,
                AdjustedHistory,
                cancellationToken) ?? [])
                .Where(item =>
                    item != null &&
                    item.Date.ToUtc(CurrentTime) >= from &&
                    item.Date.ToUtc(CurrentTime) <= to)
                .OrderBy(item => item.Date))
            {
                await SendCandle(
                    quote,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    CandleStates.Finished,
                    cancellationToken);
            }
        }
    }

    private async ValueTask PollMarketData(
        CancellationToken cancellationToken)
    {
        var subscriptions = _marketSubscriptions.Values.ToArray();

        foreach (var group in subscriptions.GroupBy(
            item => item.Native.GroupKey,
            StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            IolInstrument[] values;
            try
            {
                values = await _rest.GetInstruments(
                    first.Native.InstrumentType,
                    first.Native.Country,
                    cancellationToken);
            }
            catch (Exception error) when (
                error is HttpRequestException or InvalidDataException)
            {
                this.AddWarningLog(
                    "IOL grouped quote request failed for {0}: {1}",
                    group.Key,
                    error.Message);
                values = [];
            }

            foreach (var subscription in group)
            {
                var value = FindInstrument(
                    values, subscription.Native);
                if (value != null)
                {
                    await ProcessPolledInstrument(
                        subscription, value, cancellationToken);
                    continue;
                }

                var quote = await _rest.GetQuote(
                    subscription.Native, cancellationToken);
                if (quote != null)
                {
                    await ProcessPolledQuote(
                        subscription, quote, cancellationToken);
                }
            }
        }
    }

    private async ValueTask ProcessPolledInstrument(
        MarketSubscription subscription,
        IolInstrument value,
        CancellationToken cancellationToken)
    {
        var signature =
            $"{value.Date:O}|{value.LastPrice}|{value.Volume}|" +
            $"{value.Book?.BidPrice}|{value.Book?.BidVolume}|" +
            $"{value.Book?.AskPrice}|{value.Book?.AskVolume}";
        if (subscription.Signature == signature)
            return;
        subscription.Signature = signature;

        if (subscription.DataType == DataType.Level1)
        {
            await SendLevel1(
                value,
                subscription.TransactionId,
                subscription.SecurityId,
                cancellationToken);
        }
        else if (subscription.DataType == DataType.MarketDepth)
        {
            await SendDepth(
                value.Date.ToUtc(CurrentTime),
                value.Book is null ? [] : [value.Book],
                subscription.TransactionId,
                subscription.SecurityId,
                subscription.MaxDepth,
                cancellationToken);
        }
        else if (subscription.TimeFrame == TimeSpan.FromDays(1))
        {
            await SendCandle(
                value,
                subscription.TransactionId,
                subscription.SecurityId,
                CandleStates.Active,
                cancellationToken);
        }
    }

    private async ValueTask ProcessPolledQuote(
        MarketSubscription subscription,
        IolQuote value,
        CancellationToken cancellationToken)
    {
        var book = value.Book?.FirstOrDefault();
        var signature =
            $"{value.Date:O}|{value.LastPrice}|{value.Volume}|" +
            $"{book?.BidPrice}|{book?.BidVolume}|" +
            $"{book?.AskPrice}|{book?.AskVolume}";
        if (subscription.Signature == signature)
            return;
        subscription.Signature = signature;

        if (subscription.DataType == DataType.Level1)
        {
            await SendLevel1(
                value,
                subscription.TransactionId,
                subscription.SecurityId,
                cancellationToken);
        }
        else if (subscription.DataType == DataType.MarketDepth)
        {
            await SendDepth(
                value.Date.ToUtc(CurrentTime),
                value.Book,
                subscription.TransactionId,
                subscription.SecurityId,
                subscription.MaxDepth,
                cancellationToken);
        }
        else if (subscription.TimeFrame == TimeSpan.FromDays(1))
        {
            await SendCandle(
                value,
                subscription.TransactionId,
                subscription.SecurityId,
                CandleStates.Active,
                cancellationToken);
        }
    }

    private ValueTask SendLevel1(
        IolQuote quote,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var time = quote.Date.ToUtc(CurrentTime);
        var bid = (quote.Book ?? [])
            .Where(item => item?.BidPrice > 0)
            .OrderByDescending(item => item.BidPrice)
            .FirstOrDefault();
        var ask = (quote.Book ?? [])
            .Where(item => item?.AskPrice > 0)
            .OrderBy(item => item.AskPrice)
            .FirstOrDefault();
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = time,
        }
        .TryAdd(Level1Fields.LastTradePrice, quote.LastPrice.Positive())
        .TryAdd(
            Level1Fields.LastTradeTime,
            quote.LastPrice > 0 ? time : null)
        .TryAdd(Level1Fields.OpenPrice, quote.OpenPrice.Positive())
        .TryAdd(Level1Fields.HighPrice, quote.HighPrice.Positive())
        .TryAdd(Level1Fields.LowPrice, quote.LowPrice.Positive())
        .TryAdd(Level1Fields.ClosePrice, quote.PreviousClose.Positive())
        .TryAdd(Level1Fields.AveragePrice, quote.AveragePrice.Positive())
        .TryAdd(Level1Fields.Volume, quote.Volume.Positive())
        .TryAdd(Level1Fields.Turnover, quote.Turnover.Positive())
        .TryAdd(
            Level1Fields.SettlementPrice,
            quote.SettlementPrice.Positive())
        .TryAdd(Level1Fields.OpenInterest, quote.OpenInterest.Positive())
        .TryAdd(Level1Fields.BestBidPrice, bid?.BidPrice.Positive())
        .TryAdd(Level1Fields.BestBidVolume, bid?.BidVolume.Positive())
        .TryAdd(Level1Fields.BestAskPrice, ask?.AskPrice.Positive())
        .TryAdd(Level1Fields.BestAskVolume, ask?.AskVolume.Positive())
        .TryAdd(Level1Fields.Change, quote.ChangePercent);
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendLevel1(
        IolInstrument value,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var time = value.Date.ToUtc(CurrentTime);
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = time,
        }
        .TryAdd(Level1Fields.LastTradePrice, value.LastPrice.Positive())
        .TryAdd(
            Level1Fields.LastTradeTime,
            value.LastPrice > 0 ? time : null)
        .TryAdd(Level1Fields.OpenPrice, value.OpenPrice.Positive())
        .TryAdd(Level1Fields.HighPrice, value.HighPrice.Positive())
        .TryAdd(Level1Fields.LowPrice, value.LowPrice.Positive())
        .TryAdd(Level1Fields.ClosePrice, value.PreviousClose.Positive())
        .TryAdd(Level1Fields.Volume, value.Volume.Positive())
        .TryAdd(
            Level1Fields.BestBidPrice,
            value.Book?.BidPrice.Positive())
        .TryAdd(
            Level1Fields.BestBidVolume,
            value.Book?.BidVolume.Positive())
        .TryAdd(
            Level1Fields.BestAskPrice,
            value.Book?.AskPrice.Positive())
        .TryAdd(
            Level1Fields.BestAskVolume,
            value.Book?.AskVolume.Positive())
        .TryAdd(Level1Fields.Change, value.ChangePercent);
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendDepth(
        DateTime time,
        IEnumerable<IolBookLevel> levels,
        long transactionId,
        SecurityId securityId,
        int? maxDepth,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(
            new QuoteChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime = time,
                Bids =
                [
                    .. (levels ?? [])
                        .Where(item =>
                            item?.BidPrice > 0 && item.BidVolume >= 0)
                        .OrderByDescending(item => item.BidPrice)
                        .Take(maxDepth ?? int.MaxValue)
                        .Select(item => new QuoteChange(
                            item.BidPrice, item.BidVolume)),
                ],
                Asks =
                [
                    .. (levels ?? [])
                        .Where(item =>
                            item?.AskPrice > 0 && item.AskVolume >= 0)
                        .OrderBy(item => item.AskPrice)
                        .Take(maxDepth ?? int.MaxValue)
                        .Select(item => new QuoteChange(
                            item.AskPrice, item.AskVolume)),
                ],
            },
            cancellationToken);

    private ValueTask SendCandle(
        IolQuote quote,
        long transactionId,
        SecurityId securityId,
        CandleStates state,
        CancellationToken cancellationToken)
    {
        if (quote is null || quote.LastPrice <= 0)
            return default;
        var open = quote.OpenPrice.Positive() ?? quote.LastPrice;
        var high = quote.HighPrice.Positive() ??
            Math.Max(open, quote.LastPrice);
        var low = quote.LowPrice.Positive() ??
            Math.Min(open, quote.LastPrice);
        var openTime = quote.Date.ToUtc(CurrentTime).Date;
        return SendOutMessageAsync(
            new TimeFrameCandleMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                TypedArg = TimeSpan.FromDays(1),
                OpenTime = openTime,
                CloseTime = openTime.AddDays(1),
                OpenPrice = open,
                HighPrice = high,
                LowPrice = low,
                ClosePrice = quote.LastPrice,
                TotalVolume = quote.Volume,
                State = state,
            },
            cancellationToken);
    }

    private ValueTask SendCandle(
        IolInstrument value,
        long transactionId,
        SecurityId securityId,
        CandleStates state,
        CancellationToken cancellationToken)
    {
        if (value is null || value.LastPrice <= 0)
            return default;
        var open = value.OpenPrice.Positive() ?? value.LastPrice;
        var high = value.HighPrice.Positive() ??
            Math.Max(open, value.LastPrice);
        var low = value.LowPrice.Positive() ??
            Math.Min(open, value.LastPrice);
        var openTime = value.Date.ToUtc(CurrentTime).Date;
        return SendOutMessageAsync(
            new TimeFrameCandleMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                TypedArg = TimeSpan.FromDays(1),
                OpenTime = openTime,
                CloseTime = openTime.AddDays(1),
                OpenPrice = open,
                HighPrice = high,
                LowPrice = low,
                ClosePrice = value.LastPrice,
                TotalVolume = value.Volume,
                State = state,
            },
            cancellationToken);
    }

    private async Task<IolSecurityKey> ResolveNative(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            return securityId.ToIolNative(
                DefaultCountry.ToNative(),
                DefaultMarket,
                DefaultInstrumentType,
                DefaultSettlement.ToNative());
        }

        var lookupKey = GetLookupKey(
            securityId.SecurityCode, securityId.BoardCode);
        if (_instruments.TryGetValue(lookupKey, out var cached))
            return cached;

        var market = securityId.BoardCode
            .IsEmpty(DefaultMarket)
            .ToApiMarket();
        try
        {
            var title = await _rest.GetTitle(
                market,
                securityId.SecurityCode,
                cancellationToken);
            if (title != null)
            {
                var resolved = title.ToNative(
                    market.InferCountry(),
                    market,
                    DefaultInstrumentType,
                    DefaultSettlement.ToNative());
                RememberInstrument(resolved, resolved.ToSecurityId());
                return resolved;
            }
        }
        catch (HttpRequestException error) when (
            error.StatusCode == HttpStatusCode.NotFound)
        {
            this.AddVerboseLog(
                "IOL security {0}@{1} was not found; using configured defaults.",
                securityId.SecurityCode,
                market);
        }

        var result = securityId.ToIolNative(
            DefaultCountry.ToNative(),
            market,
            DefaultInstrumentType,
            DefaultSettlement.ToNative());
        RememberInstrument(result, result.ToSecurityId());
        return result;
    }

    private SecurityId ResolveSecurityId(
        string symbol,
        string market,
        string settlement,
        string country = null,
        string instrumentType = null)
    {
        var native = new IolSecurityKey(
            country.IsEmpty(market.InferCountry()),
            market.IsEmpty(DefaultMarket).ToApiMarket(),
            instrumentType.IsEmpty(DefaultInstrumentType),
            settlement.ToSettlement(DefaultSettlement.ToNative()),
            symbol.ThrowIfEmpty(nameof(symbol)));
        if (_securityIds.TryGetValue(
            native.ToString(), out var securityId))
        {
            return securityId;
        }
        securityId = native.ToSecurityId();
        RememberInstrument(native, securityId);
        return securityId;
    }

    private void RememberInstrument(
        IolSecurityKey native,
        SecurityId securityId)
    {
        _instruments[native.ToString()] = native;
        _instruments[GetLookupKey(
            native.Symbol, native.Market.ToBoardCode())] = native;
        _instruments[GetLookupKey(native.Symbol, null)] = native;
        _securityIds[GetLookupKey(
            native.Symbol, native.Market.ToBoardCode())] = securityId;
        _securityIds[GetLookupKey(native.Symbol, null)] = securityId;
        _securityIds[native.ToString()] = securityId;
    }

    private static IolInstrument FindInstrument(
        IEnumerable<IolInstrument> values,
        IolSecurityKey native)
        => (values ?? [])
            .Where(item => item?.Symbol.EqualsIgnoreCase(native.Symbol) == true)
            .OrderByDescending(item =>
                item.Market.ToBoardCode().EqualsIgnoreCase(
                    native.Market.ToBoardCode()))
            .ThenByDescending(item =>
                item.Settlement.ToSettlement(native.Settlement)
                    .EqualsIgnoreCase(native.Settlement))
            .FirstOrDefault();

    private static string GetLookupKey(string symbol, string board)
        => $"{symbol?.Trim().ToUpperInvariant()}|" +
            board?.Trim().ToUpperInvariant();
}
