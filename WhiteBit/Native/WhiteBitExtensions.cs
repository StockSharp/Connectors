namespace StockSharp.WhiteBit.Native;

static class WhiteBitExtensions
{
    public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
        new Dictionary<TimeSpan, string>
        {
            [TimeSpan.FromSeconds(1)] = "1s",
            [TimeSpan.FromSeconds(10)] = "10s",
            [TimeSpan.FromSeconds(30)] = "30s",
            [TimeSpan.FromMinutes(1)] = "1m",
            [TimeSpan.FromMinutes(3)] = "3m",
            [TimeSpan.FromMinutes(5)] = "5m",
            [TimeSpan.FromMinutes(15)] = "15m",
            [TimeSpan.FromMinutes(30)] = "30m",
            [TimeSpan.FromHours(1)] = "1h",
            [TimeSpan.FromHours(2)] = "2h",
            [TimeSpan.FromHours(4)] = "4h",
            [TimeSpan.FromHours(6)] = "6h",
            [TimeSpan.FromHours(8)] = "8h",
            [TimeSpan.FromHours(12)] = "12h",
            [TimeSpan.FromDays(1)] = "1d",
            [TimeSpan.FromDays(3)] = "3d",
            [TimeSpan.FromDays(7)] = "1w",
            [TimeSpan.FromDays(30)] = "1M",
        };

    public static string ToNative(this TimeSpan timeFrame)
        => TimeFrames.TryGetValue(timeFrame, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported WhiteBIT candle interval.");

    public static long ToNativeSeconds(this TimeSpan timeFrame)
    {
        if (!TimeFrames.ContainsKey(timeFrame))
            throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported WhiteBIT candle interval.");
        return timeFrame.TotalSeconds.To<long>();
    }

    public static Sides ToStockSharp(this WhiteBitSides side)
        => side == WhiteBitSides.Buy ? Sides.Buy : Sides.Sell;

    public static WhiteBitSides ToNative(this Sides side)
        => side == Sides.Buy ? WhiteBitSides.Buy : WhiteBitSides.Sell;

    public static SecurityId ToStockSharp(this string securityCode, string boardCode)
        => new()
        {
            SecurityCode = securityCode?.ToUpperInvariant(),
            BoardCode = boardCode?.ToUpperInvariant(),
        };

    public static decimal? ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    public static string ToWire(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static OrderStates ToStockSharp(this string status, decimal? left)
    {
        if (status.IsEmpty())
            return left is 0 ? OrderStates.Done : OrderStates.Active;

        return status.ToUpperInvariant() switch
        {
            "OPEN" or "PARTIALLY_FILLED" or "ACTIVE" => OrderStates.Active,
            "FILLED" or "CANCELED" or "CANCELLED" or "DONE" => OrderStates.Done,
            "REJECTED" or "FAILED" => OrderStates.Failed,
            _ => left is 0 ? OrderStates.Done : OrderStates.Active,
        };
    }

    public static DateTime ToUtcTime(this double seconds)
        => DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1_000d)).UtcDateTime;

    public static string ToBoardCode(this WhiteBitSections section)
        => section switch
        {
            WhiteBitSections.Margin => BoardCodes.WhiteBitMargin,
            WhiteBitSections.Futures => BoardCodes.WhiteBitFutures,
            _ => BoardCodes.WhiteBit,
        };
}
