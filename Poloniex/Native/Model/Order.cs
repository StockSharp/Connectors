namespace StockSharp.Poloniex.Native.Model;

class Order
{
	[JsonProperty("orderNumber")]
	public long OrderNumber { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("rate")]
	public double Rate { get; set; }

	[JsonProperty("startingAmount")]
	public double StartingAmount { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("total")]
	public double Total { get; set; }

	[JsonProperty("clientOrderId")]
	public long ClientOrderId { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketOrderPending
{
	public string EventType { get; set; }

	public long OrderNumber { get; set; }

	public int PairId { get; set; }

	public double Rate { get; set; }

	public double Amount { get; set; }

	public int Type { get; set; }

	public long ClientOrderId { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketOrderLimit
{
	public string EventType { get; set; }

	public int PairId { get; set; }

	public long OrderNumber { get; set; }

	public int Type { get; set; }

	public double Rate { get; set; }

	public double Amount { get; set; }

	public DateTime Date { get; set; }

	public double OriginalAmount { get; set; }

	public long ClientOrderId { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketOrderUpdate
{
	public string EventType { get; set; }

	public long OrderNumber { get; set; }

	public double NewAmount { get; set; }

	public int Type { get; set; }

	public long ClientOrderId { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketOrderKill
{
	public string EventType { get; set; }

	public long OrderNumber { get; set; }

	public long ClientOrderId { get; set; }
}