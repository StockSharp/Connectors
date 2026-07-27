namespace StockSharp.KrxOpenApi;

static class KrxExtensions
{
    private static readonly TimeSpan _koreaOffset =
        TimeSpan.FromHours(9);

    public static string ToDailyPath(this KrxDataSets dataSet)
        => dataSet switch
        {
            KrxDataSets.KospiStocks => "sto/stk_bydd_trd",
            KrxDataSets.KosdaqStocks => "sto/ksq_bydd_trd",
            KrxDataSets.KonexStocks => "sto/knx_bydd_trd",
            KrxDataSets.Etf => "etp/etf_bydd_trd",
            KrxDataSets.Etn => "etp/etn_bydd_trd",
            KrxDataSets.KrxIndices => "idx/krx_dd_trd",
            KrxDataSets.KospiIndices => "idx/kospi_dd_trd",
            KrxDataSets.KosdaqIndices => "idx/kosdaq_dd_trd",
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static string ToReferencePath(this KrxDataSets dataSet)
        => dataSet switch
        {
            KrxDataSets.KospiStocks => "sto/stk_isu_base_info",
            KrxDataSets.KosdaqStocks => "sto/ksq_isu_base_info",
            KrxDataSets.KonexStocks => "sto/knx_isu_base_info",
            _ => null,
        };

    public static bool IsStock(this KrxDataSets dataSet)
        => dataSet is
            KrxDataSets.KospiStocks or
            KrxDataSets.KosdaqStocks or
            KrxDataSets.KonexStocks;

    public static bool IsIndex(this KrxDataSets dataSet)
        => dataSet is
            KrxDataSets.KrxIndices or
            KrxDataSets.KospiIndices or
            KrxDataSets.KosdaqIndices;

    public static SecurityTypes ToSecurityType(
        this KrxDataSets dataSet)
        => dataSet switch
        {
            KrxDataSets.KospiStocks or
            KrxDataSets.KosdaqStocks or
            KrxDataSets.KonexStocks => SecurityTypes.Stock,
            KrxDataSets.Etf => SecurityTypes.Etf,
            KrxDataSets.Etn => SecurityTypes.Bond,
            KrxDataSets.KrxIndices or
            KrxDataSets.KospiIndices or
            KrxDataSets.KosdaqIndices => SecurityTypes.Index,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static string ToMarketName(this KrxDataSets dataSet)
        => dataSet switch
        {
            KrxDataSets.KospiStocks => "KOSPI",
            KrxDataSets.KosdaqStocks => "KOSDAQ",
            KrxDataSets.KonexStocks => "KONEX",
            KrxDataSets.Etf => "ETF",
            KrxDataSets.Etn => "ETN",
            KrxDataSets.KrxIndices => "KRX",
            KrxDataSets.KospiIndices => "KOSPI",
            KrxDataSets.KosdaqIndices => "KOSDAQ",
            _ => throw new ArgumentOutOfRangeException(
                nameof(dataSet), dataSet, null),
        };

    public static KrxDailyRecord ToRecord(
        this KrxDailyRow row,
        KrxDataSets dataSet)
    {
        var isIndex = dataSet.IsIndex();
        var symbol = isIndex
            ? row.IndexName
            : row.IssueCode;
        var close = (isIndex
            ? row.IndexClosePrice
            : row.ClosePrice).ToDecimal();
        var previousChange = (isIndex
            ? row.IndexPreviousDayChange
            : row.PreviousDayChange).ToDecimal();

        return new KrxDailyRecord
        {
            Date = row.BaseDate.ToKrxDate(),
            Symbol = symbol?.Trim(),
            Name = (isIndex ? row.IndexName : row.IssueName)?.Trim(),
            Market = (isIndex
                ? row.IndexClass
                : row.MarketName)
                .IsEmpty(dataSet.ToMarketName()),
            Section = row.SectionName,
            SecurityType = dataSet.ToSecurityType(),
            OpenPrice = (isIndex
                ? row.IndexOpenPrice
                : row.OpenPrice).ToDecimal(),
            HighPrice = (isIndex
                ? row.IndexHighPrice
                : row.HighPrice).ToDecimal(),
            LowPrice = (isIndex
                ? row.IndexLowPrice
                : row.LowPrice).ToDecimal(),
            ClosePrice = close,
            PreviousClosePrice =
                close is not null && previousChange is not null
                    ? close - previousChange
                    : null,
            ChangePercent = row.ChangePercent.ToDecimal(),
            Volume = row.Volume.ToDecimal(),
            Turnover = row.Turnover.ToDecimal(),
            MarketCapitalization =
                row.MarketCapitalization.ToDecimal(),
            ListedShares = row.ListedShares.ToDecimal(),
            IndicativeValue = dataSet == KrxDataSets.Etf
                ? row.NetAssetValue.ToDecimal()
                : dataSet == KrxDataSets.Etn
                    ? row.IndicativeValue.ToDecimal()
                    : null,
            UnderlyingIndexName = row.UnderlyingIndexName,
        };
    }

    public static SecurityMessage ToSecurityMessage(
        this KrxSecurityInfoRow row,
        long originalTransactionId,
        KrxDataSets dataSet)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = row.IssueCode.ToKrxSecurityId(row.Isin),
            Name = row.EnglishName
                .IsEmpty(row.IssueName)
                .IsEmpty(row.AbbreviatedName)
                .IsEmpty(row.IssueCode),
            ShortName = row.AbbreviatedName
                .IsEmpty(row.IssueName)
                .IsEmpty(row.IssueCode),
            Class = string.Join(
                " / ",
                new[]
                {
                    row.MarketName,
                    row.SecurityGroupName,
                    row.SectionName,
                    row.StockCertificateTypeName,
                }.Where(value => !value.IsEmpty())),
            SecurityType = dataSet.ToSecurityType(),
            Currency = CurrencyTypes.KRW,
            IssueDate = row.ListingDate.ToKrxDateOrNull(),
            IssueSize = row.ListedShares.ToDecimal(),
            FaceValue = row.ParValue.ToDecimal(),
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static SecurityMessage ToSecurityMessage(
        this KrxDailyRecord row,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = row.Symbol.ToKrxSecurityId(),
            Name = row.Name.IsEmpty(row.Symbol),
            ShortName = row.Name.IsEmpty(row.Symbol),
            Class = row.Section.IsEmpty(row.Market),
            SecurityType = row.SecurityType,
            Currency = CurrencyTypes.KRW,
            IssueSize = row.ListedShares,
            VolumeStep = row.SecurityType == SecurityTypes.Index
                ? null
                : 1,
            Multiplier = row.SecurityType == SecurityTypes.Index
                ? null
                : 1,
        };

    public static bool Matches(
        this KrxSecurityInfoRow row,
        string value)
        => value.IsEmpty() ||
            row.IssueCode.ContainsIgnoreCase(value) ||
            row.Isin.ContainsIgnoreCase(value) ||
            row.IssueName.ContainsIgnoreCase(value) ||
            row.AbbreviatedName.ContainsIgnoreCase(value) ||
            row.EnglishName.ContainsIgnoreCase(value);

    public static bool Matches(
        this KrxDailyRecord row,
        string value)
        => value.IsEmpty() ||
            row.Symbol.ContainsIgnoreCase(value) ||
            row.Name.ContainsIgnoreCase(value);

    public static string GetKrxSymbol(this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();

    public static SecurityId ToKrxSecurityId(
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

    public static DateTime ToKrxDate(this string value)
    {
        if (!DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            throw new FormatException(
                $"Invalid KRX date '{value}'.");
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(date, DateTimeKind.Unspecified),
            _koreaOffset).UtcDateTime;
    }

    public static DateTime? ToKrxDateOrNull(this string value)
        => value.IsEmpty()
            ? null
            : value.ToKrxDate();

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

    public static decimal? ToDecimal(this string value)
    {
        value = value?.Trim().Replace(",", string.Empty);
        if (value.IsEmpty() || value == "-")
            return null;

        return decimal.TryParse(
            value,
            NumberStyles.Number |
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
    }
}

sealed class KrxDailyRecord
{
    public DateTime Date { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string Market { get; set; }
    public string Section { get; set; }
    public SecurityTypes SecurityType { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? PreviousClosePrice { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? Volume { get; set; }
    public decimal? Turnover { get; set; }
    public decimal? MarketCapitalization { get; set; }
    public decimal? ListedShares { get; set; }
    public decimal? IndicativeValue { get; set; }
    public string UnderlyingIndexName { get; set; }

    public bool HasOhlc =>
        OpenPrice is not null &&
        HighPrice is not null &&
        LowPrice is not null &&
        ClosePrice is not null;
}
