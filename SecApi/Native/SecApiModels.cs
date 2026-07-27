namespace StockSharp.SecApi.Native;

sealed class SecApiMapping
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("cik")]
    public string Cik { get; set; }

    [JsonProperty("cusip")]
    public string Cusip { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("isDelisted")]
    public bool? IsDelisted { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("sic")]
    public string Sic { get; set; }

    [JsonProperty("sicSector")]
    public string SicSector { get; set; }

    [JsonProperty("sicIndustry")]
    public string SicIndustry { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("location")]
    public string Location { get; set; }
}

sealed class SecApiTotal
{
    [JsonProperty("value")]
    public long? Value { get; set; }

    [JsonProperty("relation")]
    public string Relation { get; set; }
}

sealed class SecApiFilingResponse
{
    [JsonProperty("total")]
    public SecApiTotal Total { get; set; }

    [JsonProperty("filings")]
    public SecApiFiling[] Filings { get; set; } = [];
}

sealed class SecApiFiling
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("accessionNo")]
    public string AccessionNumber { get; set; }

    [JsonProperty("formType")]
    public string FormType { get; set; }

    [JsonProperty("filedAt")]
    public string FiledAt { get; set; }

    [JsonProperty("periodOfReport")]
    public string PeriodOfReport { get; set; }

    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("cik")]
    public string Cik { get; set; }

    [JsonProperty("companyName")]
    public string CompanyName { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("linkToTxt")]
    public string LinkToText { get; set; }

    [JsonProperty("linkToHtml")]
    public string LinkToHtml { get; set; }

    [JsonProperty("linkToXbrl")]
    public string LinkToXbrl { get; set; }

    [JsonProperty("linkToFilingDetails")]
    public string LinkToFilingDetails { get; set; }
}

readonly record struct SecApiRawResponse(
    string Resource,
    string Payload);

sealed class SecApiApiException :
    InvalidOperationException
{
    public SecApiApiException(
        HttpStatusCode? statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
