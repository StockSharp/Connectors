namespace StockSharp.TradeOgre.Native.Model;

class OrderBook
{
	[JsonProperty("buy")]
	public IDictionary<double, double> Bids { get; set; }

	[JsonProperty("sell")]
	public IDictionary<double, double> Asks { get; set; }
}