namespace StockSharp.OpenDart;

static class OpenDartExtensions
{
    private static readonly TimeSpan _koreaOffset =
        TimeSpan.FromHours(9);

    public static string ToApiCode(
        this OpenDartReportTypes reportType)
        => reportType switch
        {
            OpenDartReportTypes.Annual => "11011",
            OpenDartReportTypes.FirstQuarter => "11013",
            OpenDartReportTypes.SemiAnnual => "11012",
            OpenDartReportTypes.ThirdQuarter => "11014",
            _ => throw new ArgumentOutOfRangeException(
                nameof(reportType), reportType, null),
        };

    public static string ToApiCode(
        this OpenDartDisclosureTypes disclosureType)
        => disclosureType switch
        {
            OpenDartDisclosureTypes.All => null,
            OpenDartDisclosureTypes.Periodic => "A",
            OpenDartDisclosureTypes.MajorIssues => "B",
            OpenDartDisclosureTypes.Issuance => "C",
            OpenDartDisclosureTypes.Equity => "D",
            OpenDartDisclosureTypes.Other => "E",
            OpenDartDisclosureTypes.ExternalAudits => "F",
            OpenDartDisclosureTypes.Funds => "G",
            OpenDartDisclosureTypes.AssetBackedSecuritization => "H",
            OpenDartDisclosureTypes.Exchange => "I",
            OpenDartDisclosureTypes.FairTradeCommission => "J",
            _ => throw new ArgumentOutOfRangeException(
                nameof(disclosureType), disclosureType, null),
        };

    public static string ToApiCode(
        this OpenDartCorporationClasses corporationClass)
        => corporationClass switch
        {
            OpenDartCorporationClasses.All => null,
            OpenDartCorporationClasses.Kospi => "Y",
            OpenDartCorporationClasses.Kosdaq => "K",
            OpenDartCorporationClasses.Konex => "N",
            OpenDartCorporationClasses.Other => "E",
            _ => throw new ArgumentOutOfRangeException(
                nameof(corporationClass), corporationClass, null),
        };

    public static string ToClassName(this string value)
        => value switch
        {
            "Y" => "KOSPI",
            "K" => "KOSDAQ",
            "N" => "KONEX",
            "E" => "Other",
            _ => value,
        };

    public static bool Matches(
        this OpenDartCompanyCode company,
        string value)
        => value.IsEmpty() ||
            company.CorporationCode.ContainsIgnoreCase(value) ||
            company.StockCode.ContainsIgnoreCase(value) ||
            company.CorporationName.ContainsIgnoreCase(value) ||
            company.EnglishName.ContainsIgnoreCase(value);

    public static SecurityMessage ToSecurityMessage(
        this OpenDartCompanyCode company,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = company.ToSecurityId(),
            Name = company.EnglishName
                .IsEmpty(company.CorporationName)
                .IsEmpty(company.StockCode),
            ShortName = company.CorporationName
                .IsEmpty(company.EnglishName)
                .IsEmpty(company.StockCode),
            Class = "Open DART",
            SecurityType = SecurityTypes.Stock,
            Currency = CurrencyTypes.KRW,
            VolumeStep = 1,
            Multiplier = 1,
        };

    public static SecurityId ToSecurityId(
        this OpenDartCompanyCode company)
        => company.StockCode.ToOpenDartSecurityId(
            company.CorporationCode);

    public static SecurityId ToOpenDartSecurityId(
        this string stockCode,
        string corporationCode)
        => new()
        {
            SecurityCode = stockCode
                .ThrowIfEmpty(nameof(stockCode))
                .Trim(),
            BoardCode = BoardCodes.Krx,
            Native = corporationCode
                .ThrowIfEmpty(nameof(corporationCode))
                .Trim(),
        };

    public static string GetCorporationCode(
        this SecurityId securityId)
    {
        var native = securityId.Native as string;
        return native.IsCorporationCode()
            ? native.Trim()
            : null;
    }

    public static bool IsCorporationCode(this string value)
        => value?.Length == 8 &&
            value.All(char.IsAsciiDigit);

    public static bool IsStockCode(this string value)
        => value?.Length == 6 &&
            value.All(char.IsAsciiDigit);

    public static DateTime ToKoreaUtcDate(this string value)
    {
        if (!value.TryToKoreaUtcDate(out var date))
        {
            throw new FormatException(
                $"Invalid Open DART date '{value}'.");
        }

        return date;
    }

    public static bool TryToKoreaUtcDate(
        this string value,
        out DateTime date)
    {
        if (DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            date = parsed.ToKoreaUtc();
            return true;
        }

        date = default;
        return false;
    }

    public static DateTime? ToSettlementDate(this string value)
    {
        if (value.IsEmpty())
            return null;

        if (!DateTime.TryParseExact(
            value,
            ["yyyy.MM.dd", "yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return null;
        }

        return date.ToKoreaUtc();
    }

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
        value = value?.Trim();
        if (value.IsEmpty() || value == "-")
            return null;

        var negative =
            value.StartsWith('(') && value.EndsWith(')');
        value = value
            .Trim('(', ')')
            .Replace(",", string.Empty)
            .Replace("%", string.Empty)
            .Trim();

        if (!decimal.TryParse(
            value,
            NumberStyles.Number |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return null;
        }

        return negative ? -result : result;
    }

    public static bool TryGetLevel1Field(
        this OpenDartFinancialIndicator indicator,
        out Level1Fields field)
    {
        var name = Normalize(indicator.IndicatorName);

        if (indicator.IndicatorCode == "M211000" ||
            name.Contains("operatingincomemargin") ||
            name.Contains("operatingprofitmargin"))
        {
            field = Level1Fields.OperatingMargin;
            return true;
        }
        if (name.Contains("grossprofitmargin"))
        {
            field = Level1Fields.GrossMargin;
            return true;
        }
        if (name.Contains("netprofitmargin") ||
            name.Contains("netincomemargin"))
        {
            field = Level1Fields.ProfitMargin;
            return true;
        }
        if (name == "roa" ||
            name.Contains("returnonassets"))
        {
            field = Level1Fields.ReturnOnAssets;
            return true;
        }
        if (name == "roe" ||
            name.Contains("returnonequity"))
        {
            field = Level1Fields.ReturnOnEquity;
            return true;
        }
        if (name == "roi" ||
            name.Contains("returnoninvestment"))
        {
            field = Level1Fields.ReturnOnInvestment;
            return true;
        }
        if (name.Contains("quickratio"))
        {
            field = Level1Fields.QuickRatio;
            return true;
        }
        if (name.Contains("currentratio"))
        {
            field = Level1Fields.CurrentRatio;
            return true;
        }
        if (name.Contains("longtermdebt") &&
            (name.Contains("equity") || name.Contains("ratio")))
        {
            field = Level1Fields.LongTermDebtEquity;
            return true;
        }
        if (name.Contains("debttoequity") ||
            name.Contains("debtratio") ||
            name.Contains("liabilitiestoequity"))
        {
            field = Level1Fields.TotalDebtEquity;
            return true;
        }

        field = default;
        return false;
    }

    public static string ToDisclosureUrl(
        this string receiptNumber,
        Uri disclosureAddress)
    {
        if (receiptNumber.IsEmpty())
            return null;

        var builder = new UriBuilder(
            disclosureAddress ??
            throw new ArgumentNullException(nameof(disclosureAddress)))
        {
            Query = "rcpNo=" +
                Uri.EscapeDataString(receiptNumber),
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string Normalize(string value)
        => value.IsEmpty()
            ? string.Empty
            : new string(
                value
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
}
