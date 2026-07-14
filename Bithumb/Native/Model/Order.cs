namespace StockSharp.Bithumb.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("order_currency")]
	public string OrderCurrency { get; set; }

	[JsonProperty("order_date")]
	public long OrderDate { get; set; }

	[JsonProperty("payment_currency")]
	public string PaymentCurrency { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string OrderStatus { get; set; }

	[JsonProperty("units")]
	public decimal Units { get; set; }

	[JsonProperty("units_remaining")]
	public decimal? UnitsRemaining { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("misu_yn")]
	public string MisuYn { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("total")]
	public decimal? Total { get; set; }

	[JsonProperty("date_completed")]
	public long? DateCompleted { get; set; }
}