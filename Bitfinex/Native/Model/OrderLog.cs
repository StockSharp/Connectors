namespace StockSharp.Bitfinex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderLog
{
	public long Id { get; set; }

	public double Price { get; set; }

	public double Amount { get; set; }
}