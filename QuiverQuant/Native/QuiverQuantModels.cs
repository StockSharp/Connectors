namespace StockSharp.QuiverQuant.Native;

sealed class QuiverQuantCompany
{
    [JsonProperty("Name")]
    public string Name { get; set; }

    [JsonProperty("Ticker")]
    public string Ticker { get; set; }
}

sealed class QuiverQuantNews
{
    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("datetime")]
    public string DateTime { get; set; }

    [JsonProperty("headline")]
    public string Headline { get; set; }

    [JsonProperty("summary")]
    public string Summary { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("image")]
    public string Image { get; set; }
}

readonly record struct QuiverQuantRawResponse(
    string Resource,
    string Payload);

sealed class QuiverQuantApiException :
    InvalidOperationException
{
    public QuiverQuantApiException(
        HttpStatusCode? statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
