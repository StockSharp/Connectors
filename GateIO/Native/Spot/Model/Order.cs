namespace StockSharp.GateIO.Native.Spot.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("update_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("finish_as")]
	public string FinishAs { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("left")]
	public double? Left { get; set; }

	[JsonProperty("filled_total")]
	public double? FilledTotal { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("point_fee")]
	public double? PointFee { get; set; }

	[JsonProperty("gt_fee")]
	public double? GtFee { get; set; }

	[JsonProperty("gt_discount")]
	public bool? GtDiscount { get; set; }

	[JsonProperty("rebated_fee")]
	public double? RebatedFee { get; set; }

	[JsonProperty("rebated_fee_currency")]
	public string RebatedFeeCurrency { get; set; }
}