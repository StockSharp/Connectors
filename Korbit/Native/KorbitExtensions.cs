namespace StockSharp.Korbit.Native;

static class KorbitExtensions
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
    ];

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static string NormalizeCurrency(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().Replace('/', '_')
            .Replace('-', '_').ToLowerInvariant();

    public static string CompactSymbol(this string value)
        => value.NormalizeSymbol().Replace("_", string.Empty);

    public static (string BaseCurrency, string QuoteCurrency) SplitSymbol(
        this string value)
    {
        var symbol = value.NormalizeSymbol();
        var separator = symbol.IndexOf('_');
        if (separator <= 0 || separator >= symbol.Length - 1)
            throw new InvalidOperationException(
                $"Invalid Korbit trading-pair symbol '{value}'.");
        return (symbol[..separator].NormalizeCurrency(),
            symbol[(separator + 1)..].NormalizeCurrency());
    }

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol().ToUpperInvariant(),
            BoardCode = BoardCodes.Korbit,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static KorbitOrderSides ToKorbit(this Sides side)
        => side == Sides.Buy ? KorbitOrderSides.Buy : KorbitOrderSides.Sell;

    public static string ToWire(this KorbitOrderSides side)
        => side == KorbitOrderSides.Buy ? "buy" : "sell";

    public static Sides ToStockSharp(this KorbitOrderSides side)
        => side == KorbitOrderSides.Buy ? Sides.Buy : Sides.Sell;

    public static OrderTypes ToStockSharp(this KorbitOrderTypes type)
        => type == KorbitOrderTypes.Market
            ? OrderTypes.Market
            : OrderTypes.Limit;

    public static string ToWire(this KorbitOrderTypes type)
        => type switch
        {
            KorbitOrderTypes.Market => "market",
            KorbitOrderTypes.Best => "best",
            _ => "limit",
        };

    public static string ToWire(this KorbitTimeInForces value)
        => value switch
        {
            KorbitTimeInForces.ImmediateOrCancel => "ioc",
            KorbitTimeInForces.FillOrKill => "fok",
            KorbitTimeInForces.PostOnly => "po",
            _ => "gtc",
        };

    public static OrderStates ToStockSharp(this KorbitOrderStatuses status)
        => status switch
        {
            KorbitOrderStatuses.Pending or
            KorbitOrderStatuses.Open or
            KorbitOrderStatuses.PartiallyFilled => OrderStates.Active,
            KorbitOrderStatuses.Filled or
            KorbitOrderStatuses.Canceled or
            KorbitOrderStatuses.PartiallyFilledCanceled => OrderStates.Done,
            KorbitOrderStatuses.Expired => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static OrderStates ToStockSharp(
        this KorbitStreamOrderStatuses status)
        => status switch
        {
            KorbitStreamOrderStatuses.Pending or
            KorbitStreamOrderStatuses.Unfilled or
            KorbitStreamOrderStatuses.PartiallyFilled => OrderStates.Active,
            KorbitStreamOrderStatuses.Filled or
            KorbitStreamOrderStatuses.Canceled or
            KorbitStreamOrderStatuses.PartiallyFilledCanceled =>
                OrderStates.Done,
            KorbitStreamOrderStatuses.Expired => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static KorbitTimeInForces ToKorbit(this TimeInForce? timeInForce,
        bool isPostOnly, KorbitOrderTypes orderType)
    {
        if (isPostOnly)
        {
            if (orderType == KorbitOrderTypes.Market)
                throw new InvalidOperationException(
                    "A Korbit market order cannot be post-only.");
            if (timeInForce is not null and not TimeInForce.PutInQueue)
                throw new InvalidOperationException(
                    "Korbit post-only orders cannot use IOC or FOK.");
            return KorbitTimeInForces.PostOnly;
        }

        if (orderType == KorbitOrderTypes.Market)
        {
            if (timeInForce is not null and not TimeInForce.CancelBalance)
                throw new InvalidOperationException(
                    "Korbit market orders support IOC only.");
            return KorbitTimeInForces.ImmediateOrCancel;
        }

        return timeInForce switch
        {
            null or TimeInForce.PutInQueue => KorbitTimeInForces.GoodTillCanceled,
            TimeInForce.CancelBalance => KorbitTimeInForces.ImmediateOrCancel,
            TimeInForce.MatchOrCancel => KorbitTimeInForces.FillOrKill,
            _ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
                timeInForce, "Korbit does not support this time-in-force."),
        };
    }

    public static TimeInForce ToStockSharp(this KorbitTimeInForces value)
        => value switch
        {
            KorbitTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
            KorbitTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
            _ => TimeInForce.PutInQueue,
        };

    public static string ToKorbitInterval(this TimeSpan timeFrame)
        => timeFrame switch
        {
            _ when timeFrame == TimeSpan.FromMinutes(1) => "1",
            _ when timeFrame == TimeSpan.FromMinutes(5) => "5",
            _ when timeFrame == TimeSpan.FromMinutes(15) => "15",
            _ when timeFrame == TimeSpan.FromMinutes(30) => "30",
            _ when timeFrame == TimeSpan.FromHours(1) => "60",
            _ when timeFrame == TimeSpan.FromHours(4) => "240",
            _ when timeFrame == TimeSpan.FromDays(1) => "1D",
            _ when timeFrame == TimeSpan.FromDays(7) => "1W",
            _ => throw new ArgumentOutOfRangeException(nameof(timeFrame),
                timeFrame, "Korbit does not support this candle interval."),
        };

    public static string ToWire(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static DateTime FromKorbitTimestamp(this long timestamp,
        DateTime fallback)
    {
        if (timestamp <= 0)
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        }
    }

    public static string ValidateClientOrderId(this string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (value.Length > 36 || value.Any(static character =>
            !(char.IsAsciiLetterOrDigit(character) ||
                character is '.' or ':' or '_' or '-')))
            throw new InvalidOperationException(
                "Korbit client order IDs allow ASCII letters, digits, '.', ':', '_', and '-', up to 36 characters.");
        return value;
    }
}
