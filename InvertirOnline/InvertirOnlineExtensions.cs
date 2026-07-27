namespace StockSharp.InvertirOnline;

readonly record struct IolSecurityKey(
    string Country,
    string Market,
    string InstrumentType,
    string Settlement,
    string Symbol)
{
    public string GroupKey =>
        $"{Country.Trim().ToUpperInvariant()}|" +
        InstrumentType.Trim().ToUpperInvariant();

    public string QuoteKey =>
        $"{Market.Trim().ToUpperInvariant()}|" +
        $"{Symbol.Trim().ToUpperInvariant()}|" +
        Settlement.Trim().ToUpperInvariant();

    public override string ToString()
        => string.Join(
            '|',
            Country.IsEmpty("argentina").Trim(),
            Market.IsEmpty("BCBA").Trim().ToUpperInvariant(),
            InstrumentType.IsEmpty("acciones").Trim(),
            Settlement.IsEmpty("t1").Trim().ToLowerInvariant(),
            Symbol.ThrowIfEmpty(nameof(Symbol)).Trim().ToUpperInvariant());
}

static class InvertirOnlineExtensions
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromDays(1),
    ];

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static string ToNative(this InvertirOnlineCountries value)
        => value == InvertirOnlineCountries.UnitedStates
            ? "estados_Unidos"
            : "argentina";

    public static InvertirOnlineCountries ToCountry(this string value)
    {
        var normalized = Normalize(value);
        return normalized.Contains("ESTADOSUNIDOS", StringComparison.Ordinal) ||
            normalized is "USA" or "US"
                ? InvertirOnlineCountries.UnitedStates
                : InvertirOnlineCountries.Argentina;
    }

    public static string ToNative(this InvertirOnlineSettlements value)
        => value.ToString().ToLowerInvariant();

    public static string ToSettlement(this string value, string fallback)
        => Normalize(value) switch
        {
            "T0" or "INMEDIATA" => "t0",
            "T1" or "A24HORAS" or "HRS24" => "t1",
            "T2" or "A48HORAS" or "HRS48" => "t2",
            "T3" or "A72HORAS" or "HRS72" => "t3",
            _ => fallback.IsEmpty("t1").ToLowerInvariant(),
        };

    public static string ToBoardCode(this string market)
        => Normalize(market) switch
        {
            "BYMA" or "BCBA" => "BCBA",
            "NYSE" => "NYSE",
            "NASDAQ" => "NASDAQ",
            "AMEX" => "AMEX",
            "ROFX" or "MATBAROFEX" => "ROFX",
            "BCS" => "BCS",
            var value when !value.IsEmpty() => value,
            _ => "BCBA",
        };

    public static string ToApiMarket(this string board)
        => board.ToBoardCode();

    public static string InferCountry(this string market)
        => market.ToBoardCode() is "NYSE" or "NASDAQ" or "AMEX"
            ? "estados_Unidos"
            : "argentina";

    public static SecurityTypes ToSecurityType(this string value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("OPCION", StringComparison.Ordinal))
            return SecurityTypes.Option;
        if (normalized.Contains("FUTUR", StringComparison.Ordinal))
            return SecurityTypes.Future;
        if (normalized.Contains("CEDEAR", StringComparison.Ordinal) ||
            normalized is "ADR" or "ADRS")
        {
            return SecurityTypes.Adr;
        }
        if (normalized.Contains("INDICE", StringComparison.Ordinal))
            return SecurityTypes.Index;
        if (normalized.Contains("DIVISA", StringComparison.Ordinal))
            return SecurityTypes.Currency;
        if (normalized.Contains("CAUCION", StringComparison.Ordinal))
            return SecurityTypes.Repo;
        if (normalized.Contains("FONDO", StringComparison.Ordinal) ||
            normalized is "FCI")
        {
            return SecurityTypes.Fund;
        }
        if (normalized.Contains("ETF", StringComparison.Ordinal))
            return SecurityTypes.Etf;
        if (normalized.Contains("BON", StringComparison.Ordinal) ||
            normalized.Contains("LETRA", StringComparison.Ordinal) ||
            normalized.Contains("TITULO", StringComparison.Ordinal) ||
            normalized.Contains("OBLIGACION", StringComparison.Ordinal) ||
            normalized.Contains("CUPON", StringComparison.Ordinal) ||
            normalized.Contains("DEUDA", StringComparison.Ordinal) ||
            normalized.Contains("CERTIFICADO", StringComparison.Ordinal))
        {
            return SecurityTypes.Bond;
        }
        if (normalized.Contains("CHEQUE", StringComparison.Ordinal) ||
            normalized.Contains("CHPD", StringComparison.Ordinal))
        {
            return SecurityTypes.Forward;
        }
        if (normalized is "SOJA" or "MAIZ" or "TRIGO" or "ORO" or
            "PETROLEO")
        {
            return SecurityTypes.Commodity;
        }
        return SecurityTypes.Stock;
    }

    public static string ToNativeType(this SecurityTypes value)
        => value switch
        {
            SecurityTypes.Option => "opciones",
            SecurityTypes.Future or SecurityTypes.Commodity => "futuros",
            SecurityTypes.Adr => "cedears",
            SecurityTypes.Bond => "titulosPublicos",
            SecurityTypes.Fund => "fondosMutuosUSA",
            SecurityTypes.Repo => "cauciones",
            SecurityTypes.Currency => "divisas",
            SecurityTypes.Forward => "cHPD",
            _ => "acciones",
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Normalize(value) switch
        {
            "ARS" or "PESOARGENTINO" or "PESO" or "PESOS" =>
                CurrencyTypes.ARS,
            "USD" or "DOLARESTADOUNIDENSE" or "DOLAR" or "DOLARES" or
                "DOLARBNA" or "DOLARBOLSA" => CurrencyTypes.USD,
            "BRL" or "REAL" => ParseCurrency("BRL"),
            "MXN" or "PESOMEXICANO" => ParseCurrency("MXN"),
            "CLP" or "PESOCHILENO" => ParseCurrency("CLP"),
            "JPY" or "YEN" => CurrencyTypes.JPY,
            "GBP" or "LIBRA" => CurrencyTypes.GBP,
            "EUR" or "EURO" => CurrencyTypes.EUR,
            "PEN" or "PESOPERUANO" => ParseCurrency("PEN"),
            "COP" or "PESOCOLOMBIANO" => ParseCurrency("COP"),
            "UYU" or "PESOURUGUAYO" => ParseCurrency("UYU"),
            _ => null,
        };

    public static OptionTypes? ToOptionType(this string value)
        => Normalize(value) switch
        {
            "CALL" or "C" or "COMPRA" => OptionTypes.Call,
            "PUT" or "P" or "VENTA" => OptionTypes.Put,
            _ => null,
        };

    public static Sides ToSide(this string value)
        => Normalize(value).Contains("VENTA", StringComparison.Ordinal) ||
            Normalize(value).Contains("SELL", StringComparison.Ordinal)
                ? Sides.Sell
                : Sides.Buy;

    public static string ToNativeOrderType(this OrderTypes value)
        => value == OrderTypes.Market
            ? "precioMercado"
            : "precioLimite";

    public static OrderTypes ToOrderType(this string value)
        => Normalize(value).Contains("MERCADO", StringComparison.Ordinal)
            ? OrderTypes.Market
            : OrderTypes.Limit;

    public static OrderStates ToOrderState(this string value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("PARCIAL", StringComparison.Ordinal) ||
            normalized.Contains("PROCESO", StringComparison.Ordinal) ||
            normalized.Contains("MODIFICACION", StringComparison.Ordinal))
        {
            return OrderStates.Active;
        }
        if (normalized.Contains("TERMINADA", StringComparison.Ordinal) ||
            normalized.Contains("CANCELADA", StringComparison.Ordinal) ||
            normalized.Contains("VENCIMIENTO", StringComparison.Ordinal))
        {
            return OrderStates.Done;
        }
        if (normalized.Contains("RECHAZ", StringComparison.Ordinal) ||
            normalized.Contains("ERROR", StringComparison.Ordinal) ||
            normalized.Contains("FALL", StringComparison.Ordinal))
        {
            return OrderStates.Failed;
        }
        if (normalized.Contains("INICIADA", StringComparison.Ordinal) ||
            normalized.Contains("PENDIENTE", StringComparison.Ordinal))
        {
            return OrderStates.Active;
        }
        return OrderStates.Pending;
    }

    public static IolSecurityKey ToNative(
        this IolInstrument instrument,
        string country,
        string instrumentType,
        string defaultMarket,
        string defaultSettlement)
        => new(
            country.IsEmpty(instrument.Market.InferCountry()),
            instrument.Market.IsEmpty(defaultMarket).ToApiMarket(),
            instrumentType.IsEmpty("acciones"),
            instrument.Settlement.ToSettlement(defaultSettlement),
            instrument.Symbol.ThrowIfEmpty(nameof(instrument.Symbol)));

    public static IolSecurityKey ToNative(
        this IolTitle title,
        string defaultCountry,
        string defaultMarket,
        string defaultType,
        string defaultSettlement)
        => new(
            title.Country.IsEmpty(defaultCountry),
            title.Market.IsEmpty(defaultMarket).ToApiMarket(),
            title.InstrumentType.IsEmpty(defaultType).IsEmpty("acciones"),
            title.Settlement.ToSettlement(defaultSettlement),
            title.Symbol.ThrowIfEmpty(nameof(title.Symbol)));

    public static IolSecurityKey ToIolNative(
        this SecurityId securityId,
        string defaultCountry,
        string defaultMarket,
        string defaultType,
        string defaultSettlement)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            var parts = native.Split('|');
            if (parts.Length == 5 && !parts[4].IsEmpty())
            {
                return new(
                    parts[0].IsEmpty(defaultCountry),
                    parts[1].IsEmpty(defaultMarket).ToApiMarket(),
                    parts[2].IsEmpty(defaultType).IsEmpty("acciones"),
                    parts[3].ToSettlement(defaultSettlement),
                    parts[4]);
            }
        }

        var market = securityId.BoardCode
            .IsEmpty(defaultMarket)
            .ToApiMarket();
        return new(
            market.InferCountry().IsEmpty(defaultCountry),
            market,
            defaultType.IsEmpty("acciones"),
            defaultSettlement.IsEmpty("t1"),
            securityId.SecurityCode.ThrowIfEmpty(
                nameof(securityId.SecurityCode)));
    }

    public static SecurityId ToSecurityId(this IolSecurityKey native)
        => new()
        {
            SecurityCode = native.Symbol,
            BoardCode = native.Market.ToBoardCode(),
            Native = native.ToString(),
        };

    public static SecurityMessage ToSecurityMessage(
        this IolInstrument instrument,
        long originalTransactionId,
        string country,
        string instrumentType,
        string defaultMarket,
        string defaultSettlement)
    {
        var native = instrument.ToNative(
            country,
            instrumentType,
            defaultMarket,
            defaultSettlement);
        var message = new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = native.ToSecurityId(),
            Name = instrument.Description.IsEmpty(instrument.Symbol),
            ShortName = instrument.Symbol,
            Class = native.InstrumentType,
            SecurityType = native.InstrumentType.ToSecurityType(),
            Currency = instrument.Currency.ToCurrency(),
            PriceStep = null,
            VolumeStep = (instrument.Lot > 0
                ? instrument.Lot
                : instrument.MinimumLot > 0
                    ? instrument.MinimumLot
                    : 0).Positive(),
            Strike = instrument.Strike.Positive(),
            OptionType = instrument.OptionType.ToOptionType(),
        };
        if (TryParseDate(instrument.ExpiryDate, out var expiry))
            message.ExpiryDate = expiry;
        return message;
    }

    public static SecurityMessage ToSecurityMessage(
        this IolTitle title,
        long originalTransactionId,
        string defaultCountry,
        string defaultMarket,
        string defaultType,
        string defaultSettlement)
    {
        var native = title.ToNative(
            defaultCountry,
            defaultMarket,
            defaultType,
            defaultSettlement);
        return new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = native.ToSecurityId(),
            Name = title.Description.IsEmpty(title.Symbol),
            ShortName = title.Symbol,
            Class = native.InstrumentType,
            SecurityType = native.InstrumentType.ToSecurityType(),
            Currency = title.Currency.ToCurrency(),
        };
    }

    public static DateTime ToUtc(
        this DateTimeOffset value,
        DateTime fallback)
        => value == default ? fallback : value.UtcDateTime;

    public static decimal? Positive(this decimal value)
        => value > 0 ? value : null;

    public static int? Positive(this int value)
        => value > 0 ? value : null;

    public static bool TryParseDate(string value, out DateTime date)
    {
        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed))
        {
            date = parsed.Date;
            return true;
        }
        date = default;
        return false;
    }

    private static string Normalize(string value)
    {
        if (value.IsEmpty())
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                UnicodeCategory.NonSpacingMark &&
                char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static CurrencyTypes? ParseCurrency(string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;
}
