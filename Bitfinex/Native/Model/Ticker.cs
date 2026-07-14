namespace StockSharp.Bitfinex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Ticker
{
	public double? Bid { get; set; }

	public double? BidSize { get; set; }

	public double? Ask { get; set; }

	public double? AskSize { get; set; }

	public double? DailyChange { get; set; }

	public double? DailyChangePerc { get; set; }

	public double? LastPrice { get; set; }

	public double? Volume { get; set; }

	public double? High { get; set; }

	public double? Low { get; set; }

	// decimal?, decimal?, decimal?, decimal?, decimal?, decimal?, decimal?, decimal?, decimal?, decimal?
}