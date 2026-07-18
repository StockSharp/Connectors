namespace StockSharp.Benzinga.Native.Model;

sealed class BenzingaNewsItem
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("original_id")]
	public long? OriginalId { get; set; }

	[JsonProperty("author")]
	public string Author { get; set; }

	[JsonProperty("created")]
	public string Created { get; set; }

	[JsonProperty("updated")]
	public string Updated { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("teaser")]
	public string Teaser { get; set; }

	[JsonProperty("body")]
	public string Body { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }

	[JsonProperty("image")]
	public BenzingaNewsImage[] Images { get; set; }

	[JsonProperty("channels")]
	public BenzingaNamedValue[] Channels { get; set; }

	[JsonProperty("stocks")]
	public BenzingaNewsStock[] Stocks { get; set; }

	[JsonProperty("tags")]
	public BenzingaNamedValue[] Tags { get; set; }
}

sealed class BenzingaNewsImage
{
	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }

	[JsonProperty("alt")]
	public string Alt { get; set; }
}

sealed class BenzingaNamedValue
{
	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class BenzingaNewsStock
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("cusip")]
	public string Cusip { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }
}

sealed class BenzingaNewsStreamEnvelope
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("api_version")]
	public string ApiVersion { get; set; }

	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("data")]
	public BenzingaNewsStreamData Data { get; set; }

	[JsonProperty("type")]
	public string ErrorType { get; set; }

	[JsonProperty("message")]
	public string ErrorMessage { get; set; }
}

sealed class BenzingaNewsStreamData
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("content")]
	public BenzingaNewsItem Content { get; set; }
}
