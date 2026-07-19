namespace StockSharp.Rain.Native;

static class RainExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
        TimeSpan.FromDays(1),
    ];

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.Rain,
        };

    public static string ToWire(this decimal value)
        => value.ToString("0.#############################",
            CultureInfo.InvariantCulture);

    public static string ToWire(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1m"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5m"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15m"
            : timeFrame == TimeSpan.FromHours(1) ? "1h"
            : timeFrame == TimeSpan.FromHours(6) ? "6h"
            : timeFrame == TimeSpan.FromDays(1) ? "24h"
            : throw new NotSupportedException(
                $"Rain does not support the {timeFrame} candle interval.");

    public static string ToWire(this RainSocketChannels channel)
        => channel switch
        {
            RainSocketChannels.Trades => "trades",
            RainSocketChannels.OrderBook => "orderBook",
            RainSocketChannels.ProductSummary => "productSummary",
            RainSocketChannels.MarketSummary => "marketSummary",
            RainSocketChannels.Candles => "candles",
            RainSocketChannels.AccountBalance => "accountBalance",
            RainSocketChannels.Orders => "orders",
            _ => throw new ArgumentOutOfRangeException(nameof(channel),
                channel, null),
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static DateTime ToUtcTime(this DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    public static Sides ToStockSharp(this RainSides side)
        => side == RainSides.Buy ? Sides.Buy : Sides.Sell;

    public static RainSides ToRain(this Sides side)
        => side switch
        {
            Sides.Buy => RainSides.Buy,
            Sides.Sell => RainSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    public static OrderTypes ToStockSharp(this RainOrderTypes? type)
        => type == RainOrderTypes.Market ? OrderTypes.Market :
            OrderTypes.Limit;

    public static OrderStates ToStockSharp(this RainOrderStatuses? status)
        => status switch
        {
            RainOrderStatuses.Created or RainOrderStatuses.Starting or
                RainOrderStatuses.Open => OrderStates.Active,
            RainOrderStatuses.Closed or RainOrderStatuses.Cancelled =>
                OrderStates.Done,
            _ => OrderStates.None,
        };

    public static DateTime AddPeriods(this DateTime value,
        TimeSpan interval, int count)
        => value.ToUtcTime() + TimeSpan.FromTicks(interval.Ticks * count);

    public static string EscapePath(this string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());
}
