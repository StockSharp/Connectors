namespace StockSharp.MercadoBitcoin.Native;

static class MercadoBitcoinExtensions
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().Replace('/', '-')
            .Replace('_', '-').ToUpperInvariant();

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.MercadoBitcoin,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static MercadoBitcoinOrderSides ToMercadoBitcoin(this Sides side)
        => side == Sides.Buy
            ? MercadoBitcoinOrderSides.Buy
            : MercadoBitcoinOrderSides.Sell;

    public static Sides ToStockSharp(this MercadoBitcoinOrderSides side)
        => side == MercadoBitcoinOrderSides.Buy ? Sides.Buy : Sides.Sell;

    public static OrderTypes ToStockSharp(this MercadoBitcoinOrderTypes type)
        => type switch
        {
            MercadoBitcoinOrderTypes.Market => OrderTypes.Market,
            MercadoBitcoinOrderTypes.StopLimit => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToStockSharp(this MercadoBitcoinOrderStatuses status)
        => status switch
        {
            MercadoBitcoinOrderStatuses.Created or
            MercadoBitcoinOrderStatuses.Working => OrderStates.Active,
            MercadoBitcoinOrderStatuses.Cancelled or
            MercadoBitcoinOrderStatuses.Filled => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static string ToWire(this MercadoBitcoinOrderSides side)
        => side == MercadoBitcoinOrderSides.Buy ? "buy" : "sell";

    public static string ToWire(this MercadoBitcoinOrderStatuses status)
        => status switch
        {
            MercadoBitcoinOrderStatuses.Created => "created",
            MercadoBitcoinOrderStatuses.Working => "working",
            MercadoBitcoinOrderStatuses.Cancelled => "cancelled",
            MercadoBitcoinOrderStatuses.Filled => "filled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static string ToWire(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static string ToMercadoBitcoinResolution(this TimeSpan timeFrame)
        => timeFrame switch
        {
            _ when timeFrame == TimeSpan.FromMinutes(1) => "1m",
            _ when timeFrame == TimeSpan.FromMinutes(15) => "15m",
            _ when timeFrame == TimeSpan.FromHours(1) => "1h",
            _ when timeFrame == TimeSpan.FromHours(3) => "3h",
            _ when timeFrame == TimeSpan.FromDays(1) => "1d",
            _ when timeFrame == TimeSpan.FromDays(7) => "1w",
            _ when timeFrame == TimeSpan.FromDays(30) => "1M",
            _ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
                "Mercado Bitcoin does not support this candle interval."),
        };

    public static DateTime FromMercadoBitcoinTimestamp(this long value,
        DateTime fallback)
    {
        if (value <= 0)
            return EnsureUtc(fallback);
        try
        {
            if (value >= 100_000_000_000_000_000L)
                return DateTimeOffset.FromUnixTimeMilliseconds(value / 1_000_000L)
                    .UtcDateTime;
            if (value >= 100_000_000_000_000L)
                return DateTimeOffset.FromUnixTimeMilliseconds(value / 1_000L)
                    .UtcDateTime;
            if (value >= 100_000_000_000L)
                return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
            return DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return EnsureUtc(fallback);
        }
    }

    public static long ToUnixSeconds(this DateTime value)
        => new DateTimeOffset(EnsureUtc(value)).ToUnixTimeSeconds();

    public static T GetAt<T>(this T[] values, int index, T fallback = default)
        => values is not null && index >= 0 && index < values.Length
            ? values[index]
            : fallback;

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
