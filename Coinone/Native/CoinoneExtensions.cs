namespace StockSharp.Coinone.Native;

static class CoinoneExtensions
{
    private static readonly TimeSpan[] _timeFrames =
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
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static string NormalizeCurrency(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static string ToSymbol(string targetCurrency, string quoteCurrency)
        => $"{targetCurrency.NormalizeCurrency()}_{quoteCurrency.NormalizeCurrency()}";

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().Replace('/', '_')
            .Replace('-', '_').ToUpperInvariant();

    public static string CompactSymbol(this string value)
        => value.NormalizeSymbol().Replace("_", string.Empty);

    public static SecurityId ToStockSharp(string targetCurrency,
        string quoteCurrency)
        => new()
        {
            SecurityCode = ToSymbol(targetCurrency, quoteCurrency),
            BoardCode = BoardCodes.Coinone,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static CoinoneOrderSides ToCoinone(this Sides side)
        => side == Sides.Buy ? CoinoneOrderSides.Buy : CoinoneOrderSides.Sell;

    public static Sides ToStockSharp(this CoinoneOrderSides side)
        => side == CoinoneOrderSides.Buy ? Sides.Buy : Sides.Sell;

    public static Sides ToStockSharp(this CoinoneStreamSides side)
        => side == CoinoneStreamSides.Bid ? Sides.Buy : Sides.Sell;

    public static OrderTypes ToStockSharp(this CoinonePrivateOrderTypes type)
        => type switch
        {
            CoinonePrivateOrderTypes.Market => OrderTypes.Market,
            CoinonePrivateOrderTypes.StopLimit => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToStockSharp(this CoinoneOrderStatuses status)
        => status switch
        {
            CoinoneOrderStatuses.Live or
            CoinoneOrderStatuses.PartiallyFilled or
            CoinoneOrderStatuses.NotTriggered or
            CoinoneOrderStatuses.Triggered => OrderStates.Active,
            CoinoneOrderStatuses.Filled or
            CoinoneOrderStatuses.PartiallyCanceled or
            CoinoneOrderStatuses.Canceled or
            CoinoneOrderStatuses.NotTriggeredPartiallyCanceled or
            CoinoneOrderStatuses.NotTriggeredCanceled or
            CoinoneOrderStatuses.CanceledNoOrder or
            CoinoneOrderStatuses.CanceledLimitPriceExceeded or
            CoinoneOrderStatuses.CanceledUnderProductUnit => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static OrderStates ToStockSharp(this CoinoneStreamOrderStatuses status)
        => status switch
        {
            CoinoneStreamOrderStatuses.Wait or
            CoinoneStreamOrderStatuses.Watch or
            CoinoneStreamOrderStatuses.NotTriggered or
            CoinoneStreamOrderStatuses.Trade => OrderStates.Active,
            CoinoneStreamOrderStatuses.TradeDone or
            CoinoneStreamOrderStatuses.Cancel => OrderStates.Done,
            CoinoneStreamOrderStatuses.CancelPostOnly => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static SecurityStates ToStockSharp(this CoinoneMarket market)
        => market.MaintenanceStatus == CoinoneMaintenanceStatuses.Normal &&
            market.TradeStatus != CoinoneTradeStatuses.Disabled
                ? SecurityStates.Trading
                : SecurityStates.Stoped;

    public static string ToCoinoneInterval(this TimeSpan timeFrame)
        => timeFrame switch
        {
            _ when timeFrame == TimeSpan.FromMinutes(1) => "1m",
            _ when timeFrame == TimeSpan.FromMinutes(3) => "3m",
            _ when timeFrame == TimeSpan.FromMinutes(5) => "5m",
            _ when timeFrame == TimeSpan.FromMinutes(15) => "15m",
            _ when timeFrame == TimeSpan.FromMinutes(30) => "30m",
            _ when timeFrame == TimeSpan.FromHours(1) => "1h",
            _ when timeFrame == TimeSpan.FromHours(2) => "2h",
            _ when timeFrame == TimeSpan.FromHours(4) => "4h",
            _ when timeFrame == TimeSpan.FromHours(6) => "6h",
            _ when timeFrame == TimeSpan.FromDays(1) => "1d",
            _ when timeFrame == TimeSpan.FromDays(7) => "1w",
            _ when timeFrame == TimeSpan.FromDays(30) => "1mon",
            _ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
                "Coinone does not support this candle interval."),
        };

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
            "1d" => TimeSpan.FromDays(1),
            "1w" => TimeSpan.FromDays(7),
            "1mon" => TimeSpan.FromDays(30),
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
                "Coinone returned an unsupported candle interval."),
        };

    public static bool IsStreamingTimeFrame(this TimeSpan timeFrame)
        => timeFrame != TimeSpan.FromDays(30);

    public static string ToWire(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static DateTime FromCoinoneTimestamp(this long timestamp,
        DateTime fallback)
    {
        if (timestamp <= 0)
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        try
        {
            return (timestamp < 10_000_000_000L
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : DateTimeOffset.FromUnixTimeMilliseconds(timestamp)).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        }
    }

    public static string CreateUserOrderId(long transactionId,
        string userOrderId)
    {
        var value = userOrderId.IsEmpty()
            ? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
            : userOrderId.Trim().ToLowerInvariant();
        if (value.Length > 150 || value.Any(static character =>
            !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')))
            throw new InvalidOperationException(
                "Coinone user order IDs allow lowercase letters, digits, '-', '_', and '.', up to 150 characters.");
        return value;
    }
}
