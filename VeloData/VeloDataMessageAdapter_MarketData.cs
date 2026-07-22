namespace StockSharp.VeloData;

public partial class VeloDataMessageAdapter
{
    private readonly record struct HistoryRange(DateTime From, DateTime To,
        int Limit, bool IsEmpty);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
        if (message.Count is <= 0)
        {
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }

        var value = (message.SecurityId.Native as string)
            .IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
        var securityTypes = message.GetSecurityTypes();
        var skip = Math.Max(0L, message.Skip ?? 0);
        var left = Math.Max(0L,
            Math.Min(message.Count ?? MaximumItems, MaximumItems));
        foreach (var instrument in GetInstruments()
            .Where(instrument => Matches(instrument, value))
            .OrderBy(static instrument => instrument.Code,
                StringComparer.OrdinalIgnoreCase))
        {
            var security = ToSecurityMessage(instrument, message.TransactionId);
            if (!security.IsMatch(message, securityTypes))
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
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }
        if (message.Count is <= 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        var instrument = ResolveInstrument(message.SecurityId);
        var timeFrame = TimeSpan.FromMinutes(1);
        var range = GetRange(message, instrument, timeFrame);
        if (range.IsEmpty)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }
        VeloDataColumns[] columns = instrument.MarketType switch
        {
            VeloDataMarketTypes.Futures =>
            [
                VeloDataColumns.ClosePrice,
                VeloDataColumns.CoinOpenInterestClose,
            ],
            VeloDataMarketTypes.Options =>
            [
                VeloDataColumns.DvolClose,
                VeloDataColumns.IndexPrice,
            ],
            VeloDataMarketTypes.Spot => [VeloDataColumns.ClosePrice],
            _ => throw new NotSupportedException(
                "Unknown Velo Data market type."),
        };
        var rows = await GetRowsAsync(instrument, columns, range, timeFrame,
            cancellationToken);
        IEnumerable<KeyValuePair<DateTime, VeloDataRow>> selected = rows;
        selected = message.From is null
            ? selected.TakeLast(range.Limit)
            : selected.Take(range.Limit);
        var securityId = ToSecurityId(instrument);
        foreach (var pair in selected)
        {
            var row = pair.Value;
            ValidateLevel1(row, instrument.MarketType);
            var change = new Level1ChangeMessage
            {
                OriginalTransactionId = message.TransactionId,
                SecurityId = securityId,
                ServerTime = pair.Key,
            };
            switch (instrument.MarketType)
            {
                case VeloDataMarketTypes.Futures:
                    change
                        .TryAdd(Level1Fields.ClosePrice, row.ClosePrice)
                        .TryAdd(Level1Fields.OpenInterest,
                            row.CoinOpenInterestClose);
                    break;
                case VeloDataMarketTypes.Options:
                    change
                        .TryAdd(Level1Fields.ClosePrice, row.DvolClose)
                        .TryAdd(Level1Fields.Index, row.IndexPrice);
                    break;
                case VeloDataMarketTypes.Spot:
                    change.TryAdd(Level1Fields.ClosePrice, row.ClosePrice);
                    break;
            }
            if (change.Changes.Count > 0)
                await SendOutMessageAsync(change, cancellationToken);
        }
        await FinishAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }
        if (message.Count is <= 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        var timeFrame = message.GetTimeFrame();
        _ = timeFrame.ToResolutionMinutes();
        if (!VeloDataExtensions.TimeFrames.Contains(timeFrame))
            throw new NotSupportedException(
                $"Velo Data connector does not advertise {timeFrame} candles.");
        var instrument = ResolveInstrument(message.SecurityId);
        var range = GetRange(message, instrument, timeFrame);
        if (range.IsEmpty)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }
        var columns = instrument.MarketType == VeloDataMarketTypes.Options
            ? new[]
            {
                VeloDataColumns.DvolOpen,
                VeloDataColumns.DvolHigh,
                VeloDataColumns.DvolLow,
                VeloDataColumns.DvolClose,
            }
            :
            [
                VeloDataColumns.OpenPrice,
                VeloDataColumns.HighPrice,
                VeloDataColumns.LowPrice,
                VeloDataColumns.ClosePrice,
                VeloDataColumns.CoinVolume,
                VeloDataColumns.TotalTrades,
            ];
        var rows = await GetRowsAsync(instrument, columns, range, timeFrame,
            cancellationToken);
        IEnumerable<KeyValuePair<DateTime, VeloDataRow>> selected = rows;
        selected = message.From is null
            ? selected.TakeLast(range.Limit)
            : selected.Take(range.Limit);
        var securityId = ToSecurityId(instrument);
        foreach (var pair in selected)
        {
            var row = pair.Value;
            var isOptions = instrument.MarketType == VeloDataMarketTypes.Options;
            var open = isOptions ? row.DvolOpen : row.OpenPrice;
            var high = isOptions ? row.DvolHigh : row.HighPrice;
            var low = isOptions ? row.DvolLow : row.LowPrice;
            var close = isOptions ? row.DvolClose : row.ClosePrice;
            if (open is null || high is null || low is null || close is null)
                continue;
            ValidateOhlc(open.Value, high.Value, low.Value, close.Value,
                row.CoinVolume, row.TotalTrades, isOptions);
            await SendOutMessageAsync(new TimeFrameCandleMessage
            {
                OriginalTransactionId = message.TransactionId,
                SecurityId = securityId,
                DataType = message.DataType2,
                TypedArg = timeFrame,
                OpenTime = pair.Key,
                CloseTime = AddClamped(pair.Key, timeFrame),
                OpenPrice = open.Value,
                HighPrice = high.Value,
                LowPrice = low.Value,
                ClosePrice = close.Value,
                TotalVolume = isOptions ? 0 : row.CoinVolume ?? 0,
                TotalTicks = isOptions ? null : ToTradeCount(row.TotalTrades),
                State = CandleStates.Finished,
            }, cancellationToken);
        }
        await FinishAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnNewsSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            RemoveLiveNews(message.OriginalTransactionId);
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }
        if (message.Count is <= 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        var target = ResolveNewsTarget(message.SecurityId);
        var to = (message.To ?? CurrentTime).EnsureUtc();
        var from = message.From?.EnsureUtc();
        if (from is DateTime requestedFrom && requestedFrom > to)
            throw new ArgumentOutOfRangeException(nameof(message), from,
                "Velo Data news start time is after its end time.");
        var remaining = message.Count;
        if (from is not null || message.To is not null || message.IsHistoryOnly())
        {
            var begin = from ?? SubtractClamped(to, HistoryLookback);
            var stories = await SafeRest().GetNewsAsync(begin, cancellationToken);
            var limit = checked((int)Math.Min(remaining ?? HistoryLimit,
                HistoryLimit));
            var parsed = stories
                .Where(story => story is not null && story.IsDeleted != true &&
                    !story.Headline.IsEmpty() && MatchesNews(story, target.Coin))
                .Select(story => TryGetNewsTime(story, out var time)
                    ? (Story: story, Time: time, IsValid: true)
                    : (Story: story, Time: default, IsValid: false))
                .Where(item => item.IsValid && item.Time >= begin && item.Time <= to)
                .GroupBy(item => item.Story.Id)
                .Select(group => group.OrderBy(item => item.Time).Last())
                .OrderBy(item => item.Time);
            var selected = from is null
                ? parsed.TakeLast(limit)
                : parsed.Take(limit);
            foreach (var item in selected)
            {
                await SendNewsAsync(message.TransactionId, target.SecurityId,
                    item.Story, item.Time, cancellationToken);
                if (remaining is > 0 && --remaining == 0)
                    break;
            }
        }

        if (message.IsHistoryOnly() || message.To is not null || remaining == 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        AddLiveNews(new()
        {
            TransactionId = message.TransactionId,
            SecurityId = target.SecurityId,
            Coin = target.Coin,
            Remaining = remaining,
        });
        try
        {
            await EnsureNewsSocketAsync(cancellationToken);
        }
        catch
        {
            RemoveLiveNews(message.TransactionId);
            throw;
        }
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    private async ValueTask<SortedDictionary<DateTime, VeloDataRow>> GetRowsAsync(
        VeloDataInstrument instrument, VeloDataColumns[] columns,
        HistoryRange range, TimeSpan timeFrame,
        CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<DateTime, VeloDataRow>();
        var request = new VeloDataRowsRequest
        {
            MarketType = instrument.MarketType,
            Exchange = instrument.Exchange,
            Product = instrument.Product,
            Columns = columns,
            Begin = range.From,
            End = AddClamped(range.To, timeFrame),
            Resolution = timeFrame,
        };
        await foreach (var row in SafeRest().GetRowsAsync(request,
            cancellationToken))
        {
            ValidateRowIdentity(row, instrument);
            var time = row.Time.FromVeloMilliseconds();
            if (time >= range.From && time <= range.To)
                result[time] = row;
        }
        return result;
    }

    private HistoryRange GetRange(MarketDataMessage message,
        VeloDataInstrument instrument, TimeSpan timeFrame)
    {
        _ = timeFrame.ToResolutionMinutes();
        var limit = checked((int)Math.Min(message.Count ?? HistoryLimit,
            HistoryLimit).Max(1));
        var to = (message.To ?? CurrentTime).EnsureUtc();
        if (to <= DateTime.UnixEpoch)
            throw new ArgumentOutOfRangeException(nameof(message), to,
                "Velo Data history end must be after the Unix epoch.");
        var availableFrom = instrument.Begin;
        if (to < availableFrom)
            return new(availableFrom, to, limit, true);

        var maximumSpan = TimeSpan.FromTicks(checked(timeFrame.Ticks *
            (limit + 2L)));
        DateTime from;
        if (message.From is DateTime requestedFrom)
        {
            from = requestedFrom.EnsureUtc();
            if (from >= to)
                throw new ArgumentOutOfRangeException(nameof(message), from,
                    "Velo Data history start must be earlier than its end.");
            var cappedTo = AddClamped(from, maximumSpan);
            if (cappedTo < to)
                to = cappedTo;
        }
        else
        {
            var span = HistoryLookback < maximumSpan
                ? HistoryLookback
                : maximumSpan;
            from = SubtractClamped(to, span);
        }
        if (from < availableFrom)
            from = availableFrom;
        return new(from, to, limit, from >= to);
    }

    private static void ValidateRowIdentity(VeloDataRow row,
        VeloDataInstrument instrument)
    {
        if (row is null || !row.Exchange.EqualsIgnoreCase(instrument.Exchange) ||
            !row.Product.EqualsIgnoreCase(instrument.Product))
            throw new InvalidDataException(
                "Velo Data returned a row for a different instrument.");
    }

    private static void ValidateLevel1(VeloDataRow row,
        VeloDataMarketTypes marketType)
    {
        if (marketType is VeloDataMarketTypes.Spot or VeloDataMarketTypes.Futures &&
            row.ClosePrice is <= 0)
            throw new InvalidDataException(
                "Velo Data returned an invalid closing price.");
        if (marketType == VeloDataMarketTypes.Futures &&
            row.CoinOpenInterestClose is < 0)
            throw new InvalidDataException(
                "Velo Data returned negative open interest.");
        if (marketType == VeloDataMarketTypes.Options &&
            (row.DvolClose is <= 0 || row.IndexPrice is <= 0))
            throw new InvalidDataException(
                "Velo Data returned invalid options analytics.");
    }

    private static void ValidateOhlc(decimal open, decimal high, decimal low,
        decimal close, decimal? volume, decimal? trades, bool isOptions)
    {
        if (open <= 0 || high <= 0 || low <= 0 || close <= 0 || low > high ||
            high < open || high < close || low > open || low > close ||
            !isOptions && (volume is < 0 || trades is < 0))
            throw new InvalidDataException(
                "Velo Data returned an invalid OHLCV row.");
    }

    private static int? ToTradeCount(decimal? value)
    {
        if (value is null)
            return null;
        if (value < 0 || decimal.Truncate(value.Value) != value.Value)
            throw new InvalidDataException(
                "Velo Data returned an invalid trade count.");
        return value > int.MaxValue ? int.MaxValue : decimal.ToInt32(value.Value);
    }

    private (SecurityId? SecurityId, string Coin) ResolveNewsTarget(
        SecurityId securityId)
    {
        var raw = (securityId.Native as string).IsEmpty(securityId.SecurityCode);
        if (raw.IsEmpty())
            return (null, null);
        var instrument = ResolveInstrument(securityId);
        return (ToSecurityId(instrument), instrument.Coin);
    }

    private static bool TryGetNewsTime(VeloDataNewsStory story,
        out DateTime result)
    {
        if (story?.Time is not long milliseconds)
        {
            result = default;
            return false;
        }
        try
        {
            result = milliseconds.FromVeloMilliseconds();
            return true;
        }
        catch (InvalidDataException)
        {
            result = default;
            return false;
        }
    }

    private static bool MatchesNews(VeloDataNewsStory story, string coin)
        => coin.IsEmpty() || (story?.Coins ?? [])
            .Any(value => value.EqualsIgnoreCase(coin));

    private async ValueTask SendNewsAsync(long transactionId,
        SecurityId? securityId, VeloDataNewsStory story, DateTime time,
        CancellationToken cancellationToken)
    {
        await SendOutMessageAsync(new NewsMessage
        {
            OriginalTransactionId = transactionId,
            ServerTime = time,
            Id = story.Id?.ToString(CultureInfo.InvariantCulture),
            Headline = story.Headline,
            Story = story.Summary,
            Source = story.Source.IsEmpty("Velo"),
            Url = story.Link,
            BoardCode = BoardCodes.VeloData,
            SecurityId = securityId,
        }, cancellationToken);
    }

    private static bool Matches(VeloDataInstrument instrument, string value)
    {
        if (value.IsEmpty())
            return true;
        return instrument.Key.ContainsIgnoreCase(value) ||
            instrument.Code.ContainsIgnoreCase(value) ||
            instrument.Exchange.ContainsIgnoreCase(value) ||
            instrument.Coin.ContainsIgnoreCase(value) ||
            instrument.Product.ContainsIgnoreCase(value) ||
            instrument.MarketType.ToWire().ContainsIgnoreCase(value);
    }

    private static SecurityId ToSecurityId(VeloDataInstrument instrument)
        => new()
        {
            SecurityCode = instrument.Code,
            BoardCode = BoardCodes.VeloData,
            Native = instrument.Key,
        };

    private static SecurityMessage ToSecurityMessage(
        VeloDataInstrument instrument, long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = ToSecurityId(instrument),
            Name = instrument.MarketType == VeloDataMarketTypes.Options
                ? instrument.Coin.ToUpperInvariant() + " options volatility analytics"
                : instrument.Product + " " + instrument.Exchange,
            ShortName = instrument.Product,
            Class = ("VELO-" + instrument.MarketType.ToWire()).ToUpperInvariant(),
            SecurityType = instrument.MarketType switch
            {
                VeloDataMarketTypes.Futures => SecurityTypes.Future,
                VeloDataMarketTypes.Options => SecurityTypes.Index,
                VeloDataMarketTypes.Spot => SecurityTypes.CryptoCurrency,
                _ => null,
            },
            Currency = instrument.MarketType == VeloDataMarketTypes.Options
                ? CurrencyTypes.USD
                : null,
        };

    private static DateTime AddClamped(DateTime value, TimeSpan interval)
    {
        value = value.EnsureUtc();
        var ticks = Math.Min(interval.Ticks, DateTime.MaxValue.Ticks - value.Ticks);
        return new(value.Ticks + ticks, DateTimeKind.Utc);
    }

    private static DateTime SubtractClamped(DateTime value, TimeSpan interval)
    {
        value = value.EnsureUtc();
        var ticks = Math.Max(DateTime.MinValue.Ticks, value.Ticks - interval.Ticks);
        return new(ticks, DateTimeKind.Utc);
    }

    private async ValueTask FinishAsync(MarketDataMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
