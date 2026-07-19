namespace StockSharp.Foxbit.Native;

static class FoxbitExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(14),
        TimeSpan.FromDays(30),
    ];

    public static string NormalizeMarket(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToLowerInvariant();

    public static SecurityId ToStockSharp(this string marketSymbol)
        => new()
        {
            SecurityCode = marketSymbol.NormalizeMarket().ToUpperInvariant(),
            BoardCode = BoardCodes.Foxbit,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static decimal? ToStep(this int precision)
    {
        if (precision is < 0 or > 28)
            return null;
        var result = 1m;
        for (var i = 0; i < precision; i++)
            result /= 10m;
        return result;
    }

    public static string ToWire(this decimal value)
        => value.ToString("0.#############################",
            CultureInfo.InvariantCulture);

    public static string ToWire(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1m"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5m"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15m"
            : timeFrame == TimeSpan.FromMinutes(30) ? "30m"
            : timeFrame == TimeSpan.FromHours(1) ? "1h"
            : timeFrame == TimeSpan.FromHours(2) ? "2h"
            : timeFrame == TimeSpan.FromHours(4) ? "4h"
            : timeFrame == TimeSpan.FromHours(6) ? "6h"
            : timeFrame == TimeSpan.FromHours(12) ? "12h"
            : timeFrame == TimeSpan.FromDays(1) ? "1d"
            : timeFrame == TimeSpan.FromDays(7) ? "1w"
            : timeFrame == TimeSpan.FromDays(14) ? "2w"
            : timeFrame == TimeSpan.FromDays(30) ? "1M"
            : throw new NotSupportedException(
                $"Foxbit does not support the {timeFrame} candle interval.");

    public static DateTime ToUtcTime(this DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    public static DateTime Align(this DateTime value, TimeSpan interval)
    {
        value = value.ToUtcTime();
        if (interval == TimeSpan.FromDays(30))
            return new(value.Year, value.Month, 1, 0, 0, 0,
                DateTimeKind.Utc);
        if (interval == TimeSpan.FromDays(14))
        {
            var monday = value.Date.AddDays(-((int)value.DayOfWeek + 6) % 7);
            var weeks = (monday - DateTime.UnixEpoch.Date).Days / 7;
            return monday.AddDays(-(weeks & 1) * 7);
        }
        if (interval == TimeSpan.FromDays(7))
            return value.Date.AddDays(-((int)value.DayOfWeek + 6) % 7);
        var ticks = value.Ticks - DateTime.UnixEpoch.Ticks;
        return new(DateTime.UnixEpoch.Ticks + ticks / interval.Ticks *
            interval.Ticks, DateTimeKind.Utc);
    }

    public static DateTime GetCloseTime(this DateTime openTime,
        TimeSpan interval)
        => interval == TimeSpan.FromDays(30)
            ? openTime.ToUtcTime().AddMonths(1)
            : openTime.ToUtcTime() + interval;

    public static DateTime AddPeriods(this DateTime value,
        TimeSpan interval, int count)
        => interval == TimeSpan.FromDays(30)
            ? value.ToUtcTime().AddMonths(count)
            : value.ToUtcTime() + TimeSpan.FromTicks(interval.Ticks * count);

    public static DateTime SubtractPeriods(this DateTime value,
        TimeSpan interval, int count)
        => value.AddPeriods(interval, -count);

    public static DateTime FromMilliseconds(this long value)
        => DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

    public static string EscapePath(this string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

    public static Sides ToStockSharp(this FoxbitSides side)
        => side == FoxbitSides.Buy ? Sides.Buy : Sides.Sell;

    public static FoxbitSides ToFoxbit(this Sides side)
        => side switch
        {
            Sides.Buy => FoxbitSides.Buy,
            Sides.Sell => FoxbitSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

    public static OrderTypes ToStockSharp(this FoxbitOrderTypes? type)
        => type switch
        {
            FoxbitOrderTypes.Market or FoxbitOrderTypes.Instant =>
                OrderTypes.Market,
            FoxbitOrderTypes.StopLimit or FoxbitOrderTypes.StopMarket =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToStockSharp(this FoxbitOrderStates? state)
        => state switch
        {
            FoxbitOrderStates.Active or FoxbitOrderStates.PartiallyFilled or
                FoxbitOrderStates.PendingCancel => OrderStates.Active,
            FoxbitOrderStates.Filled or FoxbitOrderStates.Canceled or
                FoxbitOrderStates.PartiallyCanceled => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static FoxbitTimeInForces ToFoxbit(this TimeInForce? timeInForce)
        => timeInForce switch
        {
            null or TimeInForce.PutInQueue => FoxbitTimeInForces.Gtc,
            TimeInForce.CancelBalance => FoxbitTimeInForces.Ioc,
            TimeInForce.MatchOrCancel => FoxbitTimeInForces.Fok,
            _ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
                timeInForce, "Foxbit supports GTC, IOC, and FOK only."),
        };

    public static TimeInForce? ToStockSharp(
        this FoxbitTimeInForces? timeInForce)
        => timeInForce switch
        {
            FoxbitTimeInForces.Gtc => TimeInForce.PutInQueue,
            FoxbitTimeInForces.Ioc => TimeInForce.CancelBalance,
            FoxbitTimeInForces.Fok => TimeInForce.MatchOrCancel,
            _ => null,
        };
}
