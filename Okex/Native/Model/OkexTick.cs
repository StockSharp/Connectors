namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
class OkexTick
{
	[JsonProperty("instId")]
	public string InstrumentId { get; set; }

	[JsonProperty("tradeId")]
	public long Id { get; set; }

	[JsonProperty("px")]
	public decimal Price { get; set; }

	[JsonProperty("sz")]
	public decimal Size { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}