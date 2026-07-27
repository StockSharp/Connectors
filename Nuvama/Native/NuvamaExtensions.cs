namespace StockSharp.Nuvama.Native;

static class NuvamaExtensions
{
    private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

    public static string ToInstrumentKey(
        this string exchange,
        string streamingSymbol)
        => $"{exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant()}|{streamingSymbol.ThrowIfEmpty(nameof(streamingSymbol))}";

    public static (string exchange, string streamingSymbol) ParseInstrumentKey(
        this string key)
    {
        var separator = key?.IndexOf('|') ?? -1;
        if (separator <= 0 || separator == key.Length - 1)
            throw new FormatException($"Invalid Nuvama instrument key '{key}'.");

        var exchange = key[..separator].ToUpperInvariant();
        exchange.ToBoardCode();
        return (exchange, key[(separator + 1)..]);
    }

    public static string ToInstrumentKey(this SecurityId securityId)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            native.ParseInstrumentKey();
            return native;
        }

        if (securityId.SecurityCode?.Contains('|') == true)
        {
            securityId.SecurityCode.ParseInstrumentKey();
            return securityId.SecurityCode;
        }

        throw new InvalidOperationException(
            "Nuvama instrument ID is missing. Select the security through Nuvama lookup so SecurityId.Native contains exchange|exchangetoken.");
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
            "NCDEX" => "NCDEX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(exchange),
                exchange,
                "Unsupported Nuvama exchange segment."),
        };

    public static SecurityId ToSecurityId(this NuvamaInstrument instrument)
        => instrument.Exchange.ToSecurityId(
            instrument.ExchangeToken,
            instrument.TradingSymbol,
            instrument.Isin);

    public static SecurityId ToSecurityId(
        this string exchange,
        string streamingSymbol,
        string tradingSymbol = null,
        string isin = null)
        => new()
        {
            SecurityCode = tradingSymbol.IsEmpty(streamingSymbol),
            BoardCode = exchange.ToBoardCode(),
            Isin = isin,
            Native = exchange.ToInstrumentKey(streamingSymbol),
        };

    public static SecurityTypes ToSecurityType(
        this NuvamaInstrument instrument)
    {
        var type = instrument.AssetType?.ToUpperInvariant();
        if (type == "INDEX")
            return SecurityTypes.Index;
        if (type?.StartsWith("OPT", StringComparison.Ordinal) == true ||
            instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
            return SecurityTypes.Option;
        if (type?.StartsWith("FUT", StringComparison.Ordinal) == true)
            return SecurityTypes.Future;
        if (instrument.Exchange.EqualsIgnoreCase("CDS"))
            return SecurityTypes.Currency;
        if (instrument.Exchange.EqualsIgnoreCase("MCX") ||
            instrument.Exchange.EqualsIgnoreCase("NCDEX"))
            return SecurityTypes.Commodity;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string optionType)
        => optionType?.ToUpperInvariant() switch
        {
            "CE" or "C" => OptionTypes.Call,
            "PE" or "P" => OptionTypes.Put,
            _ => null,
        };

    public static DateTime? ToExpiry(this string value)
    {
        if (value.IsEmpty())
            return null;

        if (!DateTime.TryParseExact(
            value.Trim(),
            ["dd/MMM/yy", "d/MMM/yy", "dd-MMM-yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var expiry))
            return null;

        return DateTime.SpecifyKind(expiry.Date, DateTimeKind.Utc);
    }

    public static string ToNative(this NuvamaProducts product)
        => product switch
        {
            NuvamaProducts.Cnc => "CNC",
            NuvamaProducts.Mis => "MIS",
            NuvamaProducts.Nrml => "NRML",
            NuvamaProducts.Mtf => "MTF",
            _ => throw new ArgumentOutOfRangeException(
                nameof(product), product, null),
        };

    public static NuvamaProducts ToProduct(this string product)
        => product?.ToUpperInvariant() switch
        {
            "MIS" or "I" => NuvamaProducts.Mis,
            "NRML" or "M" => NuvamaProducts.Nrml,
            "MTF" or "F" => NuvamaProducts.Mtf,
            _ => NuvamaProducts.Cnc,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "BUY" : "SELL";

    public static Sides ToSide(this string side)
        => side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("B")
            ? Sides.Buy
            : Sides.Sell;

    public static string ToNative(
        this OrderTypes orderType,
        decimal limitPrice)
        => orderType switch
        {
            OrderTypes.Market => "MARKET",
            OrderTypes.Limit => "LIMIT",
            OrderTypes.Conditional when limitPrice > 0 => "STOP_LIMIT",
            OrderTypes.Conditional => "STOP_MARKET",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType),
                orderType,
                "Nuvama supports market, limit, stop-limit, and stop-market orders."),
        };

    public static OrderTypes ToOrderType(this string orderType)
        => orderType?
            .Replace("-", "_")
            .ToUpperInvariant() switch
        {
            "MARKET" or "MKT" => OrderTypes.Market,
            "STOP_LIMIT" or "STOP_MARKET" or "SL" or "SL_M" or "SLM"
                => OrderTypes.Conditional,
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
        var value = status?
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .ToUpperInvariant();

        if (value is "REJECTED" or "REJECT" or "FAILED")
            return OrderStates.Failed;
        if (value is "CANCELED" or "CANCELLED" or "COMPLETE" or
            "COMPLETED" or "TRADED" or "FULLYEXECUTED")
            return OrderStates.Done;
        if (value is "PENDING" or "TRIGGERPENDING" or "OPENPENDING" or
            "VALIDATIONPENDING" or "PUTORDERREQRECEIVED" or "AMOACCEPTED")
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

    public static DateTime? ToNuvamaTime(this string value)
    {
        if (value.IsEmpty() || value.Trim() is "0" or "-" or "--")
            return null;

        var text = value.Trim();
        if (long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var epoch))
        {
            try
            {
                return DateTime.SpecifyKind(
                    epoch > 100000000000
                        ? DateTime.UnixEpoch.AddMilliseconds(epoch)
                        : DateTime.UnixEpoch.AddSeconds(epoch),
                    DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var offset) &&
            (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
             text.Contains('+', StringComparison.Ordinal)))
            return offset.UtcDateTime;

        if (!DateTime.TryParseExact(
            text,
            [
                "dd MMM yyyy, hh:mm:ss tt",
                "d MMM yyyy, hh:mm:ss tt",
                "dd/MM/yyyy HH:mm:ss",
                "dd-MM-yyyy HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "dd/MM/yyyy",
                "yyyy-MM-dd",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local) &&
            !DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out local))
            return null;

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, _indiaTimeZone);
    }

    public static string ToChartInterval(this TimeSpan timeFrame)
    {
        if (timeFrame == TimeSpan.FromMinutes(1))
            return "M1";
        if (timeFrame == TimeSpan.FromMinutes(3))
            return "M3";
        if (timeFrame == TimeSpan.FromMinutes(5))
            return "M5";
        if (timeFrame == TimeSpan.FromMinutes(15))
            return "M15";
        if (timeFrame == TimeSpan.FromMinutes(30))
            return "M30";
        if (timeFrame == TimeSpan.FromHours(1))
            return "H1";
        if (timeFrame == TimeSpan.FromDays(1))
            return "D1";
        if (timeFrame == TimeSpan.FromDays(7))
            return "W1";
        if (timeFrame == TimeSpan.FromDays(30))
            return "MN1";

        throw new ArgumentOutOfRangeException(
            nameof(timeFrame),
            timeFrame,
            "Unsupported Nuvama candle time-frame.");
    }

    public static string EffectiveOrderId(this NuvamaOrder order)
        => order?.OrderId
            .IsEmpty(order?.NestOrderId)
            .IsEmpty(order?.RequestId);

    public static string EffectiveOrderType(this NuvamaOrder order)
        => order?.OrderType.IsEmpty(order?.AlternateOrderType);

    public static string EffectiveSide(this NuvamaOrder order)
        => order?.TransactionType.IsEmpty(order?.Action);

    public static decimal EffectiveQuantity(this NuvamaOrder order)
        => order?.RequestedQuantity.ToDecimal() is > 0 and var requested
            ? requested
            : order?.Quantity.ToDecimal() is > 0 and var quantity
                ? quantity
                : order?.NetQuantity.ToDecimal() ?? 0m;

    public static decimal EffectiveFilledQuantity(this NuvamaTrade trade)
        => trade?.FilledQuantity.ToDecimal() is > 0 and var quantity
            ? quantity
            : trade?.AlternateFilledQuantity.ToDecimal() ?? 0m;

    public static decimal EffectiveFilledPrice(this NuvamaTrade trade)
        => trade?.FilledPrice.ToDecimal() is > 0 and var price
            ? price
            : trade?.NetPrice.ToDecimal() ?? 0m;

    public static string EffectiveTradeId(this NuvamaTrade trade)
        => trade?.TradeId.IsEmpty(trade?.FillId);

    public static decimal EffectiveHoldingQuantity(this NuvamaHolding holding)
    {
        var total = holding?.TotalQuantity.ToDecimal() ?? 0m;
        if (total != 0)
            return total;

        static decimal Get(NuvamaHoldingQuantity quantity)
        {
            var value = quantity?.TotalQuantity.ToDecimal() ?? 0m;
            return value != 0
                ? value
                : (quantity?.Quantity.ToDecimal() ?? 0m) +
                  (quantity?.T1Quantity.ToDecimal() ?? 0m);
        }

        return Get(holding?.Cnc) + Get(holding?.Mtf);
    }

    public static JToken FindToken(JToken token, params string[] names)
    {
        if (token is not JObject obj)
            return null;

        foreach (var name in names)
        {
            var property = obj.Properties().FirstOrDefault(
                p => p.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (property != null &&
                property.Value.Type is not JTokenType.Null and
                    not JTokenType.Undefined)
                return property.Value;
        }

        return null;
    }

    public static string FindString(JToken token, params string[] names)
        => FindToken(token, names)?.Value<string>();

    public static decimal? Positive(string value)
    {
        var parsed = value.ToDecimal();
        return parsed > 0 ? parsed : null;
    }

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "India Standard Time",
            TimeSpan.FromHours(5.5),
            "India Standard Time",
            "India Standard Time");
    }
}
