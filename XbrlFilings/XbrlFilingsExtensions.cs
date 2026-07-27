namespace StockSharp.XbrlFilings;

static class XbrlFilingsExtensions
{
    private static readonly Regex _identifierRegex = new(
        "^[A-Z0-9_.:-]{4,64}$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    public static bool IsEntityIdentifier(this string value)
        => !value.IsEmpty() &&
            _identifierRegex.IsMatch(value.Trim());

    public static DateTimeOffset? ToXbrlTime(this string value)
    {
        if (value.IsEmpty())
            return null;

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out var time))
        {
            return time;
        }

        return null;
    }

    public static SecurityMessage ToSecurityMessage(
        this XbrlEntity entity,
        long originalTransactionId)
    {
        var attributes = entity.Attributes ??
            throw new InvalidOperationException(
                "filings.xbrl.org entity has no attributes.");
        var identifier = attributes.Identifier
            .ThrowIfEmpty(nameof(attributes.Identifier))
            .Trim()
            .ToUpperInvariant();

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = identifier,
                BoardCode = "XBRL",
                Native = entity.Id.IsEmpty(identifier),
            },
            Name = attributes.Name.IsEmpty(identifier).Trim(),
            ShortName = attributes.Name.IsEmpty(identifier).Trim(),
            SecurityType = SecurityTypes.Stock,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static NewsMessage ToNewsMessage(
        this XbrlFiling filing,
        XbrlEntity entity,
        long originalTransactionId,
        Uri publicAddress)
    {
        var attributes = filing.Attributes ??
            throw new InvalidOperationException(
                "filings.xbrl.org filing has no attributes.");
        var entityAttributes = entity?.Attributes;
        var identifier = entityAttributes?.Identifier?
            .Trim()
            .ToUpperInvariant();
        var country = attributes.Country?
            .Trim()
            .ToUpperInvariant();
        var filingId = attributes.FilingId
            .IsEmpty(filing.Id)
            .ThrowIfEmpty(nameof(attributes.FilingId));
        var period = attributes.PeriodEnd.IsEmpty("unknown period");
        var name = entityAttributes?.Name
            .IsEmpty(identifier)
            .IsEmpty("Unknown entity");
        var jsonUrl = ResolvePublicUrl(
            publicAddress, attributes.JsonUrl);

        var story = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Entity identifier: {identifier.IsEmpty("unknown")}",
                $"Period end: {period}",
                $"Validation errors: {attributes.ErrorCount}",
                $"Validation warnings: {attributes.WarningCount}",
                $"Calculation inconsistencies: {attributes.InconsistencyCount}",
                jsonUrl is null
                    ? null
                    : $"xBRL-JSON: {jsonUrl}",
                attributes.Sha256.IsEmpty()
                    ? null
                    : $"SHA-256: {attributes.Sha256}",
            }.Where(value => !value.IsEmpty()));

        return new NewsMessage
        {
            OriginalTransactionId = originalTransactionId,
            ServerTime = (attributes.Processed.ToXbrlTime() ??
                attributes.DateAdded.ToXbrlTime() ??
                DateTimeOffset.UtcNow).UtcDateTime,
            Id = filingId,
            BoardCode = country.IsEmpty("XBRL"),
            SecurityId = identifier.IsEmpty()
                ? null
                : new SecurityId
                {
                    SecurityCode = identifier,
                    BoardCode = "XBRL",
                    Native = entity.Id.IsEmpty(identifier),
                },
            Source = "filings.xbrl.org",
            Headline = $"{name} — XBRL filing for {period}",
            Story = story,
            Url = ResolvePublicUrl(
                publicAddress,
                attributes.ViewerUrl
                    .IsEmpty(attributes.ReportUrl))
                ?.AbsoluteUri,
            Priority = attributes.ErrorCount > 0
                ? NewsPriorities.High
                : NewsPriorities.Regular,
            Language = "en",
        };
    }

    public static Uri ResolvePublicUrl(
        Uri publicAddress,
        string value)
    {
        if (value.IsEmpty())
            return null;
        if (Uri.TryCreate(
            value,
            UriKind.Absolute,
            out var absolute))
        {
            return absolute.Scheme == Uri.UriSchemeHttps
                ? absolute
                : null;
        }

        return new Uri(
            publicAddress ??
                throw new ArgumentNullException(nameof(publicAddress)),
            value);
    }
}
