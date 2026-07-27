namespace StockSharp.Primary;

readonly record struct PrimarySecurityKey(
    string Market,
    string Segment,
    string CfiCode,
    string Symbol)
{
    public string LookupKey =>
        $"{Market?.Trim().ToUpperInvariant()}|" +
        Symbol?.Trim().ToUpperInvariant();

    public string BoardCode =>
        PrimaryExtensions.ToBoardCode(Market, Segment, Symbol);

    public override string ToString()
        => string.Join(
            '|',
            Market.IsEmpty("ROFX").Trim().ToUpperInvariant(),
            Segment?.Trim().ToUpperInvariant(),
            CfiCode?.Trim().ToUpperInvariant(),
            Symbol.ThrowIfEmpty(nameof(Symbol)).Trim());
}

static class PrimaryExtensions
{
    private static readonly string[] _transactionFormats =
    [
        "yyyyMMdd-HH:mm:ss",
        "yyyyMMdd-HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
    ];

    public static PrimarySecurityKey ToNative(
        this PrimaryInstrument instrument)
        => new(
            instrument.InstrumentId?.MarketId
                .IsEmpty(instrument.Segment?.MarketId)
                .IsEmpty("ROFX"),
            instrument.Segment?.MarketSegmentId,
            instrument.CfiCode,
            instrument.InstrumentId?.Symbol
                .IsEmpty(instrument.Symbol)
                .ThrowIfEmpty(nameof(instrument.InstrumentId)));

    public static PrimarySecurityKey ToPrimaryNative(
        this SecurityId securityId,
        string defaultMarket)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            var parts = native.Split('|');
            if (parts.Length == 4 && !parts[3].IsEmpty())
            {
                return new(
                    parts[0].IsEmpty(defaultMarket).IsEmpty("ROFX"),
                    parts[1],
                    parts[2],
                    parts[3]);
            }
        }

        var board = securityId.BoardCode?.Trim();
        return new(
            defaultMarket.IsEmpty("ROFX"),
            board.EqualsIgnoreCase("BYMA") ||
                board.EqualsIgnoreCase("BCBA")
                    ? "MERV"
                    : null,
            null,
            securityId.SecurityCode.ThrowIfEmpty(
                nameof(securityId.SecurityCode)));
    }

    public static SecurityId ToSecurityId(this PrimarySecurityKey native)
        => new()
        {
            SecurityCode = native.Symbol,
            BoardCode = native.BoardCode,
            Native = native.ToString(),
        };

    public static SecurityMessage ToSecurityMessage(
        this PrimaryInstrument instrument,
        long originalTransactionId)
    {
        var native = instrument.ToNative();
        return new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = native.ToSecurityId(),
            Name = instrument.SecurityDescription
                .IsEmpty(native.Symbol),
            ShortName = native.Symbol,
            Class = native.Segment.IsEmpty(native.CfiCode),
            SecurityType = native.CfiCode.ToSecurityType(),
            Currency = instrument.Currency.ToCurrency(),
            ExpiryDate = instrument.MaturityDate.ToExpiry(),
            PriceStep = instrument.MinPriceIncrement.Positive(),
            VolumeStep = instrument.MinTradeVolume.Positive(),
            Multiplier = instrument.ContractMultiplier.Positive(),
        };
    }

    public static string ToBoardCode(
        string market,
        string segment,
        string symbol)
    {
        if (segment.EqualsIgnoreCase("MERV") ||
            symbol?.StartsWith(
                "MERV -", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "BYMA";
        }

        return market.IsEmpty("ROFX").Trim().ToUpperInvariant() switch
        {
            "MERV" or "BCBA" or "BYMA" or "XMEV" => "BYMA",
            "ROFX" or "MATBA-ROFEX" or "MATBAROFEX" => "ROFEX",
            var value => value,
        };
    }

    public static SecurityTypes ToSecurityType(this string cfiCode)
    {
        var value = cfiCode?.Trim().ToUpperInvariant();
        if (value.IsEmpty())
            return SecurityTypes.Stock;
        if (value.StartsWith("ES", StringComparison.Ordinal))
            return SecurityTypes.Stock;
        if (value.StartsWith("EM", StringComparison.Ordinal))
            return SecurityTypes.Adr;
        if (value.StartsWith("DB", StringComparison.Ordinal) ||
            value.StartsWith("DY", StringComparison.Ordinal))
        {
            return SecurityTypes.Bond;
        }
        if (value.StartsWith("OC", StringComparison.Ordinal) ||
            value.StartsWith("OP", StringComparison.Ordinal))
        {
            return SecurityTypes.Option;
        }
        if (value.StartsWith('F'))
            return SecurityTypes.Future;
        if (value.StartsWith("MRI", StringComparison.Ordinal))
            return SecurityTypes.Index;
        if (value.StartsWith("RP", StringComparison.Ordinal))
            return SecurityTypes.Repo;
        return SecurityTypes.Stock;
    }

    public static CurrencyTypes? ToCurrency(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "ARS" or "$" => CurrencyTypes.ARS,
            "USD" or "U$S" or "US$" => CurrencyTypes.USD,
            "EUR" => CurrencyTypes.EUR,
            "BRL" => CurrencyTypes.BRL,
            _ => null,
        };

    public static DateTime? ToExpiry(this string value)
        => DateTime.TryParseExact(
            value,
            ["yyyyMMdd", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
            out var result)
                ? result
                : null;

    public static DateTime ToUtc(
        this long unixMilliseconds,
        DateTime fallback)
    {
        if (unixMilliseconds <= 0)
            return fallback;
        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(unixMilliseconds)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback;
        }
    }

    public static DateTime ToUtc(
        this string value,
        DateTime fallback)
    {
        if (value.IsEmpty())
            return fallback;
        if (long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var unix))
        {
            return unix.ToUtc(fallback);
        }
        if (!DateTime.TryParseExact(
            value.Trim(),
            _transactionFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return fallback;
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            TimeSpan.FromHours(-3)).UtcDateTime;
    }

    public static string ToNative(this Sides side)
        => side == Sides.Sell ? "SELL" : "BUY";

    public static Sides ToSide(this string value)
        => value.EqualsIgnoreCase("SELL")
            ? Sides.Sell
            : Sides.Buy;

    public static string ToNative(this OrderTypes? type)
        => type == OrderTypes.Market ? "MARKET" : "LIMIT";

    public static OrderTypes ToOrderType(this string value)
        => value.EqualsIgnoreCase("MARKET") ||
            value.EqualsIgnoreCase("MARKET_TO_LIMIT")
                ? OrderTypes.Market
                : OrderTypes.Limit;

    public static string ToNative(
        this TimeInForce? value,
        DateTime? tillDate)
        => tillDate is not null
            ? "GTD"
            : value switch
            {
                TimeInForce.MatchOrCancel => "FOK",
                TimeInForce.CancelBalance => "IOC",
                _ => "DAY",
            };

    public static TimeInForce ToTimeInForce(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "FOK" => TimeInForce.MatchOrCancel,
            "IOC" => TimeInForce.CancelBalance,
            _ => TimeInForce.PutInQueue,
        };

    public static OrderStates ToOrderState(this string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "REJECTED" => OrderStates.Failed,
            "CANCELLED" or "FILLED" or "EXPIRED" => OrderStates.Done,
            "NEW" or "PARTIALLY_FILLED" or "PENDING_CANCEL" or
                "PENDING_REPLACE" => OrderStates.Active,
            _ => OrderStates.Pending,
        };

    public static decimal? Positive(this decimal value)
        => value > 0 ? value : null;
}
