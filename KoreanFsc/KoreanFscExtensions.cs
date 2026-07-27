namespace StockSharp.KoreanFsc;

static class KoreanFscExtensions
{
    private static readonly TimeSpan _koreaOffset =
        TimeSpan.FromHours(9);

    public static string ToEndpoint(
        this KoreanFscDataSets dataSet)
        => dataSet switch
        {
            KoreanFscDataSets.Stocks =>
                "getStockPriceInfo",
            KoreanFscDataSets.IncomeSecurities =>
                "getSecuritiesPriceInfo",
            KoreanFscDataSets.PreemptiveRightSecurities =>
                "getPreemptiveRightSecuritiesPriceInfo",
            KoreanFscDataSets.PreemptiveRightCertificates =>
                "getPreemptiveRightCertificatePriceInfo",
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static SecurityTypes ToSecurityType(
        this KoreanFscDataSets dataSet)
        => dataSet switch
        {
            KoreanFscDataSets.Stocks => SecurityTypes.Stock,
            KoreanFscDataSets.IncomeSecurities => SecurityTypes.Fund,
            KoreanFscDataSets.PreemptiveRightSecurities or
            KoreanFscDataSets.PreemptiveRightCertificates =>
                SecurityTypes.Warrant,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static string ToDisplayName(
        this KoreanFscDataSets dataSet)
        => dataSet switch
        {
            KoreanFscDataSets.Stocks => "Stocks",
            KoreanFscDataSets.IncomeSecurities =>
                "Income securities",
            KoreanFscDataSets.PreemptiveRightSecurities =>
                "Preemptive-right securities",
            KoreanFscDataSets.PreemptiveRightCertificates =>
                "Preemptive-right certificates",
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static bool SupportsMarketFilter(
        this KoreanFscDataSets dataSet)
        => dataSet switch
        {
            KoreanFscDataSets.Stocks or
            KoreanFscDataSets.PreemptiveRightSecurities or
            KoreanFscDataSets.PreemptiveRightCertificates => true,
            KoreanFscDataSets.IncomeSecurities => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static string ToApiCode(this KoreanFscMarkets market)
        => market switch
        {
            KoreanFscMarkets.All => null,
            KoreanFscMarkets.Kospi => "KOSPI",
            KoreanFscMarkets.Kosdaq => "KOSDAQ",
            KoreanFscMarkets.Konex => "KONEX",
            _ => throw new ArgumentOutOfRangeException(
                nameof(market), market, null),
        };

    public static SecurityMessage ToSecurityMessage(
        this KoreanFscPriceRow row,
        long originalTransactionId,
        KoreanFscDataSets dataSet)
    {
        var underlying = row.UnderlyingCode.IsEmpty()
            ? default
            : row.UnderlyingCode.ToKoreanFscSecurityId();
        var isWarrant = dataSet is
            KoreanFscDataSets.PreemptiveRightSecurities or
            KoreanFscDataSets.PreemptiveRightCertificates;

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = row.ToSecurityId(),
            Name = row.ItemName.IsEmpty(row.ShortCode),
            ShortName = row.ItemName.IsEmpty(row.ShortCode),
            Class = row.MarketCategory
                .IsEmpty(dataSet.ToDisplayName()),
            SecurityType = dataSet.ToSecurityType(),
            Currency = CurrencyTypes.KRW,
            IssueSize = row.GetListedCount(),
            IssueDate = row.SubscriptionStartDate.ToFscDateOrNull(),
            ExpiryDate = row.SubscriptionEndDate.ToFscDateOrNull() ??
                row.DelistingDate.ToFscDateOrNull(),
            Strike = row.ExercisePrice.ToDecimal() ??
                row.NewShareIssuePrice.ToDecimal(),
            UnderlyingSecurityId = underlying,
            UnderlyingSecurityType = isWarrant &&
                !row.UnderlyingCode.IsEmpty()
                    ? SecurityTypes.Stock
                    : null,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static KoreanFscDailyRecord ToRecord(
        this KoreanFscPriceRow row,
        KoreanFscDataSets dataSet)
    {
        var close = row.ClosePrice.ToDecimal();
        var change = row.PreviousDayChange.ToDecimal();

        return new KoreanFscDailyRecord
        {
            Date = row.BaseDate.ToFscDate(),
            SecurityId = row.ToSecurityId(),
            Name = row.ItemName,
            Market = row.MarketCategory
                .IsEmpty(dataSet.ToDisplayName()),
            SecurityType = dataSet.ToSecurityType(),
            OpenPrice = row.OpenPrice.ToDecimal(),
            HighPrice = row.HighPrice.ToDecimal(),
            LowPrice = row.LowPrice.ToDecimal(),
            ClosePrice = close,
            PreviousClosePrice =
                close is not null && change is not null
                    ? close - change
                    : null,
            ChangePercent = row.ChangePercent.ToDecimal(),
            Volume = row.Volume.ToDecimal(),
            Turnover = row.Turnover.ToDecimal(),
            ListedCount = row.GetListedCount(),
            MarketCapitalization =
                row.MarketCapitalization.ToDecimal(),
        };
    }

    public static bool Matches(
        this KoreanFscPriceRow row,
        string value,
        string name)
        => (value.IsEmpty() ||
            row.ShortCode.ContainsIgnoreCase(value) ||
            row.Isin.ContainsIgnoreCase(value)) &&
            (name.IsEmpty() ||
            row.ItemName.ContainsIgnoreCase(name));

    public static SecurityId ToSecurityId(
        this KoreanFscPriceRow row)
        => row.ShortCode.ToKoreanFscSecurityId(row.Isin);

    public static SecurityId ToKoreanFscSecurityId(
        this string symbol,
        string isin = null)
        => new()
        {
            SecurityCode = symbol
                .ThrowIfEmpty(nameof(symbol))
                .Trim(),
            BoardCode = BoardCodes.Krx,
            Native = symbol.Trim(),
            Isin = isin,
        };

    public static string GetKoreanFscSymbol(
        this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();

    public static decimal? GetListedCount(
        this KoreanFscPriceRow row)
        => row.ListedStockCount.ToDecimal() ??
            row.ListedUnitCount.ToDecimal() ??
            row.ListedSecurityCount.ToDecimal() ??
            row.ListedCertificateCount.ToDecimal();

    public static DateTime ToFscDate(this string value)
    {
        if (!DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            throw new FormatException(
                $"Invalid Korean FSC date '{value}'.");
        }

        return date.ToKoreaUtc();
    }

    public static DateTime? ToFscDateOrNull(this string value)
        => value.IsEmpty()
            ? null
            : value.ToFscDate();

    public static DateTime ToKoreaDate(
        this DateTimeOffset value)
        => value.ToOffset(_koreaOffset).Date;

    public static DateTime ToKoreaDate(this DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? value.Date
            : new DateTimeOffset(value)
                .ToOffset(_koreaOffset)
                .Date;

    public static DateTime KoreaToday()
        => DateTimeOffset.UtcNow.ToOffset(_koreaOffset).Date;

    public static DateTime ToKoreaUtc(this DateTime date)
        => new DateTimeOffset(
            DateTime.SpecifyKind(
                date.Date, DateTimeKind.Unspecified),
            _koreaOffset).UtcDateTime;

    public static decimal? ToDecimal(this string value)
    {
        value = value?.Trim().Replace(",", string.Empty);
        if (value.IsEmpty() || value == "-")
            return null;

        return decimal.TryParse(
            value,
            NumberStyles.Number |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
    }
}

sealed class KoreanFscDailyRecord
{
    public DateTime Date { get; set; }
    public SecurityId SecurityId { get; set; }
    public string Name { get; set; }
    public string Market { get; set; }
    public SecurityTypes SecurityType { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? PreviousClosePrice { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? Volume { get; set; }
    public decimal? Turnover { get; set; }
    public decimal? ListedCount { get; set; }
    public decimal? MarketCapitalization { get; set; }

    public bool HasOhlc =>
        OpenPrice is not null &&
        HighPrice is not null &&
        LowPrice is not null &&
        ClosePrice is not null;
}
