namespace StockSharp.Bitget.Native.Spot.Model;

class Order
{
	[JsonProperty("userId")]
	public string UserId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instId")]
	public string InstId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOid { get; set; }

	[JsonProperty("priceAvg")]
	public double? PriceAvg { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("notional")]
	public double? Notional { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("force")]
	public string Force { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }
}
