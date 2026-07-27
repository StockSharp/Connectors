namespace StockSharp.XbrlFilings;

public partial class XbrlFilingsMessageAdapter
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
            .IsEmpty(lookupMsg.Name)
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        if (value.IsEmpty())
        {
            throw new InvalidOperationException(
                "filings.xbrl.org entity lookup requires an exact " +
                "entity identifier or legal name.");
        }
        if (lookupMsg.Skip is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookupMsg.Skip),
                lookupMsg.Skip,
                "Entity lookup offset cannot be negative.");
        }

        var remaining = lookupMsg.Count ?? PageSize;
        var skip = lookupMsg.Skip ?? 0;
        var requestedTypes = lookupMsg.GetSecurityTypes();
        var pageNumber = checked((int)(skip / PageSize + 1));
        var innerSkip = checked((int)(skip % PageSize));

        for (var pages = 0;
            pages < MaxPages && remaining > 0;
            pages++, pageNumber++)
        {
            var document = await SafeClient().SearchEntities(
                value,
                pageNumber,
                PageSize,
                cancellationToken);
            var entities = document.Data
                .Where(entity =>
                    entity?.Attributes is not null &&
                    !entity.Attributes.Identifier.IsEmpty())
                .Skip(innerSkip)
                .Select(entity => entity.ToSecurityMessage(
                    lookupMsg.TransactionId))
                .Where(security =>
                    security.IsMatch(lookupMsg, requestedTypes))
                .Take(checked((int)Math.Min(
                    remaining, int.MaxValue)))
                .ToArray();
            innerSkip = 0;

            foreach (var security in entities)
            {
                await SendOutMessageAsync(
                    security, cancellationToken);
                remaining--;
            }

            if (document.Data.Length < PageSize ||
                document.Links?.Next.IsEmpty() != false)
            {
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
        if (mdMsg.From > mdMsg.To)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                mdMsg.From,
                "Filing history start time is after its end time.");
        }

        var entityIdentifier =
            (mdMsg.SecurityId.Native as string)
                .IsEmpty(mdMsg.SecurityId.SecurityCode)
                ?.Trim();
        if (!entityIdentifier.IsEmpty() &&
            !entityIdentifier.IsEntityIdentifier())
        {
            throw new InvalidOperationException(
                "News security must contain a filings.xbrl.org entity identifier.");
        }

        var capacity = checked(MaxPages * PageSize);
        var target = checked((int)Math.Min(
            mdMsg.Count ?? capacity,
            capacity));
        var values = new List<(
            XbrlFiling Filing,
            XbrlEntity Entity,
            DateTimeOffset Time)>();
        var ids = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var page = 1;
            page <= MaxPages && values.Count < target;
            page++)
        {
            var document = await SafeClient().GetFilings(
                entityIdentifier,
                Country,
                page,
                PageSize,
                cancellationToken);
            var entities = document.Included
                .Where(entity =>
                    entity is not null &&
                    !entity.Id.IsEmpty())
                .GroupBy(
                    entity => entity.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            var oldest = DateTimeOffset.MaxValue;

            foreach (var filing in document.Data ?? [])
            {
                var attributes = filing?.Attributes;
                var time = attributes?.Processed.ToXbrlTime() ??
                    attributes?.DateAdded.ToXbrlTime();
                if (time is null)
                    continue;
                if (time < oldest)
                    oldest = time.Value;
                if ((mdMsg.From is not null &&
                        time < mdMsg.From) ||
                    (mdMsg.To is not null &&
                        time > mdMsg.To))
                {
                    continue;
                }

                var id = attributes.FilingId
                    .IsEmpty(filing.Id);
                if (id.IsEmpty() || !ids.Add(id))
                    continue;

                entities.TryGetValue(
                    filing.Relationships?.Entity?.Data?.Id
                        .IsEmpty(string.Empty),
                    out var entity);
                values.Add((filing, entity, time.Value));
                if (values.Count >= target)
                    break;
            }

            if (document.Data.Length < PageSize ||
                document.Links?.Next.IsEmpty() != false ||
                (mdMsg.From is not null &&
                    oldest < mdMsg.From))
            {
                break;
            }
        }

        foreach (var value in values
            .OrderBy(value => value.Time)
            .Take(target))
        {
            await SendOutMessageAsync(
                value.Filing.ToNewsMessage(
                    value.Entity,
                    mdMsg.TransactionId,
                    PublicAddress),
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
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
