namespace StockSharp.Poloniex.Native.Model;

class MarketChartData
{
	[JsonProperty("date")]
	public double Date { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("weightedAverage")]
	public decimal WeightedAverage { get; set; }
}