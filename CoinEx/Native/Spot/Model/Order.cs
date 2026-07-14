namespace StockSharp.CoinEx.Native.Spot.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("market")]
	public string Symbol { get; set; }

	[JsonProperty("market_type")]
	public string MarketType { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("amount")]
	public double Volume { get; set; }

	[JsonProperty("unfilled_amount")]
	public double? Left { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("relation")]
	public int Relation { get; set; }

	[JsonProperty("base_fee")]
	public double? BaseFee { get; set; }

	[JsonProperty("deal_fee")]
	public double? DealFee { get; set; }

	[JsonProperty("last_fill_amount")]
	public double? LastFillAmount { get; set; }

	[JsonProperty("last_fill_price")]
	public double? LastFillPrice { get; set; }
}