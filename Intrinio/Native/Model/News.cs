namespace StockSharp.Intrinio.Native.Model;

sealed class IntrinioNewsResponse
{
	[JsonProperty("news")]
	public IntrinioNewsItem[] News { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioNewsItem
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("publication_date")]
	public DateTime? PublicationDate { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }

	[JsonProperty("summary")]
	public string Summary { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("securities")]
	public IntrinioSecuritySummary[] Securities { get; set; }

	[JsonProperty("article_sentiment")]
	public string ArticleSentiment { get; set; }

	[JsonProperty("language")]
	public string Language { get; set; }
}

sealed class IntrinioErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	public string GetMessage() => Message.IsEmpty(Error);
}
