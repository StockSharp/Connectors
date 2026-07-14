namespace StockSharp.PolygonIO.Native.Model;

class News
{
	public class NewsPublisher
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("homepage_url")]
		public string HomepageUrl { get; set; }

		[JsonProperty("logo_url")]
		public string LogoUrl { get; set; }

		[JsonProperty("favicon_url")]
		public string FaviconUrl { get; set; }
	}

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("publisher")]
	public NewsPublisher Publisher { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("author")]
	public string Author { get; set; }

	[JsonProperty("published_utc")]
	public DateTime PublishedUtc { get; set; }

	[JsonProperty("article_url")]
	public string ArticleUrl { get; set; }

	[JsonProperty("tickers")]
	public string[] Tickers { get; set; }

	[JsonProperty("image_url")]
	public string ImageUrl { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("keywords")]
	public string[] Keywords { get; set; }
}