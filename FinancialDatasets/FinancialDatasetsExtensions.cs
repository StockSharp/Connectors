namespace StockSharp.FinancialDatasets;

static class FinancialDatasetsExtensions
{
    public const string DefaultBoard = "FINANCIALDATASETS";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
        TimeSpan.FromTicks(TimeHelper.TicksPerYear),
    ];

    public static string GetTicker(this SecurityId securityId)
    {
        var ticker = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim()
            .ToUpperInvariant();
        if (ticker.IsEmpty())
        {
            throw new InvalidOperationException(
                "Financial Datasets security identifier requires a ticker.");
        }
        if (ticker.Contains(','))
        {
            throw new InvalidOperationException(
                "Financial Datasets subscriptions require one ticker.");
        }
        return ticker;
    }

    public static SecurityId Normalize(
        this SecurityId securityId,
        string ticker)
        => new()
        {
            SecurityCode = ticker,
            BoardCode = securityId.BoardCode
                .IsEmpty(DefaultBoard),
            Native = ticker,
        };

    public static SecurityMessage ToSecurityMessage(
        this FinancialDatasetsFacts facts,
        long originalTransactionId)
    {
        var ticker = facts.Ticker?
            .Trim()
            .ToUpperInvariant()
            .ThrowIfEmpty(nameof(facts.Ticker));
        DateTime? listingDate = null;
        if (TryParseUtc(facts.ListingDate, out var parsed))
            listingDate = parsed;

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = facts.Name.IsEmpty(ticker),
            ShortName = facts.Name.IsEmpty(ticker),
            Class = facts.Industry
                .IsEmpty(facts.SicIndustry)
                .IsEmpty(facts.Sector),
            SecurityType = facts.Category.ToSecurityType(),
            IssueDate = listingDate,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityMessage ToSecurityMessage(
        this string ticker,
        long originalTransactionId)
    {
        ticker = ticker?
            .Trim()
            .ToUpperInvariant()
            .ThrowIfEmpty(nameof(ticker));
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = ticker,
            ShortName = ticker,
            SecurityType = SecurityTypes.Stock,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(this string category)
    {
        category = category?.Trim().ToLowerInvariant();
        if (category?.Contains("etf") == true ||
            category?.Contains("exchange traded fund") == true)
        {
            return SecurityTypes.Etf;
        }
        if (category?.Contains("fund") == true)
            return SecurityTypes.Fund;
        return SecurityTypes.Stock;
    }

    public static (string Interval, TimeSpan TimeFrame)
        ToFinancialDatasetsInterval(this TimeSpan timeFrame)
        => timeFrame switch
        {
            var value when value == TimeSpan.FromDays(1)
                => ("day", value),
            var value when value == TimeSpan.FromDays(7)
                => ("week", value),
            var value when value.Ticks == TimeHelper.TicksPerMonth
                => ("month", value),
            var value when value.Ticks == TimeHelper.TicksPerYear
                => ("year", value),
            _ => throw new NotSupportedException(
                $"Financial Datasets does not support {timeFrame} candles."),
        };

    public static DateTime GetCloseTime(
        this DateTime openTime,
        TimeSpan timeFrame)
    {
        if (timeFrame.Ticks == TimeHelper.TicksPerMonth)
            return openTime.AddMonths(1);
        if (timeFrame.Ticks == TimeHelper.TicksPerYear)
            return openTime.AddYears(1);
        return openTime.Add(timeFrame);
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

    public static DateTime EstimateFrom(
        DateTime to,
        TimeSpan timeFrame,
        long? count)
    {
        if (count is not > 0)
            return to.AddYears(-3);
        var requested = Math.Min(count.Value, 1_000_000);
        if (timeFrame.Ticks == TimeHelper.TicksPerMonth)
        {
            return to.AddMonths(
                checked(-(int)Math.Min(requested * 2, 1200)));
        }
        if (timeFrame.Ticks == TimeHelper.TicksPerYear)
        {
            return to.AddYears(
                checked(-(int)Math.Min(requested * 2, 100)));
        }
        var ticks = Math.Min(
            checked(timeFrame.Ticks * requested * 3),
            TimeSpan.FromDays(365 * 100).Ticks);
        return to.Subtract(TimeSpan.FromTicks(ticks));
    }

    public static string ToApiValue(
        this FinancialDatasetsPeriods period)
        => period switch
        {
            FinancialDatasetsPeriods.Annual => "annual",
            FinancialDatasetsPeriods.Quarterly => "quarterly",
            FinancialDatasetsPeriods.Ttm => "ttm",
            _ => throw new ArgumentOutOfRangeException(
                nameof(period), period, null),
        };

    public static string ToResource(
        this FinancialDatasetsDataKinds kind)
        => kind switch
        {
            FinancialDatasetsDataKinds.CompanyFacts =>
                "company/facts",
            FinancialDatasetsDataKinds.FinancialStatements =>
                "financials",
            FinancialDatasetsDataKinds.FinancialMetrics =>
                "financial-metrics",
            FinancialDatasetsDataKinds.SecFilings => "filings",
            FinancialDatasetsDataKinds.Earnings => "earnings",
            FinancialDatasetsDataKinds.InsiderTrades =>
                "insider-trades",
            FinancialDatasetsDataKinds.InsiderOwnership =>
                "insider-ownership",
            FinancialDatasetsDataKinds.BeneficialOwnership =>
                "beneficial-ownership",
            FinancialDatasetsDataKinds.ActivistOwnership =>
                "activist-ownership",
            FinancialDatasetsDataKinds.InstitutionalHoldings =>
                "institutional-holdings",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };
}
