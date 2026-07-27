namespace StockSharp.Edinet;

static class EdinetExtensions
{
    private static readonly TimeSpan _japanOffset =
        TimeSpan.FromHours(9);

    public static bool IsListed(this EdinetCompany company)
        => company.SecuritiesCode.IsEdinetSecuritiesCode();

    public static bool Matches(
        this EdinetCompany company,
        string value)
        => value.IsEmpty() ||
            company.EdinetCode.ContainsIgnoreCase(value) ||
            company.SecuritiesCode.ContainsIgnoreCase(value) ||
            company.SecuritiesCode
                .ToEdinetTickerOrNull()
                .ContainsIgnoreCase(value) ||
            company.CorporateNumber.ContainsIgnoreCase(value) ||
            company.Name.ContainsIgnoreCase(value) ||
            company.EnglishName.ContainsIgnoreCase(value) ||
            company.PhoneticName.ContainsIgnoreCase(value);

    public static SecurityMessage ToSecurityMessage(
        this EdinetCompany company,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = company.ToSecurityId(),
            Name = company.EnglishName
                .IsEmpty(company.Name)
                .IsEmpty(company.EdinetCode),
            ShortName = company.Name
                .IsEmpty(company.EnglishName)
                .IsEmpty(company.EdinetCode),
            Class = company.Industry
                .IsEmpty(company.SubmitterType)
                .IsEmpty("EDINET"),
            SecurityType = SecurityTypes.Stock,
            Currency = CurrencyTypes.JPY,
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static SecurityId ToSecurityId(
        this EdinetCompany company)
        => new()
        {
            SecurityCode = company.SecuritiesCode
                .ToEdinetTickerOrNull()
                .IsEmpty(company.EdinetCode),
            BoardCode = company.IsListed()
                ? BoardCodes.Tse
                : "EDINET",
            Native = company.EdinetCode,
        };

    public static string ToEdinetTickerOrNull(
        this string securitiesCode)
        => securitiesCode.IsEdinetSecuritiesCode()
            ? securitiesCode[..4]
            : null;

    public static bool IsEdinetSecuritiesCode(this string value)
        => value?.Length == 5 &&
            value.All(char.IsAsciiDigit);

    public static bool IsEdinetCode(this string value)
        => value?.Length == 6 &&
            value[0] == 'E' &&
            value.Skip(1).All(char.IsAsciiDigit);

    public static string GetEdinetIdentity(
        this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();

    public static bool Matches(
        this EdinetDocument document,
        EdinetDisclosureTypes type)
    {
        var codes = type.ToDocumentTypeCodes();
        return codes is null ||
            codes.Contains(document.DocumentTypeCode);
    }

    public static bool Matches(
        this EdinetDocument document,
        EdinetCompany company)
    {
        if (company is null)
            return true;

        var documentTicker =
            document.SecuritiesCode.ToEdinetTickerOrNull();
        var companyTicker =
            company.SecuritiesCode.ToEdinetTickerOrNull();

        return document.EdinetCode.EqualsIgnoreCase(
                company.EdinetCode) ||
            document.IssuerEdinetCode.EqualsIgnoreCase(
                company.EdinetCode) ||
            document.SubjectEdinetCode.EqualsIgnoreCase(
                company.EdinetCode) ||
            document.SubsidiaryEdinetCode.EqualsIgnoreCase(
                company.EdinetCode) ||
            (!documentTicker.IsEmpty() &&
                documentTicker.EqualsIgnoreCase(companyTicker));
    }

    public static bool IsAvailable(this EdinetDocument document)
        => document.XbrlFlag == "1" ||
            document.PdfFlag == "1" ||
            document.AttachmentFlag == "1" ||
            document.EnglishFlag == "1" ||
            document.CsvFlag == "1";

    public static string GetAvailableFormats(
        this EdinetDocument document)
        => string.Join(
            ", ",
            new[]
            {
                document.XbrlFlag == "1" ? "XBRL" : null,
                document.PdfFlag == "1" ? "PDF" : null,
                document.AttachmentFlag == "1"
                    ? "attachments"
                    : null,
                document.EnglishFlag == "1"
                    ? "English"
                    : null,
                document.CsvFlag == "1" ? "CSV" : null,
            }.Where(value => !value.IsEmpty()));

    public static IReadOnlySet<string> ToDocumentTypeCodes(
        this EdinetDisclosureTypes type)
        => type switch
        {
            EdinetDisclosureTypes.All => null,
            EdinetDisclosureTypes.AnnualReports =>
                new HashSet<string>(["120", "130"]),
            EdinetDisclosureTypes.QuarterlyReports =>
                new HashSet<string>(["140", "150"]),
            EdinetDisclosureTypes.SemiAnnualReports =>
                new HashSet<string>(["160", "170"]),
            EdinetDisclosureTypes.CurrentReports =>
                new HashSet<string>(["180", "190"]),
            EdinetDisclosureTypes.LargeShareholdings =>
                new HashSet<string>(["350", "360"]),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type), type, null),
        };

    public static DateTime ToJapanDate(
        this DateTimeOffset value)
        => value.ToOffset(_japanOffset).Date;

    public static DateTime ToJapanDate(this DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? value.Date
            : new DateTimeOffset(value)
                .ToOffset(_japanOffset)
                .Date;

    public static DateTime JapanToday()
        => DateTimeOffset.UtcNow.ToOffset(_japanOffset).Date;

    public static bool TryToJapanUtc(
        this string value,
        out DateTime date)
    {
        if (DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            date = new DateTimeOffset(
                DateTime.SpecifyKind(
                    parsed, DateTimeKind.Unspecified),
                _japanOffset).UtcDateTime;
            return true;
        }

        date = default;
        return false;
    }
}
