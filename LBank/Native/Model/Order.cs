namespace StockSharp.LBank.Native.Model;

class Order
{
	[JsonProperty("orderId")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("avgPrice")]
	public decimal? AvgPrice { get; set; }

	[JsonProperty("origQty")]
	public decimal Volume { get; set; }

	[JsonProperty("executedQty")]
	public decimal DealVolume { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedTimestamp { get; set; }

	[JsonProperty("clientOrderId")]
	public string CustomerId { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }
}

class SocketOrder
{
	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("orderStatus")]
	public int Status { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("uuid")]
	public string Id { get; set; }

	[JsonProperty("txUuid")]
	public string TxUuid { get; set; }

	[JsonProperty("volumePrice")]
	public decimal VolumePrice { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("customerID")]
	public string CustomerId { get; set; }

	[JsonProperty("orderPrice")]
	public decimal? OrderPrice { get; set; }

	[JsonProperty("orderAmt")]
	public decimal OrderAmount { get; set; }

	[JsonProperty("avgPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("accAmt")]
	public decimal AccumulatedAmount { get; set; }

	[JsonProperty("remainAmt")]
	public decimal RemainingAmount { get; set; }
}
