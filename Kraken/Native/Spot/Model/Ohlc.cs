namespace StockSharp.Kraken.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Ohlc
{
	public double StartTime { get; set; }

	public decimal Open { get; set; }

	public decimal High { get; set; }

	public decimal Low { get; set; }

	public decimal Close { get; set; }

	public decimal Vwap { get; set; }

	public decimal Volume { get; set; }

	public int Count { get; set; }
}