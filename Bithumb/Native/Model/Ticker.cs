namespace StockSharp.Bithumb.Native.Model;

class Ticker
{
	[JsonProperty("buy_price")]
	public double? Bid { get; set; }

	[JsonProperty("sell_price")]
	public double? Ask { get; set; }

	[JsonProperty("opening_price")]
	public double? OpeningPrice { get; set; }

	[JsonProperty("closing_price")]
	public double? ClosingPrice { get; set; }

	[JsonProperty("max_price")]
	public double? High { get; set; }

	[JsonProperty("min_price")]
	public double? Low { get; set; }

	[JsonProperty("units_traded")]
	public double? Volume { get; set; }

	[JsonProperty("average_price")]
	public double? VWAP { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Time { get; set; }
}