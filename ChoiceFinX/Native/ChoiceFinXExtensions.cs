namespace StockSharp.ChoiceFinX.Native;

static class ChoiceFinXExtensions
{
    private static readonly DateTime _choiceEpoch =
        new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeZoneInfo _indiaTimeZone =
        GetIndiaTimeZone();

    public static string ToInstrumentKey(
        this SecurityId securityId)
    {
        if (securityId.Native is string native &&
            !native.IsEmpty())
        {
            native.ParseInstrumentKey();
            return native.Replace('@', '|');
        }

        if (!securityId.SecurityCode.IsEmpty() &&
            (securityId.SecurityCode.Contains('|') ||
                securityId.SecurityCode.Contains('@')))
        {
            var key = securityId.SecurityCode.Replace('@', '|');
            key.ParseInstrumentKey();
            return key;
        }

        var segment = securityId.BoardCode.ToSegmentId();
        if (segment != 0 &&
            long.TryParse(
                securityId.SecurityCode,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var token) &&
            token > 0)
        {
            return CreateInstrumentKey(segment, token);
        }

        throw new InvalidOperationException(
            "Choice FinX instruments require native id 'SegmentId|Token'.");
    }

    public static string ToInstrumentKey(
        this ChoiceFinXInstrument instrument)
        => CreateInstrumentKey(
            instrument.SegmentId, instrument.Token);

