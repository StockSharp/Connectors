namespace StockSharp.JpxTdnet;

public partial class JpxTdnetMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = lookupMsg.SecurityId.GetTdnetCode();
        var isCode = value.IsTdnetCode();
        var today = JpxTdnetExtensions.JapanToday();
        JpxTdnetDisclosure[] disclosures;

        if (isCode)
        {
            disclosures = (await SafeClient().GetIndex(
                value,
                null,
                null,
                JpxTdnetIndexModes.Current,
                cancellationToken)).Items;
        }
        else
        {
            disclosures = (await LoadIndexRange(
                null,
                today.AddDays(1 - SecurityLookupDays),
                today,
                JpxTdnetIndexModes.Current,
                cancellationToken)).ToArray();
        }

        var types = lookupMsg.GetSecurityTypes();
        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;
        if (left > 0)
        {
            var securities = disclosures
                .Where(item =>
                    item is not null &&
                    item.Code.IsTdnetCode() &&
                    item.Matches(value))
                .GroupBy(
                    item => item.Code,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item =>
                        $"{item.DisclosedDate} {item.DisclosedTime}")
                    .First())
                .Select(item => item.ToSecurityMessage(
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

        var rawCode = mdMsg.SecurityId.GetTdnetCode();
        var hasSecurity =
            mdMsg.SecurityId != Messages.SecurityId.News &&
            !rawCode.IsEmpty();
        if (hasSecurity && !rawCode.IsTdnetCode())
        {
            throw new ArgumentException(
                "JPX TDnet news security code must contain four or five alphanumeric characters.",
                nameof(mdMsg.SecurityId));
        }

        var today = JpxTdnetExtensions.JapanToday();
        var end = mdMsg.To?.ToJapanDate() ?? today;
        var start = mdMsg.From?.ToJapanDate() ??
            (mdMsg.To is null
                ? end
                : end.AddDays(1 - DefaultLookupDays));
        if (start > end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), start,
                "JPX TDnet disclosure start date is after its end date.");
        }
        if (end > today)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.To), end,
                "JPX TDnet disclosure dates cannot be in the future.");
        }
        if (start < today.AddYears(-5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), start,
                "JPX TDnet retains disclosure information for five years.");
        }

        var days = checked((end - start).Days + 1);
        if (days > MaxDays)
        {
            throw new InvalidOperationException(
                $"JPX TDnet subscription requests {days} days, exceeding the configured {MaxDays}-day limit.");
        }

        var disclosures = await LoadIndexRange(
            hasSecurity ? rawCode : null,
            start,
            end,
            IndexMode,
            cancellationToken);
        var target = mdMsg.Count is long count
            ? checked((int)Math.Min(count, int.MaxValue))
            : int.MaxValue;
        var dated = disclosures
            .Where(item =>
                item is not null &&
                item.Code.IsTdnetCode() &&
                !item.DisclosureNumber.IsEmpty())
            .GroupBy(
                item =>
                    $"{item.DisclosureNumber}:{item.ModifiedHistory}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item => new
            {
                Item = item,
                Time = item.TryToJapanUtc(out var time)
                    ? time
                    : (DateTime?)null,
            })
            .Where(item => item.Time is not null)
            .OrderBy(item => item.Time)
            .ThenBy(item => item.Item.DisclosureNumber)
            .ThenBy(item => item.Item.ModifiedHistory)
            .Take(target);

        foreach (var item in dated)
        {
            var disclosure = item.Item;
            var formats = disclosure.GetAvailableFormats();
            var story = string.Join(
                Environment.NewLine,
                new[]
                {
                    disclosure.Name.IsEmpty()
                        ? null
                        : $"Company: {disclosure.Name}",
                    $"Stock code: {disclosure.Code}",
                    $"Disclosure number: {disclosure.DisclosureNumber}",
                    disclosure.ModifiedHistory.IsEmpty()
                        ? null
                        : $"History number: {disclosure.ModifiedHistory}",
                    disclosure.HandlingType.IsEmpty()
                        ? null
                        : $"Handling: {disclosure.HandlingType}",
                    disclosure.DisclosureItems is
                        { Length: > 0 }
                            ? $"Public item codes: {string.Join(", ", disclosure.DisclosureItems)}"
                            : null,
                    formats.IsEmpty()
                        ? null
                        : $"Available formats: {formats}",
                }.Where(value => !value.IsEmpty()));

            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time.Value,
                    Id = disclosure.ModifiedHistory.IsEmpty()
                        ? disclosure.DisclosureNumber
                        : $"{disclosure.DisclosureNumber}:{disclosure.ModifiedHistory}",
                    BoardCode = BoardCodes.Tse,
                    SecurityId =
                        disclosure.Code.ToTdnetSecurityId(),
                    Source = "JPX TDnet",
                    Headline = disclosure.Title
                        .IsEmpty(disclosure.Name)
                        .IsEmpty(disclosure.DisclosureNumber),
                    Story = story,
                    Url = ViewerAddress.AbsoluteUri,
                    Priority = NewsPriorities.Regular,
                    Language = "ja",
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<List<JpxTdnetDisclosure>> LoadIndexRange(
        string code,
        DateTime from,
        DateTime to,
        JpxTdnetIndexModes mode,
        CancellationToken cancellationToken)
    {
        var result = new List<JpxTdnetDisclosure>();
        for (var start = from.Date; start <= to.Date;)
        {
            var end = start.AddMonths(1).AddDays(-1);
            if (end > to.Date)
                end = to.Date;

            await LoadIndexRange(
                code,
                start,
                end,
                mode,
                result,
                cancellationToken);
            start = end.AddDays(1);
        }

        return result;
    }

    private async Task LoadIndexRange(
        string code,
        DateTime from,
        DateTime to,
        JpxTdnetIndexModes mode,
        List<JpxTdnetDisclosure> result,
        CancellationToken cancellationToken)
    {
        var page = await SafeClient().GetIndex(
            code, from, to, mode, cancellationToken);
        if (page.IsPartial && from.Date < to.Date)
        {
            var middle = from.Date.AddDays(
                (to.Date - from.Date).Days / 2);
            await LoadIndexRange(
                code,
                from,
                middle,
                mode,
                result,
                cancellationToken);
            await LoadIndexRange(
                code,
                middle.AddDays(1),
                to,
                mode,
                result,
                cancellationToken);
            return;
        }

        result.AddRange(page.Items ?? []);
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
