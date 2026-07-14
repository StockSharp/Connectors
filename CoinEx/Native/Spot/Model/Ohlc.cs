namespace StockSharp.CoinEx.Native.Spot.Model;

class Ohlc
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("value")]
	public double? Value { get; set; }
}