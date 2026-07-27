namespace StockSharp.Bigul.Native;

static class BigulExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

    public static string ToInstrumentKey(this string segment, string token)
        => $"{NormalizeSegment(segment)}:{token.ThrowIfEmpty(nameof(token))}";

    public static (string segment, string token) ParseInstrumentKey(this string key)
    {
        var separator = key?.IndexOf(':') ?? -1;
        if (separator <= 0 || separator == key.Length - 1)
            throw new FormatException($"Invalid Bigul instrument key '{key}'.");
        var segment = NormalizeSegment(key[..separator]);
        segment.ToBoardCode();
        return (segment, key[(separator + 1)..]);
    }

    public static string ToInstrumentKey(this SecurityId securityId)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            var (segment, token) = native.ParseInstrumentKey();
            return segment.ToInstrumentKey(token);
        }

        if (securityId.SecurityCode?.Split(':') is { Length: 2 } parts)
            return parts[0].ToInstrumentKey(parts[1]);

        throw new InvalidOperationException(
            "Bigul token is missing. Select the security through Bigul lookup so SecurityId.Native contains segment:token.");
    }

    public static string ToBoardCode(this string segment)
        => NormalizeSegment(segment) switch
        {
            "nse_cm" or "nse" => "NSE",
            "bse_cm" or "bse" => "BSE",
            "nse_fo" or "nfo" => "NFO",
            "bse_fo" or "bfo" => "BFO",
            "cde_fo" or "nse_cd" or "cds" => "CDS",
            "mcx_fo" or "mcx" => "MCX",
            "nse_com" or "ncdex_fo" or "ncdex" => "NCDEX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(segment), segment, "Unsupported Bigul exchange segment."),
        };

    public static SecurityId ToSecurityId(this BigulInstrument instrument)
        => instrument.Segment.ToSecurityId(
            instrument.Token,
            instrument.TradingSymbol.IsEmpty(instrument.Symbol));

    public static SecurityId ToSecurityId(
        this string segment,
        string token,
        string tradingSymbol = null)
        => new()
        {
            SecurityCode = tradingSymbol.IsEmpty(token),
            BoardCode = segment.ToBoardCode(),
            Native = segment.ToInstrumentKey(token),
        };

    public static SecurityTypes ToSecurityType(this BigulInstrument instrument)
    {
        if (instrument == null)
            throw new ArgumentNullException(nameof(instrument));
        var series = instrument.Series?.ToUpperInvariant();
        if (series?.Contains("INDEX", StringComparison.Ordinal) == true)
            return SecurityTypes.Index;
        if (instrument.IsOption ||
            instrument.OptionType?.ToUpperInvariant() is "CE" or "PE" ||
            series?.Contains("OPT", StringComparison.Ordinal) == true)
            return SecurityTypes.Option;
        if (instrument.IsFuture ||
            series?.Contains("FUT", StringComparison.Ordinal) == true)
            return SecurityTypes.Future;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "CE" or "C" => OptionTypes.Call,
            "PE" or "P" => OptionTypes.Put,
            _ => null,
        };

    public static string ToNative(this BigulProducts product)
        => product switch
        {
            BigulProducts.Delivery => "CNC",
            BigulProducts.Intraday => "MIS",
            BigulProducts.Normal => "NRML",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
        };

    public static BigulProducts ToProduct(this string product)
        => product?.ToUpperInvariant() switch
        {
            "MIS" => BigulProducts.Intraday,
            "NRML" => BigulProducts.Normal,
            _ => BigulProducts.Delivery,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "B" : "S";

    public static Sides ToSide(this string side)
        => side.EqualsIgnoreCase("B") || side.EqualsIgnoreCase("BUY")
            ? Sides.Buy
            : Sides.Sell;

    public static string ToPriceType(this OrderTypes orderType, decimal price)
        => orderType switch
        {
            OrderTypes.Market => "MKT",
            OrderTypes.Limit => "L",
            OrderTypes.Conditional when price > 0 => "SL",
            OrderTypes.Conditional => "SL-M",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType), orderType,
                "Bigul supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(this BigulOrder order)
        => order.PriceType?.ToUpperInvariant() switch
        {
            "M" or "MKT" => OrderTypes.Market,
            "SL" or "SL-M" or "SL-MKT" => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static string ToRetention(this TimeInForce? timeInForce)
        => timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

    public static TimeInForce ToTimeInForce(this string retention)
        => retention.EqualsIgnoreCase("IOC")
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static OrderStates ToOrderState(this string status)
    {
        var value = NormalizeStatus(status);
        if (value.Contains("REJECT", StringComparison.Ordinal) ||
            value.Contains("FAIL", StringComparison.Ordinal))
            return OrderStates.Failed;
        if (value.Contains("CANCEL", StringComparison.Ordinal) ||
            value.Contains("COMPLETE", StringComparison.Ordinal) ||
            value.Contains("FILLED", StringComparison.Ordinal) ||
            value.Contains("EXECUTED", StringComparison.Ordinal))
            return OrderStates.Done;
        if (value.Contains("PENDING", StringComparison.Ordinal) ||
            value.Contains("RECEIVED", StringComparison.Ordinal) ||
            value.Contains("PUTORDERREQ", StringComparison.Ordinal))
            return OrderStates.Pending;
        return OrderStates.Active;
    }

    public static bool IsCancelled(this BigulOrder order)
        => NormalizeStatus(order?.Status).Contains("CANCEL", StringComparison.Ordinal);

    public static bool IsAfterMarket(this BigulOrder order)
        => order?.OrderSource?.Contains("AMO", StringComparison.OrdinalIgnoreCase) == true;

    public static decimal ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;

    public static long ToLong(this string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0L;

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

    public static DateTime FromUnixMilliseconds(this long milliseconds)
    {
        if (milliseconds <= 0)
            return DateTime.UtcNow;
        try
        {
            return DateTime.SpecifyKind(
                DateTime.UnixEpoch.AddMilliseconds(milliseconds),
                DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UtcNow;
        }
    }

    public static DateTime? ToBigulTime(this string value)
    {
        if (value.IsEmpty() || value.Trim() is "0" or "-" or "--" or "NA")
            return null;

        var text = value.Trim();
        if (long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var numeric))
        {
            if (numeric > 10_000_000_000)
                return numeric.FromUnixMilliseconds();
            if (numeric > 100_000_000)
                return numeric.FromUnixSeconds();
        }

        if (DateTime.TryParseExact(
            text,
            [
                "dd-MMM-yyyy HH:mm:ss",
                "dd-MM-yyyy HH:mm:ss",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "dd MMM, yyyy",
                "dd-MMM-yyyy",
                "dd-MM-yyyy",
                "yyyy-MM-dd",
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

    private static string NormalizeSegment(string segment)
        => segment.ThrowIfEmpty(nameof(segment)).Trim().ToLowerInvariant();

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
