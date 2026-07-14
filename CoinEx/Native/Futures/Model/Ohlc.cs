namespace StockSharp.CoinEx.Native.Futures.Model;

class Ohlc
{
	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

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
}