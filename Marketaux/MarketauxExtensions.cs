namespace StockSharp.Marketaux;

static class MarketauxExtensions
{
    public const string DefaultBoard = "MARKETAUX";

    public static string GetOptionalTicker(
        this SecurityId securityId)
    {
        var ticker = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode);
        return ticker.IsEmpty()
            ? null
            : ValidateTicker(ticker);
    }

    public static string ValidateTicker(string ticker)
    {
        ticker = ticker?.Trim().ToUpperInvariant();
        if (ticker.IsEmpty())
        {
            throw new InvalidOperationException(
                "Marketaux security identifier requires a symbol.");
        }
        if (ticker.Length > 64 ||
            ticker.Any(character =>
                char.IsControl(character) ||
                character is ',' or '?' or '#' or '&'))
        {
            throw new InvalidOperationException(
                "Marketaux entity symbol is invalid.");
        }
        return ticker;
    }

    public static string NormalizeCsv(
        string value,
        string settingName)
    {
        if (value.IsEmpty())
            return null;
        var values = value
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                if (item.Length > 128 ||
                    item.Any(character =>
                        char.IsControl(character) ||
                        character is '?' or '#' or '&'))
                {
                    throw new InvalidOperationException(
                        $"Marketaux {settingName} contains an invalid value.");
                }
                return item;
            })
            .Where(item => !item.IsEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0
            ? null
            : string.Join(",", values);
    }

    public static SecurityId Normalize(
        this SecurityId securityId,
        string ticker)
        => new()
        {
            SecurityCode = ValidateTicker(ticker),
            BoardCode = securityId.BoardCode
                .IsEmpty(DefaultBoard),
            Native = securityId.Native ?? ticker,
        };

    public static SecurityMessage ToSecurityMessage(
        this MarketauxEntity entity,
        long originalTransactionId)
    {
        var ticker = ValidateTicker(entity.Symbol);
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = entity.Name.IsEmpty(ticker),
            ShortName = entity.Name.IsEmpty(ticker),
            Class = entity.Industry
                .IsEmpty(entity.ExchangeLong)
                .IsEmpty(entity.Exchange)
                .IsEmpty(entity.Country),
            SecurityType = entity.Type.ToSecurityType(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(
        this string entityType)
        => entityType?.Trim().ToLowerInvariant() switch
        {
            "etf" => SecurityTypes.Etf,
            "mutualfund" or "mutual_fund" =>
                SecurityTypes.Fund,
            "index" => SecurityTypes.Index,
            _ => SecurityTypes.Stock,
        };

    public static string ToApiValue(
        this MarketauxIntervals interval)
        => interval switch
        {
            MarketauxIntervals.Minute => "minute",
            MarketauxIntervals.Hour => "hour",
            MarketauxIntervals.Day => "day",
            MarketauxIntervals.Week => "week",
            MarketauxIntervals.Month => "month",
            MarketauxIntervals.Quarter => "quarter",
            MarketauxIntervals.Year => "year",
            _ => throw new ArgumentOutOfRangeException(
                nameof(interval), interval, null),
        };

    public static bool TryParseUtc(
        string value,
        out DateTime result)
    {
        result = default;
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

    public static string ToResource(
        this MarketauxDataKinds kind)
        => kind switch
        {
            MarketauxDataKinds.NewsAnalysis => "v1/news/all",
            MarketauxDataKinds.SentimentTimeSeries =>
                "v1/entity/stats/intraday",
            MarketauxDataKinds.SentimentAggregation =>
                "v1/entity/stats/aggregation",
            MarketauxDataKinds.TrendingEntities =>
                "v1/entity/trending/aggregation",
            MarketauxDataKinds.EntityTypes =>
                "v1/entity/type/list",
            MarketauxDataKinds.Industries =>
                "v1/entity/industry/list",
            MarketauxDataKinds.NewsSources =>
                "v1/news/sources",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };
}
