namespace StockSharp.QuiverQuant;

static class QuiverQuantExtensions
{
    public const string DefaultBoard = "QUIVER";

    public static string NormalizeCycle(string value)
    {
        if (value.IsEmpty())
            return null;
        value = value.Trim();
        if (value.Length != 4 ||
            !value.All(char.IsDigit) ||
            !int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var year) ||
            year is < 1900 or > 2200)
        {
            throw new InvalidOperationException(
                "Quiver Quantitative donor election cycle must be a four-digit year.");
        }
        return value;
    }

    public static string GetTicker(this SecurityId securityId)
        => ValidateTicker(
            securityId.SecurityCode
                .IsEmpty(securityId.Native as string));

    public static string ValidateTicker(string ticker)
    {
        ticker = ticker?.Trim().ToUpperInvariant();
        if (ticker.IsEmpty())
        {
            throw new InvalidOperationException(
                "Quiver Quantitative security identifier requires a ticker.");
        }
        if (ticker.Length > 32 ||
            ticker.Any(character =>
                char.IsControl(character) ||
                character is '/' or '?' or '#' or ','))
        {
            throw new InvalidOperationException(
                "Quiver Quantitative ticker is invalid.");
        }
        return ticker;
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
        this QuiverQuantCompany company,
        long originalTransactionId)
    {
        var ticker = ValidateTicker(company.Ticker);
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = company.Name.IsEmpty(ticker),
            ShortName = company.Name.IsEmpty(ticker),
            SecurityType = SecurityTypes.Stock,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

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

    public static string FormatCompactDate(DateTime value)
        => value.ToUtcSafe().ToString(
            "yyyyMMdd", CultureInfo.InvariantCulture);

    public static string ToResource(
        this QuiverQuantDataKinds kind,
        string ticker)
    {
        ticker = Uri.EscapeDataString(ValidateTicker(ticker));
        return kind switch
        {
            QuiverQuantDataKinds.CongressTrades =>
                $"beta/historical/congresstrading/{ticker}",
            QuiverQuantDataKinds.InsiderTrades =>
                "beta/live/insiders",
            QuiverQuantDataKinds.InstitutionalHoldings =>
                "beta/live/sec13f",
            QuiverQuantDataKinds.InstitutionalChanges =>
                "beta/live/sec13fchanges",
            QuiverQuantDataKinds.OffExchange =>
                $"beta/historical/offexchange/{ticker}",
            QuiverQuantDataKinds.GovernmentContracts =>
                $"beta/historical/govcontractsall/{ticker}",
            QuiverQuantDataKinds.Lobbying =>
                $"beta/historical/lobbying/{ticker}",
            QuiverQuantDataKinds.CorporateDonors =>
                $"beta/historical/corporatedonors/{ticker}",
            QuiverQuantDataKinds.Patents =>
                $"beta/historical/allpatents/{ticker}",
            QuiverQuantDataKinds.ExecutiveCompensation =>
                $"beta/historical/executivecompensation/{ticker}",
            QuiverQuantDataKinds.TopShareholders =>
                $"beta/live/topshareholders/{ticker}",
            QuiverQuantDataKinds.EarningsDistortionScores =>
                $"beta/live/earningsdistortionscores/{ticker}",
            QuiverQuantDataKinds.CnbcTrades =>
                "beta/live/cnbc",
            QuiverQuantDataKinds.PatentDrift =>
                "beta/live/patentdrift",
            QuiverQuantDataKinds.PatentMomentum =>
                "beta/live/patentmomentum",
            QuiverQuantDataKinds.EventsBeta =>
                $"beta/historical/eventsbeta/{ticker}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };
    }
}
