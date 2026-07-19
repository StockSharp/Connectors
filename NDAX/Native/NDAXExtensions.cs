namespace StockSharp.NDAX.Native;

static class NDAXExtensions
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
    ];

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.NDAX,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static decimal? ToStep(this int decimals)
    {
        if (decimals is < 0 or > 28)
            return null;
        var result = 1m;
        for (var i = 0; i < decimals; i++)
            result /= 10m;
        return result;
    }

    public static int ToInterval(this TimeSpan timeFrame)
    {
        if (!TimeFrames.Contains(timeFrame))
            throw new NotSupportedException(
                $"NDAX does not support the {timeFrame} candle interval.");
        return checked((int)timeFrame.TotalSeconds);
    }

    public static DateTime FromMilliseconds(this long value)
        => value > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
            : default;

    public static DateTime FromNdaxTime(this long value)
    {
        if (value <= 0)
            return default;
        if (value >= DateTime.UnixEpoch.Ticks)
            return new DateTime(value, DateTimeKind.Utc);
        return value.FromMilliseconds();
    }

    public static DateTime ToUtcTime(this DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    public static DateTime AddPeriods(this DateTime value,
        TimeSpan timeFrame, int count)
        => value.AddTicks(checked(timeFrame.Ticks * count));

    public static string EscapeQuery(this string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

    public static Sides ToStockSharp(this NdaxSides side)
        => side switch
        {
            NdaxSides.Buy => Sides.Buy,
            NdaxSides.Sell => Sides.Sell,
            _ => throw new InvalidDataException(
                $"NDAX returned unsupported side '{side}'."),
        };

    public static NdaxSides ToNdax(this Sides side)
        => side switch
        {
            Sides.Buy => NdaxSides.Buy,
            Sides.Sell => NdaxSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static Sides ToSide(this string value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "buy" or "0" => Sides.Buy,
            "sell" or "1" => Sides.Sell,
            _ => throw new InvalidDataException(
                $"NDAX returned unsupported side '{value}'."),
        };

    public static Sides ToSide(this NdaxAccountTrade trade)
        => trade.Side?.ToStockSharp() ?? throw new InvalidDataException(
            $"NDAX trade '{trade.TradeId}' has no side.");

    public static OrderTypes ToOrderType(this string value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "market" or "1" => OrderTypes.Market,
            "limit" or "2" => OrderTypes.Limit,
            "stopmarket" or "stoplimit" or "trailingstopmarket" or
                "trailingstoplimit" or "3" or "4" or "5" or "6" =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToOrderState(this string value)
        => value?.Replace("_", string.Empty,
            StringComparison.Ordinal).Trim().ToLowerInvariant() switch
        {
            "working" or "accepted" or "1" => OrderStates.Active,
            "rejected" or "2" => OrderStates.Failed,
            "canceled" or "cancelled" or "expired" or "fullyexecuted" or
                "3" or "4" or "5" => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static NdaxTimeInForces ToNdax(this TimeInForce? value)
        => value switch
        {
            null or TimeInForce.PutInQueue => NdaxTimeInForces.Gtc,
            TimeInForce.CancelBalance => NdaxTimeInForces.Ioc,
            TimeInForce.MatchOrCancel => NdaxTimeInForces.Fok,
            _ => throw new NotSupportedException(
                $"NDAX does not support time-in-force '{value}'."),
        };

    public static TimeInForce? ToStockSharp(this NdaxTimeInForces value)
        => value switch
        {
            NdaxTimeInForces.Gtc => TimeInForce.PutInQueue,
            NdaxTimeInForces.Ioc => TimeInForce.CancelBalance,
            NdaxTimeInForces.Fok => TimeInForce.MatchOrCancel,
            _ => null,
        };
}
