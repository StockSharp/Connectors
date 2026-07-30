namespace StockSharp.Ppi;

public partial class PpiMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public PpiInstrumentKey Native { get; init; }
        public DataType DataType { get; init; }
        public TimeSpan? TimeFrame { get; init; }
        public int? MaxDepth { get; init; }
    }

    private readonly SynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, PpiInstrumentKey>
        _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId>
        _securityIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenMarketTrades =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var requestedTypes = lookupMsg.GetSecurityTypes();
        var nativeType = requestedTypes.Count == 1
            ? requestedTypes.First().ToNativeType()
            : null;
        var ticker = lookupMsg.SecurityId.SecurityCode?.Trim();
        var market = lookupMsg.SecurityId.BoardCode?.Trim();
        var instruments = await _rest.SearchInstruments(
            ticker,
            lookupMsg.Name,
            market,
            nativeType,
            cancellationToken);
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

        foreach (var instrument in instruments ?? [])
        {
            if (instrument?.Ticker.IsEmpty() != false)
                continue;
            var security = instrument.ToSecurityMessage(
                lookupMsg.TransactionId,
                DefaultMarket,
                DefaultSettlement);
            if (!security.IsMatch(lookupMsg, requestedTypes))
                continue;
            if (skip-- > 0)
                continue;

            RememberInstrument(
                instrument.ToNative(DefaultMarket, DefaultSettlement),
                security.SecurityId);
            await SendOutMessageAsync(security, cancellationToken);
            if (--left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg, DataType.Level1, null, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg, DataType.Ticks, null, cancellationToken);

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
                $"PPI supports daily candles only, not {timeFrame}.");
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
            try
            {
                await _stream.SubscribeMarket(native, cancellationToken);
            }
            catch
            {
                _marketSubscriptions.Remove(mdMsg.TransactionId);
                throw;
            }
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
        PpiInstrumentKey native,
        CancellationToken cancellationToken)
    {
        if (dataType == DataType.Level1)
        {
            var current = await _rest.GetCurrent(native, cancellationToken);
            if (current != null)
            {
                await SendLevel1(
                    current,
                    null,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }
            var book = await _rest.GetBook(native, cancellationToken);
            if (book != null)
            {
                await SendBookLevel1(
                    book,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }
            return;
        }

        if (dataType == DataType.MarketDepth)
        {
            var book = await _rest.GetBook(native, cancellationToken);
            if (book != null)
            {
                await SendDepth(
                    book.Date.ToUtc(CurrentTime),
                    book.Bids,
                    book.Offers,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    mdMsg.MaxDepth,
                    cancellationToken);
            }
            return;
        }

        if (dataType == DataType.Ticks)
        {
            var from = mdMsg.From?.ToUniversalTime();
            var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
            var values = (await _rest.GetIntraday(
                native, cancellationToken) ?? [])
                .Where(item =>
                (from is null ||
                    item.Date.UtcDateTime >= from.Value) &&
                item.Date.UtcDateTime <= to)
                .OrderBy(item => item.Date)
                .ToArray();
            if (mdMsg.Count is > 0)
            {
                values =
                [.. values.TakeLast(
                    (int)Math.Min(mdMsg.Count.Value, int.MaxValue))];
            }

            foreach (var value in values)
            {
                await SendTick(
                    value,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }

            return;
        }

        if (timeFrame is not null)
        {
            var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
            var from = (mdMsg.From ??
                to.AddDays(-(mdMsg.Count is > 0
                    ? Math.Min(mdMsg.Count.Value * 2, 3650)
                    : 365))).ToUniversalTime();
            if (from > to)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mdMsg.From),
                    mdMsg.From,
                    "PPI history start time cannot be after the end time.");
            }

            var values = (await _rest.GetHistory(
                native, from, to, cancellationToken) ?? [])
                .OrderBy(item => item.Date)
                .ToArray();
            if (mdMsg.Count is > 0)
            {
                values =
                [.. values.TakeLast(
                    (int)Math.Min(mdMsg.Count.Value, int.MaxValue))];
            }

            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                var openTime = value.Date.UtcDateTime;
                var isActive =
                    !mdMsg.IsHistoryOnly() &&
                    index == values.Length - 1 &&
                    openTime.Date == CurrentTime.ToUniversalTime().Date;
                await SendCandle(
                    value,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    isActive
                        ? CandleStates.Active
                        : CandleStates.Finished,
                    cancellationToken);
            }
        }
    }

    private async ValueTask OnMarketUpdate(
        PpiMarketUpdate update,
        CancellationToken cancellationToken)
    {
        if (update?.Ticker.IsEmpty() != false)
            return;

        var subscriptions = _marketSubscriptions.Values
            .Where(subscription =>
                subscription.Native.Ticker.EqualsIgnoreCase(update.Ticker) &&
                (update.Type.IsEmpty() ||
                    subscription.Native.Type.EqualsIgnoreCase(update.Type)) &&
                (update.Settlement.IsEmpty() ||
                    subscription.Native.Settlement.EqualsIgnoreCase(
                        update.Settlement)))
            .ToArray();

        foreach (var subscription in subscriptions)
        {
            if (subscription.DataType == DataType.Level1)
            {
                await SendLevel1(
                    null,
                    update,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
            else if (subscription.DataType == DataType.MarketDepth &&
                !update.IsTrade)
            {
                await SendDepth(
                    update.Date.ToUtc(CurrentTime),
                    update.Bids,
                    update.Offers,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    subscription.MaxDepth,
                    cancellationToken);
            }
            else if (subscription.DataType == DataType.Ticks &&
                update.IsTrade)
            {
                await SendTick(
                    new()
                    {
                        Date = update.Date,
                        Price = update.Price,
                        Volume = update.VolumeAmount,
                    },
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
            else if (subscription.TimeFrame == TimeSpan.FromDays(1))
            {
                await SendCandle(
                    new()
                    {
                        Date = update.Date,
                        Price = update.Price,
                        OpeningPrice = update.OpeningPrice,
                        High = update.HighPrice,
                        Low = update.LowPrice,
                        Volume = update.TotalVolume,
                    },
                    subscription.TransactionId,
                    subscription.SecurityId,
                    CandleStates.Active,
                    cancellationToken);
            }
        }
    }

    private ValueTask SendLevel1(
        PpiPrice price,
        PpiMarketUpdate update,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var time = update?.Date.ToUtc(CurrentTime) ??
            price?.Date.ToUtc(CurrentTime) ??
            CurrentTime;
        var bids = update?.Bids ?? [];
        var offers = update?.Offers ?? [];
        var bid = bids
            .Where(level => level?.Price > 0)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault();
        var ask = offers
            .Where(level => level?.Price > 0)
            .OrderBy(level => level.Price)
            .FirstOrDefault();
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = time,
        }
        .TryAdd(
            Level1Fields.LastTradePrice,
            (update?.Price ?? price?.Price ?? 0).Positive())
        .TryAdd(
            Level1Fields.LastTradeVolume,
            update?.IsTrade == true
                ? update.VolumeAmount.Positive()
                : null)
        .TryAdd(
            Level1Fields.LastTradeTime,
            (update?.Price ?? price?.Price ?? 0) > 0 ? time : null)
        .TryAdd(
            Level1Fields.OpenPrice,
            (update?.OpeningPrice ?? price?.OpeningPrice ?? 0).Positive())
        .TryAdd(
            Level1Fields.HighPrice,
            (update?.HighPrice ?? price?.High ?? 0).Positive())
        .TryAdd(
            Level1Fields.LowPrice,
            (update?.LowPrice ?? price?.Low ?? 0).Positive())
        .TryAdd(
            Level1Fields.ClosePrice,
            price?.PreviousClose.Positive())
        .TryAdd(
            Level1Fields.Volume,
            (update?.TotalVolume ?? price?.Volume ?? 0).Positive())
        .TryAdd(Level1Fields.BestBidPrice, bid?.Price.Positive())
        .TryAdd(Level1Fields.BestBidVolume, bid?.Quantity.Positive())
        .TryAdd(Level1Fields.BestAskPrice, ask?.Price.Positive())
        .TryAdd(Level1Fields.BestAskVolume, ask?.Quantity.Positive())
        .TryAdd(
            Level1Fields.Change,
            price?.MarketChangePercent.ParsePercent());
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendBookLevel1(
        PpiBook book,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var bid = (book.Bids ?? [])
            .Where(level => level?.Price > 0)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault();
        var ask = (book.Offers ?? [])
            .Where(level => level?.Price > 0)
            .OrderBy(level => level.Price)
            .FirstOrDefault();
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = book.Date.ToUtc(CurrentTime),
        }
        .TryAdd(Level1Fields.BestBidPrice, bid?.Price.Positive())
        .TryAdd(Level1Fields.BestBidVolume, bid?.Quantity.Positive())
        .TryAdd(Level1Fields.BestAskPrice, ask?.Price.Positive())
        .TryAdd(Level1Fields.BestAskVolume, ask?.Quantity.Positive());
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendDepth(
        DateTime time,
        IEnumerable<PpiBookLevel> bids,
        IEnumerable<PpiBookLevel> offers,
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
                    .. (bids ?? [])
                        .Where(level =>
                            level?.Price > 0 && level.Quantity >= 0)
                        .OrderBy(level =>
                            level.Position <= 0
                                ? int.MaxValue
                                : level.Position)
                        .ThenByDescending(level => level.Price)
                        .Take(maxDepth ?? int.MaxValue)
                        .Select(level => new QuoteChange(
                            level.Price, level.Quantity)),
                ],
                Asks =
                [
                    .. (offers ?? [])
                        .Where(level =>
                            level?.Price > 0 && level.Quantity >= 0)
                        .OrderBy(level =>
                            level.Position <= 0
                                ? int.MaxValue
                                : level.Position)
                        .ThenBy(level => level.Price)
                        .Take(maxDepth ?? int.MaxValue)
                        .Select(level => new QuoteChange(
                            level.Price, level.Quantity)),
                ],
            },
            cancellationToken);

    private ValueTask SendTick(
        PpiPrice value,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (value is null || value.Price <= 0 || value.Volume <= 0)
            return default;
        var time = value.Date.ToUtc(CurrentTime);
        var tradeId =
            $"{securityId.SecurityCode}:{time:O}:" +
            $"{value.Price}:{value.Volume}";
        if (!_seenMarketTrades.TryAdd(
            $"{transactionId}:{tradeId}"))
        {
            return default;
        }

        return SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Ticks,
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                TradeStringId = tradeId,
                TradePrice = value.Price,
                TradeVolume = value.Volume,
                ServerTime = time,
            },
            cancellationToken);
    }

    private ValueTask SendCandle(
        PpiPrice value,
        long transactionId,
        SecurityId securityId,
        CandleStates state,
        CancellationToken cancellationToken)
    {
        if (value is null || value.Price <= 0)
            return default;
        var open = value.OpeningPrice.Positive() ?? value.Price;
        var high = value.High.Positive() ?? Math.Max(open, value.Price);
        var low = value.Low.Positive() ?? Math.Min(open, value.Price);
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
                ClosePrice = value.Price,
                TotalVolume = value.Volume,
                State = state,
            },
            cancellationToken);
    }

    private async Task<PpiInstrumentKey> ResolveNative(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            return securityId.ToPpiNative(
                DefaultMarket,
                DefaultInstrumentType,
                DefaultSettlement);
        }

        var lookupKey = GetLookupKey(
            securityId.SecurityCode, securityId.BoardCode);
        if (_instruments.TryGetValue(lookupKey, out var cached))
            return cached;

        var values = await _rest.SearchInstruments(
            securityId.SecurityCode,
            null,
            securityId.BoardCode,
            null,
            cancellationToken);
        var instrument = values
            .FirstOrDefault(item =>
                item.Ticker.EqualsIgnoreCase(
                    securityId.SecurityCode) &&
                (securityId.BoardCode.IsEmpty() ||
                    item.Market.ToBoardCode().EqualsIgnoreCase(
                        securityId.BoardCode))) ??
            values.FirstOrDefault();
        var result = instrument is null
            ? securityId.ToPpiNative(
                DefaultMarket,
                DefaultInstrumentType,
                DefaultSettlement)
            : instrument.ToNative(DefaultMarket, DefaultSettlement);
        RememberInstrument(result, result.ToSecurityId());
        return result;
    }

    private async Task<SecurityId> ResolveSecurityId(
        string ticker,
        string instrumentType,
        string settlement,
        CancellationToken cancellationToken)
    {
        var lookupKey = GetLookupKey(ticker, null);
        if (_securityIds.TryGetValue(lookupKey, out var securityId))
            return securityId;

        var values = await _rest.SearchInstruments(
            ticker,
            null,
            null,
            instrumentType,
            cancellationToken);
        var instrument = values.FirstOrDefault(item =>
            item.Ticker.EqualsIgnoreCase(ticker)) ??
            values.FirstOrDefault();
        var native = instrument is null
            ? new PpiInstrumentKey(
                DefaultMarket,
                instrumentType.IsEmpty(DefaultInstrumentType),
                settlement.IsEmpty(DefaultSettlement),
                ticker)
            : new PpiInstrumentKey(
                instrument.Market.IsEmpty(DefaultMarket),
                instrument.Type.IsEmpty(instrumentType)
                    .IsEmpty(DefaultInstrumentType),
                settlement.IsEmpty(DefaultSettlement),
                instrument.Ticker);
        securityId = native.ToSecurityId();
        RememberInstrument(native, securityId);
        return securityId;
    }

    private void RememberInstrument(
        PpiInstrumentKey native,
        SecurityId securityId)
    {
        _instruments[native.ToString()] = native;
        _instruments[GetLookupKey(
            native.Ticker, native.Market.ToBoardCode())] = native;
        _instruments[GetLookupKey(native.Ticker, null)] = native;
        _securityIds[GetLookupKey(
            native.Ticker, native.Market.ToBoardCode())] = securityId;
        _securityIds[GetLookupKey(native.Ticker, null)] = securityId;
    }

    private static string GetLookupKey(string ticker, string board)
        => $"{ticker?.Trim().ToUpperInvariant()}|" +
            board?.Trim().ToUpperInvariant();
}
