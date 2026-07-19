namespace StockSharp.BYDFi.Native;

static class BYDFiExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.BYDFi,
        };

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

    public static DateTime ToUtcTime(this long value)
        => value > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
            : default;

    public static DateTime? ToUtcTime(this string value)
        => value.ToLong() is long timestamp && timestamp > 0
            ? timestamp.ToUtcTime()
            : null;

    public static decimal PrecisionToStep(this int precision)
    {
        if (precision is < 0 or > 28)
            throw new ArgumentOutOfRangeException(nameof(precision),
                precision, "Precision must be between 0 and 28.");
        var step = 1m;
        for (var i = 0; i < precision; i++)
            step /= 10m;
        return step;
    }

    public static string ToInterval(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1m"
            : timeFrame == TimeSpan.FromMinutes(3) ? "3m"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5m"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15m"
            : timeFrame == TimeSpan.FromMinutes(30) ? "30m"
            : timeFrame == TimeSpan.FromHours(1) ? "1h"
            : timeFrame == TimeSpan.FromHours(2) ? "2h"
            : timeFrame == TimeSpan.FromHours(4) ? "4h"
            : timeFrame == TimeSpan.FromHours(6) ? "6h"
            : timeFrame == TimeSpan.FromHours(8) ? "8h"
            : timeFrame == TimeSpan.FromHours(12) ? "12h"
            : timeFrame == TimeSpan.FromDays(1) ? "1d"
            : timeFrame == TimeSpan.FromDays(7) ? "1w"
            : timeFrame == TimeSpan.FromDays(30) ? "1M"
            : throw new NotSupportedException(
                $"BYDFi does not support the {timeFrame} candle interval.");

    public static TimeSpan ToTimeFrame(this string interval)
        => interval switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "3m" => TimeSpan.FromMinutes(3),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "2h" => TimeSpan.FromHours(2),
            "4h" => TimeSpan.FromHours(4),
            "6h" => TimeSpan.FromHours(6),
            "8h" => TimeSpan.FromHours(8),
            "12h" => TimeSpan.FromHours(12),
            "1d" => TimeSpan.FromDays(1),
            "1w" => TimeSpan.FromDays(7),
            "1M" => TimeSpan.FromDays(30),
            _ => throw new InvalidDataException(
                $"BYDFi returned unknown candle interval '{interval}'."),
        };

    public static Sides ToStockSharpSide(this string value)
        => value.EqualsIgnoreCase("BUY")
            ? Sides.Buy
            : value.EqualsIgnoreCase("SELL")
                ? Sides.Sell
                : throw new InvalidDataException(
                    $"BYDFi returned unknown side '{value}'.");

    public static BYDFiSides ToNative(this Sides side)
        => side switch
        {
            Sides.Buy => BYDFiSides.Buy,
            Sides.Sell => BYDFiSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static OrderTypes ToStockSharpOrderType(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "MARKET" => OrderTypes.Market,
            "STOP" or "TAKE_PROFIT" or "STOP_MARKET" or
                "TAKE_PROFIT_MARKET" or "TRAILING_STOP_MARKET" =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToStockSharpOrderState(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "NEW" or "PARTIALLY_FILLED" or "PENDING" =>
                OrderStates.Active,
            "FILLED" or "CANCELED" or "CANCELLED" or "EXPIRED" =>
                OrderStates.Done,
            "REJECTED" => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static TimeInForce? ToStockSharpTimeInForce(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "GTC" or "POST_ONLY" => TimeInForce.PutInQueue,
            "IOC" => TimeInForce.CancelBalance,
            "FOK" => TimeInForce.MatchOrCancel,
            _ => null,
        };

    public static BYDFiTimeInForce ToNative(this TimeInForce? value,
        bool isPostOnly)
        => isPostOnly
            ? BYDFiTimeInForce.PostOnly
            : value switch
            {
                null or TimeInForce.PutInQueue =>
                    BYDFiTimeInForce.GoodTillCanceled,
                TimeInForce.CancelBalance =>
                    BYDFiTimeInForce.ImmediateOrCancel,
                TimeInForce.MatchOrCancel => BYDFiTimeInForce.FillOrKill,
                _ => throw new ArgumentOutOfRangeException(nameof(value),
                    value, null),
            };
}
