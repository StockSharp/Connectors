namespace StockSharp.LATOKEN.Native.Model;

class Trade : BaseEntity
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	/// <summary>
	/// The buyer was the maker of the trade, which means the taker was the seller.
	/// </summary>
	[JsonProperty("makerBuyer")]
	public bool MakerBuyer { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("cost")]
	public double Cost { get; set; }
}