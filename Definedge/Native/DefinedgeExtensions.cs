namespace StockSharp.Definedge.Native;

static class DefinedgeExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone =
        GetIndiaTimeZone();

    public static string ToInstrumentKey(
        this string exchange, string token)
        => $"{exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant()}|{token.ThrowIfEmpty(nameof(token))}";

    public static (string exchange, string token)
        ParseInstrumentKey(this string key)
    {
        var parts = key?.Split('|');
        if (parts?.Length != 2 ||
            parts[0].IsEmpty() || parts[1].IsEmpty())
        {
            throw new FormatException(
                $"Invalid Definedge instrument key '{key}'.");
        }

        parts[0].ToBoardCode();
        return (parts[0].ToUpperInvariant(), parts[1]);
    }

    public static string ToInstrumentKey(
        this SecurityId securityId)
    {
        if (securityId.Native is string native &&
            !native.IsEmpty())
        {
            native.ParseInstrumentKey();
            return native;
        }

        if (securityId.SecurityCode?.Split('|') is
            { Length: 2 })
        {
            securityId.SecurityCode.ParseInstrumentKey();
            return securityId.SecurityCode;
        }

        throw new InvalidOperationException(
            "Definedge token is missing. Select the security through Definedge lookup so SecurityId.Native contains exchange|token.");
    }

    public static string ToBoardCode(this string exchange)
        => exchange?.ToUpperInvariant() switch
        {
            "NSE" => "NSE",
            "BSE" => "BSE",
            "NFO" => "NFO",
            "BFO" => "BFO",
            "CDS" => "CDS",
            "MCX" => "MCX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(exchange), exchange,
                "Unsupported Definedge exchange segment."),
        };

    public static SecurityId ToSecurityId(
        this DefinedgeInstrument instrument)
        => instrument.Exchange.ToSecurityId(
            instrument.Token,
            instrument.TradingSymbol.IsEmpty(instrument.Symbol));

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

    public static SecurityTypes ToSecurityType(
        this DefinedgeInstrument instrument)
    {
        var type =
            instrument.InstrumentType?.ToUpperInvariant();
        if (type?.Contains("INDEX", StringComparison.Ordinal) == true)
            return SecurityTypes.Index;
        if (type?.Contains("OPT", StringComparison.Ordinal) == true ||
            instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
            return SecurityTypes.Option;
        if (type?.Contains("FUT", StringComparison.Ordinal) == true)
            return SecurityTypes.Future;
        if (instrument.Exchange.EqualsIgnoreCase("CDS") ||
            type?.Contains("CUR", StringComparison.Ordinal) == true)
            return SecurityTypes.Currency;
        if (instrument.Exchange.EqualsIgnoreCase("MCX") ||
            type?.Contains("COM", StringComparison.Ordinal) == true)
            return SecurityTypes.Commodity;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "CE" or "C" => OptionTypes.Call,
            "PE" or "P" => OptionTypes.Put,
            _ => null,
        };

    public static string ToNative(
        this DefinedgeProducts product)
        => product switch
        {
            DefinedgeProducts.Delivery => "CNC",
            DefinedgeProducts.Intraday => "INTRADAY",
            DefinedgeProducts.Normal => "NORMAL",
            _ => throw new ArgumentOutOfRangeException(
                nameof(product), product, null),
        };

    public static DefinedgeProducts ToProduct(
        this string product)
        => product?.ToUpperInvariant() switch
        {
            "INTRADAY" or "I" => DefinedgeProducts.Intraday,
            "NORMAL" or "M" => DefinedgeProducts.Normal,
            _ => DefinedgeProducts.Delivery,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "BUY" : "SELL";

    public static Sides ToSide(this string side)
        => side.EqualsIgnoreCase("BUY") ||
            side.EqualsIgnoreCase("B")
                ? Sides.Buy
                : Sides.Sell;

    public static string ToPriceType(
        this OrderTypes orderType,
        decimal? triggerPrice)
        => orderType switch
        {
            OrderTypes.Market when triggerPrice is > 0 =>
                "SL-MARKET",
            OrderTypes.Market => "MARKET",
            OrderTypes.Limit when triggerPrice is > 0 =>
                "SL-LIMIT",
            OrderTypes.Limit => "LIMIT",
            OrderTypes.Conditional when triggerPrice is > 0 =>
                "SL-LIMIT",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType), orderType,
                "Definedge supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(
        this DefinedgeOrder order)
        => order.PriceType?.ToUpperInvariant() switch
        {
            "MARKET" or "MKT" => OrderTypes.Market,
            "SL-MARKET" or "SL-MKT" or
            "SL-LIMIT" or "SL-LMT" =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static string ToValidity(
        this TimeInForce? timeInForce)
        => timeInForce == TimeInForce.CancelBalance
            ? "IOC"
            : "DAY";

    public static TimeInForce ToTimeInForce(
        this string validity)
        => validity.EqualsIgnoreCase("IOC")
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static OrderStates ToOrderState(
        this string status,
        string reportType = null)
    {
        var value = NormalizeState(status);
        var report = NormalizeState(reportType);
        if (value is "REJECTED" or "REJECT" or "FAILED" ||
            report is "REJECTED" or "REPLACEREJECTED" or
                "CANCELREJECTED")
            return OrderStates.Failed;
        if (value is "CANCELED" or "CANCELLED" or
            "COMPLETE" or "COMPLETED" or "FILLED" ||
            report is "CANCELED" or "CANCELLED")
            return OrderStates.Done;
        if (value is "PENDING" or "TRIGGERPENDING" ||
            report?.StartsWith(
                "PENDING", StringComparison.Ordinal) == true)
            return OrderStates.Pending;
        return OrderStates.Active;
    }

    public static decimal ToDecimal(this string value)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0m;

    public static int ToInt(this string value)
        => int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0;

    public static long ToLong(this string value)
        => long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0L;

    public static string GetText(
        this JObject value, params string[] names)
    {
        foreach (var name in names)
        {
            var token = value.GetValue(
                name, StringComparison.OrdinalIgnoreCase);
            if (token != null && token.Type != JTokenType.Null)
                return token.ToString();
        }
        return null;
    }

    public static decimal? GetDecimal(
        this JObject value, params string[] names)
    {
        var text = value.GetText(names);
        return decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
    }

    public static int GetInt(
        this JObject value, params string[] names)
        => value.GetText(names).ToInt();

    public static DateTime? ToDefinedgeTime(this string value)
    {
        if (value.IsEmpty() || value.Trim() is "0" or "-")
            return null;

        var text = value.Trim();
        if (long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds) &&
            seconds > 100000000 &&
            text.Length <= 11)
        {
            try
            {
                return DateTimeOffset
                    .FromUnixTimeSeconds(seconds)
                    .UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (DateTime.TryParseExact(
            text,
            [
                "dd-MM-yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "ddMMyyyyHHmm",
                "dd-MMM-yyyy HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "HH:mm:ss dd-MM-yyyy",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return local.ToUtcFromIndia();
        }

        if (DateTime.TryParseExact(
            text,
            ["HH:mm:ss", "HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var time))
        {
            var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, _indiaTimeZone);
            return indiaNow.Date
                .Add(time.TimeOfDay)
                .ToUtcFromIndia();
        }

        return null;
    }

    public static DateTime GetMarketTime(this JObject update)
        => update.GetText("ft").ToDefinedgeTime() ??
            update.GetText("ltt").ToDefinedgeTime() ??
            DateTime.UtcNow;

    public static void Apply(
        this JObject state, JObject update)
    {
        foreach (var property in update.Properties())
        {
            if (property.Value.Type != JTokenType.Null)
                state[property.Name] = property.Value.DeepClone();
        }
    }

    public static DefinedgeDepthLevel[] GetBids(
        this JObject state)
        => CreateDepth(state, "bp", "bq", "bo");

    public static DefinedgeDepthLevel[] GetAsks(
        this JObject state)
        => CreateDepth(state, "sp", "sq", "so");

    public static DateTime ToUtcFromIndia(
        this DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                local, DateTimeKind.Unspecified),
            _indiaTimeZone);

    private static DefinedgeDepthLevel[] CreateDepth(
        JObject state,
        string pricePrefix,
        string volumePrefix,
        string ordersPrefix)
    {
        var levels = new List<DefinedgeDepthLevel>(5);
        for (var index = 1; index <= 5; index++)
        {
            var price =
                state.GetDecimal($"{pricePrefix}{index}") ?? 0;
            if (price <= 0)
                continue;
            levels.Add(new()
            {
                Price = price,
                Volume =
                    state.GetDecimal($"{volumePrefix}{index}") ?? 0,
                OrdersCount =
                    state.GetInt($"{ordersPrefix}{index}"),
            });
        }
        return [.. levels];
    }

    private static string NormalizeState(string value)
        => value?
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "Asia/Kolkata");
        }
    }
}
