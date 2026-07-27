namespace StockSharp.UnusualWhales;

static class UnusualWhalesExtensions
{
    public const string DefaultBoard = "UNUSUALWHALES";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
    ];

    public static string GetTicker(this SecurityId securityId)
        => ValidateTicker(
            (securityId.Native as string)
                .IsEmpty(securityId.SecurityCode));

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
                "Unusual Whales security identifier requires a ticker.");
        }
        if (ticker.Length > 32 ||
            ticker.Any(character =>
                char.IsControl(character) ||
                character is '/' or '?' or '#' or ',' or '&'))
        {
            throw new InvalidOperationException(
                "Unusual Whales ticker is invalid.");
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
        this UnusualWhalesListing listing,
        long originalTransactionId)
    {
        var ticker = ValidateTicker(listing.Ticker);
        DateTime? issueDate = null;
        if (TryParseUtc(listing.IpoDate, out var parsed))
            issueDate = parsed;
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = listing.Name.IsEmpty(ticker),
            ShortName = listing.Name.IsEmpty(ticker),
            Class = listing.Exchange
                .IsEmpty(listing.AssetType),
            SecurityType =
                listing.AssetType.ToSecurityType(),
            IssueDate = issueDate,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityMessage ToSecurityMessage(
        this UnusualWhalesCompanyProfile profile,
        long originalTransactionId)
    {
        var ticker = ValidateTicker(profile.Ticker);
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = ticker,
            },
            Name = profile.Name.IsEmpty(ticker),
            ShortName = profile.Name.IsEmpty(ticker),
            Class = profile.Industry
                .IsEmpty(profile.Sector)
                .IsEmpty(profile.Exchange),
            SecurityType =
                profile.AssetType.ToSecurityType(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(
        this string assetType)
    {
        assetType = assetType?.Trim().ToLowerInvariant();
        if (assetType?.Contains("etf") == true ||
            assetType?.Contains("exchange traded") == true)
        {
            return SecurityTypes.Etf;
        }
        if (assetType?.Contains("fund") == true)
            return SecurityTypes.Fund;
        return SecurityTypes.Stock;
    }

    public static string ToCandleSize(this TimeSpan timeFrame)
        => timeFrame switch
        {
            var value when value == TimeSpan.FromMinutes(1) => "1m",
            var value when value == TimeSpan.FromMinutes(5) => "5m",
            var value when value == TimeSpan.FromMinutes(10) => "10m",
            var value when value == TimeSpan.FromMinutes(15) => "15m",
            var value when value == TimeSpan.FromMinutes(30) => "30m",
            var value when value == TimeSpan.FromHours(1) => "1h",
            var value when value == TimeSpan.FromHours(4) => "4h",
            var value when value == TimeSpan.FromDays(1) => "1d",
            var value when value == TimeSpan.FromDays(7) => "1w",
            _ => throw new NotSupportedException(
                $"Unusual Whales does not support {timeFrame} candles."),
        };

    public static DateTime GetCloseTime(
        this DateTime openTime,
        TimeSpan timeFrame)
        => openTime.Add(timeFrame);

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
        var requested = Math.Min(
            Math.Max(count ?? 500, 1),
            2500);
        var ticks = Math.Min(
            checked(timeFrame.Ticks * requested * 3),
            TimeSpan.FromDays(3650).Ticks);
        return to.Subtract(TimeSpan.FromTicks(ticks));
    }

    public static string ToApiTimeframe(
        DateTime from,
        DateTime to)
    {
        var days = Math.Max(
            1,
            (int)Math.Ceiling((to - from).TotalDays));
        return $"{days.ToString(CultureInfo.InvariantCulture)}D";
    }

    public static string ToResource(
        this UnusualWhalesDataKinds kind,
        string ticker)
    {
        var escaped = ticker.IsEmpty()
            ? null
            : Uri.EscapeDataString(ValidateTicker(ticker));
        return kind switch
        {
            UnusualWhalesDataKinds.CompanyProfile =>
                $"api/companies/{Require(escaped, kind)}/profile",
            UnusualWhalesDataKinds.StockState =>
                $"api/stock/{Require(escaped, kind)}/stock-state",
            UnusualWhalesDataKinds.OptionsFlowAlerts =>
                "api/option-trades/flow-alerts",
            UnusualWhalesDataKinds.RecentOptionsFlow =>
                $"api/stock/{Require(escaped, kind)}/flow-recent",
            UnusualWhalesDataKinds.DarkPoolTrades =>
                $"api/darkpool/{Require(escaped, kind)}",
            UnusualWhalesDataKinds.InterpolatedIv =>
                $"api/stock/{Require(escaped, kind)}/interpolated-iv",
            UnusualWhalesDataKinds.VolatilityStats =>
                $"api/stock/{Require(escaped, kind)}/volatility/stats",
            UnusualWhalesDataKinds.GreekExposure =>
                $"api/stock/{Require(escaped, kind)}/greek-exposure",
            UnusualWhalesDataKinds.OptionsVolume =>
                $"api/stock/{Require(escaped, kind)}/options-volume",
            UnusualWhalesDataKinds.InsiderTransactions =>
                "api/insider/transactions",
            UnusualWhalesDataKinds.CongressTrades =>
                "api/congress/recent-trades",
            UnusualWhalesDataKinds.MarketTide =>
                "api/market/market-tide",
            UnusualWhalesDataKinds.MarketMovers =>
                "api/market/movers",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };
    }

    private static string Require(
        string ticker,
        UnusualWhalesDataKinds kind)
        => ticker.IsEmpty()
            ? throw new InvalidOperationException(
                $"Unusual Whales {kind} requires a ticker.")
            : ticker;
}
