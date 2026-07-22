namespace StockSharp.FXOpen.Native;

static class FXOpenExtensions
{
    public const string BoardCode = "FXOPEN";

    public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
        new Dictionary<TimeSpan, string>
        {
            [TimeSpan.FromSeconds(1)] = "S1",
            [TimeSpan.FromSeconds(10)] = "S10",
            [TimeSpan.FromMinutes(1)] = "M1",
            [TimeSpan.FromMinutes(5)] = "M5",
            [TimeSpan.FromMinutes(15)] = "M15",
            [TimeSpan.FromMinutes(30)] = "M30",
            [TimeSpan.FromHours(1)] = "H1",
            [TimeSpan.FromHours(4)] = "H4",
            [TimeSpan.FromDays(1)] = "D1",
            [TimeSpan.FromDays(7)] = "W1",
            [TimeSpan.FromDays(30)] = "MN1",
        };

    public static SecurityId ToSecurityId(this string symbol)
        => new() { SecurityCode = symbol, BoardCode = BoardCode };

    public static string ToNative(this TimeSpan timeFrame)
        => TimeFrames.TryGetValue(timeFrame, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
                "FXOpen does not support this candle interval.");

    public static TimeSpan? ToTimeFrame(this string value)
    {
        foreach (var pair in TimeFrames)
        {
            if (pair.Value.EqualsIgnoreCase(value))
                return pair.Key;
        }
        return null;
    }

    public static TickTraderOrderSides ToNative(this Sides side)
        => side == Sides.Buy ? TickTraderOrderSides.Buy : TickTraderOrderSides.Sell;

    public static Sides ToSide(this TickTraderOrderSides side)
        => side == TickTraderOrderSides.Buy ? Sides.Buy : Sides.Sell;

    public static OrderTypes ToOrderType(this TickTraderOrderTypes type)
        => type switch
        {
            TickTraderOrderTypes.Market or TickTraderOrderTypes.Position => OrderTypes.Market,
            TickTraderOrderTypes.Limit => OrderTypes.Limit,
            TickTraderOrderTypes.Stop or TickTraderOrderTypes.StopLimit => OrderTypes.Conditional,
            _ => OrderTypes.Conditional,
        };

    public static OrderStates ToOrderState(this TickTraderOrderStatuses status)
        => status switch
        {
            TickTraderOrderStatuses.New or TickTraderOrderStatuses.Calculated or
                TickTraderOrderStatuses.Activated or TickTraderOrderStatuses.PartiallyFilled =>
                OrderStates.Active,
            TickTraderOrderStatuses.Filled or TickTraderOrderStatuses.Canceled or
                TickTraderOrderStatuses.Expired => OrderStates.Done,
            TickTraderOrderStatuses.Rejected or TickTraderOrderStatuses.Invalid => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static DateTime EnsureUtc(this DateTime time)
        => time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
}
