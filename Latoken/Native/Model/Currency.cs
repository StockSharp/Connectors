namespace StockSharp.LATOKEN.Native.Model;

class Currency
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("logo")]
	public string Logo { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("created")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Created { get; set; }
}