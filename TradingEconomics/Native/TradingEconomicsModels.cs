namespace StockSharp.TradingEconomics.Native;

sealed class TradingEconomicsMarket
{
    [JsonProperty("Symbol")]
    public string Symbol { get; set; }

    [JsonProperty("Ticker")]
    public string Ticker { get; set; }

    [JsonProperty("Name")]
    public string Name { get; set; }

    [JsonProperty("Country")]
    public string Country { get; set; }

    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("Type")]
    public string Type { get; set; }

    [JsonProperty("decimals")]
    public int? Decimals { get; set; }

    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("Last")]
    public decimal? Last { get; set; }

    [JsonProperty("Close")]
    public decimal? Close { get; set; }

    [JsonProperty("CloseDate")]
    public string CloseDate { get; set; }

    [JsonProperty("MarketCap")]
    public decimal? MarketCap { get; set; }

    [JsonProperty("DailyChange")]
    public decimal? DailyChange { get; set; }

    [JsonProperty("DailyPercentualChange")]
    public decimal? DailyPercentualChange { get; set; }

    [JsonProperty("day_high")]
    public decimal? DayHigh { get; set; }

    [JsonProperty("day_low")]
    public decimal? DayLow { get; set; }

    [JsonProperty("yesterday")]
    public decimal? Yesterday { get; set; }

    [JsonProperty("ISIN")]
    public string Isin { get; set; }

    [JsonProperty("unit")]
    public string Unit { get; set; }

    [JsonProperty("frequency")]
    public string Frequency { get; set; }

    [JsonProperty("LastUpdate")]
    public string LastUpdate { get; set; }
}

sealed class TradingEconomicsBar
{
    [JsonProperty("Symbol")]
    public string Symbol { get; set; }

    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("Open")]
    public decimal? Open { get; set; }

    [JsonProperty("High")]
    public decimal? High { get; set; }

    [JsonProperty("Low")]
    public decimal? Low { get; set; }

    [JsonProperty("Close")]
    public decimal? Close { get; set; }

    [JsonProperty("Volume")]
    public decimal? Volume { get; set; }
}

sealed class TradingEconomicsArticle
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("importance")]
    public int? Importance { get; set; }
}

readonly record struct TradingEconomicsRawResponse(
    string Resource,
    string Payload);

sealed class TradingEconomicsApiException :
    InvalidOperationException
{
    public TradingEconomicsApiException(
        HttpStatusCode? statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
