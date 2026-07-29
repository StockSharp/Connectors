namespace StockSharp.Intrinio.Native.Model;

sealed class IntrinioNewsResponse
{
	[JsonPropertyName("news")]
	public IntrinioNewsItem[] News { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioNewsItem
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; }

	[JsonPropertyName("publication_date")]
	public DateTime? PublicationDate { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }

	[JsonPropertyName("summary")]
	public string Summary { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("securities")]
	public IntrinioSecuritySummary[] Securities { get; set; }

	[JsonPropertyName("article_sentiment")]
	public string ArticleSentiment { get; set; }

	[JsonPropertyName("language")]
	public string Language { get; set; }
}

sealed class IntrinioErrorResponse
{
	[JsonPropertyName("error")]
	public string Error { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }

	public string GetMessage() => Message.IsEmpty(Error);
}
