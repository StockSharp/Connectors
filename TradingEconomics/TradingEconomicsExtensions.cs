namespace StockSharp.TradingEconomics;

static class TradingEconomicsExtensions
{
    public const string DefaultBoard = "TRADINGECONOMICS";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
    ];

    public static string GetSymbol(
        this SecurityId securityId,
        string defaultMarket)
    {
        var symbol = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim()
            .ToUpperInvariant();
        if (symbol.IsEmpty())
        {
            throw new InvalidOperationException(
                "Trading Economics security identifier requires a symbol.");
        }
        if (symbol.Contains(','))
        {
            throw new InvalidOperationException(
                "Trading Economics subscriptions require one symbol.");
        }
        if (!symbol.Contains(':'))
        {
            defaultMarket = defaultMarket?
                .Trim()
                .ToUpperInvariant();
            if (defaultMarket.IsEmpty())
            {
                throw new InvalidOperationException(
                    "A Trading Economics market suffix is required for a bare ticker.");
            }
            symbol = $"{symbol}:{defaultMarket}";
        }
        return symbol;
    }

    public static SecurityId Normalize(
        this SecurityId securityId,
        string symbol)
        => new()
        {
            SecurityCode = GetTicker(symbol),
            BoardCode = securityId.BoardCode
                .IsEmpty(DefaultBoard),
            Native = NormalizeSymbol(symbol),
            Isin = securityId.Isin,
        };

    public static SecurityMessage ToSecurityMessage(
        this TradingEconomicsMarket market,
        long originalTransactionId)
    {
        var symbol = NormalizeSymbol(market.Symbol);
        var ticker = market.Ticker?
            .Trim()
            .ToUpperInvariant()
            .IsEmpty(GetTicker(symbol));
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = symbol,
                Isin = market.Isin?.Trim(),
            },
            Name = market.Name.IsEmpty(ticker),
            ShortName = market.Name.IsEmpty(ticker),
            Class = market.Country,
            SecurityType = market.Type.ToSecurityType(),
            PriceStep = market.Decimals.ToPriceStep(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(this string type)
        => type?.Trim().ToLowerInvariant() switch
        {
            "index" or "indexes" => SecurityTypes.Index,
            "fund" or "funds" => SecurityTypes.Fund,
            "etf" or "etfs" => SecurityTypes.Etf,
            _ => SecurityTypes.Stock,
        };

    public static (string Interval, bool IsDaily)
        ToTradingEconomicsInterval(this TimeSpan timeFrame)
        => timeFrame switch
        {
            var value when value == TimeSpan.FromMinutes(1)
                => ("1m", false),
            var value when value == TimeSpan.FromMinutes(5)
                => ("5m", false),
            var value when value == TimeSpan.FromMinutes(10)
                => ("10m", false),
            var value when value == TimeSpan.FromMinutes(15)
                => ("15m", false),
            var value when value == TimeSpan.FromMinutes(30)
                => ("30m", false),
            var value when value == TimeSpan.FromHours(1)
                => ("1h", false),
            var value when value == TimeSpan.FromHours(2)
                => ("2h", false),
            var value when value == TimeSpan.FromHours(4)
                => ("4h", false),
            var value when value == TimeSpan.FromDays(1)
                => ("1d", true),
            _ => throw new NotSupportedException(
                $"Trading Economics does not support {timeFrame} candles."),
        };

    public static bool TryParseUtc(
        string value,
        bool daily,
        out DateTime result)
    {
        result = default;
        if (value.IsEmpty())
            return false;
        if (daily && DateTime.TryParseExact(
            value.Trim(),
            ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            result = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            return true;
        }
        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return false;
        }
        result = parsed.UtcDateTime;
        return true;
    }

    public static DateTime ToUtcSafe(this DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static DateTime EstimateFrom(
        DateTime to,
        TimeSpan timeFrame,
        long? count)
    {
        if (count is not > 0)
            return timeFrame >= TimeSpan.FromDays(1)
                ? to.AddYears(-3)
                : to.AddDays(-7);
        var requested = Math.Min(count.Value, 1_000_000);
        var factor = timeFrame >= TimeSpan.FromDays(1) ? 3L : 2L;
        var ticks = Math.Min(
            checked(timeFrame.Ticks * requested * factor),
            TimeSpan.FromDays(365 * 100).Ticks);
        return to.Subtract(TimeSpan.FromTicks(ticks));
    }

    public static string NormalizeSymbol(string symbol)
    {
        symbol = symbol?
            .Trim()
            .ToUpperInvariant();
        if (symbol.IsEmpty())
            throw new ArgumentNullException(nameof(symbol));
        if (symbol.Contains(','))
        {
            throw new ArgumentException(
                "A single Trading Economics symbol is required.",
                nameof(symbol));
        }
        return symbol;
    }

    public static string GetTicker(string symbol)
    {
        symbol = NormalizeSymbol(symbol);
        var separator = symbol.IndexOf(':');
        return separator > 0
            ? symbol[..separator]
            : symbol;
    }

    private static decimal? ToPriceStep(this int? decimals)
    {
        if (decimals is not >= 0 or > 12)
            return null;
        var step = 1m;
        for (var index = 0; index < decimals.Value; index++)
            step /= 10m;
        return step;
    }
}