    public static string CreateInstrumentKey(
        int segmentId, long token)
    {
        if (segmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentId), segmentId,
                "Choice FinX segment id must be positive.");
        }
        if (token <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(token), token,
                "Choice FinX token must be positive.");
        }
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{segmentId}|{token}");
    }

    public static (int segmentId, long token)
        ParseInstrumentKey(this string value)
    {
        var parts = value?
            .Replace('@', '|')
            .Split('|');
        if (parts?.Length != 2 ||
            !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var segmentId) ||
            segmentId <= 0 ||
            !long.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var token) ||
            token <= 0)
        {
            throw new FormatException(
                $"Invalid Choice FinX instrument id '{value}'. Expected 'SegmentId|Token'.");
        }
        return (segmentId, token);
    }

    public static SecurityId ToSecurityId(
        this ChoiceFinXInstrument instrument)
        => new()
        {
            SecurityCode = instrument.Symbol.IsEmpty(
                instrument.Token.ToString(
                    CultureInfo.InvariantCulture)),
            BoardCode = instrument.SegmentId.ToBoardCode(),
            Native = instrument.ToInstrumentKey(),
        };

    public static SecurityId ToSecurityId(
        this int segmentId,
        long token,
        string symbol = null)
        => new()
        {
            SecurityCode = symbol.IsEmpty(
                token.ToString(CultureInfo.InvariantCulture)),
            BoardCode = segmentId.ToBoardCode(),
            Native = CreateInstrumentKey(segmentId, token),
        };

    public static string ToBoardCode(this int segmentId)
        => segmentId switch
        {
            1 => "NSE",
            2 => "NFO",
            3 => "BSE",
            4 => "BFO",
            5 or 6 => "MCX",
            7 or 8 => "NCDEX",
            13 or 14 => "CDS",
            25 => "BSE",
            33 => "NSE",
            34 => "ICEX",
            38 => "BCD",
            _ => $"CHOICE_{segmentId}",
        };

    public static int ToSegmentId(this string boardCode)
        => boardCode?.ToUpperInvariant() switch
        {
            "NSE" or "NSE_EQ" => 1,
            "NFO" or "NSE_FNO" => 2,
            "BSE" or "BSE_EQ" => 3,
            "BFO" or "BSE_FNO" => 4,
            "MCX" => 5,
            "NCDEX" => 7,
            "CDS" or "NSE_CDS" => 13,
            "BCD" or "BSE_CDS" => 38,
            "ICEX" => 34,
            _ => 0,
        };

    public static SecurityTypes ToSecurityType(
        this ChoiceFinXInstrument instrument)
    {
        var type = string.Join(
            " ",
            instrument.Instrument,
            instrument.Series,
            instrument.OptionType)
            .ToUpperInvariant();
        if (type.Contains("OPT") ||
            type.Contains("CALL") ||
            type.Contains("PUT") ||
            type is "CE" or "PE")
        {
            return SecurityTypes.Option;
        }
        if (type.Contains("FUT") ||
            instrument.SegmentId is 2 or 4 or 5 or 7 or 13)
        {
            return SecurityTypes.Future;
        }
        if (type.Contains("INDEX"))
            return SecurityTypes.Index;
        if (type.Contains("ETF"))
            return SecurityTypes.Fund;
        if (instrument.SegmentId is 6 or 8)
            return SecurityTypes.Commodity;
        if (instrument.SegmentId is 14 or 38)
            return SecurityTypes.Currency;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this string value)
    {
        value = value?.Trim().ToUpperInvariant();
        if (value is "CE" or "C" or "CALL")
            return OptionTypes.Call;
        if (value is "PE" or "P" or "PUT")
            return OptionTypes.Put;
        return null;
    }

    public static int ToNative(this Sides side)
        => side == Sides.Buy ? 1 : 2;

    public static Sides ToSide(this int side)
        => side == 2 ? Sides.Sell : Sides.Buy;

    public static string ToProduct(
        this ChoiceFinXProducts product,
        bool afterMarket)
        => (product, afterMarket) switch
        {
            (ChoiceFinXProducts.Intraday, true) => "AM",
            (ChoiceFinXProducts.Delivery, true) => "AD",
            (ChoiceFinXProducts.Intraday, false) => "M",
            _ => "D",
        };

    public static ChoiceFinXProducts ToProduct(
        this string product)
        => product?.ToUpperInvariant() is "M" or "AM"
            ? ChoiceFinXProducts.Intraday
            : ChoiceFinXProducts.Delivery;

    public static int ToValidity(this TimeInForce? value)
        => value == TimeInForce.CancelBalance ? 4 : 1;

    public static TimeInForce ToTimeInForce(this int value)
        => value == 4
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static OrderStates ToOrderState(this string value)
    {
        var status = value?
            .Trim()
            .Replace("_", " ")
            .ToUpperInvariant();
        if (status.IsEmpty())
            return OrderStates.None;
        if (status.Contains("REJECT") ||
            status.Contains("ERROR") ||
            status.Contains("FROZEN"))
        {
            return OrderStates.Failed;
        }
        if (status.Contains("CANCEL") ||
            status.Contains("EXECUTED") ||
            status.Contains("COMPLETED") ||
            status.Contains("FULLY TRADED"))
        {
            return OrderStates.Done;
        }
        if (status.Contains("PENDING") ||
            status.Contains("XMITTED") ||
            status.Contains("OPEN") ||
            status.Contains("ACCEPT") ||
            status.Contains("MODIF") ||
            status.Contains("SUBMITTED") ||
            status is "NEW" or "PLACED")
        {
            return OrderStates.Active;
        }
        return OrderStates.None;
    }

    public static OrderTypes ToOrderType(
        this string orderType)
        => orderType?.ToUpperInvariant() switch
        {
            "RL_MKT" or "MARKET" or "MKT" => OrderTypes.Market,
            "SL_LIMIT" or "SL_MKT" or "STOP" or
                "STOPLOSS" => OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };

    public static JToken GetToken(
        this JObject value, params string[] names)
    {
        if (value == null)
            return null;
        foreach (var name in names)
        {
            var property = value.Properties()
                .FirstOrDefault(item =>
                    item.Name.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase));
            if (property != null)
                return property.Value;
        }
        return null;
    }

    public static string GetText(
        this JObject value, params string[] names)
    {
        var token = value.GetToken(names);
        if (token == null ||
            token.Type is JTokenType.Null or
                JTokenType.Undefined)
        {
            return null;
        }
        return token.Type == JTokenType.String
            ? token.Value<string>()?.Trim()
            : token.ToString(Formatting.None).Trim('"', ' ');
    }

    public static decimal GetDecimal(
        this JObject value, params string[] names)
        => value.GetNullableDecimal(names) ?? 0;

    public static decimal? GetNullableDecimal(
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

    public static long GetLong(
        this JObject value, params string[] names)
    {
        var text = value.GetText(names);
        if (long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return result;
        }
        return decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var number)
                ? decimal.ToInt64(decimal.Truncate(number))
                : 0;
    }

    public static int GetInt(
        this JObject value, params string[] names)
    {
        var value64 = value.GetLong(names);
        return value64 is >= int.MinValue and <= int.MaxValue
            ? (int)value64
            : 0;
    }

    public static DateTime? GetChoiceTime(
        this JObject value, params string[] names)
        => value.GetToken(names).ToChoiceTime();

    public static DateTime? ToChoiceTime(this JToken token)
    {
        if (token == null ||
            token.Type is JTokenType.Null or
                JTokenType.Undefined)
        {
            return null;
        }

        if (token.Type is JTokenType.Integer or
            JTokenType.Float)
        {
            var number = token.Value<decimal>();
            if (number <= 0)
                return null;
            if (number > 100000000000)
            {
                return DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        decimal.ToInt64(number))
                    .UtcDateTime;
            }
            if (number > 2000000000)
            {
                return _choiceEpoch.AddSeconds(
                    decimal.ToDouble(number));
            }
            return DateTimeOffset
                .FromUnixTimeSeconds(decimal.ToInt64(number))
                .UtcDateTime;
        }

        var text = token.ToString().Trim();
        if (text.IsEmpty())
            return null;
        if (long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var numeric))
        {
            return new JValue(numeric).ToChoiceTime();
        }
        if (!DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed))
        {
            return null;
        }
        if (parsed.Kind == DateTimeKind.Utc)
            return parsed;
        if (parsed.Kind == DateTimeKind.Local)
            return parsed.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                parsed, DateTimeKind.Unspecified),
            _indiaTimeZone);
    }

    public static int ToChoiceEpoch(this DateTime value)
    {
        var seconds = (
            value.ToUniversalTime() - _choiceEpoch)
            .TotalSeconds;
        if (seconds < 0 || seconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "Choice FinX chart dates must fit its 1980 epoch.");
        }
        return Convert.ToInt32(
            Math.Floor(seconds),
            CultureInfo.InvariantCulture);
    }

    public static DateTime FromChoiceEpoch(decimal seconds)
        => _choiceEpoch.AddSeconds(
            decimal.ToDouble(seconds));

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
