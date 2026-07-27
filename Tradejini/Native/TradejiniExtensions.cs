namespace StockSharp.Tradejini.Native;

static class TradejiniExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone =
        GetIndiaTimeZone();

    public static IReadOnlyDictionary<TimeSpan, string> TimeFrames { get; } =
        new Dictionary<TimeSpan, string>
        {
            [TimeSpan.FromMinutes(1)] = "1",
        };

    public static SecurityId ToSecurityId(
        this TradejiniInstrument instrument)
    {
        if (instrument == null)
            throw new ArgumentNullException(nameof(instrument));
        return instrument.Id.ToTradejiniSecurityId(
            instrument.Exchange,
            instrument.DisplayName);
    }

    public static SecurityId ToTradejiniSecurityId(
        this string symbolId,
        string exchange = null,
        string displayName = null)
    {
        symbolId.ThrowIfEmpty(nameof(symbolId));
        exchange = exchange.IsEmpty(symbolId.GetExchange());
        return new()
        {
            SecurityCode = symbolId,
            BoardCode = exchange.ToBoardCode(),
            Native = symbolId,
        };
    }

    public static string ToSymbolId(this SecurityId securityId)
    {
        if (securityId.Native is string native && !native.IsEmpty())
            return native;

        var code = securityId.SecurityCode;
        if (!code.IsEmpty() && code.Contains('_', StringComparison.Ordinal))
        {
            var exchange = code.GetExchange();
            if (securityId.BoardCode.IsEmpty() ||
                securityId.BoardCode.EqualsIgnoreCase(exchange.ToBoardCode()))
                return code;
        }

        throw new InvalidOperationException(
            "Tradejini symbol id is missing. Select the security through Tradejini lookup so SecurityId.Native contains symId.");
    }

    public static string GetExchange(this string symbolId)
    {
        var parts = symbolId.ThrowIfEmpty(nameof(symbolId)).Split('_');
        if (parts.Length < 3)
        {
            throw new FormatException(
                $"Invalid Tradejini symbol id '{symbolId}'.");
        }

        var prefix = parts[0].ToUpperInvariant();
        var exchange = prefix.StartsWith("OPT", StringComparison.Ordinal)
            ? parts.Length >= 6 ? parts[^4] : null
            : prefix.StartsWith("FUT", StringComparison.Ordinal)
                ? parts.Length >= 4 ? parts[^2] : null
                : parts[^1];
        return exchange.ThrowIfEmpty(
            $"Tradejini exchange in symbol id '{symbolId}'");
    }

    public static string ToBoardCode(this string exchange)
        => exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant() switch
        {
            "NSE" => "NSE",
            "BSE" => "BSE",
            "NFO" => "NFO",
            "BFO" => "BFO",
            "CDS" => "CDS",
            "MCX" => "MCX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(exchange),
                exchange,
                "Unsupported Tradejini exchange."),
        };

    public static SecurityTypes ToSecurityType(
        this TradejiniInstrument instrument)
    {
        if (instrument == null)
            throw new ArgumentNullException(nameof(instrument));

        var asset = instrument.Asset?.ToUpperInvariant() ?? string.Empty;
        var kind = instrument.Instrument?.ToUpperInvariant() ?? string.Empty;
        if (asset == "OPTION" || kind.StartsWith("OPT", StringComparison.Ordinal))
            return SecurityTypes.Option;
        if (asset == "FUTURE" || kind.StartsWith("FUT", StringComparison.Ordinal))
            return SecurityTypes.Future;
        if (asset == "INDEX" || kind == "IDX")
            return SecurityTypes.Index;
        if (asset == "SPOT" || kind.StartsWith("UND", StringComparison.Ordinal))
        {
            return kind.Contains("CUR", StringComparison.Ordinal)
                ? SecurityTypes.Currency
                : SecurityTypes.Commodity;
        }
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "CE" or "C" or "CALL" => OptionTypes.Call,
            "PE" or "P" or "PUT" => OptionTypes.Put,
            _ => null,
        };

    public static string ToNative(this TradejiniTwoFactorTypes type)
        => type switch
        {
            TradejiniTwoFactorTypes.Otp => "otp",
            TradejiniTwoFactorTypes.Totp => "totp",
            _ => throw new ArgumentOutOfRangeException(
                nameof(type), type, null),
        };

    public static string ToNative(this TradejiniProducts product)
        => product switch
        {
            TradejiniProducts.Delivery => "delivery",
            TradejiniProducts.Intraday => "intraday",
            TradejiniProducts.Normal => "normal",
            _ => throw new ArgumentOutOfRangeException(
                nameof(product), product, null),
        };

    public static TradejiniProducts ToProduct(this string product)
        => product?.Trim().ToLowerInvariant() switch
        {
            "intraday" or "mis" => TradejiniProducts.Intraday,
            "normal" or "nrml" => TradejiniProducts.Normal,
            _ => TradejiniProducts.Delivery,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "buy" : "sell";

    public static Sides ToSide(this string side)
        => side.EqualsIgnoreCase("buy") ||
            side.EqualsIgnoreCase("b")
                ? Sides.Buy
                : Sides.Sell;

    public static string ToNative(
        this OrderTypes orderType,
        decimal limitPrice)
        => orderType switch
        {
            OrderTypes.Limit => "limit",
            OrderTypes.Market => "market",
            OrderTypes.Conditional when limitPrice > 0 => "stoplimit",
            OrderTypes.Conditional => "stopmarket",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType),
                orderType,
                "Tradejini supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(this string type)
        => Normalize(type) switch
        {
            "MARKET" or "MKT" or "M" => OrderTypes.Market,
            "STOPLIMIT" or "STOPMARKET" or "SL" or "SLM" or
                "SLLMT" or "SLMKT" => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static string ToNative(this TradejiniValidities validity)
        => validity switch
        {
            TradejiniValidities.Day => "day",
            TradejiniValidities.Ioc => "ioc",
            TradejiniValidities.Eos => "eos",
            TradejiniValidities.Gtc => "gtc",
            _ => throw new ArgumentOutOfRangeException(
                nameof(validity), validity, null),
        };

    public static TradejiniValidities ToValidity(
        this string validity)
        => validity?.Trim().ToLowerInvariant() switch
        {
            "ioc" => TradejiniValidities.Ioc,
            "eos" => TradejiniValidities.Eos,
            "gtc" => TradejiniValidities.Gtc,
            _ => TradejiniValidities.Day,
        };

    public static TradejiniValidities ToValidity(
        this TimeInForce? timeInForce,
        TradejiniValidities? explicitValidity)
    {
        if (timeInForce == TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Tradejini does not expose fill-or-kill validity.");
        }
        if (explicitValidity != null)
            return explicitValidity.Value;
        return timeInForce == TimeInForce.CancelBalance
            ? TradejiniValidities.Ioc
            : TradejiniValidities.Day;
    }

    public static TimeInForce ToTimeInForce(
        this TradejiniValidities validity)
        => validity == TradejiniValidities.Ioc
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static OrderStates ToOrderState(this string status)
    {
        var value = Normalize(status);
        if (value.Contains("REJECT", StringComparison.Ordinal) ||
            value.Contains("FAIL", StringComparison.Ordinal))
            return OrderStates.Failed;
        if (value.Contains("PENDING", StringComparison.Ordinal) ||
            value.Contains("RECEIVED", StringComparison.Ordinal) ||
            value.Contains("SUBMITTED", StringComparison.Ordinal))
            return OrderStates.Pending;
        if (value.Contains("PARTIAL", StringComparison.Ordinal))
            return OrderStates.Active;
        if (value.Contains("CANCEL", StringComparison.Ordinal) ||
            value.Contains("COMPLETE", StringComparison.Ordinal) ||
            value.Contains("FILLED", StringComparison.Ordinal) ||
            value.Contains("CLOSED", StringComparison.Ordinal))
            return OrderStates.Done;
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

    public static DateTime? ToTradejiniTime(
        this string value,
        DateTime referenceUtc)
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
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.FFF",
                "yyyyMMdd HH:mm:ss",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
            return local.ToUtcFromIndia();

        if (TimeSpan.TryParseExact(
            text,
            ["hh\\:mm\\:ss", "h\\:mm\\:ss"],
            CultureInfo.InvariantCulture,
            out var time))
        {
            var reference = TimeZoneInfo.ConvertTimeFromUtc(
                NormalizeUtc(referenceUtc),
                _indiaTimeZone);
            return reference.Date.Add(time).ToUtcFromIndia();
        }

        return null;
    }

    public static DateTime ToUtcFromIndia(this DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            _indiaTimeZone);

    public static JToken GetValueIgnoreCase(
        this JToken token,
        params string[] names)
    {
        if (token is not JObject obj)
            return null;
        foreach (var name in names)
        {
            var property = obj.Properties().FirstOrDefault(
                p => p.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (property != null)
                return property.Value;
        }
        return null;
    }

    public static string GetText(
        this JToken token,
        params string[] names)
    {
        var value = token.GetValueIgnoreCase(names);
        return value == null ||
            value.Type is JTokenType.Null or JTokenType.Undefined
                ? null
                : value is JValue scalar
                    ? Convert.ToString(
                        scalar.Value,
                        CultureInfo.InvariantCulture)
                    : value.ToString(Formatting.None);
    }

    public static decimal ToDecimal(this string value)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0m;

    public static bool ToBoolean(this string value)
        => value.EqualsIgnoreCase("true") ||
            value.EqualsIgnoreCase("yes") ||
            value.EqualsIgnoreCase("y") ||
            value.EqualsIgnoreCase("1");

    public static DateTime? ToExpiryDate(this string value)
    {
        if (!DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd", "dd-MM-yyyy", "dd-MMM-yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
            return null;
        return local.ToUtcFromIndia();
    }

    private static string Normalize(string value)
        => value?.Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant() ?? string.Empty;

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }
}
