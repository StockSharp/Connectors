namespace StockSharp.Tapbit.Native;

static class TapbitExtensions
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
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    public static string ToBoardCode(this TapbitProductTypes productType)
        => productType switch
        {
            TapbitProductTypes.Spot => BoardCodes.Tapbit,
            TapbitProductTypes.Futures => BoardCodes.TapbitFutures,
            _ => throw new ArgumentOutOfRangeException(nameof(productType),
                productType, null),
        };

    public static TapbitProductTypes ToTapbitProductType(
        this string boardCode)
        => boardCode.EqualsIgnoreCase(BoardCodes.Tapbit)
            ? TapbitProductTypes.Spot
            : boardCode.EqualsIgnoreCase(BoardCodes.TapbitFutures)
                ? TapbitProductTypes.Futures
                : throw new InvalidOperationException(
                    $"Unknown Tapbit board '{boardCode}'.");

    public static SecurityId ToStockSharp(this string symbol,
        TapbitProductTypes productType)
        => new()
        {
            SecurityCode = symbol.NormalizeTapbitSymbol(),
            BoardCode = productType.ToBoardCode(),
        };

    public static string NormalizeTapbitSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static decimal? ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static int? ToInt(this string value)
        => int.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static long? ToLong(this string value)
        => long.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static string ToNative(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static decimal PrecisionToStep(this int precision)
    {
        if (precision is < 0 or > 28)
            throw new ArgumentOutOfRangeException(nameof(precision),
                precision, "Precision must be between 0 and 28.");
        var step = 1m;
        for (var index = 0; index < precision; index++)
            step /= 10m;
        return step;
    }

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static DateTime ToUtcTime(this long value)
        => value > 0 ? value.FromUnix(false) : default;

    public static string ToInterval(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1"
            : timeFrame == TimeSpan.FromMinutes(3) ? "3"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15"
            : timeFrame == TimeSpan.FromMinutes(30) ? "30"
            : timeFrame == TimeSpan.FromHours(1) ? "60"
            : timeFrame == TimeSpan.FromHours(2) ? "120"
            : timeFrame == TimeSpan.FromHours(4) ? "240"
            : timeFrame == TimeSpan.FromHours(6) ? "360"
            : timeFrame == TimeSpan.FromHours(12) ? "720"
            : timeFrame == TimeSpan.FromDays(1) ? "D"
            : timeFrame == TimeSpan.FromDays(7) ? "W"
            : timeFrame == TimeSpan.FromDays(30) ? "M"
            : throw new NotSupportedException(
                $"Tapbit does not support the {timeFrame} candle interval.");

    public static Sides ToStockSharp(this TapbitSides side)
        => side switch
        {
            TapbitSides.Buy => Sides.Buy,
            TapbitSides.Sell => Sides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static Sides ToStockSharp(this TapbitTradeSides side)
        => side switch
        {
            TapbitTradeSides.Buy => Sides.Buy,
            TapbitTradeSides.Sell => Sides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static TapbitOrderDirections ToNative(this Sides side)
        => side switch
        {
            Sides.Buy => TapbitOrderDirections.Buy,
            Sides.Sell => TapbitOrderDirections.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static OrderStates ToStockSharp(this TapbitOrderStatuses status)
        => status switch
        {
            TapbitOrderStatuses.Open or TapbitOrderStatuses.Unsettled =>
                OrderStates.Active,
            TapbitOrderStatuses.Complete or TapbitOrderStatuses.Completed or
                TapbitOrderStatuses.Cancelled or TapbitOrderStatuses.Canceled or
                TapbitOrderStatuses.PartiallyCancelled or
                TapbitOrderStatuses.PartiallyCanceled => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static string ToRestSymbol(this TapbitInstrument instrument)
        => instrument.Symbol;

    public static string ToTopicPrefix(this TapbitProductTypes productType)
        => productType switch
        {
            TapbitProductTypes.Spot => "spot",
            TapbitProductTypes.Futures => "usdt",
            _ => throw new ArgumentOutOfRangeException(nameof(productType),
                productType, null),
        };

    public static string ToStreamSymbol(this string spotSymbol)
        => spotSymbol.NormalizeTapbitSymbol().Replace("/", string.Empty,
            StringComparison.Ordinal);
}
