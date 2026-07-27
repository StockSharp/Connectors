namespace StockSharp.GuruFocus.Native;

class GuruFocusSecurity
{
    [JsonProperty("company")]
    public string Company { get; set; }

    [JsonProperty("company_id")]
    public string CompanyId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("stockid")]
    public string StockId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }
}

sealed class GuruFocusEtfSecurity : GuruFocusSecurity
{
    [JsonProperty("asset_class")]
    public string AssetClass { get; set; }

    [JsonProperty("style_box")]
    public string StyleBox { get; set; }
}

sealed class GuruFocusProfileGeneral : GuruFocusSecurity
{
    [JsonProperty("IPO_date")]
    public string IpoDate { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("class_descpt")]
    public string ClassDescription { get; set; }
}

sealed class GuruFocusPage<T>
{
    [JsonProperty("data")]
    public T[] Data { get; set; } = [];

    [JsonProperty("page")]
    public int? Page { get; set; }

    [JsonProperty("per_page")]
    public int? PerPage { get; set; }

    [JsonProperty("total")]
    public long? Total { get; set; }
}

sealed class GuruFocusProfile
{
    [JsonProperty("basic_information")]
    public GuruFocusSecurity BasicInformation { get; set; }

    [JsonProperty("general")]
    public GuruFocusProfileGeneral General { get; set; }

    [JsonProperty("price")]
    public GuruFocusSnapshot Price { get; set; }

    public GuruFocusSecurity Identity =>
        General ?? BasicInformation;
}

sealed class GuruFocusEtfData
{
    [JsonProperty("basic_information")]
    public GuruFocusSecurity BasicInformation { get; set; }

    [JsonProperty("key_statistics")]
    public GuruFocusSnapshot KeyStatistics { get; set; }
}

sealed class GuruFocusSnapshot
{
    [JsonProperty("display_timestamp")]
    public string DisplayTimestamp { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("volumn_day")]
    public decimal? IntradayVolume { get; set; }

    [JsonProperty("p_pct_change")]
    public decimal? PercentChange { get; set; }
}

sealed class GuruFocusPrice
{
    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("close")]
    public decimal? Close { get; set; }

    [JsonProperty("unadjusted_close")]
    public decimal? UnadjustedClose { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }
}

sealed class GuruFocusArticle
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("subject")]
    public string Subject { get; set; }

    [JsonProperty("subtitle")]
    public string Subtitle { get; set; }

    [JsonProperty("publish_time")]
    public string PublishTime { get; set; }

    [JsonProperty("body")]
    public string Body { get; set; }

    [JsonProperty("link")]
    public string Link { get; set; }

    [JsonProperty("stocks")]
    public string[] Stocks { get; set; } = [];
}

sealed class GuruFocusNewsPage
{
    [JsonProperty("articles")]
    public GuruFocusArticle[] Articles { get; set; } = [];

    [JsonProperty("basic_information")]
    public GuruFocusSecurity BasicInformation { get; set; }

    [JsonProperty("page")]
    public int? Page { get; set; }

    [JsonProperty("per_page")]
    public int? PerPage { get; set; }

    [JsonProperty("total")]
    public long? Total { get; set; }
}

sealed class GuruFocusHeadlinesPage
{
    [JsonProperty("data")]
    public GuruFocusArticle[] Data { get; set; } = [];

    [JsonProperty("page")]
    public int? Page { get; set; }

    [JsonProperty("per_page")]
    public int? PerPage { get; set; }

    [JsonProperty("total")]
    public long? Total { get; set; }
}

readonly record struct GuruFocusSnapshotResult(
    GuruFocusSecurity Identity,
    GuruFocusSnapshot Snapshot,
    SecurityTypes SecurityType);

readonly record struct GuruFocusRawResponse(
    string Resource,
    string Payload);

sealed class GuruFocusApiException :
    InvalidOperationException
{
    public GuruFocusApiException(
        HttpStatusCode? statusCode,
        string apiCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiCode = apiCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public string ApiCode { get; }
}
