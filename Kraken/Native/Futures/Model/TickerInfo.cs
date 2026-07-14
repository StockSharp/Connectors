namespace StockSharp.Kraken.Native.Futures.Model;

class TickerInfo
{
	[JsonProperty("a")]
	public double[] Ask { get; set; }

	[JsonProperty("b")]
	public double[] Bid { get; set; }

	[JsonProperty("c")]
	public double[] Closed { get; set; }

	[JsonProperty("v")]
	public double[] Volume { get; set; }

	[JsonProperty("p")]
	public double[] VWAP { get; set; }

	[JsonProperty("t")]
	public int[] Trades { get; set; }

	[JsonProperty("l")]
	public double[] Low { get; set; }

	[JsonProperty("h")]
	public double[] High { get; set; }

	[JsonProperty("o")]
	public double[] Open { get; set; }
}