namespace StockSharp.Bavest;

static class BavestExtensions
{
    public const string DefaultBoard = "BAVEST";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
    ];

    public static string GetOptionalTicker(
        this SecurityId securityId)
    {
        var ticker = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode);
        return ticker.IsEmpty()
            ? null
            : ValidateTicker(ticker);
    }

    public static string GetTicker(this SecurityId securityId)
        => ValidateTicker(
            (securityId.Native as string)
                .IsEmpty(securityId.SecurityCode));

    public static string ValidateTicker(string ticker)
    {
        ticker = ticker?.Trim().ToUpperInvariant();
        if (ticker.IsEmpty())
        {
            throw new InvalidOperationException(
                "Bavest security identifier requires a symbol.");
        }
        if (ticker.Length > 64 ||
            ticker.Any(character =>
                char.IsControl(character) ||
                character is ',' or '?' or '#' or '&'))
        {
            throw new InvalidOperationException(
                "Bavest symbol is invalid.");
        }
        return ticker;
    }

    public static string NormalizeOptionalCode(
        string value,
        string settingName)
    {
        if (value.IsEmpty())
            return null;
        value = value.Trim().ToUpperInvariant();
        if (value.Length > 32 ||
            value.Any(character =>
                char.IsControl(character) ||
                character is ',' or '?' or '#' or '&'))
        {
            throw new InvalidOperationException(
                $"Bavest {settingName} is invalid.");
        }
        return value;
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
        this BavestSecurity security,
        long originalTransactionId,
        SecurityTypes fallbackType)
    {
        var ticker = ValidateTicker(security.Symbol);
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = security.Name
                .IsEmpty(security.CompanyName)
                .IsEmpty(ticker),
            ShortName = security.Name
                .IsEmpty(security.CompanyName)
                .IsEmpty(ticker),
            Class = security.Sector
                .IsEmpty(security.ExchangeCode)
                .IsEmpty(security.Exchange)
                .IsEmpty(security.Country),
            SecurityType =
                security.ToSecurityType(fallbackType),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(
        this BavestSecurity security,
        SecurityTypes fallbackType)
    {
        if (security.IsEtf == true ||
            security.Type?.Equals(
                "etf",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return SecurityTypes.Etf;
        }
        if (security.IsFund == true ||
            security.Type?.Contains(
                "fund",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return SecurityTypes.Fund;
        }
        if (security.Type?.Equals(
            "index",
            StringComparison.OrdinalIgnoreCase) == true)
        {
            return SecurityTypes.Index;
        }
        return fallbackType;
    }

    public static string ToResolution(this TimeSpan timeFrame)
        => timeFrame switch
        {
            var value when value == TimeSpan.FromDays(1) => "D",
            var value when value == TimeSpan.FromDays(7) => "W",
            var value when
                value.Ticks == TimeHelper.TicksPerMonth => "M",
            _ => throw new NotSupportedException(
                $"Bavest v2 does not support {timeFrame} candles."),
        };

    public static DateTime GetCloseTime(
        this DateTime openTime,
        TimeSpan timeFrame)
        => timeFrame.Ticks == TimeHelper.TicksPerMonth
            ? openTime.AddMonths(1)
            : openTime.Add(timeFrame);

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

    public static bool TryUnixTime(
        long? value,
        out DateTime result)
    {
        result = default;
        if (value is not > 0)
            return false;
        try
        {
            var parsed = value > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(
                    value.Value)
                : DateTimeOffset.FromUnixTimeSeconds(
                    value.Value);
            result = parsed.UtcDateTime;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
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
        var requested = Math.Min(
            Math.Max(count ?? 500, 1),
            10000);
        if (timeFrame.Ticks == TimeHelper.TicksPerMonth)
        {
            return to.AddMonths(
                checked(-(int)Math.Min(requested * 2, 1200)));
        }
        var ticks = Math.Min(
            checked(timeFrame.Ticks * requested * 3),
            TimeSpan.FromDays(36500).Ticks);
        return to.Subtract(TimeSpan.FromTicks(ticks));
    }

    public static string ToApiValue(
        this BavestFinancialFrequencies frequency)
        => frequency switch
        {
            BavestFinancialFrequencies.Annual => "annual",
            BavestFinancialFrequencies.Quarterly => "quarterly",
            _ => throw new ArgumentOutOfRangeException(
                nameof(frequency), frequency, null),
        };

    public static string ToResource(this BavestDataKinds kind)
        => kind switch
        {
            BavestDataKinds.CompanyProfile =>
                "v2/equities/profile",
            BavestDataKinds.EquityMetrics =>
                "v2/equities/metrics",
            BavestDataKinds.IncomeStatements =>
                "v2/equities/income-statement",
            BavestDataKinds.BalanceSheets =>
                "v2/equities/balance-sheets",
            BavestDataKinds.CashFlows =>
                "v2/equities/cash-flow",
            BavestDataKinds.FinancialsTtm =>
                "v2/equities/financials-ttm",
            BavestDataKinds.AnalystConsensus =>
                "v2/estimates/consensus",
            BavestDataKinds.AnalystRecommendations =>
                "v2/estimates/recommendations",
            BavestDataKinds.PriceTarget =>
                "v2/estimates/price-target",
            BavestDataKinds.UpgradesDowngrades =>
                "v2/estimates/upgrades-downgrades",
            BavestDataKinds.DividendHistory =>
                "v2/dividends/history",
            BavestDataKinds.EtfProfile =>
                "v2/etfs/profile",
            BavestDataKinds.EtfMetrics =>
                "v2/etfs/metrics",
            BavestDataKinds.Screener =>
                "v2/reference/screener",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    public static bool RequiresTicker(
        this BavestDataKinds kind)
        => kind != BavestDataKinds.Screener;
}
