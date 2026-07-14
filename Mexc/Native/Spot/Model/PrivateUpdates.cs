namespace StockSharp.Mexc.Native.Spot.Model;

class PrivateAccountUpdate
{
	public string Asset { get; set; }
	public double? Balance { get; set; }
	public double? Frozen { get; set; }
	public string ChangeType { get; set; }
	public DateTime Time { get; set; }
}

class PrivateDealUpdate
{
	public string Symbol { get; set; }
	public string TradeId { get; set; }
	public string OrderId { get; set; }
	public double? Price { get; set; }
	public double? Quantity { get; set; }
	public int? TradeType { get; set; }
	public double? FeeAmount { get; set; }
	public string FeeCurrency { get; set; }
	public DateTime Time { get; set; }
}

class PrivateOrderUpdate
{
	public string Symbol { get; set; }
	public string Id { get; set; }
	public double? Price { get; set; }
	public double? Quantity { get; set; }
	public double? AvgPrice { get; set; }
	public int? OrderType { get; set; }
	public int? TradeType { get; set; }
	public double? RemainQuantity { get; set; }
	public double? LastDealQuantity { get; set; }
	public double? CumulativeQuantity { get; set; }
	public double? CumulativeAmount { get; set; }
	public int? Status { get; set; }
	public DateTime CreateTime { get; set; }
}
