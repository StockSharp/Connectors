namespace StockSharp.Comdirect.Native;

static class ComdirectExtensions
{
    public static string ToNative(this ComdirectTanTypes value)
        => value switch
        {
            ComdirectTanTypes.Preferred => null,
            ComdirectTanTypes.PhotoTan => "P_TAN",
            ComdirectTanTypes.MobileTan => "M_TAN",
            ComdirectTanTypes.PhotoTanPush => "P_TAN_PUSH",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value), value, LocalizedStrings.InvalidValue),
        };

    public static decimal? ToDecimal(this ComdirectAmount value)
        => value?.Value.ToDecimal();

    public static decimal? ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var result)
                ? result : null;

    public static ComdirectAmount ToNativeAmount(this decimal value,
        string unit)
        => new()
        {
            Value = FormatAmount(value),
            Unit = unit,
        };

    internal static string FormatAmount(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static DateTime ParseTimestamp(this string value,
        DateTime fallback)
    {
        if (value.IsEmpty())
            return fallback;

        value = value.Replace(',', '.');
        if (value.Length >= 3 &&
            (value[^3] is '+' or '-') &&
            char.IsDigit(value[^2]) && char.IsDigit(value[^1]))
            value += ":00";

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var result)
                ? result.UtcDateTime : fallback;
    }

    public static DateTime? ParseDate(this string value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
            out var result)
                ? result : null;

    public static SecurityTypes ToSecurityType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "SHARE" or "SUBSCRIPTION_RIGHT" or
                "PROFIT_PART_CERTIFICATE" => SecurityTypes.Stock,
            "BONDS" => SecurityTypes.Bond,
            "ETF" => SecurityTypes.Etf,
            "FUND" => SecurityTypes.Fund,
            "WARRANT" or "CERTIFICATE" => SecurityTypes.Warrant,
            _ => SecurityTypes.Stock,
        };

    public static OrderStates ToOrderState(this string value)
        => value?.ToUpperInvariant() switch
        {
            "PENDING" or "WAITING" => OrderStates.Pending,
            "OPEN" or "PARTIALLY_EXECUTED" => OrderStates.Active,
            "EXECUTED" or "SETTLED" or "CANCELLED_USER" or
                "EXPIRED" or "CANCELLED_TRADE" => OrderStates.Done,
            "CANCELLED_SYSTEM" or "UNKNOWN" => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static OrderTypes ToOrderType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "MARKET" or "STOP_MARKET" or
                "TRAILING_STOP_MARKET" => OrderTypes.Market,
            _ => OrderTypes.Limit,
        };

    public static TimeInForce? ToTimeInForce(this string value)
        => value?.ToUpperInvariant() switch
        {
            "IOC" => TimeInForce.CancelBalance,
            "FOK" => TimeInForce.MatchOrCancel,
            _ => TimeInForce.PutInQueue,
        };

    public static string ToLimitExtension(this TimeInForce? value)
        => value switch
        {
            TimeInForce.CancelBalance => "IOC",
            TimeInForce.MatchOrCancel => "FOK",
            null or TimeInForce.PutInQueue => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value), value, LocalizedStrings.InvalidValue),
        };

    public static SecurityId ToSecurityId(this ComdirectInstrument instrument,
        string venueId = null)
    {
        if (instrument is null)
            return default;

        return new()
        {
            SecurityCode = instrument.Mnemonic
                .IsEmpty(instrument.Wkn)
                .IsEmpty(instrument.Isin)
                .IsEmpty(instrument.InstrumentId),
            BoardCode = venueId.IsEmpty("COMDIRECT"),
            Isin = instrument.Isin,
        };
    }

    public static SecurityId ToSecurityId(this string securityCode,
        string boardCode)
        => new()
        {
            SecurityCode = securityCode,
            BoardCode = boardCode.IsEmpty("COMDIRECT"),
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency : null;

    public static decimal? GetAveragePrice(this ComdirectOrder order)
    {
        var executions = order?.Executions ?? [];
        var total = executions.Sum(e =>
            e.ExecutedQuantity.ToDecimal() ?? 0m);
        if (total <= 0)
            return null;
        return executions.Sum(e =>
            (e.ExecutedQuantity.ToDecimal() ?? 0m) *
            (e.ExecutionPrice.ToDecimal() ?? 0m)) / total;
    }

    public static string GetCurrency(this ComdirectOrder order,
        string fallback)
        => order?.Limit?.Unit
            .IsEmpty(order?.Executions?.FirstOrDefault()?.ExecutionPrice?.Unit)
            .IsEmpty(order?.Instrument?.StaticData?.Currency)
            .IsEmpty(fallback);
}
