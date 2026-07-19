namespace StockSharp.Coinhako.Native;

static class CoinhakoExtensions
{
    public static CoinhakoSides ToCoinhako(this Sides side)
        => side == Sides.Buy ? CoinhakoSides.Buy : CoinhakoSides.Sell;

    public static Sides ToStockSharp(this CoinhakoSides side)
        => side == CoinhakoSides.Buy ? Sides.Buy : Sides.Sell;

    public static OrderStates ToStockSharp(this CoinhakoOrderStatuses status)
        => status switch
        {
            CoinhakoOrderStatuses.Open or CoinhakoOrderStatuses.Pending or
                CoinhakoOrderStatuses.Cancelling => OrderStates.Active,
            CoinhakoOrderStatuses.Completed or
                CoinhakoOrderStatuses.Cancelled => OrderStates.Done,
            _ => OrderStates.None,
        };

    public static string ToApi(this CoinhakoSides side)
        => side == CoinhakoSides.Buy ? "buy" : "sell";

    public static string ToApi(this CoinhakoExecutionTypes type)
        => type == CoinhakoExecutionTypes.Rfq ? "rfq" : "limit";

    public static string ToApi(this CoinhakoOrderStatuses status)
        => status switch
        {
            CoinhakoOrderStatuses.Open => "open",
            CoinhakoOrderStatuses.Cancelling => "cancelling",
            CoinhakoOrderStatuses.Completed => "completed",
            CoinhakoOrderStatuses.Cancelled => "cancelled",
            CoinhakoOrderStatuses.Pending => "pending",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                null),
        };

    public static DateTime FromCoinhakoTime(this long timestamp,
        DateTime fallback)
    {
        if (timestamp <= 0)
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        }
    }

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeCoinhakoSymbol(),
            BoardCode = BoardCodes.Coinhako,
        };

    public static string NormalizeCoinhakoSymbol(this string symbol)
        => symbol.ThrowIfEmpty(nameof(symbol)).Trim().Replace('_', '-')
            .Replace('/', '-').ToUpperInvariant();

    public static string ToCoinhakoSymbolKey(this string symbol)
        => symbol.NormalizeCoinhakoSymbol().Replace("-", string.Empty);

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;
}
