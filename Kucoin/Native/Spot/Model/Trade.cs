namespace StockSharp.Kucoin.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Trade
{
	public long Time { get; set; }

	public string Type { get; set; }

	public double Price { get; set; }

	public double Amount { get; set; }
	
	public double Value { get; set; }
}