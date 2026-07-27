namespace StockSharp.JpxTdnet;

static class JpxTdnetExtensions
{
    private static readonly TimeSpan _japanOffset =
        TimeSpan.FromHours(9);

    public static string ToApiCode(
        this JpxTdnetDocumentFormats format)
        => format switch
        {
            JpxTdnetDocumentFormats.GeneralPdf => "g",
            JpxTdnetDocumentFormats.SummaryPdf => "s",
            JpxTdnetDocumentFormats.Xbrl => "x",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format), format, null),
        };

    public static string ToApiCode(
        this JpxTdnetIndexModes mode)
        => mode switch
        {
            JpxTdnetIndexModes.Current => null,
            JpxTdnetIndexModes.RevisionAndDeletionHistory => "1",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode), mode, null),
        };

    public static bool IsTdnetCode(this string code)
        => code?.Length is 4 or 5 &&
            code.Take(4).All(char.IsAsciiLetterOrDigit) &&
            (code.Length == 4 || char.IsAsciiDigit(code[4]));

    public static string ToTdnetTicker(this string code)
    {
        code = code
            .ThrowIfEmpty(nameof(code))
            .Trim()
            .ToUpperInvariant();
        if (!code.IsTdnetCode())
        {
            throw new FormatException(
                $"Invalid JPX TDnet stock code '{code}'.");
        }

        return code.Length == 5
            ? code[..4]
            : code;
    }

    public static SecurityId ToTdnetSecurityId(
        this string code)
    {
        code = code
            .ThrowIfEmpty(nameof(code))
            .Trim()
            .ToUpperInvariant();

        return new()
        {
            SecurityCode = code.ToTdnetTicker(),
            BoardCode = BoardCodes.Tse,
            Native = code.Length == 4 ? code + "0" : code,
        };
    }

    public static string GetTdnetCode(
        this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();

    public static SecurityMessage ToSecurityMessage(
        this JpxTdnetDisclosure disclosure,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = disclosure.Code.ToTdnetSecurityId(),
            Name = disclosure.Name.IsEmpty(
                disclosure.Code.ToTdnetTicker()),
            ShortName = disclosure.Name.IsEmpty(
                disclosure.Code.ToTdnetTicker()),
            Class = "TDnet",
            SecurityType = SecurityTypes.Stock,
            Currency = CurrencyTypes.JPY,
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static bool Matches(
        this JpxTdnetDisclosure disclosure,
        string value)
        => value.IsEmpty() ||
            disclosure.Code.ContainsIgnoreCase(value) ||
            disclosure.Code
                .ToTdnetTicker()
                .ContainsIgnoreCase(value) ||
            disclosure.Name.ContainsIgnoreCase(value);

    public static bool HasFormat(
        this JpxTdnetDisclosure disclosure,
        JpxTdnetDocumentFormats format)
        => format switch
        {
            JpxTdnetDocumentFormats.GeneralPdf =>
                disclosure.GeneralPdfFlag is "1" or "2",
            JpxTdnetDocumentFormats.SummaryPdf =>
                disclosure.SummaryPdfFlag == "1",
            JpxTdnetDocumentFormats.Xbrl =>
                disclosure.XbrlFlag is "1" or "2",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format), format, null),
        };

    public static string GetAvailableFormats(
        this JpxTdnetDisclosure disclosure)
        => string.Join(
            ", ",
            new[]
            {
                disclosure.HasFormat(
                    JpxTdnetDocumentFormats.GeneralPdf)
                        ? "full-text PDF"
                        : null,
                disclosure.HasFormat(
                    JpxTdnetDocumentFormats.SummaryPdf)
                        ? "summary PDF"
                        : null,
                disclosure.HasFormat(
                    JpxTdnetDocumentFormats.Xbrl)
                        ? "XBRL"
                        : null,
            }.Where(value => !value.IsEmpty()));

    public static bool TryToJapanUtc(
        this JpxTdnetDisclosure disclosure,
        out DateTime time)
    {
        var value =
            $"{disclosure.DisclosedDate} {disclosure.DisclosedTime}";
        if (DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            time = new DateTimeOffset(
                DateTime.SpecifyKind(
                    parsed, DateTimeKind.Unspecified),
                _japanOffset).UtcDateTime;
            return true;
        }

        time = default;
        return false;
    }

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
}
