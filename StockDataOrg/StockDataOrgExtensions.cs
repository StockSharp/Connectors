namespace StockSharp.StockDataOrg;

static class StockDataOrgExtensions
{
    public const string DefaultBoard = "STOCKDATA";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(1),
    ];

    public static string GetSymbol(this SecurityId securityId)
    {
        var symbol = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim()
            .ToUpperInvariant();
        if (symbol.IsEmpty())
        {
            throw new InvalidOperationException(
                "StockData.org security identifier requires a symbol.");
        }
        if (symbol.Contains(','))
        {
            throw new InvalidOperationException(
                "StockData.org subscriptions require one symbol.");
        }
        return symbol;
    }

    public static SecurityId Normalize(
        this SecurityId securityId,
        string symbol)
        => new()
        {
            SecurityCode = symbol,
            BoardCode = securityId.BoardCode
                .IsEmpty(DefaultBoard),
            Native = symbol,
        };

    public static SecurityMessage ToSecurityMessage(
        this StockDataOrgEntity entity,
        long originalTransactionId)
    {
        var symbol = entity.Symbol?
            .Trim()
            .ToUpperInvariant()
            .ThrowIfEmpty(nameof(entity.Symbol));
        var board = entity.MicCode
            .IsEmpty(entity.Exchange)
            .IsEmpty(DefaultBoard)
            .Trim()
            .ToUpperInvariant()
            .Replace(' ', '_');

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = symbol,
                BoardCode = board,
                Native = symbol,
            },
            Name = entity.Name.IsEmpty(symbol),
            ShortName = entity.Name.IsEmpty(symbol),
            Class = entity.Industry,
            SecurityType = entity.Type.ToSecurityType(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(this string type)
        => type?.Trim().ToLowerInvariant() switch
        {
            "index" => SecurityTypes.Index,
            "etf" => SecurityTypes.Etf,
            "mutualfund" => SecurityTypes.Fund,
            _ => SecurityTypes.Stock,
        };

    public static string ToProviderTypes(
        this ISet<SecurityTypes> types)
    {
        if (types is null || types.Count == 0)
            return "equity,index,etf,mutualfund";

        var values = new List<string>();
        if (types.Contains(SecurityTypes.Stock))
            values.Add("equity");
        if (types.Contains(SecurityTypes.Index))
            values.Add("index");
        if (types.Contains(SecurityTypes.Etf))
            values.Add("etf");
        if (types.Contains(SecurityTypes.Fund))
            values.Add("mutualfund");
        return string.Join(",", values);
    }

    public static (string Interval, bool Intraday)
        ToStockDataInterval(this TimeSpan timeFrame)
        => timeFrame switch
        {
            var value when value == TimeSpan.FromMinutes(1)
                => ("minute", true),
            var value when value == TimeSpan.FromHours(1)
                => ("hour", true),
            var value when value == TimeSpan.FromDays(1)
                => ("day", false),
            _ => throw new NotSupportedException(
                $"StockData.org does not support {timeFrame} candles."),
        };

    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        timeZoneId = timeZoneId?.Trim();
        if (timeZoneId.IsEmpty())
        {
            throw new InvalidOperationException(
                "StockData.org quote time zone is not specified.");
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException(
                $"StockData.org quote time zone '{timeZoneId}' was not found.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"StockData.org quote time zone '{timeZoneId}' is invalid.");
        }
    }

    public static bool TryParseUtc(
        string value,
        out DateTimeOffset result)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out result);

    public static bool TryParseQuoteTime(
        string value,
        TimeZoneInfo timeZone,
        out DateTimeOffset result)
    {
        result = default;
        if (value.IsEmpty())
            return false;
        if (value.EndsWith('Z') ||
            HasExplicitOffset(value))
        {
            return TryParseUtc(value, out result);
        }
        if (!DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
            return false;
        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Min()
            : timeZone.GetUtcOffset(local);
        result = new DateTimeOffset(local, offset)
            .ToUniversalTime();
        return true;
    }

    public static DateTimeOffset EstimateFrom(
        DateTimeOffset to,
        TimeSpan timeFrame,
        long? count)
    {
        if (count is not > 0)
        {
            return timeFrame == TimeSpan.FromMinutes(1)
                ? to.AddDays(-7)
                : timeFrame == TimeSpan.FromHours(1)
                    ? to.AddDays(-180)
                    : to.AddDays(-180);
        }

        var requested = Math.Min(count.Value, 1_000_000);
        var ticks = checked(timeFrame.Ticks * requested);
        var calendarTicks = Math.Min(
            checked(ticks * 3),
            TimeSpan.FromDays(365 * 100).Ticks);
        return to.Subtract(TimeSpan.FromTicks(calendarTicks));
    }

    private static bool HasExplicitOffset(string value)
    {
        var separator = value.IndexOf('T');
        if (separator < 0)
            separator = value.IndexOf(' ');
        if (separator < 0)
            return false;
        return value.IndexOf('+', separator) >= 0 ||
            value.IndexOf('-', separator) >= 0;
    }
}
