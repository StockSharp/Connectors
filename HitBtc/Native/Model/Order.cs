namespace StockSharp.HitBtc.Native.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("quantity_cumulative")]
	public decimal CumQuantity { get; set; }

	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("report_type")]
	public string ReportType { get; set; }

	[JsonProperty("expire_time")]
	public DateTime? ExpireTime { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("trade_quantity")]
	public decimal? TradeQuantity { get; set; }

	[JsonProperty("trade_price")]
	public decimal? TradePrice { get; set; }

	[JsonProperty("trade_id")]
	public long? TradeId { get; set; }

	[JsonProperty("trade_fee")]
	public decimal? TradeFee { get; set; }
}
