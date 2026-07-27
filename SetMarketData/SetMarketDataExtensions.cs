namespace StockSharp.SetMarketData;

static class SetMarketDataExtensions
{
    public const string IndexBoard = "SET-INDEX";

    public static string ToApiPath(
        this SetMarketDataModes mode)
        => mode switch
        {
            SetMarketDataModes.RealTime => "realtime-data",
            SetMarketDataModes.Delayed => "delay-data",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode), mode, null),
        };

    public static SecurityTypes ToSecurityType(
        this string value)
        => value?.ToUpperInvariant() switch
        {
            "ETF" => SecurityTypes.Etf,
            "DR" => SecurityTypes.Adr,
            "UT" => SecurityTypes.Fund,
            "W" or "TSR" or "DWC" or "DWP" =>
                SecurityTypes.Warrant,
            _ => SecurityTypes.Stock,
        };

    public static SecurityId ToSecurityId(
        this SetStockQuote quote)
        => new()
        {
            SecurityCode = quote.Symbol
                .ThrowIfEmpty(nameof(quote.Symbol))
                .Trim()
                .ToUpperInvariant(),
            BoardCode = BoardCodes.Set,
            Native = quote.Symbol.Trim().ToUpperInvariant(),
        };

    public static SecurityId ToSecurityId(
        this SetIndexQuote quote)
        => new()
        {
            SecurityCode = quote.Symbol
                .ThrowIfEmpty(nameof(quote.Symbol))
                .Trim()
                .ToUpperInvariant(),
            BoardCode = IndexBoard,
            Native = "INDEX:" +
                quote.Symbol.Trim().ToUpperInvariant(),
        };

    public static SecurityMessage ToSecurityMessage(
        this SetStockQuote quote,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = quote.ToSecurityId(),
            Name = quote.FullName.IsEmpty(quote.Symbol),
            ShortName = quote.Symbol,
            Class = string.Join(
                ":",
                new[] { quote.Market, quote.SecurityType }
                    .Where(value => !value.IsEmpty())),
            SecurityType = quote.SecurityType.ToSecurityType(),
            Currency = CurrencyTypes.THB,
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static SecurityMessage ToSecurityMessage(
        this SetIndexQuote quote,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = quote.ToSecurityId(),
            Name = quote.FullName.IsEmpty(quote.Symbol),
            ShortName = quote.Symbol,
            Class = "SET Index",
            SecurityType = SecurityTypes.Index,
            Currency = CurrencyTypes.THB,
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static bool Matches(
        this SetStockQuote quote,
        string value)
        => value.IsEmpty() ||
            quote.Symbol.ContainsIgnoreCase(value) ||
            quote.FullName.ContainsIgnoreCase(value);

    public static bool Matches(
        this SetIndexQuote quote,
        string value)
        => value.IsEmpty() ||
            quote.Symbol.ContainsIgnoreCase(value) ||
            quote.FullName.ContainsIgnoreCase(value);

    public static bool IsIndex(this SecurityId securityId)
        => securityId.BoardCode.EqualsIgnoreCase(IndexBoard) ||
            (securityId.Native as string)
                ?.StartsWith(
                    "INDEX:",
                    StringComparison.OrdinalIgnoreCase) == true;

    public static string GetSetSymbol(
        this SecurityId securityId)
    {
        var value = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();
        if (value?.StartsWith(
                "INDEX:",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            value = value[6..];
        }

        return value?.Trim().ToUpperInvariant();
    }

    public static DateTime GetServerTime(this string value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var time)
                ? time.UtcDateTime
                : DateTime.UtcNow;

    public static string NormalizeCsv(
        this string value)
        => value.IsEmpty()
            ? null
            : string.Join(
                ",",
                value
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .Select(item => item.ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
}
