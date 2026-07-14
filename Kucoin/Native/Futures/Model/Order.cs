namespace StockSharp.Kucoin.Native.Futures.Model;

class Order
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("opType")]
	public string OpType { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("funds")]
	public double? Funds { get; set; }

	[JsonProperty("dealFunds")]
	public double? DealFunds { get; set; }

	[JsonProperty("dealSize")]
	public double? DealSize { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("stp")]
	public string Stp { get; set; }

	[JsonProperty("stop")]
	public string Stop { get; set; }

	[JsonProperty("stopTriggered")]
	public bool? StopTriggered { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("postOnly")]
	public bool? PostOnly { get; set; }

	[JsonProperty("hidden")]
	public bool? Hidden { get; set; }

	[JsonProperty("iceberg")]
	public bool? Iceberg { get; set; }

	[JsonProperty("visibleSize")]
	public double? VisibleSize { get; set; }

	[JsonProperty("cancelAfter")]
	public long? CancelAfter { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOid { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }

	[JsonProperty("tags")]
	public string Tags { get; set; }

	[JsonProperty("isActive")]
	public bool? IsActive { get; set; }

	[JsonProperty("cancelExist")]
	public bool? CancelExist { get; set; }

	[JsonProperty("createdAt")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdatedAt { get; set; }
}

class SocketOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("feeType")]
	public string FeeType { get; set; }

	[JsonProperty("orderTime")]
	[JsonConverter(typeof(JsonDateTimeMcsConverter))]
	public DateTime OrderTime { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("filledSize")]
	public double? FilledSize { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("matchPrice")]
	public double? MatchPrice { get; set; }

	[JsonProperty("matchSize")]
	public double? MatchSize { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOid { get; set; }

	[JsonProperty("remainSize")]
	public double? RemainSize { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("canceledSize")]
	public double? CanceledSize { get; set; }

	[JsonProperty("canceledFunds")]
	public double? CanceledFunds { get; set; }

	[JsonProperty("originSize")]
	public double? OriginSize { get; set; }

	[JsonProperty("originFunds")]
	public double? OriginFunds { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMcsConverter))]
	public DateTime TimeStamp { get; set; }
}

class SocketStopOrder
{
	[JsonProperty("createdAt")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderPrice")]
	public double? OrderPrice { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("stop")]
	public string Stop { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeType")]
	public string TradeType { get; set; }

	[JsonProperty("triggerSuccess")]
	public bool TriggerSuccess { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMcsConverter))]
	public DateTime Ts { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}