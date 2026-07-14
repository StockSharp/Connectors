namespace StockSharp.GateIO.Native.Spot.Model;

class UserTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("point_fee")]
	public double? PointFee { get; set; }

	[JsonProperty("gt_fee")]
	public double? GtFee { get; set; }
}