namespace StockSharp.BTCMarkets.Native;

static class BTCMarketsExtensions
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
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    public static string NormalizeMarket(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static SecurityId ToStockSharp(this string marketId)
        => new()
        {
            SecurityCode = marketId.NormalizeMarket(),
            BoardCode = BoardCodes.BTCMarkets,
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

    public static string ToWire(this decimal value)
        => value.ToString("0.#############################",
            CultureInfo.InvariantCulture);

    public static string ToWire(this TimeSpan timeFrame)
        => timeFrame == TimeSpan.FromMinutes(1) ? "1m"
            : timeFrame == TimeSpan.FromMinutes(3) ? "3m"
            : timeFrame == TimeSpan.FromMinutes(5) ? "5m"
            : timeFrame == TimeSpan.FromMinutes(15) ? "15m"
            : timeFrame == TimeSpan.FromMinutes(30) ? "30m"
            : timeFrame == TimeSpan.FromHours(1) ? "1h"
            : timeFrame == TimeSpan.FromHours(2) ? "2h"
            : timeFrame == TimeSpan.FromHours(3) ? "3h"
            : timeFrame == TimeSpan.FromHours(4) ? "4h"
            : timeFrame == TimeSpan.FromHours(6) ? "6h"
            : timeFrame == TimeSpan.FromDays(1) ? "1d"
            : timeFrame == TimeSpan.FromDays(7) ? "1w"
            : timeFrame == TimeSpan.FromDays(30) ? "1mo"
            : throw new NotSupportedException(
                $"BTC Markets does not support the {timeFrame} candle interval.");

    public static DateTime ToUtcTime(this DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    public static DateTime Align(this DateTime value, TimeSpan interval)
    {
        value = value.ToUtcTime();
        if (interval == TimeSpan.FromDays(30))
            return new(value.Year, value.Month, 1, 0, 0, 0,
                DateTimeKind.Utc);
        if (interval == TimeSpan.FromDays(7))
            return value.Date.AddDays(-((int)value.DayOfWeek + 6) % 7);
        var ticks = value.Ticks - DateTime.UnixEpoch.Ticks;
        return new DateTime(DateTime.UnixEpoch.Ticks +
            ticks / interval.Ticks * interval.Ticks, DateTimeKind.Utc);
    }

    public static DateTime GetCloseTime(this DateTime openTime,
        TimeSpan interval)
        => interval == TimeSpan.FromDays(30)
            ? openTime.ToUtcTime().AddMonths(1)
            : openTime.ToUtcTime() + interval;

    public static DateTime SubtractPeriods(this DateTime value,
        TimeSpan interval, int count)
        => interval == TimeSpan.FromDays(30)
            ? value.ToUtcTime().AddMonths(-count)
            : value.ToUtcTime() - TimeSpan.FromTicks(interval.Ticks * count);

    public static Sides ToStockSharp(this BTCMarketsSides side)
        => side == BTCMarketsSides.Bid ? Sides.Buy : Sides.Sell;

    public static BTCMarketsSides ToBTCMarkets(this Sides side)
        => side switch
        {
            Sides.Buy => BTCMarketsSides.Bid,
            Sides.Sell => BTCMarketsSides.Ask,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

    public static OrderTypes ToStockSharp(this BTCMarketsOrderTypes? type)
        => type switch
        {
            BTCMarketsOrderTypes.Market or BTCMarketsOrderTypes.Stop or
                BTCMarketsOrderTypes.TakeProfit => OrderTypes.Market,
            BTCMarketsOrderTypes.StopLimit => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToStockSharp(this BTCMarketsOrderStatuses? status)
        => status switch
        {
            BTCMarketsOrderStatuses.Accepted or BTCMarketsOrderStatuses.Placed or
                BTCMarketsOrderStatuses.PartiallyMatched => OrderStates.Active,
            BTCMarketsOrderStatuses.FullyMatched or
                BTCMarketsOrderStatuses.Cancelled or
                BTCMarketsOrderStatuses.PartiallyCancelled => OrderStates.Done,
            BTCMarketsOrderStatuses.Failed => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static BTCMarketsTimeInForces ToBTCMarkets(
        this TimeInForce? timeInForce)
        => timeInForce switch
        {
            null or TimeInForce.PutInQueue => BTCMarketsTimeInForces.GTC,
            TimeInForce.CancelBalance => BTCMarketsTimeInForces.IOC,
            TimeInForce.MatchOrCancel => BTCMarketsTimeInForces.FOK,
            _ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
                timeInForce, "BTC Markets supports GTC, IOC, and FOK only."),
        };

    public static TimeInForce? ToStockSharp(
        this BTCMarketsTimeInForces? timeInForce)
        => timeInForce switch
        {
            BTCMarketsTimeInForces.IOC => TimeInForce.CancelBalance,
            BTCMarketsTimeInForces.FOK => TimeInForce.MatchOrCancel,
            BTCMarketsTimeInForces.GTC => TimeInForce.PutInQueue,
            _ => null,
        };

    public static SecurityStates ToStockSharp(
        this BTCMarketsMarketStatuses status)
        => status is BTCMarketsMarketStatuses.Online or
            BTCMarketsMarketStatuses.PostOnly or
            BTCMarketsMarketStatuses.LimitOnly
            ? SecurityStates.Trading
            : SecurityStates.Stoped;

    public static string EscapePath(this string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());
}
