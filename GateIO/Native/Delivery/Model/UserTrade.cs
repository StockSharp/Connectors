namespace StockSharp.GateIO.Native.Delivery.Model;

class UserTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("create_time_ms")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("point_fee")]
	public double? PointFee { get; set; }
}