namespace StockSharp.LBank.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("avg_price")]
	public double? AvgPrice { get; set; }

	[JsonProperty("amount")]
	public double Volume { get; set; }

	[JsonProperty("deal_amount")]
	public double DealVolume { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedTimestamp { get; set; }

	[JsonProperty("customer_id")]
	public string CustomerId { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }
}

class SocketOrder
{
	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("orderStatus")]
	public int Status { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("role")]
	public double Role { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("uuid")]
	public string Id { get; set; }

	[JsonProperty("txUuid")]
	public string TxUuid { get; set; }

	[JsonProperty("volumePrice")]
	public double VolumePrice { get; set; }
}