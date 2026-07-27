namespace StockSharp.OpenDart.Native;

class OpenDartResponse
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class OpenDartListResponse<T> : OpenDartResponse
{
    [JsonProperty("list")]
    public T[] Items { get; set; }
}

sealed class OpenDartDisclosurePage : OpenDartResponse
{
    [JsonProperty("page_no")]
    public int PageNumber { get; set; }

    [JsonProperty("page_count")]
    public int PageSize { get; set; }

    [JsonProperty("total_count")]
    public int TotalCount { get; set; }

    [JsonProperty("total_page")]
    public int TotalPages { get; set; }

    [JsonProperty("list")]
    public OpenDartDisclosure[] Items { get; set; }
}

sealed class OpenDartCompanyCode
{
    public string CorporationCode { get; set; }
    public string CorporationName { get; set; }
    public string EnglishName { get; set; }
    public string StockCode { get; set; }
    public string ModifiedDate { get; set; }
}

sealed class OpenDartDisclosure
{
    [JsonProperty("corp_cls")]
    public string CorporationClass { get; set; }

    [JsonProperty("corp_name")]
    public string CorporationName { get; set; }

    [JsonProperty("corp_code")]
    public string CorporationCode { get; set; }

    [JsonProperty("stock_code")]
    public string StockCode { get; set; }

    [JsonProperty("report_nm")]
    public string ReportName { get; set; }

    [JsonProperty("rcept_no")]
    public string ReceiptNumber { get; set; }

    [JsonProperty("flr_nm")]
    public string FilerName { get; set; }

    [JsonProperty("rcept_dt")]
    public string ReceiptDate { get; set; }

    [JsonProperty("rm")]
    public string Note { get; set; }
}

sealed class OpenDartFinancialIndicator
{
    [JsonProperty("reprt_code")]
    public string ReportCode { get; set; }

    [JsonProperty("bsns_year")]
    public string BusinessYear { get; set; }

    [JsonProperty("corp_code")]
    public string CorporationCode { get; set; }

    [JsonProperty("stock_code")]
    public string StockCode { get; set; }

    [JsonProperty("stlm_dt")]
    public string SettlementDate { get; set; }

    [JsonProperty("idx_cl_code")]
    public string CategoryCode { get; set; }

    [JsonProperty("idx_cl_nm")]
    public string CategoryName { get; set; }

    [JsonProperty("idx_code")]
    public string IndicatorCode { get; set; }

    [JsonProperty("idx_nm")]
    public string IndicatorName { get; set; }

    [JsonProperty("idx_val")]
    public string IndicatorValue { get; set; }
}

readonly record struct OpenDartDisclosureQuery(
    string CorporationCode,
    DateTime From,
    DateTime To,
    string DisclosureType,
    string CorporationClass,
    bool FinalReportsOnly,
    int PageNumber,
    int PageSize);
