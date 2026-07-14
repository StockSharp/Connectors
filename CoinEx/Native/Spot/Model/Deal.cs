namespace StockSharp.CoinEx.Native.Spot.Model;

class Deal
{
	[JsonProperty("deal_id")]
	public long DealId { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("margin_market")]
	public string MarginMarket { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("fee_ccy")]
	public string FeeCcy { get; set; }
}