namespace StockSharp.Alpaca.Native.Model;

class News
{
	[JsonProperty("author")]
	public string Author { get; set; }

	[JsonProperty("content")]
	public string Content { get; set; }

	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("headline")]
	public string Headline { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("images")]
	public NewsImage[] Images { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("summary")]
	public string Summary { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("updated_at")]
	public DateTime UpdatedAt { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }
}

class NewsImage
{
	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }
}