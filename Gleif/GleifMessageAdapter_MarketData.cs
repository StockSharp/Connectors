namespace StockSharp.Gleif;

public partial class GleifMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = (lookupMsg.SecurityId.Native as string)
            .IsEmpty(lookupMsg.SecurityId.Isin)
            .IsEmpty(lookupMsg.SecurityId.SecurityCode)
            .IsEmpty(lookupMsg.Name)
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        if (value.IsEmpty())
            throw new InvalidOperationException(
                "GLEIF lookup requires an LEI, ISIN, or entity name.");
        if (lookupMsg.Skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));

        var remaining = lookupMsg.Count ?? PageSize;
        var skip = lookupMsg.Skip ?? 0;
        var page = checked((int)(skip / PageSize + 1));
        var innerSkip = checked((int)(skip % PageSize));
        var types = lookupMsg.GetSecurityTypes();

        for (var pages = 0;
            pages < MaxPages && remaining > 0;
            pages++, page++)
        {
            var document = await SafeClient().Search(
                value,
                ActiveOnly,
                page,
                PageSize,
                cancellationToken);
            foreach (var record in document.Data.Skip(innerSkip))
            {
                innerSkip = 0;
                var message = record.ToSecurityMessage(
                    value.IsIsin() ? value : null,
                    lookupMsg.TransactionId);
                if (message.IsMatch(lookupMsg, types))
                {
                    await SendOutMessageAsync(
                        message, cancellationToken);
                    if (--remaining <= 0)
                        break;
                }

                if (!ExpandIsins || value.IsIsin())
                    continue;
                for (var isinPage = 1;
                    isinPage <= MaxPages && remaining > 0;
                    isinPage++)
                {
                    var mappings = await SafeClient().GetIsins(
                        record.Attributes.Lei,
                        isinPage,
                        PageSize,
                        cancellationToken);
                    foreach (var mapping in mappings.Data)
                    {
                        var isin = mapping?.Attributes?.Isin;
                        if (!isin.IsIsin())
                            continue;
                        var mapped = record.ToSecurityMessage(
                            isin, lookupMsg.TransactionId);
                        if (!mapped.IsMatch(lookupMsg, types))
                            continue;
                        await SendOutMessageAsync(
                            mapped, cancellationToken);
                        if (--remaining <= 0)
                            break;
                    }
                    if (mappings.Links?.Next.IsEmpty() != false)
                        break;
                }
            }

            if (document.Links?.Next.IsEmpty() != false)
                break;
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }
}
