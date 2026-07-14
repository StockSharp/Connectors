namespace StockSharp.Alor.Native.Model;

class Tick
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderno")]
	public long Orderno { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("qty")]
	public double Qty { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("time")]
	public DateTime Time { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("oi")]
	public double OI { get; set; }

	[JsonProperty("existing")]
	public bool Existing { get; set; }
}