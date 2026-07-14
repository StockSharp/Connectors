namespace StockSharp.CoinCap.Native.Model;

class Ohlc
{
	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("period")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Period { get; set; }
}