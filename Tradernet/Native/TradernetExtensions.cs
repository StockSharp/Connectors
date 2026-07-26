namespace StockSharp.Tradernet.Native;

static class TradernetExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(1),
    ];

    public static decimal? ToDecimal(this string value)
    {
        if (value.IsEmpty())
            return null;
        value = value.Trim().Replace(',', '.');
        return decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var result)
                ? result : null;
    }

    public static DateTime ParseTimestamp(this string value,
        DateTime fallback)
    {
        if (value.IsEmpty())
            return fallback;
        return DateTimeOffset.TryParse(value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AllowWhiteSpaces,
            out var result)
                ? result.UtcDateTime : fallback;
    }

    public static DateTime? ParseTimestamp(this string value)
        => value.IsEmpty() ? null :
            DateTimeOffset.TryParse(value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AllowWhiteSpaces,
                out var result)
                    ? result.UtcDateTime : null;

    public static SecurityTypes ToSecurityType(
        this int type, int kind)
        => type switch
        {
            1 when kind == 7 => SecurityTypes.Fund,
            1 => SecurityTypes.Stock,
            2 => SecurityTypes.Bond,
            3 => SecurityTypes.Future,
            4 => SecurityTypes.Option,
            5 => SecurityTypes.Index,
            6 when kind == 8 => SecurityTypes.CryptoCurrency,
            6 => SecurityTypes.Currency,
            _ => SecurityTypes.Stock,
        };

    public static OrderStates ToOrderState(this int status)
        => status switch
        {
            1 or 2 or 11 => OrderStates.Pending,
            10 or 12 => OrderStates.Active,
            20 or 21 or 30 or 31 or 71 or 72 =>
                OrderStates.Done,
            0 or 70 or 74 or 75 => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static OrderTypes ToOrderType(this int type)
        => type switch
        {
            1 => OrderTypes.Market,
            2 => OrderTypes.Limit,
            3 or 4 or 5 or 6 => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static int ToNativeOrderType(this OrderTypes type)
        => type switch
        {
            OrderTypes.Market => 1,
            OrderTypes.Limit => 2,
            _ => throw new NotSupportedException(
                $"Tradernet standard registration does not support {type}."),
        };

    public static int ToNativeExpiration(
        this TimeInForce? timeInForce)
        => timeInForce switch
        {
            null or TimeInForce.PutInQueue => 3,
            _ => throw new NotSupportedException(
                $"Tradernet does not support {timeInForce} time-in-force."),
        };

    public static TimeInForce ToTimeInForce(this int expiration)
        => TimeInForce.PutInQueue;

    public static int ToNativeTimeFrame(this TimeSpan timeFrame)
        => timeFrame switch
        {
            { TotalMinutes: 1 } => 1,
            { TotalMinutes: 5 } => 5,
            { TotalMinutes: 15 } => 15,
            { TotalMinutes: 60 } => 60,
            { TotalMinutes: 1440 } => 1440,
            _ => throw new ArgumentOutOfRangeException(
                nameof(timeFrame), timeFrame,
                "Tradernet does not support this candle time frame."),
        };

    public static SecurityId ToSecurityId(this string ticker,
        string isin = null)
    {
        if (ticker.IsEmpty())
            return default;
        var dot = ticker.LastIndexOf('.');
        return new()
        {
            SecurityCode = dot > 0
                ? ticker[..dot] : ticker,
            BoardCode = dot > 0
                ? ticker[(dot + 1)..] : "TRADERNET",
            Native = ticker,
            Isin = isin,
        };
    }

    public static SecurityId ToSecurityId(
        this TradernetSecurity security)
        => security?.Ticker.ToSecurityId(security?.IssueNumber) ??
            default;

    public static SecurityId ToSecurityId(
        this TradernetSearchSecurity security)
        => security?.Ticker.ToSecurityId(security?.Isin) ??
            default;

    public static string ToNativeTicker(
        this SecurityId securityId)
    {
        var native = securityId.Native as string;
        if (!native.IsEmpty())
            return native;
        var code = securityId.SecurityCode.ThrowIfEmpty(
            nameof(securityId.SecurityCode));
        if (code.Contains('.'))
            return code;
        return securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase("TRADERNET")
                ? code
                : $"{code}.{securityId.BoardCode}";
    }

    public static long GetOrderId(this TradernetOrder order)
        => order?.OrderId ??
            order?.HistoricalOrderId ?? 0;

    public static DateTime GetOrderTime(
        this TradernetOrder order)
        => order?.StatusDate.ParseTimestamp(
            order?.Date.ParseTimestamp(DateTime.MinValue) ??
            DateTime.MinValue) ?? DateTime.MinValue;

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(
            value, true, out var currency)
                ? currency : null;
}
