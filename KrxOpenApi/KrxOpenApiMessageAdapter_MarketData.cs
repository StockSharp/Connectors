namespace StockSharp.KrxOpenApi;

public partial class KrxOpenApiMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = (lookupMsg.SecurityId.Native as string)
            .IsEmpty(lookupMsg.SecurityId.SecurityCode)
            ?.Trim();
        var types = lookupMsg.GetSecurityTypes();
        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;

        if (left > 0)
        {
            IEnumerable<SecurityMessage> securities;
            if (DataSet.IsStock())
            {
                var rows = await LoadLatestSecurityInfo(
                    cancellationToken);
                securities = rows
                    .Where(row => row.Matches(value))
                    .Select(row => row.ToSecurityMessage(
                        lookupMsg.TransactionId, DataSet));
            }
            else
            {
                var rows = await LoadLatestDailyRows(
                    cancellationToken);
                securities = rows
                    .Where(row => row.Matches(value))
                    .Select(row => row.ToSecurityMessage(
                        lookupMsg.TransactionId));
            }

            foreach (var security in securities
                .Where(security =>
                    security.IsMatch(lookupMsg, types))
                .GroupBy(
                    security => security.SecurityId.SecurityCode,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(
                    security => security.SecurityId.SecurityCode,
                    StringComparer.OrdinalIgnoreCase))
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

        var rows = await LoadHistory(
            mdMsg, cancellationToken);
        var securityId = NormalizeSecurityId(mdMsg.SecurityId);
        var sent = 0;

        foreach (var row in rows)
        {
            await SendOutMessageAsync(
                new Level1ChangeMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = securityId,
                    ServerTime = row.Date,
                }
                .TryAdd(
                    Level1Fields.OpenPrice,
                    row.OpenPrice)
                .TryAdd(
                    Level1Fields.HighPrice,
                    row.HighPrice)
                .TryAdd(
                    Level1Fields.LowPrice,
                    row.LowPrice)
                .TryAdd(
                    Level1Fields.ClosePrice,
                    row.ClosePrice)
                .TryAdd(
                    Level1Fields.LastTradePrice,
                    row.ClosePrice)
                .TryAdd(
                    Level1Fields.MarketPriceYesterday,
                    row.PreviousClosePrice)
                .TryAdd(
                    Level1Fields.Change,
                    row.ChangePercent)
                .TryAdd(
                    Level1Fields.Volume,
                    row.Volume)
                .TryAdd(
                    Level1Fields.Turnover,
                    row.Turnover)
                .TryAdd(
                    Level1Fields.SharesOutstanding,
                    row.ListedShares)
                .TryAdd(
                    Level1Fields.TheorPrice,
                    row.IndicativeValue),
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetKrxSymbol());
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
                "KRX Open API provides only native daily candles.");
        }

        var rows = await LoadHistory(
            mdMsg, cancellationToken);
        var securityId = NormalizeSecurityId(mdMsg.SecurityId);
        var sent = 0;

        foreach (var row in rows.Where(row => row.HasOhlc))
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = securityId,
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = row.Date,
                    OpenPrice = row.OpenPrice.Value,
                    HighPrice = row.HighPrice.Value,
                    LowPrice = row.LowPrice.Value,
                    ClosePrice = row.ClosePrice.Value,
                    TotalVolume = row.Volume ?? 0,
                    State = CandleStates.Finished,
                },
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetKrxSymbol());
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<KrxSecurityInfoRow[]>
        LoadLatestSecurityInfo(
            CancellationToken cancellationToken)
    {
        var path = DataSet.ToReferencePath()
            .ThrowIfEmpty(nameof(DataSet));
        return await LoadLatest(
            date => SafeClient().Get<KrxSecurityInfoRow>(
                path, date, cancellationToken),
            cancellationToken);
    }

    private Task<KrxDailyRecord[]> LoadLatestDailyRows(
        CancellationToken cancellationToken)
        => LoadLatest(
            date => GetDailyRows(date, cancellationToken),
            cancellationToken);

    private async Task<T[]> LoadLatest<T>(
        Func<DateTime, Task<T[]>> loader,
        CancellationToken cancellationToken)
    {
        var date = GetReferenceDate();
        var requests = 0;

        for (var days = 0;
            days < LatestSearchDays && requests < MaxRequests;
            days++, date = date.AddDays(-1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWeekday(date))
                continue;

            requests++;
            var rows = await loader(date);
            if (rows is { Length: > 0 })
                return rows;
        }

        return [];
    }

    private async Task<KrxDailyRecord[]> LoadHistory(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var symbol = mdMsg.SecurityId.GetKrxSymbol()
            .ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
        var from = mdMsg.From?.ToKoreaDate();
        var to = mdMsg.To?.ToKoreaDate();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), from,
                "KRX history start date is after the end date.");
        }

        var requested = mdMsg.Count is long count
            ? checked((int)Math.Min(count.Max(0), int.MaxValue))
            : (int?)null;
        var result = new List<KrxDailyRecord>();

        if (from is not null)
        {
            var end = to ?? GetReferenceDate();
            if (from.Value > end)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mdMsg.From), from,
                    "KRX history start date is after the effective end date.");
            }

            var dates = EnumerateDates(
                from.Value, end, ascending: true).ToArray();
            if (dates.Length > MaxRequests)
            {
                throw new InvalidOperationException(
                    $"KRX history requires {dates.Length} daily requests, exceeding the configured limit of {MaxRequests}.");
            }

            foreach (var date in dates)
            {
                var row = (await GetDailyRows(
                        date, cancellationToken))
                    .FirstOrDefault(item =>
                        item.Symbol.EqualsIgnoreCase(symbol));
                if (row is not null)
                    result.Add(row);
                if (requested is int limit &&
                    result.Count >= limit)
                {
                    break;
                }
            }
        }
        else
        {
            var desired = requested ?? 1;
            var start = to ?? GetReferenceDate();
            var requests = 0;
            var calendarLimit = Math.Max(
                LatestSearchDays, MaxRequests * 3);

            foreach (var date in EnumerateDates(
                start,
                start.AddDays(-calendarLimit),
                ascending: false))
            {
                if (requests >= MaxRequests)
                    break;
                if (desired <= 1 &&
                    (start - date).TotalDays >= LatestSearchDays)
                {
                    break;
                }

                requests++;
                var row = (await GetDailyRows(
                        date, cancellationToken))
                    .FirstOrDefault(item =>
                        item.Symbol.EqualsIgnoreCase(symbol));
                if (row is not null)
                    result.Add(row);
                if (result.Count >= desired)
                    break;
            }
        }

        return result
            .OrderBy(row => row.Date)
            .ToArray();
    }

    private async Task<KrxDailyRecord[]> GetDailyRows(
        DateTime date,
        CancellationToken cancellationToken)
        => (await SafeClient().Get<KrxDailyRow>(
                DataSet.ToDailyPath(),
                date,
                cancellationToken))
            .Select(row => row.ToRecord(DataSet))
            .Where(row => !row.Symbol.IsEmpty())
            .ToArray();

    private IEnumerable<DateTime> EnumerateDates(
        DateTime start,
        DateTime end,
        bool ascending)
    {
        var step = ascending ? 1 : -1;

        for (var date = start.Date;
            ascending ? date <= end.Date : date >= end.Date;
            date = date.AddDays(step))
        {
            if (IsWeekday(date))
                yield return date;
        }
    }

    private DateTime GetReferenceDate()
        => ReferenceDate?.Date ??
            (IsDemo
                ? new DateTime(2020, 4, 14)
                : KrxExtensions.KoreaToday().AddDays(-1));

    private static bool IsWeekday(DateTime date)
        => date.DayOfWeek is not
            DayOfWeek.Saturday and not
            DayOfWeek.Sunday;

    private static SecurityId NormalizeSecurityId(
        SecurityId securityId)
    {
        var symbol = securityId.GetKrxSymbol()
            .ThrowIfEmpty(nameof(securityId.SecurityCode));
        if (securityId.SecurityCode.IsEmpty() ||
            securityId.BoardCode.IsEmpty())
        {
            return symbol.ToKrxSecurityId(securityId.Isin);
        }

        return securityId;
    }

    private void WarnIfEmpty(int sent, string symbol)
    {
        if (sent == 0)
        {
            this.AddWarningLog(
                "KRX returned no {0} daily observations for {1}.",
                DataSet, symbol);
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
