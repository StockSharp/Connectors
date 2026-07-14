namespace StockSharp.DXtrade.Native.Model;

class Candle
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("candleType")]
	public string CandleType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

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

	[JsonProperty("time")]
	public DateTime Time { get; set; }
}