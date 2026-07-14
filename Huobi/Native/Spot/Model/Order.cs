namespace StockSharp.Huobi.Native.Spot.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("client-order-id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("account-id")]
	public long AccountId { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("created-at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("field-amount")]
	public double? FieldAmount { get; set; }

	[JsonProperty("field-cash-amount")]
	public double? FieldCashAmount { get; set; }

	[JsonProperty("field-fees")]
	public double? FieldFees { get; set; }

	[JsonProperty("finished-at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? FinishedAt { get; set; }

	[JsonProperty("user-id")]
	public long UserId { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("canceled-at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? CanceledAt { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("batch")]
	public string Batch { get; set; }

	[JsonProperty("stop-price")]
	public double? StopPrice { get; set; }

	[JsonProperty("operator")]
	public string Operator { get; set; }
}

class SocketOrder
{
	[JsonProperty("orderSide")]
	public string Side { get; set; }

	[JsonProperty("lastActTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? LastActTime { get; set; }

	[JsonProperty("orderCreateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? CreateTime { get; set; }

	[JsonProperty("orderOrigTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? OrigTime { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("eventType")]
	public string EventType { get; set; }

	[JsonProperty("errCode")]
	public int ErrCode { get; set; }

	[JsonProperty("errMessage")]
	public string ErrMessage { get; set; }

	[JsonProperty("tradePrice")]
	public double? TradePrice { get; set; }

	[JsonProperty("tradeVolume")]
	public double? TradeVolume { get; set; }

	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("tradeTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TradeTime { get; set; }

	[JsonProperty("aggressor")]
	public bool? Aggressor { get; set; }

	[JsonProperty("remainAmt")]
	public double? RemainAmt { get; set; }

	[JsonProperty("orderId")]
	public long? OrderId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("orderSize")]
	public double? Size { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("accountId")]
	public long AccountId { get; set; }
}