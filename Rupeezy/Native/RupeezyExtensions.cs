namespace StockSharp.Rupeezy.Native;

static class RupeezyExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

    public static IReadOnlyDictionary<TimeSpan, string> TimeFrames { get; } =
        new Dictionary<TimeSpan, string>
        {
            [TimeSpan.FromMinutes(1)] = "1",
            [TimeSpan.FromMinutes(2)] = "2",
            [TimeSpan.FromMinutes(3)] = "3",
            [TimeSpan.FromMinutes(4)] = "4",
            [TimeSpan.FromMinutes(5)] = "5",
            [TimeSpan.FromMinutes(10)] = "10",
            [TimeSpan.FromMinutes(15)] = "15",
            [TimeSpan.FromMinutes(30)] = "30",
            [TimeSpan.FromMinutes(45)] = "45",
            [TimeSpan.FromHours(1)] = "60",
            [TimeSpan.FromHours(2)] = "120",
            [TimeSpan.FromHours(3)] = "180",
            [TimeSpan.FromHours(4)] = "240",
            [TimeSpan.FromDays(1)] = "1D",
            [TimeSpan.FromDays(7)] = "1W",
            [TimeSpan.FromDays(30)] = "1M",
        };

    public static string ToInstrumentKey(this string exchange, string token)
        => $"{NormalizeExchange(exchange)}:{token.ThrowIfEmpty(nameof(token))}";

    public static (string exchange, string token) ParseInstrumentKey(this string key)
    {
        var separator = key?.IndexOf(':') ?? -1;
        if (separator <= 0 || separator == key.Length - 1)
            throw new FormatException($"Invalid Rupeezy instrument key '{key}'.");

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
            "Rupeezy token is missing. Select the security through Rupeezy lookup so SecurityId.Native contains exchange:token.");
    }

    public static string ToBoardCode(this string exchange)
        => NormalizeExchange(exchange) switch
        {
            "NSE_EQ" => "NSE",
            "BSE_EQ" => "BSE",
            "NSE_FO" => "NFO",
            "BSE_FO" => "BFO",
            "NSE_CUR" or "NSE_CD" => "CDS",
            "MCX_FO" => "MCX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(exchange), exchange, "Unsupported Rupeezy exchange."),
        };

    public static long ToNativeQuantity(
        this string exchange,
        decimal quantity,
        decimal lotSize)
    {
        var factor = exchange.GetQuantityFactor(lotSize);
        var native = quantity / factor;
        if (native <= 0 ||
            native != decimal.Truncate(native) ||
            native > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"Rupeezy quantity must resolve to a positive whole number of lots for {exchange}.");
        }
        return decimal.ToInt64(native);
    }

    public static decimal FromNativeQuantity(
        this string exchange,
        decimal quantity,
        decimal lotSize)
        => quantity * exchange.GetQuantityFactor(lotSize);

    public static decimal FromPositionQuantity(
        this string exchange,
        decimal quantity,
        decimal lotSize,
        decimal multiplier)
    {
        if (!exchange.UsesLotQuantity())
            return quantity;
        return quantity *
            (lotSize > 0 ? lotSize : 1) *
            (multiplier > 0 ? multiplier : 1);
    }

    public static SecurityId ToSecurityId(this RupeezyInstrument instrument)
        => instrument.Exchange.ToSecurityId(
            instrument.Token,
            instrument.SecurityDescription.IsEmpty(instrument.Symbol));

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

    public static SecurityTypes ToSecurityType(this RupeezyInstrument instrument)
    {
        if (instrument == null)
            throw new ArgumentNullException(nameof(instrument));

        var name = instrument.InstrumentName?.ToUpperInvariant() ?? string.Empty;
        if (name == "EQIDX")
            return SecurityTypes.Index;
        if (name.StartsWith("OPT", StringComparison.Ordinal) ||
            instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
            return SecurityTypes.Option;
        if (name.StartsWith("FUT", StringComparison.Ordinal))
            return SecurityTypes.Future;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "CE" or "C" => OptionTypes.Call,
            "PE" or "P" => OptionTypes.Put,
            _ => null,
        };

    public static string ToNative(this RupeezyProducts product)
        => product switch
        {
            RupeezyProducts.Intraday => "INTRADAY",
            RupeezyProducts.Delivery => "DELIVERY",
            RupeezyProducts.Btst => "BTST",
            RupeezyProducts.Mtf => "MTF",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
        };

    public static RupeezyProducts ToProduct(this string product)
        => product?.ToUpperInvariant() switch
        {
            "INTRADAY" => RupeezyProducts.Intraday,
            "BTST" => RupeezyProducts.Btst,
            "MTF" => RupeezyProducts.Mtf,
            _ => RupeezyProducts.Delivery,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "BUY" : "SELL";

    public static Sides ToSide(this string side)
        => side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("B")
            ? Sides.Buy
            : Sides.Sell;

    public static string ToVariety(this OrderTypes orderType, decimal price)
        => orderType switch
        {
            OrderTypes.Market => "RL-MKT",
            OrderTypes.Limit => "RL",
            OrderTypes.Conditional when price > 0 => "SL",
            OrderTypes.Conditional => "SL-MKT",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType), orderType,
                "Rupeezy supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(this string variety)
        => variety?.ToUpperInvariant() switch
        {
            "RL-MKT" => OrderTypes.Market,
            "SL" or "SL-MKT" => OrderTypes.Conditional,
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
        if (value.Contains("CANCEL", StringComparison.Ordinal) ||
            value.Contains("EXECUT", StringComparison.Ordinal) ||
            value.Contains("COMPLETE", StringComparison.Ordinal) ||
            value.Contains("FILLED", StringComparison.Ordinal))
            return OrderStates.Done;
        if (value.Contains("PENDING", StringComparison.Ordinal) ||
            value.Contains("RECEIVED", StringComparison.Ordinal) ||
            value.Contains("OMSXMITTED", StringComparison.Ordinal) ||
            value.Contains("SUBMITTED", StringComparison.Ordinal))
            return OrderStates.Pending;
        return OrderStates.Active;
    }

    public static DateTime FromUnixSeconds(this long seconds)
    {
        if (seconds <= 0)
            return DateTime.UtcNow;

        try
        {
            return DateTime.SpecifyKind(
                DateTime.UnixEpoch.AddSeconds(seconds),
                DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UtcNow;
        }
    }

    public static DateTime? ToRupeezyTime(this string value)
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
                "yyyy-MM-dd HH:mm:ss",
                "dd-MMM-yyyy HH:mm:ss",
                "dd-MMM-yyyy HH.mm.ss",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyyMMdd",
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

    private static decimal GetQuantityFactor(
        this string exchange,
        decimal lotSize)
        => exchange.UsesLotQuantity() && lotSize > 0
            ? lotSize
            : 1;

    private static bool UsesLotQuantity(this string exchange)
        => NormalizeExchange(exchange) is
            "BSE_FO" or
            "NSE_CUR" or
            "NSE_CD" or
            "MCX_FO";

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
