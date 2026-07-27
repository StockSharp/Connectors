namespace StockSharp.Gleif;

static class GleifExtensions
{
    private static readonly Regex _leiRegex = new(
        "^[A-Z0-9]{18}[0-9]{2}$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly Regex _isinRegex = new(
        "^[A-Z]{2}[A-Z0-9]{9}[0-9]$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    public static bool IsLei(this string value)
        => !value.IsEmpty() && _leiRegex.IsMatch(value.Trim());

    public static bool IsIsin(this string value)
        => !value.IsEmpty() && _isinRegex.IsMatch(value.Trim());

    public static SecurityMessage ToSecurityMessage(
        this GleifLeiRecord record,
        string isin,
        long originalTransactionId)
    {
        var attributes = record.Attributes ??
            throw new InvalidOperationException(
                "GLEIF LEI record has no attributes.");
        var lei = attributes.Lei
            .IsEmpty(record.Id)
            .ThrowIfEmpty(nameof(attributes.Lei))
            .Trim()
            .ToUpperInvariant();
        isin = isin?.Trim().ToUpperInvariant();
        var entity = attributes.Entity;
        var name = entity?.LegalName?.Name
            .IsEmpty(lei);

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = isin.IsEmpty(lei),
                BoardCode = "GLEIF",
                Isin = isin.IsIsin() ? isin : null,
                Native = lei,
            },
            Name = name,
            ShortName = name,
            Class = entity?.Category,
            SecurityType = entity?.Category
                .EqualsIgnoreCase("FUND") == true
                    ? SecurityTypes.Fund
                    : SecurityTypes.Stock,
            IssueDate = entity?.CreationDate?.UtcDateTime,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }
}
