namespace StockSharp.Cex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketTrade
{
	public string Type { get; set; }

	public long Time { get; set; }

	public double Amount { get; set; }

	public double Price { get; set; }

	public long Id { get; set; }

	public Trade ToTrade()
	{
		return new Trade
		{
			Type = Type,
			Id = Id,
			Time = Time.FromUnix(false),
			Price = Price,
			Amount = Amount,
		};
	}
}