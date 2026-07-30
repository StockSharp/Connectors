namespace StockSharp.Comdirect;

public partial class ComdirectMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        var query = message.SecurityId.SecurityCode;
        if (query.IsEmpty())
        {
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }

        var instrument = await GetInstrument(query, cancellationToken);
        if (instrument is null)
        {
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }

        var type = instrument.StaticData?.InstrumentType.ToSecurityType() ??
            SecurityTypes.Stock;
        var securityTypes = message.GetSecurityTypes();
        var venues = instrument.OrderDimensions?.Venues ?? [];
        if (venues.Length == 0)
        {
            venues =
            [
                new()
                {
                    VenueId = message.SecurityId.BoardCode
                        .IsEmpty("COMDIRECT"),
                    Name = "comdirect BestEx",
                    Currencies = [instrument.StaticData?.Currency
                        .IsEmpty(DefaultCurrency)],
                },
            ];
        }

        var left = message.Count ?? long.MaxValue;

        foreach (var venue in venues)
        {
            if (left <= 0)
                break;
            if (!message.SecurityId.BoardCode.IsEmpty() &&
                !message.SecurityId.BoardCode.EqualsIgnoreCase(
                    venue.VenueId) &&
                !message.SecurityId.BoardCode.EqualsIgnoreCase(venue.Name))
                continue;

            var securityId = instrument.ToSecurityId(venue.VenueId);
            var security = new SecurityMessage
            {
                OriginalTransactionId = message.TransactionId,
                SecurityId = securityId,
                Name = instrument.Name,
                ShortName = instrument.ShortName,
                SecurityType = type,
                Currency = (venue.Currencies?.FirstOrDefault())
                    .IsEmpty(instrument.StaticData?.Currency)
                    .ToCurrency(),
                VolumeStep = 1,
                MinVolume = 1,
                ExpiryDate =
                    instrument.DerivativeData?.ExpiryDate.ParseDate() ??
                    instrument.DerivativeData?.MaturityDate.ParseDate(),
                Strike = instrument.DerivativeData?.StrikePrice.ToDecimal(),
            };

            var underlying =
                instrument.DerivativeData?.UnderlyingInstrument;
            if (underlying is not null)
                security.UnderlyingSecurityId =
                    underlying.ToSecurityId();

            if (!security.IsMatch(message, securityTypes))
                continue;

            await SendOutMessageAsync(security, cancellationToken);
            left--;
        }

        await SendSubscriptionResultAsync(message, cancellationToken);
    }
}
