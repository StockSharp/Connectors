namespace StockSharp.Zoomex.Native;

static class ZoomexExtensions
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

    public static string ToNative(this ZoomexCategories category)
        => category switch
        {
            ZoomexCategories.Spot => "spot",
            ZoomexCategories.Linear => "linear",
            ZoomexCategories.Inverse => "inverse",
            _ => throw new ArgumentOutOfRangeException(nameof(category),
                category, null),
        };

    public static ZoomexCategories ToNative(this ZoomexSections section)
        => section switch
        {
            ZoomexSections.Spot => ZoomexCategories.Spot,
            ZoomexSections.Linear => ZoomexCategories.Linear,
            ZoomexSections.Inverse => ZoomexCategories.Inverse,
            _ => throw new ArgumentOutOfRangeException(nameof(section),
                section, null),
        };

    public static ZoomexNativeAccountTypes ToNative(
        this ZoomexAccountTypes accountType)
        => accountType switch
        {
            ZoomexAccountTypes.Unified =>
                ZoomexNativeAccountTypes.Unified,
            ZoomexAccountTypes.Contract =>
                ZoomexNativeAccountTypes.Contract,
            ZoomexAccountTypes.Spot => ZoomexNativeAccountTypes.Spot,
            _ => throw new ArgumentOutOfRangeException(nameof(accountType),
                accountType, null),
        };

    public static string ToNative(this ZoomexNativeAccountTypes accountType)
        => accountType switch
        {
            ZoomexNativeAccountTypes.Unified => "UNIFIED",
            ZoomexNativeAccountTypes.Contract => "CONTRACT",
            ZoomexNativeAccountTypes.Spot => "SPOT",
            ZoomexNativeAccountTypes.Fund => "FUND",
            ZoomexNativeAccountTypes.CopyTrading => "COPYTRADING",
            _ => throw new ArgumentOutOfRangeException(nameof(accountType),
                accountType, null),
        };

    public static string ToBoardCode(this ZoomexCategories category)
        => category switch
        {
            ZoomexCategories.Spot => BoardCodes.Zoomex,
            ZoomexCategories.Linear => BoardCodes.ZoomexLinear,
            ZoomexCategories.Inverse => BoardCodes.ZoomexInverse,
            _ => throw new ArgumentOutOfRangeException(nameof(category),
                category, null),
        };

    public static ZoomexCategories ToZoomexCategories(this string boardCode)
        => boardCode.EqualsIgnoreCase(BoardCodes.Zoomex)
            ? ZoomexCategories.Spot
            : boardCode.EqualsIgnoreCase(BoardCodes.ZoomexLinear)
                ? ZoomexCategories.Linear
                : boardCode.EqualsIgnoreCase(BoardCodes.ZoomexInverse)
                    ? ZoomexCategories.Inverse
                    : throw new InvalidOperationException(
                        $"Unknown Zoomex board '{boardCode}'.");

    public static SecurityId ToStockSharp(this string symbol,
        ZoomexCategories category)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = category.ToBoardCode(),
        };

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static decimal? ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static string ToNative(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static string ToNative(this decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    public static long? ToLong(this string value)
        => long.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static DateTime ToUtcTime(this long value)
        => value > 0
            ? value.FromUnix(false)
            : default;

    public static DateTime? ToUtcTime(this string value)
        => value.ToLong() is long timestamp && timestamp > 0
            ? timestamp.ToUtcTime()
            : null;

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
                $"Zoomex does not support the {timeFrame} candle interval.");

    public static TimeSpan ToTimeFrame(this string interval)
        => interval switch
        {
            "1" => TimeSpan.FromMinutes(1),
            "3" => TimeSpan.FromMinutes(3),
            "5" => TimeSpan.FromMinutes(5),
            "15" => TimeSpan.FromMinutes(15),
            "30" => TimeSpan.FromMinutes(30),
            "60" => TimeSpan.FromHours(1),
            "120" => TimeSpan.FromHours(2),
            "240" => TimeSpan.FromHours(4),
            "360" => TimeSpan.FromHours(6),
            "720" => TimeSpan.FromHours(12),
            "D" => TimeSpan.FromDays(1),
            "W" => TimeSpan.FromDays(7),
            "M" => TimeSpan.FromDays(30),
            _ => throw new InvalidDataException(
                $"Zoomex returned unknown candle interval '{interval}'."),
        };

    public static Sides ToStockSharp(this string side)
        => side.EqualsIgnoreCase("Buy")
            ? Sides.Buy
            : side.EqualsIgnoreCase("Sell")
                ? Sides.Sell
                : throw new InvalidDataException(
                    $"Zoomex returned unknown side '{side}'.");

    public static Sides ToStockSharp(this ZoomexSides side)
        => side switch
        {
            ZoomexSides.Buy => Sides.Buy,
            ZoomexSides.Sell => Sides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static ZoomexSides ToNative(this Sides side)
        => side switch
        {
            Sides.Buy => ZoomexSides.Buy,
            Sides.Sell => ZoomexSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static OrderStates ToStockSharpOrderState(
        this ZoomexOrderStatuses status)
        => status switch
        {
            ZoomexOrderStatuses.Created or ZoomexOrderStatuses.New or
                ZoomexOrderStatuses.PartiallyFilled or
                ZoomexOrderStatuses.PendingCancel or
                ZoomexOrderStatuses.Untriggered => OrderStates.Active,
            ZoomexOrderStatuses.Filled or
                ZoomexOrderStatuses.PartiallyFilledCanceled or
                ZoomexOrderStatuses.Cancelled or
                ZoomexOrderStatuses.Canceled or
                ZoomexOrderStatuses.Deactivated or
                ZoomexOrderStatuses.Triggered or
                ZoomexOrderStatuses.Active => OrderStates.Done,
            ZoomexOrderStatuses.Rejected => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static OrderTypes ToStockSharpOrderType(
        this ZoomexOrderTypes value,
        string stopOrderType)
        => !stopOrderType.IsEmpty() &&
            !stopOrderType.EqualsIgnoreCase("UNKNOWN")
                ? OrderTypes.Conditional
                : value == ZoomexOrderTypes.Market
                    ? OrderTypes.Market
                    : OrderTypes.Limit;

    public static TimeInForce? ToStockSharpTimeInForce(this string value)
        => value?.Trim() switch
        {
            "GTC" or "PostOnly" => TimeInForce.PutInQueue,
            "IOC" => TimeInForce.CancelBalance,
            "FOK" => TimeInForce.MatchOrCancel,
            _ => null,
        };

    public static ZoomexTimeInForces ToNative(this TimeInForce? value,
        bool isPostOnly)
        => isPostOnly
            ? ZoomexTimeInForces.PostOnly
            : value switch
            {
                null or TimeInForce.PutInQueue =>
                    ZoomexTimeInForces.GoodTillCanceled,
                TimeInForce.CancelBalance =>
                    ZoomexTimeInForces.ImmediateOrCancel,
                TimeInForce.MatchOrCancel => ZoomexTimeInForces.FillOrKill,
                _ => throw new ArgumentOutOfRangeException(nameof(value),
                    value, null),
            };

    public static ZoomexTriggerByTypes ToNative(
        this ZoomexTriggerPriceTypes value)
        => value switch
        {
            ZoomexTriggerPriceTypes.LastPrice => ZoomexTriggerByTypes.LastPrice,
            ZoomexTriggerPriceTypes.IndexPrice => ZoomexTriggerByTypes.IndexPrice,
            ZoomexTriggerPriceTypes.MarkPrice => ZoomexTriggerByTypes.MarkPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                null),
        };

    public static int ToNative(this ZoomexPositionIndexes value)
        => value switch
        {
            ZoomexPositionIndexes.OneWay => 0,
            ZoomexPositionIndexes.HedgeBuy => 1,
            ZoomexPositionIndexes.HedgeSell => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                null),
        };

    public static int ToNative(this ZoomexTriggerDirections value)
        => value switch
        {
            ZoomexTriggerDirections.Rise => 1,
            ZoomexTriggerDirections.Fall => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                null),
        };

    public static ZoomexMarketUnits ToNative(
        this ZoomexSpotMarketUnits value)
        => value switch
        {
            ZoomexSpotMarketUnits.BaseCoin => ZoomexMarketUnits.BaseCoin,
            ZoomexSpotMarketUnits.QuoteCoin => ZoomexMarketUnits.QuoteCoin,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                null),
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;
}
