namespace StockSharp.Bavest.Native;

sealed class BavestMeta
{
    [JsonProperty("page")]
    public int? Page { get; set; }

    [JsonProperty("pageSize")]
    public int? PageSize { get; set; }

    [JsonProperty("totalPages")]
    public int? TotalPages { get; set; }

    [JsonProperty("totalCount")]
    public long? TotalCount { get; set; }

    [JsonProperty("cursor")]
    public string Cursor { get; set; }

    [JsonProperty("nextCursor")]
    public string NextCursor { get; set; }
}

sealed class BavestSecuritiesResponse
{
    [JsonProperty("data")]
    public BavestSecurity[] Data { get; set; } = [];

    [JsonProperty("meta")]
    public BavestMeta Meta { get; set; }
}

sealed class BavestSecurity
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("companyName")]
    public string CompanyName { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("exchangeCode")]
    public string ExchangeCode { get; set; }

    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("isActivelyTrading")]
    public bool? IsActivelyTrading { get; set; }

    [JsonProperty("isEtf")]
    public bool? IsEtf { get; set; }

    [JsonProperty("isAdr")]
    public bool? IsAdr { get; set; }

    [JsonProperty("isFund")]
    public bool? IsFund { get; set; }
}

sealed class BavestQuote
{
    [JsonProperty("currentPrice")]
    public decimal? CurrentPrice { get; set; }

    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("previousClose")]
    public decimal? PreviousClose { get; set; }

    [JsonProperty("change")]
    public decimal? Change { get; set; }

    [JsonProperty("changePercent")]
    public decimal? ChangePercent { get; set; }

    [JsonProperty("timestamp")]
    public long? Timestamp { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class BavestCandlesData
{
    [JsonProperty("candles")]
    public BavestCandle[] Candles { get; set; } = [];

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class BavestCandle
{
    [JsonProperty("timestamp")]
    public long? Timestamp { get; set; }

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
}

sealed class BavestNewsResponse
{
    [JsonProperty("data")]
    public BavestArticle[] Data { get; set; } = [];

    [JsonProperty("meta")]
    public BavestMeta Meta { get; set; }
}

sealed class BavestArticle
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("publishedDate")]
    public string PublishedDate { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("site")]
    public string Site { get; set; }

    [JsonProperty("text")]
    public string Text { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }
}

readonly record struct BavestRawResponse(
    string Resource,
    string Payload);

sealed class BavestApiException :
    InvalidOperationException
{
    public BavestApiException(
        HttpStatusCode statusCode,
        string requestId,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestId { get; }
}
