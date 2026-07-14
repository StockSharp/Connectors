namespace StockSharp.Poloniex.Native.Model;

class OwnTrade : Order
{
	[JsonProperty("tradeID")]
	public long TradeId { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketOwnTrade
{
	public string EventType { get; set; }

	public long TradeId { get; set; }

	public double Rate { get; set; }

	public double Amount { get; set; }

	public double FeeMultiplier { get; set; }

	public int FundingType { get; set; }

	public long OrderNumber { get; set; }

	public double FeeTotal { get; set; }

	public DateTime Date { get; set; }

	public long ClientOrderId { get; set; }

	public double TradeTotal { get; set; }
}