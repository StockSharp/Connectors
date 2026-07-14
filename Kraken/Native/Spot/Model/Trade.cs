namespace StockSharp.Kraken.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Trade
{
	public double Price { get; set; }

	public double Volume { get; set; }

	public double Time { get; set; }

	public string Side { get; set; }

	public string Type { get; set; }

	public string Misc { get; set; }
}