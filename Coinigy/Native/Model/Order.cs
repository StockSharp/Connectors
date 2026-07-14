namespace StockSharp.Coinigy.Native.Model;

class Order
{
	[JsonProperty("exchmktId")]
	public int ExchMktId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchCode")]
	public string ExchCode { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("limitPrice")]
	public double? LimitPrice { get; set; }

	[JsonProperty("orderOperator")]
	public string OrderOperator { get; set; }

	[JsonProperty("orderId")]
	public long Id { get; set; }

	[JsonProperty("orderTypeId")]
	public int? TypeId { get; set; }

	[JsonProperty("orderType")]
	public string Type { get; set; }

	[JsonProperty("priceTypeId")]
	public int? PriceTypeId { get; set; }

	[JsonProperty("priceType")]
	public string PriceType { get; set; }

	[JsonProperty("orderStatusId")]
	public int? StatusId { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("orderTime")]
	public DateTime Time { get; set; }

	[JsonProperty("foreignOrderId")]
	public string ForeignOrderId { get; set; }

	[JsonProperty("authNickname")]
	public string AuthNickname { get; set; }

	[JsonProperty("authId")]
	public int AuthId { get; set; }

	[JsonProperty("quantityRemaining")]
	public double? QuantityRemaining { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("orderCurrency")]
	public string OrderCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderPriceType")]
	public string OrderPriceType { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}