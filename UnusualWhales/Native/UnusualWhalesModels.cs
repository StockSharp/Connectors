namespace StockSharp.UnusualWhales.Native;

sealed class UnusualWhalesListingsData
{
    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("listings")]
    public UnusualWhalesListing[] Listings { get; set; } = [];
}

sealed class UnusualWhalesListing
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("asset_type")]
    public string AssetType { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("ipo_date")]
    public string IpoDate { get; set; }

    [JsonProperty("delisting_date")]
    public string DelistingDate { get; set; }
}

sealed class UnusualWhalesCompanyProfile
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("asset_type")]
    public string AssetType { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("cik")]
    public string Cik { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("market_cap")]
    public decimal? MarketCap { get; set; }

    [JsonProperty("shares_outstanding")]
    public decimal? SharesOutstanding { get; set; }
}

sealed class UnusualWhalesStockState
{
    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("close")]
    public decimal? Close { get; set; }

    [JsonProperty("prev_close")]
    public decimal? PreviousClose { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("total_volume")]
    public decimal? TotalVolume { get; set; }

    [JsonProperty("tape_time")]
    public string TapeTime { get; set; }

    [JsonProperty("market_time")]
    public string MarketTime { get; set; }
}

sealed class UnusualWhalesCandle
{
    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("close")]
    public decimal? Close { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("total_volume")]
    public decimal? TotalVolume { get; set; }

    [JsonProperty("start_time")]
    public string StartTime { get; set; }

    [JsonProperty("end_time")]
    public string EndTime { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("market_time")]
    public string MarketTime { get; set; }
}

sealed class UnusualWhalesHeadline
{
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; }

    [JsonProperty("headline")]
    public string Headline { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("sentiment")]
    public string Sentiment { get; set; }

    [JsonProperty("is_major")]
    public bool? IsMajor { get; set; }

    [JsonProperty("tags")]
    public string[] Tags { get; set; } = [];

    [JsonProperty("tickers")]
    public string[] Tickers { get; set; } = [];
}

readonly record struct UnusualWhalesRawResponse(
    string Resource,
    string Payload);

sealed class UnusualWhalesApiException :
    InvalidOperationException
{
    public UnusualWhalesApiException(
        HttpStatusCode statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
