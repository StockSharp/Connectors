namespace StockSharp.WisdomCapital.Native;

static class WisdomCapitalExtensions
{
    private static readonly IReadOnlyDictionary<string, int>
        _segmentIds = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["NSECM"] = 1,
            ["NSEFO"] = 2,
            ["NSECD"] = 3,
            ["BSECM"] = 11,
            ["BSEFO"] = 12,
            ["MCXFO"] = 51,
        };

    private static readonly IReadOnlyDictionary<int, string>
        _segments = _segmentIds.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static int ToSegmentId(this string value)
    {
        value.ThrowIfEmpty(nameof(value));
        if (_segmentIds.TryGetValue(value, out var id))
            return id;
        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            "Unsupported Wisdom Capital XTS exchange segment.");
    }

    public static string ToExchangeSegment(this int value)
    {
        if (_segments.TryGetValue(value, out var segment))
            return segment;
        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            "Unsupported Wisdom Capital XTS exchange segment ID.");
    }

    public static string ToBoardCode(this string segment)
        => segment?.ToUpperInvariant() switch
        {
            "NSECM" => "NSE",
            "NSEFO" => "NFO",
            "NSECD" => "CDS",
            "BSECM" => "BSE",
            "BSEFO" => "BFO",
            "MCXFO" => "MCX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(segment),
                segment,
                "Unsupported Wisdom Capital XTS exchange segment."),
        };

    public static string ToExchangeSegmentFromBoard(this string board)
        => board?.ToUpperInvariant() switch
        {
            "NSE" => "NSECM",
            "NFO" => "NSEFO",
            "CDS" => "NSECD",
            "BSE" => "BSECM",
            "BFO" => "BSEFO",
            "MCX" => "MCXFO",
            _ => throw new ArgumentOutOfRangeException(
                nameof(board),
                board,
                "Unsupported Wisdom Capital board."),
        };

    public static string CreateInstrumentKey(
        string segment,
        long instrumentId)
        => $"{segment.ThrowIfEmpty(nameof(segment)).ToUpperInvariant()}:{instrumentId.ToString(CultureInfo.InvariantCulture)}";

    public static string CreateInstrumentKey(
        int segmentId,
        long instrumentId)
        => CreateInstrumentKey(segmentId.ToExchangeSegment(), instrumentId);

    public static WisdomInstrumentReference ToReference(
        this WisdomInstrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        return new(
            instrument.ExchangeSegment,
            instrument.SegmentId,
            instrument.ExchangeInstrumentId);
    }

    public static SecurityId ToSecurityId(this WisdomInstrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        return new()
        {
            SecurityCode = instrument.TradingSymbol
                .IsEmpty(instrument.DisplayName)
                .IsEmpty(instrument.Description)
                .IsEmpty(instrument.Name)
                .IsEmpty(
                    instrument.ExchangeInstrumentId.ToString(
                        CultureInfo.InvariantCulture)),
            BoardCode = instrument.ExchangeSegment.ToBoardCode(),
            Native = CreateInstrumentKey(
                instrument.ExchangeSegment,
                instrument.ExchangeInstrumentId),
            Isin = instrument.Isin,
        };
    }

    public static SecurityTypes ToSecurityType(
        this WisdomInstrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        if (instrument.IsIndex)
            return SecurityTypes.Index;
        if (instrument.OptionTypeCode is 3 or 4)
            return SecurityTypes.Option;
        if (instrument.OptionTypeCode == 1)
            return SecurityTypes.Future;
        var type = instrument.InstrumentType?.ToUpperInvariant();
        if (type?.Contains("OPT", StringComparison.Ordinal) == true)
            return SecurityTypes.Option;
        if (type?.Contains("FUT", StringComparison.Ordinal) == true)
            return SecurityTypes.Future;
        return SecurityTypes.Stock;
    }

    public static OptionTypes? ToOptionType(this int? value)
        => value switch
        {
            3 => OptionTypes.Call,
            4 => OptionTypes.Put,
            _ => null,
        };

    public static string ToNative(this WisdomCapitalProducts value)
        => value switch
        {
            WisdomCapitalProducts.CashAndCarry => "CNC",
            WisdomCapitalProducts.Intraday => "MIS",
            WisdomCapitalProducts.Normal => "NRML",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                null),
        };

    public static WisdomCapitalProducts ToProduct(this string value)
        => value?.ToUpperInvariant() switch
        {
            "CNC" => WisdomCapitalProducts.CashAndCarry,
            "NRML" => WisdomCapitalProducts.Normal,
            _ => WisdomCapitalProducts.Intraday,
        };

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "BUY" : "SELL";

    public static Sides ToSide(this string value)
        => value.EqualsIgnoreCase("BUY")
            ? Sides.Buy
            : Sides.Sell;

    public static OrderTypes ToOrderType(this string value)
    {
        var normalized = value?
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized switch
        {
            "MARKET" => OrderTypes.Market,
            "STOPMARKET" or "STOPLIMIT" or "SL" or "SLM" =>
                OrderTypes.Conditional,
            _ => OrderTypes.Limit,
        };
    }

    public static OrderStates ToOrderState(this string value)
    {
        var status = value?
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        if (status.IsEmpty())
            return OrderStates.Pending;
        if (status.Contains("REJECT", StringComparison.Ordinal) ||
            status.Contains("FAIL", StringComparison.Ordinal) ||
            status.Contains("ERROR", StringComparison.Ordinal))
            return OrderStates.Failed;
        if (status.Contains("CANCEL", StringComparison.Ordinal) ||
            status.Contains("FILL", StringComparison.Ordinal) ||
            status.Contains("COMPLETE", StringComparison.Ordinal) ||
            status.Contains("TRADED", StringComparison.Ordinal))
            return OrderStates.Done;
        if (status.Contains("NEW", StringComparison.Ordinal) ||
            status.Contains("OPEN", StringComparison.Ordinal) ||
            status.Contains("ACTIVE", StringComparison.Ordinal) ||
            status.Contains("PARTIAL", StringComparison.Ordinal) ||
            status.Contains("REPLACE", StringComparison.Ordinal))
            return OrderStates.Active;
        return OrderStates.Pending;
    }

    public static TimeInForce ToTimeInForce(this string value)
        => value.EqualsIgnoreCase("IOC")
            ? TimeInForce.CancelBalance
            : TimeInForce.PutInQueue;

    public static DateTime ToWisdomTime(
        this string value,
        DateTime fallback)
    {
        if (!value.IsEmpty())
        {
            var formats = new[]
            {
                "dd-MM-yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd'T'HH:mm:ss",
                "dd-MMM-yyyy HH:mm:ss",
            };
            if (DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var local))
            {
                return new DateTimeOffset(
                    DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                    TimeSpan.FromMinutes(330)).UtcDateTime;
            }
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
                return parsed.UtcDateTime;
        }
        return EnsureUtc(fallback);
    }

    public static DateTime ToWisdomTime(
        this JToken value,
        DateTime fallback)
    {
        if (value == null || value.Type == JTokenType.Null)
            return EnsureUtc(fallback);
        if (value.Type is JTokenType.Integer or JTokenType.Float)
        {
            var timestamp = value.Value<long>();
            if (timestamp > 10_000_000_000)
                timestamp /= 1000;
            if (timestamp > 0)
            {
                try
                {
                    var unix = DateTimeOffset
                        .FromUnixTimeSeconds(timestamp)
                        .UtcDateTime;
                    var dosLocal = new DateTime(
                        1980,
                        1,
                        1,
                        0,
                        0,
                        0,
                        DateTimeKind.Unspecified)
                        .AddSeconds(timestamp);
                    var dos = new DateTimeOffset(
                        dosLocal,
                        TimeSpan.FromMinutes(330)).UtcDateTime;
                    var reference = EnsureUtc(fallback);
                    return Math.Abs(
                            (reference - dos).TotalDays) <
                        Math.Abs((reference - unix).TotalDays)
                            ? dos
                            : unix;
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }
        return value.Value<string>().ToWisdomTime(fallback);
    }

    public static string TokenString(JToken value)
    {
        if (value == null || value.Type == JTokenType.Null)
            return null;
        if (value.Type == JTokenType.Float)
            return decimal.Truncate(value.Value<decimal>())
                .ToString(CultureInfo.InvariantCulture);
        return value.Value<string>();
    }

    public static decimal DecimalAt(JToken token, string name)
    {
        var value = FindProperty(token, name);
        if (value == null || value.Type == JTokenType.Null)
            return 0;
        if (value.Type is JTokenType.Integer or JTokenType.Float)
            return value.Value<decimal>();
        return decimal.TryParse(
            value.Value<string>(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0;
    }

    public static long LongAt(JToken token, string name)
    {
        var value = FindProperty(token, name);
        if (value == null || value.Type == JTokenType.Null)
            return 0;
        if (value.Type == JTokenType.Integer)
            return value.Value<long>();
        return long.TryParse(
            value.Value<string>(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : 0;
    }

    public static JToken FindProperty(JToken token, string name)
    {
        if (token is not JObject obj)
            return null;
        return obj.Properties()
            .FirstOrDefault(property => property.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
