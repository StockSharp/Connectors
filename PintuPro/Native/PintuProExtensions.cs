namespace StockSharp.PintuPro.Native;

static class PintuProExtensions
{
    public static string NormalizeCurrency(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static string NormalizeSymbol(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().Replace('_', '-')
            .Replace('/', '-').ToUpperInvariant();

    public static string CompactSymbol(this string value)
        => value.NormalizeSymbol().Replace("-", string.Empty);

    public static (string BaseCurrency, string QuoteCurrency) SplitSymbol(
        this string value)
    {
        var symbol = value.NormalizeSymbol();
        var separator = symbol.IndexOf('-');
        if (separator <= 0 || separator >= symbol.Length - 1)
            throw new InvalidOperationException(
                $"Invalid Pintu Pro trading-pair symbol '{value}'.");
        return (symbol[..separator].NormalizeCurrency(),
            symbol[(separator + 1)..].NormalizeCurrency());
    }

    public static SecurityId ToStockSharp(this string symbol)
        => new()
        {
            SecurityCode = symbol.NormalizeSymbol(),
            BoardCode = BoardCodes.PintuPro,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static PintuProSides ToPintuPro(this Sides side)
        => side == Sides.Buy ? PintuProSides.Buy : PintuProSides.Sell;

    public static Sides ToStockSharp(this PintuProSides side)
        => side == PintuProSides.Buy ? Sides.Buy : Sides.Sell;

    public static PintuProOrderTypes ToPintuPro(this OrderTypes type)
        => type switch
        {
            OrderTypes.Limit => PintuProOrderTypes.Limit,
            OrderTypes.Market => PintuProOrderTypes.Market,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                "Pintu Pro supports limit and market orders only."),
        };

    public static OrderTypes ToStockSharp(this PintuProOrderTypes type)
        => type == PintuProOrderTypes.Market
            ? OrderTypes.Market
            : OrderTypes.Limit;

    public static PintuProTimeInForces ToPintuPro(this TimeInForce? value,
        PintuProOrderTypes orderType)
    {
        if (orderType == PintuProOrderTypes.Market)
        {
            if (value is not null and not TimeInForce.CancelBalance)
                throw new InvalidOperationException(
                    "Pintu Pro market orders do not accept a time-in-force.");
            return PintuProTimeInForces.ImmediateOrCancel;
        }

        return value switch
        {
            null or TimeInForce.PutInQueue =>
                PintuProTimeInForces.GoodTillCanceled,
            TimeInForce.CancelBalance =>
                PintuProTimeInForces.ImmediateOrCancel,
            TimeInForce.MatchOrCancel => PintuProTimeInForces.FillOrKill,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                "Pintu Pro does not support this time-in-force."),
        };
    }

    public static TimeInForce ToStockSharp(this PintuProTimeInForces? value)
        => value switch
        {
            PintuProTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
            PintuProTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
            _ => TimeInForce.PutInQueue,
        };

    public static TimeInForce ToStockSharp(this PintuProTimeInForces value)
        => ((PintuProTimeInForces?)value).ToStockSharp();

    public static OrderStates ToStockSharp(this PintuProOrderStatuses status)
        => status switch
        {
            PintuProOrderStatuses.Placed or
            PintuProOrderStatuses.PartiallyFilled => OrderStates.Active,
            PintuProOrderStatuses.Filled or
            PintuProOrderStatuses.Canceled => OrderStates.Done,
            PintuProOrderStatuses.Rejected => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static string ToApiValue(this PintuProSides value)
        => value == PintuProSides.Buy ? "BUY" : "SELL";

    public static string ToApiValue(this PintuProOrderTypes value)
        => value == PintuProOrderTypes.Market ? "MARKET" : "LIMIT";

    public static string ToApiValue(this PintuProTimeInForces value)
        => value switch
        {
            PintuProTimeInForces.ImmediateOrCancel => "IOC",
            PintuProTimeInForces.FillOrKill => "FOK",
            _ => "GTC",
        };

    public static string ToApiValue(
        this PintuProExecutionInstructions value)
        => value == PintuProExecutionInstructions.PostOnly
            ? "POST_ONLY"
            : throw new ArgumentOutOfRangeException(nameof(value), value, null);

    public static string ToApiValue(this PintuProOrderStatuses value)
        => value switch
        {
            PintuProOrderStatuses.Placed => "PLACED",
            PintuProOrderStatuses.Canceled => "CANCELED",
            PintuProOrderStatuses.Rejected => "REJECTED",
            PintuProOrderStatuses.PartiallyFilled => "PARTIALLY_FILLED",
            PintuProOrderStatuses.Filled => "FILLED",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

    public static string ToWire(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static DateTime FromPintuProTimestamp(this long timestamp,
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
        if (value.Length > 128 || value.Any(static character =>
            char.IsControl(character)))
            throw new InvalidOperationException(
                "Pintu Pro client order IDs must be 1 through 128 visible characters.");
        return value;
    }

    public static void ValidateOrderValue(this PintuProSymbol market,
        decimal price, decimal size, decimal? notional)
    {
        if (size > 0)
        {
            ValidateStep(size, market.QuantityTickSize, "size");
            if (market.MinimumSize is > 0 && size < market.MinimumSize)
                throw new InvalidOperationException(
                    $"Pintu Pro order size is below {market.MinimumSize}.");
            if (market.MaximumSize is > 0 && size > market.MaximumSize)
                throw new InvalidOperationException(
                    $"Pintu Pro order size exceeds {market.MaximumSize}.");
        }

        if (price > 0)
        {
            ValidateStep(price, market.PriceTickSize, "price");
            if (market.MinimumPrice is > 0 && price < market.MinimumPrice)
                throw new InvalidOperationException(
                    $"Pintu Pro order price is below {market.MinimumPrice}.");
            if (market.MaximumPrice is > 0 && price > market.MaximumPrice)
                throw new InvalidOperationException(
                    $"Pintu Pro order price exceeds {market.MaximumPrice}.");
        }

        var value = notional ?? (price > 0 && size > 0 ? price * size : 0m);
        if (value > 0)
        {
            if (market.MinimumValue is > 0 && value < market.MinimumValue)
                throw new InvalidOperationException(
                    $"Pintu Pro order value is below {market.MinimumValue}.");
            if (market.MaximumValue is > 0 && value > market.MaximumValue)
                throw new InvalidOperationException(
                    $"Pintu Pro order value exceeds {market.MaximumValue}.");
        }
    }

    private static void ValidateStep(decimal value, decimal step, string name)
    {
        if (step > 0 && value % step != 0)
            throw new InvalidOperationException(
                $"Pintu Pro order {name} must be divisible by {step}.");
    }
}
