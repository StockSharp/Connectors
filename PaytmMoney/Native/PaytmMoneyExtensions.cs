namespace StockSharp.PaytmMoney.Native;

static class PaytmMoneyExtensions
{
    private static readonly TimeSpan _indiaOffset =
        TimeSpan.FromMinutes(330);

    public static string ToNative(this PaytmMoneyProducts product)
        => product switch
        {
            PaytmMoneyProducts.Intraday => "T",
            PaytmMoneyProducts.Delivery => "C",
            PaytmMoneyProducts.Margin => "M",
            PaytmMoneyProducts.Cover => "V",
            PaytmMoneyProducts.Bracket => "B",
            _ => throw new ArgumentOutOfRangeException(
                nameof(product), product, null),
        };

    public static PaytmMoneyProducts ToProduct(this string product)
        => product?.ToUpperInvariant() switch
        {
            "C" or "CNC" or "DELIVERY" =>
                PaytmMoneyProducts.Delivery,
            "M" or "MARGIN" or "MTF" =>
                PaytmMoneyProducts.Margin,
            "V" or "COVER" or "CO" =>
                PaytmMoneyProducts.Cover,
            "B" or "BRACKET" or "BO" =>
                PaytmMoneyProducts.Bracket,
            _ => PaytmMoneyProducts.Intraday,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "B" : "S";

    public static Sides ToSide(this string side)
        => side?.ToUpperInvariant() is "B" or "BUY"
            ? Sides.Buy
            : Sides.Sell;

    public static string ToNative(
        this TimeInForce? timeInForce)
        => timeInForce == TimeInForce.CancelBalance
            ? "IOC"
            : "DAY";

    public static TimeInForce ToTimeInForce(
        this string validity)
        => validity.EqualsIgnoreCase("IOC")
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static string ToNative(
        this OrderTypes orderType, decimal? triggerPrice)
        => orderType switch
        {
            OrderTypes.Market when triggerPrice is > 0 => "SLM",
            OrderTypes.Market => "MKT",
            OrderTypes.Limit when triggerPrice is > 0 => "SL",
            OrderTypes.Limit => "LMT",
            OrderTypes.Conditional when triggerPrice is > 0 => "SL",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType), orderType,
                "Unsupported Paytm Money order type."),
        };

    public static OrderTypes ToOrderType(this string orderType)
        => orderType?.ToUpperInvariant() switch
        {
            "MKT" or "MARKET" => OrderTypes.Market,
            "SL" or "SLM" or "STOPLOSS" or
                "STOP_LOSS" => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static OrderStates ToOrderState(this string status)
    {
        status = status?
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

        if (status is "REJECTED" or "FAILED" or "ERROR")
            return OrderStates.Failed;

        if (status is "COMPLETE" or "COMPLETED" or
            "TRADED" or "FULLYTRADED" or "FILLED" or
            "FULLYFILLED" or "CANCELLED" or
            "CANCELED" or "EXPIRED")
        {
            return OrderStates.Done;
        }

        return OrderStates.Active;
    }

    public static string ToBoardCode(
        this string exchange, string segment, string scripType)
    {
        exchange = exchange?.ToUpperInvariant();
        segment = segment?.ToUpperInvariant();
        scripType = scripType?.ToUpperInvariant();

        if (scripType == "INDEX" || segment == "I")
            return exchange == "BSE" ? "BSE_IDX" : "NSE_IDX";

        return (exchange, segment) switch
        {
            ("NSE", "E") => "NSE_EQ",
            ("NSE", "D") => "NSE_FNO",
            ("BSE", "E") => "BSE_EQ",
            ("BSE", "D") => "BSE_FNO",
            _ => throw new ArgumentOutOfRangeException(
                nameof(segment), $"{exchange}:{segment}",
                "Unsupported Paytm Money exchange segment."),
        };
    }

    public static (string exchange, string segment)
        ToExchangeSegment(this string boardCode)
        => boardCode?.ToUpperInvariant() switch
        {
            "NSE_EQ" => ("NSE", "E"),
            "NSE_FNO" => ("NSE", "D"),
            "NSE_IDX" or "IDX_I" => ("NSE", "I"),
            "BSE_EQ" => ("BSE", "E"),
            "BSE_FNO" => ("BSE", "D"),
            "BSE_IDX" => ("BSE", "I"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(boardCode), boardCode,
                "Unsupported Paytm Money board."),
        };

    public static string ToInstrumentKey(
        this PaytmMoneyInstrument instrument)
        => CreateInstrumentKey(
            instrument.Exchange,
            instrument.Segment,
            instrument.SecurityId,
            instrument.ScripType,
            instrument.HistoryType);

    public static string CreateInstrumentKey(
        string exchange, string segment, string securityId,
        string scripType, string historyType = null)
        => string.Join("|",
            exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant(),
            segment.ThrowIfEmpty(nameof(segment)).ToUpperInvariant(),
            securityId.ThrowIfEmpty(nameof(securityId)),
            scripType.ThrowIfEmpty(nameof(scripType)).ToUpperInvariant(),
            historyType?.ToUpperInvariant() ?? string.Empty);

    public static (
        string exchange,
        string segment,
        string securityId,
        string scripType,
        string historyType)
        ParseInstrumentKey(this string key)
    {
        var parts = key?.Split('|');
        if (parts?.Length is not (4 or 5) ||
            parts[0].IsEmpty() ||
            parts[1].IsEmpty() ||
            parts[2].IsEmpty() ||
            parts[3].IsEmpty())
        {
            throw new FormatException(
                $"Invalid Paytm Money instrument key '{key}'.");
        }

        parts[0].ToBoardCode(parts[1], parts[3]);
        return (
            parts[0].ToUpperInvariant(),
            parts[1].ToUpperInvariant(),
            parts[2],
            parts[3].ToUpperInvariant(),
            parts.Length == 5 ? parts[4].ToUpperInvariant() : null);
    }

    public static string ToInstrumentKey(
        this SecurityId securityId)
    {
        if (securityId.Native is string native &&
            !native.IsEmpty())
        {
            return native;
        }

        if (securityId.SecurityCode?.Contains('|') == true)
            return securityId.SecurityCode;

        if (long.TryParse(
            securityId.SecurityCode,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out _))
        {
            var (exchange, segment) =
                securityId.BoardCode.ToExchangeSegment();
            var scripType = segment switch
            {
                "I" => "INDEX",
                "E" => "EQUITY",
                _ => throw new InvalidOperationException(
                    "Select derivatives through Paytm Money security lookup so their native FUTURE or OPTION type is available."),
            };
            return CreateInstrumentKey(
                exchange, segment,
                securityId.SecurityCode, scripType);
        }

        throw new InvalidOperationException(
            "Paytm Money security identifier is missing. Select the security through Paytm Money lookup.");
    }

    public static SecurityId ToSecurityId(
        this PaytmMoneyInstrument instrument)
        => new()
        {
            SecurityCode = instrument.Symbol.IsEmpty()
                ? instrument.SecurityId
                : instrument.Symbol,
            BoardCode = instrument.Exchange.ToBoardCode(
                instrument.Segment, instrument.ScripType),
            Native = instrument.ToInstrumentKey(),
        };

    public static SecurityTypes ToSecurityType(
        this PaytmMoneyInstrument instrument)
        => instrument.ScripType?.ToUpperInvariant() switch
        {
            "INDEX" => SecurityTypes.Index,
            "FUTURE" => SecurityTypes.Future,
            "OPTION" => SecurityTypes.Option,
            "ETF" => SecurityTypes.Stock,
            _ => SecurityTypes.Stock,
        };

    public static OptionTypes? ToOptionType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "CE" or "CALL" => OptionTypes.Call,
            "PE" or "PUT" => OptionTypes.Put,
            _ => null,
        };

    public static DateTime? ToPaytmTime(this string value)
    {
        if (value.IsEmpty())
            return null;

        if (DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var timestamp) &&
            (value.Contains('Z') ||
                value.Contains('+') ||
                value.EndsWith(" GMT", StringComparison.OrdinalIgnoreCase)))
        {
            return timestamp.UtcDateTime;
        }

        if (DateTime.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return new DateTimeOffset(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                _indiaOffset).UtcDateTime;
        }

        return null;
    }

    public static DateTime ToIndiaTime(this DateTime value)
    {
        if (value.Kind == DateTimeKind.Unspecified)
            return value;

        return new DateTimeOffset(value.ToUniversalTime())
            .ToOffset(_indiaOffset).DateTime;
    }

    public static DateTime? FromPaytmEpoch(long value)
    {
        if (value <= 0)
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(
            checked(value + 315532800L)).UtcDateTime;
    }

    public static decimal? ToDecimalInvariant(this string value)
        => decimal.TryParse(
            value, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static bool ToBoolean(this string value)
        => value?.Trim().ToUpperInvariant() is
            "TRUE" or "1" or "Y" or "YES";
}
