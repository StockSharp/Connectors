namespace StockSharp.Marketaux.Native;

sealed class MarketauxMeta
{
    [JsonProperty("found")]
    public long? Found { get; set; }

    [JsonProperty("returned")]
    public int? Returned { get; set; }

    [JsonProperty("limit")]
    public int? Limit { get; set; }

    [JsonProperty("page")]
    public int? Page { get; set; }
}

sealed class MarketauxEntityResponse
{
    [JsonProperty("meta")]
    public MarketauxMeta Meta { get; set; }

    [JsonProperty("data")]
    public MarketauxEntity[] Data { get; set; } = [];
}

sealed class MarketauxEntity
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

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("match_score")]
    public decimal? MatchScore { get; set; }

    [JsonProperty("sentiment_score")]
    public decimal? SentimentScore { get; set; }
}

sealed class MarketauxNewsResponse
{
    [JsonProperty("meta")]
    public MarketauxMeta Meta { get; set; }

    [JsonProperty("data")]
    public MarketauxArticle[] Data { get; set; } = [];
}

sealed class MarketauxArticle
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

    [JsonProperty("relevance_score")]
    public decimal? RelevanceScore { get; set; }

    [JsonProperty("entities")]
    public MarketauxEntity[] Entities { get; set; } = [];
}

readonly record struct MarketauxRawResponse(
    string Resource,
    string Payload);

sealed class MarketauxApiException :
    InvalidOperationException
{
    public MarketauxApiException(
        HttpStatusCode statusCode,
        string apiCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiCode = apiCode;
    }

    public HttpStatusCode StatusCode { get; }

    public string ApiCode { get; }
}
