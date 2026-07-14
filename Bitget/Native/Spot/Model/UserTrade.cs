namespace StockSharp.Bitget.Native.Spot.Model;

class UserTrade
{
	[JsonProperty("userId")]
	public string UserId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("priceAvg")]
	public double? PriceAvg { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("fees")]
	public double? Fees { get; set; }

	[JsonProperty("feeCcy")]
	public string FeeCurrency { get; set; }
}
