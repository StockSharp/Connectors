namespace StockSharp.CoinJar.Native;

static class CoinJarExtensions
{
    private static readonly IReadOnlyDictionary<TimeSpan, string> _timeFrames =
        new Dictionary<TimeSpan, string>
        {
            [TimeSpan.FromMinutes(1)] = "1m",
            [TimeSpan.FromMinutes(5)] = "5m",
            [TimeSpan.FromMinutes(15)] = "15m",
            [TimeSpan.FromMinutes(30)] = "30m",
            [TimeSpan.FromHours(1)] = "1h",
            [TimeSpan.FromHours(4)] = "4h",
            [TimeSpan.FromHours(8)] = "8h",
            [TimeSpan.FromDays(1)] = "1d",
            [TimeSpan.FromDays(7)] = "1w",
        };

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

    public static string NormalizeProduct(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static SecurityId ToStockSharp(this string productId)
        => new()
        {
            SecurityCode = productId.NormalizeProduct(),
            BoardCode = BoardCodes.CoinJar,
        };

    public static string EscapePath(this string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    public static string ToCoinJarWire(this decimal value)
        => value.ToString("0.############################",
            CultureInfo.InvariantCulture);

    public static DateTime ToUtcTime(this DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    public static DateTime Align(this DateTime value, TimeSpan interval)
    {
        value = value.ToUtcTime();
        if (interval == TimeSpan.FromDays(7))
            return value.Date.AddDays(-((int)value.DayOfWeek + 6) % 7);
        var ticks = value.Ticks - DateTime.UnixEpoch.Ticks;
        return new DateTime(DateTime.UnixEpoch.Ticks +
            ticks / interval.Ticks * interval.Ticks, DateTimeKind.Utc);
    }

    public static DateTime GetCloseTime(this DateTime openTime,
        TimeSpan interval)
        => openTime.ToUtcTime() + interval;

    public static DateTime SubtractPeriods(this DateTime value,
        TimeSpan interval, int count)
        => value.ToUtcTime() - TimeSpan.FromTicks(interval.Ticks * count);

    public static string ToCoinJarInterval(this TimeSpan value)
        => _timeFrames.TryGetValue(value, out var interval)
            ? interval
            : throw new NotSupportedException(
                $"CoinJar does not support the {value} candle interval.");

    public static CoinJarSides ToCoinJar(this Sides value)
        => value == Sides.Buy ? CoinJarSides.Buy : CoinJarSides.Sell;

    public static Sides ToStockSharp(this CoinJarSides value)
        => value == CoinJarSides.Buy ? Sides.Buy : Sides.Sell;

    public static Sides? ToStockSharp(this CoinJarTakerSides value)
        => value switch
        {
            CoinJarTakerSides.Buy => Sides.Buy,
            CoinJarTakerSides.Sell => Sides.Sell,
            _ => null,
        };

    public static OrderTypes ToStockSharp(this CoinJarOrderTypes value)
        => value switch
        {
            CoinJarOrderTypes.Market => OrderTypes.Market,
            CoinJarOrderTypes.StopLimit => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static CoinJarTimeInForces ToCoinJar(this TimeInForce value,
        bool isPostOnly)
    {
        if (isPostOnly)
        {
            if (value is TimeInForce.CancelBalance or TimeInForce.MatchOrCancel)
                throw new InvalidOperationException(
                    "CoinJar maker-or-cancel orders cannot use IOC or FOK.");
            return CoinJarTimeInForces.MOC;
        }
        return value switch
        {
            TimeInForce.PutInQueue => CoinJarTimeInForces.GTC,
            TimeInForce.CancelBalance => CoinJarTimeInForces.IOC,
            TimeInForce.MatchOrCancel => throw new NotSupportedException(
                "CoinJar does not support fill-or-kill orders."),
            _ => CoinJarTimeInForces.GTC,
        };
    }

    public static TimeInForce ToStockSharp(this CoinJarTimeInForces value)
        => value switch
        {
            CoinJarTimeInForces.IOC => TimeInForce.CancelBalance,
            _ => TimeInForce.PutInQueue,
        };

    public static OrderStates ToStockSharp(this CoinJarOrderStatuses value)
        => value switch
        {
            CoinJarOrderStatuses.Filled => OrderStates.Done,
            CoinJarOrderStatuses.Cancelled => OrderStates.Done,
            _ => OrderStates.Active,
        };

    public static SecurityStates ToSecurityState(this string value)
        => value.EqualsIgnoreCase("continuous")
            ? SecurityStates.Trading
            : SecurityStates.Stoped;

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static CoinJarPriceLevel GetPriceLevel(this CoinJarProduct product,
        decimal price)
    {
        ArgumentNullException.ThrowIfNull(product);
        var level = (product.PriceLevels ?? []).FirstOrDefault(value =>
            value is not null && price >= value.MinimumPrice &&
            price <= value.MaximumPrice);
        return level ?? throw new InvalidOperationException(
            $"Price {price} is outside CoinJar product '{product.Id}' levels.");
    }
}
