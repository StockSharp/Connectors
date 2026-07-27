namespace StockSharp.B3Up2Data;

static class B3Up2DataExtensions
{
    public const string EquitiesBoard = BoardCodes.Bovespa;
    public const string IndexBoard = "B3INDEX";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromDays(1),
    ];

    public static string ToExtension(
        this B3Up2DataFileFormats format)
        => format switch
        {
            B3Up2DataFileFormats.Csv => ".csv",
            B3Up2DataFileFormats.Json => ".json",
            B3Up2DataFileFormats.Xml => ".xml",
            B3Up2DataFileFormats.Txt => ".txt",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format), format, null),
        };

    public static B3DatasetDescriptor ToDescriptor(
        this B3Up2DataDataKinds kind)
        => kind switch
        {
            B3Up2DataDataKinds.SecurityMaster => new(
                "Equities/SecurityList",
                "Equities_EquityInstrumentFileV2_"),
            B3Up2DataDataKinds.EquitiesEod => new(
                "Equities/TradeInformation",
                "Equities_EODPriceFile_"),
            B3Up2DataDataKinds.EquitiesTrades => new(
                "Equities/TradeInformation",
                "Equities_TradeInformationFile_"),
            B3Up2DataDataKinds.EtfTrades => new(
                "Equities/ETFTrade",
                "Equities_ETFTradeFile_"),
            B3Up2DataDataKinds.IndexEod => new(
                "Index/TradeInformation",
                "Index_TradeInformationIndexFile_"),
            B3Up2DataDataKinds.IndexIntraday => new(
                "Index/IntradayInformation",
                "Index_IndexMarketDataFile_"),
            B3Up2DataDataKinds.IndexComposition => new(
                "Index/PortfolioComposition",
                "Index_PortfolioCompositionFile_"),
            B3Up2DataDataKinds.CorporateActions => new(
                "Corporate_Action/CorporateAction",
                "Corporate_Action_CorporateActionFileV2_"),
            B3Up2DataDataKinds.CorporateActionLifeCycle => new(
                "Corporate_Action/LifeCycle",
                "Corporate_Action_CorporateActionLifeCycleFileV2_"),
            B3Up2DataDataKinds.CorporateActionSchedule => new(
                "Corporate_Action/Schedule",
                "Corporate_Action_CorporateActionSchedule"),
            B3Up2DataDataKinds.CorporateActionIssuers => new(
                "Corporate_Action/Issuer",
                "Corporate_Action_CorporateActionIssuer_"),
            B3Up2DataDataKinds.BlobCatalog =>
                throw new InvalidOperationException(
                    "Blob catalog uses the configured arbitrary prefix."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    public static string BuildPrefix(
        this B3DatasetDescriptor descriptor,
        DateTime date)
        => $"{date:yyyyMMdd}/{descriptor.Directory}/" +
            descriptor.FilePrefix;

    public static string NormalizeBlobPrefix(string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            return null;
        value = value.TrimStart('/');
        ValidateBlobPath(value, true);
        return value;
    }

    public static string ValidateBlobName(string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            throw new ArgumentNullException(nameof(value));
        ValidateBlobPath(value, false);
        return value;
    }

    private static void ValidateBlobPath(
        string value,
        bool allowTrailingSlash)
    {
        if (value.Length > 2048 ||
            value.StartsWith('/') ||
            (!allowTrailingSlash && value.EndsWith('/')) ||
            value.Contains('\\') ||
            value.Contains('?') ||
            value.Contains('#') ||
            value.Any(char.IsControl) ||
            value.Split('/').Any(segment =>
                segment is "." or ".."))
        {
            throw new InvalidOperationException(
                "B3 UP2DATA blob path is invalid.");
        }
    }

    public static string GetTicker(this SecurityId securityId)
    {
        var ticker = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode);
        ticker = ticker?.Trim().ToUpperInvariant();
        if (ticker.IsEmpty() ||
            ticker.Length > 32 ||
            ticker.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '.' and not '-'))
        {
            throw new InvalidOperationException(
                "B3 UP2DATA security identifier is invalid.");
        }
        return ticker;
    }

    public static string GetOptionalTicker(
        this SecurityId securityId)
    {
        var ticker = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode);
        return ticker.IsEmpty()
            ? null
            : new SecurityId
            {
                SecurityCode = ticker,
            }.GetTicker();
    }

    public static bool IsIndex(this SecurityId securityId)
        => securityId.BoardCode.EqualsIgnoreCase(IndexBoard) ||
            (securityId.BoardCode.IsEmpty() &&
                !securityId.GetTicker().Any(char.IsDigit));

    public static SecurityId Normalize(
        this SecurityId securityId,
        string ticker,
        bool index)
        => new()
        {
            SecurityCode = ticker,
            BoardCode = index
                ? IndexBoard
                : EquitiesBoard,
            Native = ticker,
            Isin = securityId.Isin,
        };

    public static SecurityTypes ToSecurityType(
        this B3CsvRow row)
    {
        var category = row.Get("SctyCtgyNm");
        var description = row.Get("Desc");
        if (category?.Contains(
            "ETF",
            StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains(
                "ETF",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return SecurityTypes.Etf;
        }
        if (category?.Contains(
                "FUND",
                StringComparison.OrdinalIgnoreCase) == true ||
            category?.Contains(
                "FII",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return SecurityTypes.Fund;
        }
        return SecurityTypes.Stock;
    }

    public static SecurityMessage ToSecurityMessage(
        this B3CsvRow row,
        long originalTransactionId)
    {
        var ticker = new SecurityId
        {
            SecurityCode = row.Get("TckrSymb"),
        }.GetTicker();
        var lot = row.GetDecimal("AllcnRndLot");
        var multiplier = row.GetDecimal("PricFctr");
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = EquitiesBoard,
                Native = ticker,
                Isin = row.Get("ISIN"),
            },
            Name = row.Get("CrpnNm")
                .IsEmpty(row.Get("Desc"))
                .IsEmpty(ticker),
            ShortName = row.Get("Desc").IsEmpty(ticker),
            Class = row.Get("SctyCtgyNm")
                .IsEmpty(row.Get("SgmtNm")),
            SecurityType = row.ToSecurityType(),
            CfiCode = row.Get("CFICd"),
            Currency = row.Get("TradgCcy")
                ?.Equals(
                    "BRL",
                    StringComparison.OrdinalIgnoreCase) == true
                        ? CurrencyTypes.BRL
                        : null,
            VolumeStep = 1,
            Multiplier = lot is > 0
                ? lot
                : multiplier is > 0
                    ? multiplier
                    : 1,
        };
    }

    public static SecurityMessage ToIndexSecurityMessage(
        this B3CsvRow row,
        long originalTransactionId)
    {
        var ticker = new SecurityId
        {
            SecurityCode = row.Get("TckrSymb"),
        }.GetTicker();
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = IndexBoard,
                Native = ticker,
            },
            Name = row.Get("AsstDesc").IsEmpty(ticker),
            ShortName = ticker,
            Class = "B3 Index",
            SecurityType = SecurityTypes.Index,
            Currency = CurrencyTypes.BRL,
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static DateTime ToUtcDate(this DateTime value)
        => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    public static string DecodeText(
        this B3DownloadedBlob blob)
    {
        if (blob?.Content is null)
            throw new ArgumentNullException(nameof(blob));
        if (blob.Content.Length >= 3 &&
            blob.Content[0] == 0xEF &&
            blob.Content[1] == 0xBB &&
            blob.Content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(
                blob.Content, 3, blob.Content.Length - 3);
        }
        try
        {
            return new UTF8Encoding(false, true)
                .GetString(blob.Content);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(blob.Content);
        }
    }

    public static int GetRevision(
        this B3BlobItem blob,
        DateTime date)
    {
        var extension = Path.GetExtension(blob.Name);
        var stem = Path.GetFileNameWithoutExtension(blob.Name);
        var marker = $"_{date:yyyyMMdd}_";
        var index = stem.LastIndexOf(
            marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 &&
            int.TryParse(
                stem[(index + marker.Length)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision)
                    ? revision
                    : 0;
    }

    public static string GetLogicalName(
        this B3BlobItem blob,
        DateTime date)
    {
        var file = Path.GetFileNameWithoutExtension(blob.Name);
        var marker = $"_{date:yyyyMMdd}_";
        var index = file.LastIndexOf(
            marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? file : file[..index];
    }
}
