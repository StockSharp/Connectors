namespace StockSharp.Bitget.Native.Futures.Model;

class Candle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("granularity")]
	public string Granularity { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("usdtVolume")]
	public double? UsdtVolume { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class RestCandle
{
	public DateTime Time { get; set; }
	public double? Open { get; set; }
	public double? High { get; set; }
	public double? Low { get; set; }
	public double? Close { get; set; }
	public double? BaseVolume { get; set; }
	public double? UsdtVolume { get; set; }
}