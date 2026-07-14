namespace StockSharp.Gopax.Native.Model;

class OwnTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("baseAmount")]
	public double BaseAmount { get; set; }

	[JsonProperty("quoteAmount")]
	public double QuoteAmount { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("tradingPairName")]
	public string Symbol { get; set; }
}