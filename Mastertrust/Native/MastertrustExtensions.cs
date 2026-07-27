namespace StockSharp.Mastertrust.Native;

static class MastertrustExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

    public static string ToInstrumentKey(this string exchange, string token)
        => $"{NormalizeExchange(exchange)}:{token.ThrowIfEmpty(nameof(token))}";

    public static (string exchange, string token) ParseInstrumentKey(this string key)
    {
        var separator = key?.IndexOf(':') ?? -1;
        if (separator <= 0 || separator == key.Length - 1)
            throw new FormatException($"Invalid Mastertrust instrument key '{key}'.");

        var exchange = NormalizeExchange(key[..separator]);
        exchange.ToBoardCode();
        return (exchange, key[(separator + 1)..]);
    }

    public static string ToInstrumentKey(this SecurityId securityId)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            var (exchange, token) = native.ParseInstrumentKey();
            return exchange.ToInstrumentKey(token);
        }

        if (securityId.SecurityCode?.Split(':') is { Length: 2 } parts)
            return parts[0].ToInstrumentKey(parts[1]);

        throw new InvalidOperationException(
            "Mastertrust token is missing. Select the security through Mastertrust lookup so SecurityId.Native contains exchange:token.");
    }

    public static string ToBoardCode(this string exchange)
        => NormalizeExchange(exchange) switch
        {
            "NSE" => "NSE",
            "BSE" => "BSE",
            "NFO" => "NFO",
            "BFO" => "BFO",
            "MCX" => "MCX",
            "CDS" => "CDS",
            "BCD" => "BCD",
            "MCXSX" => "MCXSX",
            "NSE_INDICES" => "NSE",
            _ => throw new ArgumentOutOfRangeException(
                nameof(exchange), exchange, "Unsupported Mastertrust exchange."),
        };

    public static byte ToExchangeCode(this string exchange)
        => NormalizeExchange(exchange) switch
        {
            "NSE" => 1,
            "NFO" => 2,
            "CDS" => 3,
            "MCX" => 4,
            "MCXSX" => 5,
            "BSE" => 6,
            "BFO" => 7,
            "BCD" => 8,
            "NSE_INDICES" => 9,
            _ => throw new ArgumentOutOfRangeException(
                nameof(exchange), exchange, "Unsupported Mastertrust streaming exchange."),
        };

    public static string ToExchange(this byte code)
        => code switch
        {
            1 => "NSE",
            2 => "NFO",
            3 => "CDS",
            4 => "MCX",
            5 => "MCXSX",
            6 => "BSE",
            7 => "BFO",
            8 => "BCD",
            9 => "NSE_INDICES",
            _ => throw new ArgumentOutOfRangeException(
                nameof(code), code, "Unsupported Mastertrust streaming exchange code."),
        };

    public static decimal GetPriceDivisor(this byte exchangeCode)
        => exchangeCode == 3 ? 10_000_000m : 100m;

    public static SecurityId ToSecurityId(this MastertrustInstrument instrument)
        => instrument.Exchange.ToSecurityId(
            instrument.Token,
            instrument.TradingSymbol);

    public static SecurityId ToSecurityId(
        this string exchange,
        string token,
        string symbol = null)
        => new()
        {
            SecurityCode = symbol.IsEmpty(token),
            BoardCode = exchange.ToBoardCode(),
            Native = exchange.ToInstrumentKey(token),
        };

    public static SecurityTypes ToSecurityType(this MastertrustInstrument instrument)
    {
        if (instrument == null)
            throw new ArgumentNullException(nameof(instrument));

        var name = instrument.InstrumentName?.Trim().ToUpperInvariant() ??
            string.Empty;
        if (instrument.OptionType.ToOptionType() != null ||
            name.StartsWith("OPT", StringComparison.Ordinal) ||
            name is "SO" or "IO")
            return SecurityTypes.Option;
        if (name.StartsWith("FUT", StringComparison.Ordinal) ||
            name is "SF" or "IF")
            return SecurityTypes.Future;
        if (name.Contains("INDEX", StringComparison.Ordinal) ||
            name is "IDX")
            return SecurityTypes.Index;
        if (instrument.Exchange.EqualsIgnoreCase("MCX") && name == "COM")
            return SecurityTypes.Commodity;
        if (name is "D" or "SG" or "GS" or "TB" or "GB")
            return SecurityTypes.Bond;
        if (name == "MF")
            return SecurityTypes.Fund;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "CE" or "C" => OptionTypes.Call,
            "PE" or "P" => OptionTypes.Put,
            _ => null,
        };

    public static string ToNative(this MastertrustProducts product)
        => product switch
        {
            MastertrustProducts.Normal => "NRML",
            MastertrustProducts.Intraday => "MIS",
            MastertrustProducts.Delivery => "CNC",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
        };

    public static MastertrustProducts ToProduct(this string product)
        => product?.Trim().ToUpperInvariant() switch
        {
            "MIS" => MastertrustProducts.Intraday,
            "CNC" => MastertrustProducts.Delivery,
            _ => MastertrustProducts.Normal,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "BUY" : "SELL";

    public static Sides ToSide(this string side)
        => side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("B")
            ? Sides.Buy
            : Sides.Sell;

    public static string ToNative(this OrderTypes orderType, decimal price)
        => orderType switch
        {
            OrderTypes.Market => "MARKET",
            OrderTypes.Limit => "LIMIT",
            OrderTypes.Conditional when price > 0 => "SL",
            OrderTypes.Conditional => "SLM",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType), orderType,
                "Mastertrust supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(this string orderType)
        => orderType?.Replace("-", string.Empty)
            .Trim()
            .ToUpperInvariant() switch
        {
            "MARKET" or "MKT" => OrderTypes.Market,
            "SL" or "SLM" or "STOPLOSS" or "STOPLOSSMARKET" =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static string ToValidity(this TimeInForce? timeInForce)
        => timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

    public static TimeInForce ToTimeInForce(this string validity)
        => validity.EqualsIgnoreCase("IOC")
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static OrderStates ToOrderState(this string status)
    {
        var value = NormalizeStatus(status);
        if (value.Contains("REJECT", StringComparison.Ordinal) ||
            value.Contains("FAIL", StringComparison.Ordinal))
            return OrderStates.Failed;
        if (value.Contains("PENDING", StringComparison.Ordinal) ||
            value.Contains("VALIDATION", StringComparison.Ordinal) ||
            value.Contains("SUBMITTED", StringComparison.Ordinal))
            return OrderStates.Pending;
        if (value.Contains("PARTIAL", StringComparison.Ordinal))
            return OrderStates.Active;
        if (value.Contains("CANCEL", StringComparison.Ordinal) ||
            value.Contains("COMPLETE", StringComparison.Ordinal) ||
            value.Contains("FILLED", StringComparison.Ordinal) ||
            value.Contains("TRADED", StringComparison.Ordinal))
            return OrderStates.Done;
        return OrderStates.Active;
    }

    public static long ToNativeQuantity(
        this string exchange,
        decimal quantity,
        decimal lotSize)
    {
        var factor = exchange.UsesLotQuantity() && lotSize > 0
            ? lotSize
            : 1;
        var native = quantity / factor;
        if (native <= 0 ||
            native != decimal.Truncate(native) ||
            native > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"Mastertrust quantity must resolve to a positive whole number for {exchange}.");
        }
        return decimal.ToInt64(native);
    }

    public static decimal FromNativeQuantity(
        this string exchange,
        decimal quantity,
        decimal lotSize)
        => quantity * (
            exchange.UsesLotQuantity() && lotSize > 0
                ? lotSize
                : 1);

    public static DateTime FromUnixSeconds(this long seconds)
    {
        if (seconds <= 0)
            return DateTime.UtcNow;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UtcNow;
        }
    }

    public static DateTime? ToMastertrustTime(this JToken value)
    {
        if (value == null || value.Type is JTokenType.Null or JTokenType.Undefined)
            return null;
        if (value.Type == JTokenType.Integer)
            return value.Value<long>() is > 0 and var seconds
                ? seconds.FromUnixSeconds()
                : null;
        return value.ToString().ToMastertrustTime();
    }

    public static DateTime? ToMastertrustTime(this string value)
    {
        if (value.IsEmpty() || value.Trim() is "0" or "-" or "--")
            return null;

        var text = value.Trim();
        if (long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds) &&
            seconds > 100_000_000)
            return seconds.FromUnixSeconds();

        if (DateTime.TryParseExact(
            text,
            [
                "dd-MMM-yyyy",
                "dd-MMM-yyyy HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
            return local.ToUtcFromIndia();

        return null;
    }

    public static DateTime ToUtcFromIndia(this DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            _indiaTimeZone);

    public static JToken GetValueIgnoreCase(this JToken token, params string[] names)
    {
        if (token is not JObject obj)
            return null;

        foreach (var name in names)
        {
            var property = obj.Properties().FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property != null)
                return property.Value;
        }
        return null;
    }

    public static string GetText(this JToken token, params string[] names)
    {
        var value = token.GetValueIgnoreCase(names);
        return value == null || value.Type is JTokenType.Null or JTokenType.Undefined
            ? null
            : value is JValue scalar
                ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)
                : value.ToString(Formatting.None);
    }

    public static decimal? GetDecimal(this JToken token, params string[] names)
    {
        var value = token.GetValueIgnoreCase(names);
        if (value == null || value.Type is JTokenType.Null or JTokenType.Undefined)
            return null;
        if (value.Type is JTokenType.Integer or JTokenType.Float)
            return value.Value<decimal>();

        return decimal.TryParse(
            value.ToString(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
    }

    public static decimal ToDecimal(this string value)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0m;

    private static string NormalizeExchange(string exchange)
        => exchange.ThrowIfEmpty(nameof(exchange)).Trim().ToUpperInvariant();

    private static bool UsesLotQuantity(this string exchange)
        => NormalizeExchange(exchange) == "MCX";

    private static string NormalizeStatus(string value)
        => value?.Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .ToUpperInvariant() ?? string.Empty;

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }
}
