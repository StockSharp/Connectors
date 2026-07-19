namespace StockSharp.GmoCoin.Native;

static class GmoCoinExtensions
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().Replace('/', '_')
            .Replace('-', '_').ToUpperInvariant();

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.GmoCoin,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static GmoCoinSides ToGmoCoin(this Sides side)
        => side == Sides.Buy ? GmoCoinSides.Buy : GmoCoinSides.Sell;

    public static Sides ToStockSharp(this GmoCoinSides side)
        => side == GmoCoinSides.Buy ? Sides.Buy : Sides.Sell;

    public static GmoCoinSides Opposite(this GmoCoinSides side)
        => side == GmoCoinSides.Buy ? GmoCoinSides.Sell : GmoCoinSides.Buy;

    public static OrderTypes ToStockSharp(this GmoCoinExecutionTypes type)
        => type switch
        {
            GmoCoinExecutionTypes.Market => OrderTypes.Market,
            GmoCoinExecutionTypes.Stop => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToStockSharp(this GmoCoinOrderStatuses status)
        => status switch
        {
            GmoCoinOrderStatuses.Waiting or
            GmoCoinOrderStatuses.Ordered or
            GmoCoinOrderStatuses.Modifying or
            GmoCoinOrderStatuses.Cancelling => OrderStates.Active,
            GmoCoinOrderStatuses.Executed or
            GmoCoinOrderStatuses.Canceled or
            GmoCoinOrderStatuses.Expired => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static GmoCoinTimeInForce ToGmoCoin(this TimeInForce? value,
        bool isPostOnly, GmoCoinExecutionTypes executionType)
    {
        if (isPostOnly)
        {
            if (executionType != GmoCoinExecutionTypes.Limit)
                throw new InvalidOperationException(
                    "GMO Coin post-only policy is available for limit orders only.");
            return GmoCoinTimeInForce.StoreOrKill;
        }

        return value switch
        {
            TimeInForce.MatchOrCancel => GmoCoinTimeInForce.FillAndKill,
            TimeInForce.CancelBalance => GmoCoinTimeInForce.FillOrKill,
            _ when executionType == GmoCoinExecutionTypes.Limit =>
                GmoCoinTimeInForce.FillAndStore,
            _ => GmoCoinTimeInForce.FillAndKill,
        };
    }

    public static TimeInForce? ToStockSharp(this GmoCoinTimeInForce value)
        => value switch
        {
            GmoCoinTimeInForce.FillAndKill => TimeInForce.MatchOrCancel,
            GmoCoinTimeInForce.FillOrKill => TimeInForce.CancelBalance,
            _ => TimeInForce.PutInQueue,
        };

    public static string ToGmoCoinInterval(this TimeSpan timeFrame)
        => timeFrame switch
        {
            _ when timeFrame == TimeSpan.FromMinutes(1) => "1min",
            _ when timeFrame == TimeSpan.FromMinutes(5) => "5min",
            _ when timeFrame == TimeSpan.FromMinutes(10) => "10min",
            _ when timeFrame == TimeSpan.FromMinutes(15) => "15min",
            _ when timeFrame == TimeSpan.FromMinutes(30) => "30min",
            _ when timeFrame == TimeSpan.FromHours(1) => "1hour",
            _ when timeFrame == TimeSpan.FromHours(4) => "4hour",
            _ when timeFrame == TimeSpan.FromHours(8) => "8hour",
            _ when timeFrame == TimeSpan.FromHours(12) => "12hour",
            _ when timeFrame == TimeSpan.FromDays(1) => "1day",
            _ when timeFrame == TimeSpan.FromDays(7) => "1week",
            _ when timeFrame == TimeSpan.FromDays(30) => "1month",
            _ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
                "GMO Coin does not support this candle interval."),
        };

    public static bool UsesYearlyKlines(this TimeSpan timeFrame)
        => timeFrame >= TimeSpan.FromHours(4);

    public static string ToWire(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static DateTime FromGmoCoinTime(this string value, DateTime fallback)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
            return parsed.UtcDateTime;
        return fallback.Kind == DateTimeKind.Utc
            ? fallback
            : fallback.ToUniversalTime();
    }

    public static DateTime FromGmoCoinTime(this long value, DateTime fallback)
    {
        if (value <= 0)
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        }
    }
}
