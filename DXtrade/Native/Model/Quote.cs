namespace StockSharp.DXtrade.Native.Model;

class Quote
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }

	[JsonProperty("time")]
	public DateTime Time { get; set; }
}
