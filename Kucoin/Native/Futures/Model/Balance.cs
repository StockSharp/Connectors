namespace StockSharp.Kucoin.Native.Futures.Model;

class BalanceRelationContext
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }
}

class Balance
{
	[JsonProperty("total")]
	public double? Total { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("availableChange")]
	public double? AvailableChange { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("hold")]
	public double? Hold { get; set; }

	[JsonProperty("holdChange")]
	public double? HoldChange { get; set; }

	[JsonProperty("relationEvent")]
	public string RelationEvent { get; set; }

	[JsonProperty("relationEventId")]
	public string RelationEventId { get; set; }

	[JsonProperty("relationContext")]
	public BalanceRelationContext RelationContext { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}