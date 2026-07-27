namespace StockSharp.OpenDart;

public partial class OpenDartMessageAdapter
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
            var securities = (await LoadCompanies(cancellationToken))
                .Where(company => company.Matches(value))
                .Select(company => company.ToSecurityMessage(
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
    protected override async ValueTask OnNewsSubscriptionAsync(
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

        var rawIdentity = (mdMsg.SecurityId.Native as string)
            .IsEmpty(mdMsg.SecurityId.SecurityCode);
        var hasSecurity =
            mdMsg.SecurityId != Messages.SecurityId.News &&
            !rawIdentity.IsEmpty();
        var company = !hasSecurity
            ? null
            : await ResolveCompany(
                mdMsg.SecurityId, cancellationToken);
        var end = mdMsg.To?.ToKoreaDate() ??
            OpenDartExtensions.KoreaToday();
        var start = mdMsg.From?.ToKoreaDate() ??
            (mdMsg.To is null
                ? end
                : end.AddMonths(-3).AddDays(1));

        if (start > end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), start,
                "Open DART disclosure start date is after its end date.");
        }

        var capacity = checked(MaxPages * 100);
        var target = checked((int)Math.Min(
            mdMsg.Count ?? 100,
            capacity));
        var values = new List<OpenDartDisclosure>();
        var receipts = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var pages = 0;

        foreach (var range in BuildDisclosureRanges(
            start, end, company is not null))
        {
            for (var pageNumber = 1;
                pages < MaxPages && values.Count < target;
                pageNumber++)
            {
                var page = await SafeClient().GetDisclosures(
                    new OpenDartDisclosureQuery(
                        company?.CorporationCode,
                        range.From,
                        range.To,
                        DisclosureType.ToApiCode(),
                        CorporationClass.ToApiCode(),
                        FinalReportsOnly,
                        pageNumber,
                        100),
                    cancellationToken);
                pages++;

                foreach (var item in page.Items ?? [])
                {
                    if (item is not null &&
                        !item.ReceiptNumber.IsEmpty() &&
                        receipts.Add(item.ReceiptNumber))
                    {
                        values.Add(item);
                        if (values.Count >= target)
                            break;
                    }
                }

                if (page.TotalPages <= pageNumber ||
                    page.Items is not { Length: 100 })
                {
                    break;
                }
            }

            if (pages >= MaxPages || values.Count >= target)
                break;
        }

        var dated = values
            .Select(item => new
            {
                Item = item,
                Time = item.ReceiptDate.TryToKoreaUtcDate(
                    out var time)
                        ? time
                        : (DateTime?)null,
            })
            .Where(item => item.Time is not null)
            .OrderBy(item => item.Time)
            .ThenBy(item => item.Item.ReceiptNumber)
            .Take(target);

        foreach (var item in dated)
        {
            var disclosure = item.Item;
            var securityId =
                disclosure.StockCode.IsStockCode() &&
                disclosure.CorporationCode.IsCorporationCode()
                    ? disclosure.StockCode.ToOpenDartSecurityId(
                        disclosure.CorporationCode)
                    : company?.ToSecurityId();
            var story = string.Join(
                Environment.NewLine,
                new[]
                {
                    disclosure.FilerName.IsEmpty()
                        ? null
                        : $"Filer: {disclosure.FilerName}",
                    disclosure.Note.IsEmpty()
                        ? null
                        : $"Note: {disclosure.Note}",
                }.Where(value => !value.IsEmpty()));

            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time.Value,
                    Id = disclosure.ReceiptNumber,
                    BoardCode = BoardCodes.Krx,
                    SecurityId = securityId,
                    Source = "Open DART",
                    Headline = disclosure.ReportName
                        .IsEmpty(disclosure.CorporationName)
                        .IsEmpty(disclosure.ReceiptNumber),
                    Story = story,
                    Url = disclosure.ReceiptNumber.ToDisclosureUrl(
                        DisclosureAddress),
                    Priority = NewsPriorities.Regular,
                    Language = "en",
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
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

        var company = await ResolveCompany(
            mdMsg.SecurityId, cancellationToken);
        var from = mdMsg.From?.ToKoreaDate();
        var to = mdMsg.To?.ToKoreaDate();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), from,
                "Open DART financial start date is after its end date.");
        }

        var explicitRange = from is not null;
        int[] years;
        if (explicitRange)
        {
            if (from.Value.Year < 2022)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mdMsg.From), from,
                    "Open DART financial indicators are available from fiscal year 2022.");
            }

            var endYear = (to ??
                OpenDartExtensions.KoreaToday()).Year;
            if (endYear - from.Value.Year + 1 >
                FinancialSearchYears)
            {
                throw new InvalidOperationException(
                    "Open DART financial history exceeds the configured year limit.");
            }

            years = Enumerable
                .Range(
                    from.Value.Year,
                    endYear - from.Value.Year + 1)
                .ToArray();
        }
        else
        {
            var currentYear =
                OpenDartExtensions.KoreaToday().Year;
            var startYear = BusinessYear ??
                to?.Year ??
                (ReportType == OpenDartReportTypes.Annual
                    ? currentYear - 1
                    : currentYear);
            years = Enumerable
                .Range(0, FinancialSearchYears)
                .Select(offset => startYear - offset)
                .Where(year => year >= 2022)
                .ToArray();
        }

        var requested = checked((int)Math.Min(
            mdMsg.Count ?? (explicitRange
                ? years.Length
                : 1),
            int.MaxValue));
        var messages = new List<Level1ChangeMessage>();

        foreach (var year in years)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indicators = await LoadIndicators(
                company.CorporationCode,
                year,
                cancellationToken);
            var message = CreateFinancialMessage(
                mdMsg.TransactionId,
                company,
                year,
                indicators);
            if (message is null)
                continue;

            messages.Add(message);
            if (!explicitRange || messages.Count >= requested)
                break;
        }

        foreach (var message in messages
            .OrderBy(message => message.ServerTime)
            .Take(requested))
        {
            await SendOutMessageAsync(
                message, cancellationToken);
        }

        if (messages.Count == 0)
        {
            this.AddWarningLog(
                "Open DART returned no mapped {0} financial indicators for {1}.",
                ReportType, company.StockCode);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<OpenDartFinancialIndicator[]>
        LoadIndicators(
            string corporationCode,
            int businessYear,
            CancellationToken cancellationToken)
    {
        var result = new List<OpenDartFinancialIndicator>();

        foreach (var category in _indicatorCategories)
        {
            result.AddRange(
                await SafeClient().GetIndicators(
                    corporationCode,
                    businessYear,
                    ReportType.ToApiCode(),
                    category,
                    cancellationToken));
        }

        return result.ToArray();
    }

    private static Level1ChangeMessage CreateFinancialMessage(
        long originalTransactionId,
        OpenDartCompanyCode company,
        int businessYear,
        OpenDartFinancialIndicator[] indicators)
    {
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = company.ToSecurityId(),
            ServerTime = indicators
                .Select(indicator =>
                    indicator.SettlementDate.ToSettlementDate())
                .Where(date => date is not null)
                .Select(date => date.Value)
                .DefaultIfEmpty(
                    new DateTime(
                        businessYear, 12, 31).ToKoreaUtc())
                .Max(),
        };

        foreach (var indicator in indicators)
        {
            if (indicator.TryGetLevel1Field(out var field) &&
                indicator.IndicatorValue.ToDecimal() is decimal value)
            {
                message.Changes[field] = value;
            }
        }

        return message.Changes.Count == 0
            ? null
            : message;
    }

    private static IEnumerable<(DateTime From, DateTime To)>
        BuildDisclosureRanges(
            DateTime from,
            DateTime to,
            bool hasCorporation)
    {
        if (hasCorporation)
        {
            yield return (from.Date, to.Date);
            yield break;
        }

        for (var rangeEnd = to.Date;
            rangeEnd >= from.Date;)
        {
            var rangeStart = rangeEnd
                .AddMonths(-3)
                .AddDays(1);
            if (rangeStart < from.Date)
                rangeStart = from.Date;

            yield return (rangeStart, rangeEnd);
            rangeEnd = rangeStart.AddDays(-1);
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
