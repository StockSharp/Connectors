namespace StockSharp.Ppi;

readonly record struct PpiInstrumentKey(
    string Market,
    string Type,
    string Settlement,
    string Ticker)
{
    public string SubscriptionKey =>
        $"{Ticker.Trim().ToUpperInvariant()}|" +
        $"{Type.Trim().ToUpperInvariant()}|" +
        $"{Settlement.Trim().ToUpperInvariant()}";

    public override string ToString()
        => string.Join(
            '|',
            Market.IsEmpty("BYMA").Trim().ToUpperInvariant(),
            Type.IsEmpty("ACCIONES").Trim().ToUpperInvariant(),
            Settlement.IsEmpty("A-24HS").Trim().ToUpperInvariant(),
            Ticker.ThrowIfEmpty(nameof(Ticker)).Trim().ToUpperInvariant());
}

static class PpiExtensions
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromDays(1),
    ];

    public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

    public static PpiInstrumentKey ToNative(
        this PpiInstrument instrument,
        string defaultMarket,
        string defaultSettlement)
        => new(
            instrument.Market.IsEmpty(defaultMarket).IsEmpty("BYMA"),
            instrument.Type.IsEmpty("ACCIONES"),
            defaultSettlement.IsEmpty("A-24HS"),
            instrument.Ticker.ThrowIfEmpty(nameof(instrument.Ticker)));

    public static PpiInstrumentKey ToPpiNative(
        this SecurityId securityId,
        string defaultMarket,
        string defaultType,
        string defaultSettlement)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            var parts = native.Split('|');
            if (parts.Length == 4 && !parts[3].IsEmpty())
            {
                return new(
                    parts[0].IsEmpty(defaultMarket).IsEmpty("BYMA"),
                    parts[1].IsEmpty(defaultType).IsEmpty("ACCIONES"),
                    parts[2].IsEmpty(defaultSettlement).IsEmpty("A-24HS"),
                    parts[3]);
            }
        }

        return new(
            securityId.BoardCode.IsEmpty(defaultMarket).IsEmpty("BYMA"),
            defaultType.IsEmpty("ACCIONES"),
            defaultSettlement.IsEmpty("A-24HS"),
            securityId.SecurityCode.ThrowIfEmpty(
                nameof(securityId.SecurityCode)));
    }

    public static SecurityId ToSecurityId(this PpiInstrumentKey native)
        => new()
        {
            SecurityCode = native.Ticker,
            BoardCode = native.Market.ToBoardCode(),
            Native = native.ToString(),
        };

    public static SecurityMessage ToSecurityMessage(
        this PpiInstrument instrument,
        long originalTransactionId,
        string defaultMarket,
        string defaultSettlement)
    {
        var native = instrument.ToNative(defaultMarket, defaultSettlement);
        return new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = native.ToSecurityId(),
            Name = instrument.Description.IsEmpty(instrument.Ticker),
            ShortName = instrument.Ticker,
            Class = native.Type,
            SecurityType = native.Type.ToSecurityType(),
            Currency = instrument.Currency.ToCurrency(),
        };
    }

    public static string ToBoardCode(this string market)
        => market.IsEmpty("BYMA").Trim().ToUpperInvariant() switch
        {
            "MERVAL" or "BCBA" => "BYMA",
            "NASDAQ" => "NASDAQ",
            "NYSE" => "NYSE",
            "ROFEX" or "MATBA-ROFEX" or "MATBAROFEX" => "ROFEX",
            "OTC" => "OTC",
            var value => value,
        };

    public static SecurityTypes ToSecurityType(this string value)
        => Normalize(value) switch
        {
            "ETF" => SecurityTypes.Etf,
            "CEDEARS" or "CEDEAR" or "ADR" => SecurityTypes.Adr,
            "OPCIONES" or "OPCION" or "OPTIONS" or "OPTION" =>
                SecurityTypes.Option,
            "FUTUROS" or "FUTURO" or "FUTURES" or "FUTURE" =>
                SecurityTypes.Future,
            "BONOS" or "BONO" or "BONDS" or "BOND" or
                "LETRAS" or "LETRA" or "NOBAC" or "LEBAC" or
                "ON" or "OBLIGACIONES-NEGOCIABLES" =>
                SecurityTypes.Bond,
            "FCI" or "FCI-EXTERIOR" or "FONDOS" or "FUND" =>
                SecurityTypes.Fund,
            "CAUCIONES" or "CAUCION" or "REPO" =>
                SecurityTypes.Repo,
            _ => SecurityTypes.Stock,
        };

    public static string ToNativeType(this SecurityTypes value)
        => value switch
        {
            SecurityTypes.Stock => "ACCIONES",
            SecurityTypes.Etf => "ETF",
            SecurityTypes.Adr => "CEDEARS",
            SecurityTypes.Option => "OPCIONES",
            SecurityTypes.Future => "FUTUROS",
            SecurityTypes.Bond => "BONOS",
            SecurityTypes.Fund => "FCI",
            SecurityTypes.Repo => "CAUCIONES",
            _ => null,
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Normalize(value) switch
        {
            "ARS" or "PESO" or "PESOS" or "$" => CurrencyTypes.ARS,
            "USD" or "DOLAR" or "DOLARES" or "U$S" or "US$" =>
                CurrencyTypes.USD,
            "EUR" or "EURO" or "EUROS" => CurrencyTypes.EUR,
            _ => null,
        };

    public static string ToNative(this PpiQuantityTypes value)
        => value switch
        {
            PpiQuantityTypes.Money => "DINERO",
            PpiQuantityTypes.Total => "CANTIDAD-TOTAL",
            _ => "PAPELES",
        };

    public static string ToNative(this PpiOperationTerms value)
        => value switch
        {
            PpiOperationTerms.Day => "POR-EL-DÍA",
            PpiOperationTerms.UntilDate => "VÁLIDA-HASTA-EL",
            PpiOperationTerms.SeventyTwoHours => "72-HS",
            _ => "HASTA-SU-EJECUCIÓN",
        };

    public static string ToNativeOperation(this Sides side)
        => side == Sides.Sell ? "VENTA" : "COMPRA";

    public static Sides ToSide(this string value)
    {
        var normalized = Normalize(value);
        return normalized.Contains("VENTA", StringComparison.Ordinal) ||
            normalized.Contains("SELL", StringComparison.Ordinal) ||
            normalized.Contains("STOP", StringComparison.Ordinal)
                ? Sides.Sell
                : Sides.Buy;
    }

    public static string ToNativeOperationType(this OrderTypes value)
        => value == OrderTypes.Market
            ? "PRECIO-DE-MERCADO"
            : "PRECIO-LIMITE";

    public static OrderTypes ToOrderType(this string value)
        => Normalize(value).Contains("MERCADO", StringComparison.Ordinal) ||
            Normalize(value).Contains("MARKET", StringComparison.Ordinal)
                ? OrderTypes.Market
                : OrderTypes.Limit;

    public static OrderStates ToOrderState(this string value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("RECHAZ", StringComparison.Ordinal) ||
            normalized.Contains("REJECT", StringComparison.Ordinal) ||
            normalized.Contains("ERROR", StringComparison.Ordinal) ||
            normalized.Contains("FAILED", StringComparison.Ordinal))
        {
            return OrderStates.Failed;
        }
        if (normalized.Contains("PARCIAL", StringComparison.Ordinal) ||
            normalized.Contains("PARTIAL", StringComparison.Ordinal))
        {
            return OrderStates.Active;
        }
        if (normalized.Contains("EJECUT", StringComparison.Ordinal) ||
            normalized.Contains("FILLED", StringComparison.Ordinal) ||
            normalized.Contains("COMPLET", StringComparison.Ordinal) ||
            normalized.Contains("CANCEL", StringComparison.Ordinal) ||
            normalized.Contains("ANUL", StringComparison.Ordinal) ||
            normalized.Contains("VENCID", StringComparison.Ordinal) ||
            normalized.Contains("EXPIRED", StringComparison.Ordinal))
        {
            return OrderStates.Done;
        }
        if (normalized.Contains("ACTIV", StringComparison.Ordinal) ||
            normalized.Contains("INGRES", StringComparison.Ordinal) ||
            normalized.Contains("PEND", StringComparison.Ordinal) ||
            normalized.Contains("PROCESS", StringComparison.Ordinal))
        {
            return OrderStates.Active;
        }
        return OrderStates.Pending;
    }

    public static DateTime ToUtc(this DateTimeOffset value, DateTime fallback)
        => value == default ? fallback : value.UtcDateTime;

    public static decimal? Positive(this decimal value)
        => value > 0 ? value : null;

    public static decimal? ParsePercent(this string value)
    {
        if (value.IsEmpty())
            return null;
        var normalized = value.Trim().TrimEnd('%').Replace(',', '.');
        return decimal.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
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
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
