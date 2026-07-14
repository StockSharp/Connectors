namespace StockSharp.HitBtc.Native.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("cumQuantity")]
	public double? CumQuantity { get; set; }

	[JsonProperty("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("reportType")]
	public string ReportType { get; set; }

	[JsonProperty("expireTime")]
	public DateTime? ExpireTime { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("tradeQuantity")]
	public double? TradeQuantity { get; set; }

	[JsonProperty("tradePrice")]
	public double? TradePrice { get; set; }

	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("tradeFee")]
	public double? TradeFee { get; set; }
}