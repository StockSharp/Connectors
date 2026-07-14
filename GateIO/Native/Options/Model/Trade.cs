namespace StockSharp.GateIO.Native.Options.Model;

class Trade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("create_time_ms")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }
}