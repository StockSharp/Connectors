namespace StockSharp.Digifinex.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public string Id { get; set; }

	[JsonProperty("created_date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreatedDate { get; set; }

	[JsonProperty("finished_date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? FinishedDate { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("executed_amount")]
	public double? ExecutedAmount { get; set; }

	[JsonProperty("cash_amount")]
	public double? CashAmount { get; set; }

	[JsonProperty("avg_price")]
	public double? AvgPrice { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("kind")]
	public string Kind { get; set; }
}

//[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
//class OrderDetails : Order
//{
//	[JsonProperty("detail")]
//	public OwnTrade[] Trades { get; set; }
//}