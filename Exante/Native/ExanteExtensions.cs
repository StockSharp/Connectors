namespace StockSharp.Exante.Native;

static class ExanteExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
        TimeSpan.FromDays(1),
    ];

    public static decimal? ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var result)
                ? result : null;

    internal static string FormatDecimal(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static long ToNativeDuration(this TimeSpan value)
        => value switch
        {
            { TotalSeconds: 60 } => 60,
            { TotalSeconds: 300 } => 300,
            { TotalSeconds: 600 } => 600,
            { TotalSeconds: 900 } => 900,
            { TotalSeconds: 1800 } => 1800,
            { TotalSeconds: 3600 } => 3600,
            { TotalSeconds: 14400 } => 14400,
            { TotalSeconds: 21600 } => 21600,
            { TotalSeconds: 86400 } => 86400,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "EXANTE does not support this candle time frame."),
        };

    public static SecurityTypes ToSecurityType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "FX_SPOT" or "CURRENCY" => SecurityTypes.Currency,
            "INDEX" => SecurityTypes.Index,
            "STOCK" => SecurityTypes.Stock,
            "BOND" => SecurityTypes.Bond,
            "FUND" => SecurityTypes.Fund,
            "FUTURE" or "CALENDAR_SPREAD" => SecurityTypes.Future,
            "OPTION" => SecurityTypes.Option,
            "CFD" => SecurityTypes.Cfd,
            _ => SecurityTypes.Stock,
        };

    public static OrderStates ToOrderState(this string value)
        => value?.ToLowerInvariant() switch
        {
            "placing" or "pending" => OrderStates.Pending,
            "working" => OrderStates.Active,
            "cancelled" or "filled" => OrderStates.Done,
            "rejected" => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static OrderTypes ToOrderType(this string value)
        => value?.ToLowerInvariant() switch
        {
            "market" => OrderTypes.Market,
            "limit" or "iceberg" or "twap" => OrderTypes.Limit,
            "stop" or "stop_limit" or "trailing_stop" =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static string ToNativeOrderType(this OrderTypes value)
        => value switch
        {
            OrderTypes.Market => "market",
            OrderTypes.Limit => "limit",
            _ => throw new NotSupportedException(
                $"EXANTE standard order registration does not support {value}."),
        };

    public static string ToNativeDuration(this TimeInForce? value,
        DateTime? tillDate)
        => tillDate is not null ? "good_till_time" :
            value switch
            {
                TimeInForce.CancelBalance => "immediate_or_cancel",
                TimeInForce.MatchOrCancel => "fill_or_kill",
                null or TimeInForce.PutInQueue => "good_till_cancel",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(value), value, LocalizedStrings.InvalidValue),
            };

    public static TimeInForce? ToTimeInForce(this string value)
        => value?.ToLowerInvariant() switch
        {
            "immediate_or_cancel" => TimeInForce.CancelBalance,
            "fill_or_kill" => TimeInForce.MatchOrCancel,
            _ => TimeInForce.PutInQueue,
        };

    public static DateTime FromUnixMilliseconds(this long value)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UtcNow;
        }
    }

    public static long ToUnixMilliseconds(this DateTime value)
        => new DateTimeOffset(value.ToUniversalTime())
            .ToUnixTimeMilliseconds();

    public static DateTime ParseTimestamp(this string value,
        DateTime fallback)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var result)
                ? result.UtcDateTime : fallback;

    public static DateTime? ParseTimestamp(this string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var result)
                ? result.UtcDateTime : null;

    public static DateTime? ParseExpiration(this string value)
        => long.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var timestamp) && timestamp > 0
                ? timestamp.FromUnixMilliseconds()
                : value.ParseTimestamp();

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency : null;

    public static SecurityId ToSecurityId(this ExanteSymbol symbol)
    {
        if (symbol is null)
            return default;

        return new()
        {
            SecurityCode = symbol.Ticker.IsEmpty(symbol.SymbolId),
            BoardCode = symbol.Exchange.IsEmpty("EXANTE"),
            Native = symbol.SymbolId,
            Isin = symbol.Identifiers?.Isin,
        };
    }

    public static SecurityId ToSecurityId(this string symbolId)
    {
        if (symbolId.IsEmpty())
            return default;
        var dot = symbolId.LastIndexOf('.');
        return new()
        {
            SecurityCode = dot > 0 ? symbolId[..dot] : symbolId,
            BoardCode = dot > 0 ? symbolId[(dot + 1)..] : "EXANTE",
            Native = symbolId,
        };
    }

    public static string ToNativeSymbol(this SecurityId securityId)
    {
        var native = securityId.Native as string;
        if (!native.IsEmpty())
            return native;
        var code = securityId.SecurityCode.ThrowIfEmpty(
            nameof(securityId.SecurityCode));
        if (code.Contains('.'))
            return code;
        return securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase("EXANTE")
                ? code
                : $"{code}.{securityId.BoardCode}";
    }

    public static string GetId(this ExanteOrder order)
        => order?.OrderId.IsEmpty(order?.Id);

    public static decimal? GetExecutedVolume(this ExanteOrder order)
        => order?.OrderState?.Fills?
            .Sum(fill => fill.Quantity.ToDecimal() ?? 0m);

    public static decimal? GetAveragePrice(this ExanteOrder order)
    {
        var fills = order?.OrderState?.Fills ?? [];
        var volume = fills.Sum(fill => fill.Quantity.ToDecimal() ?? 0m);
        if (volume <= 0)
            return null;
        return fills.Sum(fill =>
            (fill.Quantity.ToDecimal() ?? 0m) *
            (fill.Price.ToDecimal() ?? 0m)) / volume;
    }
}
