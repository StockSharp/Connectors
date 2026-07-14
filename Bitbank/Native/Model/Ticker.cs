namespace StockSharp.Bitbank.Native.Model;

class Ticker
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("sell")]
	public double? Sell { get; set; }

	[JsonProperty("buy")]
	public double? Buy { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("vol")]
	public double? Volume { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}