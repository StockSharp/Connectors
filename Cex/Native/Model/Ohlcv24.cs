namespace StockSharp.Cex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Ohlcv24
{
	public double Open { get; set; }

	public double High { get; set; }

	public double Low { get; set; }

	public double Close { get; set; }

	public double Volume { get; set; }
}