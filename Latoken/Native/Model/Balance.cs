namespace StockSharp.LATOKEN.Native.Model;

class Balance
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public double Available { get; set; }

	[JsonProperty("blocked")]
	public double Blocked { get; set; }
}