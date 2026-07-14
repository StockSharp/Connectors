namespace StockSharp.Deribit.Native.Model;

class Announcement
{
	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("important")]
	public bool Important { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("body")]
	public string Body { get; set; }
}