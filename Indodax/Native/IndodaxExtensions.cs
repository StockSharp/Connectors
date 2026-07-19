namespace StockSharp.Indodax.Native;

static class IndodaxExtensions
{
    public static string NormalizeCurrency(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

    public static string NormalizePairId(this string value)
        => value.ThrowIfEmpty(nameof(value)).Trim().Replace("_", string.Empty)
            .Replace("-", string.Empty).Replace("/", string.Empty)
            .ToLowerInvariant();

    public static SecurityId ToStockSharp(this string pairId)
        => new()
        {
            SecurityCode = pairId.NormalizePairId().ToUpperInvariant(),
            BoardCode = BoardCodes.Indodax,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;

    public static IndodaxSides ToIndodax(this Sides side)
        => side == Sides.Buy ? IndodaxSides.Buy : IndodaxSides.Sell;

    public static Sides ToStockSharp(this IndodaxSides side)
        => side == IndodaxSides.Buy ? Sides.Buy : Sides.Sell;

    public static IndodaxOrderTypes ToIndodax(this OrderTypes type)
        => type switch
        {
            OrderTypes.Limit => IndodaxOrderTypes.Limit,
            OrderTypes.Market => IndodaxOrderTypes.Market,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                "Indodax supports limit and market orders only."),
        };

    public static OrderTypes ToStockSharp(this IndodaxOrderTypes type)
        => type == IndodaxOrderTypes.Market
            ? OrderTypes.Market
            : OrderTypes.Limit;

    public static OrderStates ToStockSharp(this IndodaxOrderStatuses status)
        => status switch
        {
            IndodaxOrderStatuses.New or IndodaxOrderStatuses.Open or
            IndodaxOrderStatuses.Fill or IndodaxOrderStatuses.Pending =>
                OrderStates.Active,
            IndodaxOrderStatuses.Done or IndodaxOrderStatuses.Filled or
            IndodaxOrderStatuses.Cancelled => OrderStates.Done,
            IndodaxOrderStatuses.Rejected => OrderStates.Failed,
            _ => OrderStates.None,
        };

    public static DateTime FromIndodaxSeconds(this long timestamp,
        DateTime fallback)
    {
        if (timestamp <= 0)
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback.Kind == DateTimeKind.Utc
                ? fallback
                : fallback.ToUniversalTime();
        }
    }

    public static DateTime FromIndodaxMilliseconds(this long timestamp,
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
            !char.IsLetterOrDigit(character) && character is not '_' and not '-'))
            throw new InvalidOperationException(
                "Indodax client order IDs must contain 1 through 36 alphanumeric, underscore, or hyphen characters.");
        return value;
    }

    public static decimal GetVolumeStep(this IndodaxPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        if (pair.PriceRound is < 0 or > 28)
            return 0m;
        var step = 1m;
        for (var i = 0; i < pair.PriceRound; i++)
            step /= 10m;
        return step;
    }

    public static void ValidateOrder(this IndodaxPair pair, decimal price,
        decimal baseVolume, decimal? quoteAmount)
    {
        ArgumentNullException.ThrowIfNull(pair);
        var volumeStep = pair.GetVolumeStep();
        if (baseVolume > 0)
        {
            if (volumeStep > 0 && baseVolume % volumeStep != 0)
                throw new InvalidOperationException(
                    $"Indodax order volume must be divisible by {volumeStep}.");
            if (pair.MinimumBaseVolume > 0 &&
                baseVolume < pair.MinimumBaseVolume)
                throw new InvalidOperationException(
                    $"Indodax order volume is below {pair.MinimumBaseVolume}.");
        }

        if (price > 0 && pair.PricePrecision > 0 &&
            price % pair.PricePrecision != 0)
            throw new InvalidOperationException(
                $"Indodax order price must be divisible by {pair.PricePrecision}.");

        var value = quoteAmount ??
            (price > 0 && baseVolume > 0 ? price * baseVolume : 0m);
        if (value > 0 && pair.MinimumQuoteValue > 0 &&
            value < pair.MinimumQuoteValue)
            throw new InvalidOperationException(
                $"Indodax order value is below {pair.MinimumQuoteValue} {pair.QuoteCurrency.ToUpperInvariant()}.");
    }

    public static string ToIndodaxTimeFrame(this TimeSpan timeFrame)
        => timeFrame switch
        {
            { TotalMinutes: 1 } => "1",
            { TotalMinutes: 15 } => "15",
            { TotalMinutes: 30 } => "30",
            { TotalMinutes: 60 } => "60",
            { TotalMinutes: 240 } => "240",
            { TotalDays: 1 } => "1D",
            { TotalDays: 3 } => "3D",
            { TotalDays: 7 } => "1W",
            _ => throw new ArgumentOutOfRangeException(nameof(timeFrame),
                timeFrame, "Unsupported Indodax candle time frame."),
        };
}
