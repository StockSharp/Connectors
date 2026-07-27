namespace StockSharp.Dnse;

readonly record struct DnseInstrumentKey(
    string MarketId,
    string BoardId,
    string SecurityGroupId,
    string Symbol)
{
    public override string ToString()
        => string.Join(
            '|',
            MarketId.IsEmpty("STO"),
            BoardId.IsEmpty("G1"),
            SecurityGroupId.IsEmpty("ST"),
            Symbol);
}

static class DnseExtensions
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
    ];

    private static readonly TimeZoneInfo _vietnamTimeZone =
        CreateVietnamTimeZone();

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static DnseInstrumentKey ToNative(
        this DnseInstrument instrument,
        string defaultBoardId)
        => new(
            instrument.MarketId.IsEmpty("STO"),
            defaultBoardId.IsEmpty("G1"),
            instrument.SecurityGroupId.IsEmpty("ST"),
            instrument.Symbol.ThrowIfEmpty(nameof(instrument.Symbol)));

    public static DnseInstrumentKey ToNative(
        this DnseSecurityDefinition definition)
        => new(
            definition.MarketId.IsEmpty("STO"),
            definition.BoardId.IsEmpty("G1"),
            definition.SecurityGroupId.IsEmpty("ST"),
            definition.Symbol.ThrowIfEmpty(nameof(definition.Symbol)));

    public static DnseInstrumentKey ToDnseNative(
        this SecurityId securityId,
        string defaultBoardId)
    {
        if (securityId.Native is string native &&
            !native.IsEmpty())
        {
            var parts = native.Split('|');
            if (parts.Length == 4 && !parts[3].IsEmpty())
            {
                return new(
                    parts[0].IsEmpty("STO"),
                    parts[1].IsEmpty(defaultBoardId).IsEmpty("G1"),
                    parts[2].IsEmpty("ST"),
                    parts[3]);
            }
        }

        return new(
            securityId.BoardCode.ToDnseMarketId(),
            defaultBoardId.IsEmpty("G1"),
            "ST",
            securityId.SecurityCode.ThrowIfEmpty(
                nameof(securityId.SecurityCode)));
    }

    public static SecurityId ToSecurityId(
        this DnseInstrumentKey native,
        string isin = null)
        => new()
        {
            SecurityCode = native.Symbol,
            BoardCode = native.MarketId.ToBoardCode(),
            Isin = isin,
            Native = native.ToString(),
        };

    public static SecurityMessage ToSecurityMessage(
        this DnseInstrument instrument,
        long originalTransactionId,
        string defaultBoardId)
    {
        var native = instrument.ToNative(defaultBoardId);
        return new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = native.ToSecurityId(),
            Name = instrument.Name.IsEmpty(instrument.ShortName),
            ShortName = instrument.ShortName.IsEmpty(instrument.Symbol),
            Class = instrument.IndexNames?.Join(","),
            SecurityType =
                instrument.SecurityGroupId.ToSecurityType(),
            Currency = CurrencyTypes.VND,
            PriceStep = native.MarketId == "STO" ? 10 : 100,
            VolumeStep = native.BoardId == "G4" ? 1 : 100,
            MinVolume = native.BoardId == "G4" ? 1 : 100,
            IssueDate = instrument.ListedDate.ToDnseDate(),
        };
    }

    public static string ToBoardCode(this string marketId)
        => marketId?.ToUpperInvariant() switch
        {
            "STX" => "HNX",
            "UPX" => "UPCOM",
            _ => "HOSE",
        };

    public static string ToDnseMarketId(this string boardCode)
        => boardCode?.ToUpperInvariant() switch
        {
            "HNX" => "STX",
            "UPCOM" => "UPX",
            _ => "STO",
        };

    public static SecurityTypes ToSecurityType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "EF" => SecurityTypes.Etf,
            "EW" => SecurityTypes.Warrant,
            "BS" => SecurityTypes.Bond,
            "FU" => SecurityTypes.Future,
            _ => SecurityTypes.Stock,
        };

    public static string ToSecurityGroupId(
        this IEnumerable<SecurityTypes> securityTypes)
    {
        var values = securityTypes?.Distinct().ToArray() ?? [];
        if (values.Length != 1)
            return null;
        return values[0] switch
        {
            SecurityTypes.Stock => "ST",
            SecurityTypes.Etf => "EF",
            SecurityTypes.Warrant => "EW",
            SecurityTypes.Bond => "BS",
            SecurityTypes.Future => "FU",
            _ => null,
        };
    }

    public static string ToResolution(this TimeSpan timeFrame)
        => timeFrame.TotalMinutes switch
        {
            1 => "1",
            3 => "3",
            5 => "5",
            15 => "15",
            30 => "30",
            60 => "1H",
            1440 => "1D",
            10080 => "1W",
            _ => throw new NotSupportedException(
                $"DNSE does not support {timeFrame} candles."),
        };

    public static TimeSpan ToTimeFrame(this string resolution)
        => resolution?.ToUpperInvariant() switch
        {
            "1" => TimeSpan.FromMinutes(1),
            "3" => TimeSpan.FromMinutes(3),
            "5" => TimeSpan.FromMinutes(5),
            "15" => TimeSpan.FromMinutes(15),
            "30" => TimeSpan.FromMinutes(30),
            "1H" => TimeSpan.FromHours(1),
            "1D" => TimeSpan.FromDays(1),
            "1W" => TimeSpan.FromDays(7),
            _ => default,
        };

    public static string ToNative(
        this DnseOtpTypes value)
        => value == DnseOtpTypes.Email
            ? "email_otp"
            : "smart_otp";

    public static string ToNative(
        this DnseOrderTypes value,
        OrderTypes? orderType,
        TimeInForce? timeInForce)
        => value switch
        {
            DnseOrderTypes.Limit => "LO",
            DnseOrderTypes.MatchOrKill => "MOK",
            DnseOrderTypes.MatchAndKill => "MAK",
            DnseOrderTypes.MarketToLimit => "MTL",
            DnseOrderTypes.AtOpen => "ATO",
            DnseOrderTypes.AtClose => "ATC",
            DnseOrderTypes.PostLimit => "PLO",
            _ when timeInForce == TimeInForce.MatchOrCancel => "MOK",
            _ when timeInForce == TimeInForce.CancelBalance => "MAK",
            _ when orderType == OrderTypes.Market => "MTL",
            _ => "LO",
        };

    public static Sides ToSide(this string value)
        => value.EqualsIgnoreCase("NS") ||
            value.EqualsIgnoreCase("SELL")
                ? Sides.Sell
                : Sides.Buy;

    public static string ToNative(this Sides value)
        => value == Sides.Sell ? "NS" : "NB";

    public static OrderTypes ToOrderType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "MOK" or "MAK" or "MTL" => OrderTypes.Market,
            _ => OrderTypes.Limit,
        };

    public static TimeInForce ToTimeInForce(this string value)
        => value?.ToUpperInvariant() switch
        {
            "MOK" => TimeInForce.MatchOrCancel,
            "MAK" => TimeInForce.CancelBalance,
            _ => TimeInForce.PutInQueue,
        };

    public static OrderStates ToOrderState(this string value)
        => value?.ToUpperInvariant() switch
        {
            "NEW" or "PARTIALLYFILLED" => OrderStates.Active,
            "FILLED" or "CANCELED" or "EXPIRED" or
                "DONEFORDAY" => OrderStates.Done,
            "REJECTED" => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static SecurityStates ToSecurityState(this string value)
        => value.EqualsIgnoreCase("HALT")
            ? SecurityStates.Stoped
            : SecurityStates.Trading;

    public static DateTime ToDnseTime(
        this JToken value,
        DateTime? fallback = null)
    {
        if (value is null || value.Type == JTokenType.Null)
            return fallback ?? DateTime.UtcNow;

        if (value is JObject map)
        {
            var seconds =
                map.Value<long?>("Seconds") ??
                map.Value<long?>("seconds");
            var nanos =
                map.Value<long?>("Nanos") ??
                map.Value<long?>("nanos") ??
                0;
            if (seconds is > 0)
            {
                return DateTimeOffset
                    .FromUnixTimeSeconds(seconds.Value)
                    .AddTicks(nanos / 100)
                    .UtcDateTime;
            }
        }

        if (value.Type is JTokenType.Integer or JTokenType.Float)
        {
            var numeric = value.Value<decimal>();
            try
            {
                return numeric > 1_000_000_000_000m
                    ? DateTimeOffset
                        .FromUnixTimeMilliseconds((long)numeric)
                        .UtcDateTime
                    : DateTimeOffset
                        .FromUnixTimeSeconds((long)numeric)
                        .UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return fallback ?? DateTime.UtcNow;
            }
        }

        var text = value.Value<string>();
        if (text.IsEmpty())
            return fallback ?? DateTime.UtcNow;
        if (DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
                DateTimeStyles.AssumeUniversal,
            out var offset) &&
            (text.EndsWith('Z') ||
                text.Contains('+') ||
                HasOffsetSuffix(text)))
        {
            return offset.UtcDateTime;
        }
        if (DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                _vietnamTimeZone);
        }
        return fallback ?? DateTime.UtcNow;
    }

    public static DateTime ToDnseTime(
        this string value,
        DateTime? fallback = null)
        => value.IsEmpty()
            ? fallback ?? DateTime.UtcNow
            : new JValue(value).ToDnseTime(fallback);

    public static DateTime? ToDnseDate(this string value)
    {
        if (value.IsEmpty())
            return null;
        if (DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        }
        return null;
    }

    public static decimal ScalePrice(
        this decimal value,
        decimal multiplier)
        => value * multiplier;

    public static decimal? ScalePrice(
        this decimal? value,
        decimal multiplier)
        => value * multiplier;

    public static decimal GetPriceStep(
        this DnseSecurityDefinition definition,
        decimal multiplier)
    {
        var price = definition.BasicPrice.ScalePrice(multiplier) ?? 0;
        if (!definition.MarketId.EqualsIgnoreCase("STO"))
            return 100;
        if (price < 10_000)
            return 10;
        if (price < 50_000)
            return 50;
        return 100;
    }

    private static bool HasOffsetSuffix(string value)
    {
        if (value.Length < 6)
            return false;
        var suffix = value[^6..];
        return (suffix[0] is '+' or '-') &&
            char.IsDigit(suffix[1]) &&
            char.IsDigit(suffix[2]) &&
            suffix[3] == ':' &&
            char.IsDigit(suffix[4]) &&
            char.IsDigit(suffix[5]);
    }

    private static TimeZoneInfo CreateVietnamTimeZone()
    {
        foreach (var id in new[]
        {
            "SE Asia Standard Time",
            "Asia/Ho_Chi_Minh",
        })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone(
            "Vietnam",
            TimeSpan.FromHours(7),
            "Vietnam",
            "Vietnam");
    }
}
