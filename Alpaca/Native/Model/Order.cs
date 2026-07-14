namespace StockSharp.Alpaca.Native.Model;

class Order
{
	[JsonProperty("asset_class")]
	public string AssetClass { get; set; }

	[JsonProperty("asset_id")]
	public string AssetId { get; set; }

	[JsonProperty("cancel_requested_at")]
	public DateTime? CancelRequestedAt { get; set; }

	[JsonProperty("canceled_at")]
	public DateTime? CanceledAt { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("expired_at")]
	public DateTime? ExpiredAt { get; set; }

	[JsonProperty("extended_hours")]
	public bool? ExtendedHours { get; set; }

	[JsonProperty("failed_at")]
	public DateTime? FailedAt { get; set; }

	[JsonProperty("filled_at")]
	public DateTime? FilledAt { get; set; }

	[JsonProperty("filled_avg_price")]
	public double? FilledAvgPrice { get; set; }

	[JsonProperty("filled_qty")]
	public double? FilledQty { get; set; }

	[JsonProperty("hwm")]
	public object Hwm { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("legs")]
	public object Legs { get; set; }

	[JsonProperty("limit_price")]
	public double? LimitPrice { get; set; }

	[JsonProperty("notional")]
	public double? Notional { get; set; }

	[JsonProperty("order_class")]
	public string OrderClass { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("replaced_at")]
	public DateTime? ReplacedAt { get; set; }

	[JsonProperty("replaced_by")]
	public object ReplacedBy { get; set; }

	[JsonProperty("replaces")]
	public object Replaces { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stop_price")]
	public double? StopPrice { get; set; }

	[JsonProperty("submitted_at")]
	public DateTime? SubmittedAt { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("trail_percent")]
	public double? TrailPercent { get; set; }

	[JsonProperty("trail_price")]
	public double? TrailPrice { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("updated_at")]
	public DateTime? UpdatedAt { get; set; }
}

class OrderData
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("order")]
	public Order Order { get; set; }

	[JsonProperty("position_qty")]
	public double? PositionQty { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}