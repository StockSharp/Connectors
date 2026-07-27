namespace StockSharp.TaseDataHub;

static class TaseDataHubExtensions
{
    private static readonly TimeZoneInfo _jerusalem =
        GetJerusalemTimeZone();

    public static SecurityTypes ToSecurityType(
        this TaseSecurityType type)
    {
        var value = string.Join(
            " ",
            type?.MainTypeDescription,
            type?.TypeDescription);

        if (value.ContainsIgnoreCase("ETF") ||
            value.ContainsIgnoreCase("EXCHANGE TRADED"))
        {
            return SecurityTypes.Etf;
        }
        if (value.ContainsIgnoreCase("MUTUAL FUND") ||
            value.ContainsIgnoreCase("UNIT TRUST"))
        {
            return SecurityTypes.Fund;
        }
        if (value.ContainsIgnoreCase("BOND") ||
            value.ContainsIgnoreCase("DEBENTURE"))
        {
            return SecurityTypes.Bond;
        }
        if (value.ContainsIgnoreCase("WARRANT"))
            return SecurityTypes.Warrant;
        if (value.ContainsIgnoreCase("OPTION"))
            return SecurityTypes.Option;
        if (value.ContainsIgnoreCase("FUTURE"))
            return SecurityTypes.Future;
        if (value.ContainsIgnoreCase("SHARE") ||
            value.ContainsIgnoreCase("STOCK"))
        {
            return SecurityTypes.Stock;
        }

        return SecurityTypes.Stock;
    }

    public static SecurityId ToSecurityId(
        this TaseSecurity security)
        => new()
        {
            SecurityCode = security.Symbol
                .IsEmpty(security.SecurityId.ToString(
                    CultureInfo.InvariantCulture))
                .Trim()
                .ToUpperInvariant(),
            BoardCode = BoardCodes.Tase,
            Native = security.SecurityId,
            Isin = security.Isin,
        };

    public static SecurityId ToSecurityId(
        this TaseEodRecord record,
        SecurityId requested)
        => requested.SecurityCode.IsEmpty()
            ? new SecurityId
            {
                SecurityCode = record.Symbol
                    .IsEmpty(record.SecurityId.ToString(
                        CultureInfo.InvariantCulture))
                    .Trim()
                    .ToUpperInvariant(),
                BoardCode = BoardCodes.Tase,
                Native = record.SecurityId,
            }
            : requested;

    public static SecurityMessage ToSecurityMessage(
        this TaseSecurity security,
        TaseSecurityType type,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = security.ToSecurityId(),
            Name = security.CompanyName
                .IsEmpty(security.SecurityName)
                .IsEmpty(security.Symbol)
                .Trim(),
            ShortName = security.SecurityName
                .IsEmpty(security.Symbol)
                .Trim(),
            Class = security.CompanySubSector
                .IsEmpty(security.CompanySector)
                .IsEmpty(type?.TypeDescription),
            SecurityType = type.ToSecurityType(),
            Currency = CurrencyTypes.ILS,
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static bool Matches(
        this TaseSecurity security,
        string code,
        string name)
        => (code.IsEmpty() ||
            security.Symbol.ContainsIgnoreCase(code) ||
            security.SecurityId.ToString(
                CultureInfo.InvariantCulture)
                .ContainsIgnoreCase(code) ||
            security.Isin.ContainsIgnoreCase(code)) &&
            (name.IsEmpty() ||
                security.SecurityName.ContainsIgnoreCase(name) ||
                security.CompanyName.ContainsIgnoreCase(name));

    public static long GetTaseSecurityId(
        this SecurityId securityId)
    {
        if (securityId.Native is long native && native > 0)
            return native;
        if (securityId.Native is int nativeInt && nativeInt > 0)
            return nativeInt;
        if (long.TryParse(
            securityId.SecurityCode,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value) &&
            value > 0)
        {
            return value;
        }

        throw new InvalidOperationException(
            "TASE EOD requests require the numeric security ID " +
            "returned by security lookup.");
    }

    public static DateTime ToTaseDate(
        this string value)
    {
        if (!DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var date))
        {
            throw new InvalidOperationException(
                $"TASE returned invalid trading date '{value}'.");
        }

        return DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
    }

    public static DateTime ToTaseTime(
        this DateTime date,
        TimeSpan localTime)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                date.Date.Add(localTime),
                DateTimeKind.Unspecified),
            _jerusalem);

    private static TimeZoneInfo GetJerusalemTimeZone()
    {
        foreach (var id in new[]
        {
            "Israel Standard Time",
            "Asia/Jerusalem",
        })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new TimeZoneNotFoundException(
            "The Jerusalem time zone is unavailable.");
    }
}
