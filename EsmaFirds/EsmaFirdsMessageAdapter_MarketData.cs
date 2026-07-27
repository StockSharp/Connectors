namespace StockSharp.EsmaFirds;

public partial class EsmaFirdsMessageAdapter
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
        var mic = lookupMsg.SecurityId.BoardCode?.Trim();
        if (value.IsEmpty() && mic.IsEmpty())
        {
            throw new InvalidOperationException(
                "ESMA FIRDS lookup requires an ISIN, a name, or a MIC.");
        }
        if (lookupMsg.Skip is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookupMsg.Skip),
                lookupMsg.Skip,
                "ESMA FIRDS lookup offset cannot be negative.");
        }

        var requested = lookupMsg.Count ?? MaxResults;
        if (requested > 0)
        {
            var rows = checked((int)Math.Min(
                requested,
                MaxResults));
            var start = checked((int)Math.Min(
                lookupMsg.Skip ?? 0,
                int.MaxValue));
            var response = await SafeClient().SearchInstruments(
                new EsmaInstrumentSearch(
                    value,
                    mic,
                    _cfiCategories,
                    ActiveOnly,
                    start,
                    rows),
                cancellationToken);
            var requestedTypes = lookupMsg.GetSecurityTypes();
            var securities = (response.Documents ?? [])
                .Where(instrument =>
                    instrument is not null &&
                    instrument.Isin.IsIsin() &&
                    !instrument.Mic.IsEmpty())
                .GroupBy(
                    instrument =>
                        $"{instrument.Isin.Trim().ToUpperInvariant()}|" +
                        instrument.Mic.Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(instrument =>
                        instrument.PublicationDate.ToEsmaDate())
                    .First()
                    .ToSecurityMessage(lookupMsg.TransactionId))
                .Where(security =>
                    security.IsMatch(lookupMsg, requestedTypes))
                .OrderBy(
                    security => security.SecurityId.SecurityCode,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    security => security.SecurityId.BoardCode,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var security in securities)
            {
                await SendOutMessageAsync(
                    security, cancellationToken);
            }
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }
}
