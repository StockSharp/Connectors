namespace StockSharp.KoreanFsc;

public partial class KoreanFscMessageAdapter
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
        var name = lookupMsg.Name
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var isin = lookupMsg.SecurityId.Isin?.Trim();
        var types = lookupMsg.GetSecurityTypes();
        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;

        if (left > 0)
        {
            var rows = await LoadLatestRows(
                value,
                name,
                isin,
                cancellationToken);
            var securities = rows
                .Where(row => row.Matches(value, name))
                .Select(row => row.ToSecurityMessage(
                    lookupMsg.TransactionId, DataSet))
                .Where(security =>
                    security.IsMatch(lookupMsg, types))
                .GroupBy(
                    security => security.SecurityId.SecurityCode,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
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

        var rows = await LoadHistory(
            mdMsg, cancellationToken);
        var sent = 0;

        foreach (var row in rows)
        {
            await SendOutMessageAsync(
                new Level1ChangeMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = row.SecurityId,
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
                    row.ListedCount)
                .TryAdd(
                    Level1Fields.IssueSize,
                    row.ListedCount),
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetKoreanFscSymbol());
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
                "Korean FSC provides only native daily candles.");
        }

        var rows = await LoadHistory(
            mdMsg, cancellationToken);
        var sent = 0;

        foreach (var row in rows.Where(row => row.HasOhlc))
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = row.SecurityId,
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

        WarnIfEmpty(sent, mdMsg.SecurityId.GetKoreanFscSymbol());
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<KoreanFscPriceRow[]> LoadLatestRows(
        string symbol,
        string name,
        string isin,
        CancellationToken cancellationToken)
    {
        var date = GetReferenceDate();

        for (var days = 0;
            days < LatestSearchDays;
            days++, date = date.AddDays(-1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWeekday(date))
                continue;

            var rows = await LoadRows(
                new KoreanFscQuery(
                    date,
                    null,
                    null,
                    symbol,
                    name,
                    isin,
                    Market.ToApiCode()),
                cancellationToken);
            if (rows.Length > 0)
                return rows;
        }

        return [];
    }

    private async Task<KoreanFscDailyRecord[]> LoadHistory(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var symbol = mdMsg.SecurityId.GetKoreanFscSymbol()
            .ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
        var from = mdMsg.From?.ToKoreaDate();
        var to = mdMsg.To?.ToKoreaDate();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), from,
                "Korean FSC history start date is after its end date.");
        }

        KoreanFscPriceRow[] rows;
        if (from is not null)
        {
            var end = to ?? GetReferenceDate();
            if (from.Value > end)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mdMsg.From), from,
                    "Korean FSC history start date is after its effective end date.");
            }

            rows = await LoadRows(
                new KoreanFscQuery(
                    null,
                    from.Value,
                    end.AddDays(1),
                    symbol,
                    null,
                    mdMsg.SecurityId.Isin,
                    Market.ToApiCode()),
                cancellationToken);
        }
        else if (to is not null)
        {
            rows = await LoadRows(
                new KoreanFscQuery(
                    to,
                    null,
                    null,
                    symbol,
                    null,
                    mdMsg.SecurityId.Isin,
                    Market.ToApiCode()),
                cancellationToken);
        }
        else
        {
            rows = await LoadLatestRows(
                symbol,
                null,
                mdMsg.SecurityId.Isin,
                cancellationToken);
        }

        var requested = mdMsg.Count is long count
            ? checked((int)Math.Min(count.Max(0), int.MaxValue))
            : int.MaxValue;

        return rows
            .Where(row =>
                row.ShortCode.EqualsIgnoreCase(symbol))
            .Select(row => row.ToRecord(DataSet))
            .GroupBy(record => record.Date)
            .Select(group => group.First())
            .OrderBy(record => record.Date)
            .Take(requested)
            .ToArray();
    }

    private async Task<KoreanFscPriceRow[]> LoadRows(
        KoreanFscQuery query,
        CancellationToken cancellationToken)
    {
        var result = new List<KoreanFscPriceRow>();
        var totalCount = int.MaxValue;

        for (var pageNumber = 1;
            pageNumber <= MaxPages;
            pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await SafeClient().GetPage(
                DataSet,
                pageNumber,
                PageSize,
                query,
                cancellationToken);
            totalCount = page.TotalCount;
            result.AddRange(page.Items ?? []);

            if (result.Count >= totalCount ||
                page.Items is not { Length: > 0 } ||
                page.Items.Length < PageSize)
            {
                return result.ToArray();
            }
        }

        if (result.Count < totalCount)
        {
            throw new InvalidOperationException(
                $"Korean FSC response requires more than the configured {MaxPages} pages.");
        }

        return result.ToArray();
    }

    private DateTime GetReferenceDate()
        => ReferenceDate?.Date ??
            KoreanFscExtensions.KoreaToday().AddDays(-1);

    private static bool IsWeekday(DateTime date)
        => date.DayOfWeek is not
            DayOfWeek.Saturday and not
            DayOfWeek.Sunday;

    private void WarnIfEmpty(int sent, string symbol)
    {
        if (sent == 0)
        {
            this.AddWarningLog(
                "Korean FSC returned no {0} daily observations for {1}.",
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
