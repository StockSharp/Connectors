namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
class Trade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}