namespace StockSharp.EsmaFirds;

static class EsmaFirdsExtensions
{
    private static readonly Regex _isinRegex = new(
        "^[A-Z]{2}[A-Z0-9]{9}[0-9]$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    public static bool IsIsin(this string value)
        => !value.IsEmpty() &&
            _isinRegex.IsMatch(value.Trim());

    public static string EscapeSolr(this string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            return null;

        var result = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            if (character is
                '+' or '-' or '!' or '(' or ')' or '{' or '}' or
                '[' or ']' or '^' or '"' or '~' or '*' or '?' or
                ':' or '\\' or '/')
            {
                result.Append('\\');
            }

            result.Append(character);
        }

        return result.ToString();
    }

    public static string ToSolrQuery(this string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            return "*:*";
        if (value.IsIsin())
            return $"isin:\"{value.ToUpperInvariant()}\"";

        var terms = value
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(term => term.EscapeSolr())
            .Where(term => !term.IsEmpty())
            .Take(8)
            .ToArray();
        if (terms.Length == 0)
            return "*:*";

        return string.Join(
            " AND ",
            terms.Select(term =>
                $"(gnr_full_name:{term} OR " +
                $"gnr_short_name:{term} OR isin:{term})"));
    }

    public static SecurityTypes ToSecurityType(
        this string cfiCode)
    {
        cfiCode = cfiCode?.Trim().ToUpperInvariant();
        if (cfiCode.IsEmpty())
            return SecurityTypes.Stock;

        return cfiCode[0] switch
        {
            'E' => SecurityTypes.Stock,
            'C' when cfiCode.Length > 1 &&
                cfiCode[1] == 'E' => SecurityTypes.Etf,
            'C' => SecurityTypes.Fund,
            'D' => SecurityTypes.Bond,
            'R' => SecurityTypes.Warrant,
            'O' => SecurityTypes.Option,
            'F' => SecurityTypes.Future,
            'I' => SecurityTypes.Index,
            _ => SecurityTypes.Stock,
        };
    }

    public static CurrencyTypes? ToCurrency(
        this string value)
        => Enum.TryParse<CurrencyTypes>(
            value,
            ignoreCase: true,
            out var currency)
                ? currency
                : null;

    public static DateTime? ToEsmaDate(
        this string value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal,
            out var date)
                ? date.UtcDateTime
                : null;

    public static SecurityMessage ToSecurityMessage(
        this EsmaInstrument instrument,
        long originalTransactionId)
    {
        var isin = instrument.Isin
            .ThrowIfEmpty(nameof(instrument.Isin))
            .Trim()
            .ToUpperInvariant();
        var mic = instrument.Mic
            .ThrowIfEmpty(nameof(instrument.Mic))
            .Trim()
            .ToUpperInvariant();

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = isin,
                BoardCode = mic,
                Isin = isin,
                Native = instrument.Id
                    .IsEmpty($"{isin}|{mic}"),
            },
            Name = instrument.FullName
                .IsEmpty(instrument.ShortName)
                .IsEmpty(isin)
                .Trim(),
            ShortName = instrument.ShortName
                .IsEmpty(instrument.FullName)
                .IsEmpty(isin)
                .Trim(),
            Class = instrument.CfiCode,
            SecurityType = instrument.CfiCode.ToSecurityType(),
            Currency = instrument.Currency.ToCurrency(),
            IssueDate = instrument.TradingStartDate.ToEsmaDate(),
            ExpiryDate = instrument.TradingTerminationDate.ToEsmaDate(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }
}
