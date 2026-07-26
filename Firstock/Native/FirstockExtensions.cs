namespace StockSharp.Firstock.Native;

static class FirstockExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

    public static string ToInstrumentKey(this string exchange, string token)
        => $"{exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant()}:{token.ThrowIfEmpty(nameof(token))}";

    public static (string exchange, string token) ParseInstrumentKey(this string key)
    {
        var parts = key?.Split(':');
        if (parts?.Length != 2 || parts[0].IsEmpty() || parts[1].IsEmpty())
            throw new FormatException($"Invalid Firstock instrument key '{key}'.");
        parts[0].ToBoardCode();
        return (parts[0].ToUpperInvariant(), parts[1]);
    }

    public static string ToInstrumentKey(this SecurityId securityId)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            native.ParseInstrumentKey();
            return native;
        }

        if (securityId.SecurityCode?.Split(':') is { Length: 2 })
        {
            securityId.SecurityCode.ParseInstrumentKey();
            return securityId.SecurityCode;
        }

        throw new InvalidOperationException(
            "Firstock token is missing. Select the security through Firstock lookup so SecurityId.Native contains exchange:token.");
    }

    public static string ToBoardCode(this string exchange)
        => exchange?.ToUpperInvariant() switch
        {
            "NSE" or "EQT" => "NSE",
            "BSE" => "BSE",
            "NFO" => "NFO",
            "BFO" => "BFO",
            _ => throw new ArgumentOutOfRangeException(nameof(exchange), exchange,
                "Unsupported Firstock exchange segment."),
        };

    public static SecurityId ToSecurityId(this FirstockInstrument instrument)
        => instrument.Exchange.ToSecurityId(instrument.Token,
            instrument.TradingSymbol.IsEmpty(instrument.Symbol));

    public static SecurityId ToSecurityId(this string exchange, string token, string symbol = null)
        => new()
        {
            SecurityCode = symbol.IsEmpty(token),
            BoardCode = exchange.ToBoardCode(),
            Native = exchange.ToBoardCode().ToInstrumentKey(token),
        };

    public static SecurityTypes ToSecurityType(this FirstockInstrument instrument)
    {
        var type = instrument.Instrument?.ToUpperInvariant();
        if (type?.Contains("INDEX", StringComparison.Ordinal) == true ||
            instrument.TradingSymbol.EqualsIgnoreCase("NIFTY") ||
            instrument.TradingSymbol.EqualsIgnoreCase("SENSEX"))
            return SecurityTypes.Index;
        if (type?.Contains("OPT", StringComparison.Ordinal) == true ||
            instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
            return SecurityTypes.Option;
        if (type?.Contains("FUT", StringComparison.Ordinal) == true)
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

    public static string ToNative(this FirstockProducts product)
        => product switch
        {
            FirstockProducts.Delivery => "C",
            FirstockProducts.Margin => "M",
            FirstockProducts.Intraday => "I",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
        };

    public static FirstockProducts ToProduct(this string product)
        => product?.ToUpperInvariant() switch
        {
            "M" => FirstockProducts.Margin,
            "I" => FirstockProducts.Intraday,
            _ => FirstockProducts.Delivery,
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
            OrderTypes.Limit => "LMT",
            OrderTypes.Conditional when price > 0 => "SL-LMT",
            OrderTypes.Conditional => "SL-MKT",
            _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
                "Firstock supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(this FirstockOrder order)
        => order.PriceType?.ToUpperInvariant() switch
        {
            "MKT" => OrderTypes.Market,
            "SL-LMT" or "SL-MKT" or "SL-M" => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static string ToRetention(this TimeInForce? timeInForce)
        => timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

    public static TimeInForce ToTimeInForce(this string retention)
        => retention.EqualsIgnoreCase("IOC")
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static OrderStates ToOrderState(this string status, string reportType = null)
    {
        var value = Normalize(status);
        var report = Normalize(reportType);
        if (value is "REJECTED" or "REJECT" or "FAILED" ||
            report is "REJECTED" or "REPLACEREJECTED" or "CANCELREJECTED")
            return OrderStates.Failed;
        if (value is "CANCELED" or "CANCELLED" or "COMPLETE" or "COMPLETED" or "FILLED" ||
            report is "CANCELED" or "CANCELLED" or "FILL")
            return OrderStates.Done;
        if (value?.Contains("PENDING", StringComparison.Ordinal) == true ||
            report?.StartsWith("PENDING", StringComparison.Ordinal) == true)
            return OrderStates.Pending;
        return OrderStates.Active;
    }

    public static decimal ToDecimal(this string value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;

    public static int ToInt(this string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    public static DateTime FromUnixSeconds(this long seconds)
    {
        if (seconds <= 0)
            return DateTime.UtcNow;
        try
        {
            return DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(seconds), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UtcNow;
        }
    }

    public static DateTime? ToFirstockTime(this string value)
    {
        if (value.IsEmpty() || value.Trim() is "0" or "-")
            return null;

        var text = value.Trim();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 100000000)
            return seconds.FromUnixSeconds();

        if (DateTime.TryParseExact(text,
            [
                "dd-MM-yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "dd-MMM-yyyy HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "HH:mm:ss dd-MM-yyyy",
                "HH:mm:ss dd/MM/yyyy",
                "HH:mm:ss dd-MMM-yyyy",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
            return local.ToUtcFromIndia();

        return null;
    }

    public static DateTime GetCandleTime(this FirstockCandle candle)
        => candle.EpochTime > 0
            ? candle.EpochTime.FromUnixSeconds()
            : candle.Time.ToFirstockTime() ?? DateTime.UtcNow;

    public static decimal? ToPrice(this decimal? raw, decimal divisor)
    {
        if (raw is null)
            return null;
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "Price divisor must be positive.");
        return raw.Value / divisor;
    }

    public static DateTime ToUtcFromIndia(this DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            _indiaTimeZone);

    public static DateTime ToIndiaLocal(this DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _indiaTimeZone);
    }

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
        return decimal.TryParse(value.ToString(), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    public static long? GetLong(this JToken token, params string[] names)
    {
        var value = token.GetValueIgnoreCase(names);
        if (value == null || value.Type is JTokenType.Null or JTokenType.Undefined)
            return null;
        if (value.Type == JTokenType.Integer)
            return value.Value<long>();
        return long.TryParse(value.ToString(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string Normalize(string value)
        => value?.Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

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
