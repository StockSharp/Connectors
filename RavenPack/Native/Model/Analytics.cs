namespace StockSharp.RavenPack.Native.Model;

sealed class RavenPackRecordsResponse
{
	[JsonProperty("records")]
	public RavenPackAnalyticsRecord[] Records { get; set; }
}

sealed class RavenPackAnalyticsRecord
{
	[JsonProperty("timestamp_utc")]
	public string TimestampUtc { get; set; }

	[JsonProperty("rp_story_id")]
	public string StoryId { get; set; }

	[JsonProperty("rp_document_id")]
	public string DocumentId { get; set; }

	[JsonProperty("rp_entity_id")]
	public string EntityId { get; set; }

	[JsonProperty("entity_type")]
	public string EntityType { get; set; }

	[JsonProperty("entity_name")]
	public string EntityName { get; set; }

	[JsonProperty("country_code")]
	public string CountryCode { get; set; }

	[JsonProperty("relevance")]
	public decimal? Relevance { get; set; }

	[JsonProperty("event_relevance")]
	public decimal? EventRelevance { get; set; }

	[JsonProperty("event_sentiment_score")]
	public decimal? EventSentimentScore { get; set; }

	[JsonProperty("event_sentiment")]
	public decimal? EventSentiment { get; set; }

	[JsonProperty("entity_sentiment")]
	public decimal? EntitySentiment { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("group")]
	public string Group { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("sub_type")]
	public string SubType { get; set; }

	[JsonProperty("event_text")]
	public string EventText { get; set; }

	[JsonProperty("news_type")]
	public string NewsType { get; set; }

	[JsonProperty("source_name")]
	public string SourceName { get; set; }

	[JsonProperty("provider_id")]
	public string ProviderId { get; set; }

	[JsonProperty("provider_story_id")]
	public string ProviderStoryId { get; set; }

	[JsonProperty("headline")]
	public string Headline { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("listing")]
	public string Listing { get; set; }
}
