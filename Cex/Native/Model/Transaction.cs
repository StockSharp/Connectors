namespace StockSharp.Cex.Native.Model;

class Transaction
{
	[JsonProperty("d")]
	public string D { get; set; }

	[JsonProperty("c")]
	public string C { get; set; }

	[JsonProperty("a")]
	public decimal? A { get; set; }

	[JsonProperty("ds")]
	public decimal? Ds { get; set; }

	[JsonProperty("cs")]
	public decimal? Cs { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbol2")]
	public string Symbol2 { get; set; }

	[JsonProperty("time")]
	//[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("order")]
	public long? OrderId { get; set; }

	[JsonProperty("buy")]
	public long? BuyOrderId { get; set; }

	[JsonProperty("sell")]
	public long? SellOrderId { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("fee_amount")]
	public decimal? FeeAmount { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }
}