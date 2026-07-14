namespace StockSharp.TradeOgre.Native.Model;

class Ticker
{
	[JsonProperty("initialprice")]
	public double? InitialPrice { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }
}