namespace StockSharp.Huobi.Native.Futures.Model;

class Ohlc
{
	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }

	[JsonProperty("id")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime OpenTime { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("vol")]
	public double? Volume { get; set; }
}