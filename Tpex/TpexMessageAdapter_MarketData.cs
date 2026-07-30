namespace StockSharp.Tpex;

public partial class TpexMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = lookupMsg.SecurityId.GetTpexSymbol();
        var name = lookupMsg.Name
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var types = lookupMsg.GetSecurityTypes();
        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;

        if (left > 0)
        {
            var snapshot = await LoadSnapshot(cancellationToken);
            var securities = snapshot
                .GetAllProfiles(IncludeListedDerivatives)
                .Where(profile => profile.Matches(value, name))
                .Select(profile => profile.ToSecurityMessage(
                    lookupMsg.TransactionId))
                .Where(security =>
                    security.IsMatch(lookupMsg, types))
                .OrderBy(
                    security => security.SecurityId.SecurityCode,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var security in securities)
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                await SendOutMessageAsync(
                    security, cancellationToken);
                if (--left <= 0)
                    break;
            }
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }

        var records = await LoadRecords(mdMsg, cancellationToken);
        var sent = 0;

        foreach (var record in records)
        {
            await SendOutMessageAsync(
                new Level1ChangeMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = record.SecurityId,
                    ServerTime = record.ServerTime,
                }
                .TryAdd(
                    Level1Fields.OpenPrice,
                    record.OpenPrice)
                .TryAdd(
                    Level1Fields.HighPrice,
                    record.HighPrice)
                .TryAdd(
                    Level1Fields.LowPrice,
                    record.LowPrice)
                .TryAdd(
                    Level1Fields.ClosePrice,
                    record.ClosePrice)
                .TryAdd(
                    Level1Fields.LastTradePrice,
                    record.LastTradePrice)
                .TryAdd(
                    Level1Fields.AveragePrice,
                    record.AveragePrice)
                .TryAdd(
                    Level1Fields.MarketPriceYesterday,
                    record.PreviousPrice)
                .TryAdd(
                    Level1Fields.Change,
                    record.PriceChange)
                .TryAdd(
                    Level1Fields.Volume,
                    record.Volume)
                .TryAdd(
                    Level1Fields.Turnover,
                    record.Turnover)
                .TryAdd(
                    Level1Fields.TradesCount,
                    record.TradesCount)
                .TryAdd(
                    Level1Fields.BestBidPrice,
                    record.BestBidPrice)
                .TryAdd(
                    Level1Fields.BestBidVolume,
                    record.BestBidVolume)
                .TryAdd(
                    Level1Fields.BestAskPrice,
                    record.BestAskPrice)
                .TryAdd(
                    Level1Fields.BestAskVolume,
                    record.BestAskVolume)
                .TryAdd(
                    Level1Fields.IssueSize,
                    record.IssueSize)
                .TryAdd(
                    Level1Fields.PriceEarnings,
                    record.PriceEarnings)
                .TryAdd(
                    Level1Fields.Yield,
                    record.DividendYield)
                .TryAdd(
                    Level1Fields.PriceBook,
                    record.PriceBook),
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetTpexSymbol());
        await CompleteSubscription(mdMsg, cancellationToken);
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
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }

        var timeFrame = mdMsg.GetTimeFrame();
        if (timeFrame != TimeSpan.FromDays(1))
        {
            throw new NotSupportedException(
                "TPEx provides only native daily candles.");
        }
        if (Market == TpexMarkets.Emerging)
        {
            throw new NotSupportedException(
                "The Emerging Stock Board publishes average prices, not native OHLC candles.");
        }

        var records = await LoadRecords(mdMsg, cancellationToken);
        var sent = 0;

        foreach (var record in records.Where(
            record => record.HasOhlc))
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = record.SecurityId,
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = record.OpenTime,
                    OpenPrice = record.OpenPrice.Value,
                    HighPrice = record.HighPrice.Value,
                    LowPrice = record.LowPrice.Value,
                    ClosePrice = record.ClosePrice.Value,
                    TotalVolume = record.Volume ?? 0,
                    State = CandleStates.Finished,
                },
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetTpexSymbol());
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<TpexDailyRecord[]> LoadRecords(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var symbol = mdMsg.SecurityId.GetTpexSymbol();
        var from = mdMsg.From?.ToTaipeiDate();
        var to = mdMsg.To?.ToTaipeiDate();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), from,
                "TPEx history start date is after its end date.");
        }

        TpexDailyRecord[] records;
        if (from is not null || to is not null)
        {
            if (symbol.IsEmpty())
            {
                throw new NotSupportedException(
                    "TPEx historical subscriptions require a security code.");
            }

            records = await LoadHistory(
                symbol, from, to, cancellationToken);
        }
        else
        {
            records = await LoadLatest(
                symbol, cancellationToken);
        }

        var requested = mdMsg.Count is long count
            ? checked((int)Math.Min(count.Max(0), int.MaxValue))
            : int.MaxValue;
        var ordered = records
            .OrderBy(record => record.TradingDate)
            .ThenBy(
                record => record.SecurityId.SecurityCode,
                StringComparer.OrdinalIgnoreCase);

        return from is null &&
            to is not null &&
            requested != int.MaxValue
                ? ordered.TakeLast(requested).ToArray()
                : ordered.Take(requested).ToArray();
    }

    private async Task<TpexDailyRecord[]> LoadLatest(
        string symbol,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadSnapshot(cancellationToken);
        var allowedCodes = snapshot
            .GetAllProfiles(IncludeListedDerivatives)
            .Select(profile => profile.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var valuations = (snapshot.Valuations ?? [])
            .Where(value => !value.Code.IsEmpty())
            .GroupBy(
                value => value.Code,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var records = new List<TpexDailyRecord>();

        foreach (var row in snapshot.MainboardPrices ?? [])
        {
            if (!allowedCodes.Contains(row.Code) ||
                (!symbol.IsEmpty() &&
                    !row.Code.EqualsIgnoreCase(symbol)))
            {
                continue;
            }

            records.Add(row.ToRecord(
                valuations.TryGetValue(row.Code, out var valuation)
                    ? valuation
                    : null));
        }

        foreach (var row in snapshot.EmergingPrices ?? [])
        {
            if (!allowedCodes.Contains(row.Code) ||
                (!symbol.IsEmpty() &&
                    !row.Code.EqualsIgnoreCase(symbol)))
            {
                continue;
            }

            records.Add(row.ToRecord());
        }

        return records
            .GroupBy(record =>
                record.SecurityId.SecurityCode,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(record => record.IsEmerging)
                .First())
            .ToArray();
    }

    private async Task<TpexDailyRecord[]> LoadHistory(
        string symbol,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        var end = to ?? TpexExtensions.TaipeiToday();
        var start = from ??
            new DateTime(end.Year, end.Month, 1);
        if (start > end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from), start,
                "TPEx history start date is after its effective end date.");
        }

        var months = EnumerateMonths(start, end).ToArray();
        if (months.Length > MaxHistoryMonths)
        {
            throw new InvalidOperationException(
                $"TPEx history requires {months.Length} months, exceeding the configured limit of {MaxHistoryMonths}.");
        }

        var records = new List<TpexDailyRecord>();

        foreach (var month in months)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Market.IncludesMainboard())
            {
                var rows = await SafeClient().GetMainboardHistory(
                    symbol, month, cancellationToken);
                records.AddRange(rows.Select(
                    row => row.ToRecord(symbol)));
            }
            if (Market.IncludesEmerging())
            {
                var rows = await SafeClient().GetEmergingHistory(
                    symbol, month, cancellationToken);
                records.AddRange(rows.Select(
                    row => row.ToRecord(symbol)));
            }
        }

        return records
            .Where(record =>
                record.TradingDate >= start &&
                record.TradingDate <= end)
            .GroupBy(record => record.TradingDate)
            .Select(group => group
                .OrderByDescending(record => record.HasOhlc)
                .First())
            .ToArray();
    }

    private static IEnumerable<DateTime> EnumerateMonths(
        DateTime from,
        DateTime to)
    {
        var month = new DateTime(from.Year, from.Month, 1);
        var last = new DateTime(to.Year, to.Month, 1);

        while (month <= last)
        {
            yield return month;
            month = month.AddMonths(1);
        }
    }

    private void WarnIfEmpty(int sent, string symbol)
    {
        if (sent == 0)
        {
            this.AddWarningLog(
                "TPEx returned no daily observations for {0}.",
                symbol.IsEmpty("all selected securities"));
        }
    }

    private async Task CompleteSubscription(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }
}
