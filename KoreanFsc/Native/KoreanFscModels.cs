namespace StockSharp.KoreanFsc.Native;

sealed class KoreanFscPage
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public KoreanFscPriceRow[] Items { get; set; } = [];
}

sealed class KoreanFscPriceRow
{
    [JsonProperty("basDt")]
    public string BaseDate { get; set; }

    [JsonProperty("srtnCd")]
    public string ShortCode { get; set; }

    [JsonProperty("isinCd")]
    public string Isin { get; set; }

    [JsonProperty("itmsNm")]
    public string ItemName { get; set; }

    [JsonProperty("mrktCtg")]
    public string MarketCategory { get; set; }

    [JsonProperty("clpr")]
    public string ClosePrice { get; set; }

    [JsonProperty("vs")]
    public string PreviousDayChange { get; set; }

    [JsonProperty("fltRt")]
    public string ChangePercent { get; set; }

    [JsonProperty("mkp")]
    public string OpenPrice { get; set; }

    [JsonProperty("hipr")]
    public string HighPrice { get; set; }

    [JsonProperty("lopr")]
    public string LowPrice { get; set; }

    [JsonProperty("trqu")]
    public string Volume { get; set; }

    [JsonProperty("trPrc")]
    public string Turnover { get; set; }

    [JsonProperty("lstgStCnt")]
    public string ListedStockCount { get; set; }

    [JsonProperty("stLstgCnt")]
    public string ListedUnitCount { get; set; }

    [JsonProperty("lstgScrtCnt")]
    public string ListedSecurityCount { get; set; }

    [JsonProperty("lstgCtfCnt")]
    public string ListedCertificateCount { get; set; }

    [JsonProperty("mrktTotAmt")]
    public string MarketCapitalization { get; set; }

    [JsonProperty("exertPric")]
    public string ExercisePrice { get; set; }

    [JsonProperty("nstIssPrc")]
    public string NewShareIssuePrice { get; set; }

    [JsonProperty("subtPdSttgDt")]
    public string SubscriptionStartDate { get; set; }

    [JsonProperty("subtPdEdDt")]
    public string SubscriptionEndDate { get; set; }

    [JsonProperty("dltDt")]
    public string DelistingDate { get; set; }

    [JsonProperty("purRgtScrtItmsCd")]
    public string UnderlyingCode { get; set; }

    [JsonProperty("purRgtScrtItmsNm")]
    public string UnderlyingName { get; set; }

    [JsonProperty("purRgtScrtItmsClpr")]
    public string UnderlyingClosePrice { get; set; }
}

readonly record struct KoreanFscQuery(
    DateTime? ExactDate,
    DateTime? From,
    DateTime? ToExclusive,
    string Symbol,
    string Name,
    string Isin,
    string Market);
