namespace StockSharp.OSL.Native;

static class OSLExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(3),
        TimeSpan.FromDays(7),
    ];

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.OSL,
        };

    public static string ToWire(this decimal value)
        => value.ToString("0.#############################",
            CultureInfo.InvariantCulture);

    public static string ToRestInterval(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1min"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5min"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15min"
            : timeFrame == TimeSpan.FromMinutes(30) ? "30min"
            : timeFrame == TimeSpan.FromHours(1) ? "1h"
            : timeFrame == TimeSpan.FromHours(4) ? "4h"
            : timeFrame == TimeSpan.FromHours(6) ? "6h"
            : timeFrame == TimeSpan.FromHours(12) ? "12h"
            : timeFrame == TimeSpan.FromDays(1) ? "1day"
            : timeFrame == TimeSpan.FromDays(3) ? "3day"
            : timeFrame == TimeSpan.FromDays(7) ? "1week"
            : throw new NotSupportedException(
                $"OSL does not support the {timeFrame} candle interval.");

    public static string ToLegacyInterval(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1m"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5m"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15m"
            : timeFrame == TimeSpan.FromMinutes(30) ? "30m"
            : timeFrame == TimeSpan.FromHours(1) ? "1H"
            : timeFrame == TimeSpan.FromHours(4) ? "4H"
            : timeFrame == TimeSpan.FromHours(6) ? "6H"
            : timeFrame == TimeSpan.FromHours(12) ? "12H"
            : timeFrame == TimeSpan.FromDays(1) ? "1D"
            : timeFrame == TimeSpan.FromDays(3) ? "3D"
            : timeFrame == TimeSpan.FromDays(7) ? "1W"
            : throw new NotSupportedException(
                $"OSL does not support the {timeFrame} candle interval.");

    public static string ToCandleSelector(this string symbol,
        TimeSpan timeFrame)
        => $"{symbol.NormalizeSymbol().ToLowerInvariant()}@kline_" +
            timeFrame.ToLegacyInterval();

    public static decimal? ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static long? ToLong(this string value)
        => long.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static DateTime ToUtcTime(this long timestamp)
        => timestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
            : default;

    public static DateTime? ToUtcTime(this string timestamp)
        => timestamp.ToLong() is long value && value > 0
            ? value.ToUtcTime()
            : null;

    public static decimal? ToStep(this string precision)
    {
        if (!int.TryParse(precision, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var digits) ||
            digits is < 0 or > 28)
            return null;
        var step = 1m;
        for (var i = 0; i < digits; i++)
            step /= 10m;
        return step;
    }

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static Sides ToStockSharpSide(this string value)
        => value.EqualsIgnoreCase("buy")
            ? Sides.Buy
            : value.EqualsIgnoreCase("sell")
                ? Sides.Sell
                : throw new InvalidDataException(
                    $"OSL returned unknown side '{value}'.");

    public static OSLSides ToOSL(this Sides side)
        => side switch
        {
            Sides.Buy => OSLSides.Buy,
            Sides.Sell => OSLSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static OrderTypes ToStockSharpOrderType(this string value)
        => value.EqualsIgnoreCase("market")
            ? OrderTypes.Market
            : OrderTypes.Limit;

    public static OrderStates ToStockSharpOrderState(this string value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "new" or "live" or "partially_filled" => OrderStates.Active,
            "filled" or "canceled" or "cancelled" => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static TimeInForce? ToStockSharpTimeInForce(this string value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "gtc" => TimeInForce.PutInQueue,
            "ioc" => TimeInForce.CancelBalance,
            "fok" => TimeInForce.MatchOrCancel,
            "post_only" => TimeInForce.PutInQueue,
            _ => null,
        };

    public static OSLTimeInForce ToOSL(this TimeInForce? value,
        bool isPostOnly)
        => isPostOnly
            ? OSLTimeInForce.PostOnly
            : value switch
            {
                null or TimeInForce.PutInQueue =>
                    OSLTimeInForce.GoodTillCanceled,
                TimeInForce.CancelBalance =>
                    OSLTimeInForce.ImmediateOrCancel,
                TimeInForce.MatchOrCancel => OSLTimeInForce.FillOrKill,
                _ => throw new ArgumentOutOfRangeException(nameof(value),
                    value, null),
            };

    public static OSLSelfTradePreventionModes ToOSL(
        this OSLSelfTradePrevention value)
        => value switch
        {
            OSLSelfTradePrevention.ExpireTaker =>
                OSLSelfTradePreventionModes.ExpireTaker,
            OSLSelfTradePrevention.ExpireMaker =>
                OSLSelfTradePreventionModes.ExpireMaker,
            OSLSelfTradePrevention.ExpireBoth =>
                OSLSelfTradePreventionModes.ExpireBoth,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                null),
        };

    public static DateTime AddPeriods(this DateTime value,
        TimeSpan interval, int count)
        => value.ToUniversalTime() +
            TimeSpan.FromTicks(interval.Ticks * count);
}
