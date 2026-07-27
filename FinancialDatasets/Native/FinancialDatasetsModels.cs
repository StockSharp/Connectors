namespace StockSharp.FinancialDatasets.Native;

sealed class FinancialDatasetsTickersResponse
{
    [JsonProperty("resource")]
    public string Resource { get; set; }

    [JsonProperty("tickers")]
    public string[] Tickers { get; set; } = [];
}

sealed class FinancialDatasetsFactsResponse
{
    [JsonProperty("company_facts")]
    public FinancialDatasetsFacts Facts { get; set; }
}

sealed class FinancialDatasetsFacts
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("cik")]
    public string Cik { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("is_active")]
    public bool? IsActive { get; set; }

    [JsonProperty("listing_date")]
    public string ListingDate { get; set; }

    [JsonProperty("location")]
    public string Location { get; set; }

    [JsonProperty("market_cap")]
    public decimal? MarketCap { get; set; }

    [JsonProperty("number_of_employees")]
    public long? NumberOfEmployees { get; set; }

    [JsonProperty("sec_filings_url")]
    public string SecFilingsUrl { get; set; }

    [JsonProperty("sic_code")]
    public string SicCode { get; set; }

    [JsonProperty("sic_industry")]
    public string SicIndustry { get; set; }

    [JsonProperty("sic_sector")]
    public string SicSector { get; set; }

    [JsonProperty("website_url")]
    public string WebsiteUrl { get; set; }

    [JsonProperty("weighted_average_shares")]
    public decimal? WeightedAverageShares { get; set; }
}

sealed class FinancialDatasetsPricesResponse
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("prices")]
    public FinancialDatasetsPrice[] Prices { get; set; } = [];
}

sealed class FinancialDatasetsPrice
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("close")]
    public decimal? Close { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("time")]
    public string Time { get; set; }
}

sealed class FinancialDatasetsSnapshotResponse
{
    [JsonProperty("snapshot")]
    public FinancialDatasetsSnapshot Snapshot { get; set; }
}

sealed class FinancialDatasetsSnapshot
{
    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("day_change")]
    public decimal? DayChange { get; set; }

    [JsonProperty("day_change_percent")]
    public decimal? DayChangePercent { get; set; }

    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("time_milliseconds")]
    public long? TimeMilliseconds { get; set; }
}

sealed class FinancialDatasetsNewsResponse
{
    [JsonProperty("news")]
    public FinancialDatasetsArticle[] News { get; set; } = [];
}

sealed class FinancialDatasetsArticle
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }
}

readonly record struct FinancialDatasetsRawResponse(
    string Resource,
    string Payload);

sealed class FinancialDatasetsApiException :
    InvalidOperationException
{
    public FinancialDatasetsApiException(
        HttpStatusCode? statusCode,
        string apiError,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiError = apiError;
    }

    public HttpStatusCode? StatusCode { get; }

    public string ApiError { get; }
}
