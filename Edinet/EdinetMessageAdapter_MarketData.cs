namespace StockSharp.Edinet;

public partial class EdinetMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = lookupMsg.SecurityId.GetEdinetIdentity();
        var types = lookupMsg.GetSecurityTypes();
        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;

        if (left > 0)
        {
            var securities = (await LoadCompanies(cancellationToken))
                .Where(company =>
                    (!ListedOnly || company.IsListed()) &&
                    company.Matches(value))
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

        var rawIdentity = mdMsg.SecurityId.GetEdinetIdentity();
        var hasSecurity =
            mdMsg.SecurityId != Messages.SecurityId.News &&
            !rawIdentity.IsEmpty();
        var company = !hasSecurity
            ? null
            : await ResolveCompany(
                mdMsg.SecurityId, cancellationToken);

        var today = EdinetExtensions.JapanToday();
        var end = mdMsg.To?.ToJapanDate() ?? today;
        var start = mdMsg.From?.ToJapanDate() ??
            (mdMsg.To is null
                ? end
                : end.AddDays(1 - DefaultLookupDays));

        if (start > end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), start,
                "EDINET disclosure start date is after its end date.");
        }
        if (end > today)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.To), end,
                "EDINET document-list dates cannot be in the future.");
        }
        if (start < today.AddYears(-10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), start,
                "EDINET document-list history is limited to the rolling ten-year availability period.");
        }

        var days = checked((end - start).Days + 1);
        if (days > MaxDays)
        {
            throw new InvalidOperationException(
                $"EDINET subscription requests {days} days, exceeding the configured {MaxDays}-day limit.");
        }

        var target = mdMsg.Count is long count
            ? checked((int)Math.Min(count, int.MaxValue))
            : int.MaxValue;
        var companies = await LoadCompanies(cancellationToken);
        var byEdinetCode = companies.ToDictionary(
            item => item.EdinetCode,
            StringComparer.OrdinalIgnoreCase);
        var byTicker = companies
            .Where(item => item.IsListed())
            .GroupBy(
                item => item.SecuritiesCode.ToEdinetTickerOrNull(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var documents = new List<EdinetDocument>();
        var identifiers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var daily = await SafeClient().GetDocuments(
                date, cancellationToken);

            foreach (var document in daily ?? [])
            {
                if (document is null ||
                    document.DocumentId.IsEmpty() ||
                    !identifiers.Add(document.DocumentId) ||
                    !document.Matches(DisclosureType) ||
                    !document.Matches(company) ||
                    (ListedOnly &&
                        !document.SecuritiesCode
                            .IsEdinetSecuritiesCode() &&
                        ResolveNewsCompany(
                            document,
                            byEdinetCode,
                            byTicker)?.IsListed() != true) ||
                    (!IncludeWithdrawn &&
                        document.WithdrawalStatus != "0") ||
                    (!IncludeUnavailable &&
                        (document.DisclosureStatus != "0" ||
                         !document.IsAvailable())))
                {
                    continue;
                }

                documents.Add(document);
            }

            if (date < end && RequestInterval > TimeSpan.Zero)
            {
                await Task.Delay(
                    RequestInterval, cancellationToken);
            }
        }

        var dated = documents
            .Select(document => new
            {
                Document = document,
                Time = document.SubmittedAt.TryToJapanUtc(
                    out var time)
                        ? time
                        : (DateTime?)null,
            })
            .Where(item => item.Time is not null)
            .OrderBy(item => item.Time)
            .ThenBy(item => item.Document.SequenceNumber)
            .Take(target);

        foreach (var item in dated)
        {
            var document = item.Document;
            var relatedCompany = ResolveNewsCompany(
                document, byEdinetCode, byTicker);
            var securityId = document.SecuritiesCode
                    .IsEdinetSecuritiesCode()
                ? new SecurityId
                {
                    SecurityCode = document.SecuritiesCode[..4],
                    BoardCode = BoardCodes.Tse,
                    Native = document.EdinetCode,
                }
                : relatedCompany?.ToSecurityId();
            var formats = document.GetAvailableFormats();
            var story = string.Join(
                Environment.NewLine,
                new[]
                {
                    document.FilerName.IsEmpty()
                        ? null
                        : $"Filer: {document.FilerName}",
                    document.EdinetCode.IsEmpty()
                        ? null
                        : $"EDINET code: {document.EdinetCode}",
                    document.SecuritiesCode.IsEmpty()
                        ? null
                        : $"Securities code: {document.SecuritiesCode}",
                    document.PeriodStart.IsEmpty() &&
                        document.PeriodEnd.IsEmpty()
                            ? null
                            : $"Period: {document.PeriodStart.IsEmpty("?")} - {document.PeriodEnd.IsEmpty("?")}",
                    $"Document ID: {document.DocumentId}",
                    document.CurrentReportReason.IsEmpty()
                        ? null
                        : $"Reason: {document.CurrentReportReason}",
                    formats.IsEmpty()
                        ? null
                        : $"Available formats: {formats}",
                }.Where(value => !value.IsEmpty()));

            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time.Value,
                    Id = document.DocumentId,
                    BoardCode = BoardCodes.Tse,
                    SecurityId = securityId,
                    Source = "EDINET",
                    Headline = document.Description
                        .IsEmpty(document.FilerName)
                        .IsEmpty(document.DocumentId),
                    Story = story,
                    Url = ViewerAddress.AbsoluteUri,
                    Priority = NewsPriorities.Regular,
                    Language = "ja",
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private static EdinetCompany ResolveNewsCompany(
        EdinetDocument document,
        IReadOnlyDictionary<string, EdinetCompany> byEdinetCode,
        IReadOnlyDictionary<string, EdinetCompany> byTicker)
    {
        var ticker =
            document.SecuritiesCode.ToEdinetTickerOrNull();
        if (!ticker.IsEmpty() &&
            byTicker.TryGetValue(ticker, out var company))
        {
            return company;
        }

        var codes = new[]
        {
            document.EdinetCode,
            document.IssuerEdinetCode,
            document.SubjectEdinetCode,
            document.SubsidiaryEdinetCode,
        }.Where(code => !code.IsEmpty()).ToArray();

        foreach (var code in codes)
        {
            if (byEdinetCode.TryGetValue(
                    code, out company) &&
                company.IsListed())
            {
                return company;
            }
        }

        foreach (var code in codes)
        {
            if (byEdinetCode.TryGetValue(
                code, out company))
            {
                return company;
            }
        }

        return null;
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
