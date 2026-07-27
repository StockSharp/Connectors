namespace StockSharp.StockDataOrg.Native;

sealed class StockDataOrgResponse<T>
{
    [JsonProperty("meta")]
    public StockDataOrgMeta Meta { get; set; }

    [JsonProperty("data")]
    public T[] Data { get; set; } = [];

    [JsonProperty("error")]
    public StockDataOrgError Error { get; set; }
}

sealed class StockDataOrgMeta
{
    [JsonProperty("requested")]
    public int? Requested { get; set; }

    [JsonProperty("found")]
    public long? Found { get; set; }

    [JsonProperty("returned")]
    public int? Returned { get; set; }

    [JsonProperty("limit")]
    public int? Limit { get; set; }

    [JsonProperty("page")]
    public int? Page { get; set; }

    [JsonProperty("date_from")]
    public string DateFrom { get; set; }

    [JsonProperty("date_to")]
    public string DateTo { get; set; }

    [JsonProperty("max_period_days")]
    public int? MaxPeriodDays { get; set; }
}

sealed class StockDataOrgError
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class StockDataOrgEntity
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("exchange_long")]
    public string ExchangeLong { get; set; }

    [JsonProperty("mic_code")]
    public string MicCode { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }
}

sealed class StockDataOrgQuote
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("exchange_short")]
    public string ExchangeShort { get; set; }

    [JsonProperty("exchange_long")]
    public string ExchangeLong { get; set; }

    [JsonProperty("mic_code")]
    public string MicCode { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("day_high")]
    public decimal? DayHigh { get; set; }

    [JsonProperty("day_low")]
    public decimal? DayLow { get; set; }

    [JsonProperty("day_open")]
    public decimal? DayOpen { get; set; }

    [JsonProperty("52_week_high")]
    public decimal? YearHigh { get; set; }

    [JsonProperty("52_week_low")]
    public decimal? YearLow { get; set; }

    [JsonProperty("market_cap")]
    public decimal? MarketCap { get; set; }

    [JsonProperty("previous_close_price")]
    public decimal? PreviousClose { get; set; }

    [JsonProperty("previous_close_price_time")]
    public string PreviousCloseTime { get; set; }

    [JsonProperty("day_change")]
    public decimal? DayChange { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("is_extended_hours_price")]
    public bool? IsExtendedHoursPrice { get; set; }

    [JsonProperty("last_trade_time")]
    public string LastTradeTime { get; set; }
}

sealed class StockDataOrgBar
{
    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("data")]
    public StockDataOrgBarValue Data { get; set; }

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

    public StockDataOrgBarValue GetValue()
        => Data ?? new StockDataOrgBarValue
        {
            Open = Open,
            High = High,
            Low = Low,
            Close = Close,
            Volume = Volume,
        };
}

sealed class StockDataOrgBarValue
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

    [JsonProperty("is_extended_hours")]
    public bool? IsExtendedHours { get; set; }
}

sealed class StockDataOrgArticle
{
    [JsonProperty("uuid")]
    public string Uuid { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("snippet")]
    public string Snippet { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("image_url")]
    public string ImageUrl { get; set; }

    [JsonProperty("language")]
    public string Language { get; set; }

    [JsonProperty("published_at")]
    public string PublishedAt { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("entities")]
    public StockDataOrgArticleEntity[] Entities { get; set; } = [];
}

sealed class StockDataOrgArticleEntity
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("exchange_long")]
    public string ExchangeLong { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("match_score")]
    public decimal? MatchScore { get; set; }

    [JsonProperty("sentiment_score")]
    public decimal? SentimentScore { get; set; }
}

sealed class StockDataOrgApiException :
    InvalidOperationException
{
    public StockDataOrgApiException(
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
