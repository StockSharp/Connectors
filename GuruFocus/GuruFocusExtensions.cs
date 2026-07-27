namespace StockSharp.GuruFocus;

static class GuruFocusExtensions
{
    public const string DefaultBoard = "GURUFOCUS";

    private static readonly HashSet<string> _regionCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "U",
            "A",
            "E",
            "B",
            "C",
            "O",
            "F",
            "S",
            "I",
        };

    private static readonly HashSet<string> _guruActions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "buy",
            "sell",
            "add",
            "reduce",
        };

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromDays(1),
    ];

    public static string NormalizeRegionCode(string value)
    {
        value = value?.Trim().ToUpperInvariant();
        if (!_regionCodes.Contains(value))
        {
            throw new InvalidOperationException(
                "GuruFocus region code must be U, A, E, B, C, O, F, S, or I.");
        }
        return value;
    }

    public static string NormalizeGuruActions(string value)
    {
        if (value.IsEmpty())
            return null;

        var normalized = new List<string>();
        foreach (var item in value.Split(','))
        {
            var action = item.Trim().ToLowerInvariant();
            if (!_guruActions.Contains(action))
            {
                throw new InvalidOperationException(
                    $"GuruFocus guru-trade action '{action}' is invalid.");
            }
            if (!normalized.Contains(
                action, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(action);
            }
        }
        return normalized.Count == 0
            ? null
            : string.Join(",", normalized);
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
                "GuruFocus security identifier requires a ticker.");
        }
        if (ticker.Length > 64 ||
            ticker.Any(character =>
                char.IsControl(character) ||
                character is '/' or '?' or '#' or ','))
        {
            throw new InvalidOperationException(
                "GuruFocus ticker is invalid.");
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
        this GuruFocusSecurity security,
        SecurityTypes type,
        long originalTransactionId)
    {
        var ticker = ValidateTicker(security.Symbol);
        DateTime? issueDate = null;
        var profile = security as GuruFocusProfileGeneral;
        if (TryParseUtc(profile?.IpoDate, out var parsed))
            issueDate = parsed;

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = security.StockId.IsEmpty(ticker),
            },
            Name = security.Company.IsEmpty(ticker),
            ShortName = security.Company.IsEmpty(ticker),
            Class = security.Exchange
                .IsEmpty(profile?.Industry),
            SecurityType = type,
            IssueDate = issueDate,
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

    public static bool TryParseDisplayTime(
        string value,
        out DateTime result)
    {
        if (TryParseUtc(value, out result))
            return true;

        result = default;
        value = value?.Trim();
        if (value.IsEmpty())
            return false;
        var split = value.LastIndexOf(' ');
        if (split <= 0)
            return false;
        var offset = value[(split + 1)..].ToUpperInvariant() switch
        {
            "EST" => TimeSpan.FromHours(-5),
            "EDT" => TimeSpan.FromHours(-4),
            "CST" => TimeSpan.FromHours(-6),
            "CDT" => TimeSpan.FromHours(-5),
            "MST" => TimeSpan.FromHours(-7),
            "MDT" => TimeSpan.FromHours(-6),
            "PST" => TimeSpan.FromHours(-8),
            "PDT" => TimeSpan.FromHours(-7),
            "GMT" or "UTC" => TimeSpan.Zero,
            "CET" => TimeSpan.FromHours(1),
            "CEST" => TimeSpan.FromHours(2),
            "HKT" => TimeSpan.FromHours(8),
            "JST" => TimeSpan.FromHours(9),
            "AEST" => TimeSpan.FromHours(10),
            "AEDT" => TimeSpan.FromHours(11),
            _ => (TimeSpan?)null,
        };
        if (offset is null ||
            !DateTime.TryParseExact(
                value[..split],
                ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var local))
        {
            return false;
        }
        result = new DateTimeOffset(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            offset.Value).UtcDateTime;
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
        long? count)
    {
        if (count is not > 0)
            return to.AddYears(-3);
        var requested = Math.Min(count.Value, 365L * 50);
        return to.AddDays(
            -Math.Min(
                requested * 2,
                365L * 100));
    }

    public static string FormatDate(DateTime value)
        => value.ToUtcSafe().ToString(
            "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool IsPageComplete<T>(
        this GuruFocusPage<T> page,
        int requestedPage,
        int pageSize)
    {
        var count = page?.Data?.Length ?? 0;
        if (count < pageSize)
            return true;
        return page?.Total is > 0 &&
            checked((long)requestedPage * pageSize) >=
                page.Total.Value;
    }

    public static string ToResource(
        this GuruFocusDataKinds kind,
        string ticker)
    {
        ticker = Uri.EscapeDataString(ValidateTicker(ticker));
        return kind switch
        {
            GuruFocusDataKinds.Profile =>
                $"stocks/{ticker}/profile",
            GuruFocusDataKinds.Fundamentals =>
                $"stocks/{ticker}/fundamental",
            GuruFocusDataKinds.Valuations =>
                $"stocks/{ticker}/valuations",
            GuruFocusDataKinds.Rankings =>
                $"stocks/{ticker}/rankings",
            GuruFocusDataKinds.EtfData =>
                $"etf/{ticker}",
            GuruFocusDataKinds.SecFilings =>
                $"stocks/{ticker}/filings",
            GuruFocusDataKinds.InsiderTrades =>
                $"stocks/{ticker}/insider-trades",
            GuruFocusDataKinds.GuruTrades =>
                $"stocks/{ticker}/guru-trades",
            GuruFocusDataKinds.GuruHoldings =>
                $"stocks/{ticker}/guru-holdings",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };
    }
}
