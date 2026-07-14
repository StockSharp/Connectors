namespace StockSharp.Bitfinex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Trade
{
	public long? Id { get; set; }

	public long Time { get; set; }

	public double Amount { get; set; }
	
	public double Price { get; set; }

	//public int Period { get; set; }
}